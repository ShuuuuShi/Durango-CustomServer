using System;
using Durango.Utils;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

/// <summary>
/// ค่าของการกระโดดหลบ — อ่านจาก <c>data/assets/constants.json → dash</c>
///
/// **ค่าของ NEXON** ไฟล์จริงมีบล็อกเดียวสั้น ๆ:
/// <code>
/// "dash": { "stamina": 20 }
/// </code>
///
/// ยืนยันความหมายจากฝั่งเกม: <c>client/Yaml/Dash.cs</c> อ่านฟิลด์ <c>stamina</c> ตัวเดียวกัน
/// และหลอด stamina ของผู้เล่นนิยามไว้ที่ <c>entity_types/players.json</c>
/// (เต็ม 100 · ฟื้น 5 หน่วย/วินาที) ⇒ กระโดดรัวได้ 5 ครั้งแล้วต้องรอ ~4 วินาที
///
/// ⚠️ หลอดฝั่งเกมเป็นแค่ภาพ — เซิร์ฟเป็นเจ้าของค่าจริง ไม่หักที่นี่ = กระโดดได้ไม่จำกัด
/// </summary>
public static class DashTuning
{
    private static bool _loaded;
    private static float _stamina = 20f;

    /// <summary>ความอึดที่เสียต่อการกระโดดหนึ่งครั้ง</summary>
    public static float Stamina { get { EnsureLoaded(); return _stamina; } }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        JObject root = Json.ReadFromFile<JObject>("constants");
        if (root?["dash"] is not JObject dash)
        {
            Console.WriteLine("[กระโดด] ⚠️ อ่าน constants.json → dash ไม่ได้ — ใช้ค่าสำรอง");
            return;
        }
        _stamina = (float?)dash["stamina"] ?? _stamina;
    }
}
