using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Network;
using Durango.Utils;
using Durango.Utils.Extensions;
using Messages;
using Newtonsoft.Json;
using Shared.Ability;
using Yaml.Util;
using SkillCat = Shared.Skill.Category;

namespace Durango.Online;

/// <summary>
/// [5 ก.ย. 2026] **ค่าปรับสมดุลของระบบสกิล/เลเวล — ของเราทั้งหมด รวมไว้ที่เดียว**
///
/// ⚠️ ทำไมต้องตั้งเอง: ข้อมูลเกมที่ NEXON ปล่อยมามีแต่ "ตาราง" ไม่มี "อัตราได้รับ"
///   · <c>data/assets/statistics/player.json</c> → <c>levelup_rewards</c> เป็น 0 ทุกช่อง
///   · <c>entity_types/animal.json</c> → <c>exp_amount</c> ของสัตว์ทุกตัวเป็น 0
///   · <c>constants.json</c> → <c>experience</c> มีแค่ <c>exp_multiplier</c> (เพดานตัวคูณ 0.1–10)
///   ⇒ ตัวเลข exp ต่อการกระทำอยู่ฝั่งเซิร์ฟ NEXON ล้วน ๆ กู้ไม่ได้ ต้องตั้งใหม่
///
/// สิ่งที่ **ไม่ได้** อยู่ในนี้เพราะมีของจริงให้ใช้แล้ว (อย่าก๊อปมาซ้ำ):
///   · เกณฑ์เลเวล → <c>statistics/player.json</c> → <c>level_thresholds[80]</c>
///   · เลเวลสูงสุด/เลเวลมือใหม่ → <c>constants.json</c> → <c>max_levels.player</c>, <c>newbie_level</c>
///   · แต้มสกิลตั้งต้น → <c>constants.json</c> → <c>skill_points.initial</c>
///   · exp ต่อเลเวลของหมวดสกิล/เวลาวิจัย → <c>skill/categories.json</c>
///   · สูตรลดเวลาวิจัย → <c>constants.json</c> → <c>skill.research_time_reduce</c>
/// </summary>
public static class SkillTuning
{
    /// <summary>
    /// **ค่าของเรา** — แต้มสกิลที่ได้เพิ่มต่อ 1 เลเวลผู้เล่น
    ///
    /// คิดจากของจริง: สกิลทั้งเกมรวมกัน 3,213 แต้ม (นับ <c>skill_point</c> ของทั้ง 918 โหนดใน
    /// <c>skill/skills.json</c>) และเลเวลสูงสุดคือ 60 ⇒ 10 + 59×3 = 187 แต้มตอนเลเวลเต็ม
    /// = ราว 6% ของทั้งเกม ซึ่งบังคับให้ต้อง "เลือกสายถนัด" ตามที่เกมออกแบบไว้
    /// (โหนดหนึ่งราคา 3–8 แต้ม ⇒ 187 แต้ม ≈ 30–50 สกิล)
    /// </summary>
    public const int SkillPointsPerLevel = 3;

    /// <summary>
    /// **ค่าของเรา** — เลเวลตั้งต้นของตัวละครใหม่
    ///
    /// ⚠️ ของเดิม <c>PlayerContext.cs:68</c> ตั้ง <c>PlayerLevel = 60</c> ตายตัว (= เลเวลสูงสุด)
    /// และ <c>Player.cs:598</c> ส่ง <c>Exp = 3532536</c> (= <c>level_thresholds[58]</c> เกณฑ์ lv60 พอดี)
    /// นั่นคือ "ตัวเลขปลอมของเซิร์ฟ offline ที่ NEXON ฝังไว้ให้เทส" ไม่ใช่ความคืบหน้าจริงของใคร
    /// ⇒ เริ่มที่ 1 ไม่ได้ทำให้ใครเสียของ แต่ทำให้แถบ exp เดินจริงเป็นครั้งแรก
    /// </summary>
    public const int StartLevel = 1;

    // ── exp ต่อการกระทำ ────────────────────────────────────────────────────────────
    // หน่วยคือ "จำนวนครั้ง" ไม่ใช่ exp ดิบ เพราะตาราง level_thresholds ชันมาก
    // (lv1→2 ต้องการ 11 exp แต่ lv59→60 ต้องการ 432,346) ถ้าให้ exp ดิบคงที่
    // เลเวลต้น ๆ จะพุ่งพรวดแล้วเลเวลปลายจะตันสนิท
    // ⇒ AddExpForAction() แปลง "ครั้ง" เป็น exp ดิบตามช่วงเลเวลปัจจุบัน ดู ExpPerAction()

    /// <summary>**ค่าของเรา** — น้ำหนักของการเก็บของ/ขุด/ตัด (1 ครั้ง)</summary>
    public const int GatherWeight = 1;

    /// <summary>**ค่าของเรา** — คราฟต์ 1 ชิ้น (ใช้เวลานานกว่าเก็บของ)</summary>
    public const int CraftWeight = 2;

    /// <summary>**ค่าของเรา** — สร้าง/ติดตั้งสิ่งปลูกสร้าง 1 หลัง</summary>
    public const int BuildWeight = 3;

    /// <summary>**ค่าของเรา** — ล้มสัตว์/ศัตรู 1 ตัว</summary>
    public const int KillWeight = 3;

    /// <summary>**ค่าของเรา** — ชำแหละซาก 1 ครั้ง</summary>
    public const int ButcherWeight = 2;

    /// <summary>
    /// **ค่าของเรา** — จำนวน "ครั้ง" โดยประมาณที่ต้องทำเพื่อขึ้น 1 เลเวล
    ///
    /// 40 ครั้ง/เลเวล × 59 เลเวล ≈ 2,400 ครั้งจนเลเวลเต็ม ที่น้ำหนัก 1
    /// (ทำจริงจะเร็วกว่านั้นเพราะกิจกรรมส่วนใหญ่มีน้ำหนัก 2–3)
    /// ตัวเลขนี้กำหนด "จังหวะของเกม" ทั้งเกม ⇒ ปรับที่นี่ที่เดียว
    /// </summary>
    public const int ActionsPerLevel = 40;

    /// <summary>
    /// **ค่าของเรา** — exp หมวดสกิลที่ได้ต่อ 1 ครั้ง
    ///
    /// เพดาน 3 มาจากของจริง (<c>constants.json</c> → <c>skill.exp_increase_limit</c>)
    /// เลือก 1 เพราะตาราง <c>exp_needed</c> ของหมวดเล็กมาก (lv1 ต้องการ 1, lv19 ต้องการ 6,
    /// รวม lv1→60 แค่ 1,396) ⇒ ให้ 1 ต่อครั้งก็ไล่ทันเลเวลผู้เล่นพอดี
    /// </summary>
    public const int CategoryExpPerAction = 1;

    /// <summary>
    /// **ค่าของเรา** — ราคา "ข้ามเวลาวิจัย" (หน่วย Gem)
    ///
    /// 0 = ฟรี เพราะเซิร์ฟนี้ยังไม่มีระบบเงินตรา/Gem จริง (ดู <c>costs.json</c> ที่มีแต่
    /// <c>skill_untrain</c> ไม่มีคีย์ของการวิจัยเลย ⇒ ราคาจริงอยู่ฝั่ง NEXON)
    /// ⚠️ ห้ามใส่ null: <c>client/Durango.UI/SkillCategoryProgressGauge.cs:236</c> เรียก
    ///    <c>ResearchSkipCost.Get()</c> ตรง ๆ ไม่เช็ค null ⇒ ส่ง Gauge ค่า 0 ไปเสมอ
    /// </summary>
    public const float ResearchSkipCost = 0f;

    /// <summary>
    /// **ค่าของเรา** — ค่าพลังพื้นฐาน (Basic 8 ตัว) ตอนยังไม่มีสกิล/อุปกรณ์อะไรเลย
    ///
    /// ⚠️ ไม่มีในไฟล์ data ไหนเลย (เช็คแล้วทั้ง constants.json, statistics/player.json,
    /// entity_types/players.json) — ต้นฉบับก็ส่ง dict ว่าง (client/Durango.Online/Player.cs:408)
    /// 10 = ตัวเลขกลม ๆ ที่ทำให้ modifier แบบ <c>*_mul</c> (คูณเป็นสัดส่วน) มีผลเห็นได้
    /// client แค่เอาไปโชว์ตัวเลข (client/Durango.UI/CharacterBasicStatusWidget.cs:66)
    /// </summary>
    public const int BasicAbilityBase = 10;

    /// <summary>คีย์ใน <c>PlayerContext.Storage</c> ที่เก็บสถานะสกิล/exp</summary>
    /// <remarks>
    /// ⚠️ ต้องไม่ชนกับคีย์ที่ client เขียนเองผ่าน SetStorageItem (Player.cs:452 เขียนทับได้ทุกคีย์):
    /// "encyclopedia" · "ChatChannelInfo" · "Emotional" · "CharacterTitle" ·
    /// "RecentlyUnlockedMenuList" · "store_review" · คีย์ของ PlayGuideSystem
    /// ⇒ ใช้ prefix "server_" ให้เห็นชัดว่าเป็นของฝั่งเซิร์ฟ
    /// </remarks>
    public const string StorageKey = "server_skills";
}

// ═══════════════════════════════════════════════════════════════════════════════════
//  รูปร่างของไฟล์ data จริง (data/assets/skill/*, player/jobs.json, entity_types/players.json)
//  ตั้งชื่อคลาสลงท้าย Json เพื่อไม่ให้ชนกับ Messages.Skill* และ Yaml.* ที่มีอยู่แล้ว
//  ประกาศเฉพาะฟิลด์ที่เซิร์ฟใช้จริง — Newtonsoft ข้ามคีย์ที่ไม่ได้ประกาศให้เอง
//  (จงใจไม่ประกาศ name/description ซึ่งเป็น Gettext เพราะเซิร์ฟไม่มีตารางภาษา)
// ═══════════════════════════════════════════════════════════════════════════════════

// CS0649 "ฟิลด์ไม่เคยถูกกำหนดค่า" — ถูกต้องแล้วสำหรับ DTO: Newtonsoft เป็นคนเขียนค่าให้ตอน
// deserialize คอมไพเลอร์มองไม่เห็น (คลาส Yaml ใน Support/ ไม่โดนเพราะเป็น public ไม่ใช่ internal)
#pragma warning disable CS0649

/// <summary>หนึ่งโหนดสกิล = สกิลหนึ่งตัวที่เลเวลหนึ่ง (index ในอาเรย์ + 1 = เลเวลของโหนด)</summary>
internal class SkillNodeJson
{
    /// <summary>เลเวลหมวดที่ต้องถึงก่อนถึงจะเรียนโหนดนี้ได้</summary>
    [JsonProperty("category_level")] public int CategoryLevel;

    /// <summary>ราคาเป็นแต้มสกิล</summary>
    [JsonProperty("skill_point")] public int SkillPoint;

    /// <summary>ถอนคืนไม่ได้ (สกิลที่อาชีพแจกให้)</summary>
    [JsonProperty("untrain_disabled")] public bool UntrainDisabled;

    /// <summary>id ของรางวัลใน rewards.json — จุดเชื่อม "สกิล → ค่าสถานะ"</summary>
    [JsonProperty("rewards")] public string[] Rewards;
}

/// <summary>หนึ่งหมวดสกิลใน <c>skill/categories.json</c></summary>
internal class SkillCategoryJson
{
    /// <summary>เลเวลหมวด → exp ที่ต้องใช้ขึ้นเลเวลถัดไป</summary>
    [JsonProperty("exp_needed")] public Dictionary<int, int> ExpNeeded;

    /// <summary>เลเวลหมวด → วินาทีที่ต้อง "วิจัย" ก่อนขึ้นเลเวลถัดไป (0 = ขึ้นได้เลย)</summary>
    [JsonProperty("research_times")] public Dictionary<int, int> ResearchTimes;
}

/// <summary>หนึ่งรางวัลใน <c>skill/rewards.json</c> (1,321 รายการ)</summary>
internal class SkillRewardJson
{
    [JsonProperty("type")] public int Type;

    /// <summary>รางวัลแบบหลายโมดิฟายเออร์ (type 7/10/12/17)</summary>
    [JsonProperty("modifiers")] public Dictionary<string, float> Modifiers;

    /// <summary>รางวัลแบบโมดิฟายเออร์เดียว + ค่า (type 9 = ActionEnhancement)</summary>
    [JsonProperty("modifier")] public string Modifier;

    [JsonProperty("value")] public float Value;
}

/// <summary>นิยามโมดิฟายเออร์ใน <c>skill/modifiers.json</c> (154 รายการ)</summary>
internal class SkillModifierJson
{
    /// <summary>Shared.Ability.StatType — 0 = Basic, 1 = Derived</summary>
    [JsonProperty("type")] public int Type;

    /// <summary>เฉพาะ type 0: หมายเลข Shared.Ability.Basic ที่โมดิฟายเออร์นี้ไปเพิ่ม</summary>
    [JsonProperty("stat")] public int Stat = -1;

    /// <summary>"plus" = บวกตรง ๆ · "rate" = คูณเป็นสัดส่วนกับค่าฐาน (มีเฉพาะ type 0)</summary>
    [JsonProperty("operation")] public string Operation;

    /// <summary>Shared.Ability.IncreaseType — 0 = Amount (บวก), 1 = Ratio (สัดส่วน)</summary>
    [JsonProperty("increase_type")] public int IncreaseType = -1;

    /// <summary>วิธีรวมค่าจากหลายแหล่ง: "sum" (ปกติ) / "greatest" / "least"</summary>
    [JsonProperty("reduce_type")] public string ReduceType;
}

/// <summary>หนึ่งอาชีพใน <c>player/jobs.json</c> (8 อาชีพ ตรงกับดัชนี job ที่ /players ส่งมา)</summary>
internal class JobJson
{
    /// <summary>[skillId, level, subId] — สกิลที่แจกให้ตอนสร้างตัวละคร</summary>
    [JsonProperty("given_skills")] public object[][] GivenSkills;

    /// <summary>หมายเลขหมวด → เลเวลหมวดตั้งต้น (ของทุกอาชีพคือหมวดถนัด = 20)</summary>
    [JsonProperty("category_levels")] public Dictionary<int, int> CategoryLevels;
}

/// <summary>
/// ค่าต่อสู้ฐานของผู้เล่นจาก <c>entity_types/players.json</c> → key "player"
///
/// ⚠️ ต้องอ่านไฟล์นี้ซ้ำเองแทนที่จะใช้ <c>Yaml.PlayerType</c> ที่ <c>Support/DataStore.cs:35</c>
/// โหลดไว้แล้ว เพราะคลาสนั้นพอร์ตมาแค่บล็อก survival (มี Attack/Defense/InventoryCapacity
/// แต่ไม่มี accuracy/critical/dodge/…) และ <c>Support/</c> เป็นไฟล์ที่ห้ามแก้ในรอบนี้
/// อ่านครั้งเดียวตอนบูต ⇒ ไม่กระทบ performance
/// </summary>
internal class PlayerBaseStatsJson
{
    [JsonProperty("attack")] public float Attack;
    [JsonProperty("accuracy")] public float Accuracy;
    [JsonProperty("critical")] public float Critical;
    [JsonProperty("attack_rating")] public float AttackRating;
    [JsonProperty("counter_power")] public float CounterPower;
    [JsonProperty("defense")] public float Defense;
    [JsonProperty("dodge")] public float Dodge;
    [JsonProperty("blow_resistance")] public float BlowResistance;
    [JsonProperty("knock_back_resistance")] public float KnockBackResistance;
    [JsonProperty("inventory_capacity")] public float InventoryCapacity;
}

/// <summary>เฉพาะบล็อกที่ <c>Yaml.Constants</c> (Support/, ห้ามแก้) ยังไม่ได้พอร์ต</summary>
internal class SkillConstantsJson
{
    /// <summary>RepresentType → (หมายเลข Derived → น้ำหนัก) — สูตรพลังรบ/คราฟต์/เก็บของ</summary>
    [JsonProperty("represent_powers")] public Dictionary<int, Dictionary<int, float>> RepresentPowers;

    [JsonProperty("max_levels")] public Dictionary<string, int> MaxLevels;

    [JsonProperty("skill_points")] public Dictionary<string, int> SkillPoints;

    [JsonProperty("skill")] public Dictionary<string, float> Skill;
}

#pragma warning restore CS0649

// ═══════════════════════════════════════════════════════════════════════════════════
//  สถานะสกิลที่เก็บลงเซฟ (PlayerContext.Storage["server_skills"] เป็น byte[] JSON)
//  ⚠️ ไม่ได้เพิ่มฟิลด์ใน PlayerContext เพราะไฟล์นั้นห้ามแก้ในรอบนี้ — Storage เป็น
//     dict<string, byte[]> ที่มีอยู่แล้วและถูกเซฟ/โหลดพร้อม context อยู่แล้ว
// ═══════════════════════════════════════════════════════════════════════════════════

internal class SkillSave
{
    /// <summary>เผื่ออนาคตต้องแปลงรูปแบบเซฟ</summary>
    [JsonProperty("v")] public int Version = 1;

    /// <summary>exp สะสมทั้งหมด (ไม่ใช่ exp ในเลเวล) — เลเวลคำนวณจากค่านี้เสมอ</summary>
    [JsonProperty("exp")] public int Exp;

    /// <summary>skillId → subId → เลเวลที่เรียนไว้</summary>
    [JsonProperty("learned")] public Dictionary<string, Dictionary<string, int>> Learned = new();

    /// <summary>หมายเลขหมวด → สถานะหมวด</summary>
    [JsonProperty("categories")] public Dictionary<int, SkillCategorySave> Categories = new();

    /// <summary>จำนวนครั้งที่ถอนสกิลไปแล้ว (ของจริง constants.json → skill_untrain.max_count = 15)</summary>
    [JsonProperty("untrained")] public int UntrainedCount;
}

internal class SkillCategorySave
{
    [JsonProperty("lv")] public int Level = 1;

    /// <summary>exp ภายในเลเวลปัจจุบัน (client คิดสัดส่วนจาก exp_needed[Level] ตรง ๆ)</summary>
    [JsonProperty("exp")] public int Exp;

    /// <summary>unix time ที่เริ่มวิจัย (0 = ไม่ได้วิจัยอยู่)</summary>
    [JsonProperty("rs")] public double ResearchStart;

    /// <summary>unix time ที่วิจัยจะเสร็จ (0 = ไม่ได้วิจัยอยู่)</summary>
    [JsonProperty("re")] public double ResearchEnd;

    /// <summary>เวลาที่ย่นได้แล้วจากการเล่นระหว่างรอวิจัย (วินาที)</summary>
    [JsonProperty("rsv")] public float ResearchSaved;
}

// ═══════════════════════════════════════════════════════════════════════════════════
//  ตารางข้อมูลจริง โหลดครั้งเดียวทั้งเซิร์ฟ
//  ⚠️ ไม่ได้ต่อสายใน Support/DataStore.cs เพราะไฟล์นั้นห้ามแก้ในรอบนี้ ⇒ โหลดเองแบบ lazy
//     (เธรดเดียว: Player ถูกสร้างบนลูปเกมเท่านั้น ดู Program.cs)
// ═══════════════════════════════════════════════════════════════════════════════════

internal static class SkillDataStore
{
    private static bool _loaded;

    /// <summary>หมวด → bundleId → subId → โหนดเรียงตามเลเวล — จาก skill/skills.json</summary>
    public static Dictionary<int, Dictionary<string, Dictionary<string, SkillNodeJson[]>>> Skills { get; private set; }

    /// <summary>หมายเลขหมวด → ตาราง exp/เวลาวิจัย — จาก skill/categories.json</summary>
    public static Dictionary<int, SkillCategoryJson> Categories { get; private set; }

    /// <summary>rewardId → ผลของรางวัล — จาก skill/rewards.json</summary>
    public static Dictionary<string, SkillRewardJson> Rewards { get; private set; }

    /// <summary>modifierId → นิยาม — จาก skill/modifiers.json</summary>
    public static Dictionary<string, SkillModifierJson> Modifiers { get; private set; }

    /// <summary>job index → สกิล/เลเวลหมวดตั้งต้น — จาก player/jobs.json</summary>
    public static Dictionary<int, JobJson> Jobs { get; private set; }

    /// <summary>ค่าต่อสู้ฐานของผู้เล่น — จาก entity_types/players.json → "player"</summary>
    public static PlayerBaseStatsJson BaseStats { get; private set; }

    /// <summary>บล็อกที่ Yaml.Constants ยังไม่ได้พอร์ต — จาก constants.json</summary>
    public static SkillConstantsJson Constants { get; private set; }

    /// <summary>bundleId → หมวดที่มันสังกัด (ไล่หาจาก Skills ทุกครั้งจะช้า)</summary>
    public static Dictionary<string, int> CategoryOfBundle { get; } = new();

    /// <summary>modifierId → Derived ที่มันไปเพิ่ม (mapping ของเรา — ดูหมายเหตุใน BuildDerivedMap)</summary>
    public static Dictionary<string, Derived> DerivedOfModifier { get; } = new();

    /// <summary>เลเวลผู้เล่นสูงสุด — จาก constants.json → max_levels.player (60)</summary>
    public static int MaxPlayerLevel { get; private set; } = 60;

    /// <summary>แต้มสกิลตั้งต้น — จาก constants.json → skill_points.initial (10)</summary>
    public static int InitialSkillPoints { get; private set; } = 10;

    /// <summary>เพดาน exp หมวดที่ได้ต่อครั้ง — จาก constants.json → skill.exp_increase_limit (3)</summary>
    public static float CategoryExpLimit { get; private set; } = 3f;

    /// <summary>เพดานสัดส่วนที่ย่นเวลาวิจัยได้ — constants.json → skill.research_reduce_time_limit (0.25)</summary>
    public static float ResearchReduceLimit { get; private set; } = 0.25f;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        Skills = Json.ReadFromFile<Dictionary<int, Dictionary<string, Dictionary<string, SkillNodeJson[]>>>>("skill/skills")
                 ?? new Dictionary<int, Dictionary<string, Dictionary<string, SkillNodeJson[]>>>();
        Categories = Json.ReadFromFile<Dictionary<int, SkillCategoryJson>>("skill/categories")
                     ?? new Dictionary<int, SkillCategoryJson>();
        Rewards = Json.ReadFromFile<Dictionary<string, SkillRewardJson>>("skill/rewards")
                  ?? new Dictionary<string, SkillRewardJson>();
        Modifiers = Json.ReadFromFile<Dictionary<string, SkillModifierJson>>("skill/modifiers")
                    ?? new Dictionary<string, SkillModifierJson>();
        Jobs = Json.ReadFromFile<Dictionary<int, JobJson>>("player/jobs")
               ?? new Dictionary<int, JobJson>();
        Constants = Json.ReadFromFile<SkillConstantsJson>("constants") ?? new SkillConstantsJson();

        var playerTypes = Json.ReadFromFile<Dictionary<string, PlayerBaseStatsJson>>("entity_types/players");
        BaseStats = playerTypes != null && playerTypes.TryGetValue("player", out var bs) ? bs : new PlayerBaseStatsJson();

        if (Constants.MaxLevels != null && Constants.MaxLevels.TryGetValue("player", out int maxLv) && maxLv > 0)
        {
            MaxPlayerLevel = maxLv;
        }
        if (Constants.SkillPoints != null && Constants.SkillPoints.TryGetValue("initial", out int sp))
        {
            InitialSkillPoints = sp;
        }
        if (Constants.Skill != null)
        {
            if (Constants.Skill.TryGetValue("exp_increase_limit", out float lim)) CategoryExpLimit = lim;
            if (Constants.Skill.TryGetValue("research_reduce_time_limit", out float rl)) ResearchReduceLimit = rl;
        }

        foreach (var (cat, bundles) in Skills)
        {
            if (bundles == null) continue;
            foreach (string bundleId in bundles.Keys)
            {
                CategoryOfBundle[bundleId] = cat;
            }
        }
        BuildDerivedMap();

        Console.WriteLine($"[skill] โหลดตารางสกิล: {CategoryOfBundle.Count} bundle · {Categories.Count} หมวด · " +
                          $"{Rewards.Count} รางวัล · {Modifiers.Count} โมดิฟายเออร์ ({DerivedOfModifier.Count} ตัวผูกกับ Derived) · " +
                          $"{Jobs.Count} อาชีพ · เลเวลสูงสุด {MaxPlayerLevel}");
    }

    /// <summary>
    /// ผูก modifierId → <see cref="Derived"/>
    ///
    /// ⚠️ **การผูกนี้เป็นการอนุมานของเรา** — <c>skill/modifiers.json</c> บอกแค่ว่าโมดิฟายเออร์ตัวนี้
    /// เป็นสาย Basic หรือ Derived (<c>type</c>) แต่ **ไม่มีฟิลด์ไหนบอกว่าเป็น Derived ตัวไหน**
    /// (มีแต่ <c>stat</c> ของฝั่ง Basic เท่านั้น) จึงต้องเดาจากชื่อ: ตัด suffix
    /// <c>_plus/_mul/_ratio/_rate</c> แล้วแปลง snake_case → ชื่อ enum
    ///
    /// ตรวจแล้วกับข้อมูลจริง: จับคู่ได้ 40 ตัวและ **ไม่มีตัวไหนจับผิด** (เช่น gathering_plus →
    /// Gathering(204), max_modular_size → MaxModularSize(234), guard_protection_ratio →
    /// GuardProtection(9)) ส่วนอีก 98 ตัวไม่มี Derived คู่กัน (damage_bonus, cooldown_reduction, …)
    /// ⇒ พวกนั้นอยู่ใน <c>Statistics.Modifiers</c> อย่างเดียว ซึ่งถูกแล้ว เพราะ client อ่านมันด้วยชื่อ
    /// (client/StatisticsSystem.cs:411-418 GetModifier(string))
    /// </summary>
    private static void BuildDerivedMap()
    {
        string[] suffixes = { "_plus", "_mul", "_ratio", "_rate" };
        foreach (var (id, def) in Modifiers)
        {
            if (def == null || def.Type != (int)StatType.Derived) continue;
            // ตัดซ้ำจนไม่เหลือ suffix — มีตัวที่ซ้อนสองชั้นจริงในไฟล์: critical_rate_plus → critical
            string baseName = id;
            bool stripped = true;
            while (stripped)
            {
                stripped = false;
                foreach (string suffix in suffixes)
                {
                    if (!baseName.EndsWith(suffix, StringComparison.Ordinal)) continue;
                    baseName = baseName[..^suffix.Length];
                    stripped = true;
                    break;
                }
            }
            Derived derived = baseName.ToCamelCase().ToEnum(Derived.Invalid);
            if (derived != Derived.Invalid) DerivedOfModifier[id] = derived;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════════
//  ระบบสกิล + เลเวล/exp ของผู้เล่นหนึ่งคน
// ═══════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// [5 ก.ย. 2026] ระบบสกิล/เลเวล — ดู <see cref="SkillTuning"/> สำหรับค่าที่เราตั้งเอง
///
/// ทำไมเลเวลถึงสำคัญกว่าที่คิด: client **ไม่คำนวณเลเวลเองเลย** มันรออย่างเดียว
/// (<c>client/StatisticsSystem.cs:33</c> <c>Level =&gt; Statistics.Value.Level</c>) และถ้าไม่เคยได้
/// <c>Statistics</c> เลย <c>Level</c> จะเป็น **-1** ⇒ หน้าล่องเรือล็อกทุกเกาะโดยไม่มี error ให้เห็น
///
/// เลเวลอยู่ 3 ที่ที่ต้องตรงกันเสมอ (ดู <see cref="ApplyLevel"/>):
///   1. <c>PlayerContext.PlayerInfo.PlayerLevel</c> — หน้าเลือกตัวละคร (/accounts)
///   2. <c>PlayerContext.AppearPlayer.Level</c>     — เลเวลลอยเหนือหัวที่คนอื่นเห็น
///   3. <c>Statistics.Level</c>                     — เลเวลที่ระบบในเกมใช้ทุกอย่าง
/// </summary>
public partial class Player
{
    private SkillSave _skills;

    /// <summary>ค่าที่คำนวณไว้รอบล่าสุด — ใช้จับ "เลเวลเปลี่ยน" โดยไม่ต้องคำนวณซ้ำ</summary>
    private int _skillLevel = 1;

    private void RegisterSkillHandlers()
    {
        SkillDataStore.EnsureLoaded();
        LoadOrCreateSkillState();

        // ⚠️ ต้องตั้งเลเวลให้ตรงก่อน constructor จะเดินต่อไปเรียก SendStatistics() (Player.cs:524)
        //    เพราะบรรทัดนั้นอ่าน _context.AppearPlayer.Level ไปใส่ Statistics.Level ตรง ๆ
        //    (Player.cs:597) ⇒ ถ้าไม่ตั้งตรงนี้ push ชุดแรกจะพกเลเวลเก่าติดไปด้วย
        ApplyLevel(LevelFromExp(_skills.Exp), sendStats: false);

        // ── ค่าสถานะทั้งชุด ──────────────────────────────────────────────────────
        // ⚠️ ทับ handler เดิมที่ Player.cs:234 ตั้งใจ: Connection.Recv ลง key ตาม TypeCode และ
        //    "ลบของเดิมก่อนใส่ใหม่" (GameCode/Durango.Online/Connection.cs:195-198)
        //    RegisterSystemHandlers() ถูกเรียกที่ Player.cs:522 = หลังบรรทัด 234 ⇒ ของเราชนะ
        //    ของเดิมส่งแค่ Level/Exp/Swimming ส่วนตัวนี้ส่งครบทั้ง 8 ฟิลด์
        _connection.Recv(delegate(GetStatistics msg, PacketHeader header)
        {
            PollResearch();
            SendFullStatistics();
        });

        // ── ต้นไม้สกิล ──────────────────────────────────────────────────────────
        // ทับ handler เดิมที่ Player.cs:283 (ตอบชุดว่าง) ด้วยของจริง
        // ⚠️ SkillSystem.OnReceiveSkillMsg เป็นตัวเดียวที่ปลดล็อก _isInitSkills
        //    (client/Durango.Logic/SkillSystem.cs:271-278) ⇒ ยังต้องตอบเสมอแม้ไม่มีสกิลเลย
        _connection.Recv(delegate(GetSkills msg, PacketHeader header)
        {
            PollResearch();
            SendSkills(header.Seq);
        });

        _connection.Recv(delegate(LearnSkill msg, PacketHeader header) { HandleLearnSkill(msg, header.Seq); });
        _connection.Recv(delegate(UntrainSkill msg, PacketHeader header) { HandleUntrainSkill(msg, header.Seq); });
        _connection.Recv(delegate(ResearchSkillCategory msg, PacketHeader header) { HandleResearch(msg, header.Seq); });
        _connection.Recv(delegate(CancelSkillCategoryResearch msg, PacketHeader header) { HandleCancelResearch(msg.SkillCategory); });
        _connection.Recv(delegate(SkipSkillCategoryResearch msg, PacketHeader header) { HandleSkipResearch(msg.SkillCategory); });

        // ── ประตูหลังสำหรับเทส ────────────────────────────────────────────────────
        // Cheat ถูกลงทะเบียนไว้แล้วที่ Player.cs:107 ⇒ ห่อของเดิมแล้วส่งต่อ
        // (HandleCheatMsg เป็น private ของ partial class เดียวกัน เรียกข้ามไฟล์ได้)
        // ⚠️ ถ้าระบบอื่นจะห่อ Cheat ด้วย ต้องระวังว่าใครลงทะเบียนทีหลังชนะ — ตอนนี้มีแค่ที่นี่ที่เดียว
        _connection.Recv(delegate(Cheat msg, PacketHeader header)
        {
            if (TryHandleSkillCheat(msg._Cheat)) return;
            HandleCheatMsg(msg._Cheat, header.Seq);
        });
    }

    // ───────────────────────────────────────────────────────────────────────────────
    //  เซฟ/โหลด + สกิลเริ่มต้นตามอาชีพ
    // ───────────────────────────────────────────────────────────────────────────────

    private void LoadOrCreateSkillState()
    {
        _context.Storage ??= new Dictionary<string, byte[]>();
        if (_context.Storage.TryGetValue(SkillTuning.StorageKey, out byte[] blob) && blob is { Length: > 0 })
        {
            _skills = Json.Read<SkillSave>(blob);
        }
        if (_skills != null)
        {
            _skills.Learned ??= new Dictionary<string, Dictionary<string, int>>();
            _skills.Categories ??= new Dictionary<int, SkillCategorySave>();
            return;
        }

        // ยังไม่เคยมีสถานะสกิล = ตัวละครนี้เพิ่งสร้าง (หรือเป็นเซฟเก่าก่อนมีระบบนี้)
        _skills = new SkillSave { Exp = ExpForLevel(SkillTuning.StartLevel) };
        GrantJobStartingSkills();
        SaveSkillState();
    }

    /// <summary>
    /// แจกสกิล/เลเวลหมวดตั้งต้นตามอาชีพ — ข้อมูลจริงจาก <c>data/assets/player/jobs.json</c>
    ///
    /// ⚠️ **เซิร์ฟไม่ได้เก็บอาชีพไว้ที่ไหนเลย** — <c>Gateway.cs:213</c> อ่าน <c>job</c> จาก POST /players
    /// แล้วใช้แค่เลือกเสื้อ ไม่ได้เขียนลง <c>PlayerContext</c> (และไฟล์ทั้งสองห้ามแก้ในรอบนี้)
    /// ⇒ ย้อนอาชีพจาก **เสื้อที่ใส่อยู่** แทน: <c>Gateway.cs:212</c> มีอาเรย์
    ///   [engineer, officeworker, student, farmer, waiter, soldier, homeworker, jobless]
    ///   ที่ index = job id เป๊ะ แล้วใส่ให้เป็น <c>EquippedItems["body"]</c>
    ///
    /// ยืนยันว่าลำดับตรงกับ jobs.json จริง (ไม่ได้เดา) จาก given_items ที่ผูกกับชื่ออาชีพ:
    ///   job 3 = <c>hoe_twohand_farmer_starting</c> ตรงกับ clothes_farmer (index 3)
    ///   job 4 = <c>needle_waiter_starting</c>     ตรงกับ clothes_waiter (index 4)
    /// และ category_levels ก็ตรงสาย: 3→13 (Farming), 4→10 (Armorcrafting), 5→2 (MeleeCombat)
    /// </summary>
    private void GrantJobStartingSkills()
    {
        int job = DetectJobFromBodyItem();
        if (job < 0 || !SkillDataStore.Jobs.TryGetValue(job, out JobJson data) || data == null)
        {
            Console.WriteLine($"[skill] {ShortId()} ไม่รู้อาชีพ (เสื้อไม่ตรงตาราง) — ไม่แจกสกิลตั้งต้น");
            return;
        }

        if (data.CategoryLevels != null)
        {
            foreach (var (cat, level) in data.CategoryLevels)
            {
                CategoryState(cat).Level = Math.Max(1, level);
            }
        }

        int granted = 0;
        foreach (object[] entry in data.GivenSkills ?? Array.Empty<object[]>())
        {
            // รูปแบบในไฟล์: ["bow", 1, "__base__"] — Newtonsoft ให้ string/long มา
            if (entry == null || entry.Length < 3) continue;
            string skillId = entry[0]?.ToString();
            string subId = entry[2]?.ToString();
            if (!int.TryParse(entry[1]?.ToString(), out int level) || string.IsNullOrEmpty(skillId)) continue;
            if (!_skills.Learned.TryGetValue(skillId, out var subs))
            {
                subs = new Dictionary<string, int>();
                _skills.Learned[skillId] = subs;
            }
            subs[subId] = Math.Max(subs.GetValueOrDefault(subId), level);
            granted++;
        }
        Console.WriteLine($"[skill] {ShortId()} อาชีพ {job} → แจกสกิล {granted} ตัว, " +
                          $"หมวดถนัด {string.Join(",", data.CategoryLevels?.Select(p => $"{p.Key}=lv{p.Value}") ?? Array.Empty<string>())}");
    }

    /// <summary>ย้อน job id จากเสื้อที่ใส่ (ดูเหตุผลที่ <see cref="GrantJobStartingSkills"/>)</summary>
    private int DetectJobFromBodyItem()
    {
        // ลำดับเดียวกับ Gateway.cs:212 — index = job id
        string[] clothes =
        {
            "clothes_engineer", "clothes_officeworker", "clothes_student", "clothes_farmer",
            "clothes_waiter", "clothes_soldier", "clothes_homeworker", "clothes_jobless"
        };
        if (_context.EquippedItems == null || !_context.EquippedItems.TryGetValue("body", out string itemId)) return -1;
        int idx = _context.InventoryItems?.FindIndex(item => item.Id == itemId) ?? -1;
        if (idx < 0) return -1;
        return Array.IndexOf(clothes, _context.InventoryItems[idx].Prototype);
    }

    private void SaveSkillState()
    {
        _context.Storage[SkillTuning.StorageKey] = Json.WriteToBytes(_skills);
        OnContextChanged();
    }

    private SkillCategorySave CategoryState(int category)
    {
        if (!_skills.Categories.TryGetValue(category, out SkillCategorySave state))
        {
            state = new SkillCategorySave();
            _skills.Categories[category] = state;
        }
        return state;
    }

    private string ShortId() => EntityId[..Math.Min(8, EntityId.Length)];

    // ───────────────────────────────────────────────────────────────────────────────
    //  เลเวล / exp
    // ───────────────────────────────────────────────────────────────────────────────

    /// <summary>ตาราง <c>level_thresholds</c> จริงจาก data/assets/statistics/player.json (80 ช่อง)</summary>
    private static int[] LevelThresholds => Singleton<Yaml.PlayerStatistics>.Instance?.LevelThresholds;

    /// <summary>
    /// exp สะสม → เลเวล
    ///
    /// นิยามของตารางยืนยันจาก client: <c>GetExpRange(level)</c> ใช้ <c>thresholds[level-2]</c>
    /// เป็นขอบล่างและ <c>thresholds[level-1]</c> เป็นขอบบน (client/StatisticsSystem.cs:363-374)
    /// ⇒ <c>thresholds[0] = 11</c> คือ exp ที่ต้องมีถึงจะเป็นเลเวล 2
    /// </summary>
    private static int LevelFromExp(int exp)
    {
        int[] table = LevelThresholds;
        if (table == null || table.Length == 0) return 1;
        int level = 1;
        while (level - 1 < table.Length && exp >= table[level - 1] && level < SkillDataStore.MaxPlayerLevel)
        {
            level++;
        }
        return level;
    }

    /// <summary>exp สะสมขั้นต่ำของเลเวลนี้ (เลเวล 1 = 0)</summary>
    private static int ExpForLevel(int level)
    {
        int[] table = LevelThresholds;
        if (level <= 1 || table == null) return 0;
        int idx = Math.Min(level - 2, table.Length - 1);
        return idx < 0 ? 0 : table[idx];
    }

    /// <summary>
    /// exp ดิบที่ให้ต่อ "1 ครั้ง" ณ เลเวลปัจจุบัน
    ///
    /// **สูตรของเรา** แต่ตัวเลขทั้งหมดมาจากตารางจริง: ระยะ exp ของเลเวลนี้
    /// (<c>thresholds[lv-1] - thresholds[lv-2]</c>) หารด้วย <see cref="SkillTuning.ActionsPerLevel"/>
    /// ⇒ ทุกเลเวลใช้จำนวนครั้งเท่ากันโดยประมาณ ไม่ว่าตารางจะชันแค่ไหน
    /// (ถ้าให้ exp คงที่ 3 หน่วย: lv1→2 ใช้ 4 ครั้ง แต่ lv59→60 ใช้ 144,115 ครั้ง — เล่นไม่ได้)
    /// </summary>
    private static int ExpPerAction(int level)
    {
        int span = ExpForLevel(level + 1) - ExpForLevel(level);
        if (span <= 0) span = 11; // เลเวลสุดท้าย/ตารางหาย — ใช้ระยะของ lv1→2
        return Math.Max(1, span / SkillTuning.ActionsPerLevel);
    }

    /// <summary>
    /// **API หลักสำหรับระบบอื่น** — เพิ่ม exp ดิบให้ผู้เล่นคนนี้
    ///
    /// ระบบอื่น (เก็บของ/คราฟต์/ตี) ควรเรียก <see cref="AddExpForAction"/> มากกว่า เพราะมันคิด
    /// จำนวน exp ให้ตามเลเวลและแจก exp หมวดสกิลไปพร้อมกัน — ตัวนี้ไว้ให้กรณีที่รู้ตัวเลขแน่นอนแล้ว
    /// </summary>
    /// <param name="amount">exp ดิบ (0 หรือติดลบ = ไม่ทำอะไร)</param>
    /// <param name="reason">เขียนลง log ให้ไล่ที่มาได้ตอนสมดุลเพี้ยน</param>
    public void AddExp(int amount, string reason)
    {
        if (amount <= 0 || _skills == null) return;

        int before = _skillLevel;
        int cap = ExpCap();
        _skills.Exp = (int)Math.Min((long)_skills.Exp + amount, cap);

        // แจ้ง client ให้ขึ้นตัวเลข "+exp" ลอย ๆ (client/Durango.UI/IndicatorGroup.cs:48-56)
        Send(new ExpGained { EntityId = EntityId, Exp = amount, BonusExp = 0, ResistanceExp = 0 });

        int after = LevelFromExp(_skills.Exp);
        if (after != before)
        {
            Console.WriteLine($"[skill] {ShortId()} เลเวล {before} → {after} (exp {_skills.Exp}, จาก {reason})");
            ApplyLevel(after, sendStats: true);
        }
        else
        {
            // ไม่ได้เลเวลขึ้นก็ต้องส่ง Statistics ใหม่อยู่ดี เพราะแถบ exp ของ client
            // อ่านจาก Statistics.Exp ไม่ได้คำนวณเอง (client/StatisticsSystem.cs:377-392)
            SendFullStatistics();
        }
        SaveSkillState();
    }

    /// <summary>
    /// **API สำหรับระบบอื่น** — ให้ exp จากการกระทำหนึ่งครั้ง (แปลงน้ำหนัก → exp ตามเลเวล)
    /// พร้อมแจก exp หมวดสกิลที่เกี่ยวข้อง
    /// </summary>
    /// <param name="weight">น้ำหนักจาก <see cref="SkillTuning"/> เช่น <c>SkillTuning.GatherWeight</c></param>
    /// <param name="category">หมวดสกิลที่ได้ exp ไปด้วย (null = ไม่มีหมวด)</param>
    /// <param name="reason">คำอธิบายสั้น ๆ ลง log</param>
    public void AddExpForAction(int weight, SkillCat? category, string reason)
    {
        if (weight <= 0 || _skills == null) return;
        // save: false เพราะ AddExp ข้างล่างเซฟให้อยู่แล้ว — OnContextChanged เขียนไฟล์ .player
        // ทั้งไฟล์ทุกครั้ง (Core/GameServer.cs:186-194) ⇒ อย่าเขียนสองรอบต่อการกระทำหนึ่งครั้ง
        if (category.HasValue) AddCategoryExp(category.Value, SkillTuning.CategoryExpPerAction, save: false);
        AddExp(ExpPerAction(_skillLevel) * weight, reason);
    }

    /// <summary>เพดาน exp — ค้างที่เลเวลสูงสุดแต่ยังให้แถบเดินจนเต็มช่องสุดท้าย</summary>
    private static int ExpCap()
    {
        int[] table = LevelThresholds;
        int max = SkillDataStore.MaxPlayerLevel;
        if (table == null || max - 1 >= table.Length) return int.MaxValue;
        return Math.Max(0, table[max - 1] - 1);
    }

    /// <summary>
    /// ตั้งเลเวลให้ตรงกันทั้ง 3 ที่ (ดูหมายเหตุหัวคลาส)
    ///
    /// ⚠️ **จงใจไม่ broadcast AppearPlayer** ถึงแม้แผนงานจะเขียนไว้ — ตรวจโค้ด client แล้วพบว่า
    /// มันไม่ได้ผลและมีผลข้างเคียงหนัก:
    ///   · ส่งให้ **ตัวเอง** = client ทำลาย PlayerBehavior เดิมแล้วสร้างใหม่ + Teleport
    ///     (client/PlayerManager.cs:311-327) ⇒ ผู้เล่นกระตุก/วาร์ปทุกครั้งที่เลเวลขึ้น
    ///   · ส่งให้ **คนอื่นที่เห็นเราอยู่แล้ว** = client เข้า else แล้วเจอ <c>playerBehavior != null</c>
    ///     ⇒ **ไม่ทำอะไรเลย** (client/PlayerManager.cs:329-338) เลเวลเหนือหัวไม่อัปเดตอยู่ดี
    /// และ <c>World.BroadCast</c> (Core/World.cs:211-217) ส่งให้ทุกคน "รวมตัวเอง" แยกไม่ได้
    /// (World._players เป็น private ไม่มี accessor)
    /// ⇒ อัปเดตแค่ค่าใน context ก็พอ คนที่เข้ามาเห็นเราใหม่จะได้เลเวลที่ถูกต้องเสมอ
    /// ถ้าอยากให้เลเวลเหนือหัวเด้งทันที ต้องแก้ฝั่ง client ให้อัปเดต PlayerBehavior ตัวที่มีอยู่แล้ว
    /// </summary>
    private void ApplyLevel(int level, bool sendStats)
    {
        _skillLevel = level;
        _context.PlayerInfo.PlayerLevel = level;   // 1) หน้าเลือกตัวละคร
        _context.AppearPlayer.Level = level;       // 2) เลเวลลอยเหนือหัว (คนที่เห็นเราใหม่)
        if (sendStats) SendFullStatistics();       // 3) เลเวลที่เกมใช้ทุกอย่าง
    }

    // ───────────────────────────────────────────────────────────────────────────────
    //  หมวดสกิล + การวิจัย
    // ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// เพิ่ม exp หมวดสกิล — เลเวลหมวดขึ้นเองจนกว่าจะชนขั้นที่ต้อง "วิจัย"
    ///
    /// ตรรกะยืนยันจากข้อมูลจริง: <c>skill/categories.json</c> → <c>research_times</c> เป็น 0 ทุกเลเวล
    /// ยกเว้น 20/25/30/35/40/45/50/55/59 ⇒ เลเวล 1–19 ขึ้นได้เองด้วย exp เฉย ๆ ส่วนเลเวลหมุดหมาย
    /// ต้องรอเวลา ตรงกับที่ client เช็คใน <c>IsReadyToResearch()</c>
    /// (client/Durango.Logic.Skill/Category.cs:58-72 — ต้องมี research_times &gt; 0 ถึงจะกดวิจัยได้)
    /// </summary>
    /// <param name="save">false = ผู้เรียกจะเซฟเองทีหลัง (กันเขียนไฟล์เซฟซ้ำในการกระทำเดียว)</param>
    public void AddCategoryExp(SkillCat category, int amount, bool save = true)
    {
        if (_skills == null || amount <= 0) return;
        amount = (int)Math.Min(amount, SkillDataStore.CategoryExpLimit);
        if (!SkillDataStore.Categories.TryGetValue((int)category, out SkillCategoryJson table) || table?.ExpNeeded == null) return;

        SkillCategorySave state = CategoryState((int)category);
        double reduced = 0.0;

        if (state.ResearchEnd > 0.0)
        {
            // กำลังวิจัยอยู่ = exp ที่ได้กลายเป็น "เวลาที่ย่นได้" แทนที่จะเป็นเลเวล
            // สูตรจริง constants.json → skill.research_time_reduce = "exp * (level / 10.)"
            reduced = ReduceResearchTime(state, table, amount);
        }
        else
        {
            state.Exp += amount;
            while (state.Level < SkillDataStore.MaxPlayerLevel &&
                   table.ExpNeeded.TryGetValue(state.Level, out int need) && need > 0 && state.Exp >= need)
            {
                if (table.ResearchTimes != null && table.ResearchTimes.TryGetValue(state.Level, out int secs) && secs > 0)
                {
                    // ถึงขั้นหมุดหมายแล้ว — ค้าง exp ไว้ (client เช็ค Exp >= exp_needed ถึงจะให้กดวิจัย)
                    state.Exp = need;
                    break;
                }
                state.Exp -= need;
                state.Level++;
            }
        }

        // ตัวเลขลอย "+exp หมวด" / "ย่นเวลาวิจัย" ที่มุมจอ (client/Durango.UI/IndicatorGroup.cs:79-100)
        Send(new SkillCategoryExperienced
        {
            Category = category,
            Exp = state.ResearchEnd > 0.0 ? 0 : amount,
            ResearchReducedTime = reduced
        });
        if (save) SaveSkillState();
    }

    /// <summary>ย่นเวลาวิจัยตามสูตรจริง — คืนจำนวนวินาทีที่ย่นได้รอบนี้</summary>
    private static double ReduceResearchTime(SkillCategorySave state, SkillCategoryJson table, int exp)
    {
        double total = state.ResearchEnd - state.ResearchStart + state.ResearchSaved;
        double limit = total * SkillDataStore.ResearchReduceLimit;   // constants.json → research_reduce_time_limit
        double delta = exp * (state.Level / 10.0);                   // constants.json → research_time_reduce
        delta = Math.Min(delta, limit - state.ResearchSaved);
        if (delta <= 0.0) return 0.0;
        state.ResearchSaved += (float)delta;
        state.ResearchEnd -= delta;
        return delta;
    }

    /// <summary>
    /// ปิดงานวิจัยที่ครบเวลาแล้ว
    ///
    /// ⚠️ **ข้อจำกัดที่แก้ไม่ได้ในรอบนี้**: เซิร์ฟไม่มีจุดให้ระบบใหม่เกาะรอบเวลา —
    /// <c>Player.Process()</c> (Player.cs:1338) ถูกเรียกทุกเฟรมแต่เป็นเมธอดใน Player.cs ซึ่งห้ามแก้
    /// ⇒ ตรวจแบบ lazy: ทุกครั้งที่มีคำขอที่เกี่ยวข้องเข้ามา หรือทุกครั้งที่ได้ exp
    /// ผลคือถ้าผู้เล่นยืนนิ่งจนวิจัยเสร็จ ตัวนับฝั่ง client จะถึง 0 ก่อน แล้วเลเวลหมวดจะขึ้นจริง
    /// ตอนขยับ/เก็บของครั้งถัดไป (หรือตอนต่อใหม่) — ไม่มีข้อมูลหาย แค่ช้าไปนิดเดียว
    /// </summary>
    private void PollResearch()
    {
        if (_skills == null) return;
        double now = Times.UnixTimeNow();
        bool changed = false;
        foreach (var (cat, state) in _skills.Categories)
        {
            if (state.ResearchEnd <= 0.0 || now < state.ResearchEnd) continue;
            FinishResearch(cat, state);
            changed = true;
        }
        if (changed) SaveSkillState();
    }

    private void FinishResearch(int cat, SkillCategorySave state)
    {
        if (SkillDataStore.Categories.TryGetValue(cat, out SkillCategoryJson table) &&
            table?.ExpNeeded != null && table.ExpNeeded.TryGetValue(state.Level, out int need))
        {
            state.Exp = Math.Max(0, state.Exp - need);
        }
        else
        {
            state.Exp = 0;
        }
        state.Level = Math.Min(state.Level + 1, SkillDataStore.MaxPlayerLevel);
        state.ResearchStart = 0.0;
        state.ResearchEnd = 0.0;
        state.ResearchSaved = 0f;
        Console.WriteLine($"[skill] {ShortId()} วิจัยหมวด {(SkillCat)cat} เสร็จ → หมวดเลเวล {state.Level}");
    }

    private void HandleResearch(ResearchSkillCategory msg, uint seq)
    {
        PollResearch();

        // SkipCategory = "ขอจ่ายเพื่อจบหมวดที่ค้างอยู่ แล้วเริ่มหมวดใหม่ทันที"
        // (client/Durango.UI/SkillCategoryProgressGauge.cs:275-283 ส่งมาพร้อมกันในคำขอเดียว)
        if (msg.SkipCategory.HasValue) SkipResearchNow((int)msg.SkipCategory.Value);

        int cat = (int)msg.Category;
        if (!SkillDataStore.Categories.TryGetValue(cat, out SkillCategoryJson table) || table == null)
        {
            Send(default(Failed), seq);
            return;
        }
        SkillCategorySave state = CategoryState(cat);

        // เงื่อนไขเดียวกับที่ client ใช้เปิดปุ่ม (client/Durango.Logic.Skill/Category.cs:58-72)
        // เช็คซ้ำฝั่งเซิร์ฟเพราะ client เชื่อไม่ได้
        bool alreadyResearching = _skills.Categories.Values.Any(s => s.ResearchEnd > 0.0);
        int seconds = table.ResearchTimes != null && table.ResearchTimes.TryGetValue(state.Level, out int t) ? t : 0;
        int need = table.ExpNeeded != null && table.ExpNeeded.TryGetValue(state.Level, out int n) ? n : -1;
        if (alreadyResearching || seconds <= 0 || need <= 0 || state.Exp < need || state.Level >= _skillLevel)
        {
            Console.WriteLine($"[skill] {ShortId()} ขอวิจัย {msg.Category} ไม่ผ่าน " +
                              $"(กำลังวิจัยอยู่={alreadyResearching} เวลา={seconds} exp={state.Exp}/{need} " +
                              $"หมวดlv={state.Level} ผู้เล่นlv={_skillLevel})");
            Send(default(Failed), seq);
            return;
        }

        double now = Times.UnixTimeNow();
        state.ResearchStart = now;
        state.ResearchEnd = now + seconds;
        state.ResearchSaved = 0f;
        Console.WriteLine($"[skill] {ShortId()} เริ่มวิจัย {msg.Category} (หมวดเลเวล {state.Level} → {state.Level + 1}, {seconds} วิ)");
        SaveSkillState();
        // client ไม่ได้รอ reply ที่ seq นี้ (SkillSystem.cs:530 Send เฉย ๆ ไม่มี .On)
        // ตัวที่มันฟังคือ Skills แบบ push ⇒ push ชุดใหม่ทั้งชุด
        SendSkills();
    }

    private void HandleCancelResearch(SkillCat category)
    {
        SkillCategorySave state = CategoryState((int)category);
        if (state.ResearchEnd <= 0.0) return;
        state.ResearchStart = 0.0;
        state.ResearchEnd = 0.0;
        state.ResearchSaved = 0f;
        Console.WriteLine($"[skill] {ShortId()} ยกเลิกวิจัย {category}");
        SaveSkillState();
        SendSkills();
    }

    private void HandleSkipResearch(SkillCat category)
    {
        if (!SkipResearchNow((int)category)) return;
        SaveSkillState();
        SendSkills();
    }

    /// <summary>จบงานวิจัยทันที (ราคาอยู่ที่ <see cref="SkillTuning.ResearchSkipCost"/>)</summary>
    private bool SkipResearchNow(int cat)
    {
        if (!_skills.Categories.TryGetValue(cat, out SkillCategorySave state) || state.ResearchEnd <= 0.0) return false;
        FinishResearch(cat, state);
        return true;
    }

    // ───────────────────────────────────────────────────────────────────────────────
    //  เรียน / ถอนสกิล
    // ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// เรียนสกิล — เช็คเงื่อนไขชุดเดียวกับที่ client ใช้ตัดสินว่าโหนดนี้ <c>State.Learnable</c> ไหม
    /// (client/Durango.Logic.Skill/Node.cs:131-150)
    ///
    /// ⚠️ ตอบสำเร็จต้องเป็น <c>OK</c> (TypeCode 1231) เท่านั้น — client เช็คเลขนี้ตรง ๆ
    /// (client/Durango.Logic/SkillSystem.cs:359-365) อย่างอื่นถือว่าไม่สำเร็จหมด
    /// </summary>
    private void HandleLearnSkill(LearnSkill msg, uint seq)
    {
        PollResearch();
        // SubId ว่าง/null = สกิลแม่ — ต้องแปลงก่อนใช้เป็น key ของ dict (Dictionary โยน exception
        // ถ้า key เป็น null และมันจะพาเธรดรับแพ็กเก็ตร่วงไปทั้งเส้น)
        string subId = string.IsNullOrEmpty(msg.SubId) ? BaseSubId : msg.SubId;
        SkillNodeJson node = FindNode(msg.SkillId, subId, msg.Level, out int category);
        if (node == null)
        {
            Send(default(Failed), seq);
            return;
        }

        var subs = _skills.Learned.GetValueOrDefault(msg.SkillId);
        int current = subs?.GetValueOrDefault(subId) ?? 0;
        int categoryLevel = CategoryState(category).Level;
        // สกิลลูกต้องมีสกิลแม่ (__base__) ก่อน — เงื่อนไข NoHaveParent ของ client
        bool hasParent = subId == BaseSubId || (subs?.GetValueOrDefault(BaseSubId) ?? 0) > 0;
        int remain = RemainSkillPoints();

        if (msg.Level != current + 1 || categoryLevel < node.CategoryLevel || !hasParent || remain < node.SkillPoint)
        {
            Console.WriteLine($"[skill] {ShortId()} เรียน {msg.SkillId}/{subId} lv{msg.Level} ไม่ผ่าน " +
                              $"(ตอนนี้ lv{current} หมวดlv {categoryLevel}/{node.CategoryLevel} " +
                              $"แม่={hasParent} แต้ม {remain}/{node.SkillPoint})");
            Send(default(Failed), seq);
            return;
        }

        if (subs == null)
        {
            subs = new Dictionary<string, int>();
            _skills.Learned[msg.SkillId] = subs;
        }
        subs[subId] = msg.Level;
        Console.WriteLine($"[skill] {ShortId()} เรียน {msg.SkillId}/{subId} → lv{msg.Level} (-{node.SkillPoint} แต้ม)");
        SaveSkillState();
        Send(default(OK), seq);
        SendSkills();
        SendFullStatistics();   // สกิลใหม่ = ค่าสถานะเปลี่ยน (rewards → modifiers)
    }

    /// <summary>
    /// ถอนสกิลคืนแต้ม
    ///
    /// **ทำให้ฟรี** (ไม่หัก voucher/เงิน) เพราะเซิร์ฟนี้ยังไม่มีระบบเงินตรา — ของจริงมีราคาอยู่ที่
    /// <c>costs.json</c> → <c>skill_untrain</c> (30 หน่วยสกุล 1) และเพดานที่
    /// <c>constants.json</c> → <c>skill_untrain</c> {free_count 5, max_count 15}
    /// เรายังนับจำนวนครั้งไว้ใน <c>UntrainedCount</c> เพื่อให้เปิดระบบราคาทีหลังได้โดยไม่เสียข้อมูล
    /// </summary>
    private void HandleUntrainSkill(UntrainSkill msg, uint seq)
    {
        string subId = string.IsNullOrEmpty(msg.SubId) ? BaseSubId : msg.SubId;
        SkillNodeJson node = FindNode(msg.SkillId, subId, msg.Level, out _);
        var subs = _skills.Learned.GetValueOrDefault(msg.SkillId);
        int current = subs?.GetValueOrDefault(subId) ?? 0;
        if (node == null || node.UntrainDisabled || current < 1 || msg.Level != current)
        {
            Send(default(Failed), seq);
            return;
        }
        subs[subId] = current - 1;
        _skills.UntrainedCount++;
        Console.WriteLine($"[skill] {ShortId()} ถอน {msg.SkillId}/{subId} → lv{current - 1} (+{node.SkillPoint} แต้ม)");
        SaveSkillState();
        Send(default(OK), seq);
        SendSkills();
        SendFullStatistics();
    }

    /// <summary>หาโหนดสกิลในตารางจริง (คืน null ถ้าไม่มี) พร้อมบอกหมวดที่มันสังกัด</summary>
    private static SkillNodeJson FindNode(string skillId, string subId, int level, out int category)
    {
        category = -1;
        if (string.IsNullOrEmpty(skillId) || level < 1) return null;
        if (!SkillDataStore.CategoryOfBundle.TryGetValue(skillId, out category)) return null;
        if (!SkillDataStore.Skills.TryGetValue(category, out var bundles) ||
            !bundles.TryGetValue(skillId, out var subskills) ||
            !subskills.TryGetValue(string.IsNullOrEmpty(subId) ? BaseSubId : subId, out SkillNodeJson[] nodes)) return null;
        return level <= nodes.Length ? nodes[level - 1] : null;
    }

    /// <summary>คีย์ของ "สกิลแม่" ในไฟล์ skills.json (client/Durango.Logic.Skill/Bundle.cs:11)</summary>
    private const string BaseSubId = "__base__";

    /// <summary>
    /// แต้มสกิลทั้งหมด = ของจริง (<c>constants.json</c> → <c>skill_points.initial</c> = 10)
    /// + <see cref="SkillTuning.SkillPointsPerLevel"/> ต่อเลเวล (ค่าของเรา)
    /// </summary>
    private int TotalSkillPoints() =>
        SkillDataStore.InitialSkillPoints + (_skillLevel - 1) * SkillTuning.SkillPointsPerLevel;

    /// <summary>
    /// แต้มที่ใช้ไปแล้ว = ผลรวม <c>skill_point</c> ของโหนด 1..Level ทุกสกิล
    /// (สูตรเดียวกับ client/Durango.Logic.Skill/Skill.cs:76-84 เพื่อให้ตัวเลขสองฝั่งตรงกันเป๊ะ)
    /// </summary>
    private int UsedSkillPoints()
    {
        int used = 0;
        foreach (var (skillId, subs) in _skills.Learned)
        {
            foreach (var (subId, level) in subs)
            {
                for (int lv = 1; lv <= level; lv++)
                {
                    SkillNodeJson node = FindNode(skillId, subId, lv, out _);
                    if (node != null) used += node.SkillPoint;
                }
            }
        }
        return used;
    }

    private int RemainSkillPoints() => TotalSkillPoints() - UsedSkillPoints();

    // ───────────────────────────────────────────────────────────────────────────────
    //  ส่งข้อความออก
    // ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ต้นไม้สกิลทั้งชุด — คำตอบของ <c>GetSkills</c>
    ///
    /// ⚠️ ทุกฟิลด์ต้องเป็น array/dict ว่าง ห้าม null: client วน <c>foreach (msg.Categories)</c>
    /// ตรง ๆ ไม่เช็ค null (client/Durango.Logic/SkillSystem.cs:229)
    /// </summary>
    private void SendSkills(uint replyOf = 0u)
    {
        var list = new List<SkillBundle>();
        foreach (var (skillId, subs) in _skills.Learned)
        {
            if (!SkillDataStore.CategoryOfBundle.TryGetValue(skillId, out int cat)) continue;
            var levels = subs.Where(pair => pair.Value > 0).ToDictionary(pair => pair.Key, pair => pair.Value);
            if (levels.Count == 0) continue;
            list.Add(new SkillBundle { Category = (SkillCat)cat, SkillId = skillId, Levels = levels });
        }

        var categories = new Dictionary<SkillCat, Messages.SkillCategory>();
        foreach (int cat in SkillDataStore.Categories.Keys)
        {
            SkillCategorySave state = CategoryState(cat);
            categories[(SkillCat)cat] = new Messages.SkillCategory
            {
                Level = state.Level,
                Exp = state.Exp,
                ResearchTime = BuildResearchTime(cat, state),
                Researching = BuildResearching(state)
            };
        }

        Send(new Messages.Skills
        {
            SkillList = list.ToArray(),
            SkillPoint = TotalSkillPoints(),
            Categories = categories,
            UntrainedCount = _skills.UntrainedCount,
            // "สกิลแนะนำ" เป็นของระบบไกด์ (LearningGuideSystem) ที่เซิร์ฟยังไม่ทำ — ส่งว่างไว้ก่อน
            AdvisedSkills = Array.Empty<Messages.Skill>(),
            AdvisedSkillCategories = new Dictionary<SkillCat, int>()
        }, replyOf);
    }

    private static SkillCategoryResearchTime? BuildResearchTime(int cat, SkillCategorySave state)
    {
        if (state.ResearchEnd <= 0.0) return null;
        if (!SkillDataStore.Categories.TryGetValue(cat, out SkillCategoryJson table) || table?.ResearchTimes == null) return null;
        if (!table.ResearchTimes.TryGetValue(state.Level, out int seconds) || seconds <= 0) return null;
        return new SkillCategoryResearchTime
        {
            DefaultNeededTime = seconds,
            // client เอาไปโชว์เป็น "% ที่ย่นได้" (client/Durango.UI/SkillCategoryProgressGauge.cs:219-225)
            ReduceRate = seconds > 0 ? state.ResearchSaved / seconds : 0f,
            ReduceUntil = state.ResearchEnd
        };
    }

    private static SkillCategoryResearching? BuildResearching(SkillCategorySave state)
    {
        if (state.ResearchEnd <= 0.0) return null;
        return new SkillCategoryResearching
        {
            StartedAt = state.ResearchStart,
            EndsAt = state.ResearchEnd,
            SavedTime = state.ResearchSaved,
            // ⚠️ ห้ามเป็น null — client เรียก .Get() ตรง ๆ (SkillCategoryProgressGauge.cs:236)
            SkipCost = new Gauge(new[] { new GaugeNode(0.0, SkillTuning.ResearchSkipCost) })
        };
    }

    /// <summary>
    /// <c>Statistics</c> ฉบับเต็ม — ค่าสถานะทั้งตัวละคร
    ///
    /// ⚠️ **ไม่ได้แก้ <c>SendStatistics()</c> เดิม** (Player.cs:588) เพราะไฟล์นั้นห้ามแก้ในรอบนี้
    /// ตัวนี้ทับได้เฉพาะทาง <c>GetStatistics</c> ซึ่งเป็นทางที่ client ใช้จริงตอนเข้าเกม
    /// (client/StatisticsSystem.cs:101-105 AddOnReady) — push ชุดแรกที่ Player.cs:524 ยังเป็นของเดิม
    /// แต่ <see cref="RegisterSkillHandlers"/> ตั้ง <c>AppearPlayer.Level</c> ไว้ก่อนแล้ว
    /// ⇒ push ชุดแรกมีเลเวล **ถูก** (ขาดแค่ Exp/ค่าอื่นที่ถูกแทนที่ในไม่กี่ ms ถัดมา)
    ///
    /// **จุดที่ควรต่อสายให้ถูกจริง ๆ**: เปลี่ยน Player.cs:588-600 ให้เรียก SendFullStatistics()
    /// แทน แล้วลบ handler GetStatistics เดิมที่ Player.cs:234-237 ทิ้ง
    /// </summary>
    private void SendFullStatistics()
    {
        Statistics msg = default;

        // 1) ค่า Derived ฐาน — จากข้อมูลจริง entity_types/players.json → "player"
        var deriveds = new Dictionary<Derived, float>
        {
            // ต้นฉบับ (client/Durango.Online/Player.cs:407) ตั้ง Swimming 100 ตายตัว — คงไว้
            { Derived.Swimming, 100f }
        };
        PlayerBaseStatsJson b = SkillDataStore.BaseStats;
        deriveds[Derived.Attack] = b.Attack;
        deriveds[Derived.Accuracy] = b.Accuracy;
        deriveds[Derived.Critical] = b.Critical;
        deriveds[Derived.AttackRating] = b.AttackRating;
        deriveds[Derived.CounterPower] = b.CounterPower;
        deriveds[Derived.Defense] = b.Defense;
        deriveds[Derived.Dodge] = b.Dodge;
        deriveds[Derived.BlowResistance] = b.BlowResistance;
        deriveds[Derived.KnockBackResistance] = b.KnockBackResistance;
        deriveds[Derived.InventoryCapacity] = b.InventoryCapacity;

        // ⚠️ ต้องเรียกหลังตั้งค่าฐาน และห้ามไปเขียนทับหลังจากนี้ — ตัวนี้เติมค่าสูงสุดของหลอด
        //    (LifeMax/MaxEnergy/FatigueMax/MaxHealth) + เกณฑ์ FatigueCaution/FatigueDanger
        //    ซึ่งเป็นของระบบ survival ทั้งหมด (Core/SurvivalState.cs:543-562)
        SurvivalState.FillDeriveds(deriveds);

        // 2) โมดิฟายเออร์จากสกิลที่เรียนแล้ว — สกิล → rewards.json → modifiers
        Dictionary<string, float> modifiers = CollectModifiers();

        // 3) พลังพื้นฐาน + เอาโมดิฟายเออร์ไปบวก/คูณ
        var basics = new Dictionary<Basic, int>();
        var basicRaw = new Dictionary<Basic, float>();
        foreach (Basic ability in Enum.GetValues<Basic>())
        {
            if (ability == Basic.Invalid) continue;
            basicRaw[ability] = SkillTuning.BasicAbilityBase;
        }
        ApplyModifiers(modifiers, basicRaw, deriveds);
        foreach (var (ability, value) in basicRaw)
        {
            basics[ability] = (int)Math.Round(value);
        }

        msg.BasicAbilities = basics;
        msg.DerivedsAbilities = deriveds;
        msg.Modifiers = modifiers;
        msg.Level = _skillLevel;
        msg.Exp = _skills?.Exp ?? 0;
        msg.RepresentPowers = BuildRepresentPowers(deriveds);
        BuildResistances(out var resistLevels, out var resistExps);
        msg.ResistanceLevels = resistLevels;
        msg.ResistanceExps = resistExps;
        Send(msg);
    }

    /// <summary>
    /// รวมโมดิฟายเออร์จากสกิลที่เรียนแล้วทั้งหมด
    ///
    /// เส้นทางข้อมูลจริง: <c>skills.json</c> โหนด → <c>rewards[]</c> → <c>rewards.json</c> →
    /// <c>modifiers</c> (หลายตัว) หรือ <c>modifier</c>+<c>value</c> (ตัวเดียว, RewardType 9)
    /// วิธีรวมค่าซ้ำใช้ <c>reduce_type</c> จาก <c>modifiers.json</c> (sum/greatest/least)
    /// </summary>
    private Dictionary<string, float> CollectModifiers()
    {
        var result = new Dictionary<string, float>();
        foreach (var (skillId, subs) in _skills.Learned)
        {
            foreach (var (subId, level) in subs)
            {
                for (int lv = 1; lv <= level; lv++)
                {
                    SkillNodeJson node = FindNode(skillId, subId, lv, out _);
                    foreach (string rewardId in node?.Rewards ?? Array.Empty<string>())
                    {
                        if (!SkillDataStore.Rewards.TryGetValue(rewardId, out SkillRewardJson reward) || reward == null) continue;
                        if (reward.Modifiers != null)
                        {
                            foreach (var (id, value) in reward.Modifiers) Accumulate(result, id, value);
                        }
                        if (!string.IsNullOrEmpty(reward.Modifier)) Accumulate(result, reward.Modifier, reward.Value);
                    }
                }
            }
        }
        return result;
    }

    private static void Accumulate(Dictionary<string, float> into, string id, float value)
    {
        if (!into.TryGetValue(id, out float old))
        {
            into[id] = value;
            return;
        }
        string reduce = SkillDataStore.Modifiers.GetValueOrDefault(id)?.ReduceType;
        into[id] = reduce switch
        {
            "greatest" => Math.Max(old, value),
            "least" => Math.Min(old, value),
            _ => old + value   // "sum" และค่าที่ไม่รู้จัก
        };
    }

    /// <summary>
    /// แปลงโมดิฟายเออร์เป็นค่าสถานะจริง
    ///
    /// · <c>type 0</c> (Basic) ใช้ฟิลด์ <c>stat</c> + <c>operation</c> ("plus" บวก / "rate" คูณฐาน)
    /// · <c>type 1</c> (Derived) ใช้ mapping ตามชื่อ (ดู <see cref="SkillDataStore.DerivedOfModifier"/>)
    ///   โดย <c>increase_type</c> 0 = บวกตรง ๆ, 1 = คูณเป็นสัดส่วนกับค่าฐาน
    /// · ตัวที่ไม่มีคู่ Derived (98 จาก 138) ปล่อยไว้ใน <c>Statistics.Modifiers</c> อย่างเดียว
    ///   ซึ่งถูกต้อง เพราะ client อ่านพวกนี้ด้วยชื่อผ่าน <c>GetModifier(string)</c>
    /// </summary>
    private static void ApplyModifiers(Dictionary<string, float> modifiers,
                                       Dictionary<Basic, float> basics,
                                       Dictionary<Derived, float> deriveds)
    {
        foreach (var (id, value) in modifiers)
        {
            SkillModifierJson def = SkillDataStore.Modifiers.GetValueOrDefault(id);
            if (def == null) continue;

            if (def.Type == (int)StatType.Basic)
            {
                var ability = (Basic)def.Stat;
                if (!basics.TryGetValue(ability, out float current)) continue;
                basics[ability] = def.Operation == "rate"
                    ? current * (1f + value)
                    : current + value;
                continue;
            }

            if (!SkillDataStore.DerivedOfModifier.TryGetValue(id, out Derived derived)) continue;
            float baseValue = deriveds.GetValueOrDefault(derived);
            deriveds[derived] = def.IncreaseType == (int)IncreaseType.Ratio
                ? baseValue * (1f + value)
                : baseValue + value;
        }
    }

    /// <summary>
    /// พลังรบ/พลังคราฟต์/พลังเก็บของ — **สูตรและน้ำหนักเป็นข้อมูลจริง** จาก
    /// <c>constants.json</c> → <c>represent_powers</c> (RepresentType → หมายเลข Derived → น้ำหนัก)
    /// เช่น CombatPower = Dodge×0.0005 + Defense×0.0006 + Attack×0.0003 + …
    /// </summary>
    private static Dictionary<RepresentType, float> BuildRepresentPowers(Dictionary<Derived, float> deriveds)
    {
        var result = new Dictionary<RepresentType, float>();
        var table = SkillDataStore.Constants?.RepresentPowers;
        if (table == null) return result;
        foreach (var (type, weights) in table)
        {
            float sum = 0f;
            foreach (var (derivedId, weight) in weights)
            {
                sum += deriveds.GetValueOrDefault((Derived)derivedId) * weight;
            }
            result[(RepresentType)type] = sum;
        }
        return result;
    }

    /// <summary>
    /// เลเวล/exp ค่าต้านทาน (ร้อน/หนาว/พิษ …)
    ///
    /// ชนิดที่มีจริงมาจาก <c>constants.json</c> → <c>resistance.types_by_biome</c>
    /// (ตัวเดียวกับที่ <c>BuildResistanceExpCaps()</c> ใน Player.cs:646 ใช้)
    /// ⚠️ ยังส่งเลเวล 1 / exp 0 ทุกชนิด เพราะยังไม่มีระบบสะสม exp ต้านทาน (ต้องผูกกับชีวนิเวศ
    /// ที่ผู้เล่นยืนอยู่ + สภาพอากาศ ซึ่งเป็นระบบคนละก้อน) — ค่านี้ตรงกับที่ client เดาไว้อยู่แล้ว
    /// (client/StatisticsSystem.cs:55 <c>ResistanceLevels.Get(type, 1)</c>) แต่ส่งไปให้ครบ
    /// จะได้ไม่ต้องพึ่ง default และตอน <c>GetCurrentAndMaxResistanceExp</c> คำนวณได้ถูก
    /// </summary>
    private static void BuildResistances(out Dictionary<Derived, int> levels, out Dictionary<Derived, int> exps)
    {
        levels = new Dictionary<Derived, int>();
        exps = new Dictionary<Derived, int>();
        var typeByBiome = Singleton<Yaml.Constants>.Instance?.Resistance.TypeByBiome;
        if (typeByBiome == null) return;
        foreach (Derived type in typeByBiome.Values)
        {
            levels[type] = 1;
            exps[type] = 0;
        }
    }

    // ───────────────────────────────────────────────────────────────────────────────
    //  ประตูหลังสำหรับเทส
    // ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// cheat ของระบบสกิล — คืน true ถ้าจัดการเองแล้ว (ไม่ต้องส่งต่อให้ <c>HandleCheatMsg</c>)
    ///
    /// มีไว้เทสอย่างเดียว เพราะตอนนี้ยังไม่มีระบบไหนเรียก <see cref="AddExpForAction"/>
    /// (เก็บของ/คราฟต์/ต่อสู้ อยู่คนละไฟล์ที่ทีมอื่นดูแล) ⇒ ถ้าไม่มีทางนี้จะเทสเลเวลอัพไม่ได้เลย
    ///   <c>exp 500</c>  = ได้ exp ดิบ 500
    ///   <c>lv 12</c>    = กระโดดไปเลเวล 12 (ตั้ง exp เท่าเกณฑ์ของเลเวลนั้น)
    ///   <c>catexp 7 5</c> = ให้ exp หมวด 7 (Gathering) 5 หน่วย
    /// </summary>
    private bool TryHandleSkillCheat(string cheat)
    {
        string[] parts = (cheat ?? string.Empty).ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        switch (parts[0])
        {
            case "exp" when int.TryParse(parts[1], out int amount):
                AddExp(amount, "cheat");
                return true;

            case "lv" when int.TryParse(parts[1], out int level):
            {
                level = Math.Clamp(level, 1, SkillDataStore.MaxPlayerLevel);
                _skills.Exp = ExpForLevel(level);
                ApplyLevel(level, sendStats: true);
                SaveSkillState();
                Console.WriteLine($"[skill] {ShortId()} cheat ตั้งเลเวล {level} (exp {_skills.Exp})");
                return true;
            }

            case "catexp" when parts.Length >= 3 && int.TryParse(parts[1], out int cat) && int.TryParse(parts[2], out int catExp):
                AddCategoryExp((SkillCat)cat, catExp);
                SendSkills();
                return true;

            default:
                return false;
        }
    }
}
