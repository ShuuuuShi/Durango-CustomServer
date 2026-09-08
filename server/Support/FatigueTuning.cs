using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Durango.Utils;
using Newtonsoft.Json.Linq;
using Shared.Region;
using Shared.Survival;

namespace Durango.Online;

/// <summary>
/// ความเหนื่อยจากสภาพแวดล้อม — ค่าทั้งหมดเป็นของ NEXON ไม่มีตัวเลขที่แต่งเอง
///
/// ═══ อาการเดิม ═══
/// เซิร์ฟไม่เคยส่ง <c>FatigueVelocities</c>(318) เลยสักครั้ง ⇒ ฝั่งเกม
/// (client/Durango.Logic/FatigueSystem.cs:67,83) ไม่มีรายการความเหนื่อยแยกหมวดเลย
/// ⇒ บัพที่มีผล <c>type=2</c> (Fatigue) **261 จาก 372 ชนิด** ไม่มีอะไรให้ไปลด
/// เช่น <c>inside</c> ("집" อยู่ในบ้าน) ที่มี <c>default -1.0 · gale -1.0 · hot -0.75</c>
/// กลายเป็นไอคอนเปล่า ๆ
///
/// ═══ ที่มาของค่าแต่ละตัว ═══
/// <code>
/// constants.json → fatigue_velocity   สูตรความเร็วฐาน แยกตาม Role ของเกาะ
/// survival/fatigue_categories.json    default_ratio + effect ของแต่ละหมวด
/// region_templates.json → biome_effects  ไบโอมไหนบนเกาะนี้ให้หมวดอะไร
/// survival/status_effects.json type=2 ตัวคูณลดจากบัพ (คีย์เป็นชื่อหมวดแบบ snake_case)
/// </code>
///
/// **คีย์ของ <c>fatigue_velocity</c> คือ <see cref="Role"/> ไม่ใช่ไบโอมหรือหมวด** — ยืนยันจาก
/// ค่าที่มีจริง {1, 3, 5, 6, 7, default} เทียบกับ <c>Shared.Region.Role</c> แล้วได้ความหมายครบทุกตัว:
/// <code>
///   1 Tutorial  → "0"          เกาะสอนเล่นไม่เหนื่อย
///   3 Rural     → สูตร × 0.5   เกาะมือใหม่เหนื่อยครึ่งเดียว
///   5 Outpost   → สูตรเต็ม
///   6 Urban     → สูตร × 0.5   ในเมืองเหนื่อยครึ่งเดียว
///   7 Safehouse → "0"          บ้านพักไม่เหนื่อย
///   default     → สูตรเต็ม     (Risky = 217 จาก 267 เกาะ คือเกมจริง)
/// </code>
/// ถ้าอ่านเป็นไบโอมจะได้ "ป่าเขตร้อน 0 · ทุ่งหญ้าเต็ม · ภูเขาไฟ 0" ซึ่งขัดกันเอง
/// ⇒ Role เป็นการอ่านเดียวที่ลงตัวทั้งหกค่า
///
/// ⚠️ คีย์ชั้นในเป็น <c>"1"</c> ทุกก้อนและมีก้อนละตัวเดียว — ยังไม่รู้ว่าหมายถึงอะไร
///    (น่าจะเป็นเลเวลของผล) ⇒ อ่านค่าแรกที่เจอ ไม่เดาว่ามีชั้นอื่น
/// </summary>
public static class FatigueTuning
{
    /// <summary>หมวดความเหนื่อยหนึ่งหมวด — ตรงกับ <c>Yaml.FatigueCategory</c> ฝั่งเกม</summary>
    public sealed class CategoryInfo
    {
        public FatigueCategory Category;

        /// <summary>ชื่อแบบ snake_case — คีย์ที่ <c>status_effects.json</c> ผล type=2 ใช้อ้างถึงหมวดนี้</summary>
        public string SnakeCase;

        /// <summary>ตัวคูณความแรงของหมวด (ปกติ 1.0 · หมวด "혹서/혹한" = 2.0)</summary>
        public float DefaultRatio = 1f;

        /// <summary>
        /// ท่ายืนของตัวละครตอนหมวดนี้เป็นตัวนำ — <c>"hot"</c> / <c>"cold"</c> / ว่าง
        /// ส่งไปเป็น <c>FatigueVelocities.FatigueEffect</c> แล้วฝั่งเกมแปลงเป็น
        /// <c>StandStateEnum.Hot/Cold</c> (client/LocalMotionUpdater.cs:589-594)
        /// </summary>
        public string Effect;
    }

    private static bool _loaded;
    private static readonly Dictionary<FatigueCategory, CategoryInfo> _categories = new();
    private static readonly Dictionary<Role, string> _velocityByRole = new();
    private static string _velocityDefault;

    /// <summary>ข้อมูลของหมวดหนึ่ง — คืน null ถ้าไฟล์ไม่มีหมวดนั้น</summary>
    public static CategoryInfo Get(FatigueCategory category)
    {
        EnsureLoaded();
        return _categories.GetValueOrDefault(category);
    }

    /// <summary>แปลงชื่อ snake_case จาก <c>biome_effects</c> เป็นหมวด — ไม่รู้จักคืน Invalid</summary>
    public static FatigueCategory ParseCategory(string snake)
    {
        if (string.IsNullOrEmpty(snake)) return FatigueCategory.Invalid;
        string pascal = string.Concat(snake.Split('_').Select(p =>
            p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
        return Enum.TryParse(pascal, ignoreCase: true, out FatigueCategory c) ? c : FatigueCategory.Invalid;
    }

    /// <summary>
    /// ความเร็วความเหนื่อยฐานต่อวินาทีของเกาะแบบนี้ — <c>sl</c> = เลเวลตัวละคร · <c>rl</c> = เลเวลเกาะ
    /// สูตรหลัก <c>(0.04 + 0.001 * sl) * max(0.5, 1 - 0.05 * (sl - rl))</c>
    /// ⇒ เลเวลเราสูงกว่าเกาะมาก ยิ่งเหนื่อยช้า (ต่ำสุดครึ่งหนึ่ง) ตรงกับคำอธิบายหมวด Default ในไฟล์
    /// </summary>
    public static double BaseVelocity(Role role, int selfLevel, int regionLevel)
    {
        EnsureLoaded();
        string expr = _velocityByRole.GetValueOrDefault(role) ?? _velocityDefault;
        if (string.IsNullOrEmpty(expr)) return 0.0;

        var vars = new Dictionary<string, double>
        {
            ["sl"] = Math.Max(1, selfLevel),
            ["rl"] = Math.Max(1, regionLevel)
        };
        // อ่านสูตรไม่ออก = ไม่คิดผล ดีกว่าเดาตัวเลขให้ (กฎเดียวกับ StatFormula)
        return StatFormula.TryEval(expr, vars, out double v) && v > 0.0 ? v : 0.0;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        LoadCategories();
        LoadVelocities();
    }

    private static void LoadCategories()
    {
        var root = Json.ReadFromFile<Dictionary<string, JToken>>("survival/fatigue_categories");
        if (root == null)
        {
            Console.WriteLine("[ความเหนื่อย] ⚠️ อ่าน survival/fatigue_categories.json ไม่ได้");
            return;
        }
        foreach (KeyValuePair<string, JToken> kv in root)
        {
            if (!int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) continue;
            if (!Enum.IsDefined(typeof(FatigueCategory), id)) continue;
            if (kv.Value is not JObject o) continue;

            var category = (FatigueCategory)id;
            _categories[category] = new CategoryInfo
            {
                Category = category,
                SnakeCase = ToSnake(category.ToString()),
                DefaultRatio = (float?)o["default_ratio"] ?? 1f,
                Effect = (string)o["effect"]
            };
        }
        Console.WriteLine($"[ความเหนื่อย] โหลดหมวด {_categories.Count} หมวด");
    }

    private static void LoadVelocities()
    {
        JObject root = Json.ReadFromFile<JObject>("constants");
        if (root?["fatigue_velocity"] is not JObject table)
        {
            Console.WriteLine("[ความเหนื่อย] ⚠️ อ่าน constants.json → fatigue_velocity ไม่ได้ — จะไม่มีความเหนื่อยจากสภาพแวดล้อม");
            return;
        }
        foreach (JProperty prop in table.Properties())
        {
            // ชั้นในมีก้อนเดียวเสมอ (คีย์ "1") — เอาค่าแรก ไม่เดาว่าคีย์แปลว่าอะไร
            string expr = prop.Value is JObject inner
                ? inner.Properties().FirstOrDefault()?.Value?.ToString()
                : prop.Value?.ToString();
            if (string.IsNullOrWhiteSpace(expr)) continue;

            if (prop.Name == "default")
            {
                _velocityDefault = expr;
            }
            else if (int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int roleId) &&
                     Enum.IsDefined(typeof(Role), roleId))
            {
                _velocityByRole[(Role)roleId] = expr;
            }
        }
    }

    /// <summary>PascalCase → snake_case (VeryHot → very_hot) เทียบเท่า Durango.Utils.Extensions ฝั่งเกม</summary>
    private static string ToSnake(string pascal)
    {
        if (string.IsNullOrEmpty(pascal)) return pascal;
        var sb = new System.Text.StringBuilder(pascal.Length + 4);
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
