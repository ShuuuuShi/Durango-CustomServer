using System;
using System.Collections.Generic;
using Messages;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  [7 ก.ย. 2026] ความทนทานของเครื่องมือในกระเป๋า
//
//  ⚠️ ก่อนมีไฟล์นี้: คราฟต์ขวานครั้งเดียวใช้ได้ตลอดชีวิต ⇒ หลังชั่วโมงแรกไม่มีเหตุผล
//     ให้กลับไปหาวัสดุอีกเลย วงจร "หาของ → คราฟต์ → ของดีกว่า" ขาดตรงกลาง
//     (ไอเทมทุกชิ้นถูกสร้างด้วย Durability = Gauge(1,0,[node(0,1)]) เต็มตายตัว —
//      Core/Cheats.cs:32 และ Core/Player.Inventory.cs:541)
//
//  ═══ ขอบเขต: เฉพาะ "ไอเทมในกระเป๋า" เท่านั้น ═══
//  สิ่งปลูกสร้างทำไม่ได้ และมีเหตุผลบันทึกไว้แล้วที่ Core/Player.Repair.cs:14-38 —
//  ArtifactManager ไม่เปิด API เขียน States.Durability กลับ และ Get() คืน struct copy
//  ⇒ ไฟล์นี้ไม่แตะสิ่งปลูกสร้างเลย
//
//  ไอเทมในกระเป๋าแก้ได้จริงเพราะ _context.InventoryItems เป็น List ที่เขียนกลับได้
//  (Item เป็น struct ⇒ ต้องเขียนกลับเข้า list ด้วย index ห้ามแก้ตัวที่ foreach ออกมา)
//
//  ═══ ข้อมูลจริงไม่มีเลขความทนทานต่อไอเทม ═══
//  ค่า `durability` ที่เจอใน data เป็นของสิ่งปลูกสร้าง ไม่ใช่ของถือ
//  ⇒ **ค่าของเราทั้งหมด** คิดจาก "วัสดุที่ทำ" ซึ่งตรงกับที่เกมสื่ออยู่แล้ว
//    ขวานหิน < ขวานกระดูก < ขวานเหล็ก
// ═══════════════════════════════════════════════════════════════════════════════════

/// <summary>**ค่าของเรา** — ตัวเลขความทนทานทั้งหมด รวมไว้ที่เดียวให้ปรับสมดุลง่าย</summary>
public static class ToolWearTuning
{
    /// <summary>จำนวนครั้งที่ใช้ได้ของวัสดุระดับ 1 (หิน/ไม้)</summary>
    public const int UsesBase = 40;

    /// <summary>เพิ่มต่อระดับวัสดุ — ระดับ 2 = 80 ครั้ง · ระดับ 3 = 120 ครั้ง</summary>
    public const int UsesPerTier = 40;

    /// <summary>เหลือต่ำกว่าสัดส่วนนี้ถึงเตือน (เตือนครั้งเดียวตอนข้ามเส้น)</summary>
    public const float WarnBelow = 0.2f;
}

public partial class Player
{
    /// <summary>
    /// tag ที่ถือว่าเป็นเครื่องมือสึกหรอได้ — ชุดเดียวกับที่ระบบเก็บของใช้ตรวจ
    /// (ดู CollectibleTable.ToolsFor ที่อ่าน tag ของ prototype)
    /// </summary>
    private static readonly string[] WearableToolTags =
        { "axe", "knife", "pickaxe", "shovel", "hammer", "sickle" };

    /// <summary>
    /// ระดับวัสดุ 1-3 จาก **ชื่อ prototype**
    ///
    /// ⚠️ ห้ามดูจาก tag วัสดุ — tag stone/bone/metal ในข้อมูลเกมเป็นระดับ 1 หมดทุกอัน
    /// (บอกแค่ "ทำจากอะไร" ไม่ได้บอกว่าดีกว่ากันแค่ไหน) และบางชิ้นติด tag ไม่ตรงชื่อด้วย
    /// </summary>
    private static int ToolTierOf(string prototype)
    {
        if (string.IsNullOrEmpty(prototype)) return 1;
        string p = prototype.ToLowerInvariant();
        if (p.Contains("metal") || p.Contains("brass") || p.Contains("iron") || p.Contains("steel")) return 3;
        if (p.Contains("bone") || p.Contains("horn") || p.Contains("tusk")) return 2;
        return 1;
    }

    /// <summary>ใช้ได้กี่ครั้งก่อนพัง</summary>
    private static int ToolMaxUses(string prototype) =>
        ToolWearTuning.UsesBase + (ToolTierOf(prototype) - 1) * ToolWearTuning.UsesPerTier;

    /// <summary>ชิ้นนี้เป็นเครื่องมือที่สึกได้ไหม (ดูจาก tag ของตัวไอเทมที่ถืออยู่จริง)</summary>
    private static bool IsWearableTool(Item item)
    {
        if (item.Tags == null) return false;
        foreach (Messages.Tag tag in item.Tags)
        {
            if (tag.Id == null) continue;
            foreach (string toolTag in WearableToolTags)
            {
                if (string.Equals(tag.Id, toolTag, StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// สึกเครื่องมือหนึ่งครั้ง — เรียกหลัง "ได้ของจริง" เท่านั้น ไม่ใช่ตอนกดเริ่ม
    ///
    /// ⚠️ เรียกตอนเริ่ม = กดแล้วยกเลิก/โดนปฏิเสธก็เสียความทนทานฟรี
    ///
    /// Durability ของไอเทมเป็นสัดส่วน 0..1 (Core/Cheats.cs:32) ⇒ หักทีละ 1/จำนวนครั้งที่ใช้ได้
    /// พังแล้วลบออกจากกระเป๋าและบอกผู้เล่น ไม่ใช่ปล่อยให้ถือของ 0% ต่อไปเงียบ ๆ
    /// </summary>
    private void WearTool(string toolItemId)
    {
        if (string.IsNullOrEmpty(toolItemId)) return;

        List<Item> inventory = _context.InventoryItems;
        int idx = inventory.FindIndex(it => it.Id == toolItemId);
        if (idx < 0) return;                       // ทิ้งไปแล้ว/ย้ายเข้ากล่อง

        Item item = inventory[idx];
        if (!IsWearableTool(item)) return;         // มือเปล่า/ของที่ไม่ใช่เครื่องมือ

        int maxUses = Math.Max(1, ToolMaxUses(item.Prototype));
        float before = item.Durability?.Get(Gauge.CurrentTime) ?? 1f;
        float after = before - 1f / maxUses;

        if (after <= 0f)
        {
            inventory.RemoveAt(idx);
            Send(new InventoryUpdated { EntityId = EntityId, RemovedItemIds = new[] { toolItemId } });
            Send(new Info { Text = $"{item.Name} พังแล้ว — ต้องคราฟต์อันใหม่" });
            Console.WriteLine($"[เครื่องมือ] {ShortId()} {item.Prototype} พังแล้ว (ใช้ครบ {maxUses} ครั้ง)");
            OnContextChanged();
            return;
        }

        // ⚠️ Item เป็น struct — ต้องเขียนกลับเข้า list ไม่งั้นแก้แค่สำเนา
        item.Durability = new Gauge(1f, 0f, new[] { new GaugeNode(0.0, after) });
        inventory[idx] = item;
        Send(new InventoryUpdated { EntityId = EntityId, Items = new[] { item } });

        // เตือนครั้งเดียวตอนข้ามเส้น ไม่ใช่ทุกครั้งหลังจากนั้น
        if (after < ToolWearTuning.WarnBelow && before >= ToolWearTuning.WarnBelow)
        {
            Send(new Info { Text = $"{item.Name} ใกล้พังแล้ว (เหลือ {after:P0})" });
        }
        OnContextChanged();
    }
}
