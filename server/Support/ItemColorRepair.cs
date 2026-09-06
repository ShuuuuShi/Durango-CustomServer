using System;
using System.Collections.Generic;
using Durango.UI.Control;
using Durango.Utils.Extensions;   // Color.ToHex()
using JetBrains.Annotations;
using Messages;
using UnityEngine;
using Yaml;

namespace Durango.Online;

/// <summary>
/// ซ่อมสีของไอเทมที่ถูกเซฟไว้ตอนที่ตารางสียังอ่านไม่ได้
///
/// ═══ ทำไมต้องซ่อมย้อนหลัง ═══
/// ก่อน 7 ก.ย. 2026 <c>ItemIconTex.TryGetDefaultColor</c> คืน false ทุกครั้งที่คีย์สี
/// ไม่ได้ขึ้นต้นด้วย '#' (ซึ่งคือ prototype ส่วนใหญ่ — เขาเก็บเป็น "ชื่อชุดสี" เช่น color_wood)
/// แล้ว <c>Cheats.MakeItem</c> เขียนค่า default ลงไอเทมเลย ⇒ **ของทุกชิ้นถูกเซฟเป็นสีขาว**
///
/// พอแก้ตัวอ่านตารางแล้ว ของที่ทำใหม่จะมีสีถูก แต่ของเก่าในไฟล์เซฟยังขาวอยู่
/// ⇒ ตอนโหลดไฟล์ ให้คำนวณสีใหม่จาก prototype ให้เฉพาะชิ้นที่ยังขาว
///
/// ═══ เกณฑ์ว่าชิ้นไหนควรซ่อม ═══
/// ซ่อมเมื่อ **ครบทั้งสามเงื่อนไข** เท่านั้น เพื่อไม่ไปทับของที่ผู้เล่นย้อมสีเอง:
///   1. สีที่เก็บไว้ว่างเปล่า หรือเป็นสีขาวเต็ม (FFFFFFFF) ทั้งสามช่อง
///   2. หา prototype ของชิ้นนั้นเจอ
///   3. prototype ชี้ไปที่ชุดสีที่มีอยู่จริงในตาราง
/// ถ้า prototype ตั้งใจให้ขาวจริง ๆ (ชุดสีมีแต่สีขาว) ผลลัพธ์ก็ยังขาวเหมือนเดิม ไม่เสียหาย
/// </summary>
internal static class ItemColorRepair
{
    public static void Normalize([CanBeNull] List<Item> items, string where)
    {
        if (items == null || items.Count == 0) return;
        int repaired = 0;
        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            if (!TryRecolor(ref item)) continue;
            items[i] = item;
            repaired++;
        }
        if (repaired > 0)
        {
            Console.WriteLine($"[สี] ซ่อมสีไอเทมใน{where} {repaired} ชิ้น");
        }
    }

    /// <summary>คืน true ถ้าเปลี่ยนสีให้จริง</summary>
    private static bool TryRecolor(ref Item item)
    {
        if (!IsColorless(item.ColorR) || !IsColorless(item.ColorG) || !IsColorless(item.ColorB))
        {
            return false;
        }
        Prototype proto = PrototypeYaml.GetItemPrototype(item.Prototype, item.Level);
        if (proto == null) return false;

        // seed เดียวกับตอนสร้างของ (Core/Cheats.cs — value.Id.GetHashCode())
        // ⇒ ของชิ้นเดิมได้เฉดเดิมทุกครั้งที่โหลด ไม่เปลี่ยนไปมา
        int seed = item.Id?.GetHashCode() ?? 0;
        bool any = false;
        any |= TryChannel(proto.ColorR, seed, ref item.ColorR);
        any |= TryChannel(proto.ColorG, seed, ref item.ColorG);
        any |= TryChannel(proto.ColorB, seed, ref item.ColorB);
        return any;
    }

    private static bool TryChannel(string key, int seed, ref string stored)
    {
        if (!ItemIconTex.TryGetDefaultColor(key, out Color col, seed, Color.white)) return false;
        stored = col.ToHex();
        return true;
    }

    /// <summary>ว่าง หรือขาวเต็ม = ยังไม่เคยมีสีจริง</summary>
    private static bool IsColorless(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return true;
        return string.Equals(hex, "FFFFFFFF", StringComparison.OrdinalIgnoreCase)
            || string.Equals(hex, "FFFFFF", StringComparison.OrdinalIgnoreCase);
    }
}
