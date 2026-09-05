using System;
using System.Collections.Generic;

namespace Durango.Online;

/// <summary>
/// สภาพอากาศประจำเกาะ — ตัวที่ทำให้มีฝน/หิมะ/เถ้าภูเขาไฟตกจริงบนจอ
///
/// ═══ อาการเดิม ═══
/// <c>World.Weather</c> ถูกตั้งจาก cheat <c>weather</c> เท่านั้น ⇒ เล่นปกติจะเป็นค่าว่างตลอดเกม
/// ⇒ <c>World.SendInitialState</c> ข้ามการส่ง <c>Weather</c>(2028) ไปเลย
/// ⇒ <c>WeatherManager</c> ฝั่งเกม (client/Durango.Environment/WeatherManager.cs:161-173)
///    ไม่เคยได้รับอะไร ⇒ **ท้องฟ้าแจ่มใสตลอดกาล ทุกเกาะ ทุกฤดู** ไม่มี error ให้เห็น
///
/// ═══ ข้อมูลจริงที่ใช้ ═══
/// <c>region_templates.json</c> มีฟิลด์ <c>weather</c> ของแต่ละแม่แบบเกาะ **ของ NEXON เอง**
/// ค่าที่พบจริงทั้งหมด 5 แบบ (จาก 267 แม่แบบ):
/// <code>
///   ending_climate_storm   96 เกาะ      ending_climate_snowy  41 เกาะ
///   volcanic_normal        32 เกาะ      always_volcanic_ash    4 เกาะ
///   heavy_snowy             1 เกาะ      (ไม่ระบุ)             93 เกาะ
/// </code>
/// และชื่อสภาพอากาศที่ฝั่งเกมรู้จักมีชุดตายตัวอยู่แล้วที่
/// <c>WeatherManager.GetWeatherFromString</c> (บรรทัด 111-151) — ส่งชื่อนอกชุดนี้ไปมันจะ
/// <c>Debug.LogError("Unknown Weather: …")</c> แล้วคืน <c>Invalid</c> ⇒ ห้ามคิดชื่อขึ้นเอง
///
/// ⚠️ **จุดที่เป็นการตัดสินใจของเรา — บอกไว้ตรง ๆ**
/// ฟิลด์ในไฟล์เป็น "ชื่อสถานการณ์" ไม่ใช่ลำดับสภาพอากาศ ตัวลำดับจริงกับจังหวะการเปลี่ยน
/// อยู่ในเซิร์ฟของ NEXON ซึ่งไม่มีซอร์ส ⇒ ที่นี่อ่าน **ภูมิอากาศ** จากชื่อ (ส่วนที่ข้อมูลบอกชัด)
/// แล้วประกอบลำดับเองจากชื่อที่ฝั่งเกมรับได้เท่านั้น:
///   ...<c>_snowy</c> / <c>heavy_snowy</c> → เกาะหนาว ⇒ วนมีหิมะ
///   ...<c>_storm</c>                      → เกาะฝน  ⇒ วนมีฝน
///   <c>volcanic_*</c>                     → เกาะภูเขาไฟ ⇒ วนมีเถ้า
///   ไม่ระบุ                                → เกาะอากาศดี ⇒ วนแดด/เมฆ
///
/// ที่เลือก "วน" ไม่ใช่ "ค้างที่ค่าเดียว" เพราะ <c>always_volcanic_ash</c> มีคำว่า always
/// แยกไว้ต่างหาก ⇒ แบบที่ไม่มี always คือแบบที่เปลี่ยนไปมาได้
/// </summary>
public static class WeatherTuning
{
    /// <summary>เปลี่ยนสภาพอากาศทุกกี่วินาที — ค่าของเรา (ข้อมูลเกมไม่ได้บอกจังหวะไว้)</summary>
    public const double CycleSeconds = 300.0;

    /// <summary>
    /// ลำดับสภาพอากาศของเกาะที่ใช้แม่แบบนี้ — ชื่อทุกตัวอยู่ในชุดที่
    /// <c>WeatherManager.GetWeatherFromString</c> รับได้
    /// </summary>
    public static IReadOnlyList<string> SequenceFor(string weatherId)
    {
        if (string.IsNullOrEmpty(weatherId)) return Mild;

        // "always_…" = ค้างค่าเดียวตลอด ไม่วน
        if (weatherId.StartsWith("always_", StringComparison.Ordinal))
        {
            string only = weatherId["always_".Length..];
            return Known.Contains(only) ? new[] { only } : Mild;
        }

        if (weatherId.Contains("volcanic")) return Volcanic;
        if (weatherId.EndsWith("snowy", StringComparison.Ordinal)) return Snowy;
        if (weatherId.EndsWith("storm", StringComparison.Ordinal)) return Stormy;

        // ชื่อสภาพอากาศตรง ๆ (เช่น "heavy_snowy") — ใช้เป็นค่าคงที่
        return Known.Contains(weatherId) ? new[] { weatherId } : Mild;
    }

    private static readonly string[] Mild = { "sunny", "cloudy", "sunny", "cloudy" };
    private static readonly string[] Stormy = { "sunny", "cloudy", "rainy", "heavy_rainy", "cloudy" };
    private static readonly string[] Snowy = { "cloudy", "snowy", "heavy_snowy", "snowy", "cloudy" };
    private static readonly string[] Volcanic = { "volcanic_sign", "volcanic_ash", "volcanic_storm", "volcanic_ash" };

    /// <summary>
    /// ชื่อทั้งหมดที่ฝั่งเกมแปลงเป็นสภาพอากาศได้ — คัดลอกจาก
    /// <c>client/Durango.Environment/WeatherManager.cs:111-151</c> ตรง ๆ
    /// (ตัด pvp_* ออกเพราะเป็นของโหมด PvP ที่ยังไม่มี)
    /// </summary>
    private static readonly HashSet<string> Known = new()
    {
        "sunny", "cloudy", "rainy", "heavy_rainy", "snowy", "heavy_snowy",
        "volcanic_ash", "volcanic_sign", "volcanic_storm",
        "climate_sunny", "climate_snowy", "climate_storm"
    };

    /// <summary>ใช้ตรวจว่าชื่อที่ cheat ส่งมาถูกต้องไหม ก่อนกระจายให้ทุกคน</summary>
    public static bool IsKnown(string weather) => weather != null && Known.Contains(weather);
}
