using Newtonsoft.Json;
using Shared.Ability;
using Shared.Region;
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

    // พอร์ตเพิ่ม: เซิร์ฟต้องรู้ว่า "ชนิดต้านทาน" ที่เกมใช้จริงมีอะไรบ้าง ตอนตอบ GetResistanceExpCaps
    // client วนตามชุดนี้ชุดเดียวเวลาวาดหน้าต้านทาน (Durango.UI.Popup/ResistanceInfoPopup.cs:59)
    // ⇒ ชนิดไหนไม่อยู่ใน Caps ที่ส่งไป popup จะโชว์ "ได้ exp 0%" (ResistanceInfo.cs:56)
    [JsonProperty("resistance")]
    public Resistance Resistance;

    // พอร์ตเพิ่ม [5 ก.ย. 2026]: อัตราสิ้นเปลืองพลังงานตอนเดิน — ใช้คิด velocity ของหลอด energy
    // (= stamina.max_gauge ดู entity_types/players.json) ไฟล์จริงมีคีย์เดียว speeds.moving = 0.032
    // client ไม่ parse บล็อกนี้ เป็นสูตรฝั่งเซิร์ฟล้วน ๆ
    [JsonProperty("energy")]
    public EnergyConstants Energy;

    // พอร์ตเพิ่ม [7 ก.ย. 2026]: เวลาที่ซากสัตว์อยู่บนพื้นก่อนหายไป
    // ⚠️ ไม่มีตัวนี้ = ซากอยู่ถาวรจนปิดเซิร์ฟ (AnimalManager.DiedAt ถูกเขียนแต่ไม่มีใครอ่าน)
    [JsonProperty("herd")]
    public HerdConstants Herd;
}

public class HerdConstants
{
    /// <summary>ซากอยู่กี่วินาทีก่อนหายไป — ไฟล์จริงมีคีย์เดียว = 180</summary>
    [JsonProperty("collectible_dispose_delay")]
    public double CollectibleDisposeDelay;
}

public class EnergyConstants
{
    /// <summary>ค่าในไฟล์เป็น "อัตราสิ้นเปลือง" (บวก) — ตอนใช้ต้องกลับเครื่องหมายเป็นลบ</summary>
    [JsonProperty("speeds")]
    public Dictionary<string, float> Speeds;
}

// พอร์ตจาก nexonSRC/Yaml/Resistance.cs — ชื่อฟิลด์/JsonProperty ตรงต้นฉบับ
// ข้อมูลจริงอยู่ที่ data/assets/constants.json → resistance.types_by_biome
public struct Resistance
{
    [JsonProperty("types_by_biome")]
    public Dictionary<Biome, Derived> TypeByBiome;
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
