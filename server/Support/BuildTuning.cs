using System;
using System.Collections.Generic;
using System.Linq;
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
    private static string _capsulatingCostInside = "0";
    private static string _capsulatingCostOutside = "t_stone_reference * level";
    private static double? _tStoneReference;

    public static string SiteDuration { get { EnsureLoaded(); return _siteDuration; } }
    public static string SiteEnergy { get { EnsureLoaded(); return _siteEnergy; } }
    public static string BuildEnergy { get { EnsureLoaded(); return _buildEnergy; } }
    public static float DefaultDurability { get { EnsureLoaded(); return _defaultDurability; } }
    public static float CancelTime { get { EnsureLoaded(); return _cancelTime; } }

    /// <summary>วินาทีที่ใช้ "แพ็ก" สิ่งปลูกสร้างเก็บเป็นไอเทม (capsulating.capsulating_time.default)</summary>
    public static float CapsulatingTime { get { EnsureLoaded(); return _capsulatingTime; } }

    /// <summary>วินาทีที่ใช้ "วาง" ของที่แพ็กไว้ลงพื้น (capsulating.placing_time)</summary>
    public static float PlacingTime { get { EnsureLoaded(); return _placingTime; } }

    /// <summary>สูตรค่าเก็บบนที่ดิน — <c>constants.json → build.capsulating.cost.inside</c> (= <c>"0"</c>)</summary>
    public static string CapsulatingCostInside { get { EnsureLoaded(); return _capsulatingCostInside; } }

    /// <summary>สูตรค่าเก็บนอกที่ดิน — <c>constants.json → build.capsulating.cost.outside</c></summary>
    public static string CapsulatingCostOutside { get { EnsureLoaded(); return _capsulatingCostOutside; } }

    /// <summary>
    /// ค่าตัวเลขของ <c>t_stone_reference</c> ถ้ามีในชุดข้อมูล
    ///
    /// สูตรหลายตัวใน <c>constants.json</c> อ้างตัวแปรนี้ (ค่าเก็บนอกที่ดิน · ค่าวาร์ป · ค่าเดินเรือ · รางวัล POI)
    /// แต่ในไฟล์ที่สกัดมาไม่มีคีย์ตัวเลขชื่อนี้ — มีแต่สตริงสูตร ⇒ คืน <c>null</c>
    /// ไม่เดาเรทเอง ดู <c>docs/t-stone-reference.md</c>
    /// </summary>
    public static double? TStoneReference { get { EnsureLoaded(); return _tStoneReference; } }

    /// <summary>คิดสูตรที่มีตัวแปร <c>area</c> — คืนค่าสำรองถ้าสูตรเสีย</summary>
    public static double EvalByArea(string formula, int area, double fallback)
    {
        var vars = new Dictionary<string, double> { ["area"] = area };
        return StatFormula.TryEval(formula, vars, out double value) ? value : fallback;
    }

    /// <summary>
    /// คิดค่าเก็บสิ่งปลูกสร้างเป็นแคปซูลจากสูตรใน constants
    ///
    /// <paramref name="insideEstate"/> = อยู่บนที่ดิน/แคลน (inside) หรือนอกที่ดิน (outside)
    /// คืน <c>false</c> เมื่อสูตรใช้ตัวแปรที่ไม่มีค่า (โดยเฉพาะ <c>t_stone_reference</c>)
    /// — ผู้เรียกต้องตัดสินใจเองว่าจะ Abort หรือตอบ 0 แบบ STUB ไม่ให้เดาเรท
    /// </summary>
    public static bool TryEvalCapsulatingCost(bool insideEstate, int artifactLevel, out long amount) =>
        TryEvalCapsulatingCost(insideEstate, artifactLevel, TStoneReference, out amount);

    /// <summary>เวอร์ชันระบุ <c>t_stone_reference</c> เอง — ใช้เทสสูตรโดยไม่เดาเรทของเซิร์ฟ</summary>
    public static bool TryEvalCapsulatingCost(
        bool insideEstate, int artifactLevel, double? tStoneReference, out long amount)
    {
        EnsureLoaded();
        amount = 0;
        string formula = insideEstate ? _capsulatingCostInside : _capsulatingCostOutside;
        var vars = new Dictionary<string, double> { ["level"] = Math.Max(1, artifactLevel) };
        if (tStoneReference.HasValue)
        {
            vars["t_stone_reference"] = tStoneReference.Value;
        }
        if (!StatFormula.TryEval(formula, vars, out double value))
        {
            return false;
        }
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            return false;
        }
        amount = (long)Math.Round(value, MidpointRounding.AwayFromZero);
        return true;
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
        _capsulatingCostInside = (string)build["capsulating"]?["cost"]?["inside"] ?? _capsulatingCostInside;
        _capsulatingCostOutside = (string)build["capsulating"]?["cost"]?["outside"] ?? _capsulatingCostOutside;
        _tStoneReference = FindNumericTStoneReference(root);

        Console.WriteLine($"[สร้าง] capsulating.cost inside=\"{_capsulatingCostInside}\" " +
                          $"outside=\"{_capsulatingCostOutside}\"");
        if (!_tStoneReference.HasValue)
        {
            Console.WriteLine("[สร้าง] ⚠️ t_stone_reference ไม่มีค่าตัวเลขในชุดข้อมูลที่สกัดมา " +
                              "— ค่าเก็บนอกที่ดินตอบ 0 (STUB) จนกว่าจะมีแหล่งข้อมูลจริง");
        }
    }

    /// <summary>
    /// หาคีย์ <c>t_stone_reference</c> ที่เป็นตัวเลขใน constants ทั้งไฟล์
    /// ไม่เก็บสตริงสูตร (เช่น <c>"t_stone_reference * 12"</c>) เพราะนั่นคือการใช้ตัวแปร ไม่ใช่ค่า
    /// </summary>
    private static double? FindNumericTStoneReference(JObject root)
    {
        if (root == null) return null;
        foreach (JProperty prop in root.DescendantsAndSelf().OfType<JProperty>())
        {
            if (!string.Equals(prop.Name, "t_stone_reference", StringComparison.Ordinal)) continue;
            if (prop.Value.Type == JTokenType.Integer || prop.Value.Type == JTokenType.Float)
            {
                return (double)prop.Value;
            }
        }
        return null;
    }
}
