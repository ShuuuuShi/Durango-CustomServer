using System;
using System.Collections.Generic;
using Durango.Utils;
using UnityEngine;

namespace Durango.Online;

/// <summary>
/// ตารางสีของไอเทม — จาก <c>data/assets/colortable.json</c> (109 ชุดสี · ข้อมูลจริงของเกม)
///
/// ═══ ทำไมต้องมี ═══
/// prototype ของไอเทมไม่ได้เก็บสีเป็นรหัส hex แต่เก็บเป็น **ชื่อชุดสี**
/// (item/prototype_data.json → <c>"color_r": "color_wood"</c>) แล้วให้ไปสุ่มเอาจากชุดนั้น
/// เช่น <c>color_bamboo</c> มีเฉียดร้อยเฉดเขียว ⇒ ไม้ไผ่แต่ละชิ้นสีไม่เหมือนกันเป๊ะ
///
/// ต้นฉบับโหลดจากไฟล์ <c>Resources/ColorTable/&lt;ชื่อ&gt;.raw</c> ของตัวเกม
/// (nexonSRC/Durango.Utils/ColorTableLoader.cs) ซึ่งฝั่งเซิร์ฟไม่มี
/// ⇒ เดิม <see cref="ItemIconTex.TryGetDefaultColor"/> คืน false แล้ว <c>Cheats.MakeItem</c>
/// เขียนสี default (ขาว) ลงไอเทมทุกชิ้น — **นั่นคือสาเหตุที่ของในกระเป๋าขาวไปหมด**
///
/// แต่ข้อมูลชุดเดียวกันมีอยู่แล้วในรูป JSON ที่ <c>data/assets/colortable.json</c>
/// (คีย์ = ชื่อชุด · ค่า = อาเรย์ hex) ⇒ อ่านจากตรงนั้นแทน ได้ผลเท่าต้นฉบับโดยไม่ต้องมีไฟล์ .raw
/// </summary>
public static class ColorTableStore
{
    private static Dictionary<string, Color[]> _tables;

    /// <summary>
    /// ชุดสีของคีย์นี้ — คืน <c>null</c> ถ้าไม่มีในตาราง (ผู้เรียกต้องจัดการเอง)
    /// </summary>
    public static Color[] Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        Load();
        return _tables.TryGetValue(key, out Color[] palette) && palette.Length > 0 ? palette : null;
    }

    private static void Load()
    {
        if (_tables != null) return;
        _tables = new Dictionary<string, Color[]>();
        var raw = Json.ReadFromFile<Dictionary<string, string[]>>("colortable");
        if (raw == null)
        {
            Console.WriteLine("[สี] ไม่พบ colortable.json — ไอเทมจะได้สีเริ่มต้นทั้งหมด");
            return;
        }
        foreach (KeyValuePair<string, string[]> pair in raw)
        {
            if (pair.Value == null || pair.Value.Length == 0) continue;
            var list = new List<Color>(pair.Value.Length);
            foreach (string hex in pair.Value)
            {
                // ค่าในไฟล์เป็น "#RRGGBB" — ตัวแปลงเดียวกับที่ต้นฉบับใช้กับคีย์ที่ขึ้นต้น '#'
                if (!string.IsNullOrEmpty(hex) && hex.Length >= 7 && hex[0] == '#')
                {
                    list.Add(NGUIText.ParseColor24(hex, 1));
                }
            }
            if (list.Count > 0) _tables[pair.Key] = list.ToArray();
        }
        Console.WriteLine($"[สี] โหลดตารางสีไอเทม {_tables.Count} ชุด");
    }
}
