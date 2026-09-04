using System.Collections.Generic;
using Durango.Utils;
using Durango.Utils.Extensions;
using Yaml.Util;

namespace Yaml;

// พอร์ตจาก nexonSRC/Yaml — Recipe/RecipeDict + ArtifactPrototype/Dict + Blueprint data classes
// Recipe ต้นฉบับมีหลายฟิลด์ (craft slots/effort/...) — เซิร์ฟใช้แค่ add_on (ตกแต่ง artifact)
public class Recipe
{
    public Dictionary<string, string[]> add_on;
}

// จาก /assets/item/recipes — dict<blueprint id, Recipe>
public class RecipeDict : SingletonDict<string, Recipe>
{
    private static Dictionary<string, List<string[]>> _decorations;

    private static Dictionary<string, List<string[]>> Decorations
    {
        get
        {
            if (_decorations == null)
            {
                _decorations = new Dictionary<string, List<string[]>>();
                if (Instance != null)
                {
                    foreach (var pair in Instance)
                    {
                        if (pair.Value?.add_on == null) continue;
                        foreach (var item in pair.Value.add_on)
                        {
                            if (!_decorations.ContainsKey(item.Key)) _decorations[item.Key] = new List<string[]>();
                            _decorations[item.Key].Add(item.Value);
                        }
                    }
                }
            }
            return _decorations;
        }
    }

    public static List<string[]> GetDecorations(string blueprintId) => Decorations.Get(blueprintId);

    public static bool HasDecoration(string blueprintId) => Decorations.ContainsKey(blueprintId);
}

// ArtifactPrototype — จาก /assets/entity_types/artifact (dict<int entity type, prototype>)
// ฟิลด์ชื่อ snake_case ตรงกับ JSON ⇒ Newtonsoft อ่านตรง ๆ (เหมือน ReadYaml_ArtifactPrototype ต้นฉบับ)
public class ArtifactPrototype
{
    public float bound_radius { get; set; }
    public string __name__ { get; set; }
    public string icon { get; set; }
    public bool permanent { get; set; }
    public int rotatable_directions { get; set; }
    public int[] size { get; set; }
    public int height { get; set; }
    public bool interior_set_effect { get; set; }
    public bool is_size_variable { get; set; }
    public Shared.Region.Biome[] biomes { get; set; }
    public float? depth_min { get; set; }
    public float? depth_max { get; set; }
    public string[] components { get; set; }
    public string[] client_only_components { get; set; }
    public IndicatorData indicator { get; set; }
    public Shared.Building.Exclusive exclusive { get; set; }
    public bool exterior { get; set; }
    public bool interior { get; set; }
    public bool transparent_site { get; set; }
    public ScribbleType scribble { get; set; }
    public int repair_requirement { get; set; }
    public int[][] unoccupiable_tiles { get; set; }
    public int[][] effect_tiles { get; set; }
    public bool time_limited { get; set; }
    public bool is_craft { get; set; }
    public string[] musics { get; set; }
    public string gender { get; set; }
}

public class ArtifactPrototypeDict : SingletonDict<int, ArtifactPrototype>
{
}

// รูปร่าง {"text": bool, "canvas": {...}} ของ JSON — เซิร์ฟไม่ใช้ต่อ (ฝั่ง client ทำ canvas เอง)
public class ScribbleType
{
    public bool text { get; set; }
    public CanvasInfo canvas { get; set; }

    public class CanvasInfo
    {
        public int width { get; set; }
        public int height { get; set; }
        public int frame { get; set; }
    }
}

public class IndicatorData
{
    public bool active;
    public string icon;
    public string color;
    public int size;
    public float visible_zoom;
}

// Blueprint (data layer) — จาก /assets/building/blueprints (dict<id, blueprint>)
// client เอาตัวนี้ merge กับ ArtifactPrototype เป็น Building.Blueprint ตอนรันไทม์
// เซิร์ฟทำ merge เดียวกันใน BlueprintStore
public class Blueprint
{
    public string category;
    public string subcategory;
    public Gettext name;
    public Gettext description;
    public int min_level;
    public int max_level;
    public string icon;
    public string default_look;
    public int postprocess_time;
    public string preview;
    public Dictionary<string, int> tool_tags;
    public BlueprintSlot[] slots;
    public string season;
    public string required_blueprint;
}

public class BlueprintSlot
{
    public string slot_id;
    public Gettext slot_name;
    public string size_factor;
    public int count;
    public Dictionary<string, int> required_tags;
    public Dictionary<string, int> required_materials;
    public Dictionary<string, ArtifactLook> looks;
    public SlotSourceInfo[] source_info;
    public string default_look_tag;
}

public class ArtifactLook
{
    public Gettext name;
    public string model_key;
}

public class SlotSourceInfo
{
    public string collectible_id;
    public int type;
    public string generator_id;
}
