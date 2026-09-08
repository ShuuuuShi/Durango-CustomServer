using System;
using System.Collections.Generic;
using Durango.Utils;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

/// <summary>
/// ความเหนื่อยต่อการกระทำ — สูตรจาก <c>constants.json → fatigue_cost</c> (ของ NEXON)
/// <code>
///   collect = "0.4 * e ** 0.5"   craft = "2 * e ** 0.5"   build = "4 * e ** 0.5"
///   combat  = "4 * e"            default = "e"
/// </code>
/// <c>e</c> = พลังงานที่ใช้ไปกับการกระทำนั้น
///
/// ⚠️ **การตีความ (บอกตรง ๆ):** ไฟล์ไม่ได้ระบุว่า <c>e</c> คือ effort หรือ energy — เลือก
/// "energy ที่ใช้จริง" ให้ตรงกับระบบเก็บของที่ทำอยู่แล้ว (Player.Gathering / CollectibleTable.FatigueCost
/// ใช้ <c>0.4 * √energy</c>) เพื่อให้ทุกการกระทำคิดฐานเดียวกัน
///
/// การเก็บของหัก fatigue อยู่แล้ว (Player.Gathering) — ตัวนี้เติมให้ "คราฟต์/สร้าง" ที่เดิมหักแต่ energy
/// </summary>
public static class ActionFatigue
{
    private static bool _loaded;
    private static readonly Dictionary<string, string> _formulas = new(StringComparer.Ordinal);

    /// <summary>ความเหนื่อยที่เพิ่มขึ้นจากการกระทำ (คืน 0 ถ้าไม่มีสูตร/พลังงาน) — ค่าบวก = เหนื่อยขึ้น</summary>
    public static float Of(string action, float energy)
    {
        EnsureLoaded();
        if (energy <= 0f) return 0f;
        string formula = _formulas.TryGetValue(action, out string f) ? f : _formulas.GetValueOrDefault("default");
        if (string.IsNullOrEmpty(formula)) return 0f;
        return StatFormula.TryEval(formula, "e", energy, out double v) && v > 0.0 ? (float)v : 0f;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        JObject root = Json.ReadFromFile<JObject>("constants");
        if (root?["fatigue_cost"] is not JObject fc)
        {
            Console.WriteLine("[ความเหนื่อย] ⚠️ อ่าน constants.json → fatigue_cost ไม่ได้ — การกระทำจะไม่ทำให้เหนื่อย");
            return;
        }
        foreach (JProperty p in fc.Properties())
        {
            string s = (string)p.Value;
            if (!string.IsNullOrEmpty(s)) _formulas[p.Name] = s;
        }
    }
}
