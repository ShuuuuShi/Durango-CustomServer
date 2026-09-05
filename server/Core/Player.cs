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
        _world.ArtifactAppeared += World_ArtifactAppeared;
        _world.ArtifactDisappeared += World_ArtifactDisappeared;
        _world.PlayerAppeared += World_PlayerAppeared;
        _world.PlayerDisappeared += World_PlayerDisappeared;
        _world.ArtifactManager.ArtifactDisplayUpdated += delegate(ArtifactDisplay msg) { Send(msg); };
        _world.ArtifactManager.ArtifactStateUpdated += delegate(ArtifactState msg) { Send(msg); };
        _world.NaturalAdded += delegate(Point2 chunk, byte[] bytes)
        {
            Send(new GardenDiff { Chunk = chunk, _GardenDiff = bytes });
        };
        _world.NaturalDestroyed += delegate(Point2 tile)
        {
            Send(new DisappearEntityOnTile { Tile = tile });
        };
        _connection.Recv(delegate(SetChunk msg, PacketHeader header)
        {
            SetCenterChunks(msg.Chunk.x, msg.Chunk.y);
        });
        _connection.Recv(delegate(Move msg, PacketHeader header)
        {
            _world.BroadCast(msg);
            HandleMoveMsg(msg.Movements);
        });
        _connection.Recv(delegate(Cheat msg, PacketHeader header)
        {
            HandleCheatMsg(msg._Cheat, header.Seq);
        });
        _connection.Recv(delegate(Messages.Touch msg, PacketHeader header)
        {
            HandleTouchMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(DestructArtifact msg, PacketHeader header)
        {
            HandleDestructMsg(msg);
        });
        _connection.Recv(delegate(RestOn msg, PacketHeader header)
        {
            Send(default(OK), header.Seq);
            // นั่งพัก = ความชันของ fatigue/life/health เปลี่ยน ⇒ ต้องส่งเส้นชุดใหม่ทันที
            // ไม่ใช่รอรอบตรวจ (ค่าจาก status_effects.json → "rest" ดู SurvivalTuning)
            // เลิกพักเองตอนขยับ ตามแท็ก "clear_on_move" ของสถานะนั้น — ดู HandleMoveMsg
            _survival.SetResting(true);
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
            Send(default(PersonalRegionInfo), header.Seq);
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
            Send(default(EstateLicenses), header.Seq);
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
            _context.Storage[msg.Key] = msg.Value;
            OnContextChanged();
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
            Send(default(Abort), header.ReplyOf);
        });
        _connection.Recv(delegate(TakeOutItem msg, PacketHeader header)
        {
            if (_world.ArtifactManager.TakeOutItems(msg.EntityId, msg.ItemIds))
            {
                Send(default(OK), header.ReplyOf);
            }
            else
            {
                Send(default(Abort), header.ReplyOf);
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
            Closed?.Invoke();
        };
        RegisterSystemHandlers();
        _context.PlayerInfo.DisconnectedAt = Times.UnixTimeNow();
        SendStatistics();
        SendInventory();
        SendEquipments();
        SendDefoggedChunks();
        SendQuestCategories();
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
        Send(new StatusEffects
        {
            EntityId = EntityId,
            _StatusEffects = _toggledStatusEffects
                .Select(effect => new StatusEffect
                {
                    Id = effect.Key,
                    EffectId = effect.Key,
                    Level = 1,
                    Since = effect.Value,
                    Until = 0.0,
                    Stacked = 0,
                    DurationHidden = true,
                    Effects = Array.Empty<EffectDetail>()
                })
                .ToArray()
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
                _survival.SetResting(false);
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

    private void SendQuestCategories()
    {
        QuestCategory value = new()
        {
            Category = EpicCategory
        };
        Send(new QuestCategories { Epic = value });
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
                _world.ConstructArtifact(appearArtifact.Value, addons);
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
                if (flag) list.Add(Shared.System.Interaction.DestructArtifact);
                if (blueprint.Components.Contains("Washable")) list.Add(Shared.System.Interaction.Wash);
                if (blueprint.Components.Contains("Shelter")) list.Add(Shared.System.Interaction.Rest);
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
                if (blueprint.Components.Contains("Scribble") && flag)
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

    private void HandleDestructMsg(DestructArtifact msg)
    {
        _world.DestructArtifact(msg.EntityId);
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
        var list = new List<string>();
        foreach (MergedBlueprint allBlueprint in BlueprintStore.GetAllBlueprints())
        {
            if (allBlueprint.IsShowCraftMode)
            {
                list.Add(allBlueprint.Id);
            }
        }
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
            Send(default(Abort), seq);
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

        Send(new SetBaseMoveSpeed
        {
            EntityId = EntityId,
            NormalSpeed = (int)DefaultNormalSpeed,        // players.json → player.moving.default_normal_speed
            BattleSpeed = (int)battle
        });
    }

    /// <summary>ค่าสำรองตรงกับ players.json เป๊ะ — มีไว้กันไฟล์หาย ไม่ใช่ค่าที่คิดเอง</summary>
    private const float DefaultNormalSpeed = 500f;
    private const float DefaultBareHandsBattleSpeed = 350f;

    public void Process()
    {
        _connection.Process();
        UpdateSurvival();
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
        Send(default(Error), seq);
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
        if (!string.IsNullOrEmpty(target) && !RegionCatalog.TryGet(target, out _))
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
