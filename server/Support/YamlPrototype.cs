using System.Collections.Generic;
using Durango.Utils;
using Newtonsoft.Json;
using Shared.Item;

namespace Yaml;

// พอร์ตจาก nexonSRC/Yaml/Prototype.cs (item prototype — อ่านจาก data/assets/item/prototype_data.json)
public class Prototype
{
    [JsonProperty("min_level")] public int MinLevel;
    [JsonProperty("max_level")] public int MaxLevel;
    [JsonProperty("item_description")] public Gettext Description;
    [JsonProperty("name")] public Gettext Name;
    [JsonProperty("icon")] public string Icon;
    [JsonProperty("category")] public string Category;
    [JsonProperty("sub_categories")] public string[] SubCategories;
    [JsonProperty("dump_locked")] public bool DumpLocked;
    [JsonProperty("dyeables")] public List<ColorChannel> Dyeables;
    [JsonProperty("help")] public Gettext Help;
    [JsonProperty("color_r")] public string ColorR;
    [JsonProperty("color_g")] public string ColorG;
    [JsonProperty("color_b")] public string ColorB;
    [JsonProperty("hiding_color")] public bool HidingColor;
    [JsonProperty("immune_to_time")] public bool ImmuneToTime;
    [JsonProperty("time_limited")] public bool TimeLimited;
    [JsonProperty("size")] public int Size;
    [JsonProperty("tags")] public Dictionary<string, string> Tags;
}
