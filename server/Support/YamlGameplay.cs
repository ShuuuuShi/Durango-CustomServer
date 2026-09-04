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
