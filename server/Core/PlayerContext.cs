using System;
using System.Collections.Generic;
using System.IO;
using Durango.Logic.Clusters;
using Durango.Logic.Encyclopedia;
using Durango.Utils;
using JetBrains.Annotations;
using Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared.Animal;
using UnityEngine;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/PlayerContext.cs — ฟอร์แมตเซฟตรงต้นฉบับ (.player JSON)
// ความต่าง:
//  1) ต้นฉบับสุ่มหน้าตาผ่าน EditPlayerDisplayProxy (ฝั่ง UI client) — ที่นี่ตั้งค่า default เรียบ ๆ
//     (client สร้างหน้าตาจริงเองเสมอผ่าน POST /players model_info ตอน prologue)
//  2) blob "encyclopedia" ต้นฉบับเติม memo ที่มีข้อความทั้งหมด — เซิร์ฟไม่มีตารางภาษา เริ่มว่าง (MemoStorageDefaults)
public class PlayerContext
{
    [JsonProperty("player_slot")]
    public int PlayerSlot;

    [JsonProperty("appear_player")]
    public AppearPlayer AppearPlayer;

    [JsonProperty("player_info")]
    public Durango.Logic.Clusters.PlayerInfo PlayerInfo;

    [JsonProperty("inventory_items")]
    public List<Item> InventoryItems;

    [JsonProperty("equipped_items")]
    public Dictionary<string, string> EquippedItems;

    [JsonProperty("musics")]
    public Dictionary<int, Music> Musics;

    [JsonProperty("storage")]
    public Dictionary<string, byte[]> Storage;

    /// <summary>
    /// [5 ก.ย. 2026] เกาะที่ผู้เล่นอยู่ตอนนี้ (= terrain id ดู RegionCatalog) — ว่าง = เกาะตั้งต้น
    ///
    /// ต้นฉบับไม่มีฟิลด์นี้เพราะเซิร์ฟในตัวของเกมมีโลกเดียวเสมอ พอทำระบบล่องเรือแล้ว
    /// ผู้เล่นแต่ละคนอยู่คนละเกาะได้ ⇒ ต้องจำไว้กับตัวผู้เล่น ไม่ใช่กับเซิร์ฟ
    ///
    /// การย้ายเกาะทำผ่านการต่อใหม่: เซิร์ฟส่ง Emigrated แล้วเกมตัดการเชื่อมต่อเอง
    /// (client/GameManager.cs:316-331 EmigratedReceived → Connections.Frontend.Close())
    /// รอบต่อไปที่ต่อเข้ามา เซิร์ฟอ่านค่านี้แล้วส่งเข้าโลกของเกาะปลายทาง
    /// </summary>
    /// <summary>
    /// [5 ก.ย. 2026] กุญแจบัญชีของเจ้าของตัวละครตัวนี้ — ว่าง = "ไม่มีเจ้าของ" (ตัวละครกำพร้า)
    ///
    /// ⚠️ ไม่มีช่องนี้ = ไม่มีระบบบัญชี ⇒ /accounts แจกตัวละครทุกตัวให้ทุกคน แล้วใครก็กดเข้าเล่น
    /// ตัวละครคนอื่นได้จากหน้าเลือกตัวละครโดยไม่ต้องแฮกอะไรเลย (ดูเหตุผลเต็มที่ Support/AccountKeys)
    ///
    /// ตัวละครกำพร้า (เซฟที่สร้างก่อนมีระบบนี้) จะ **มองไม่เห็นและเข้าไม่ได้** โดยปริยาย
    /// เปิดให้บัญชีแรกที่เข้ามารับไปได้ด้วย <c>--adopt-orphans</c> ตอนย้ายข้อมูลครั้งเดียว
    /// </summary>
    [JsonProperty("owner_key", NullValueHandling = NullValueHandling.Ignore)]
    public string OwnerKey;

    [JsonProperty("region_id")]
    public string RegionId;

    /// <summary>
    /// [5 ก.ย. 2026] สัตว์เลี้ยงของผู้เล่นคนนี้
    ///
    /// ก่อนหน้านี้อยู่แต่ใน <c>Player.PetStore</c> (Core/Player.Animals.cs) ซึ่งเป็น static dict
    /// ในหน่วยความจำล้วน ⇒ รีสตาร์ตเซิร์ฟทีเดียวสัตว์ที่ผู้เล่นทำให้เชื่องมาหายเกลี้ยง
    /// ตัวเชื่อมสองทาง (โหลดตอนเข้าเกม / เขียนกลับก่อนเซฟ) อยู่ที่ <c>Core/Player.PetSave.cs</c>
    ///
    /// **ไฟล์เซฟเก่าไม่มีคีย์ "pets"** ⇒ Newtonsoft คืน null แล้ว <see cref="Initialize"/>
    /// สร้างลิสต์ว่างให้ ⇒ อ่านไฟล์รุ่นก่อนได้ตามปกติ ไม่ต้องแปลงไฟล์
    /// </summary>
    [JsonProperty("pets")]
    [CanBeNull]
    public List<PetSaveData> Pets;

    /// <summary>
    /// [5 ก.ย. 2026] ตายมาแล้วกี่ครั้ง — ใช้เปิดแถวตาราง constants.json → death_penalty
    /// (<c>gauge_ratio_by_death_count</c> / <c>fatigue_recovery_ratio_by_death_count</c>)
    ///
    /// เดิม <c>Core/Player.Combat.cs:_deathCount</c> อยู่ในหน่วยความจำต่อ connection ⇒ ต่อใหม่แล้ว
    /// นับ 0 ใหม่ทุกครั้ง บทลงโทษไม่สะสม (ตายแล้วออกแล้วเข้าใหม่ = ฟื้นเต็ม 60% ตลอด)
    ///
    /// จงใจใช้ <c>int</c> ไม่ใช่ <c>int?</c> เพราะ "ไฟล์เก่าที่ยังไม่มีช่องนี้" กับ "ยังไม่เคยตาย"
    /// มีความหมายเดียวกันคือ 0 อยู่แล้ว ⇒ ค่า default ของชนิดทำหน้าที่ migration ให้ในตัว
    /// </summary>
    /// <summary>
    /// [5 ก.ย. 2026] จุดสำคัญที่ผู้เล่นคนนี้เดินไปเจอมาแล้ว — คีย์คือ "เกาะ|x,y"
    ///
    /// เหตุผลที่ต้องเก็บกับผู้เล่น (ไม่ใช่กับโลก) และเก็บเป็นชนิดของเราเอง:
    /// ดูที่หัวคลาส <see cref="ExploredPoint"/> ใน Core/Player.Map.cs
    /// </summary>
    [JsonProperty("explored_pois", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, ExploredPoint> ExploredPOIs;

    [JsonProperty("death_count")]
    public int DeathCount;

    /// <summary>
    /// [6 ก.ย. 2026] entity ของสิ่งปลูกสร้างที่ผู้เล่นตั้งเป็น "จุดกลับ" (귀환 지점)
    ///
    /// ตั้งผ่าน <c>SetAsHome</c>(2102) ที่เตียง — ข้อมูลจริงมี 12 แบบแปลนที่มี component
    /// <c>Home</c> (bed_01..bed_04 · tent · temptent ฯลฯ ดู entity_types/artifact.json)
    /// ใช้ตอนผู้เล่นกดปุ่มบ้านบนแผนที่ (<c>ReturnToHome</c> 2100)
    ///
    /// ⚠️ เก็บเป็น entity id ไม่ใช่พิกัด เพราะบ้านถูกรื้อได้ — เก็บพิกัดไว้จะวาร์ปไปที่ว่าง
    /// เปล่ากลางเกาะหลังบ้านหาย ⇒ ต้องเช็คว่าหลังนั้นยังอยู่ทุกครั้งก่อนวาร์ป
    /// </summary>
    [JsonProperty("home_artifact_id", NullValueHandling = NullValueHandling.Ignore)]
    public string HomeArtifactId;

    /// <summary>
    /// จุดเกิดใหม่/จุดกลับที่ผู้เล่นตั้งเอง (<c>SetReturningPoint</c> 2105) — หน่วยเป็นช่อง
    ///
    /// ไม่มีค่า = ใช้จุดเข้าเกาะของโลก (<c>World.EntryPoint</c>) เหมือนเดิม
    /// เก็บเป็นตัวเลขสองตัวไม่ใช่ <c>Point2</c> ด้วยเหตุผลเดียวกับ <see cref="ExploredPoint"/>
    /// (struct ของโปรโตคอลผ่าน JSON แล้วอ่านกลับไม่ตรงชนิด)
    /// </summary>
    [JsonProperty("returning_x", NullValueHandling = NullValueHandling.Ignore)]
    public int? ReturningX;

    [JsonProperty("returning_y", NullValueHandling = NullValueHandling.Ignore)]
    public int? ReturningY;

    /// <summary>
    /// เวลา (unix) ที่ผู้เล่นกดค้นหาจุดสำคัญครั้งล่าสุด — <c>SearchPOIs</c>(904)
    ///
    /// ต้องเก็บลงไฟล์เพราะฝั่งเกมถามกลับมาทุกครั้งที่เปิดเมนู (<c>GetLastSearchedTime</c> 906)
    /// เพื่อโชว์เวลาที่ค้นล่าสุด — เก็บแค่ในหน่วยความจำจะรีเซ็ตทุกครั้งที่ต่อใหม่
    /// </summary>
    [JsonProperty("poi_searched_at", NullValueHandling = NullValueHandling.Ignore)]
    public double POISearchedAt;

    [JsonIgnore]
    public string Path { get; private set; }

    [JsonIgnore]
    public string EntityId => PlayerInfo == null ? string.Empty : PlayerInfo.PlayerEntityId;

    public void Initialize(string path)
    {
        Path = path;
        if (PlayerInfo == null)
        {
            PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
            {
                PlayerLevel = 60
            };
            string text = Guid.NewGuid().ToString();
            PlayerInfo.PlayerEntityId = text;
            PlayerInfo.PlayerName = text.Substring(0, 8);
            AppearPlayer.Name = PlayerInfo.PlayerName;
            AppearPlayer.Level = PlayerInfo.PlayerLevel;
            AppearPlayer.EntityId = text;
            AppearPlayer.IsAlive = true;
            AppearPlayer.Title.EntityId = text;
            AppearPlayer.Title._Title = string.Empty;
            AppearPlayer.Member.EntityId = text;
            AppearPlayer.Member.ClanId = string.Empty;
            AppearPlayer.Member.ClanName = string.Empty;
            AppearPlayer.Member.RoleId = -1;
            AppearPlayer.Move.EntityId = text;
            AppearPlayer.Survival.EntityId = text;
            // ค่าตั้งต้นก่อนรู้เพศ — ตัวจริงถูกตั้งอีกทีตอนสร้างตัวละครที่ Gateway.UpdateAppearPlayer
            // (ตอน Initialize ยังไม่มีใครบอกเพศมา EntityType จึงยังเป็น 0)
            AppearPlayer.Display.Body = "Models/PC/Male/Body/m_body_nothing.FBX";
            AppearPlayer.Display.DefaultBody = AppearPlayer.Display.Body;
            AppearPlayer.Display.DefaultInner = "Models/PC/Male/Inner/m_inner_basic.FBX";
            AppearPlayer.Display.BodySize = 0.5f;
            AppearPlayer.Display.EntityId = text;
        }
        InventoryItems ??= new List<Item>();
        EquippedItems ??= new Dictionary<string, string>();
        // [5 ก.ย. 2026] ช่องใหม่ของรอบนี้ — ไฟล์เซฟเก่าไม่มีคีย์นี้จึงโหลดมาเป็น null
        Pets ??= new List<PetSaveData>();
        // ⚠️ ซ่อม Item.Ext ที่โหลดกลับมาเป็น JObject **ก่อน** ที่ใครจะเอาไอเทมไปแพ็กลงแพ็กเก็ต
        // (เหตุผลเต็ม ๆ ดูที่หัวคลาส ItemExtRepair ท้ายไฟล์ — ไม่ทำ = ไอเทมทั้งชิ้นเลื่อนช่อง)
        ItemExtRepair.Normalize(InventoryItems, "กระเป๋าผู้เล่น");
        // [6 ก.ย. 2026] ไอเทมที่เซฟไว้ก่อนมีระบบคำแปล เก็บ "ชื่อ" เป็นข้อความเกาหลีลงไฟล์ไปแล้ว
        // ⇒ โหลดกลับมาก็ยังเกาหลี ทั้งที่ของใหม่เป็นไทยหมดแล้ว (ดู Support/MoCatalog.cs)
        // แปลตอนโหลดครั้งเดียว แล้วรอบเซฟถัดไปจะเขียนทับเป็นไทยเอง
        ItemNames.Localize(InventoryItems, "กระเป๋าผู้เล่น");
        foreach (PetSaveData pet in Pets)
        {
            if (pet != null)
            {
                ItemExtRepair.Normalize(pet.Bag, "กระเป๋าสัตว์");
                ItemNames.Localize(pet.Bag, "กระเป๋าสัตว์");
            }
        }
        // [5 ก.ย. 2026] สร้าง/ซ่อมหลอดสถานะจากข้อมูลจริงทุกครั้งที่เปิด context ไม่ใช่แค่ตอนสร้างใหม่
        //
        // ทำไมต้องทำตอนโหลดด้วย: GaugeConverter ย่อ Gauge เป็น {min,max,cur} ตอนเขียนไฟล์เซฟ
        // (Support/GaugeConverter.cs:13-20) ⇒ เส้นแนวโน้มหายหมด เหลือ node เดียวที่ Time = 0
        // ถ้าไม่สร้างใหม่ หลอดจะค้างนิ่งตลอดเกม · SurvivalState หยิบค่า cur ที่เซฟไว้ไปตั้งต้นให้เอง
        //
        // ⚠️ ของเดิมเอา Gauge ก้อนเดียวใส่ทั้ง Survival.Life และ Gauges["stamina"] ⇒ HP กับ
        // ความอึดเดินพร้อมกันเป๊ะ · ตอนนี้แยกก้อนตามนิยามจริงใน entity_types/players.json
        SurvivalState.Reset(this);
        ResetStaleMotion();
        if (KUtility.GetSize(Storage) != 0) return;
        Storage = new Dictionary<string, byte[]>();
        var data = MemoStorageDefaults.Empty();
        // WriteToBytes คืน null ได้เมื่อ serialize พลาด — ไม่ใส่คีย์ดีกว่าใส่ null ค้างไว้
        if (Json.WriteToBytes(data) is { } memoBlob)
        {
            Storage[MemoStorageDefaults.StorageKey] = memoBlob;
        }
    }

    /// <summary>
    /// ท่าที่จะให้ตัวละครยืนตอนเพิ่งโผล่เข้าโลก — ชื่อนี้ไม่ได้ตั้งเอง
    /// เป็นตัวเดียวกับที่ฝั่งเกมส่งให้ตัวเองตอนสร้างตัวละคร (client/PlayerManager.cs:109)
    /// จึงมั่นใจได้ว่าโหลดเสร็จก่อน clip ของอาวุธเสมอ
    /// </summary>
    private const string SafeSpawnMotion = "Barehand_Stand";

    /// <summary>
    /// ล้างท่าค้างที่ผูกกับอาวุธออกจากเซฟ — **กันเกมแครชทั้งโปรเซสตอนเข้าเกม**
    ///
    /// ⚠️ อาการจริง (แครช 2 ครั้งซ้อน 6 ก.ย. 2026): ผู้เล่นออกจากเกมตอนถืออาวุธสองมือ
    /// เซฟจึงเก็บ <c>MotionName = "Twohand_Stand"</c> ไว้ พอเข้าเกมใหม่เซิร์ฟส่งค่านี้กลับไปกับ
    /// <c>AppearPlayer</c> แล้วฝั่งเกมเล่นท่าทันทีตอนสร้างตัวละคร ทั้งที่ clip ของอาวุธ
    /// **ยังโหลดไม่เสร็จ** ⇒ <c>Anim["M_Twohand_Stand"]</c> ทำ UnityPlayer.dll ล้ม (Access Violation)
    /// <code>
    ///   UnityEngine.Animation:GetState(string)          ← แครชตรงนี้ (native)
    ///   PlayerBehavior:TryPlayClip                       (client/PlayerBehavior.cs:1712 Anim[text])
    ///   PlayerManager:MakePlayerObject
    ///   PlayerManager:&lt;Start&gt;b__42_0(AppearPlayer, …)     ← รับ AppearPlayer จากเซิร์ฟ
    /// </code>
    /// เป็นแครชทั้งโปรเซส ไม่ใช่ exception ที่ดักได้ ⇒ ต้องกันที่ต้นทาง (ไม่ส่งชื่อท่าของอาวุธมาแต่แรก)
    ///
    /// ท่าของอาวุธจะถูกตั้งใหม่เองตามปกติเมื่อผู้เล่นขยับ (Move) หลังอุปกรณ์โหลดครบแล้ว
    /// </summary>
    private void ResetStaleMotion()
    {
        Movement[] movements = AppearPlayer.Move.Movements;
        if (movements == null) return;
        for (int i = 0; i < movements.Length; i++)
        {
            if (string.IsNullOrEmpty(movements[i].MotionName)) continue;
            if (movements[i].MotionName == SafeSpawnMotion) continue;
            movements[i].MotionName = SafeSpawnMotion;
        }
    }

    [CanBeNull]
    public static PlayerContext Load(string path)
    {
        PlayerContext playerContext = SafeSave.ReadWithBackup(path, "player-context", data => Json.Read<PlayerContext>(data));
        playerContext?.Initialize(path);
        return playerContext;
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(Path)) return;
        SafeSave.WriteAtomic(Path, Json.WriteToBytes(this, indented: false), "player-context");
    }

    public static string MakePath(int slot, string clusterKey)
    {
        return System.IO.Path.Combine(AppData.CombinePath(WorldContext.GetBasePath(clusterKey)), slot + ".player");
    }
}

/// <summary>
/// สัตว์เลี้ยงหนึ่งตัวในไฟล์เซฟ — โครงตรงกับ <c>Player.PetStore.Entry</c> ทีละฟิลด์
///
/// ═══ ทำไมเซฟ <c>Messages.Pet</c> ทั้งก้อนได้ (ไม่ต้องแตกเป็น DTO ทีละฟิลด์) ═══
/// ไล่เช็คทั้งกิ่งแล้วไม่มีฟิลด์ไหนประกาศเป็น <c>object</c> หรือ interface เลย จึงไม่มีทางกลาย
/// เป็น <c>JObject</c> ตอนอ่านกลับ (ต่างจาก <c>ArtifactState.Cage</c> และ <c>Item.Ext</c>):
///   Pet            → EntityId/EntityType/TamerEntityId/Name/Rank/Generation/IsBoarding/IsSpawned
///                    + Stat(PetStats) + Statistics(PetStatistics) + CageInfo(CageInfo?)
///   PetStats       → Gauge Life/Hungry · Money? RetryCost · string[]/Dictionary&lt;string,int&gt; ล้วน
///   PetStatistics  → Dictionary&lt;Derived,float&gt; · MilestoneInfo[] · PetActiveSkill[] (struct ทั้งหมด)
/// การเซฟทั้งก้อนยังได้เปรียบตรงที่ ถ้า Core/Player.Animals.cs เพิ่มฟิลด์ใน Pet วันหน้า
/// ไฟล์เซฟจะตามไปเองโดยไม่ต้องแก้ที่นี่ (ถ้าแตกเป็น DTO มือ จะตกหล่นแบบเงียบ ๆ)
///
/// ═══ สองช่องที่ round-trip ไม่ครบ และซ่อมที่ไหน (Core/Player.PetSave.cs: FromSave) ═══
///   1. <c>Gauge Life/Hungry</c> — GaugeConverter ย่อเหลือ {min,max,cur} ⇒ เส้นแนวโน้มหาย
///      ⇒ สร้างหลอดใหม่ตอนโหลดจาก LifeMax/HungryMax/HungryVelocity ที่เก็บไว้ในนี้
///   2. <c>Money? RetryCost</c> — Money มีแต่ฟิลด์ <c>readonly</c> ⇒ Newtonsoft เขียนได้แต่อ่านกลับไม่ได้
///      (ได้ Money(0, TStone) = โชว์ราคาหมุนซ้ำเป็น 0) ⇒ คิดใหม่จาก costs.json ตอนโหลด
/// </summary>
public class PetSaveData
{
    /// <summary>เผื่ออนาคตต้องแปลงรูปแบบเซฟ (แนวเดียวกับ SkillSave ใน Core/Player.Skills.cs)</summary>
    [JsonProperty("v")] public int Version = 1;

    [JsonProperty("pet")] public Messages.Pet Pet;

    [JsonProperty("grazing")] public bool Grazing;

    [JsonProperty("bag")] public List<Item> Bag;

    /// <summary>เพดานหลอด + อัตราหิว — ต้องเก็บ เพราะใช้ประกอบ Gauge ใหม่ตอนโหลด</summary>
    [JsonProperty("life_max")] public float LifeMax;

    [JsonProperty("hungry_max")] public float HungryMax;

    [JsonProperty("hungry_velocity")] public float HungryVelocity;

    [JsonProperty("pending_milestone_tag")] public string PendingMilestoneTag;

    [JsonProperty("pending_milestone_tag_level")] public int PendingMilestoneTagLevel;

    /// <summary>ค่าเริ่มต้น -1 ตรงกับ PetStore.Entry (0 คือ "ช่องแรก" ซึ่งคนละความหมายกับ "ไม่มี")</summary>
    [JsonProperty("pending_milestone_slot")] public int PendingMilestoneSlot = -1;

    [JsonProperty("milestone_redraw")] public int MilestoneRedrawCount;

    [JsonProperty("skill_redraw")] public int SkillRedrawCount;

    [JsonProperty("pending_rank")] public PetRank? PendingRank;

    [JsonProperty("pending_rank_tag")] public string PendingRankTag;
}

/// <summary>
/// ซ่อม <c>Item.Ext</c> ที่โหลดกลับมาจากไฟล์เซฟให้เป็นชนิดจริง
///
/// ⚠️ **ไม่ทำ = แพ็กเก็ตไอเทมพังทั้งชิ้น ไม่ใช่แค่ช่อง Ext หาย**
/// <c>Messages/Item.cs:51</c> ประกาศ <c>public object Ext;</c> เพราะช่องนี้ใส่ได้ 7 ชนิด
/// ⇒ Newtonsoft อ่านกลับมาเป็น <c>JObject</c> เสมอ · แล้ว <c>Item.Pack</c> (บรรทัด 213-244) เขียนแบบ
/// <code>if (Ext == null) PackNull(); else if (Ext is DeodorantItem) … else if (Ext is Reins) …</code>
/// **ไม่มี else** ⇒ เจอ JObject แล้วไม่เขียนอะไรลงไปเลยสักไบต์ ทำให้ 6 ฟิลด์ที่เหลือ
/// (CollectibleId / GeneratorId / EmotionalMotions / PioneerCost / Tradable / ReformSlots)
/// เลื่อนตำแหน่งไปหนึ่งช่องทั้งหมด — อาการเดียวกับบั๊กสถานะกรงใน Support/CageTypes.NormalizeLoaded
///
/// ทำไมต้องอยู่ที่นี่ ทั้งที่ Core/Player.Domestication.cs:NormalizeReinItems ก็กวาดบังเหียนอยู่แล้ว:
///   • ตัวนั้นกวาดเฉพาะ <c>InventoryItems</c> และเฉพาะ "ไอเทมที่เป็นบังเหียน" — ของในกระเป๋าสัตว์
///     (PetSaveData.Bag ที่เพิ่งเริ่มเซฟรอบนี้) กับ Ext ชนิดอื่นยังค้างเป็น JObject อยู่ดี
///   • ตัวนั้นเจอ Ext ชนิดที่ไม่รู้จักแล้วได้แต่พิมพ์เตือน (บรรทัด 818-823) แล้วปล่อยผ่าน
///   ⇒ ที่นี่ซ่อมให้ครบทุกชนิดตั้งแต่ตอนโหลดไฟล์ · ของที่ซ่อมแล้วจะเป็น <c>Reins</c> จริง
///     ทำให้ NormalizeReinItems ข้ามไปเอง (<c>if (item.Ext is Reins) continue;</c>) — ไม่ตีกัน
///
/// **วิธีแยกชนิดเป็นการตัดสินใจของเรา** (ไฟล์เซฟไม่ได้เขียนชื่อชนิดกำกับไว้ และแก้ Messages/Item.cs
/// ซึ่งเป็นซอร์สของ NEXON ไม่ได้) ⇒ ดูจาก "ชื่อฟิลด์ที่มีเฉพาะในชนิดนั้น" แบบเดียวกับที่
/// Support/CageTypes.NormalizeLoaded ใช้ฟิลด์ Tasks แยก GrowCage ออกจาก Cage
/// </summary>
/// <summary>
/// แปลชื่อ/คำอธิบายไอเทมที่ค้างเป็นภาษาเกาหลีอยู่ในไฟล์เซฟ
///
/// ═══ ทำไมต้องมี ═══
/// <c>Messages.Item.Name</c> เป็น <c>string</c> ที่เซิร์ฟ "ตัดสินใจแล้ว" ตอนสร้างไอเทม
/// (Cheats.MakeItem หยิบจาก <c>Prototype.Name</c> ซึ่งเป็น <see cref="Durango.Utils.Gettext"/>)
/// ⇒ ไอเทมที่สร้างก่อนมีระบบคำแปล ถูกเขียนลงไฟล์เป็นภาษาเกาหลีถาวร
/// และฝั่งเกมเอาไปโชว์ตรง ๆ ไม่แปลซ้ำ (client/Durango.Logic.Item/ItemData.cs:132)
///
/// ⚠️ ไม่แปลตรงนี้ = ผู้เล่นเก่าเห็นของเก่าเป็นเกาหลี ของใหม่เป็นไทย ปนกันในกระเป๋าเดียว
///
/// ปลอดภัยที่จะทำซ้ำ: <see cref="MoCatalog.Translate"/> คืนข้อความเดิมถ้าไม่เจอคำแปล
/// และคำแปลไทยจะไม่ตรงกับ msgid เกาหลีอยู่แล้ว ⇒ แปลรอบสองไม่เปลี่ยนอะไร
/// </summary>
internal static class ItemNames
{
    public static void Localize([CanBeNull] List<Item> items, string where)
    {
        if (items == null || items.Count == 0 || !MoCatalog.Ready) return;
        int changed = 0;
        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            string name = MoCatalog.Translate(item.Name);
            string desc = MoCatalog.Translate(item.Description);
            if (ReferenceEquals(name, item.Name) && ReferenceEquals(desc, item.Description)) continue;
            item.Name = name;
            item.Description = desc;
            items[i] = item;
            changed++;
        }
        if (changed > 0) Console.WriteLine($"[ภาษา] แปลชื่อไอเทมใน{where} {changed} ชิ้น");
    }
}

internal static class ItemExtRepair
{
    public static void Normalize([CanBeNull] List<Item> items, string where)
    {
        if (items == null || items.Count == 0) return;
        int repaired = 0;
        int dropped = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Ext is not JObject node) continue;
            Item item = items[i];
            object rebuilt = Rebuild(node);
            if (rebuilt == null) dropped++;
            else repaired++;
            item.Ext = rebuilt;
            items[i] = item;
        }
        if (repaired > 0)
        {
            Console.WriteLine($"[เซฟ] ซ่อมข้อมูลเสริมของไอเทมใน{where} {repaired} ชิ้น");
        }
        if (dropped > 0)
        {
            // ล้างเป็น null ดีกว่าปล่อย JObject ค้าง: null ทำให้ Item.Pack เขียน PackNull ซึ่งฟอร์แมต
            // ยังถูกต้อง เสียแค่ข้อมูลเสริมของชิ้นนั้น ส่วน JObject ทำให้ทั้งแพ็กเก็ตอ่านผิดตำแหน่ง
            Console.WriteLine($"[เซฟ] ⚠️ ข้อมูลเสริมของไอเทมใน{where} {dropped} ชิ้นระบุชนิดไม่ได้ — ล้างทิ้งกันแพ็กเก็ตเลื่อนช่อง");
        }
    }

    /// <summary>
    /// ซ่อมไอเทม "ชิ้นเดียว" ที่ไม่ได้อยู่ในลิสต์ — ประตู/หน้าต่างที่ติดกับบ้าน (AddOns._AddOns)
    /// และเสื้อผ้าบนหุ่นโชว์ (Mannequin.Head/Body) ซึ่งเก็บลงไฟล์ .world เหมือนกัน
    /// </summary>
    public static Item Fix(Item item, string where)
    {
        if (item.Ext is not JObject node) return item;
        object rebuilt = Rebuild(node);
        Console.WriteLine(rebuilt != null
            ? $"[เซฟ] ซ่อมข้อมูลเสริมของไอเทมใน{where}"
            : $"[เซฟ] ⚠️ ข้อมูลเสริมของไอเทมใน{where} ระบุชนิดไม่ได้ — ล้างทิ้งกันแพ็กเก็ตเลื่อนช่อง");
        item.Ext = rebuilt;
        return item;
    }

    /// <summary>แปลง JObject กลับเป็นชนิดจริง — null = ระบุชนิดไม่ได้ (ผู้เรียกล้างทิ้ง)</summary>
    [CanBeNull]
    private static object Rebuild(JObject node)
    {
        // เรียงจากชนิดที่มีฟิลด์เฉพาะตัวชัดที่สุดลงมา (ดูชื่อฟิลด์จริงใน server/GameCode/Messages/)
        if (node["PetEntityType"] != null && node["VehicleEntityType"] != null) return To<Reins>(node);
        if (node["StatusEffectId"] != null) return To<DeodorantItem>(node);
        if (node["RewardId"] != null) return To<LootBoxItem>(node);
        if (node["Contents"] != null && node["Capacity"] != null) return To<Container>(node);
        if (node["Artifacts"] != null && node["Status"] != null) return To<ArtifactPackage>(node);
        // ArtifactCapsule กับ BlueprintItem มี BlueprintId เหมือนกัน — ตัวแรกมี EntityId ด้วย
        if (node["BlueprintId"] != null && node["EntityId"] != null) return RebuildCapsule(node);
        if (node["BlueprintId"] != null) return To<BlueprintItem>(node);
        return null;
    }

    /// <summary>
    /// <c>ArtifactCapsule</c> มี <c>ArtifactState.Cage</c> ที่เป็น <c>object</c> ซ้อนอยู่ข้างในอีกชั้น
    /// ⇒ กับดักเดิมซ้อนกันสองชั้น · แยกชนิดด้วยฟิลด์ <c>Tasks</c> เหมือน Support/CageTypes.cs:58
    /// </summary>
    private static object RebuildCapsule(JObject node)
    {
        ArtifactCapsule capsule = Json.Read<ArtifactCapsule>(node.ToString(Formatting.None));
        if (node["State"]?["Cage"] is JObject cage)
        {
            capsule.State.Cage = cage["Tasks"] != null
                ? Json.Read<GrowCage>(cage.ToString(Formatting.None))
                : Json.Read<Messages.Cage>(cage.ToString(Formatting.None));
        }
        return capsule;
    }

    /// <summary>
    /// อ่านผ่าน <see cref="Json.Read{T}(string,bool)"/> ไม่ใช่ <c>node.ToObject&lt;T&gt;()</c>
    /// เพราะ ToObject ใช้ serializer เปล่า ⇒ ไม่ผ่าน GaugeConverter/GettextConverter
    /// แล้วหลอด (เช่น Reins.Pet.Stat.Life) จะอ่านกลับมาเพี้ยน
    /// </summary>
    private static object To<T>(JObject node)
    {
        return Json.Read<T>(node.ToString(Formatting.None));
    }
}
