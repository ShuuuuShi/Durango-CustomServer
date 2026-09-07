using System;
using Shared.Region;

namespace Durango.Online;

/// <summary>
/// กติกาแหล่งบัพโลกที่เซิร์ฟใส่เอง — อากาศ + ยืนในน้ำ
/// แยกออกจาก <see cref="Player"/> เพื่อให้ตรวจ offline ได้โดยไม่ต้องเปิดโลก
/// </summary>
public static class WorldStatusRules
{
    /// <summary>ขนาดช่อง (หน่วยโลก) — ตรง <c>client/Durango.Terrain/Util.cs</c> SingleTileSize = 200</summary>
    public const float TileSize = 200f;

    /// <summary>
    /// บิตธงบนไบโอมใน terrain zip (collidable 0x80 + not-plantable 0x40)
    /// ตรง <c>client/Durango.Terrain/Util.cs</c> GetUnmaskedBiome ที่มาสก์ด้วย <c>&amp; -193</c>
    /// </summary>
    public const byte BiomeFlagMask = 0xC0;

    public static bool IsRainyWeather(string weather) =>
        string.Equals(weather, "rainy", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(weather, "heavy_rainy", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(weather, "climate_storm", StringComparison.OrdinalIgnoreCase);

    public static bool IsVolcanicStormWeather(string weather) =>
        string.Equals(weather, "volcanic_storm", StringComparison.OrdinalIgnoreCase);

    public static bool IsVolcanicSignWeather(string weather) =>
        string.Equals(weather, "volcanic_sign", StringComparison.OrdinalIgnoreCase);

    /// <summary>ไบโอมน้ำที่ฝั่งเกมถือว่ายืนในน้ำ — ตรง <c>Durango.Terrain.Util.IsWater</c></summary>
    public static bool IsWaterBiome(Biome biome) =>
        biome is Biome.ColdOcean or Biome.WarmOcean or Biome.River or Biome.Lake;

    public static Biome UnmaskBiome(byte masked)
    {
        int value = masked & ~BiomeFlagMask;
        if (value < 0 || value > (int)Biome.Lava) return Biome.Invalid;
        return (Biome)value;
    }

    /// <summary>ถอดธงจากค่าที่ <see cref="World.BiomeAt"/> แคสต์มาจากไบต์ดิบ</summary>
    public static Biome UnmaskBiome(Biome maybeMasked)
    {
        int raw = (int)maybeMasked;
        if (raw < 0) return Biome.Invalid;
        if (raw <= (int)Biome.Lava) return maybeMasked;
        return raw <= byte.MaxValue ? UnmaskBiome((byte)raw) : Biome.Invalid;
    }

    public static bool ShouldApplyWet(string weather, Biome biome) =>
        IsRainyWeather(weather) || IsWaterBiome(UnmaskBiome(biome));

    /// <summary>Until = 0 คือไม่มีอายุ · Until ยังมาไม่ถึง = ยังอยู่</summary>
    public static bool IsActiveTimed(double until, double now) =>
        until <= 0 || until > now;

    public static Point2 TileFromWorldPosition(float x, float y) =>
        new((int)(x / TileSize), (int)(y / TileSize));
}
