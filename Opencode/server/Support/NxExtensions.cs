using System;
using System.Collections.Generic;

namespace Durango.Utils.Extensions;

// extension ของ AppearPlayer (พอร์ตจาก nexonSRC/AppearPlayerExtension.cs)
public static class AppearPlayerExtension
{
    public static bool IsMale(this Messages.AppearPlayer appear) => appear.EntityType == 1000;
}

// พอร์ตย่อจาก nexonSRC/Durango.Utils.Extensions — เฉพาะ extension ที่โค้ดเซิร์ฟแท้เรียกใช้
public static class NxExtensions
{
    /// <summary>"12" → 12 (พาร์สไม่ได้คืน 0) — เหมือน StringExtensions.ToInt ต้นฉบับ</summary>
    public static int ToInt(this string source)
    {
        return int.TryParse(source, out int v) ? v : 0;
    }

    /// <summary>dict.Get(key) — ไม่เจอคืน default (DictionaryExtensions.Get ต้นฉบับ)</summary>
    public static TValue Get<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default)
    {
        if (dict == null || key == null) return defaultValue;
        return dict.TryGetValue(key, out var v) ? v : defaultValue;
    }

    public static List<TValue> Get<TKey, TValue>(this Dictionary<TKey, List<TValue>> dict, TKey key)
    {
        return dict.Get<TKey, List<TValue>>(key);
    }

    /// <summary>Color → "RRGGBB" (ColorExtensions.ToHex ต้นฉบับ)</summary>
    public static string ToHex(this UnityEngine.Color c)
    {
        byte r = (byte)Math.Round(Math.Clamp(c.r, 0f, 1f) * 255f);
        byte g = (byte)Math.Round(Math.Clamp(c.g, 0f, 1f) * 255f);
        byte b = (byte)Math.Round(Math.Clamp(c.b, 0f, 1f) * 255f);
        return $"{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>string → enum (พาร์สไม่ได้คืนค่า default) — StringExtensions.ToEnum ต้นฉบับ</summary>
    public static T ToEnum<T>(this string source, T value = default) where T : struct, Enum
    {
        return Enum.TryParse(source, ignoreCase: true, out T v) ? v : value;
    }

    /// <summary>"warm_ocean" → "WarmOcean" (StringExtensions.ToCamelCase ต้นฉบับ)</summary>
    public static string ToCamelCase(this string source)
    {
        if (string.IsNullOrEmpty(source)) return source;
        var parts = source.Split('_', ' ');
        var sb = new System.Text.StringBuilder();
        foreach (string p in parts)
        {
            if (p.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(p[0]));
            if (p.Length > 1) sb.Append(p, 1, p.Length - 1);
        }
        return sb.ToString();
    }

    /// <summary>string → enum คืน bool บอกความสำเร็จ — StringExtensions.TryEnum ต้นฉบับ</summary>
    public static bool TryEnum<T>(this string source, out T value, bool showError = false) where T : struct, Enum
    {
        return Enum.TryParse(source, ignoreCase: true, out value);
    }
}
