using System;
using System.Collections.Generic;
using Messages;
using Yaml;

namespace Durango.Online;

/// <summary>
/// กติกาเก็บเกี่ยวแปลงเพาะปลูก
///
/// <c>crops.json → grows_to</c> เป็น <b>collectible/generator id</b> (เช่น <c>corn_crop</c>)
/// ไม่ใช่ prototype ของไอเทมในกระเป๋า — <c>prototype_data.json</c> ไม่มีคีย์นั้น
/// ของที่ได้จริงมีสองชั้นจากข้อมูลที่มีอยู่:
///   1. <c>{stem}_infertility</c> ถ้ามี (อาหารปลูกต่อไม่ได้ เช่น <c>corn_infertility</c>)
///   2. เมล็ดที่ปลูก (<c>corn_seed</c>) — มีในตารางเสมอเพราะผู้เล่นเพิ่งปลูกมัน
///
/// ฝั่งเกมเก็บเกี่ยวแปลงด้วย <c>Collect</c> ชุดเดียวกับของธรรมชาติ
/// (client/GatheringSystem.cs ผูกปุ่มจาก <c>Touched.Collectible.Generators</c>)
/// ⇒ generator เดียว กดครั้งเดียวได้ของครบ แล้วล้างแปลง
/// </summary>
internal static class FarmHarvest
{
    /// <summary>โตเต็มที่เมื่อนาฬิกาเซิร์ฟถึง <c>GrowsUntil</c> — ค่า 0 = ยังไม่ปลูก</summary>
    public static bool IsMature(in Farming farming, double now) =>
        farming.GrowsUntil > 0.0 && now >= farming.GrowsUntil;

    /// <summary>id ที่ฝั่งเกมส่งกลับมาใน <c>Collect.GeneratorId</c> = <c>grows_to</c></summary>
    public static string GeneratorId(Crop crop) => crop?.GrowsTo;

    /// <summary>prototype ของไอเทมที่จะเสกเข้ากระเป๋า — เฉพาะคีย์ที่มีใน prototype_data จริง</summary>
    public static string[] ProductPrototypes(string seedPrototypeId, Crop crop)
    {
        if (crop == null) return Array.Empty<string>();
        var list = new List<string>();
        AddIfPrototype(list, crop.GrowsTo);
        if (!string.IsNullOrEmpty(crop.GrowsTo)
            && crop.GrowsTo.EndsWith("_crop", StringComparison.Ordinal))
        {
            string stem = crop.GrowsTo.Substring(0, crop.GrowsTo.Length - "_crop".Length);
            AddIfPrototype(list, stem + "_infertility");
        }
        AddIfPrototype(list, seedPrototypeId);
        return list.ToArray();
    }

    /// <summary>
    /// <c>Collectible</c> ที่ใส่ใน <c>Touched</c> ให้ฝั่งเกมโชว์ปุ่มเก็บ
    /// (client/InteractionSystem.cs:628 → GatheringSystem.SetCollectible)
    /// </summary>
    public static Collectible BuildCollectible(string entityId, string seedPrototypeId, Crop crop)
    {
        if (crop == null || string.IsNullOrEmpty(crop.GrowsTo)) return default;
        string[] products = ProductPrototypes(seedPrototypeId, crop);
        if (products.Length == 0) return default;

        Prototype proto = PrototypeYaml.GetItemPrototype(products[0]);
        float effort = CollectibleTable.Effort(1);
        float duration = CollectibleTable.Duration(effort);
        string genId = GeneratorId(crop);
        return new Collectible
        {
            EntityId = entityId ?? string.Empty,
            CollectibleId = crop.GrowsTo,
            Size = "low",
            CriticalGenerator = genId,
            Generators = new[]
            {
                new Generator
                {
                    Id = genId,
                    Level = 1,
                    Name = proto?.Name?.ToString() ?? crop.GrowsTo,
                    Icon = proto?.Icon ?? string.Empty,
                    Amount = 1,
                    Effort = effort,
                    Duration = duration,
                    ToolRequirements = new Dictionary<string, int> { { CollectibleTable.BareHands, 1 } },
                    Enabled = true
                }
            }
        };
    }

    static void AddIfPrototype(List<string> list, string prototypeId)
    {
        if (string.IsNullOrEmpty(prototypeId) || list.Contains(prototypeId)) return;
        if (PrototypeYaml.GetItemPrototype(prototypeId) == null) return;
        list.Add(prototypeId);
    }
}
