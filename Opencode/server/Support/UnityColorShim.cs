using System;

namespace UnityEngine;

// shim สีแบบย่อ — เซิร์ฟแท้ใช้ Color เฉพาะตอนสุ่ม/แปลงสีไอเทม (TryGetDefaultColor, ColorHSV)
// เรนเดอร์จริงเกิดฝั่ง client เสมอ (Random.Range อยู่ใน Shims/UnityEngineShims.cs ของโปรเจกต์เดิมแล้ว)
public struct Color
{
    public float r, g, b, a;

    public Color(float r, float g, float b, float a = 1f)
    {
        this.r = r; this.g = g; this.b = b; this.a = a;
    }

    public static Color white => new(1f, 1f, 1f, 1f);
    public static Color clear => new(0f, 0f, 0f, 0f);

    public static bool operator ==(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    public static bool operator !=(Color a, Color b) => !(a == b);
    public override bool Equals(object obj) => obj is Color c && this == c;
    public override int GetHashCode() => (r, g, b, a).GetHashCode();
}

public struct Color32
{
    public byte r, g, b, a;

    public Color32(byte r, byte g, byte b, byte a)
    {
        this.r = r; this.g = g; this.b = b; this.a = a;
    }

    public static implicit operator Color(Color32 c) => new(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
}

// แทน UnityEngine.Random.ColorHSV() (Random.Range มีใน shim เดิม แต่ ColorHSV ไม่มี)
public static class NxRandom
{
    public static Color ColorHSV()
    {
        double h = System.Random.Shared.NextDouble();
        double s = 0.75 + System.Random.Shared.NextDouble() * 0.25;
        double v = 1.0;
        int i = (int)(h * 6) % 6;
        double f = h * 6 - Math.Floor(h * 6);
        double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        return i switch
        {
            0 => new Color((float)v, (float)t, (float)p),
            1 => new Color((float)q, (float)v, (float)p),
            2 => new Color((float)p, (float)v, (float)t),
            3 => new Color((float)p, (float)q, (float)v),
            4 => new Color((float)t, (float)p, (float)v),
            _ => new Color((float)v, (float)p, (float)q)
        };
    }
}

// พอร์ตย่อจาก NGUIText — เซิร์ฟใช้แค่ ParseColor24 ของ "#RRGGBB"
public static class NGUIText
{
    public static Color ParseColor24(string text, int offset = 0)
    {
        if (text.Length < offset + 6) return Color.white;
        int r = ParseHex(text, offset);
        int g = ParseHex(text, offset + 2);
        int b = ParseHex(text, offset + 4);
        return new Color(r / 255f, g / 255f, b / 255f, 1f);
    }

    private static int ParseHex(string text, int offset)
    {
        int v = 0;
        for (int i = 0; i < 2; i++)
        {
            char c = text[offset + i];
            v <<= 4;
            v += c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => 0
            };
        }
        return v;
    }
}
