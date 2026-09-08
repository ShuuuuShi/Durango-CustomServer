using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Logic.Clusters;
using Durango.Network;
using Durango.Terrain;
using Durango.UI.Control;
using Durango.Utils;
using Durango.Utils.Extensions;
using Messages;
using Shared.Ability;
using Shared.Item;
using Shared.Region;
using Shared.Teleport;
using UnityEngine;
using Yaml;
using Yaml.Util;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/Player.cs (handler 39 ตัวของเซิร์ฟแท้)
// จุดที่ต้นฉบับดึงข้อมูลจากระบบฝั่ง client ใน process เดียวกัน (RecipeSystem/SocialSystem/
// GameManager.ClusterMode) เปลี่ยนเป็น BlueprintStore / DataStore.Emotions / Host.ClusterMode
// [5 ก.ย. 2026] แยกเป็น partial class — handler ของแต่ละระบบอยู่คนละไฟล์ (Player.<ระบบ>.cs)
// เหตุผล: ไฟล์นี้โตขึ้นเรื่อย ๆ ตามจำนวน handler และเวลาทำหลายระบบพร้อมกันจะแก้ชนกันตลอด
// ไฟล์นี้เก็บแกน (ต่อ/ปิด/เดิน/แตะ/chunk) ส่วนระบบใหม่ให้เพิ่มไฟล์ของตัวเองแล้วลงทะเบียนใน
// RegisterSystemHandlers() ด้านล่าง
public partial class Player
{
    private const string EpicCategory = "sunset";

    private readonly Connection _connection;

    private readonly World _world;

    private readonly PlayerContext _context;

    private int _centerX;

    private int _centerY;

    private readonly World.ChunkVisit[,] _chunkVisited;

    private readonly HashSet<string> _artifactSet = new();

    /// <summary>[5 ก.ย. 2026] หลอดสถานะที่เดินตามเวลาจริง — ดู Core/SurvivalState.cs</summary>
    private readonly SurvivalState _survival;

    /// <summary>เวลาล่าสุดที่ Move ทำให้ตำแหน่งเปลี่ยนจริง (ใช้เดาว่ายังเดินอยู่ไหม)</summary>
    private double _lastMovedAt;

    // สถานะที่ client เป็นคนสั่งเปิด/ปิดเอง (ToggleStatusEffect) → เวลาที่เริ่มติดสถานะนั้น
    // เก็บในหน่วยความจำต่อ connection พอ เพราะ data ของ away_from_keyboard ติดแท็ก
    // "clear_on_connect" ไว้ (data/assets/survival/status_effects.json) = ต่อใหม่ต้องหลุดอยู่แล้ว
    private readonly Dictionary<string, double> _toggledStatusEffects = new();

    public string EntityId { get; }

    public bool IsLocalPlayer { get; }

    public event Action Closed;

    public event Action ContextChanged;

    public Player(string entityId, Connection connection, World world, PlayerContext context, bool isLocalPlayer)
    {
        EntityId = entityId;
        _connection = connection;
        _world = world;
        _context = context;
        IsLocalPlayer = isLocalPlayer;
        if (_context.AppearPlayer.Move.Movements == null || !IsLocalPlayer)
        {
            _context.AppearPlayer.Move.Movements = new Movement[1];
            _context.AppearPlayer.Move.Movements[0].Path = new Location[1];
            _context.AppearPlayer.Move.Movements[0].Path[0].Position = GetEntryPosition();
        }
        _centerX = _world.NumChunksX / 2;
        _centerY = _world.NumChunksY / 2;
        _chunkVisited = new World.ChunkVisit[_world.NumChunksX, _world.NumChunksY];
        // ต้องสร้างก่อน Send(_context.AppearPlayer) ท้าย ctor เพราะ AppearPlayer พก Survival (182)
        // ไปด้วยเป็นลำดับที่ 11 ⇒ เส้นแนวโน้มต้องถูกสร้างใหม่ตามเวลาปัจจุบันก่อนถูกส่ง
        _survival = new SurvivalState(_context, live: true);
        // ⚠️ **ต้องเก็บ delegate ไว้ในฟิลด์ ห้ามใช้ lambda ลอย ๆ** — ไม่งั้นถอดออกไม่ได้
        // แล้ว World (ซึ่งอยู่ยาวกว่าผู้เล่น) จะถือ Player ที่หลุดไปแล้วไว้ตลอด
        // ⇒ Connection พร้อมบัฟเฟอร์ ~4 MB ไม่ถูกคืนสักไบต์ (ดู Detach)
        _onArtifactDisplayUpdated = msg => Send(msg);
        _onArtifactStateUpdated = msg => Send(msg);
        _onNaturalAdded = (chunk, bytes) => Send(new GardenDiff { Chunk = chunk, _GardenDiff = bytes });
        _onNaturalDestroyed = tile => Send(new DisappearEntityOnTile { Tile = tile });

        _world.ArtifactAppeared += World_ArtifactAppeared;
        _world.ArtifactDisappeared += World_ArtifactDisappeared;
        _world.PlayerAppeared += World_PlayerAppeared;
        _world.PlayerDisappeared += World_PlayerDisappeared;
        _world.ArtifactManager.ArtifactDisplayUpdated += _onArtifactDisplayUpdated;
        _world.ArtifactManager.ArtifactStateUpdated += _onArtifactStateUpdated;
        _world.NaturalAdded += _onNaturalAdded;
        _world.NaturalDestroyed += _onNaturalDestroyed;
        _connection.Recv(delegate(SetChunk msg, PacketHeader header)
        {
            SetCenterChunks(msg.Chunk.x, msg.Chunk.y);
        });
        _connection.Recv(delegate(Move msg, PacketHeader header)
        {
            // ⚠️ เดิมกระจายก้อนที่รับมา **ดิบ ๆ** ให้ทุกคนบนเกาะก่อนตรวจอะไรเลย
            // ⇒ ผู้เล่นคนเดียวยัด Movements ให้ใหญ่ ~400-500 KB หนึ่งก้อน แล้วทุก connection
            // แพ็กไม่ลงบัฟเฟอร์ 512 KB (Connection.cs:21 BufferCapacity) พร้อมกัน
            // ⇒ **ทุกคนบนเกาะหลุดพร้อมกันในเฟรมเดียว** และทำซ้ำได้ไม่จำกัด
            if (!IsSaneMove(msg))
            {
                Console.WriteLine($"[เดิน] ปฏิเสธก้อนผิดขนาดจาก {Short(EntityId)}");
                return;
            }
            _world.BroadCast(msg);
            HandleMoveMsg(msg.Movements);
        });
        // [6 ก.ย. 2026] กระโดดหลบ — ฝั่งเกม **ไม่รอคำตอบเลย** (client/PlayerController.cs:656
        // ยิง Send(default(Dashed)) แล้วทิ้งค่าที่คืนมา ไม่ต่อ .On/.Rest สักตัว)
        // ⇒ ห้ามตอบอะไรกลับ · หน้าที่เดียวของเซิร์ฟคือหักหลอดความอึด
        // ไม่หัก = กระโดดรัวได้ไม่จำกัด (หลอด stamina ฝั่งเกมเป็นแค่ภาพ เซิร์ฟเป็นเจ้าของค่าจริง)
        _connection.Recv(delegate(Dashed msg, PacketHeader header)
        {
            if (!_context.AppearPlayer.IsAlive) return;
            // constants.json → dash → stamina (ของจริง = 20 · หลอดเต็ม 100 ฟื้น 5/วินาที
            // ⇒ กระโดดรัวได้ 5 ครั้งแล้วต้องรอ ~4 วินาที ตรงกับเกมจริง)
            _survival.Add(SurvivalState.KeyStamina, -DashTuning.Stamina);
            FlushSurvival();   // ค่ากระโดด ⇒ ส่งเส้นใหม่ทันที ไม่รอรอบตรวจ
        });

        // [6 ก.ย. 2026] เริ่มออกเดิน — ฝั่งเกมยิงตอนขอบขาขึ้นของการเคลื่อนที่
        // (client/MoveMsgGenerator.cs:92) และ **ไม่รอคำตอบเช่นกัน**
        //
        // ⚠️ ห้ามตอบกลับเด็ดขาด และห้าม BroadCast ต่อ — ไม่มีใครรับ Depart ในเกมเลย
        // (grep ทั้ง client/ เจอแค่ไฟล์ struct กับจุดที่ยิง)
        //
        // [7 ก.ย. 2026] เดิมเคลียร์ rest ทันทีที่นี่ แต่เกมยิง Depart ตอนเปลี่ยนท่าด้วย
        // (รวมถึงตอนกดนั่งพัก) ⇒ ไอคอน rest เด้งแล้วหายในเสี้ยววิ
        // ⇒ อย่าเคลียร์ที่นี่ ให้ HandleMoveMsg เคลียร์เมื่อตำแหน่งเปลี่ยนจริงเท่านั้น
        _connection.Recv(delegate(Depart msg, PacketHeader header)
        {
        });

        _connection.Recv(delegate(Cheat msg, PacketHeader header)
        {
            // ⚠️ เดิมรับจากทุก connection ไม่มีด่านเลย ⇒ ผู้เล่นคนไหนก็เสกไอเทมทุกชิ้นทุกเลเวล
            // ไม่จำกัดจำนวน กระโดดเลเวล 60 วางสิ่งปลูกสร้างทับที่ใครก็ได้ = เศรษฐกิจเกมตายทันที
            // ที่มีคนเดียวลอง และเช็คไม่ได้ด้วยว่าใครทำ
            if (!IsAdmin)
            {
                Console.WriteLine($"[โกง] ปฏิเสธคำสั่งจาก {Short(EntityId)}: {msg._Cheat}");
                Send(new Abort { Text = "ไม่มีสิทธิ์ใช้คำสั่งนี้" }, header.Seq);
                return;
            }
            HandleCheatMsg(msg._Cheat, header.Seq);
        });
        // สถานะปุ่มสลับในแผงคำสั่งของแอดมิน — client/Durango.UI/CommandButtonGroup.cs:154,365
        //
        // ⚠️ **ต้องตอบ ReplyOf = 0** — ฝั่งเกมรับด้วย global On<CheatFlags> (บรรทัด 68)
        // ตัวที่ยิงไม่ได้ผูก .On() ไว้เลย ⇒ ตอบที่ seq จะไม่มีใครรับ
        //
        // ⚠️ ตอบตารางว่างไม่ได้: OnCheatFlags เขียน flags["ar"] ลงไปทันทีที่รับ
        // ⇒ ส่ง Flags = null มา ฝั่งเกมจะ NullReferenceException ตอนแตะ index
        _connection.Recv(delegate(GetCheatFlags msg, PacketHeader header)
        {
            // เซิร์ฟไม่มีสถานะโกงค้างไว้เลยสักตัว (คำสั่ง cheat เป็นแบบยิงแล้วจบ)
            // ⇒ ตารางว่างคือคำตอบที่ถูก ไม่ใช่การยอมแพ้ — ฝั่งเกมเติม "ar" ของมันเองต่อ
            Send(new CheatFlags { Flags = new Dictionary<string, bool>() });
        });

        _connection.Recv(delegate(Messages.Touch msg, PacketHeader header)
        {
            HandleTouchMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(DestructArtifact msg, PacketHeader header)
        {
            HandleDestructMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(RestOn msg, PacketHeader header)
        {
            Send(default(OK), header.Seq);
            // นั่งพัก = ความชันของ fatigue/life/health เปลี่ยน ⇒ ต้องส่งเส้นชุดใหม่ทันที
            // ไม่ใช่รอรอบตรวจ (ค่าจาก status_effects.json → "rest" ดู SurvivalTuning)
            // เลิกพักเองตอนขยับ ตามแท็ก "clear_on_move" ของสถานะนั้น — ดู HandleMoveMsg
            _survival.SetResting(true);
            // [7 ก.ย. 2026] ใส่ไอคอน rest ด้วย — Until=0 จนกว่าจะเดิน (ตรงแท็ก clear_on_move)
            ApplyTimedStatusEffect("rest", 1, durationOverride: 0);
            SendStatusEffects();
            FlushSurvival();
            OnContextChanged();
        });
        _connection.Recv(delegate(Wash msg, PacketHeader header)
        {
            Send(new Messages.Timer { Duration = 5f }, header.Seq);
            OnContextChanged();
        });
        _connection.Recv(delegate(PlantSeed msg, PacketHeader header)
        {
            HandlePlantSeedMsg(msg);
        });
        _connection.Recv(delegate(ChargeEffect msg, PacketHeader header)
        {
            HandleChargeEffectMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(Scribble msg, PacketHeader header)
        {
            HandleScribbleMsg(msg);
        });
        _connection.Recv(delegate(DumpItems msg, PacketHeader header)
        {
            HandleDumpItemsMsg(msg);
        });
        _connection.Recv(delegate(GetAddOns msg, PacketHeader header)
        {
            HandleGetAddOnsMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(PlaceAddOns msg, PacketHeader header)
        {
            HandlePlaceAddOnsMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(Messages.Display msg, PacketHeader header)
        {
            HandleChangeDecorationMsg(msg);
        });
        _connection.Recv(delegate(Equip msg, PacketHeader header)
        {
            HandleEquipMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(SearchProducts msg, PacketHeader header)
        {
            HandleSearchProductsMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(GetFavoriteProducts msg, PacketHeader header)
        {
            Send(default(Products), header.Seq);
        });
        _connection.Recv(delegate(BuyProduct msg, PacketHeader header)
        {
            HandleBuyProductMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(GetRecipes msg, PacketHeader header)
        {
            Send(default(Recipes), header.Seq);
        });
        // ── ระบบล่องเรือ (5 ก.ย. 2026) ──────────────────────────────────────────────
        // ผู้เล่นแตะท่าเรือ → เกมเปิดหน้า "เส้นทางเดินเรือ" แล้วยิง GetRoutes มาถามว่าไปไหนได้บ้าง
        // (client/Durango.UI/ExploreGroup.cs:233 Open → ExploreSystem.cs:371 RequestRoutes)
        // จากนั้นเกมขอรายละเอียดของแต่ละปลายทางต่อด้วย GetRegion (client/MapSystem.cs:642)
        _connection.Recv(delegate(GetRoutes msg, PacketHeader header)
        {
            HandleGetRoutesMsg(header.Seq);
        });
        _connection.Recv(delegate(GetRegion msg, PacketHeader header)
        {
            HandleGetRegionMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(GetArchipelago msg, PacketHeader header)
        {
            HandleGetArchipelagoMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(TravelByRegion msg, PacketHeader header)
        {
            HandleTravelMsg(msg.RegionId, header.Seq);
        });
        _connection.Recv(delegate(TravelByRegionInArchipelago msg, PacketHeader header)
        {
            HandleTravelMsg(msg.RegionId, header.Seq);
        });
        _connection.Recv(delegate(SailingBack msg, PacketHeader header)
        {
            // ล่องกลับ = กลับเกาะตั้งต้น (ยังไม่มีประวัติการเดินทาง จึงยังไม่รู้ว่า "เกาะก่อนหน้า" คือลูกไหน)
            HandleTravelMsg(null, header.Seq);
        });
        _connection.Recv(delegate(GetSailingBackCost msg, PacketHeader header)
        {
            Send(new SailingBackCost { Cost = 0L }, header.Seq);
        });
        // ⚠️ ขาดตัวนี้แล้วหน้าเลือกเส้นทางจะว่างเปล่า ทั้งที่ Routes ส่งไปครบแล้ว
        // client/Durango.UI/WorldRoutesViewer.cs:263-272 ซ้อน callback ไว้ 2 ชั้น:
        //   GetEstateLicenses → GetPersonalRegionInfo → RefreshRegionPoint()
        // RefreshRegionPoint คือตัววาดจุดเกาะที่กดได้ ⇒ ไม่ตอบตัวใดตัวหนึ่ง = ไม่มีปุ่มให้กด
        // ทั้งสองฟิลด์เป็น nullable — ยังไม่มีระบบที่ดิน จึงตอบว่างไปก่อน (เกมรับได้)
        _connection.Recv(delegate(GetPersonalRegionInfo msg, PacketHeader header)
        {
            Send(BuildPersonalRegionInfo(), header.Seq);
        });
        // ── สถานะตัวละคร ────────────────────────────────────────────────────────────
        // ⚠️ ตัวนี้กระทบระบบล่องเรือโดยตรง: client/StatisticsSystem.cs:33
        //   Level => Statistics.HasValue ? Statistics.Value.Level : -1
        // และ client/Durango.UI/WorldRoutesUnstableArea.cs:191 เทียบ
        //   StatisticsSystem.Level < Template.AvailableLevel  ⇒ เกาะกดไม่ได้
        // ไม่ตอบ = Level เป็น -1 = **ทุกเกาะกดไม่ได้ทั้งกระดาน** โดยไม่มี error ให้เห็น
        //
        // เซิร์ฟส่ง Statistics ให้ครั้งหนึ่งแล้วตอน Player ถูกสร้าง แต่ client ยิงถามซ้ำอีกรอบ
        // หลังพร้อม (StatisticsSystem.cs:101-105 ใน AddOnReady) — ของที่ส่งไปก่อนหน้าอาจถึง
        // ก่อน client subscribe (On<Statistics> ที่ :89) จึงต้องตอบตอนถูกถามด้วย
        _connection.Recv(delegate(GetStatistics msg, PacketHeader header)
        {
            SendStatistics();
        });
        _connection.Recv(delegate(GetTitles msg, PacketHeader header)
        {
            Send(new Titles { TitleIds = Array.Empty<string>() }, header.Seq);
        });
        _connection.Recv(delegate(GetStatusEffects msg, PacketHeader header)
        {
            _insideCheckedTile = new Point2(int.MinValue, int.MinValue);
            SyncWorldDrivenStatusEffects();
            SendStatusEffects(header.Seq);
        });
        // เปิด/ปิดสถานะที่ "client เป็นคนตัดสินใจเอง" — ตอนนี้เกมส่งมาตัวเดียวคือ away_from_keyboard
        // (client/SleepChecker.cs:169-187 Sleep()/WakeUp() ส่ง Toggle=true/false)
        // ⚠️ client ไม่ได้รอ reply ที่ seq นี้เลย (Send เฉย ๆ ไม่มี .On) — ตัวที่มันฟังคือ
        //    StatusEffects แบบ push (client/StatusEffectSystem.cs:29 → :111-115 เข้า SetStatusEffects
        //    ทันทีถ้า EntityId ตรงกับตัวเอง) ⇒ "ตอบ" ที่ถูกต้องคือ push StatusEffects ชุดใหม่ทั้งชุด
        //    ไม่ใช่ OK เพราะ client/Durango.Logic/StatusEffects.cs:36-72 แทนที่ list ทั้งก้อนทุกครั้ง
        _connection.Recv(delegate(ToggleStatusEffect msg, PacketHeader header)
        {
            if (string.IsNullOrEmpty(msg.Id))
            {
                return;
            }
            if (msg.Toggle)
            {
                _toggledStatusEffects.TryAdd(msg.Id, Times.UnixTimeNow());
            }
            else
            {
                _toggledStatusEffects.Remove(msg.Id);
            }
            SendStatusEffects();
        });
        // เพดาน exp ต้านทานรายวัน — client/StatisticsSystem.cs:113-115 ยิงถามตอนเข้าเกม (AddOnReady)
        // ⚠️ ต้องตอบตรง header.Seq: callback ที่มีตรรกะนัดถามซ้ำอยู่ใน .On() ของ seq นั้น
        //    (StatisticsSystem.cs:116-129) ถ้า push เฉย ๆ จะตกไป global handler ที่ :93 ซึ่งเซ็ตค่า
        //    ให้เหมือนกันแต่ไม่มีตัวนัดเวลารีเซ็ต — ตอบตรง seq ครอบคลุมทั้งสองทาง เพราะ
        //    client/Durango.Network/Connection.cs:883-887 fallback ไป global handler ให้เองอยู่แล้ว
        _connection.Recv(delegate(GetResistanceExpCaps msg, PacketHeader header)
        {
            Send(new ResistanceExpCaps { Caps = BuildResistanceExpCaps() }, header.Seq);
        });
        // รายการสกิล — client/Durango.Logic/SkillSystem.cs:102-105 ยิงถามตอนเข้าเกม
        // ⚠️ SkillSystem.OnReceiveSkillMsg เป็นตัวเดียวที่ปลดล็อก _isInitSkills (ผ่าน RaiseSkillEvent
        //    ที่ :271-278) ⇒ ไม่ตอบ = ระบบสกิลไม่เคย init เลย และ :229 วน foreach(msg.Categories)
        //    ตรง ๆ ไม่เช็ค null
        // ยังไม่มีระบบสกิลจริง จึงตอบชุดว่างครบทุกฟิลด์ (array/dict ว่าง ไม่ใช่ null)
        // client จะตั้งเลเวลสกิลทุกตัวเป็น 0 (:210-228) ซึ่งตรงกับสภาพจริงของเซิร์ฟตอนนี้
        _connection.Recv(delegate(GetSkills msg, PacketHeader header)
        {
            Send(new Skills
            {
                SkillList = Array.Empty<SkillBundle>(),
                SkillPoint = 0,
                Categories = new Dictionary<Shared.Skill.Category, SkillCategory>(),
                UntrainedCount = 0,
                AdvisedSkills = Array.Empty<Messages.Skill>(),
                AdvisedSkillCategories = new Dictionary<Shared.Skill.Category, int>()
            }, header.Seq);
        });
        // "วิชาที่กำลังเรียน" ของระบบไกด์ — client/Durango.Logic/LearningGuideSystem.cs:51-55
        // ส่งสองตัวนี้ติดกันตอนเข้าเกม
        // AdvisorTargetsReceived (:247-253) วน msg.Titles และเรียก msg.RemainingRewards.Contains()
        // ⇒ ต้องเป็น dict/array ว่าง ไม่ใช่ null
        _connection.Recv(delegate(GetAdvisorTargets msg, PacketHeader header)
        {
            Send(new AdvisorTargets
            {
                Titles = new Dictionary<string, float>(),
                RemainingRewards = Array.Empty<string>()
            }, header.Seq);
        });
        // TitleId = null คือ "ยังไม่ได้เลือกวิชา" — TargetTitleReceived (:270-273) ส่งต่อเข้า
        // StatisticsSystem.GetAdvice(null) ซึ่งคืน null ได้อย่างปลอดภัย (StatisticsSystem.cs:277-283)
        // และ TargetTitle.Pack รองรับ null ตรง ๆ (PackNull) จึงไม่ต้องแปลงเป็นสตริงว่าง
        _connection.Recv(delegate(GetTargetTitle msg, PacketHeader header)
        {
            Send(new TargetTitle { TitleId = null }, header.Seq);
        });
        // ระดับการบุกเบิก — client/ArchipelagoRouteExtension.cs:37
        //   IsPioneerGradeSatisfied = CurrentAccessLevel >= ArchipelagoRoute.UnstableFactor
        // ไม่ตอบ ⇒ CurrentAccessLevel = 0 ⇒ เกาะทุกลูกไม่ผ่านเงื่อนไข แสดงเป็นก้อนเปล่า (SetEmpty)
        // ยังไม่มีระบบบุกเบิก จึงเปิดสูงสุดไว้ก่อน ให้เดินทางได้ทุกเส้นทาง
        _connection.Recv(delegate(GetPioneerGradeInfo msg, PacketHeader header)
        {
            Send(new PioneerGradeInfo
            {
                EntityId = EntityId,
                Grade = 9,
                CurrentAccessLevel = 9,
                CurrentMaximumEstateSize = 30,
                DailyExchangedPoints = new Dictionary<float, float>()
            }, header.Seq);
        });
        // จุดสำคัญบนแผนที่ (GetPOICount / GetExploredPOIs / ExplorePOI) → Core/Player.Map.cs
        // ข้อมูลสิ่งที่ค้นพบบนเกาะ — client/Durango.UI/ArchipelagoDiscoveryInfos.cs:75-113
        // ⚠️ ไม่มี .On<Error> fallback ⇒ ไม่ตอบ = ไอคอนโหลดหมุนค้างถาวร
        // ⚠️ TemplateId ต้อง echo กลับให้ตรงกับที่ขอ เพราะ client ใช้เป็น cache key (MapSystem.cs:670-674)
        _connection.Recv(delegate(GetDiscoveryInfo msg, PacketHeader header)
        {
            Send(BuildDiscoveryInfo(msg.TemplateId), header.Seq);
        });
        _connection.Recv(delegate(GetArtifactBlueprints msg, PacketHeader header)
        {
            HandleGetArtifactBlueprintsMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(ExtendFloor msg, PacketHeader header)
        {
            HandleExtendFloorMsg(msg);
        });
        _connection.Recv(delegate(GetMusics msg, PacketHeader header)
        {
            HandleGetMusicsMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(SaveMusicToSlot msg, PacketHeader header)
        {
            HandleSaveMusicToSlotMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(RemoveMusicFromSlot msg, PacketHeader header)
        {
            HandleRemoveMusicFromSlotMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(PlayMusic msg, PacketHeader header)
        {
            HandlePlayMusicMsg(msg);
        });
        _connection.Recv(delegate(StopMusic msg, PacketHeader header)
        {
            HandleStopMusicMsg(msg);
        });
        _connection.Recv(delegate(ArtifactDisplay msg, PacketHeader header)
        {
            HandleArtifactDisplayMsg(msg);
        });
        _connection.Recv<GetAvailableEmotions>(delegate
        {
            AvailableEmotions msg2 = default;
            msg2.Motions = DataStore.Emotions?.Motions?.Keys.ToArray() ?? Array.Empty<string>();
            msg2.Emoticons = DataStore.Emotions?.Emoticons?.Select(emo => emo.Id).ToArray() ?? Array.Empty<string>();
            Send(msg2);
        });
        _connection.Recv(delegate(SayInExclusiveChannel msg, PacketHeader header)
        {
            Message_ message = msg.Message;
            message.Speaker = new RadioId
            {
                Name = _context.AppearPlayer.Name,
                Freq = _context.AppearPlayer.Freq
            };
            msg.Message = message;
            _world.BroadCast(msg);
        });
        _connection.Recv(delegate(DisappearEntityOnTile msg, PacketHeader header)
        {
            _world.DestroyNatural(msg.Tile);
        });
        _connection.Recv(delegate(GetEstateLicenses msg, PacketHeader header)
        {
            Send(BuildEstateLicenses(), header.Seq);
        });
        _connection.Recv(delegate(OpenGate msg, PacketHeader header)
        {
            _world.ArtifactManager.OpenGate(new PropKey
            {
                EntityId = msg.EntityId,
                Tile = msg.Tile
            }, open: true);
        });
        _connection.Recv(delegate(CloseGate msg, PacketHeader header)
        {
            _world.ArtifactManager.OpenGate(new PropKey
            {
                EntityId = msg.EntityId,
                Tile = msg.Tile
            }, open: false);
        });
        _connection.Recv(delegate(SetStorageItem msg, PacketHeader header)
        {
            HandleSetStorageItemMsg(msg);
        });
        _connection.Recv(delegate(TurnOnMusic msg, PacketHeader header)
        {
            _world.ArtifactManager.TurnOnMusic(msg.EntityId);
        });
        _connection.Recv(delegate(TurnOffMusic msg, PacketHeader header)
        {
            _world.ArtifactManager.TurnOffMusic(msg.EntityId);
        });
        _connection.Recv(delegate(ChangeMannequinDisplay msg, PacketHeader header)
        {
            List<Item> inventoryItems = _context.InventoryItems;
            int num = inventoryItems.FindIndex(it => it.Id == msg.ItemId);
            if (num != -1)
            {
                Item item = inventoryItems[num];
                if (_world.ArtifactManager.ChangeMannequin(msg.EntityId, msg.Slot, item))
                {
                    Send(default(OK), header.ReplyOf);
                    return;
                }
            }
            // ⚠️ ห้ามส่ง default(Abort) — Text เป็น null แล้ว **ฝั่งเกมแครช**
            // nil → UnpackGettextFromMsgPack คืน null (client/LocalizeSystem.cs:548)
            // → LimitText(null).Length → NRE (client/GameManager.cs:292)
            // ไม่ใช่บั๊กของโปรโตคอล ⇒ แก้ที่ต้นทาง ห้ามแตะ GameCode/Messages
            Send(new Abort { Text = "ทำรายการนี้ไม่ได้" }, header.ReplyOf);
        });
        _connection.Recv(delegate(TakeOutItem msg, PacketHeader header)
        {
            if (_world.ArtifactManager.TakeOutItems(msg.EntityId, msg.ItemIds))
            {
                Send(default(OK), header.ReplyOf);
            }
            else
            {
                // เหตุผลเดียวกับข้างบน — ห้ามส่ง Abort ที่ไม่มีข้อความ
                Send(new Abort { Text = "ทำรายการนี้ไม่ได้" }, header.ReplyOf);
            }
        });
        _connection.Recv(delegate(GetGrazedPets msg, PacketHeader header)
        {
            Send(new GrazedPets
            {
                Data = _world.GetGrazedPets().ToArray()
            }, header.ReplyOf);
        });
        _connection.Recv(delegate(GetQuests msg, PacketHeader header)
        {
            Chapters chapters = SingletonDict<string, Chapters>.Get(EpicCategory);
            if (chapters?.ChapterList != null)
            {
                Send(new Quests
                {
                    Category = EpicCategory,
                    Todos = chapters.ChapterList
                        .Select(chapter => chapter.Quests?.Select(q => new QuestToDo
                        {
                            Finished = true,
                            Id = q
                        }) ?? Enumerable.Empty<QuestToDo>())
                        .Aggregate(Enumerable.Concat)
                        .ToArray()
                }, header.Seq);
            }
        });
        _connection.ConnetionClosed += delegate
        {
            // แช่หลอดไว้ที่ค่าปัจจุบันก่อนปล่อย context — ไม่งั้นเส้นแนวโน้มที่ส่งไปแล้วจะเดินต่อ
            // อีกจนสุด horizon แล้วรอบเซฟอัตโนมัติจะเขียนค่าที่เดินไปแล้วลงไฟล์ (ดู SurvivalState._live)
            _survival.Freeze(Gauge.CurrentTime);
            Detach();
            Closed?.Invoke();
        };
        RegisterSystemHandlers();
        _context.PlayerInfo.DisconnectedAt = Times.UnixTimeNow();
        SendStatistics();
        SendInventory();
        SendEquipments();
        SendDefoggedChunks();
        SendQuestCategories();
        AnnouncePlayableQuests();
        Send(_context.AppearPlayer);
    }

    private WorldPosition GetEntryPosition()
    {
        return new WorldPosition(_world.EntryPoint.x * 200, _world.EntryPoint.y * 200);
    }

    private void World_ArtifactAppeared(AppearArtifact artifact)
    {
        if (IsOverlapped(artifact))
        {
            _artifactSet.Add(artifact.EntityId);
            Send(artifact);
        }
    }

    private void World_ArtifactDisappeared(AppearArtifact artifact)
    {
        if (_artifactSet.Contains(artifact.EntityId))
        {
            _artifactSet.Remove(artifact.EntityId);
            SendDisappear(artifact);
        }
    }

    private void SendDisappear(AppearArtifact artifact)
    {
        Send(new DisappearEntity { EntityId = artifact.EntityId });
    }

    private void World_PlayerAppeared(Player player) => SendAppear(player);

    public void SendAppear(Player player)
    {
        if (player.EntityId != EntityId)
        {
            Send(player._context.AppearPlayer);
        }
    }

    private void World_PlayerDisappeared(Player player) => SendDisappear(player);

    private void SendDisappear(Player player)
    {
        if (player.EntityId != EntityId)
        {
            Send(new DisappearEntity { EntityId = player.EntityId });
        }
    }

    public void AddItems(IList<Item> items)
    {
        _context.InventoryItems.AddRange(items);
        OnContextChanged();
    }

    private void OnContextChanged() => ContextChanged?.Invoke();

    /// <summary>
    /// [5 ก.ย. 2026] ส่งต่อให้ระบบสกิลจัดการทั้งชุด (Core/Player.Skills.cs)
    ///
    /// ของเดิมส่งแค่ Swimming + เลเวล + Exp ที่ hard-code ไว้ 3,532,536 ซึ่งพอมีระบบสกิลจริงแล้ว
    /// กลายเป็นตัวส่งข้อมูลผิดทับของถูก: constructor เรียกตัวนี้ (บรรทัด 524) หลังระบบสกิล
    /// ตั้งค่าเสร็จ ⇒ เลเวลกับ exp ที่ผู้เล่นเห็นตอนเข้าเกมเป็นของปลอมจนกว่าจะยิง GetStatistics
    ///
    /// ⚠️ ค่า Derived ที่ขาดแล้วพังเงียบ: FatigueCaution(4)/FatigueDanger(5) ไม่มี ⇒ หลอดความ
    /// เหนื่อยไม่มีขั้น "เหนื่อย/หมดแรง" (client/Durango.Logic/FatigueSystem.cs:124-125 ได้ -1
    /// แล้ว fallback เป็น Max) · MaxTamingPet(303) ไม่มี ⇒ ป้ายนับสัตว์โชว์ "N / 0"
    /// ทั้งสองอย่างอยู่ใน SendFullStatistics แล้ว
    /// </summary>
    private void SendStatistics() => SendFullStatistics();

    /// <summary>
    /// รายชื่อสิ่งมีชีวิตที่ "ควรเจอได้" บนเกาะแบบนี้ + เจอแล้วหรือยัง
    ///
    /// ⚠️ ส่งชุดว่างไป = ระบบค้นพบตายสนิทแบบเงียบ ๆ: <c>client/Durango.Logic.Map/DiscoverInfo.cs:47-60</c>
    /// นับว่า "เจอครบแล้ว" ทันทีเมื่อรายการว่าง (num == size ตอนที่ทั้งคู่เป็น 0) แล้ว return
    /// ⇒ เกมไม่เคยยิง <c>DiscoverAnimal</c> ออกมาเลย สารานุกรมจึงไม่มีวันปลด
    ///
    /// รายชื่อสัตว์เอามาจากฝูงที่แม่แบบของเกาะนั้นสั่งไว้จริง (region_templates.json → herds → spawns)
    /// ⇒ ตรงกับสัตว์ที่เกิดจริงบนเกาะ ไม่ใช่รายชื่อทั้งเกม
    ///
    /// <c>BiocomNames</c> ยังส่งชุดว่าง — ยังไม่ได้ทำระบบ biocom (แหล่งแร่/หลุม) และยังไม่รู้แน่ชัด
    /// ว่าฝั่งเกมคาดหวังชื่ออะไร (ในแม่แบบเป็นคีย์ scoops/cracks/commons/craters) ⇒ ไม่เดา
    /// </summary>
    private DiscoveryInfo BuildDiscoveryInfo(string templateId)
    {
        var animals = new List<Pair<ushort, bool>>();
        RegionCatalog.TemplateInfo template = RegionCatalog.GetTemplate(templateId)
                                              ?? RegionCatalog.GetTemplate(_world.TerrainInfo?.region_template);
        if (template != null)
        {
            var seen = new HashSet<ushort>();
            foreach (var group in template.Herds)
            {
                foreach (RegionCatalog.HerdSpawn spawn in group.Value)
                {
                    // ยังไม่มีที่เก็บ "เจอชนิดไหนไปแล้วบ้าง" ต่อผู้เล่น (PlayerContext ไม่มีช่อง)
                    // ⇒ ส่ง false ไว้ก่อน ผลคือเกมพยายามค้นหาสัตว์รอบตัวทุกครั้งที่เข้าเกาะ
                    // ซึ่งถูกต้องกว่าบอกว่าเจอครบแล้วทั้งที่ยังไม่เคยเจอ
                    if (seen.Add(spawn.EntityType)) animals.Add(new Pair<ushort, bool>(spawn.EntityType, false));
                }
            }
        }

        return new DiscoveryInfo
        {
            TemplateId = templateId,
            BiocomNames = Array.Empty<Pair<string, bool>>(),
            AnimalTypes = animals.ToArray()
        };
    }

    // ส่งสถานะทั้งชุดของผู้เล่นคนนี้ (client แทนที่ list ทั้งก้อนทุกครั้งที่ได้รับ)
    // replyOf = 0 คือ push (ใช้ตอน ToggleStatusEffect), ใส่ seq ตอนตอบ GetStatusEffects
    // Level = 1 เอามาจาก data จริง: away_from_keyboard มี min_level/max_level = 1
    //   (data/assets/survival/status_effects.json) และ client หา template ด้วยช่วง
    //   MinLevel <= level <= MaxLevel (client/Yaml/StatusEffectTemplateYaml.cs:15)
    //   ⇒ ส่งเลเวลผิดช่วง = client หา template ไม่เจอแล้วทิ้งสถานะนั้นเงียบ ๆ
    //   (client/Durango.Logic/StatusEffects.cs:52-58)
    // Until = 0 เพราะสถานะกลุ่มนี้ไม่มีอายุ — มันจบเมื่อ client ส่ง Toggle=false เท่านั้น
    //   client แสดงเวลาที่เหลือแบบ clamp ที่ 0 อยู่แล้ว (Durango.Logic/StatusEffect.cs:87)
    // Stacked = 0 ตรงกับ stack_size = 0 ในไฟล์ data (ไม่ซ้อนชั้น)
    private void SendStatusEffects(uint replyOf = 0u)
    {
        // รวม toggle (AFK) + timed (อาหาร/อากาศ/พัก) เป็นลิสต์เดียว
        // client แทนที่ทั้งก้อนทุกครั้ง — ห้ามส่งแค่ส่วนที่เปลี่ยน
        var list = new List<StatusEffect>();
        foreach (KeyValuePair<string, double> effect in _toggledStatusEffects)
        {
            list.Add(new StatusEffect
            {
                Id = effect.Key,
                EffectId = effect.Key,
                Level = 1,
                Since = effect.Value,
                Until = 0.0,
                Stacked = 0,
                DurationHidden = true,
                Effects = StatusEffectCatalog.PackDetails(effect.Key, 1)
            });
        }
        foreach (TimedStatusEffect effect in _timedStatusEffects.Values)
        {
            // ถ้า id ซ้ำกับ toggle ให้ timed ทับ (เช่น rest ที่เซิร์ฟเป็นคนคุม)
            int idx = list.FindIndex(e => string.Equals(e.EffectId, effect.Id, StringComparison.OrdinalIgnoreCase));
            var packed = new StatusEffect
            {
                Id = effect.Id,
                EffectId = effect.Id,
                Level = effect.Level,
                Since = effect.Since,
                Until = effect.Until,
                Stacked = 0,
                DurationHidden = effect.Until <= 0,
                NameGettext = effect.NameGettext,
                Effects = StatusEffectCatalog.PackDetails(effect.Id, effect.Level)
            };
            if (idx >= 0) list[idx] = packed;
            else list.Add(packed);
        }
        Send(new StatusEffects
        {
            EntityId = EntityId,
            _StatusEffects = list.ToArray()
        }, replyOf);
    }

    // สร้างเพดาน exp ต้านทานจากตารางจริงใน data/assets/statistics/player.json
    // key ของตารางคือ "เลเวลต้านทาน" — เซิร์ฟยังไม่ส่ง Statistics.ResistanceLevels (SendStatistics
    // ข้างบนไม่ได้เซ็ตฟิลด์นั้น) client จึงถือว่าทุกชนิดเป็นเลเวล 1 (StatisticsSystem.cs:55
    // ResistanceLevels.Get(type, 1)) ⇒ ใช้แถวเลเวล 1 ให้ตรงกับที่ client เชื่อ
    // CapIndex = 0 คือยังไม่ชนเพดานขั้นไหน (เซิร์ฟยังไม่นับ exp ที่ได้ต่อวัน) ⇒ ได้เรตของขั้นแรก
    // ExpLimits = cap_amount ของทุกขั้นที่มีเพดาน (ขั้นสุดท้าย cap_amount = null แปลว่าไม่จำกัด
    //   จึงตัดทิ้ง — ฟิลด์เป็น int[] ใส่ null ไม่ได้) client ไม่เคยอ่านฟิลด์นี้ ส่งไปเพื่อความครบเท่านั้น
    //   ⇒ ความหมายของลำดับในอาเรย์นี้ยืนยันจากโค้ดเกมไม่ได้ ถ้าจะทำระบบเพดานจริงต้องเช็คซ้ำ
    // ExpiresAt = 0 เป็นค่าของเรา ไม่ใช่ของต้นฉบับ: ยังไม่มีระบบรีเซ็ตเพดานรายวัน
    //   ⚠️ ห้ามใส่ค่า > 0 มั่ว ๆ เพราะ client จะนัดถามซ้ำที่เวลานั้น (StatisticsSystem.cs:118-122)
    private static Dictionary<Derived, ResistanceExpCap> BuildResistanceExpCaps()
    {
        var caps = new Dictionary<Derived, ResistanceExpCap>();
        Dictionary<Biome, Derived> typeByBiome = Singleton<Constants>.Instance?.Resistance.TypeByBiome;
        Dictionary<int, List<ResistanceExpGrownCap>> table =
            Singleton<PlayerStatistics>.Instance?.ResistanceExpGrownCaps;
        if (typeByBiome == null || table == null ||
            !table.TryGetValue(1, out List<ResistanceExpGrownCap> tiers) || tiers == null || tiers.Count == 0)
        {
            // ไม่มี data = ตอบ dict ว่าง ดีกว่าเดาเลข — client รับ dict ว่างได้ และจะไม่นัดถามซ้ำ
            // (FirstOrDefault(ExpiresAt > 0) คืน default ⇒ เวลาที่ได้ติดลบ ⇒ ไม่ตั้ง DelayedCall)
            Console.WriteLine("[stat] ไม่มีตาราง resistance_exp_grown_caps — ตอบ ResistanceExpCaps ว่าง");
            return caps;
        }
        var cap = new ResistanceExpCap
        {
            CapIndex = 0,
            ExpLimits = tiers.Where(tier => tier.CapAmount.HasValue).Select(tier => tier.CapAmount.Value).ToArray(),
            ExpRate = tiers[0].ExpRate,
            ExpiresAt = 0.0
        };
        foreach (Derived type in typeByBiome.Values)
        {
            caps[type] = cap;
        }
        return caps;
    }

    private void SetCenterChunks(int x, int y)
    {
        int num = Math.Clamp(x, 0, _world.NumChunksX - 1);
        int num2 = Math.Clamp(y, 0, _world.NumChunksY - 1);
        ClearVisited(num, num2);
        _centerX = num;
        _centerY = num2;
        MarkVisit();
        List<Chunk> list = _world.CreateChunkMessages(_centerX, _centerY, _chunkVisited);
        for (int i = 0; i < list.Count; i++)
        {
            Send(list[i]);
        }
        foreach (string item in _artifactSet.ToList())
        {
            AppearArtifact? appearArtifact = _world.ArtifactManager.Get(item);
            if (appearArtifact.HasValue && !IsOverlapped(appearArtifact.Value))
            {
                SendDisappear(appearArtifact.Value);
                _artifactSet.Remove(item);
            }
        }
        foreach (AppearArtifact item2 in _world.ArtifactManager.Enumerable(artifact =>
                     !_artifactSet.Contains(artifact.EntityId) && IsOverlapped(artifact)))
        {
            _artifactSet.Add(item2.EntityId);
            Send(item2);
        }
        // ส่งที่ดินในรัศมี chunk ที่เพิ่งเข้า
        var estateChunks = new System.Collections.Generic.List<Point2>();
        for (int ex = _centerX - 1; ex <= _centerX + 1; ex++)
        for (int ey = _centerY - 1; ey <= _centerY + 1; ey++)
        {
            if (ex >= 0 && ey >= 0 && ex < _world.NumChunksX && ey < _world.NumChunksY)
            {
                estateChunks.Add(new Point2(ex, ey));
            }
        }
        if (estateChunks.Count > 0)
        {
            Send(_world.BuildEstateGridsForChunks(estateChunks));
        }
    }

    private void ClearVisited(int newX, int newY)
    {
        for (int i = _centerX - 1; i <= _centerX + 1; i++)
        for (int j = _centerY - 1; j <= _centerY + 1; j++)
        {
            if (i >= 0 && i < _world.NumChunksX && j >= 0 && j < _world.NumChunksY &&
                (i < newX - 1 || i > newX + 1 || j < newY - 1 || j > newY + 1))
            {
                _chunkVisited[i, j] = World.ChunkVisit.None;
            }
        }
    }

    private void MarkVisit()
    {
        for (int i = _centerX - 1; i <= _centerX + 1; i++)
        for (int j = _centerY - 1; j <= _centerY + 1; j++)
        {
            if (i >= 0 && i < _world.NumChunksX && j >= 0 && j < _world.NumChunksY &&
                _chunkVisited[i, j] != World.ChunkVisit.Sent)
            {
                _chunkVisited[i, j] = World.ChunkVisit.Visit;
            }
        }
    }

    /// <summary>
    /// เพดานก้อนการเคลื่อนที่ — **ค่าของเรา** ตั้งจากพฤติกรรมจริงของตัวเกม
    ///
    /// ฝั่งเกมยุบก้อนก่อนส่งเสมอ (client/MoveMsgGenerator.cs:152 CompactMovement) ⇒ ปกติได้ไม่กี่ชิ้น
    /// ตั้งเผื่อไว้กว้างมากแล้ว แต่ยังต่ำกว่าที่จะทำให้แพ็กเก็ตล้นบัฟเฟอร์ 512 KB หลายเท่า
    /// (จุดของ path หนึ่งจุด ~30 ไบต์ ⇒ 512 จุด ≈ 15 KB)
    /// </summary>
    private const int MaxMovementsPerMessage = 16;
    private const int MaxPathNodesPerMessage = 512;

    /// <summary>ก้อนนี้มีขนาดสมเหตุสมผลพอจะกระจายต่อไหม</summary>
    private static bool IsSaneMove(Move msg)
    {
        Movement[] movements = msg.Movements;
        if (movements == null || movements.Length == 0) return false;
        if (movements.Length > MaxMovementsPerMessage) return false;

        int nodes = 0;
        foreach (Movement movement in movements)
        {
            if (movement.Path == null || movement.Path.Length == 0) return false;
            nodes += movement.Path.Length;
            if (nodes > MaxPathNodesPerMessage) return false;
        }
        return true;
    }

    private void HandleMoveMsg(Movement[] movements)
    {
        int num = movements.Length - 1;
        if (num >= 0)
        {
            WorldPosition before = _context.AppearPlayer.Move.Movements[0].Path[0].Position;
            Movement movement = movements[num];
            _context.AppearPlayer.Move.Movements[0] = movement;
            int num2 = movement.Path.Length - 1;
            if (num2 >= 0)
            {
                _context.AppearPlayer.Move.Movements[0].Path[0].Position = movement.Path[num2].Position;
            }
            // [5 ก.ย. 2026] จับว่า "ขยับจริง" ไหม — เกมส่ง Move ตอนเปลี่ยนท่าทางด้วย ไม่ใช่แค่ตอนเดิน
            // (client/MoveMsgGenerator.cs:104-110 MotionChanged ก็ตั้ง SendMoveRequired) ⇒ ดูตำแหน่ง
            // ไม่ใช่ดูว่ามี message มา · ไม่มี message "หยุดเดิน" จึงเก็บเวลาไว้แล้วให้ Process ตัดสิน
            WorldPosition after = _context.AppearPlayer.Move.Movements[0].Path[0].Position;
            if (!Mathf.Approximately(before.x, after.x) || !Mathf.Approximately(before.y, after.y))
            {
                _lastMovedAt = Gauge.CurrentTime;
                // สถานะ "rest" ติดแท็ก clear_on_move ในไฟล์ data (survival/status_effects.json)
                bool wasResting = _timedStatusEffects.ContainsKey("rest");
                _survival.SetResting(false);
                if (wasResting && ClearTimedStatusEffect("rest"))
                {
                    SendStatusEffects();
                    FlushSurvival();
                }
            }
        }
    }

    private bool IsOverlapped(AppearArtifact artifact)
    {
        int num = (_centerX - 1) * 16;
        int num2 = (_centerX + 2) * 16;
        int num3 = (_centerY - 1) * 16;
        int num4 = (_centerY + 2) * 16;
        bool flag = num - artifact.Size.x + 1 <= artifact.Tile.x && artifact.Tile.x < num2;
        bool flag2 = num3 - artifact.Size.y + 1 <= artifact.Tile.y && artifact.Tile.y < num4;
        return flag && flag2;
    }

    private void SendDefoggedChunks()
    {
        Send(_world.CreateDefoggedChunks());
    }

    private void SendQuestCategories(uint seq = 0u)
    {
        Send(new QuestCategories
        {
            Categories = new[]
            {
                new QuestCategory
                {
                    Category = QuestCatalog.DailyCategory,
                    Name = "รายวัน",
                    UnreceivedCount = CountClaimableDaily()
                }
            },
            Epic = new QuestCategory { Category = EpicCategory }
        }, seq);
    }

    private void HandleCheatMsg(string cheat, uint seq)
    {
        cheat = cheat.ToLower();
        string[] array = cheat.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (array.Length == 0)
        {
            return;
        }
        switch (array[0])
        {
            case "m":
            {
                if (array.Length >= 3)
                {
                    Teleported msg = default;
                    msg.Type = TeleportType.Unknown;
                    if (int.TryParse(array[1], out msg.Tile.x) && int.TryParse(array[2], out msg.Tile.y))
                    {
                        Send(msg);
                    }
                }
                break;
            }
            case "it":
            {
                InventoryUpdated msg2 = new()
                {
                    EntityId = EntityId
                };
                int level = int.Parse(array[2]);
                int result = 0;
                if (KUtility.GetSize(array) >= 4)
                {
                    int.TryParse(array[3], out result);
                }
                if (result == 0) result = 1;
                var list = new List<Item>();
                for (int num6 = 0; num6 < result; num6++)
                {
                    Item? item2 = Cheats.MakeItem(array[1], level);
                    if (item2.HasValue) list.Add(item2.Value);
                }
                msg2.Items = list.ToArray();
                AddItems(list);
                Send(new Info { Text = $"{list[0].Name} {result}개 획득" }, seq);
                Send(msg2);
                break;
            }
            case "immortal":
            case "prop":
            {
                AppearArtifact? appearArtifact = Cheats.MakeAppearArtifact(array, out var addons);
                if (!appearArtifact.HasValue) return;
                _world.ConstructArtifact(appearArtifact.Value, addons, EntityId);
                break;
            }
            case "it_color":
            {
                if (array.Length >= 2)
                {
                    string itemId2 = array[1];
                    List<Item> inventoryItems2 = _context.InventoryItems;
                    int num5 = inventoryItems2.FindIndex(it => it.Id == itemId2);
                    if (num5 == -1) return;
                    Item item = inventoryItems2[num5];
                    Prototype itemPrototype = PrototypeYaml.GetItemPrototype(item.Prototype);
                    if (itemPrototype == null) return;
                    int value = UnityEngine.Random.Range(0, int.MaxValue);
                    ItemIconTex.TryGetDefaultColor(itemPrototype.ColorR, out var col, value, Color.white);
                    ItemIconTex.TryGetDefaultColor(itemPrototype.ColorG, out var col2, value, Color.white);
                    ItemIconTex.TryGetDefaultColor(itemPrototype.ColorB, out var col3, value, Color.white);
                    item.ColorR = col.ToHex();
                    item.ColorG = col2.ToHex();
                    item.ColorB = col3.ToHex();
                    inventoryItems2[num5] = item;
                    Send(new InventoryUpdated
                    {
                        EntityId = EntityId,
                        Items = new[] { item }
                    });
                    if (_context.EquippedItems.Any(pair => pair.Value == itemId2))
                    {
                        UpdateEquipments();
                        _world.BroadCast(_context.AppearPlayer.Display);
                    }
                }
                break;
            }
            case "natural":
            {
                if (array.Length >= 4)
                {
                    int x = array[1].ToInt();
                    int y = array[2].ToInt();
                    int num3 = array[3].ToInt();
                    _world.AddNatural(new Point2(x, y), (ushort)num3);
                }
                break;
            }
            // [5 ก.ย. 2026] "sv <หลอด> <ค่า>" — ตั้งค่าหลอดสถานะตรง ๆ เพื่อทดสอบเส้นทาง
            // SurvivalUpdated (183) แบบ "ค่ากระโดด" ให้เห็นบนจอจริง เช่น  sv fatigue 70
            // หลอดที่ใช้ได้: life / stamina / energy / health / fatigue / groggy
            case "sv":
            {
                if (array.Length >= 3 && float.TryParse(array[2],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float target))
                {
                    _survival.Set(array[1], target);
                    FlushSurvival();
                    Send(new Info { Text = $"{array[1]} = {target}" }, seq);
                }
                break;
            }
            // [7 ก.ย. 2026] "animal <ชนิด> [เลเวล]" — เสกสัตว์ลงข้าง ๆ ตัวเรา
            // มีไว้ทดสอบอนิเมชั่น/การไล่กัด/การตาย โดยไม่ต้องเดินหาสัตว์ทั่วเกาะ
            // เช่น  animal 2013 20   (ดูเลขชนิดที่ data/assets/entity_types/animal.json)
            case "animal":
            {
                AnimalManager manager = _world.AnimalManager;
                if (manager == null || array.Length < 2 || !ushort.TryParse(array[1], out ushort animalType))
                {
                    Send(new Info { Text = "ใช้: animal <ชนิด> [เลเวล]" }, seq);
                    break;
                }
                int animalLevel = array.Length >= 3 && int.TryParse(array[2], out int lv) ? lv : 1;

                // วางห่างไป 2 ช่องทางขวา ไม่ให้เกิดทับตัวผู้เล่นจนกล้องมองไม่เห็น
                WorldPosition me = PlayerWorldPosition();
                var spot = new Point2((int)Math.Round(me.x / 200f) + 2, (int)Math.Round(me.y / 200f));

                AnimalManager.Animal spawned = manager.SpawnAt(animalType, animalLevel, spot);
                if (spawned == null)
                {
                    Send(new Info { Text = $"ไม่มีสัตว์ชนิด {animalType} ในข้อมูล" }, seq);
                    break;
                }
                Send(new Info { Text = $"เสก {animalType} lv{spawned.CombatLevel} ที่ [{spot.x},{spot.y}]" }, seq);
                Console.WriteLine($"[โกง] {Short(EntityId)} เสกสัตว์ {animalType} lv{spawned.CombatLevel} " +
                                  $"ที่ [{spot.x},{spot.y}] (id {spawned.EntityId})");
                break;
            }
            case "weather":
            {
                string[] array2 = { "sunny", "cloudy", "rainy", "heavy_rainy", "snowy", "heavy_snowy" };
                string text = null;
                for (int num4 = 0; num4 < 10; num4++)
                {
                    string text2 = array2[UnityEngine.Random.Range(0, array2.Length)];
                    if (text2 != _world.Weather)
                    {
                        text = text2;
                        break;
                    }
                }
                if (text != null)
                {
                    _world.ChangeWeather(text);
                }
                return;
            }
            case "pet_grazing":
            {
                string itemId = array[1];
                List<Item> inventoryItems = _context.InventoryItems;
                int num2 = inventoryItems.FindIndex(it => it.Id == itemId);
                if (num2 == -1) return;
                PerformanceYaml.Rein rein = PerformanceYaml.GetRein(inventoryItems[num2].Prototype);
                if (rein == null) return;
                List<Messages.Pet> grazedPets2 = _world.GetGrazedPets();
                grazedPets2.Add(new Messages.Pet
                {
                    EntityId = itemId,
                    EntityType = (ushort)rein.PetEntityType,
                    Name = rein.PetName,
                    Stat = new PetStats
                    {
                        GrazedAt = Times.UnixTimeNow(),
                        PlaybackRate = rein.PlaybackRate
                    }
                });
                _world.BroadCast(new GrazedPets { Data = grazedPets2.ToArray() });
                _context.InventoryItems.RemoveAt(num2);
                Send(new InventoryUpdated
                {
                    EntityId = EntityId,
                    RemovedItemIds = new[] { itemId }
                });
                _world.Save();
                break;
            }
            case "remove_grazing":
            {
                string id = array[1];
                List<Messages.Pet> grazedPets = _world.GetGrazedPets();
                int num = grazedPets.FindIndex(p => p.EntityId == id);
                if (num != -1)
                {
                    grazedPets.RemoveAt(num);
                    _world.BroadCast(new GrazedPets { Data = grazedPets.ToArray() });
                    _world.Save();
                }
                break;
            }
        }
        OnContextChanged();
    }

    private void HandleTouchMsg(Messages.Touch touch, uint seq)
    {
        if (touch.EntityType == 0)
        {
            return;
        }
        Touched msg = new()
        {
            EntityId = touch.EntityId
        };
        // ต้นฉบับ: GameManager.ClusterMode == Mode.Editable — โหมดสร้างสรรค์ (Creative Island)
        // เซิร์ฟนี้เป็น Offline เสมอตามค่า config (Host.ClusterMode) ⇒ อินเทอร์แอกชัน Editable ถูกซ่อนตามแท้
        bool flag = Host.ClusterMode == Mode.Editable;

        // [5 ก.ย. 2026] สัตว์ป่า — ต้องเช็คก่อนสาขาสิ่งปลูกสร้าง เพราะชนิดสัตว์เป็นเลข 2000-2999
        // ซึ่งเข้าเงื่อนไข "< 10000" เหมือนกัน แล้วไปหา blueprint ไม่เจอ ⇒ ได้เมนูเปล่า
        // (อาการ: แตะสัตว์แล้วไม่มีปุ่มอะไรขึ้นเลย ตีไม่ได้)
        // รายละเอียดระบบอยู่ที่ Core/Player.Hunting.cs
        if (TryTouchAnimal(touch, ref msg))
        {
            Send(msg, seq);
            OnContextChanged();
            return;
        }

        if (touch.EntityType < 10000)
        {
            MergedBlueprint blueprint = BlueprintStore.GetBlueprint(touch.EntityType);
            if (blueprint != null)
            {
                msg.EntityName = blueprint.Name;
                var list = new List<Shared.System.Interaction>();

                // [6 ก.ย. 2026] เมนูก่อสร้าง — ผูกกับ **สถานะของหลัง** ไม่ใช่โหมดเกาะ
                //
                // ⚠️ ไม่ใส่สองบรรทัดนี้ = ผู้เล่นจองพื้นที่ได้แล้วแตะดู **ไม่มีปุ่มอะไรขึ้นเลย**
                // เพราะฝั่งเกมไม่ได้ตัดสินใจเอง มันเชื่อรายการที่เซิร์ฟส่งมาล้วน ๆ
                // (เหตุผลเดียวกับเมนูกรง/สัตว์ที่เคยหายไปทั้งระบบ)
                //
                // ⚠️ ห้ามใช้ `flag` มาคุมตัวนี้: flag คือ Mode.Editable (เกาะสร้างสรรค์ของโหมดออฟไลน์)
                // แต่การสร้างบ้านเป็นแกนหลักของเกมที่ต้องใช้ได้ทุกเกาะ — ผูกกับ flag แล้ว
                // เซิร์ฟที่รันโหมด Online (ค่าปัจจุบันใน data/config.json) จะสร้างอะไรไม่ได้เลย
                AppearArtifact? touched = _world.ArtifactManager.Get(touch.EntityId);

                // [6 ก.ย. 2026] เมนู "ใช้งาน" ทุกตัวต้องรอสร้างเสร็จก่อน
                //
                // ⚠️ เจอตอนเทส: หลุมกองไฟที่ยังไม่ใส่วัสดุสักชิ้น กลับ "พักผ่อนเต็มที่" ได้เลย
                // เพราะเมนูที่มาจาก component ถูกใส่ทุกกรณีโดยไม่ดู BuildingState
                // ต้นฉบับผูก component พวกนี้กับของที่สร้างเสร็จแล้วเท่านั้น
                //
                // หลังที่ไม่รู้จัก (ไม่อยู่ใน ArtifactManager — คนละเกาะ/ของระบบ) ปล่อยผ่านเหมือนเดิม
                // เพราะเช็คสถานะไม่ได้ ไม่ใช่เพราะมันสร้างเสร็จ
                bool completed = !touched.HasValue
                                 || touched.Value.States.BuildingState == Shared.Building.BuildingState.Completed;

                // เจ้าของเท่านั้นที่รื้อ/เก็บ/เขียนป้ายได้ — ตัวจริงที่บังคับคือ MayTouchArtifact ตอนรับคำสั่ง
                // ตรงนี้แค่ไม่โชว์ปุ่มที่กดไปก็โดนปฏิเสธ (ของคนอื่นจะไม่มีปุ่มพวกนี้เลย)
                //
                // [7 ก.ย. 2026] ย้ายออกมานอกบล็อก `touched` เพราะเมนูที่มาจาก component
                // (ป้าย/ประตู/หุ่น) อยู่ข้างล่างและต้องใช้ค่านี้ด้วย
                bool mine = string.Equals(_world.ArtifactManager.OwnerOf(touch.EntityId),
                                          EntityId, StringComparison.Ordinal);

                if (touched is { } building)
                {
                    switch (building.States.BuildingState)
                    {
                        case Shared.Building.BuildingState.Occupied:
                            // "건설" — เปิดหน้าต่างใส่วัสดุ (client/BuildSystem.cs InteractionBuildArtifact)
                            list.Add(Shared.System.Interaction.BuildArtifact);
                            break;
                        case Shared.Building.BuildingState.Built:
                            // "완성" — โผล่เฉพาะตอนมาร์มูรีครบแล้ว ฝั่งเกมเดินหลอดเองจาก Postprocess.EndsAt
                            if (building.States.Postprocess is not { } pp || Gauge.CurrentTime >= pp.EndsAt)
                            {
                                list.Add(Shared.System.Interaction.CompleteArtifact);
                            }
                            break;
                        case Shared.Building.BuildingState.Completed:
                            // "포장" — เก็บใส่กระเป๋าแล้วเอาไปวางที่ใหม่ (Player.Building.cs)
                            // ของถาวร (ท่าเรือ/รูวาร์ป) เก็บไม่ได้ตามข้อมูลเกมเอง
                            if (mine && !blueprint.Permanent)
                            {
                                list.Add(Shared.System.Interaction.Capsulate);
                            }
                            break;
                    }

                    // "제거" — รื้อทิ้ง
                    //
                    // ⚠️ [6 ก.ย. 2026] เดิมผูกกับ `flag` (Mode.Editable) ⇒ เซิร์ฟที่รันโหมด Online
                    // ซึ่งเป็นค่าจริงใน data/config.json **ไม่มีปุ่มรื้อเลยสักหลัง** ผู้เล่นสร้างผิดที่
                    // แล้วแก้ไม่ได้ ต้องเรียกแอดมินมาลบให้ (เจอตอนเทสด้วย bot — เมนูมีแค่ Rest)
                    // ⇒ ผูกกับ "เป็นเจ้าของ" แทน ซึ่งตรงกับด่านจริงที่ HandleDestructMsg ใช้อยู่แล้ว
                    if (mine || flag) list.Add(Shared.System.Interaction.DestructArtifact);
                }
                if (!completed)
                {
                    // ยังสร้างไม่เสร็จ — มีได้แค่เมนูของงานก่อสร้างข้างบน (สร้าง/สำเร็จ/รื้อ)
                    msg.Interactions = list.Select(o => (int)o).ToArray();
                    msg.Mannequin = _world.ArtifactManager.GetMannequin(touch.EntityId);
                    Send(msg, seq);
                    OnContextChanged();
                    return;
                }

                // [6 ก.ย. 2026] "제작" — โต๊ะทำงาน กองไฟทำอาหาร เตาเผา ฯลฯ
                //
                // ⚠️ ไม่ใส่บรรทัดนี้ = แตะโต๊ะทำงานชนิดไหนก็ **ไม่มีเมนูคราฟเลย**
                // ทั้งที่ระบบคราฟฝั่งเซิร์ฟทำครบแล้ว (Player.Crafting.cs — CheckWorkbench
                // ก็ดู component "Workbench" ตัวเดียวกันนี้)
                // ฝั่งเกมผูกหน้าจอเลือกสูตรไว้กับ Interaction.Craft
                // (client/Durango.UI/RecipeSelectorGroup.cs:112 AddInteractionHandler)
                // และไม่ได้ดู components เอง มันเชื่อรายการที่เซิร์ฟส่งมาล้วน ๆ
                //
                // ข้อมูลจริง entity_types/artifact.json: prototype ที่มี component นี้ 61 ชนิด
                // เช่น s02_bonfire (7111) = 요리용 모닥불 "กองไฟทำอาหาร"
                if (blueprint.Components.Contains("Workbench")) list.Add(Shared.System.Interaction.Craft);
                if (blueprint.Components.Contains("Washable")) list.Add(Shared.System.Interaction.Wash);
                if (blueprint.Components.Contains("Shelter")) list.Add(Shared.System.Interaction.Rest);

                // [7 ก.ย. 2026] "불 붙이기 / 불 끄기" — จุดไฟและดับไฟกองไฟ เตาเผา เตาอบ
                //
                // ⚠️ ไม่ใส่บล็อกนี้ = แตะกองไฟแล้ว **ไม่มีปุ่มจุดไฟเลย** ทั้งที่ handler ฝั่งเซิร์ฟ
                // ทำครบตั้งแต่แรก (Core/Player.Farm.cs — FireBurnable/ExtinguishBurnable
                // สลับโมเดล default_look ↔ default_look + "_burning" แล้วกระจายทั้งเกาะ)
                // ฝั่งเกมผูกไว้ที่ client/Durango.Logic.Interactions/ArtifactInteractions.cs:80-81
                // และไม่ได้ดู components เอง มันเชื่อรายการที่เซิร์ฟส่งมาล้วน ๆ
                //
                // ข้อมูลจริง entity_types/artifact.json: prototype ที่มี component นี้ 19 ชนิด
                // (bonfire · s02_bonfire · kitchen_01-04 · kiln_01-04 · furnace_01 · camp_* ฯลฯ)
                // และทั้ง 19 ชนิดมีโมเดล <default_look>_burning อยู่จริงใน building/artifact_models.json
                // ⇒ ทุกตัวจุดไฟได้จริง ไม่มีตัวไหนกดแล้วค้าง
                //
                // โชว์ปุ่มเดียวตามสภาพปัจจุบัน (ติดอยู่ = โชว์ "ดับไฟ") เพราะฝั่งเกมไม่มีปุ่ม toggle
                // — เกณฑ์เดียวกับที่เมนูประตู (Gate) ข้างล่างใช้อยู่แล้ว
                //
                // ไม่ผูกกับ `flag` (Mode.Editable) และไม่เช็คเจ้าของ: กองไฟเป็นของใช้ร่วมกัน
                // (ต้นฉบับไม่เช็คเจ้าของในเมนูนี้) และ HandleBurnableMsg มีด่านระยะทางอยู่แล้ว
                if (blueprint.Components.Contains("Burnable") && !string.IsNullOrEmpty(blueprint.DefaultLook))
                {
                    list.Add(IsBurning(touched, blueprint)
                        ? Shared.System.Interaction.Extinguish
                        : Shared.System.Interaction.Fire);
                }

                // [6 ก.ย. 2026] "ตั้งเป็นจุดกลับ" — ข้อมูลจริงมี 12 แบบแปลนที่มี component Home
                // (bed_01..bed_04 · tent · temptent ฯลฯ) ทั้งหมดเป็นที่นอน
                //
                // ⚠️ ไม่ใส่บรรทัดนี้ = **ผู้เล่นตั้งบ้านไม่ได้เลย** แล้วปุ่มบ้านบนแผนที่
                // ก็พาไปจุดเข้าเกาะตลอดกาล — ระบบวาร์ปทั้งระบบ (Player.Warp.cs) ไร้ประโยชน์
                // ฝั่งเกมไม่ได้ตัดสินใจเอง มันเชื่อรายการที่เซิร์ฟส่งมาล้วน ๆ
                //
                // ไม่ผูกกับ flag (Mode.Editable) ด้วยเหตุผลเดียวกับเมนูก่อสร้างข้างล่าง:
                // การตั้งจุดกลับเป็นเรื่องปกติของทุกเกาะ ไม่ใช่ของโหมดสร้างสรรค์
                if (blueprint.Components.Contains("Home"))
                {
                    list.Add(Shared.System.Interaction.SetAsHome);
                }
                // [5 ก.ย. 2026] ท่าเรือ — เมนู "เส้นทางเดินเรือ" ของเกมผูกกับ interaction นี้
                // (client/Durango.UI/ExploreGroup.cs:259 AddInteractionHandler(Interaction.SailingRoutes)
                //  → Open(entityId, tile, RouteType.Normal) → ยิง GetRoutes มาที่เซิร์ฟ)
                // เกมไม่ได้ดู components เอง มันเชื่อรายการที่เซิร์ฟส่งมาใน Touched.Interactions ล้วน ๆ
                if (blueprint.Components.Contains("Port")) list.Add(Shared.System.Interaction.SailingRoutes);
                if (blueprint.Components.Contains("Growable") && flag) list.Add(Shared.System.Interaction.Plant);
                // [5 ก.ย. 2026] กรง — ไม่ใส่ 2 บรรทัดนี้ แตะกรงแล้วไม่มีปุ่มอะไรขึ้นเลย
                // (client/Durango.UI/GrowCageGroup.cs:62 ผูกหน้าจอไว้กับ Interaction.Cage)
                // สถานะความจุกรงเติมให้ตอนสร้าง/โหลดโลกแล้วที่ Support/CageTypes.cs
                if (blueprint.Components.Contains("GrowCage")) list.Add(Shared.System.Interaction.Cage);
                if (blueprint.Components.Contains("DomesticCage")) list.Add(Shared.System.Interaction.OpenDomesticCage);
                if (blueprint.Components.Contains("Modular") && flag)
                {
                    list.Add(Shared.System.Interaction.AddOnManage);
                    list.Add(Shared.System.Interaction.RemodelArtifact);
                }
                // [7 ก.ย. 2026] ป้าย/กระดาน — เขียนข้อความและวาดรูปลงบนป้ายที่สร้างเอง
                //
                // ⚠️ เดิมผูกกับ `flag` (Mode.Editable) แบบเดียวกับปุ่มรื้อที่แก้ไปแล้ว
                // ⇒ เซิร์ฟที่รันโหมด Online (ค่าจริงใน data/config.json) **แตะป้ายแล้วไม่มีปุ่มเขียนเลย**
                // ทั้งที่ handler ฝั่งเซิร์ฟทำครบแล้ว (Scribble → ArtifactManager.Scribble)
                // ฝั่งเกมไม่ได้ดู components เอง มันเชื่อรายการที่เซิร์ฟส่งมาล้วน ๆ
                //
                // ข้อมูลจริง entity_types/artifact.json: prototype ที่มี component นี้ 13 ชนิด
                // (표지판 7020 · 칠판 7081 · 화이트보드 7098 ฯลฯ) ทั้งหมดเป็นป้าย/กระดานที่ผู้เล่นสร้างเอง
                //
                // ผูกกับ "เป็นเจ้าของ" แทน ให้ตรงกับปุ่มรื้อ — คนอื่นเดินมาลบข้อความป้ายเราไม่ได้
                if (blueprint.Components.Contains("Scribble") && (mine || flag))
                {
                    list.Add(Shared.System.Interaction.ScribbleDrawing);
                    list.Add(Shared.System.Interaction.ScribbleText);
                }
                if (blueprint.Components.Contains("Gate") && flag)
                {
                    AppearArtifact? appearArtifact = _world.ArtifactManager.Get(touch.EntityId);
                    if (appearArtifact.HasValue)
                    {
                        list.Add(!appearArtifact.Value.States.GateOpened
                            ? Shared.System.Interaction.OpenGate
                            : Shared.System.Interaction.CloseGate);
                    }
                }
                if (blueprint.Components.Contains("Mannequin") && flag)
                {
                    list.Add(Shared.System.Interaction.ChangeMannequinHead);
                    list.Add(Shared.System.Interaction.ChangeMannequinBody);
                }
                if (KUtility.GetSize(blueprint.Musics) > 0 && flag)
                {
                    AppearArtifact? appearArtifact2 = _world.ArtifactManager.Get(touch.EntityId);
                    if (appearArtifact2.HasValue)
                    {
                        list.Add(!appearArtifact2.Value.Display.Music.HasValue
                            ? Shared.System.Interaction.TurnOnMusic
                            : Shared.System.Interaction.TurnOffMusic);
                    }
                }
                if (RecipeDict.HasDecoration(blueprint.Id) && flag)
                {
                    // 10267 = Interaction.ChangeDecoration (GameCode enum ไม่มีตัวนี้ — ค่าจาก InteractionData ต้นฉบับ)
                    list.Add((Shared.System.Interaction)10267);
                }
                msg.Interactions = list.Select(o => (int)o).ToArray();
            }
            msg.Mannequin = _world.ArtifactManager.GetMannequin(touch.EntityId);
        }
        else if (DataHelper.IsNaturalObject(touch.EntityType))
        {
            // ⚠️ เดิมเชื่อ EntityType/Tile ที่ client ส่งมาล้วน ๆ ⇒ วน Touch ปลอม → Collect
            // เสกไอเทมชนิดไหน เลเวลไหน เท่าไรก็ได้ โดยไม่ต้องใช้คำสั่ง cheat เลย
            //
            // ⚠️⚠️ **ห้ามตรวจด้วย garden ของเซิร์ฟ** — เคยลองแล้วบล็อกการเก็บของที่ถูกต้อง:
            // ของธรรมชาติที่ตัวเกมแสดง (เช่นชนิด 13014/15001/14037 บนเกาะ pe10gr_4)
            // **ไม่มีอยู่ใน whole.garden ของเซิร์ฟเลยสักตัว** — ฝั่งเกมมีของที่มันวาดเองด้วย
            // (แบบเดียวกับสัตว์ประดับที่ terrain วาง ดู Support/AnimalMotions) ⇒ garden ไม่ใช่ความจริงทั้งหมด
            //
            // ⇒ ด่านที่ใช้ได้จริงคือ "ระยะ" — ผู้เล่นต้องยืนใกล้ของที่อ้างว่าแตะ
            // กันการฟาร์มจากอีกฝั่งเกาะได้ โดยไม่ไปบล็อกการเล่นปกติ
            if (!IsWithinTiles(touch.Tile, NaturalReachTiles))
            {
                Console.WriteLine($"[แตะ] ปฏิเสธ {Short(EntityId)}: ช่อง [{touch.Tile.x},{touch.Tile.y}] อยู่ไกลเกินไป");
                Send(new Abort { Text = "อยู่ไกลเกินไป" }, seq);
                return;
            }
            BiomeSpriteInfo biomeSpriteInfo = DataHelper.GetBiomeSpriteInfo(touch.EntityType);
            if (biomeSpriteInfo != null)
            {
                msg.EntityName = biomeSpriteInfo.Name;
            }
            // [5 ก.ย. 2026] ระบบเก็บเกี่ยว — ดู Core/Player.Gathering.cs (จุดเดียวที่ระบบนั้นแตะไฟล์นี้)
            // ⚠️ ตัวที่ทำให้ "กดเก็บได้จริง" คือ msg.Collectible ไม่ใช่ 506:
            //    client/InteractionSystem.cs:628 ส่ง Touched.Collectible เข้า GatheringSystem.SetCollectible
            //    ซึ่ง **ลบเมนู Collect ทุกตัวทิ้งก่อน** แล้วเติมใหม่หนึ่งปุ่มต่อหนึ่ง Generator
            //    (client/GatheringSystem.cs:237-249) ⇒ ส่ง 506 เปล่า ๆ = ไม่มีปุ่มให้กด
            // 506 ยังต้องส่งตามความหมายเดิมของ protocol เผื่อ SetCollectible return ก่อนเพราะ
            // target ไม่ตรง (client/GatheringSystem.cs:202) — กดแล้วไม่มีอะไรเกิดขึ้นแทนที่จะพัง
            // (client/InteractionSystem.cs:486 Gathering(menu.Id) → FindGatheringData(null) → null)
            msg.Collectible = BuildCollectibleFor(touch.EntityId, touch.EntityType, touch.Tile);
            var naturalInteractions = new List<int> { (int)Shared.System.Interaction.Collect };
            if (flag)
            {
                // 10268 = Interaction.RemoveNatural (GameCode enum ไม่มีตัวนี้ — ค่าจาก InteractionData ต้นฉบับ)
                naturalInteractions.Add(10268);
            }
            msg.Interactions = naturalInteractions.ToArray();
        }
        Send(msg, seq);
        OnContextChanged();
    }

    /// <summary>
    /// ระยะไกลสุด (ช่อง) ที่ยังยุ่งกับสิ่งปลูกสร้างได้ — บวกขนาดของหลังนั้นเข้าไปอีกที
    ///
    /// **ค่าของเรา** — ข้อมูลเกมไม่มีตัวเลขนี้ ตั้ง 6 ช่องเพราะฝั่งเกมเดินเข้าไปหาก่อนเสมอ
    /// (client/BuildSystem.cs:530 MoveToPosition(pos, …, 141f) แล้วค่อยยิงคำสั่ง)
    /// ⇒ ผู้เล่นปกติอยู่ใกล้กว่านี้มาก · ด่านนี้กันสคริปต์ที่ยิงจากอีกฝั่งเกาะ
    /// </summary>
    private const int ArtifactReachTiles = 6;

    /// <summary>
    /// ระยะไกลสุด (ช่อง) ที่ยังแตะของธรรมชาติได้
    ///
    /// **ค่าของเรา** — ตั้ง 8 ช่องเพราะฝั่งเกมเดินเข้าไปหาก่อนแตะเสมอ และ Player.Hunting.cs
    /// ใช้ระยะใกล้เคียงกันกับการกัดสัตว์ · เผื่อไว้กว้างหน่อยเพราะของบางชนิดมีพื้นที่กว้าง
    /// </summary>
    private const int NaturalReachTiles = 8;

    /// <summary>
    /// ผู้เล่นคนนี้มีสิทธิ์ยุ่งกับสิ่งปลูกสร้างหลังนี้ไหม (เจ้าของ + อยู่ใกล้พอ)
    ///
    /// ⚠️ ไม่มีด่านนี้ = ผู้เล่นคนเดียวเขียนสคริปต์วน entity id ที่ได้ฟรีจากแพ็กเก็ต AppearArtifact
    /// แล้วรื้อสิ่งปลูกสร้างทั้งเกาะ/ขนของออกจากตู้คนอื่นได้จากทั่วเกาะ
    ///
    /// ของที่ไม่มีเจ้าของ (ท่าเรือ/รูวาร์ปที่เซิร์ฟวางเอง · ของเก่าก่อนมีระบบเจ้าของ)
    /// **ยุ่งไม่ได้ทั้งคู่** — ปลอดภัยกว่าปล่อยให้ใครก็รื้อ
    /// </summary>
    private bool MayTouchArtifact(string artifactEntityId, string what)
    {
        AppearArtifact? found = _world.ArtifactManager.Get(artifactEntityId);
        if (!found.HasValue)
        {
            Console.WriteLine($"[สิทธิ์] {Short(EntityId)} {what}: ไม่มีสิ่งปลูกสร้าง {artifactEntityId}");
            return false;
        }

        string owner = _world.ArtifactManager.OwnerOf(artifactEntityId);
        if (!string.Equals(owner, EntityId, StringComparison.Ordinal))
        {
            Console.WriteLine($"[สิทธิ์] {Short(EntityId)} {what} {artifactEntityId} ไม่ได้ — " +
                              (string.IsNullOrEmpty(owner) ? "ของนี้ไม่มีเจ้าของ" : $"เจ้าของคือ {Short(owner)}"));
            return false;
        }

        // ใช้ตัวเดียวกับที่ระบบล่าสัตว์ใช้ (Player.Hunting.cs) — พิสูจน์แล้วว่าอ่านตำแหน่งถูก
        // วัดจากมุมของ footprint แล้วบวกขนาดหลังเข้าไป เพื่อไม่ให้บ้านหลังใหญ่โดนตัดสิทธิ์
        AppearArtifact artifact = found.Value;
        int reach = ArtifactReachTiles + Math.Max(artifact.Size.x, artifact.Size.y);
        if (!IsWithinTiles(artifact.Tile, reach))
        {
            Console.WriteLine($"[สิทธิ์] {Short(EntityId)} {what} {artifactEntityId} ไม่ได้ — อยู่ไกลเกินไป");
            return false;
        }
        return true;
    }

    /// <summary>
    /// ผู้เล่นคนนี้เป็นผู้ดูแลไหม — รายชื่อจาก <c>--admins</c> หรือ env <c>DURANGO_ADMINS</c>
    /// (คั่นด้วยจุลภาค ใส่ entity id ของตัวละคร)
    ///
    /// ⚠️ ค่าตั้งต้นคือ **ไม่มีใครเป็นแอดมิน** ⇒ คำสั่ง cheat ปิดสนิท
    /// ไม่ตั้ง = ปลอดภัย · ตั้งผิด = แค่ใช้คำสั่งไม่ได้ ไม่ได้เปิดช่องให้ใคร
    /// </summary>
    private bool IsAdmin => Admins.Contains(EntityId);

    /// <summary>รายชื่อ entity id ของผู้ดูแล — Program ตั้งให้ตอนบูต</summary>
    public static readonly HashSet<string> Admins = new(StringComparer.Ordinal);

    private static string Short(string id) =>
        string.IsNullOrEmpty(id) ? "(ว่าง)" : id[..Math.Min(8, id.Length)];

    /// <summary>เนมสเปซคีย์ที่ **เซิร์ฟเป็นเจ้าของ** — client เขียนไม่ได้</summary>
    private const string ServerStoragePrefix = "server_";

    /// <summary>
    /// เพดานขนาดของช่องเก็บของ client (ไบต์) — **ค่าของเรา**
    ///
    /// ข้อมูลที่ client เก็บจริงเป็นของเล็ก ๆ (ช่องแชทที่เปิดไว้ · อิโมติคอนที่ปักหมุด ·
    /// ตำแหน่งที่ชอบ · สมุดบันทึก) รวมกันไม่ถึงหลักสิบ KB ⇒ 256 KB ต่อคีย์ / 2 MB รวม เหลือเฟือ
    /// ไม่จำกัด = ยิง SetStorageItem รัว ๆ ทำ RAM เซิร์ฟหมดและไฟล์เซฟบวมจนเขียนไม่ไหว
    /// </summary>
    private const int MaxStorageValueBytes = 256 * 1024;
    private const int MaxStorageTotalBytes = 2 * 1024 * 1024;

    /// <summary>
    /// client ขอเก็บค่าลงช่องเก็บของตัวเอง (การตั้งค่า UI · สมุดบันทึก · อิโมติคอน)
    ///
    /// ⚠️ เดิมเขียนทับได้ **ทุกคีย์** รวมคีย์ที่เซิร์ฟใช้เก็บสถานะจริง
    /// (<c>server_skills</c> — Player.Skills.cs:119) ⇒ client ที่ถูกแก้ตั้งเลเวล/สกิลของตัวเองได้
    /// **แม้จะปิดคำสั่ง cheat ไปแล้ว** เพราะเส้นนี้ไม่ใช่ cheat แต่เป็นช่องเก็บของปกติ
    ///
    /// วิธีกัน: จองเนมสเปซ <c>server_</c> ไว้ให้เซิร์ฟฝ่ายเดียว (เผื่อคีย์ใหม่ในอนาคตด้วย)
    /// ส่วนคีย์อื่นปล่อยให้เขียนได้ตามเดิม เพราะฝั่งเกมใช้เก็บของจริง เช่น
    /// <c>encyclopedia</c> (client/MemoSystem.cs:186) · ช่องแชท · อิโมติคอน
    /// </summary>
    private void HandleSetStorageItemMsg(SetStorageItem msg)
    {
        if (string.IsNullOrEmpty(msg.Key)) return;

        if (msg.Key.StartsWith(ServerStoragePrefix, StringComparison.Ordinal))
        {
            Console.WriteLine($"[เก็บของ] ปฏิเสธ {Short(EntityId)}: คีย์ '{msg.Key}' เป็นของเซิร์ฟ");
            return;
        }

        int size = msg.Value?.Length ?? 0;
        if (size > MaxStorageValueBytes)
        {
            Console.WriteLine($"[เก็บของ] ปฏิเสธ {Short(EntityId)}: คีย์ '{msg.Key}' ใหญ่ {size} ไบต์");
            return;
        }

        _context.Storage ??= new Dictionary<string, byte[]>();
        int total = size;
        foreach (KeyValuePair<string, byte[]> pair in _context.Storage)
        {
            if (pair.Key == msg.Key) continue;              // ตัวที่กำลังจะเขียนทับ ไม่นับของเดิม
            total += pair.Value?.Length ?? 0;
        }
        if (total > MaxStorageTotalBytes)
        {
            Console.WriteLine($"[เก็บของ] ปฏิเสธ {Short(EntityId)}: รวมแล้วเกินเพดาน ({total} ไบต์)");
            return;
        }

        _context.Storage[msg.Key] = msg.Value;
        OnContextChanged();
    }

    private Action<ArtifactDisplay> _onArtifactDisplayUpdated;
    private Action<ArtifactState> _onArtifactStateUpdated;
    private Action<Point2, byte[]> _onNaturalAdded;
    private Action<Point2> _onNaturalDestroyed;
    private bool _detached;

    /// <summary>
    /// ถอดตัวเองออกจาก event ของโลกทั้งหมด — **ตัวที่กันหน่วยความจำรั่ว**
    ///
    /// ⚠️ อาการเดิม: ผู้เล่น subscribe event ของ World 8 ตัวตอนสร้าง แล้ว**ไม่เคยถอดเลย**
    /// World อยู่ยาวกว่าผู้เล่นมาก ⇒ ทุกครั้งที่ใครต่อเข้ามาแล้วหลุด Player ตัวนั้นยังถูก World
    /// ถือไว้ผ่าน delegate ⇒ <c>Connection</c> ที่พ่วงบัฟเฟอร์ ~4 MB ไม่ถูกคืนสักไบต์
    ///
    /// เกมมือถือหลุด/กลับเข้าบ่อยมาก — 50 คน × reconnect 10 ครั้ง/วัน = **~2 GB/วัน**
    /// จนกว่าเซิร์ฟจะถูกรีสตาร์ต
    ///
    /// เรียกได้ซ้ำโดยไม่พัง (ปิดคอนเนกชันกับถูกถอดออกจากโลกอาจเกิดคนละจังหวะ)
    /// </summary>
    /// <summary>ชื่อตัวละคร — หน้าแอดมินใช้แสดงว่ากำลังจะเตะใคร</summary>
    public string Name => _context?.PlayerInfo?.PlayerName;

    /// <summary>
    /// เตะออกจากเกมพร้อมบอกเหตุผล
    ///
    /// ส่ง <c>Abort</c> ก่อนปิดสาย เพื่อให้ฝั่งเกมขึ้นข้อความแทนที่จะค้างแล้วหลุดเงียบ ๆ
    /// (client รับ Abort เป็นข้อความระบบทั่วไป — เส้นทางเดียวกับที่เซิร์ฟใช้ปฏิเสธคำสั่งอื่น)
    /// </summary>
    public void KickWith(string reason)
    {
        try { Send(new Abort { Text = reason }); } catch (Exception) { }
        try { _connection.Close(); } catch (Exception) { }
    }

    /// <summary>ประกาศจากผู้ดูแลถึงผู้เล่นคนนี้</summary>
    public void SendNotice(string text)
    {
        try { Send(new Info { Text = text }); } catch (Exception) { }
    }

    public void Detach()
    {
        if (_detached) return;
        _detached = true;

        _world.ArtifactAppeared -= World_ArtifactAppeared;
        _world.ArtifactDisappeared -= World_ArtifactDisappeared;
        _world.PlayerAppeared -= World_PlayerAppeared;
        _world.PlayerDisappeared -= World_PlayerDisappeared;
        if (_onArtifactDisplayUpdated != null) _world.ArtifactManager.ArtifactDisplayUpdated -= _onArtifactDisplayUpdated;
        if (_onArtifactStateUpdated != null) _world.ArtifactManager.ArtifactStateUpdated -= _onArtifactStateUpdated;
        if (_onNaturalAdded != null) _world.NaturalAdded -= _onNaturalAdded;
        if (_onNaturalDestroyed != null) _world.NaturalDestroyed -= _onNaturalDestroyed;
    }

    /// <summary>
    /// รื้อสิ่งปลูกสร้าง — ปฏิเสธถ้ายังมีของในตู้ (ของจะหายถาวร ดู Player.Inventory.HasStoredItems)
    ///
    /// ═══ ทำไมต้องหน่วงเวลา ═══
    /// ต้นฉบับเกม (client/Durango.Logic.Interactions/ArtifactInteractions.cs:309) ยิง DestructArtifact
    /// แล้ว **รอ reply <c>Destructing</c>(2007) {Duration, ToolType}** เพื่อเล่นหลอดความคืบหน้า + ท่าทุบ
    /// (OnDestructedReplied:433 — Onehand/Twohand/Barehand_Destroy) เดิมเซิร์ฟลบทันทีไม่ตอบ ⇒ หลัง
    /// หายวับ ไม่มีหลอด ไม่มีท่า ไม่เสียพลังงานเลย
    ///
    /// ═══ ค่าทั้งหมดจากข้อมูลจริง ═══
    /// <code>
    /// constants.json → build → destruct : energy = "10 + durability / 2."  time = "5 + durability / 10."
    ///                        → default_durability = 7 · default_time_limited_durability = 60
    /// durability ของหลัง = time_limited ? 60 : 7  (entity_types/artifact.json → time_limited)
    /// ToolType = weapon_framework ของเครื่องมือที่ถือ (performance.json) — onehand→1 twohand→2 อื่น→0
    /// </code>
    /// ⚠️ **การตีความ (บอกตรง ๆ):** บล็อก destruct ในไฟล์ระบุแค่ energy — ไม่ได้บอกว่าคิด fatigue ด้วย
    ///    เลือกหัก fatigue หมวด "build" (SpendBuildEnergy) เพราะ destruct อยู่ใต้คีย์ <c>build</c> และให้เข้าชุด
    ///    กับระบบความเหนื่อยต่อการกระทำ (การก่อสร้างทุกแบบเหนื่อย) — ถ้าไม่เอา ลบ SpendBuildEnergy เหลือหัก energy อย่างเดียวได้
    /// </summary>
    private void HandleDestructMsg(DestructArtifact msg, uint seq)
    {
        if (!MayTouchArtifact(msg.EntityId, "รื้อ")) return;
        // ⚠️ [6 ก.ย. 2026] ของในตู้ไม่ได้ถูกลบไปกับหลัง แต่จะหายจากไฟล์ในรอบเซฟถัดไป
        // แล้วดึงคืนไม่ได้ (เหตุผลเต็มที่ Player.Inventory.HasStoredItems)
        if (HasStoredItems(msg.EntityId))
        {
            Console.WriteLine($"[รื้อ] ปฏิเสธ {Short(EntityId)}: {msg.EntityId} ยังมีของในตู้");
            Send(new Abort { Text = "ต้องเอาของออกจากตู้ก่อนรื้อ" });
            return;
        }

        // durability ของหลัง — time_limited?60:7 (ไฟล์ไม่มี durability รายหลัง เหมือน BuildEstimation)
        float durability = BuildTuning.DefaultDurability;
        if (_world.ArtifactManager.Get(msg.EntityId) is { } artifact &&
            SingletonDict<int, Yaml.ArtifactPrototype>.TryGetValue(artifact.EntityType, out Yaml.ArtifactPrototype proto) &&
            proto.time_limited)
        {
            durability = BuildTuning.DefaultTimeLimitedDurability;
        }

        float duration = (float)Math.Max(0.0, BuildTuning.DestructTime(durability));
        float energy = (float)Math.Max(0.0, BuildTuning.DestructEnergy(durability));

        // หักพลังงาน+ความเหนื่อยก่อนตอบ (พลังงานไม่พอก็ยังรื้อได้ — ของจริงไม่บล็อก แค่หลอดลด)
        SpendBuildEnergy(energy);

        // ตอบ Destructing ที่ seq เดิม → client เล่นหลอด+ท่าตาม Duration/ToolType (reply ใบเดียว client auto-clean)
        Send(new Destructing { Duration = duration, ToolType = EquippedToolType() }, seq);

        // หน่วงตาม Duration แล้วค่อยลบจริง — ใช้คิวของ World (drain ใน Process บน main-thread)
        // ไม่ใช้ System.Threading.Timer เพราะ callback รัน pool thread แล้วแตะ world = race กับลูปหลัก
        if (duration <= 0.05f)
        {
            _world.DestructArtifact(msg.EntityId);
        }
        else
        {
            _world.ScheduleDestruct(msg.EntityId, duration);
        }
    }

    /// <summary>
    /// ชนิดท่าทุบของ <c>Destructing.ToolType</c> — อ่านจาก weapon_framework ของเครื่องมือที่ถืออยู่
    /// (performance.json) 0 = มือเปล่า/เครื่องมืออื่น (Barehand_Destroy) · 1 = onehand · 2 = twohand
    /// </summary>
    private byte EquippedToolType()
    {
        foreach (var pair in _context.EquippedItems)
        {
            int index = _context.InventoryItems.FindIndex(item => item.Id == pair.Value);
            if (index < 0) continue;
            string fw = PerformanceYaml.GetWeapon(_context.InventoryItems[index].Prototype)?.WeaponFramework;
            if (string.IsNullOrEmpty(fw)) continue;
            if (fw.Equals("onehand", StringComparison.OrdinalIgnoreCase)) return 1;
            if (fw.Equals("twohand", StringComparison.OrdinalIgnoreCase)) return 2;
        }
        return 0;
    }

    private void HandleDumpItemsMsg(DumpItems msg)
    {
        _context.InventoryItems.RemoveAll(o => msg.ItemIds.Any(p => p == o.Id));
        Send(new InventoryUpdated
        {
            EntityId = EntityId,
            RemovedItemIds = msg.ItemIds
        });
        OnContextChanged();
    }

    private void HandleGetAddOnsMsg(GetAddOns msg, uint seq)
    {
        Send(_world.ArtifactManager.GetAddons(msg.EntityId), seq);
    }

    private void HandlePlaceAddOnsMsg(PlaceAddOns msg, uint seq)
    {
        var dictionary = new Dictionary<int, Item>();
        AddOns addons = _world.ArtifactManager.GetAddons(msg.EntityId);
        foreach (var pair in msg.AddOnPlacements)
        {
            Item? item = null;
            int num = _context.InventoryItems.FindIndex(o => o.Id == pair.Value);
            if (num == -1)
            {
                if (addons._AddOns != null)
                {
                    foreach (var addOn in addons._AddOns)
                    {
                        if (pair.Value == addOn.Value.Id)
                        {
                            item = addOn.Value;
                            break;
                        }
                    }
                }
            }
            else
            {
                item = _context.InventoryItems[num];
            }
            if (item.HasValue)
            {
                dictionary.Add(pair.Key, item.Value);
            }
        }
        if (_world.ArtifactManager.PlaceAddOns(msg.EntityId, dictionary).HasValue)
        {
            Send(_world.ArtifactManager.GetAddons(msg.EntityId), seq);
        }
    }

    private void HandlePlantSeedMsg(PlantSeed msg)
    {
        foreach (Item inventoryItem in _context.InventoryItems)
        {
            if (inventoryItem.Id != msg.SeedItemId) continue;

            // เลเวลของเมล็ดคุมเวลาปลูก (crops.json → grows_until เป็นสูตรของ level)
            // และไบโอมของช่องคุมความเหมาะสมของภูมิอากาศบนป้ายข้อมูล
            Point2 tile = _world.ArtifactManager.Get(msg.EntityId)?.Tile ?? default;
            _world.ArtifactManager.SeedPlant(msg.EntityId, inventoryItem.Prototype,
                inventoryItem.Level, _world.BiomeAt(tile));

            // [7 ก.ย. 2026] exp หมวดเกษตร — ⚠️ ก่อนหน้านี้ไม่มีจุดไหนให้ exp หมวดนี้เลย
            // ⇒ ปลูกทั้งวันหมวด Farming ค้างที่เลเวล 1 ตลอดกาล และไม่ได้ exp ตัวละครด้วย
            AddExpForAction(SkillTuning.GatherWeight, Shared.Skill.Category.Farming, "ปลูกพืช");
            NoteQuestEvent(Shared.Quest.QuestEventType.Farmed);
            break;
        }
    }

    private void HandleChargeEffectMsg(ChargeEffect msg, uint seq)
    {
        _world.ArtifactManager.ChargeEffect(msg.EntityId);
        Send(default(OK), seq);
    }

    private void HandleScribbleMsg(Scribble msg)
    {
        _world.ArtifactManager.Scribble(msg);
    }

    private void HandleChangeDecorationMsg(Messages.Display msg)
    {
        _world.ArtifactManager.ChangeDecoration(msg.EntityId);
    }

    private void HandleEquipMsg(Equip msg, uint headerSeq)
    {
        if (msg.Action == "equip")
        {
            int num = _context.InventoryItems.FindIndex(x => x.Id == msg.ItemId);
            if (num < 0) return;
            _context.EquippedItems[msg.SlotName] = msg.ItemId;
        }
        else if (!_context.EquippedItems.Remove(msg.SlotName))
        {
            return;
        }
        SendEquipments(headerSeq);
        _world.BroadCast(_context.AppearPlayer.Display);
        OnContextChanged();
    }

    private void HandleSearchProductsMsg(SearchProducts msg, uint seq)
    {
        Products msg2 = _world.MarketManager.SearchProduct(msg);
        Send(msg2, seq);
    }

    private void HandleBuyProductMsg(BuyProduct msg, uint seq)
    {
        Item[] array = _world.MarketManager.BuyProduct(msg.ProductId);
        if (array == null)
        {
            Send(default(Messages.Error), seq);
            return;
        }
        InventoryUpdated msg2 = new()
        {
            EntityId = EntityId,
            Items = array
        };
        AddItems(array);
        Send(msg2);
        Send(default(OK), seq);
    }

    private void HandleGetArtifactBlueprintsMsg(GetArtifactBlueprints msg, uint seq)
    {
        // [8 ก.ย. 2026] กรองด้วยสิ่งปลูกสร้างที่ "ปลดล็อกแล้ว" — เดิมส่งทุกหลัง is_craft
        // = โหมดครีเอทีฟ (สร้างได้ทุกอย่างตั้งแต่ตัวแรก) ต้องกรองให้ตรงกับสูตรไอเทม (SendRecipes)
        HashSet<string> unlocked = UnlockedBlueprintIds();
        var list = new List<string>();
        foreach (MergedBlueprint allBlueprint in BlueprintStore.GetAllBlueprints())
        {
            if (allBlueprint.IsShowCraftMode && unlocked.Contains(allBlueprint.Id))
            {
                list.Add(allBlueprint.Id);
            }
        }
        Console.WriteLine($"[craft] {Short(EntityId)} ส่งบลูปริ้นท์ที่ปลดแล้ว {list.Count} หลัง");
        Send(new ArtifactBlueprints { Ids = list.ToArray() }, seq);
    }

    private void HandleExtendFloorMsg(ExtendFloor msg)
    {
        _world.ExtendFloor(msg.EntityId, msg.WithRoof);
    }

    private void HandleGetMusicsMsg(GetMusics msg, uint seq)
    {
        Send(new Musics { _Musics = _context.Musics }, seq);
    }

    private void HandleSaveMusicToSlotMsg(SaveMusicToSlot msg, uint seq)
    {
        Dictionary<int, Music> dictionary = _context.Musics;
        if (dictionary == null)
        {
            dictionary = new Dictionary<int, Music>();
            _context.Musics = dictionary;
        }
        dictionary[msg.Slot] = msg.Music;
        Send(default(OK), seq);
        OnContextChanged();
    }

    private void HandleRemoveMusicFromSlotMsg(RemoveMusicFromSlot msg, uint seq)
    {
        Dictionary<int, Music> musics = _context.Musics;
        if (musics != null && musics.Remove(msg.Slot))
        {
            Send(default(OK), seq);
            OnContextChanged();
        }
        else
        {
            // ห้ามส่ง Abort ที่ไม่มีข้อความ (ฝั่งเกม NRE — ดูเหตุผลเต็มที่จุดแรกในไฟล์นี้)
            Send(new Abort { Text = "ทำรายการนี้ไม่ได้" }, seq);
        }
    }

    private void HandlePlayMusicMsg(PlayMusic msg)
    {
        Musician msg2 = new()
        {
            EntityId = EntityId
        };
        Dictionary<int, Music> musics = _context.Musics;
        if (musics == null || !musics.TryGetValue(msg.Slot, out var value)) return;
        msg2.Music = value;
        int num = _context.InventoryItems.FindIndex(item2 => item2.Id == msg.InstrumentItemId);
        if (num == -1) return;
        Item item = _context.InventoryItems[num];
        string text = null;
        if (item.Performance != null)
        {
            Performance performance = item.Performance.FirstOrDefault(p => p.Id == "instrument");
            if (performance.Strs != null)
            {
                text = performance.Strs.Get("timbre");
            }
        }
        if (!string.IsNullOrEmpty(text))
        {
            msg2.Timbre = text;
            msg2.PlayedAt = Times.UnixTimeNow();
            _world.BroadCast(msg2);
        }
    }

    private void HandleStopMusicMsg(StopMusic msg)
    {
        _world.BroadCast(new Musician { EntityId = EntityId });
    }

    private void HandleArtifactDisplayMsg(ArtifactDisplay msg)
    {
        _world.ArtifactManager.UpdateArtifactDisplay(msg);
    }

    private void SendInventory()
    {
        Inventory msg = default;
        msg.EntityId = EntityId;
        msg.InventoryInfos.EntityId = EntityId;
        msg.InventoryItems.EntityId = EntityId;
        msg.InventoryInfos.MaxSize = 200;
        msg.InventoryItems.Items = _context.InventoryItems.ToArray();
        // [7 ก.ย. 2026] ⚠️ เส้นนี้คือ inventory "ของตัวผู้เล่นเอง" ที่ส่งตอนเข้าเกม
        // เดิมไม่เคยใส่ Wallet เลย ⇒ ฝั่งเกมได้ค่า default (null) แล้วยอดเงินเป็น 0 ตลอด
        // ถึงจะแก้เส้นตู้/คลังใน Core/Player.Inventory.cs แล้วก็ไม่พอ เพราะคนละเส้นกัน
        // (ตัวจริงที่ทำให้ "ไม่มีเงินในตัวละครเลย" คือบรรทัดนี้)
        msg.Wallet = BuildWallet();
        Send(msg);
    }

    private Equipments UpdateEquipments()
    {
        Equipments result = new()
        {
            CurrentType = EquipSlotType.Slot1
        };
        EquipmentSlot value = default;
        value.IsLocked = false;
        PlayerDisplay display = _context.AppearPlayer.Display;
        display.Body = display.DefaultBody;
        display.Head = null;
        display.BodyColor = new[] { "FFFFFF", "FFFFFF", "FFFFFF" };
        display.WeaponInfo = default;
        display.Equip = null;
        display.EquipColor = null;
        var dictionary = new Dictionary<string, Item>();
        foreach (var pair in _context.EquippedItems)
        {
            int num = _context.InventoryItems.FindIndex(x => x.Id == pair.Value);
            if (num < 0) continue;
            Item value2 = _context.InventoryItems[num];
            dictionary[pair.Key] = value2;
            string[] array = { value2.ColorR, value2.ColorG, value2.ColorB };
            PerformanceYaml.Weapon weapon = PerformanceYaml.GetWeapon(value2.Prototype);
            if (weapon != null)
            {
                display.WeaponInfo = new WeaponDisplayInfo
                {
                    WeaponFramework = weapon.WeaponFramework,
                    // ไม่ส่งสองตัวนี้ = ธนูยิงแล้วไม่มีลูกศรเลย (ดูหมายเหตุที่ PerformanceYaml.Weapon)
                    Projectile = weapon.Projectile,
                    ProjectileSpeed = weapon.ProjectileSpeed
                };
                display.Equip = weapon.Model;
                display.EquipColor = array;
            }
            bool flag = _context.AppearPlayer.IsMale();
            PerformanceYaml.Armor armor = PerformanceYaml.GetArmor(value2.Prototype);
            if (armor != null)
            {
                if (armor.Slot == "body")
                {
                    display.Body = !flag ? armor.FemaleModel : armor.MaleModel;
                    display.BodyColor = array;
                }
                else if (armor.Slot == "head")
                {
                    display.Head = !flag ? armor.FemaleModel : armor.MaleModel;
                    display.HeadColor = array;
                }
            }
        }
        _context.AppearPlayer.Display = display;
        value.ItemSlots = dictionary;
        value.UnlockSince = null;
        value.UnlockUntil = null;
        value.TitleId = string.Empty;
        result.Presets = new Dictionary<EquipSlotType, EquipmentSlot> { { EquipSlotType.Slot1, value } };
        return result;
    }

    private void SendEquipments(uint replyOf = 0u)
    {
        Send(UpdateEquipments(), replyOf);
        SendBaseMoveSpeed();
    }

    /// <summary>
    /// ความเร็วเดินโหมดปกติ/โหมดต่อสู้ — ต้องส่งใหม่ทุกครั้งที่เปลี่ยนอาวุธ
    ///
    /// ⚠️ **ไม่ส่งเลย = โหมดต่อสู้วิ่งเร็วเท่าเดินปกติ** ฝั่งเกมเก็บค่านี้เป็น nullable
    /// (client/PlayerController.cs:406-409) แล้วที่ :100-106 เขียนว่า
    /// <c>ไม่มีค่า ⇒ ใช้ 500 ทั้งสองโหมด</c> ⇒ ค่า BattleSpeed ไม่เคยถูกใช้
    /// ⇒ ชักอาวุธแล้วไม่มีอาการเดินช้าลง และอาวุธหนัก/เบาให้ความเร็วเท่ากันหมด
    ///
    /// ค่าจากข้อมูลจริงทั้งคู่: <c>players.json → player.moving.default_normal_speed</c> = 500
    /// และ <c>performance.json → weapon.&lt;id&gt;.battle_speed</c> (300/350/400 ตามชนิดอาวุธ)
    /// มือเปล่าใช้ <c>players.json → player.bare_hands.battle_speed</c> = 350
    /// </summary>
    private void SendBaseMoveSpeed()
    {
        float battle = DefaultBareHandsBattleSpeed;   // players.json → player.bare_hands.battle_speed
        foreach (var pair in _context.EquippedItems)
        {
            int index = _context.InventoryItems.FindIndex(item => item.Id == pair.Value);
            if (index < 0) continue;
            PerformanceYaml.Weapon weapon = PerformanceYaml.GetWeapon(_context.InventoryItems[index].Prototype);
            if (weapon?.BattleSpeed is > 0f) { battle = weapon.BattleSpeed.Value; break; }
        }

        // [7 ก.ย. 2026] ความเหนื่อยมีผลเดินช้า — ค่าเป็น **ค่าของเรา**
        // ใช้เกณฑ์ FatigueCaution/FatigueDanger ที่ส่งให้ client อยู่แล้ว
        // ต่ำกว่า caution = ปกติ · ระหว่าง caution→danger = ค่อย ๆ ช้า · ถึง danger = ช้าสุด
        float fatigueMul = FatigueMoveMultiplier();
        int normal = Math.Max(1, (int)Math.Round(DefaultNormalSpeed * fatigueMul));
        int battleSpeed = Math.Max(1, (int)Math.Round(battle * fatigueMul));

        // ไม่ยิงซ้ำถ้าค่าไม่เปลี่ยน — กันสแปมทุกเฟรมตอน Process
        if (_lastSentNormalSpeed == normal && _lastSentBattleSpeed == battleSpeed)
        {
            return;
        }
        _lastSentNormalSpeed = normal;
        _lastSentBattleSpeed = battleSpeed;

        Send(new SetBaseMoveSpeed
        {
            EntityId = EntityId,
            NormalSpeed = normal,
            BattleSpeed = battleSpeed
        });
    }

    /// <summary>
    /// ตัวคูณความเร็วจากความเหนื่อย — **ค่าของเรา**
    /// 0%..Caution → 1.0 · Danger → 0.55 · เกิน Danger → 0.40
    /// </summary>
    private float FatigueMoveMultiplier()
    {
        float fatigue = _survival.ValueAt(SurvivalState.KeyFatigue, Gauge.CurrentTime);
        float caution = SurvivalTuning.FatigueCaution;
        float danger = SurvivalTuning.FatigueDanger;
        if (fatigue <= caution) return 1f;
        if (fatigue >= danger) return 0.40f;
        float t = (fatigue - caution) / Math.Max(1f, danger - caution);
        return 1f - (0.45f * t); // caution→danger: 1.0 → 0.55
    }

    /// <summary>ค่าสำรองตรงกับ players.json เป๊ะ — มีไว้กันไฟล์หาย ไม่ใช่ค่าที่คิดเอง</summary>
    private const float DefaultNormalSpeed = 500f;
    private const float DefaultBareHandsBattleSpeed = 350f;
    private int _lastSentNormalSpeed = -1;
    private int _lastSentBattleSpeed = -1;

    public void Process()
    {
        _connection.Process();
        // หมดอายุก่อน แล้วค่อยใส่คืนจากฝน/น้ำที่ยังอยู่ — ส่งชุดเดียว จะได้ไม่กระพริบไอคอน
        bool statusChanged = ExpireTimedStatusEffects();
        statusChanged |= SyncWorldDrivenStatusEffects();
        if (statusChanged)
        {
            SendStatusEffects();
        }
        // ต้องอยู่ "หลัง" ชุดสถานะ — ผล type=2 ของบัพเป็นตัวคูณลดความเหนื่อยรายหมวด
        // ใส่ก่อนบัพอัปเดตจะได้เลขของเฟรมที่แล้ว (เดินเข้าบ้านแล้วยังเหนื่อยเท่าเดิมอีกเฟรม)
        SyncFatigueVelocities();
        UpdateSurvival();
        // รีเฟรชความเร็วตามความเหนื่อยเป็นระยะ (ไม่ทุกเฟรม — SendBaseMoveSpeed กันค่าซ้ำเอง)
        SendBaseMoveSpeed();
    }

    /// <summary>
    /// [5 ก.ย. 2026] รอบตรวจหลอดสถานะ — ถูกเรียกทุกเฟรม (120 ครั้ง/วินาที ดู Program.cs:159-181)
    /// แต่แทบไม่ส่งอะไรออกไป เพราะ Gauge เป็นเส้นแนวโน้มที่ client เดินเองอยู่แล้ว
    /// SurvivalState.Tick จะคืน true เฉพาะตอนความชันเปลี่ยนหรือเส้นเดิมใกล้หมด
    /// </summary>
    private void UpdateSurvival()
    {
        double now = Gauge.CurrentTime;
        // เกมไม่มี message "หยุดเดิน" ⇒ ถือว่าหยุดเมื่อไม่ขยับนานเกิน MoveIdleTimeout
        _survival.SetMoving(now - _lastMovedAt < SurvivalTuning.MoveIdleTimeout);
        if (_survival.Tick(now, out SurvivalUpdated msg))
        {
            // ⚠️ ต้องกระจายให้ทุกคนบนเกาะ ไม่ใช่ส่งให้เจ้าตัวคนเดียว
            // หลอดเลือดของ entity อื่นบนจอมีที่มาเดียวคือ Survival(182)/SurvivalUpdated(183)
            // (client/ObjectManager.cs:47-87) ⇒ ส่งเฉพาะเจ้าตัว = คนอื่นเห็นหลอดค้างนิ่ง
            // จนกว่าจะตาย แล้วกระโดดเป็น 0 ทันที
            _world.BroadCast(msg);
        }
    }

    /// <summary>ส่งเส้นหลอดชุดใหม่เดี๋ยวนี้ — ใช้ตอนค่า/ความชันกระโดดแบบไม่ต่อเนื่อง (พัก/กิน/โดนตี)</summary>
    private void FlushSurvival()
    {
        _world.BroadCast(_survival.Flush(Gauge.CurrentTime));   // เหตุผลเดียวกับ UpdateSurvival
    }

    public void Stop()
    {
        _connection.Close();
    }

    /// <summary>
    /// ตอบว่า "จากท่าเรือนี้ล่องเรือไปเกาะไหนได้บ้าง" — Routes (2032)
    ///
    /// รูปแบบที่เกมต้องการ: <c>Dictionary&lt;Role, Dictionary&lt;templateId, Route[]&gt;&gt;</c>
    /// เกมวนอ่านทีละ template แล้วเช็คกับตารางในตัวเอง
    /// (client/ExploreSystem.cs:307 — <c>SingletonDict&lt;string, RegionTemplate&gt;.Get(templateId)</c>
    /// ถ้าไม่รู้จัก template นั้นจะข้ามทิ้งทั้งกลุ่ม) ⇒ TemplateId ต้องมีใน
    /// data/assets/region_templates.json เท่านั้น เกาะที่ generate เองต้องตั้งให้ตรงด้วย
    ///
    /// ราคา: <c>Route.Price = null</c> = ฟรี (client/Durango.UI/ExploreGroup.cs:222
    /// ตีความ null เป็น Money.ForFree แล้วข้ามหน้าจ่ายเงินไปเลย) — ค่าเดินเรือเป็นเรื่องของ
    /// ระบบเศรษฐกิจซึ่งยังไม่ได้ทำ จึงให้ฟรีไปก่อนแทนที่จะตั้งตัวเลขเดาเอง
    /// </summary>
    private void HandleGetRoutesMsg(uint seq)
    {
        // จัดเกาะเข้ากลุ่มตาม Role ของ template จริง ไม่ใช่ Role เดียวทั้งหมด —
        // client/Durango.UI/WorldRoutesUnstableArea.cs:236 จับคู่โซนด้วย Role+Level+MajorBiome
        // ของ template ⇒ ส่ง Role ที่ไม่ตรง เกาะจะไม่ไปโผล่ในโซนไหนเลย
        var byRole = new Dictionary<Role, Dictionary<string, List<Route>>>();
        var byArchipelago = new Dictionary<string, (RegionCatalog.TemplateInfo Template, List<Route> Routes)>();

        foreach (Messages.Region region in RegionCatalog.Others(_world.TerrainId))
        {
            RegionCatalog.TemplateInfo template = RegionCatalog.GetTemplate(region.TemplateId);
            if (template == null)
            {
                continue;
            }
            var route = new Route { RegionId = region.Id, Price = null };   // null = ฟรี (ยังไม่มีระบบเงิน)

            if (!byRole.TryGetValue(template.Role, out Dictionary<string, List<Route>> byTemplate))
            {
                byTemplate = new Dictionary<string, List<Route>>();
                byRole[template.Role] = byTemplate;
            }
            if (!byTemplate.TryGetValue(region.TemplateId, out List<Route> list))
            {
                list = new List<Route>();
                byTemplate[region.TemplateId] = list;
            }
            list.Add(route);

            // หมู่เกาะ = กลุ่มของเกาะที่ระดับ+ไบโอมเดียวกัน
            // เกาะแบบ Risky ต้องมี ArchipelagoRoute ที่ Level/Biome ตรงกับ template ไม่งั้นขึ้นเป็น
            // "ดินแดนที่ยังไม่รู้จัก" กดเข้าไม่ได้ (client/ExploreSystem.cs:123-126 GetArchipelagoRoutes)
            string archId = RegionCatalog.ArchipelagoIdOf(template);
            if (!byArchipelago.TryGetValue(archId, out var bucket))
            {
                bucket = (template, new List<Route>());
                byArchipelago[archId] = bucket;
            }
            bucket.Routes.Add(route);
        }

        var routes = new Routes
        {
            _Routes = byRole.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToDictionary(t => t.Key, t => t.Value.ToArray())),
            ArchipelagoRoutes = byArchipelago.Select(kv => new ArchipelagoRoute
            {
                ArchipelagoId = kv.Key,
                Level = kv.Value.Template.Level,
                Biome = kv.Value.Template.Biome,
                // UnstableFactor = 1 ⇒ ผ่านเงื่อนไขด้วย PioneerGradeInfo.CurrentAccessLevel >= 1
                // และไม่ต้องพึ่ง ClearedUnstableFactors (บังคับเฉพาะ UF >= 2)
                // client/ArchipelagoRouteExtension.cs:8-47
                UnstableFactor = 1,
                IncludedRoutes = kv.Value.Routes.ToArray(),
                PrerequisiteQuest = null,   // null = ไม่มีเควสบังคับ (ยังไม่มีระบบเควส)
                IsEpic = false
            }).ToArray()
        };
        Console.WriteLine($"[sail] ส่งเส้นทางจาก {_world.TerrainId}: " +
                          string.Join(" · ", byRole.Select(r => $"{r.Key} {r.Value.Sum(t => t.Value.Count)} เกาะ")) +
                          $" · หมู่เกาะ {byArchipelago.Count}");
        Send(routes, seq);
    }

    /// <summary>
    /// รายละเอียดหมู่เกาะ — Archipelago (2053) ตอบ GetArchipelago (2121)
    ///
    /// ⚠️ ต้องตอบทุกครั้ง ไม่งั้นบล็อกทั้งสาย: client/ExploreSystem.cs:332 รอ callback ชุดนี้
    /// ก่อนจะไปขอ GetRegion ต่อ แล้วค่อยยิง RoutesUpdated ⇒ ไม่ตอบ = หน้าจอว่างแบบเงียบ ๆ
    /// Progess = 100 ทุกเกาะ เพราะยังไม่มีระบบภารกิจประจำหมู่เกาะ — ถ้าน้อยกว่านั้น เกาะถัดไป
    /// จะถูกล็อก (client/Durango.UI/Archipelago.cs:150-164 เช็คความคืบหน้าของเกาะก่อนหน้า)
    /// </summary>
    private void HandleGetArchipelagoMsg(GetArchipelago msg, uint seq)
    {
        var included = new List<ArchipelagoRegionInfo>();
        foreach (Messages.Region region in RegionCatalog.All)
        {
            RegionCatalog.TemplateInfo template = RegionCatalog.GetTemplate(region.TemplateId);
            if (template != null && RegionCatalog.ArchipelagoIdOf(template) == msg.ArchipelagoId)
            {
                included.Add(new ArchipelagoRegionInfo
                {
                    Id = region.Id,
                    Progess = 100,
                    CoOpList = Array.Empty<RegionCoOpTodo>()
                });
            }
        }
        Send(new Archipelago
        {
            Id = msg.ArchipelagoId,
            TemplateId = null,          // ไม่ผูกกับ ArchipelagoMission ที่ยังไม่ได้ทำ
            UnstableFactor = 1,
            Name = null,
            ExpiresAt = 0.0,            // client ไม่ได้ใช้ฟิลด์นี้เลย
            IncludedRegions = included.ToArray()
        }, seq);
    }

    /// <summary>
    /// ตอบรายละเอียดเกาะปลายทาง — Region (2041) ตอบ GetRegion (2120)
    /// เกมถามทีละ id หลังได้ Routes มาแล้ว เพื่อเอาไปโชว์ชื่อ/ประเภทในหน้าเลือกเส้นทาง
    /// </summary>
    private void HandleGetRegionMsg(GetRegion msg, uint seq)
    {
        if (RegionCatalog.TryGet(msg.RegionId, out Messages.Region region))
        {
            Send(region, seq);
            return;
        }
        // เกาะที่เราไม่รู้จัก — ตอบ Error ให้เกมเลิกรอ (client/MapSystem.cs:650 มี .On<Error> รออยู่)
        Console.WriteLine($"[sail] ไม่รู้จักเกาะ '{msg.RegionId}'");
        // Error มีสอง string: TypeName ต้นฉบับกัน null ให้แล้ว แต่ Text ไม่ได้กัน
        // ⇒ default(Error) ทำให้ฝั่งเกมแครชด้วยเหตุผลเดียวกับ Abort
        Send(new Error { Text = "ไม่พบเกาะปลายทาง" }, seq);
    }

    /// <summary>
    /// ล่องเรือไปเกาะอื่น — TravelByRegion (2029) / TravelByRegionInArchipelago / SailingBack (3130)
    ///
    /// การย้ายเกาะของเกมนี้ทำผ่าน **การต่อใหม่** ไม่ใช่สลับโลกกลางคัน:
    ///   เซิร์ฟส่ง Emigrated (2099) → client/GameManager.cs:316-331 EmigratedReceived
    ///   ตั้ง Emigrated = Explore แล้วเรียก Connections.Frontend.Close() ตัดการเชื่อมต่อทันที
    ///   เกมกลับหน้า Title แล้วต่อใหม่เอง (knock → sessions → entry → Auth → Ready)
    /// ⇒ หน้าที่ของเราคือจำว่าผู้เล่นคนนี้จะไปเกาะไหน แล้วรอบต่อไปส่งเข้าโลกนั้น
    ///
    /// ตำแหน่ง: ล้าง Movements ทิ้งเพื่อให้ Player ctor ตั้งจุดเกิดของเกาะปลายทางให้เอง
    /// (Core/Player.cs ctor — ถ้า Movements เป็น null จะใช้ GetEntryPosition ของโลกนั้น)
    /// ถ้าไม่ล้าง ผู้เล่นจะไปโผล่พิกัดเดิมของเกาะเก่าซึ่งอาจกลางทะเลของเกาะใหม่
    /// </summary>
    private void HandleTravelMsg(string regionId, uint seq)
    {
        string target = regionId;
        bool knownCatalog = !string.IsNullOrEmpty(target) && RegionCatalog.TryGet(target, out _);
        bool knownPersonal = !string.IsNullOrEmpty(target) && (string.Equals(target, _context.PersonalRegionId, StringComparison.OrdinalIgnoreCase) || target.StartsWith("personal_", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(target) && !knownCatalog && !knownPersonal)
        {
            Console.WriteLine($"[sail] ปฏิเสธ: ไม่รู้จักเกาะ '{target}'");
            Send(new Abort { Text = "ไม่พบเกาะปลายทาง" }, seq);
            return;
        }

        _context.RegionId = target;                       // null = กลับเกาะตั้งต้น
        _context.AppearPlayer.Move.Movements = null;      // ให้ตั้งจุดเกิดใหม่ตามเกาะปลายทาง
        if (!string.IsNullOrEmpty(_context.Path))
        {
            _context.Save();
        }

        Console.WriteLine($"[sail] {EntityId[..Math.Min(8, EntityId.Length)]} ออกเรือ {_world.TerrainId} → {target ?? "(เกาะตั้งต้น)"}");
        Send(default(OK), seq);
        // Type.Unknown ⇒ client ตั้ง EmigratedType.Explore (ไม่ใช่ Warp) ตรงกับการเดินทางด้วยเรือ
        Send(new Emigrated { Type = TeleportType.Unknown });
    }

    public void Send<T>(T msg, uint replyOf = 0u)
    {
        _connection.Send(msg, replyOf);
    }
}
