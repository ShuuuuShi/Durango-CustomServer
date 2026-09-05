using System.Collections.Generic;
using Durango.Utils;
using Durango.Utils.Extensions;
using Newtonsoft.Json;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/CropYaml.cs + Crop.cs
// ต้นฉบับอ่าน Resources "offline/assets/crops" — ที่นี่ Json.ReadFromFile อ่าน data/assets/crops.json
public class Crop
{
    /// <summary>โมเดลตอนโตเต็มที่ — สุ่มเลือกหนึ่งอันด้วย hash ของช่อง (ต้นฉบับทำแบบเดียวกัน)</summary>
    [JsonProperty("grown_looks")]
    public string[] GrownLooks;

    /// <summary>โมเดลระหว่างทาง — <c>growing</c> = ต้นอ่อน · <c>dead</c> = ตายแล้ว</summary>
    [JsonProperty("look")]
    public Dictionary<string, string> Look;

    /// <summary>สูตรเวลาปลูก (วินาที) เช่น <c>"(60 * level + 600) * 0.5"</c> — ตัวแปรคือ level</summary>
    [JsonProperty("grows_until")]
    public string GrowsUntil;

    /// <summary>น้ำที่ต้องรด (หน่วยเดียวกับที่ Water.y ในโปรโตคอลใช้)</summary>
    [JsonProperty("required_water")]
    public float RequiredWater;

    [JsonProperty("required_fertilizer")]
    public int RequiredFertilizer;

    /// <summary>ไบโอมที่พืชชนิดนี้ชอบ — ใช้คิด <c>Farming.BiomeFitness</c></summary>
    [JsonProperty("preference_land")]
    public string PreferenceLand;

    /// <summary>โอกาสรอด 0-1</summary>
    [JsonProperty("survivability")]
    public float Survivability;

    /// <summary>prototype ของผลผลิตที่ได้ — ใช้เป็นชื่อ "작물" บนป้ายข้อมูล</summary>
    [JsonProperty("grows_to")]
    public string GrowsTo;

    [JsonProperty("additional_product")]
    public int AdditionalProduct;

    /// <summary>ชื่อโมเดลต้นอ่อน — null ถ้าไฟล์ไม่ได้ให้มา</summary>
    public string GrowingLook => Look != null && Look.TryGetValue("growing", out string v) ? v : null;

    /// <summary>โมเดลตอนโตเต็มที่ของช่องนี้ — เลือกด้วย hash เหมือนต้นฉบับ</summary>
    public string GrownLookAt(int tileX, int tileY) =>
        GrownLooks is { Length: > 0 }
            ? GrownLooks[KUtilityNx.GetRandomHash(tileX, tileY) % GrownLooks.Length]
            : null;
}

public static class CropYaml
{
    private static Dictionary<string, Dictionary<object, Crop>> _crops;

    public static Crop Get(string prototypeId)
    {
        if (_crops == null)
        {
            _crops = Json.ReadFromFile<Dictionary<string, Dictionary<object, Crop>>>("offline/assets/crops");
        }
        return _crops?.Get(prototypeId)
            ?.Select(pair => pair.Value)
            .FirstOrDefault();
    }
}
