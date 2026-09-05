using System;
using JetBrains.Annotations;

// เติมส่วน KUtility ที่ shim ของเซิร์ฟเดิมไม่มี: GetRandomHash (ใช้เลือกลุคธัญพืชตอนปลูก)
// ต้นฉบับใช้ XXHash จาก assembly อื่นที่ไม่ได้ถอดมา — ที่นี่ใช้ xxHash32 มาตรฐาน
// (ค่า hash ต่างจากต้นฉบับได้ ไม่กระทบ gameplay เพราะทุกลุคโตเท่ากัน)
public static class KUtilityNx
{
    public static int GetRandomHash(int x, int y) => (int)XxHash32.Compute(x, (uint)y, 1);

    public static int GetRandomHashRange(int min, int max, int key)
    {
        if (max <= min) return min;
        uint h = XxHash32.Compute(key, 0x9E3779B9u, 1);
        return min + (int)(h % (uint)(max - min));
    }
}

// xxHash32 มาตรฐาน (สเปกสาธารณะ)
public static class XxHash32
{
    private const uint Prime1 = 2654435761u;
    private const uint Prime2 = 2246822519u;
    private const uint Prime3 = 3266489917u;
    private const uint Prime4 = 668265263u;
    private const uint Prime5 = 374761393u;

    public static uint Compute(int value, uint extra, uint seed)
    {
        uint acc = seed + Prime5;
        acc += (uint)value * Prime3;
        acc = Rotl(acc, 17) * Prime4;
        acc += extra * Prime1;
        acc = Rotl(acc, 11) * Prime2;
        acc ^= acc >> 15;
        acc *= Prime3;
        acc ^= acc >> 13;
        acc *= Prime2;
        acc ^= acc >> 16;
        return acc;
    }

    [CanBeNull]
    private static uint Rotl(uint x, int r) => (x << r) | (x >> (32 - r));
}
