using System;
using System.Collections.Generic;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Yaml;

namespace Durango.Online;

/// <summary>
/// เงื่อนไขการซ่อมของ — ตัวที่ทำให้ปุ่ม "ซ่อม" ในเกมกดได้
///
/// ═══ อาการเดิม ═══
/// <c>Item.RepairRequirement</c> เป็น <c>null</c> ทุกชิ้น ⇒ ฝั่งเกมตัดสินว่าซ่อมไม่ได้ตั้งแต่ต้น
/// (<c>client/Durango.Logic.Item/ItemData.cs:107</c>
///  <c>IsRepairable =&gt; RepairRequirement.HasValue &amp;&amp; !string.IsNullOrEmpty(TagId)</c>)
/// ⇒ **หน้าต่างซ่อมไม่เปิด** และช่อง "필요 성능" ใน <c>ItemContextRepair.cs:38</c> โชว์ขีด "-"
/// ทั้งที่ของสึกจนพัง — ไม่มี error อะไรให้เห็น
///
/// ═══ ค่าที่ใช้มาจากไหน ═══
/// • **สูตรตัวเลข** เป็นของ NEXON เป๊ะ — <c>constants.json → repair.repair_requirement_perf</c>
///   <code>int(max(5, int(pow(int((repair_requirement + level) / 10), 2) / 10) * 10))</code>
///   ฝั่งเกมใช้สูตรเดียวกันนี้กับสิ่งปลูกสร้าง
///   (<c>Constants.Repair.GetRepairRequirementPerformance(repair_requirement, level)</c>
///    ถูกเรียกที่ <c>ArtifactInteractions.cs:764</c> และ <c>RepairKitsWidget.cs:106</c>)
/// • **ชื่อแท็กชุดซ่อม** เป็นของ NEXON เช่นกัน — <c>tags.json</c> มีสามตัว:
///   <c>tool_repair_kit</c> (도구 수리 키트) · <c>clothes_repair_kit</c> (의상 수리 키트) ·
///   <c>artifact_repair_kit</c> (건물 수리 키트)
/// • **การจับคู่ของ→แท็ก** อ่านจากช่อง <c>category</c> ของไอเทมเอง (item/prototype_data.json):
///   <c>weapon/tool</c> → ชุดซ่อมเครื่องมือ · <c>clothing</c>/<c>accessory</c> → ชุดซ่อมเสื้อผ้า
///   ตรงกับชื่อแท็กภาษาเกาหลีพอดี ไม่ได้เดา
///
/// ⚠️ **จุดที่ข้อมูลไม่ครบ — บอกไว้ตรง ๆ**
/// ต้นฉบับเก็บ <c>repair_requirement</c> ของ "ไอเทมแต่ละชนิด" ไว้ใน prototype preset
/// (<c>client/Yaml/PrototypePresetRepair.cs</c> — ฟิลด์ <c>tag</c> กับ <c>perf</c>)
/// ตารางนั้น **ไม่ได้อยู่ในชุดข้อมูลที่สกัดออกมา** (มีแต่ของสิ่งปลูกสร้างใน artifact.json)
/// ⇒ ที่นี่คิดด้วยสูตรเดียวกันโดยให้ <c>repair_requirement = 0</c> เหลือแค่ตัวแปร level
/// ผลที่ได้ต่อเนื่องกับข้อมูลจริง: ของเลเวล 60 ต้องใช้ชุดซ่อมที่มี <c>ability</c> ≥ 30
/// ซึ่งเท่ากับ <c>tool_repair_kit_01</c> (performance.json → repair_kit) พอดี
/// ถ้าวันหนึ่งสกัดตารางนั้นมาได้ ให้เปลี่ยนมาอ่านจากไฟล์แทนสูตรนี้
/// </summary>
public static class RepairTuning
{
    private const string DefaultFormula =
        "int(max(5, int(pow(int((repair_requirement + level) / 10), 2) / 10) * 10))";

    /// <summary>แท็กชุดซ่อมของแต่ละหมวด — ชื่อจริงจาก data/assets/tags.json</summary>
    public const string ToolKitTag = "tool_repair_kit";
    public const string ClothesKitTag = "clothes_repair_kit";
    public const string ArtifactKitTag = "artifact_repair_kit";

    private static bool _loaded;
    private static string _formula = DefaultFormula;

    /// <summary>
    /// เงื่อนไขซ่อมของไอเทมชิ้นนี้ — <c>null</c> = ของชนิดนี้ซ่อมไม่ได้ (วัตถุดิบ/อาหาร/ของใช้แล้วหมด)
    /// </summary>
    public static RepairRequirement? Of(Prototype prototype, int level)
    {
        string tag = KitTagOf(prototype?.Category);
        if (tag == null) return null;

        return new RepairRequirement
        {
            TagId = tag,
            RepairPerformance = PerformanceFor(repairRequirement: 0, level)
        };
    }

    /// <summary>
    /// หมวดของไอเทม → แท็กชุดซ่อมที่ต้องใช้ · null = ซ่อมไม่ได้
    ///
    /// ค่า <c>category</c> ที่มีจริงในไฟล์: weapon/tool · clothing · accessory · material ·
    /// food/medicine · … — สามตัวแรกเท่านั้นที่มีหลอดความทนทานให้ซ่อม
    /// </summary>
    private static string KitTagOf(string category) => category switch
    {
        "weapon/tool" => ToolKitTag,
        "clothing" or "accessory" => ClothesKitTag,
        _ => null
    };

    /// <summary>ค่าพลังของชุดซ่อมที่ต้องใช้ — สูตรเดียวกับที่ฝั่งเกมใช้กับสิ่งปลูกสร้าง</summary>
    public static int PerformanceFor(int repairRequirement, int level)
    {
        EnsureLoaded();
        var vars = new Dictionary<string, double>
        {
            ["repair_requirement"] = repairRequirement,
            ["level"] = level
        };
        return StatFormula.TryEval(_formula, vars, out double value)
            ? Math.Max(5, (int)value)
            : 5;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        JObject root = Json.ReadFromFile<JObject>("constants");
        string formula = (string)root?["repair"]?["repair_requirement_perf"];
        if (string.IsNullOrEmpty(formula))
        {
            Console.WriteLine("[ซ่อม] ⚠️ อ่าน constants.json → repair.repair_requirement_perf ไม่ได้ — ใช้สูตรสำรอง");
            return;
        }
        _formula = formula;
    }
}
