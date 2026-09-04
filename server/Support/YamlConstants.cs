using Newtonsoft.Json;
using Yaml.Util;

namespace Yaml;

// พอร์ตจาก nexonSRC/Yaml — Constants (พอร์ตบางส่วน)
// ต้นฉบับมีฟิลด์ทั้งเกม (dash/explorer/item/market/.../artifact_floor) — เซิร์ฟแท้ใช้แค่
// PersonalRegion (เลือก region template ตอนสร้างตัวละคร) ⇒ พอร์ตเท่าที่ใช้ ที่เหลือ client มีอยู่แล้ว
public class Constants : Singleton<Constants>
{
    [JsonProperty("personal_region")]
    public PersonalRegion PersonalRegion;

    [JsonProperty("market")]
    public MarketConstants Market;
}

public class PersonalRegion
{
    [JsonProperty("region_template_ids")]
    public List<string> RegionTemplateIds;

    [JsonProperty("expand_cost")]
    public int ExpandCost;
}

public class MarketConstants
{
    [JsonProperty("ui_enabled")]
    public bool UiEnabled;
}
