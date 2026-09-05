using System.Collections.Generic;
using Newtonsoft.Json;
using Yaml.Util;

namespace Yaml;

// พอร์ตจาก /assets/entity_types/players.json (ตารางเดียวกับที่ client โหลดผ่าน gateway ตอน Online)
// ต้นฉบับฝั่ง client แตกไฟล์นี้เป็นคลาส EntityType/Survivability หลายชั้น — เซิร์ฟใช้แค่บล็อก
// survival / online_momenta / max_effected_by (สร้างหลอดสถานะของผู้เล่น) จึงพอร์ตเท่าที่ใช้
//
// รูปแบบไฟล์: dict<key, PlayerType> โดย key "player" คือค่ากลาง ส่วน "1000:male"/"1001:female"
// สืบทอดผ่าน "__extend__" (ยังไม่ได้ใช้ เพราะสองตัวนั้นมีแต่ path โมเดล ไม่มีค่าสมดุล)

public class PlayerType
{
    /// <summary>ชื่อ key ที่สืบทอดค่ามา — ยังไม่ได้ใช้ (ตัวที่ extend มีแต่ path โมเดล)</summary>
    [JsonProperty("__extend__")] public string Extend;

    /// <summary>นิยามหลอดสถานะ: life / stamina / fatigue / groggy / energy / health</summary>
    [JsonProperty("survival")] public Dictionary<string, SurvivalGaugeDef> Survival;

    /// <summary>
    /// แรงคงที่ที่บวกเข้ากับ velocity ฐานตลอดเวลาที่ "ออนไลน์อยู่"
    /// ของ player มีตัวเดียว: fatigue 0.083333 ซึ่งหักล้างกับ velocity ฐาน -0.083333 พอดี
    /// ⇒ อยู่เฉย ๆ ความเหนื่อยนิ่ง ไม่ขึ้นไม่ลง (ตามที่ข้อมูลต้นฉบับออกแบบไว้)
    /// </summary>
    [JsonProperty("online_momenta")] public Dictionary<string, float> OnlineMomenta;

    /// <summary>
    /// หลอด → หมายเลข Shared.Ability.Derived ที่เป็นตัวกำหนดค่าสูงสุดของหลอดนั้น
    /// ของ player: fatigue→6 (FatigueMax) · energy→3 (MaxEnergy) · groggy→0 · health→0 (MaxHealth)
    /// ใช้ตอนประกอบ Statistics (2040) ให้ตัวเลขบนหน้าสถานะตรงกับหลอดจริง
    /// </summary>
    [JsonProperty("max_effected_by")] public Dictionary<string, int> MaxEffectedBy;

    [JsonProperty("inventory_capacity")] public int InventoryCapacity;

    [JsonProperty("defense")] public int Defense;

    [JsonProperty("attack")] public int Attack;
}

/// <summary>นิยามหลอดหนึ่งหลอดในบล็อก survival ของ players.json</summary>
public class SurvivalGaugeDef
{
    /// <summary>"Gauge" = หลอดจริง · "ProxyGauge" = ชื่อเรียกอีกชื่อของหลอดอื่น (ดู Ref)</summary>
    [JsonProperty("type")] public string Type;

    /// <summary>ค่าเริ่มต้น (ตอนสร้างตัวละครใหม่)</summary>
    [JsonProperty("value")] public float? Value;

    [JsonProperty("max")] public float? Max;

    [JsonProperty("min")] public float? Min;

    /// <summary>หน่วยต่อวินาที — เส้นแนวโน้มที่ client เอาไป interpolate เอง</summary>
    [JsonProperty("velocity")] public float Velocity;

    /// <summary>ProxyGauge เท่านั้น: "stamina.max_gauge" / "life.max_gauge"</summary>
    [JsonProperty("ref")] public string Ref;

    /// <summary>หลอดที่ทำหน้าที่เป็น "ค่าสูงสุด" ของหลอดนี้ (life.max_gauge = health, stamina.max_gauge = energy)</summary>
    [JsonProperty("max_gauge")] public SurvivalGaugeDef MaxGauge;

    [JsonProperty("min_gauge")] public SurvivalGaugeDef MinGauge;

    [JsonProperty("decr_outbound")] public string DecrOutbound;

    [JsonProperty("incr_outbound")] public string IncrOutbound;

    /// <summary>
    /// object ไม่ใช่ string เพราะไฟล์จริงมีทั้งตัวเลข (groggy: 1) และสูตรข้อความ
    /// ("past" / "maximum * 0.1") — เซิร์ฟยังไม่มีตัวคำนวณสูตร จึงเก็บดิบไว้เฉย ๆ
    /// </summary>
    [JsonProperty("on_reset")] public object OnReset;

    [JsonProperty("decr_fallback")] public Dictionary<string, float> DecrFallback;
}

/// <summary>จาก /assets/entity_types/players.json — dict&lt;key, PlayerType&gt;</summary>
public class PlayerTypes : SingletonDict<string, PlayerType>
{
    /// <summary>key ของค่ากลางที่ทุกเพศสืบทอด</summary>
    public const string PlayerKey = "player";

    public static PlayerType Player => Get(PlayerKey);
}
