using Durango.Online;   // ColorTableStore — ตารางสีจาก data/assets/colortable.json
using UnityEngine;

namespace Durango.UI.Control;

// พอร์ตย่อจาก nexonSRC/Durango.UI.Control/ItemIconTex.cs (TryGetDefaultColor)
// ต้นฉบับ: key ขึ้นต้น '#' = hex ตรง ๆ นอกนั้นโหลดตารางสี .raw จาก resource ของเกม
// (ไฟล์ .raw ไม่มีใน server data ⇒ คืน false = client ใช้สี default ขาว เหมือนพฤติกรรมต้นฉบับตอนหาตารางไม่เจอ)
public static class ItemIconTex
{
    public static bool TryGetDefaultColor(string key, out Color col, int? seed = null, Color defaultColor = default)
    {
        if (string.IsNullOrEmpty(key))
        {
            col = defaultColor;
            return false;
        }
        if (key[0] == '#')
        {
            if (key.Length < 7)
            {
                col = defaultColor;
                return false;
            }
            col = NGUIText.ParseColor24(key, 1);
            return true;
        }
        // [7 ก.ย. 2026] คีย์ที่ไม่ใช่ hex = **ชื่อชุดสี** ให้ไปหยิบจากตารางจริง
        //
        // ⚠️ เดิมตรงนี้คืน false ทันที ⇒ Cheats.MakeItem เขียนสี default (ขาว) ลงไอเทมทุกชิ้น
        // ทั้งที่ prototype ส่วนใหญ่ใช้ชื่อชุดสี ("color_wood", "color_bamboo", …) ไม่ใช่ hex
        // อาการที่เห็น: ของในกระเป๋าเป็นสีขาวเกือบทั้งหมด
        //
        // ต้นฉบับ (nexonSRC/Durango.UI.Control/ItemIconTex.cs:262-269):
        //   ColorTable t = ColorTableLoader.Load($"{key}.raw");
        //   col = seed.HasValue ? t.GetRandom(seed.Value) : t.GetColor(0f);
        // และ GetRandom(hashKey) = _colors[KUtility.GetRandomHashRange(0, _colors.Length, hashKey)]
        //   (nexonSRC/Durango.Utils/ColorTable.cs:38-42)
        //   GetColor(0f) = _colors[0]  (:44-48 — (int)(len * 0) % len)
        // ⇒ ทำตามนั้นเป๊ะ ต่างแค่ที่มาของข้อมูล: อ่าน data/assets/colortable.json แทนไฟล์ .raw
        //   (ดูเหตุผลเต็มที่ Support/ColorTableStore.cs)
        Color[] palette = ColorTableStore.Get(key);
        if (palette == null)
        {
            col = defaultColor;
            return false;
        }
        col = seed.HasValue
            ? palette[KUtilityNx.GetRandomHashRange(0, palette.Length, seed.Value)]
            : palette[0];
        return true;
    }
}
