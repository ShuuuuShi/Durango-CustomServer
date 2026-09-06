using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Durango.Network;
using Durango.Utils;
using JetBrains.Annotations;
using Messages;
using Newtonsoft.Json;
using Shared.Item;
using Yaml;

namespace Durango.Online;

/// <summary>
/// [5 ก.ย. 2026] ระบบของ/กระเป๋า — handler ของหมวด "ของ/กระเป๋า" ใน docs/protocol-coverage.md
///
/// ไฟล์นี้เก็บเฉพาะเรื่องไอเทม: จัดลำดับช่อง · ล็อกของ · ใช้ของ (กิน/ดื่ม) · ซ่อม · ย้อมสี ·
/// คลังสินค้า (Warehouse) · และตัวที่ยังไม่มีระบบรองรับซึ่งต้อง "ตอบให้ถูกชนิด" ไม่ให้ UI ค้าง
///
/// ⚠️ สองข้อจำกัดใหญ่ที่ต้องรู้ก่อนอ่านโค้ดข้างล่าง (ทั้งคู่ติดอยู่ที่ Core/Player.cs ซึ่งอยู่นอกขอบเขตไฟล์นี้)
///
/// 1) **เมนูเปิดตู้/คลังยังไม่โผล่บนจอ** — เกมเปิดหน้าคลังจาก interaction ที่เซิร์ฟส่งมาใน
///    Touched.Interactions เท่านั้น (client/Durango.UI/InventoryGroup.cs:114 Interaction.Inventory
///    → OpenArtifactInventory · :217 Interaction.UseWarehouse → OpenWarehouseInventory)
///    แต่ Core/Player.cs:971-1027 HandleTouchMsg ยังไม่ได้ใส่สองตัวนี้ลงในรายการ
///    ทั้งที่ข้อมูลจริงมีครบแล้ว: data/assets/entity_types/artifact.json มี component
///    "Inventory" 49 ตัว และ "Warehouse" 1 ตัว ⇒ handler คลังในไฟล์นี้ถูกต้องแต่ยังไม่มีใครยิงมา
///    จนกว่า HandleTouchMsg จะเพิ่มบรรทัดทำนอง
///      if (blueprint.Components.Contains("Inventory")) list.Add(Shared.System.Interaction.Inventory);
///      if (blueprint.Components.Contains("Warehouse")) list.Add(Shared.System.Interaction.UseWarehouse);
///
/// 2) **TakeOutItem (2435) ถูกจองไปแล้ว** — Core/Player.cs:478-488 ลงทะเบียนไว้ให้หุ่นโชว์เสื้อ
///    (mannequin) อย่างเดียว ถ้าไม่ใช่หุ่นจะตอบ Abort เสมอ ⇒ ถ้าเรารับ PutInItem เก็บของเข้าตู้
///    ผู้เล่นจะ **เอาของออกไม่ได้อีกเลย** จึงเลือกตอบ Abort ที่ PutInItem แทน (ดู HandlePutInItemMsg)
///    คลังสินค้าไม่ติดปัญหานี้ เพราะทั้งใส่ (AddItemsToWarehouse) และเอาออก (PopItemsFromWarehouse)
///    เป็นของไฟล์นี้ทั้งคู่
/// </summary>
public partial class Player
{
    // ── ค่าที่ "เราตั้งเอง" ของระบบนี้ (ไม่มีในข้อมูลต้นฉบับ) ────────────────────────────

    /// <summary>
    /// **ค่าของเรา** — ขนาดกระเป๋าที่เราบอก client ตอน push InventoryInfos ซ้ำ
    ///
    /// ⚠️ ค่าจริงในข้อมูลคือ 100 (data/assets/entity_types/players.json → player → inventory_capacity)
    /// แต่ Core/Player.cs:1270 SendInventory ส่ง MaxSize = 200 ไปตั้งแต่ตอนเข้าเกม
    /// ถ้าเราตอบ 100 ทีหลัง กระเป๋าจะ "หด" กลางเกมตอนผู้เล่นแค่กดล็อกไอเทม ⇒ ยึดเลขเดียวกับที่
    /// ส่งไปแล้วไว้ก่อน จนกว่า SendInventory จะถูกแก้ให้ตรงกับข้อมูลจริง (ไฟล์นั้นอยู่นอกขอบเขต)
    /// </summary>
    private const int InventoryMaxSizeMirroredFromPlayerCs = 200;

    /// <summary>
    /// **ค่าของเรา** — ชื่อแท็บเริ่มต้นของคลังสินค้า
    ///
    /// เกมไม่มีชื่อ default ในซอร์สเลย (client/Durango.UI/WarehouseTabConfig.cs:155 ให้ผู้เล่นพิมพ์เอง
    /// ผ่าน MakeSection) แต่ MakeSection (3686) อยู่คนละหมวดในเอกสาร ⇒ ถ้าไม่มีแท็บสักอันตั้งแต่แรก
    /// ผู้เล่นจะเปิดคลังมาเจอหน้าว่างและสร้างแท็บไม่ได้ จึงแจกให้หนึ่งแท็บ
    /// </summary>
    private const string DefaultWarehouseSection = "창고";

    /// <summary>
    /// **ค่าของเรา** — ไอเทมที่ถูก "ล็อก" ของผู้เล่นคนนี้ เก็บในหน่วยความจำต่อ connection
    ///
    /// ต่อใหม่แล้วหลุด เพราะทางเดียวที่ client รู้จักรายการล็อกคือ InventoryInfos.LockedItemIds
    /// (client/InventorySystem.cs:237 UpdateLockedItems) ซึ่งถูกส่งครั้งแรกจาก Core/Player.cs
    /// SendInventory — ไฟล์นั้นอยู่นอกขอบเขต จึงยังเสียบค่าที่โหลดจากเซฟเข้าไปไม่ได้
    /// เก็บลงไฟล์ตอนนี้ = ข้อมูลที่ไม่มีใครอ่าน จึงยังไม่ทำ
    /// </summary>
    private readonly HashSet<string> _lockedItemIds = new();

    private void RegisterInventoryHandlers()
    {
        // ── จัดของในกระเป๋า ────────────────────────────────────────────────────────────
        // ลากสลับช่อง — client/InventorySystem.cs:748-785 SendItemLocationInfo ส่งลำดับ id ทั้งชุด
        // ไม่รอ reply (Send เฉย ๆ) ⇒ หน้าที่เราคือ "จำ" ลำดับไว้เท่านั้น
        _connection.Recv(delegate(InventoryOrder msg, PacketHeader header)
        {
            HandleInventoryOrderMsg(msg);
        });
        // ล็อก/ปลดล็อกไอเทม — client/InventorySystem.cs:879-889 LockItem ส่งแล้วไม่รอ reply
        // ตัวที่มันฟังคือ InventoryInfos (109) แบบ push (client/InventorySystem.cs:78,126-132)
        _connection.Recv(delegate(LockOrUnlockItems msg, PacketHeader header)
        {
            HandleLockOrUnlockItemsMsg(msg);
        });
        // ใช้ของ (กิน/ดื่ม) — client/InventorySystem.cs:787-818 รอ StartTimer หรือ OK ที่ seq นี้
        _connection.Recv(delegate(UseItem msg, PacketHeader header)
        {
            HandleUseItemMsg(msg, header.Seq);
        });
        // ซ่อมของ — client/RepairSystem.cs:9-16 → RegisterPostRepairEvents รอ Timer ที่ seq นี้
        _connection.Recv(delegate(RepairItem msg, PacketHeader header)
        {
            HandleRepairItemMsg(msg, header.Seq);
        });
        // ย้อมสี — client/CraftSystem.cs:271-283 → RegisterPostCraftEvents รอ Crafted (122)
        // ⚠️ Dye อยู่คาบเกี่ยวสองระบบ: เอกสารจัดไว้หมวด "ของ/กระเป๋า" แต่เกมยิงมาจาก CraftSystem
        // พร้อมโต๊ะทำงาน + recipe และมีฝาแฝดคือ Bleach ซึ่งเป็นของระบบคราฟต์ล้วน ๆ
        // ⇒ ถ้าระบบคราฟต์ลงทะเบียนไว้แล้วให้ใช้ของเขา (ครบกว่า) ตัวนี้เป็นแค่ตัวสำรอง
        // ที่เปลี่ยนสีจาก dyeables จริงในข้อมูล เผื่อระบบคราฟต์ยังไม่ถูกเสียบ
        if (!_connection.HasHandler(Dye.TypeCode))
        {
            _connection.Recv(delegate(Dye msg, PacketHeader header)
            {
                HandleDyeMsg(msg, header.Seq);
            });
        }
        // [6 ก.ย. 2026] ธงประดับที่ผู้เล่นติดได้ — client/EquipSystem.cs:86-89 ยิงตัวนี้
        // ทุกครั้งที่เข้าเกม (AddOnReady) แล้วรับด้วย **global handler** ที่ลงทะเบียนไว้ตั้งแต่ Awake
        // (บรรทัด 85 On<AttachableAccessories>) — ตัวที่ยิงไม่ได้ต่อ .On() ไว้เลย
        //
        // ⚠️ **ต้องตอบด้วย ReplyOf = 0 เท่านั้น** ตอบที่ seq จะไม่มีใครรับ
        //
        // ⚠️ ต้องตอบแม้รายการจะว่าง: OnAttachableAccessories เริ่มด้วย _attachableAccessories.Clear()
        // ⇒ ไม่ตอบ = รายการค้างจากเซสชันก่อนไม่ถูกล้าง (สลับตัวละครแล้วเห็นธงของตัวเก่า)
        _connection.Recv(delegate(GetAttachableAccessories msg, PacketHeader header)
        {
            SendAttachableAccessories();
        });

        // สลับชุดสวมใส่ — client/EquipSystem.cs:238-262 ChangePreset รอ OK ถึงจะสลับ preset จริง
        _connection.Recv(delegate(ChangeEquipSlotType msg, PacketHeader header)
        {
            HandleChangeEquipSlotTypeMsg(msg, header.Seq);
        });
        // ── ตู้เก็บของในสิ่งปลูกสร้าง ───────────────────────────────────────────────────
        _connection.Recv(delegate(GetInventory msg, PacketHeader header)
        {
            HandleGetInventoryMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(PutInItem msg, PacketHeader header)
        {
            HandlePutInItemMsg(msg, header.Seq);
        });
        // ── คลังสินค้า (Warehouse) ─────────────────────────────────────────────────────
        _connection.Recv(delegate(GetSectionItems msg, PacketHeader header)
        {
            HandleGetSectionItemsMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(AddItemsToWarehouse msg, PacketHeader header)
        {
            HandleAddItemsToWarehouseMsg(msg);
        });
        _connection.Recv(delegate(PopItemsFromWarehouse msg, PacketHeader header)
        {
            HandlePopItemsFromWarehouseMsg(msg);
        });
        _connection.Recv(delegate(MoveItemsInWarehouse msg, PacketHeader header)
        {
            HandleMoveItemsInWarehouseMsg(msg);
        });
        _connection.Recv(delegate(SetSectionItemOrder msg, PacketHeader header)
        {
            WarehouseStore.SetItemOrder(msg.EntityId, msg.SectionName, msg.ItemOrder);
        });
        // สร้างแท็บใหม่ในคลัง — client/InventorySystem.cs:700-711 ใช้ .All() ⇒ รับคำตอบชนิดใดก็ได้
        // แล้วตัดสินสำเร็จ/ไม่สำเร็จจาก Packet.IsSuccess ⇒ ตอบ OK พอ
        // ไม่ตอบ = ปุ่ม "เพิ่มแท็บ" ค้างหมุนตลอดไป
        _connection.Recv(delegate(MakeSection msg, PacketHeader header)
        {
            HandleMakeSectionMsg(msg, header.Seq);
        });
        // GetWarehouse (3683) อยู่หมวด "อื่น ๆ" ของเอกสาร ไม่ใช่ของไฟล์นี้ — แต่ถ้าไม่มีใครตอบ
        // handler คลัง 5 ตัวข้างบนจะไม่มีวันถูกยิง เพราะ client ต้องได้รายชื่อแท็บก่อน
        // (client/Durango.Logic.Item/Inventory.cs:171-177 Request → GetWarehouse)
        // ⇒ เสียบให้เฉพาะตอนยังไม่มีเจ้าของ เพื่อไม่ทับ handler ของระบบอื่นที่อาจมาทีหลัง
        if (!_connection.HasHandler(GetWarehouse.TypeCode))
        {
            _connection.Recv(delegate(GetWarehouse msg, PacketHeader header)
            {
                HandleGetWarehouseMsg(msg, header.Seq);
            });
        }
        // ── ไอเทมไม่เสถียร (ก่อนออกจากเกาะ Risky) ────────────────────────────────────────
        _connection.Recv(delegate(CheckUnstableItem msg, PacketHeader header)
        {
            HandleCheckUnstableItemMsg(header.Seq);
        });
        // ── กลุ่มที่ยังไม่มีระบบรองรับ — ตอบให้ถูกชนิดเพื่อไม่ให้ UI ค้าง ──────────────────
        RegisterUnimplementedItemHandlers();
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  จัดลำดับช่องในกระเป๋า
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// จำลำดับช่องที่ผู้เล่นลากสลับ — InventoryOrder (15)
    ///
    /// เก็บลำดับด้วยการ **เรียง <c>_context.InventoryItems</c> เองตามที่ขอมา** ไม่ใช่เก็บอาเรย์ id แยก
    /// เพราะ Core/Player.cs:1271 SendInventory ส่ง <c>InventoryItems.Items = _context.InventoryItems.ToArray()</c>
    /// แล้ว client เอาลำดับในอาเรย์นั้นเป็นลำดับช่องตรง ๆ (client/InventorySystem.cs:252-272)
    /// ⇒ เรียง list จริงคือทางเดียวที่ทำให้ลำดับรอดตอนต่อใหม่ โดยไม่ต้องแก้ไฟล์ที่อยู่นอกขอบเขต
    ///
    /// TargetArtifact มีค่า = กำลังจัดลำดับใน "ตู้ของสิ่งปลูกสร้าง" ไม่ใช่กระเป๋าตัวเอง
    /// (client/InventorySystem.cs:755-765)
    /// </summary>
    private void HandleInventoryOrderMsg(InventoryOrder msg)
    {
        if (msg.TargetArtifact.HasValue)
        {
            WarehouseStore.SetItemOrder(msg.TargetArtifact.Value.EntityId,
                WarehouseStore.ContainerSection, msg.ItemOrder);
            return;
        }
        if (ReorderInPlace(_context.InventoryItems, msg.ItemOrder))
        {
            OnContextChanged();
        }
    }

    /// <summary>
    /// เรียง list ตามลำดับ id ที่ขอมา — id ที่ไม่ได้ระบุถูกต่อท้ายโดยคงลำดับเดิมไว้
    /// คืน true ถ้าลำดับเปลี่ยนจริง (จะได้ไม่สั่งเซฟฟรี ๆ)
    /// </summary>
    private static bool ReorderInPlace(List<Item> items, string[] order)
    {
        if (items == null || items.Count == 0 || order == null || order.Length == 0) return false;
        var byId = new Dictionary<string, int>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            if (!string.IsNullOrEmpty(items[i].Id)) byId[items[i].Id] = i;
        }
        var taken = new bool[items.Count];
        var sorted = new List<Item>(items.Count);
        foreach (string id in order)
        {
            if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out int idx) || taken[idx]) continue;
            taken[idx] = true;
            sorted.Add(items[idx]);
        }
        for (int i = 0; i < items.Count; i++)
        {
            if (!taken[i]) sorted.Add(items[i]);
        }
        bool changed = false;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Id != sorted[i].Id) { changed = true; break; }
        }
        if (!changed) return false;
        items.Clear();
        items.AddRange(sorted);
        return true;
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ล็อกไอเทม
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ล็อก/ปลดล็อกไอเทม — LockOrUnlockItems (3497)
    ///
    /// ของที่ล็อกไว้ client จะกันไม่ให้ทิ้ง/ใช้เป็นวัตถุดิบเอง (SafeLevel.Locked —
    /// client/InventorySystem.cs:395-409) ⇒ ฝั่งเซิร์ฟแค่ต้อง "จำ" แล้วส่งรายการกลับไปทั้งชุด
    /// เพราะ UpdateLockedItems (:369-376) เคลียร์ของเดิมทิ้งทุกครั้งที่ได้รับ
    /// </summary>
    private void HandleLockOrUnlockItemsMsg(LockOrUnlockItems msg)
    {
        if (msg.ItemIds == null || msg.ItemIds.Length == 0) return;
        foreach (string id in msg.ItemIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            // ล็อกได้เฉพาะของที่อยู่ในกระเป๋าจริง — กัน client ส่ง id มั่วมาบวมรายการ
            if (msg.Lock && _context.InventoryItems.Any(it => it.Id == id)) _lockedItemIds.Add(id);
            else if (!msg.Lock) _lockedItemIds.Remove(id);
        }
        SendInventoryInfos();
    }

    /// <summary>
    /// push InventoryInfos (109) ชุดใหม่ — client รับแบบ global แล้วแทนที่ข้อมูลทั้งก้อน
    /// (client/InventorySystem.cs:78 → :126-132 → :234-241 UpdatePlayerInventoryInfo)
    /// ⚠️ ProtectedItems เป็น struct ไม่ใช่ nullable ⇒ ต้องใส่อาเรย์ว่าง ไม่ใช่ปล่อย null
    /// ไม่งั้น UpdateProtectedItems(null) จะแค่เคลียร์ทิ้ง ซึ่งบังเอิญตรงกับที่เราต้องการอยู่แล้ว
    /// แต่ใส่ให้ชัดดีกว่าเพราะเรายังไม่มีระบบ "ของที่ปกป้องไว้ตอนตาย"
    /// </summary>
    private void SendInventoryInfos()
    {
        Send(new InventoryInfos
        {
            EntityId = EntityId,
            MaxSize = InventoryMaxSizeMirroredFromPlayerCs,
            LockedItemIds = _lockedItemIds.ToArray(),
            ItemOrder = null,
            ProtectedItems = new ProtectedItems { ItemIds = Array.Empty<string>() }
        });
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ใช้ของ (กิน/ดื่ม)
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// กิน/ดื่มของ — UseItem (17)
    ///
    /// client รอ reply สองแบบที่ seq เดียวกัน (client/InventorySystem.cs:787-818):
    ///   StartTimer (124) = มีหลอดเวลาให้รอ · OK (1231) = สำเร็จทันที
    /// เราใช้ OK เพราะยังไม่มีระบบ "ย่อยอาหารตามเวลา" (ดูหมายเหตุเรื่อง digestivetime ข้างล่าง)
    /// แล้ว push ItemUsed (18) ตามไปเพื่อสั่งท่าทางกิน/ดื่ม (client/InventorySystem.cs:655-665
    /// → PlayerController.MotionUpdater.Motion(motion, time)) — Time = 0 แปลว่า "เล่นจนจบท่า"
    /// (client/LocalMotionUpdater.cs:239-246 time &lt;= 0 ⇒ PlayUntil = 0 = ไม่ตัดจบเอง)
    ///
    /// ค่าที่ใช้มาจากไฟล์จริง data/assets/performance.json → "food" → &lt;prototype&gt; → "[minLv, maxLv]"
    /// ⇒ ของทุกชิ้นใช้สูตรของตัวเองตามเลเวลไอเทม ไม่มีการเดาตัวเลข
    /// </summary>
    /// <summary>
    /// ธงประดับที่ผู้เล่นคนนี้ติดได้ — <c>AttachableAccessories</c>(9823458)
    ///
    /// ข้อมูลจริง <c>data/assets/accessories.json</c> มี **3 รายการ** ทั้งหมดเป็นธงแคลน
    /// (<c>clan_honour_04/05/06</c> · <c>type = 1</c> = <c>AccessoryType.ClanDefense</c>)
    /// ปลดล็อกด้วย <c>limits {"1": N}</c> = <c>AccessoryLimit.DefenseCount</c>
    /// คือ "ป้องกันฐานแคลนสำเร็จติดกัน N ครั้ง"
    ///
    /// ⇒ เซิร์ฟยังไม่มีระบบแคลนและไม่มีการป้องกันฐาน **ไม่มีใครปลดล็อกได้เลยตามนิยามของข้อมูลเอง**
    /// จึงตอบอาเรย์ว่าง ซึ่งเป็นคำตอบที่ถูกต้อง ไม่ใช่การยอมแพ้ — ส่ง id ทั้งสามไปให้เฉย ๆ
    /// จะกลายเป็นการแจกของที่ข้อมูลเกมบอกว่าต้องได้มาด้วยเงื่อนไข
    /// </summary>
    private void SendAttachableAccessories()
    {
        Send(new AttachableAccessories { Accessories = Array.Empty<string>() });
    }

    /// <summary>
    /// สร้างแท็บใหม่ในคลังของสิ่งปลูกสร้าง — ตัวเก็บของมีอยู่แล้วที่ WarehouseStore.MakeSection
    ///
    /// ใช้ด่านเจ้าของตัวเดียวกับการเปิดคลัง เพราะการเพิ่มแท็บคือการแก้ของในหลังนั้น
    /// </summary>
    /// <summary>
    /// หลังนี้ยังมีของค้างอยู่ในตู้/คลังไหม — ใช้กันไม่ให้ "เก็บ/รื้อ" แล้วของหายเงียบ ๆ
    ///
    /// ⚠️ <c>ArtifactManager.RemoveArtifact</c> ล้างแค่ addOns/owners/plantings/buildMaterials
    /// **ไม่แตะ WarehouseStore** ⇒ ของยังค้างในหน่วยความจำใต้ entity id เดิม แต่รอบเซฟถัดไป
    /// <c>WarehouseStore.Export(Artifacts?.Keys)</c> กรองด้วยรายชื่อหลังที่ยังอยู่ ⇒ **หายจากไฟล์ถาวร**
    /// และดึงคืนไม่ได้เลยแม้ก่อนรีสตาร์ต เพราะ <c>MayTouchArtifact</c> ล้มเหลวเมื่อหลังนั้นไม่มีแล้ว
    ///
    /// ⇒ ปฏิเสธไปเลยดีกว่า ให้ผู้เล่นขนของออกเองก่อน (ด่านเดียวกับของบนหุ่นโชว์)
    /// </summary>
    internal bool HasStoredItems(string entityId)
    {
        if (string.IsNullOrEmpty(entityId)) return false;
        foreach (string section in WarehouseStore.SectionNames(entityId))
        {
            List<Item> items = WarehouseStore.Items(entityId, section, create: false);
            if (items is { Count: > 0 }) return true;
        }
        return false;
    }

    private void HandleMakeSectionMsg(MakeSection msg, uint seq)
    {
        if (!MayTouchArtifact(msg.EntityId, "สร้างแท็บคลัง"))
        {
            Send(new Abort { Text = "ทำกับสิ่งปลูกสร้างนี้ไม่ได้" }, seq);
            return;
        }
        if (string.IsNullOrWhiteSpace(msg.SectionName))
        {
            Send(new Abort { Text = "ต้องตั้งชื่อแท็บ" }, seq);
            return;
        }
        if (!WarehouseStore.MakeSection(msg.EntityId, msg.SectionName))
        {
            Send(new Abort { Text = "มีแท็บชื่อนี้อยู่แล้ว" }, seq);
            return;
        }
        Console.WriteLine($"[คลัง] {Short(EntityId)} เพิ่มแท็บ '{msg.SectionName}' ใน {msg.EntityId[..Math.Min(8, msg.EntityId.Length)]}");
        _world.Save();
        Send(default(OK), seq);
    }

    private void HandleUseItemMsg(UseItem msg, uint seq)
    {
        int idx = _context.InventoryItems.FindIndex(it => it.Id == msg.ItemId);
        if (idx < 0)
        {
            Send(new Abort { Text = "ไม่พบไอเทมในกระเป๋า" }, seq);
            return;
        }
        Item item = _context.InventoryItems[idx];
        if (_lockedItemIds.Contains(item.Id))
        {
            Send(new Abort { Text = "ไอเทมถูกล็อกอยู่" }, seq);
            return;
        }
        // ── [6 ก.ย. 2026] บังเหียนที่มีสัตว์เชื่องแล้วอยู่ข้างใน = "ผูกพัน" (귀속) ──────────
        //
        // ฝั่งเกมไม่มีข้อความเฉพาะสำหรับการผูกพัน — มันยิง UseItem ตัวเดียวกับการกินอาหาร
        // (client/Durango.UI/InventoryContainerBase.cs:1116-1119 DoImprinting → InventorySystem.UseItem)
        // แล้วรอ OK หรือ StartTimer กลับมา (client/InventorySystem.cs:787-817)
        //
        // ⚠️ ไม่มีสาขานี้ = บังเหียนที่ฝึกเสร็จแล้ว **ใช้ไม่ได้เลย** ตกลงไปที่ Abort ข้างล่าง
        // ซึ่งเป็นเหตุผลที่ Player.Domestication.cs เคยต้องยัดสัตว์เข้า PetStore ตั้งแต่ตอนเอาออก
        // จากกรง (ดูคอมเมนต์ HandleTakeOutReinFromCageMsg) — ตอนนี้ทำตามของจริงได้แล้ว
        if (item.Ext is Reins)
        {
            if (!TryImprintRein(item, out string imprintError))
            {
                Send(new Abort { Text = imprintError ?? "ใช้บังเหียนนี้ไม่ได้" }, seq);
                return;
            }
            _context.InventoryItems.RemoveAt(idx);
            _lockedItemIds.Remove(item.Id);
            Send(new InventoryUpdated
            {
                EntityId = EntityId,
                RemovedItemIds = new[] { item.Id }
            });
            Send(default(OK), seq);
            // หน้าจอสัตว์เลี้ยงเปิดทันทีหลังได้ OK แล้วอ่านรายชื่อจากชุดที่เซิร์ฟส่งให้
            // ⇒ ต้องส่งชุดใหม่ ไม่งั้นสัตว์ที่เพิ่งผูกพันไม่โผล่จนกว่าจะเข้าเกมใหม่
            SendPetsInfo(0u);
            OnContextChanged();
            return;
        }

        FoodTable.Effect food = FoodTable.Get(item.Prototype, item.Level);
        if (food == null)
        {
            // ไม่ใช่ของกิน — ของใช้ชนิดอื่น (ยา/กล่องสุ่ม/หนังสือสูตร) ยังไม่มีระบบรองรับ
            // ตอบ Abort เพื่อให้ข้อความขึ้นบนจอ ดีกว่าเงียบแล้วผู้เล่นกดซ้ำไปเรื่อย ๆ
            // (client/GameManager.cs:303-306 DefaultAbortHandler → UIManager.SystemMsg)
            Send(new Abort { Text = "ยังใช้ไอเทมชนิดนี้ไม่ได้" }, seq);
            return;
        }

        // ── ผลต่อหลอดสถานะ (ดู Core/SurvivalState.cs) ──────────────────────────────────
        // ค่าบวก = ฟื้น · fatigue ในไฟล์เป็นค่าลบอยู่แล้ว (เช่น fatigue_drug = "-150") จึงบวกตรง ๆ
        // SurvivalState.Add จะ clamp ให้อยู่ในช่วง min..max ของหลอดเอง (MakeLine)
        bool touched = false;
        touched |= _survival.Add(SurvivalState.KeyLife, food.Life);
        touched |= _survival.Add(SurvivalState.KeyHealth, food.Health);
        touched |= _survival.Add(SurvivalState.KeyFatigue, food.Fatigue);
        // energy_potential = พลังงานที่อาหารชิ้นนี้ให้ได้ทั้งหมด
        // ⚠️ **ที่เป็นของเราคือการให้ทันทีทั้งก้อน** — ไฟล์จริงมี energy_expression /
        // energy_per_sec / digestivetime ไว้ทยอยปล่อยระหว่างย่อย แต่ความหมายของสามตัวนี้
        // ยืนยันจากซอร์สเกมไม่ได้ (client ไม่ได้อ่านเลย เป็นงานของเซิร์ฟแท้) ⇒ ไม่เดาสูตร
        touched |= _survival.Add(SurvivalState.KeyEnergy, food.EnergyPotential);
        if (touched) FlushSurvival();

        _context.InventoryItems.RemoveAt(idx);
        _lockedItemIds.Remove(item.Id);
        Send(new InventoryUpdated
        {
            EntityId = EntityId,
            RemovedItemIds = new[] { item.Id }
        });
        Send(default(OK), seq);
        Send(new ItemUsed { Motion = food.EatMotion, Time = 0f, Msg = null });
        OnContextChanged();
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ซ่อมของ
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ซ่อมไอเทมด้วยชุดซ่อม — RepairItem (3717)
    ///
    /// client/RepairSystem.cs:29-45 รอ <see cref="Messages.Timer"/> ที่ seq นี้ (เล่นท่าซ่อม +
    /// onResult(true)) ถ้าได้อย่างอื่น .Rest จะถือว่าล้มเหลว ⇒ ต้องตอบ Timer เท่านั้นเวลาสำเร็จ
    ///
    /// Duration มาจากข้อมูลจริง constants.json → repair → item → time (= 3.0)
    /// ผลลัพธ์ก็ตามข้อมูลจริง: durability_result.success = "max_durability" ⇒ เต็มหลอด
    ///
    /// **ที่ยังไม่ได้ทำ** (จงใจ ไม่ใช่ลืม): โอกาสสำเร็จ/สำเร็จมาก (success_ratio),
    /// ค่าพลังงาน (energy = "required_perf * 0.8"), เพดานจำนวนครั้ง (limit_durability)
    /// — สามอย่างนี้ต้องมีระบบความสามารถของตัวละครก่อน ซึ่งเซิร์ฟยังไม่มี จึงให้สำเร็จเสมอ
    /// </summary>
    private void HandleRepairItemMsg(RepairItem msg, uint seq)
    {
        int idx = _context.InventoryItems.FindIndex(it => it.Id == msg.ItemId);
        if (idx < 0)
        {
            Send(new Abort { Text = "ไม่พบไอเทมที่จะซ่อม" }, seq);
            return;
        }
        // ต้องมีชุดซ่อมอยู่ในกระเป๋าจริงทุกชิ้น ไม่งั้นซ่อมฟรี
        var kits = new List<string>();
        if (msg.KitItemIds != null)
        {
            foreach (string kitId in msg.KitItemIds)
            {
                if (!string.IsNullOrEmpty(kitId) && _context.InventoryItems.Any(it => it.Id == kitId))
                {
                    kits.Add(kitId);
                }
            }
        }
        if (kits.Count == 0)
        {
            Send(new Abort { Text = "ไม่มีชุดซ่อม" }, seq);
            return;
        }

        Item item = _context.InventoryItems[idx];
        // Durability ของไอเทมเป็นสัดส่วน 0..1 (Core/Cheats.cs:31 สร้างด้วย Gauge(1f, 0f, node(0,1)))
        // ⇒ "เต็มหลอด" คือเส้นแบนที่ค่า max ไม่ใช่ตัวเลขดิบของ prototype
        item.Durability = new Gauge(1f, 0f, new[] { new GaugeNode(0.0, 1f) });
        _context.InventoryItems[idx] = item;
        _context.InventoryItems.RemoveAll(it => kits.Contains(it.Id));
        foreach (string kitId in kits) _lockedItemIds.Remove(kitId);

        Send(new InventoryUpdated
        {
            EntityId = EntityId,
            Items = new[] { item },
            RemovedItemIds = kits.ToArray()
        });
        Send(new Messages.Timer { Duration = ItemConstants.RepairItemTime }, seq);
        OnContextChanged();
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ย้อมสี
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ย้อมสีไอเทม — Dye (3666)
    ///
    /// client/CraftSystem.cs:258-284 Dyeing ส่ง Materials = {slot_id: [item_id]} โดยช่อง IsBase
    /// คือของที่จะย้อม ช่องอื่นคือสีย้อม — แต่ข้อมูล recipe ฝั่งเซิร์ฟถูกพอร์ตมาแบบย่อ
    /// (Support/YamlArtifact.cs:10-13 Recipe มีแค่ add_on) ⇒ แยกสองตัวนี้จาก **ข้อมูลไอเทมจริง**
    /// แทน: ของที่ย้อมได้คือของที่ prototype มี dyeables ครอบคลุมช่องสีที่ขอมา
    /// (data/assets/item/prototype_data.json → "dyeables": [0,1,2] = R/G/B ตรงกับ
    ///  Shared.Item.ColorChannel) ที่เหลือถือเป็นสีย้อม
    ///
    /// สีใหม่ = สีของ "ตัวสีย้อม" เอง — **เป็นทางเลือกของเรา** ที่ใกล้เคียงที่สุดกับของจริง
    /// เพราะไอเทมสีย้อมพก hex ของตัวเองมาใน ColorR อยู่แล้ว (Core/Cheats.cs:38-43 สุ่มจาก
    /// ตารางสีของ prototype) ตารางแปลงสีจริงของเซิร์ฟแท้ (colortable) ยังไม่ได้พอร์ต
    /// </summary>
    private void HandleDyeMsg(Dye msg, uint seq)
    {
        if (msg.Channel == ColorChannel.Invalid || msg.Materials == null || msg.Materials.Count == 0)
        {
            Send(new Abort { Text = "ข้อมูลย้อมสีไม่ครบ" }, seq);
            return;
        }
        var ids = msg.Materials.Values
            .Where(v => v != null)
            .SelectMany(v => v)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        int targetIdx = -1;
        int dyeIdx = -1;
        foreach (string id in ids)
        {
            int i = _context.InventoryItems.FindIndex(it => it.Id == id);
            if (i < 0) continue;
            Prototype proto = PrototypeYaml.GetItemPrototype(_context.InventoryItems[i].Prototype,
                _context.InventoryItems[i].Level);
            bool dyeable = proto?.Dyeables != null && proto.Dyeables.Contains(msg.Channel);
            if (dyeable && targetIdx < 0) targetIdx = i;
            else if (!dyeable && dyeIdx < 0) dyeIdx = i;
        }
        if (targetIdx < 0 || dyeIdx < 0)
        {
            Send(new Abort { Text = "ย้อมสีชิ้นนี้ไม่ได้" }, seq);
            return;
        }

        Item target = _context.InventoryItems[targetIdx];
        Item dye = _context.InventoryItems[dyeIdx];
        switch (msg.Channel)
        {
            case ColorChannel.ColorR: target.ColorR = dye.ColorR; break;
            case ColorChannel.ColorG: target.ColorG = dye.ColorR; break;
            case ColorChannel.ColorB: target.ColorB = dye.ColorR; break;
        }
        _context.InventoryItems[targetIdx] = target;
        _context.InventoryItems.RemoveAll(it => it.Id == dye.Id);
        _lockedItemIds.Remove(dye.Id);

        Send(new InventoryUpdated
        {
            EntityId = EntityId,
            Items = new[] { target },
            RemovedItemIds = new[] { dye.Id }
        });
        // Result.Success (2) — BigFailure เท่านั้นที่ client ถือว่าล้มเหลว (CraftSystem.cs:176-186)
        Send(new Crafted
        {
            Result = Result.Success,
            Items = new[] { target }
        }, seq);
        // ของที่ย้อมอาจกำลังสวมอยู่ ⇒ ต้องอัปเดตหน้าตาแล้วบอกคนรอบข้าง
        // (ทางเดียวกับ cheat "it_color" ใน Core/Player.cs:858-862)
        if (_context.EquippedItems.Any(pair => pair.Value == target.Id))
        {
            UpdateEquipments();
            _world.BroadCast(_context.AppearPlayer.Display);
        }
        OnContextChanged();
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ชุดสวมใส่
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// สลับชุดสวมใส่ (preset) — ChangeEquipSlotType (81534)
    ///
    /// client/EquipSystem.cs:240-262 จะสลับ preset จริงเมื่อได้ OK เท่านั้น
    /// เซิร์ฟนี้มี preset เดียวคือ Slot1 (Core/Player.cs:1279 UpdateEquipments ตั้ง
    /// CurrentType = Slot1 และ Presets มีคีย์เดียว) ⇒ ตอบ OK เฉพาะ Slot1
    /// ถ้าตอบ OK ให้ช่องอื่น client จะเชื่อว่าสลับสำเร็จแล้วโชว์ชุดว่าง ทั้งที่ตัวละครยังใส่ของเดิม
    /// </summary>
    private void HandleChangeEquipSlotTypeMsg(ChangeEquipSlotType msg, uint seq)
    {
        if (msg.SlotType == EquipSlotType.Slot1)
        {
            // ⚠️ ต้องส่ง OK **ก่อน** แล้วค่อย push Equipments แบบไม่ผูก seq
            // client/Durango.Network/Connection.cs:905-908 ลบ handler ของ seq ทิ้งทันทีที่ได้ reply
            // ใบแรก ⇒ ถ้าส่ง Equipments ไปที่ seq ก่อน OK จะไม่มีใครรับ .On<OK> อีกเลย
            // = ปุ่มสลับชุดกดแล้วเงียบ (client/EquipSystem.cs:240-248)
            Send(default(OK), seq);
            SendEquipments();
            return;
        }
        Send(new Abort { Text = "ยังมีชุดสวมใส่ชุดเดียว" }, seq);
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ตู้เก็บของในสิ่งปลูกสร้าง
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ขอดูของในตู้ — GetInventory (2010) ตอบด้วย Inventory (110)
    ///
    /// Target = null ไม่เกิดขึ้นจากเกม (client/Durango.Logic.Item/Inventory.cs:153-154
    /// InventoryType.Player return ทิ้งก่อนส่ง) แต่รองรับไว้ให้ครบ
    ///
    /// ⚠️ ไม่ตอบ = client ค้างที่ State.Loading ตลอดกาล เพราะ Request() เปลี่ยนสถานะเป็น Loading
    /// แล้วมีแต่ Requested() ตอนได้ข้อมูลเท่านั้นที่ปลดล็อก (:147-186)
    ///
    /// MaxSize มาจากข้อมูลจริง performance.json → "inventory" → &lt;prototype&gt; → size_limit
    /// </summary>
    private void HandleGetInventoryMsg(GetInventory msg, uint seq)
    {
        if (!msg.Target.HasValue)
        {
            SendInventory();
            return;
        }
        string entityId = msg.Target.Value.EntityId;
        List<Item> items = WarehouseStore.Items(entityId, WarehouseStore.ContainerSection, create: false);
        Send(new Messages.Inventory
        {
            EntityId = entityId,
            InventoryItems = new InventoryItems
            {
                EntityId = entityId,
                Items = items?.ToArray() ?? Array.Empty<Item>()
            },
            InventoryInfos = new InventoryInfos
            {
                EntityId = entityId,
                MaxSize = ContainerSizeLimit(entityId),
                LockedItemIds = Array.Empty<string>(),
                ItemOrder = null,
                ProtectedItems = new ProtectedItems { ItemIds = Array.Empty<string>() }
            },
            Wallet = null
        }, seq);
    }

    /// <summary>
    /// ความจุของตู้ใบนั้นจากข้อมูลจริง — performance.json → "inventory" → size_limit
    /// (เช่น secured_box ที่เลเวล 1-59 = "75 + int(level * 0.35)")
    /// ไม่พบข้อมูล = 0 (client จะถือว่าใส่อะไรไม่ได้ ซึ่งตรงกับความจริงว่าเรายังไม่รู้ความจุ)
    /// </summary>
    private int ContainerSizeLimit(string entityId)
    {
        AppearArtifact? artifact = _world.ArtifactManager.Get(entityId);
        if (!artifact.HasValue) return 0;
        // คีย์ของ performance.json คือ prototype id แบบข้อความ (เช่น "secured_box") ซึ่งก็คือ
        // __name__ ของ artifact prototype — BlueprintStore เก็บไว้ในช่อง Id ให้แล้ว
        // (Support/BlueprintStore.cs:54 Id = proto.__name__)
        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(artifact.Value.EntityType);
        if (blueprint == null) return 0;
        int level = artifact.Value.States.Level > 0 ? artifact.Value.States.Level : 1;
        return (int)ItemConstants.ContainerSizeLimit(blueprint.Id, level);
    }

    /// <summary>
    /// เก็บของเข้าตู้ — PutInItem (2434)
    ///
    /// ⚠️ **จงใจตอบ Abort ไม่ใช่ทำไม่เสร็จ** — ดูหมายเหตุข้อ 2 ที่หัวไฟล์:
    /// ตัวคู่กันคือ TakeOutItem (2435) ถูก Core/Player.cs:478-488 จองไว้ให้หุ่นโชว์เสื้ออย่างเดียว
    /// (ไม่ใช่หุ่น = ตอบ Abort เสมอ) และไฟล์นั้นอยู่นอกขอบเขตของงานนี้
    /// ⇒ ถ้ารับของเข้าตู้ตอนนี้ ผู้เล่นจะเอาของออกไม่ได้ = ของหายถาวร
    /// ยอมให้ฟีเจอร์ไม่ทำงานดีกว่าทำให้ของผู้เล่นหาย
    ///
    /// วิธีปลดล็อกเมื่อแก้ Core/Player.cs ได้: ใน handler ของ TakeOutItem ให้ลอง
    /// ArtifactManager.TakeOutItems ก่อน (หุ่น) ถ้าไม่ผ่านค่อยดึงจาก
    /// Player.WarehouseStore.Pop(entityId, WarehouseStore.ContainerSection, ids) แล้วค่อยเปลี่ยน
    /// เมธอดนี้ให้เรียก MoveFromInventory ตามแบบเดียวกับ HandleAddItemsToWarehouseMsg
    /// </summary>
    private void HandlePutInItemMsg(PutInItem msg, uint seq)
    {
        Console.WriteLine($"[item] ปฏิเสธ PutInItem เข้า {msg.EntityId} — ยังไม่มีทางเอาของออก (TakeOutItem ถูกจองให้หุ่นโชว์เสื้อ)");
        Send(new Abort { Text = "ยังเก็บของเข้าตู้ไม่ได้ ใช้คลังสินค้าแทน" }, seq);
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  คลังสินค้า (Warehouse)
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// รายชื่อแท็บของคลัง — GetWarehouse (3683) ตอบ Warehouse (3684)
    ///
    /// client/InventorySystem.cs:488-507 เอา SectionInfos ไปทำแท็บ แล้วตั้ง SelectedCategory = null
    /// ⇒ ต้องมีอย่างน้อยหนึ่งแท็บถึงจะกดดูของได้ · MaxSize ต่อแท็บมาจากข้อมูลจริง
    /// constants.json → warehouse → section_size (client อ่านค่าเดียวกันเองที่ :491)
    /// </summary>
    private void HandleGetWarehouseMsg(GetWarehouse msg, uint seq)
    {
        // ⚠️ ตู้/คลังผูกกับสิ่งปลูกสร้าง ⇒ ต้องเป็นเจ้าของและอยู่ใกล้ ไม่งั้นเดินผ่านบ้านคนอื่น
        // จำ entity id จากแพ็กเก็ต แล้วขนของทั้งคลังเข้ากระเป๋าตัวเองได้โดยเจ้าของไม่รู้ตัว
        if (!MayTouchArtifact(msg.EntityId, "เปิดตู้")) return;
        WarehouseStore.EnsureDefaultSection(msg.EntityId, DefaultWarehouseSection);
        Send(new Messages.Warehouse
        {
            EntityId = msg.EntityId,
            SectionInfos = WarehouseStore.SectionNames(msg.EntityId)
                .Select(name => new InventorySectionInfos
                {
                    SectionName = name,
                    UsedSize = WarehouseStore.UsedSize(msg.EntityId, name),
                    MaxSize = ItemConstants.WarehouseSectionSize
                })
                .ToArray()
        }, seq);
    }

    /// <summary>
    /// ของในแท็บที่กด — GetSectionItems (3692) ตอบ SectionItems (3693)
    /// client/InventorySystem.cs:570-594 ล้างรายการทิ้งก่อนแล้วรอชุดนี้ ⇒ ไม่ตอบ = แท็บว่างถาวร
    /// </summary>
    private void HandleGetSectionItemsMsg(GetSectionItems msg, uint seq)
    {
        // ⚠️ ตู้/คลังผูกกับสิ่งปลูกสร้าง ⇒ ต้องเป็นเจ้าของและอยู่ใกล้ ไม่งั้นเดินผ่านบ้านคนอื่น
        // จำ entity id จากแพ็กเก็ต แล้วขนของทั้งคลังเข้ากระเป๋าตัวเองได้โดยเจ้าของไม่รู้ตัว
        if (!MayTouchArtifact(msg.EntityId, "ดูของในตู้")) return;
        List<Item> items = WarehouseStore.Items(msg.EntityId, msg.SectionName, create: false);
        Send(new SectionItems
        {
            Items = items?.ToArray() ?? Array.Empty<Item>(),
            // ลำดับถูกเรียงไว้ใน list อยู่แล้ว (SetItemOrder) จึงไม่ต้องส่งซ้ำ
            ItemOrder = Array.Empty<string>()
        }, seq);
    }

    /// <summary>
    /// ใส่ของเข้าคลัง — AddItemsToWarehouse (3690)
    /// client/InventorySystem.cs:916-928 ส่งแล้วไม่รอ reply — มันรอ push สองตัว:
    /// InventoryUpdated (ของหายจากกระเป๋า) กับ WarehouseUpdated (ของโผล่ในแท็บ + ที่ว่างเปลี่ยน)
    /// </summary>
    private void HandleAddItemsToWarehouseMsg(AddItemsToWarehouse msg)
    {
        List<Item> section = WarehouseStore.Items(msg.EntityId, msg.SectionName, create: false);
        if (section == null)
        {
            Send(new Abort { Text = "ไม่พบแท็บคลังนี้" });
            return;
        }
        int free = ItemConstants.WarehouseSectionSize - WarehouseStore.UsedSize(msg.EntityId, msg.SectionName);
        var moved = new List<Item>();
        foreach (string id in msg.ItemIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(id) || _lockedItemIds.Contains(id)) continue;
            int idx = _context.InventoryItems.FindIndex(it => it.Id == id);
            if (idx < 0) continue;
            Item item = _context.InventoryItems[idx];
            int size = Math.Max(1, item.Size);
            if (size > free) break;                 // เต็มแล้ว — หยุดแค่นี้ ที่ย้ายไปแล้วยังถือว่าสำเร็จ
            free -= size;
            _context.InventoryItems.RemoveAt(idx);
            section.Add(item);
            moved.Add(item);
        }
        if (moved.Count == 0)
        {
            Send(new Abort { Text = "คลังเต็ม" });
            return;
        }
        Send(new InventoryUpdated
        {
            EntityId = EntityId,
            RemovedItemIds = moved.Select(it => it.Id).ToArray()
        });
        SendWarehouseUpdated(msg.EntityId, msg.SectionName, moved.ToArray(), null);
        OnContextChanged();
    }

    /// <summary>
    /// เอาของออกจากคลัง — PopItemsFromWarehouse (3691)
    /// client/InventorySystem.cs:956-965 ส่งแล้วไม่รอ reply เช่นกัน
    /// </summary>
    private void HandlePopItemsFromWarehouseMsg(PopItemsFromWarehouse msg)
    {
        // ⚠️ ตู้/คลังผูกกับสิ่งปลูกสร้าง ⇒ ต้องเป็นเจ้าของและอยู่ใกล้ ไม่งั้นเดินผ่านบ้านคนอื่น
        // จำ entity id จากแพ็กเก็ต แล้วขนของทั้งคลังเข้ากระเป๋าตัวเองได้โดยเจ้าของไม่รู้ตัว
        if (!MayTouchArtifact(msg.EntityId, "เอาของออกจากตู้")) return;
        List<Item> section = WarehouseStore.Items(msg.EntityId, msg.SectionName, create: false);
        if (section == null) return;
        int free = InventoryMaxSizeMirroredFromPlayerCs
                   - _context.InventoryItems.Sum(it => Math.Max(1, it.Size));
        var moved = new List<Item>();
        foreach (string id in msg.ItemIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(id)) continue;
            int idx = section.FindIndex(it => it.Id == id);
            if (idx < 0) continue;
            Item item = section[idx];
            int size = Math.Max(1, item.Size);
            if (size > free) break;                 // กระเป๋าเต็ม
            free -= size;
            section.RemoveAt(idx);
            _context.InventoryItems.Add(item);
            moved.Add(item);
        }
        if (moved.Count == 0)
        {
            Send(new Abort { Text = "กระเป๋าเต็ม" });
            return;
        }
        Send(new InventoryUpdated
        {
            EntityId = EntityId,
            Items = moved.ToArray()
        });
        SendWarehouseUpdated(msg.EntityId, msg.SectionName, null, moved.Select(it => it.Id).ToArray());
        OnContextChanged();
    }

    /// <summary>
    /// ย้ายของข้ามแท็บในคลัง — MoveItemsInWarehouse (3685)
    /// ต้องส่ง WarehouseUpdated สองใบ (แท็บต้นทาง/ปลายทาง) เพราะ client อัปเดตทีละแท็บ
    /// (client/InventorySystem.cs:183-208 วนหา categories[i].Key == msg.SectionName ใบละครั้ง)
    /// </summary>
    private void HandleMoveItemsInWarehouseMsg(MoveItemsInWarehouse msg)
    {
        List<Item> from = WarehouseStore.Items(msg.EntityId, msg.SourceSectionName, create: false);
        List<Item> to = WarehouseStore.Items(msg.EntityId, msg.TargetSectionName, create: false);
        if (from == null || to == null || ReferenceEquals(from, to)) return;
        int free = ItemConstants.WarehouseSectionSize - WarehouseStore.UsedSize(msg.EntityId, msg.TargetSectionName);
        var moved = new List<Item>();
        foreach (string id in msg.ItemIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(id)) continue;
            int idx = from.FindIndex(it => it.Id == id);
            if (idx < 0) continue;
            Item item = from[idx];
            int size = Math.Max(1, item.Size);
            if (size > free) break;
            free -= size;
            from.RemoveAt(idx);
            to.Add(item);
            moved.Add(item);
        }
        if (moved.Count == 0)
        {
            Send(new Abort { Text = "แท็บปลายทางเต็ม" });
            return;
        }
        SendWarehouseUpdated(msg.EntityId, msg.SourceSectionName, null, moved.Select(it => it.Id).ToArray());
        SendWarehouseUpdated(msg.EntityId, msg.TargetSectionName, moved.ToArray(), null);
    }

    private void SendWarehouseUpdated(string entityId, string section, Item[] added, string[] removedIds)
    {
        Send(new WarehouseUpdated
        {
            EntityId = entityId,
            SectionName = section,
            Items = added ?? Array.Empty<Item>(),
            RemovedItemIds = removedIds ?? Array.Empty<string>(),
            ItemOrder = Array.Empty<string>(),
            ProtectedItems = null,
            UsedSize = WarehouseStore.UsedSize(entityId, section),
            MaxSize = ItemConstants.WarehouseSectionSize
        });
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ไอเทมไม่เสถียร
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// นับไอเทมไม่เสถียรก่อนออกจากเกาะ — CheckUnstableItem (1590123) ตอบ ResultCheckUnstableItem
    ///
    /// client/MapSystem.cs:505-528 อ่านแค่ TotalUnstableCount: &gt; 0 = เด้งกล่องเตือนว่าของจะหาย
    /// ⚠️ ไม่ตอบ = action ที่ห่อไว้ไม่ถูกเรียก ⇒ **ผู้เล่นวาร์ปกลับที่ดินไม่ได้เลย** แบบเงียบ ๆ
    /// Result ไม่ถูกอ่านที่ไหนเลยในซอร์สเกม จึงส่ง true ไว้ตามความหมายตรงตัวว่า "ตรวจแล้ว"
    /// นับจาก Item.Unstable ของจริงในกระเป๋า (กระเป๋าสัตว์เลี้ยงยังไม่มีระบบ จึงยังไม่ได้นับ)
    /// </summary>
    private void HandleCheckUnstableItemMsg(uint seq)
    {
        int count = _context.InventoryItems.Count(it => it.Unstable);
        Send(new ResultCheckUnstableItem
        {
            Result = true,
            TotalUnstableCount = count
        }, seq);
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ยังไม่มีระบบรองรับ — ตอบให้ถูกชนิดเพื่อไม่ให้ UI ค้าง
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// กลุ่มนี้ผูกกับระบบที่เซิร์ฟยังไม่มี (ตู้ขนส่งข้ามเกาะ · กรงสัตว์ · สมาคม · ที่ดิน · แคลน)
    /// หลักการตอบ:
    ///   • ตัวที่ client รอ reply ชนิดใดชนิดหนึ่ง → ตอบ "ชุดว่างที่ถูกชนิด" เพื่อให้หน้าจอปิดตัวเอง
    ///   • ตัวที่ client ใช้ <c>.All(Packet.IsSuccess)</c> → ตอบ Abort เพื่อให้ onResult(false) ทำงาน
    ///   • ตัวที่ยิงทิ้งไม่รอ reply แต่ผู้เล่นเป็นคนกดปุ่ม → ตอบ Abort พร้อมข้อความ
    ///     (client/GameManager.cs:303-306 DefaultAbortHandler เอา Text ไปขึ้นเป็น system message)
    ///   • ตัวที่ยิงทิ้งแบบอัตโนมัติ → เงียบ ๆ พอ ไม่รบกวนผู้เล่น
    /// </summary>
    private void RegisterUnimplementedItemHandlers()
    {
        // ── ตู้ขนส่งข้ามเกาะ (Cargo Warphole) ──────────────────────────────────────────
        // client/CargoWarpholeSystem.cs:38-47 → callback เดียวคือ OnResult(CargoReceivers)
        // client/Durango.UI/CargoWarpholeGroup.cs:132-152 ถ้าไม่มี receiver จะ ForceClose() ให้เอง
        // ⇒ ตอบ null ทั้งคู่ = หน้าต่างปิดตัวเองเรียบร้อย ดีกว่าปล่อยค้าง
        _connection.Recv(delegate(GetCargoReceivers msg, PacketHeader header)
        {
            Send(new CargoReceivers
            {
                PrivateReceiver = null,
                ClanReceiver = null,
                CostPerSize = 0
            }, header.Seq);
        });
        // client/CargoWarpholeSystem.cs:50-59 → OnReceivedItems ต้องได้ชุดว่าง ไม่งั้นหน้าโหลดค้าง
        _connection.Recv(delegate(GetReceivedItems msg, PacketHeader header)
        {
            Send(new ReceivedItems
            {
                ReceivingItems = Array.Empty<ReceivingItem>(),
                _ReceivedItems = Array.Empty<Item>(),
                UsingSize = 0,
                MaxSize = 0
            }, header.Seq);
        });
        // client/CargoWarpholeSystem.cs:17-35 รอ CargoReceiver — ไม่มีระบบส่งของข้ามเกาะ
        // ⚠️ ห้ามตอบ CargoReceiver ปลอม เพราะของที่ส่งไปจะหายจริง ๆ (ไม่มีที่เก็บปลายทาง)
        _connection.Recv(delegate(SendCargo msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่มีระบบส่งของข้ามเกาะ" }, header.Seq);
        });
        // ยิงทิ้งอัตโนมัติตอนเปิดหน้าต่าง — เงียบไว้
        _connection.Recv(delegate(ActivateCargoReceiver msg, PacketHeader header) { });
        // ผู้เล่นกดปุ่มยึดครองเอง (client/Durango.UI/CargoWarpholeGroup.cs:125-132 ไม่รอ reply)
        _connection.Recv(delegate(OccupyCargoWarphole msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่มีระบบยึดครองตู้ขนส่ง" });
        });
        // ตั้งค่าภาษี/โอนเข้ากองทุนแคลน — ยังไม่มีระบบแคลนและเงิน
        _connection.Recv(delegate(SetCargoWarpholeTaxRate msg, PacketHeader header) { });
        // client/EstateSystem.cs:620-627 รอ ClanCargoWarphole — ไม่มีแคลน จึงตอบ Abort ให้เลิกรอ
        _connection.Recv(delegate(CargoWarpholeTaxToClanFund msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่มีระบบแคลน" }, header.Seq);
        });
        // ── กรงสัตว์ ────────────────────────────────────────────────────────────────────
        // client/PetManager.cs:890-905 และ :1117-1132 ใช้ .All(Packet.IsSuccess) ⇒ Abort = onResult(false)
        _connection.Recv(delegate(TakeOutFromCage msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่มีระบบกรงสัตว์" }, header.Seq);
        });
        _connection.Recv(delegate(TakeOutReinFromCage msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่มีระบบกรงสัตว์" }, header.Seq);
        });
        // ── ที่ดิน/สมาคม ────────────────────────────────────────────────────────────────
        // client/EstateSystem.cs:678-692 .All(Packet.IsSuccess) — ยังไม่มีระบบแต้มบุกเบิก
        // ⚠️ ห้ามตอบ OK เด็ดขาด เพราะ client จะถือว่าไอเทมถูกใช้ไปแล้ว
        _connection.Recv(delegate(UseItemsForPioneerPoint msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่มีระบบแต้มบุกเบิก" }, header.Seq);
        });
        // client/FactionSystem.cs:397-406 ยิงทิ้งไม่รอ reply — ผู้เล่นเป็นคนกดส่งของให้สมาคม
        _connection.Recv(delegate(DeliverItems msg, PacketHeader header)
        {
            Console.WriteLine($"[item] DeliverItems ({msg.FactionType}) {msg.ItemIds?.Length ?? 0} ชิ้น — ยังไม่มีระบบสมาคม");
            Send(new Abort { Text = "ยังไม่มีระบบสมาคม" });
        });
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  คลังของสิ่งปลูกสร้าง (ใช้ร่วมกันทุกผู้เล่นในโปรเซสเดียว)
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ที่เก็บของของ "คลังสินค้า" และ "ตู้ในสิ่งปลูกสร้าง"
    ///
    /// ⚠️ **อยู่ในหน่วยความจำเท่านั้น — รีสตาร์ตเซิร์ฟแล้วของในคลังหาย**
    /// ไม่ใช่เพราะขี้เกียจ: ไฟล์เซฟของโลกคือ Core/WorldContext.cs ซึ่งไม่มีช่องเก็บไอเทมของ
    /// สิ่งปลูกสร้างเลย (มีแค่ Artifacts / ArtifactAddOns / ArtifactMannequins) และ
    /// ArtifactState.Inventory ก็เก็บได้แค่ StorableTags/UnstorableTags ไม่ใช่ตัวไอเทม
    /// ⇒ ต้องเพิ่มฟิลด์ใน WorldContext ก่อน ซึ่งอยู่นอกขอบเขตไฟล์งานนี้
    /// **ก่อนเปิดให้ผู้เล่นใช้จริงต้องต่อ persistence ก่อน** ไม่งั้นของหายตอนรีสตาร์ต
    ///
    /// ไม่ต้องล็อกเธรด เพราะ handler ทั้งหมดถูกเรียกจาก main loop เส้นเดียว
    /// (Program.cs:184 host.Process() → World.Process → Player.Process → Connection.Process)
    /// </summary>
    public static class WarehouseStore
    {
        /// <summary>ชื่อแท็บที่ใช้แทน "ตู้ที่ไม่มีแท็บ" (ตู้ในสิ่งปลูกสร้างมีช่องเดียว ไม่มีระบบแท็บ)</summary>
        public const string ContainerSection = "";

        private sealed class Store
        {
            public readonly List<string> Order = new();
            public readonly Dictionary<string, List<Item>> Sections = new();
        }

        private static readonly Dictionary<string, Store> Stores = new();

        private static Store Of(string entityId, bool create)
        {
            if (string.IsNullOrEmpty(entityId)) return null;
            if (Stores.TryGetValue(entityId, out Store store)) return store;
            if (!create) return null;
            store = new Store();
            Stores[entityId] = store;
            return store;
        }

        /// <summary>ของในแท็บหนึ่ง — คืน null ถ้ายังไม่มีแท็บนั้นและไม่ได้สั่งให้สร้าง</summary>
        public static List<Item> Items(string entityId, string section, bool create)
        {
            Store store = Of(entityId, create);
            if (store == null) return null;
            section ??= ContainerSection;
            if (store.Sections.TryGetValue(section, out List<Item> items)) return items;
            if (!create) return null;
            items = new List<Item>();
            store.Sections[section] = items;
            store.Order.Add(section);
            return items;
        }

        public static IReadOnlyList<string> SectionNames(string entityId)
        {
            Store store = Of(entityId, create: false);
            return store == null ? Array.Empty<string>() : store.Order;
        }

        /// <summary>ที่ใช้ไปในแท็บ = ผลรวม Item.Size (client คิดแบบเดียวกันที่ Inventory.CurrentSize)</summary>
        public static int UsedSize(string entityId, string section)
        {
            List<Item> items = Items(entityId, section, create: false);
            if (items == null) return 0;
            int sum = 0;
            foreach (Item item in items) sum += Math.Max(1, item.Size);
            return sum;
        }

        public static bool MakeSection(string entityId, string section)
        {
            if (string.IsNullOrEmpty(section)) return false;
            Store store = Of(entityId, create: true);
            if (store == null || store.Sections.ContainsKey(section)) return false;
            store.Sections[section] = new List<Item>();
            store.Order.Add(section);
            return true;
        }

        /// <summary>สร้างแท็บแรกให้อัตโนมัติถ้าคลังใบนี้ยังไม่เคยถูกเปิด</summary>
        public static void EnsureDefaultSection(string entityId, string defaultName)
        {
            Store store = Of(entityId, create: true);
            if (store != null && store.Order.Count == 0) MakeSection(entityId, defaultName);
        }

        public static void SetItemOrder(string entityId, string section, string[] order)
        {
            ReorderInPlace(Items(entityId, section, create: false), order);
        }

        // ── เซฟลงไฟล์โลก ────────────────────────────────────────────────────────────
        // ตู้/คลังเป็น "ของที่ผูกกับสิ่งปลูกสร้าง" ⇒ ต้องอยู่ในไฟล์ .world ของเกาะที่มันตั้งอยู่
        // ไม่ใช่ไฟล์ผู้เล่น (คนอื่นเปิดตู้เดียวกันต้องเห็นของชุดเดียวกัน)
        //
        // แยกว่าใบไหนอยู่เกาะไหนด้วย WorldContext.Artifacts.Keys — เป็นรายชื่อ entity ของ
        // สิ่งปลูกสร้างในเกาะนั้นอยู่แล้ว ⇒ ไม่ต้องจำ mapping เพิ่ม และเกาะหนึ่งเซฟทับของอีกเกาะไม่ได้

        /// <summary>รูปที่เขียนลงไฟล์ — ไม่ใช้ Store ตรง ๆ เพราะฟิลด์เป็น readonly (Newtonsoft เซ็ตไม่ได้)</summary>
        public sealed class Box
        {
            [JsonProperty("order")] public List<string> Order { get; set; }
            [JsonProperty("sections")] public Dictionary<string, List<Item>> Sections { get; set; }
        }

        /// <summary>ดึงเฉพาะคลังของ entity ที่อยู่ในเกาะนี้ — คืน null ถ้าไม่มีใบไหนมีของเลย (ไม่ต้องเปลืองที่ในไฟล์)</summary>
        [CanBeNull]
        public static Dictionary<string, Box> Export(IEnumerable<string> entityIds)
        {
            if (entityIds == null) return null;
            Dictionary<string, Box> result = null;
            foreach (string id in entityIds)
            {
                if (id == null || !Stores.TryGetValue(id, out Store store)) continue;
                if (store.Order.Count == 0) continue;
                result ??= new Dictionary<string, Box>();
                result[id] = new Box
                {
                    Order = new List<string>(store.Order),
                    Sections = new Dictionary<string, List<Item>>(store.Sections)
                };
            }
            return result;
        }

        /// <summary>โหลดกลับตอนเปิดเกาะ — ทับของเดิมในหน่วยความจำ (เกาะเพิ่งโหลด ยังไม่มีใครแตะ)</summary>
        public static void Import([CanBeNull] Dictionary<string, Box> data)
        {
            if (data == null) return;
            foreach (KeyValuePair<string, Box> pair in data)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value?.Sections == null) continue;
                Store store = Of(pair.Key, create: true);
                store.Order.Clear();
                store.Sections.Clear();
                foreach (string section in pair.Value.Order ?? new List<string>())
                {
                    if (!pair.Value.Sections.TryGetValue(section, out List<Item> items)) continue;
                    // ⚠️ ของในตู้ก็ผ่าน JSON มาเหมือนกระเป๋าผู้เล่น ⇒ Item.Ext เป็น JObject
                    // ไม่ซ่อมก่อน = แพ็กเก็ตตู้ทั้งใบเลื่อนช่อง (เหตุผลเต็มที่ ItemExtRepair)
                    ItemExtRepair.Normalize(items, $"ตู้ {pair.Key[..Math.Min(8, pair.Key.Length)]}");
                    store.Sections[section] = items ?? new List<Item>();
                    store.Order.Add(section);
                }
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ค่าคงที่จากไฟล์ data ที่คลาส Constants ฝั่งเซิร์ฟยังไม่ได้พอร์ตมา
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ค่าจาก data/assets/constants.json และ performance.json ที่ระบบของ/กระเป๋าต้องใช้
    ///
    /// ทำไมอ่านเอง: Support/YamlConstants.cs พอร์ต Constants มาแค่บางส่วน (PersonalRegion,
    /// Market, Resistance, Energy) ยังไม่มี warehouse/repair — และไฟล์นั้นอยู่นอกขอบเขตงานนี้
    /// ⇒ อ่านคีย์ที่ต้องใช้จากไฟล์เดิมโดยตรง ค่าเดียวกันเป๊ะ ไม่ได้ตั้งเลขเอง
    /// </summary>
    private static class ItemConstants
    {
        // ประกาศเป็น property ไม่ใช่ field เพื่อไม่ให้ CS0649 เตือน (Newtonsoft เป็นคนเซ็ตค่าให้)
        private class Root
        {
            [JsonProperty("warehouse")] public WarehouseNode Warehouse { get; set; }
            [JsonProperty("repair")] public RepairNode Repair { get; set; }
        }

        private class WarehouseNode
        {
            [JsonProperty("section_size")] public int SectionSize { get; set; }
        }

        private class RepairNode
        {
            [JsonProperty("item")] public RepairItemNode Item { get; set; }
        }

        private class RepairItemNode
        {
            [JsonProperty("time")] public float Time { get; set; }
        }

        private class PerformanceRoot
        {
            [JsonProperty("inventory")]
            public Dictionary<string, Dictionary<string, InventoryPerf>> Inventory { get; set; }
        }

        private class InventoryPerf
        {
            [JsonProperty("size_limit")] public string SizeLimit { get; set; }
        }

        private static Root _root;
        private static PerformanceRoot _perf;

        private static Root Data => _root ??= Json.ReadFromFile<Root>("constants") ?? new Root();

        private static PerformanceRoot Perf => _perf ??= Json.ReadFromFile<PerformanceRoot>("performance")
                                                        ?? new PerformanceRoot();

        /// <summary>constants.json → warehouse → section_size (ของจริง = 200)</summary>
        public static int WarehouseSectionSize => Data.Warehouse?.SectionSize > 0 ? Data.Warehouse.SectionSize : 0;

        /// <summary>constants.json → repair → item → time (ของจริง = 3.0 วินาที)</summary>
        public static float RepairItemTime => Data.Repair?.Item?.Time > 0f ? Data.Repair.Item.Time : 0f;

        /// <summary>performance.json → inventory → &lt;prototype&gt; → size_limit (สูตรตามเลเวลของตู้)</summary>
        public static float ContainerSizeLimit(string prototypeId, int level)
        {
            if (string.IsNullOrEmpty(prototypeId) || Perf.Inventory == null) return 0f;
            if (!Perf.Inventory.TryGetValue(prototypeId, out Dictionary<string, InventoryPerf> byRange)) return 0f;
            InventoryPerf perf = LevelRange.Pick(byRange, level);
            return perf != null && Formula.TryEval(perf.SizeLimit, level, out float v) ? v : 0f;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ตารางอาหารจริง (performance.json → "food")
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ค่าของอาหารแต่ละชนิดจาก data/assets/performance.json → "food"
    ///
    /// รูปแบบในไฟล์: <c>{ prototypeId: { "[minLv, maxLv]": { life, health, fatigue,
    /// energy_potential, eat_motion, ... } } }</c> — ค่าเป็นสูตรข้อความ เช่น
    /// "20 + 0.50 * (level -1)" หรือ "(16 + 0.3 * level) * -1" ⇒ ต้องคิดเลขเอง (ดู Formula)
    ///
    /// ฟิลด์ที่ **ยังไม่ได้ใช้** และเหตุผล: satiety/water (ยังไม่มีหลอดความอิ่ม/น้ำในเซิร์ฟนี้) ·
    /// energy_expression / energy_per_sec / digestivetime (ระบบย่อยตามเวลา — ความหมายยืนยัน
    /// จากซอร์สเกมไม่ได้ เพราะ client ไม่เคยอ่านฟิลด์พวกนี้เลย) · effect_on/modifier_effect_time
    /// (สถานะบัฟจากอาหาร — ต้องมีระบบ status effect แบบมีอายุก่อน) · *_plus (ค่าความสามารถ)
    /// </summary>
    private static class FoodTable
    {
        public sealed class Effect
        {
            public float Life;
            public float Health;
            public float Fatigue;
            public float EnergyPotential;
            public string EatMotion;
        }

        private class Root
        {
            [JsonProperty("food")] public Dictionary<string, Dictionary<string, Def>> Food { get; set; }
        }

        private class Def
        {
            [JsonProperty("life")] public string Life { get; set; }
            [JsonProperty("health")] public string Health { get; set; }
            [JsonProperty("fatigue")] public string Fatigue { get; set; }
            [JsonProperty("energy_potential")] public string EnergyPotential { get; set; }
            [JsonProperty("eat_motion")] public string EatMotion { get; set; }
        }

        private static Root _root;

        private static Root Data => _root ??= Json.ReadFromFile<Root>("performance") ?? new Root();

        /// <summary>คืน null ถ้าไอเทมนี้ไม่ใช่ของกิน (ไม่มีใน performance.food)</summary>
        public static Effect Get(string prototypeId, int level)
        {
            if (string.IsNullOrEmpty(prototypeId) || Data.Food == null) return null;
            if (!Data.Food.TryGetValue(prototypeId, out Dictionary<string, Def> byRange)) return null;
            Def def = LevelRange.Pick(byRange, level);
            if (def == null) return null;
            return new Effect
            {
                Life = Eval(def.Life, level),
                Health = Eval(def.Health, level),
                Fatigue = Eval(def.Fatigue, level),
                EnergyPotential = Eval(def.EnergyPotential, level),
                // ท่าทางในไฟล์คือ "Eat"/"Drink" ตรงกับชื่อ motion ที่ client ใช้
                EatMotion = string.IsNullOrEmpty(def.EatMotion) ? "Eat" : def.EatMotion
            };
        }

        private static float Eval(string expr, int level)
        {
            if (string.IsNullOrEmpty(expr)) return 0f;
            if (Formula.TryEval(expr, level, out float v)) return v;
            // คิดไม่ออก = ไม่ให้ผล ดีกว่าเดาเลข — และต้องเห็นใน log ว่ามีสูตรแบบไหนที่ยังไม่รองรับ
            Console.WriteLine($"[item] คิดสูตรอาหารไม่ได้: \"{expr}\" — ข้ามค่านี้ไป");
            return 0f;
        }
    }

    /// <summary>
    /// คีย์ช่วงเลเวลของ performance.json มีรูปแบบ "[minLv, maxLv]" (เช่น "[1, 70]")
    /// ⇒ เลือกช่วงที่ครอบเลเวลของไอเทม · ไม่เจอช่วงไหนเลยก็ใช้อันแรกไปก่อน
    /// </summary>
    private static class LevelRange
    {
        public static T Pick<T>(Dictionary<string, T> byRange, int level) where T : class
        {
            if (byRange == null || byRange.Count == 0) return null;
            T fallback = null;
            foreach (var pair in byRange)
            {
                fallback ??= pair.Value;
                string[] parts = pair.Key.Trim('[', ']', ' ').Split(',');
                if (parts.Length != 2) continue;
                if (int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int min) &&
                    int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) &&
                    min <= level && level <= max)
                {
                    return pair.Value;
                }
            }
            return fallback;
        }
    }

    /// <summary>
    /// ตัวคิดสูตรเลขแบบจำกัด สำหรับสูตรที่อยู่ในไฟล์ data
    ///
    /// ทำไมต้องมี: ค่าใน performance.json ไม่ใช่ตัวเลขล้วน แต่เป็นสูตรตามเลเวล เช่น
    /// "20 + 0.50 * (level -1)" · "(16 + 0.3 * level) * -1" · "75 + int(level * 0.35)"
    /// ⇒ ถ้าไม่คิดสูตร ก็ต้องเดาตัวเลข ซึ่งผิดกฎโปรเจกต์
    ///
    /// รองรับเท่าที่ไฟล์ใช้จริงเท่านั้น: ตัวเลข · ตัวแปร level · + - * / · วงเล็บ · int(...)
    /// เจออย่างอื่น (pow, max, min, **) คืน false ให้ผู้เรียกตัดสินใจเอง ไม่ใช่เดาค่าให้
    /// </summary>
    private static class Formula
    {
        public static bool TryEval(string expr, float level, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(expr)) return false;
            int pos = 0;
            try
            {
                double result = Additive(expr, ref pos, level);
                Skip(expr, ref pos);
                if (pos != expr.Length) return false;
                if (double.IsNaN(result) || double.IsInfinity(result)) return false;
                value = (float)result;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static double Additive(string s, ref int i, float level)
        {
            double left = Multiplicative(s, ref i, level);
            while (true)
            {
                Skip(s, ref i);
                if (i >= s.Length || (s[i] != '+' && s[i] != '-')) return left;
                char op = s[i++];
                double right = Multiplicative(s, ref i, level);
                left = op == '+' ? left + right : left - right;
            }
        }

        private static double Multiplicative(string s, ref int i, float level)
        {
            double left = Unary(s, ref i, level);
            while (true)
            {
                Skip(s, ref i);
                // '**' ของ python (เจอในสูตร repair) ไม่รองรับ — ต้องไม่ไปกิน '*' ตัวแรกแล้วคิดผิด
                if (i + 1 < s.Length && s[i] == '*' && s[i + 1] == '*') throw new FormatException();
                if (i >= s.Length || (s[i] != '*' && s[i] != '/')) return left;
                char op = s[i++];
                double right = Unary(s, ref i, level);
                left = op == '*' ? left * right : left / right;
            }
        }

        private static double Unary(string s, ref int i, float level)
        {
            Skip(s, ref i);
            if (i < s.Length && s[i] == '-')
            {
                i++;
                return -Unary(s, ref i, level);
            }
            if (i < s.Length && s[i] == '+')
            {
                i++;
                return Unary(s, ref i, level);
            }
            return Primary(s, ref i, level);
        }

        private static double Primary(string s, ref int i, float level)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException();
            if (s[i] == '(')
            {
                i++;
                double inner = Additive(s, ref i, level);
                Skip(s, ref i);
                if (i >= s.Length || s[i] != ')') throw new FormatException();
                i++;
                return inner;
            }
            if (char.IsLetter(s[i]))
            {
                int start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                string name = s.Substring(start, i - start);
                Skip(s, ref i);
                if (i < s.Length && s[i] == '(')
                {
                    i++;
                    double arg = Additive(s, ref i, level);
                    Skip(s, ref i);
                    if (i >= s.Length || s[i] != ')') throw new FormatException();
                    i++;
                    // int() ของ python ตัดเศษเข้าหาศูนย์
                    if (name == "int") return Math.Truncate(arg);
                    throw new FormatException();
                }
                if (name == "level") return level;
                throw new FormatException();
            }
            int numStart = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (i == numStart) throw new FormatException();
            if (!double.TryParse(s.Substring(numStart, i - numStart), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double number))
            {
                throw new FormatException();
            }
            return number;
        }
    }
}
