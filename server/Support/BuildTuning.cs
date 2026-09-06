using System;
using System.Collections.Generic;
using Durango.Utils;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

/// <summary>
/// ค่าคงที่ของการสร้างสิ่งปลูกสร้าง — อ่านจาก <c>data/assets/constants.json → build</c>
///
/// **ทุกค่าเป็นของ NEXON** ไฟล์จริงมี:
/// <code>
/// "build": {
///   "site_selection": { "duration": "2 + (area * 1)", "energy": "1 + (area * 2)" },
///   "building":       { "energy": "1" },
///   "default_durability": 7,
///   "cancel_time": 3,
///   "destruct": { "energy": "10 + durability / 2.", "time": "5 + durability / 10." }
/// }
/// </code>
///
/// <c>duration</c>/<c>energy</c> เป็นสูตรที่มีตัวแปร <c>area</c> (= กว้าง × ยาว ของ footprint)
/// ⇒ ต้องคิดด้วย <see cref="StatFormula"/> ไม่ใช่อ่านเป็นตัวเลขตรง ๆ
///
/// ⚠️ เวลา/พลังงานของ "การสร้างจริง" อยู่ที่ **แบบแปลนแต่ละหลัง** ไม่ใช่ที่นี่
/// (<c>blueprints.json → energy · postprocess_time · effort</c>) — ดู <see cref="BlueprintStore"/>
/// </summary>
public static class BuildTuning
{
    private static bool _loaded;
    private static string _siteDuration = "2 + (area * 1)";
    private static string _siteEnergy = "1 + (area * 2)";
    private static string _buildEnergy = "1";
    private static float _defaultDurability = 7f;
    private static float _cancelTime = 3f;
    private static float _capsulatingTime = 0.5f;
    private static float _placingTime = 0.5f;

    public static string SiteDuration { get { EnsureLoaded(); return _siteDuration; } }
    public static string SiteEnergy { get { EnsureLoaded(); return _siteEnergy; } }
    public static string BuildEnergy { get { EnsureLoaded(); return _buildEnergy; } }
    public static float DefaultDurability { get { EnsureLoaded(); return _defaultDurability; } }
    public static float CancelTime { get { EnsureLoaded(); return _cancelTime; } }

    /// <summary>วินาทีที่ใช้ "แพ็ก" สิ่งปลูกสร้างเก็บเป็นไอเทม (capsulating.capsulating_time.default)</summary>
    public static float CapsulatingTime { get { EnsureLoaded(); return _capsulatingTime; } }

    /// <summary>วินาทีที่ใช้ "วาง" ของที่แพ็กไว้ลงพื้น (capsulating.placing_time)</summary>
    public static float PlacingTime { get { EnsureLoaded(); return _placingTime; } }

    /// <summary>คิดสูตรที่มีตัวแปร <c>area</c> — คืนค่าสำรองถ้าสูตรเสีย</summary>
    public static double EvalByArea(string formula, int area, double fallback)
    {
        var vars = new Dictionary<string, double> { ["area"] = area };
        return StatFormula.TryEval(formula, vars, out double value) ? value : fallback;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        JObject root = Json.ReadFromFile<JObject>("constants");
        if (root?["build"] is not JObject build)
        {
            Console.WriteLine("[สร้าง] ⚠️ อ่าน constants.json → build ไม่ได้ — ใช้ค่าสำรอง");
            return;
        }

        _siteDuration = (string)build["site_selection"]?["duration"] ?? _siteDuration;
        _siteEnergy = (string)build["site_selection"]?["energy"] ?? _siteEnergy;
        _buildEnergy = (string)build["building"]?["energy"] ?? _buildEnergy;
        _defaultDurability = (float?)build["default_durability"] ?? _defaultDurability;
        _cancelTime = (float?)build["cancel_time"] ?? _cancelTime;
        // capsulating.capsulating_time เป็นตารางแยกตามชนิด ({default, "0", "2", "4"})
        // ค่าจริงในไฟล์เท่ากันหมด (0.5) ⇒ ใช้ default ตัวเดียวพอ ไม่ต้องแยกตามชนิด
        _capsulatingTime = (float?)build["capsulating"]?["capsulating_time"]?["default"] ?? _capsulatingTime;
        _placingTime = (float?)build["capsulating"]?["placing_time"] ?? _placingTime;
    }
}
