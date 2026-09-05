using System.Collections.Generic;
using Durango.Utils;
using Newtonsoft.Json;
using Yaml.Util;

namespace Yaml;

// พอร์ตจาก nexonSRC/Yaml — Pet/Pets/Natural/Chapter/Chapters/StoryYaml/Emotions
// (รวมไฟล์เล็ก ๆ ที่เซิร์ฟแท้ใช้)

public class Pet
{
    [JsonProperty("type")] public string Type;
    [JsonProperty("name")] public Gettext Name;
    [JsonProperty("vehicle_entity_type")] public int VehicleEntityType;
    [JsonProperty("is_ridable")] public bool IsRidable;
    [JsonProperty("is_fightable")] public bool IsFightable;
    [JsonProperty("is_reinifiable")] public bool IsReinifiable;
    [JsonProperty("is_craft")] public bool IsCraft;
}

// จาก /assets/pet/pets_for_client — dict<int entity type, Pet>
public class Pets : SingletonDict<int, Pet>
{
}

// จาก /assets/entity_types/natural — dict<int entity type, Natural>
public class Natural
{
    public string collectible_id { get; set; }
    public string icon { get; set; }
    public string[] sprite_names { get; set; }
    public Gettext name { get; set; }
    public bool additive { get; set; }
    public string particle { get; set; }
    public Dictionary<string, bool> survivability { get; set; }
    public bool is_craft { get; set; }
}

public class Chapter
{
    [JsonProperty("chapter")] public int ChapterNum;
    [JsonProperty("title")] public Gettext Title;
    [JsonProperty("description")] public Gettext Description;
    [JsonProperty("image")] public string Image;
    [JsonProperty("movie")] public Dictionary<string, string> Movie;
    [JsonProperty("quests")] public string[] Quests;
}

public class Chapters
{
    [JsonProperty("chapters")] public Chapter[] ChapterList;
}

// จาก /assets/quests/epics_for_client — dict<category, Chapters> ("sunset" = epic quest)
public class StoryYaml : SingletonDict<string, Chapters>
{
}

public class Emoticon
{
    [JsonProperty("id")] public string Id;
    [JsonProperty("default")] public bool Default;
    [JsonProperty("free")] public bool Free;
    [JsonProperty("icon")] public string Icon;
}

// จาก /assets/emotions — คืนรายการ motion/emoticon ที่ผู้เล่นใช้ได้ (GetAvailableEmotions)
public class Emotions
{
    [JsonProperty("emoticons")] public Emoticon[] Emoticons;
    [JsonProperty("motions")] public Dictionary<string, Motion> Motions;
}

public class Motion
{
    [JsonProperty("id")] public string Id;
}

// จาก /assets/statistics/player — ตารางเลเวล/เพดาน exp ของผู้เล่น
// ต้นฉบับฝั่ง client (nexonSRC/Yaml/PlayerStatistics.cs) มีแค่ 2 ฟิลด์แรก เพราะ
// resistance_exp_grown_caps เป็นข้อมูลของฝั่งเซิร์ฟล้วน — client ไม่เคยอ่านไฟล์ส่วนนี้
// มันรอรับผลสำเร็จรูปทาง message ResistanceExpCaps อย่างเดียว (StatisticsSystem.cs:93,113)
// ⇒ ฟิลด์ ResistanceExpGrownCaps + คลาส ResistanceExpGrownCap เป็นของที่เราเพิ่มเอง
//    (ชื่อ property ตาม key ในไฟล์ ไม่ได้ก๊อปชื่อคลาสมาจากต้นฉบับ)
public class PlayerStatistics : Singleton<PlayerStatistics>
{
    [JsonProperty("level_thresholds")] public int[] LevelThresholds;

    // ชื่อฟิลด์ตามต้นฉบับ client: JsonProperty เป็น resistance_level_thresholds แต่ตัวแปรชื่อ ResistanceExpTable
    [JsonProperty("resistance_level_thresholds")] public int[] ResistanceExpTable;

    // key = เลเวลต้านทาน (มี 1..50 ตรงกับจำนวนช่องของ resistance_level_thresholds)
    // value = ขั้นเพดานของเลเวลนั้น เรียงจากเรตสูงไปต่ำ ขั้นสุดท้าย cap_amount = null (ไม่มีเพดาน)
    [JsonProperty("resistance_exp_grown_caps")] public Dictionary<int, List<ResistanceExpGrownCap>> ResistanceExpGrownCaps;
}

public class ResistanceExpGrownCap
{
    // null = ขั้นสุดท้าย ได้ exp ต่อไม่จำกัดที่เรตนี้
    [JsonProperty("cap_amount")] public int? CapAmount;

    [JsonProperty("exp_rate")] public float ExpRate;
}
