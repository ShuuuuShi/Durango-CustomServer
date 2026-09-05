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
        col = defaultColor;
        return false;
    }
}
