using System;
using Durango.Online;
using Shared.Region;

namespace DurangoServerNx;

/// <summary>
/// Offline assertions สำหรับบัพโลก (ฝน/น้ำ → wet) ที่ฝั่งเกมคาดว่าจะเห็นไอคอน
/// Run: <c>DurangoServer --se-check [--data &lt;dataDir&gt;]</c>
/// </summary>
internal static class StatusEffectWorldCheck
{
    static int _failed;
    static int _passed;

    public static int Run(string dataDir)
    {
        _failed = 0;
        _passed = 0;
        Durango.Utils.Json.DataDir = dataDir;

        CheckRainyWeatherNames();
        CheckWaterBiomes();
        CheckBiomeUnmask();
        CheckShouldApplyWet();
        CheckTimedActive();
        CheckTileFromWorld();
        CheckCatalogDurations();

        Console.WriteLine($"[se-check] ผ่าน {_passed} · ตก {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    static void CheckRainyWeatherNames()
    {
        Expect(WorldStatusRules.IsRainyWeather("rainy"), "rainy เป็นฝน");
        Expect(WorldStatusRules.IsRainyWeather("heavy_rainy"), "heavy_rainy เป็นฝน");
        Expect(WorldStatusRules.IsRainyWeather("climate_storm"), "climate_storm เป็นฝน");
        Expect(!WorldStatusRules.IsRainyWeather("sunny"), "sunny ไม่ใช่ฝน");
        Expect(!WorldStatusRules.IsRainyWeather("cloudy"), "cloudy ไม่ใช่ฝน");
        Expect(!WorldStatusRules.IsRainyWeather("snowy"), "snowy ไม่ใช่ฝน");
        Expect(!WorldStatusRules.IsRainyWeather("volcanic_ash"), "volcanic_ash ไม่ใช่ฝน");
        Expect(!WorldStatusRules.IsRainyWeather(null), "weather ว่างไม่ใช่ฝน");
        Expect(!WorldStatusRules.IsRainyWeather(""), "weather ว่างเปล่าไม่ใช่ฝน");
        Expect(WorldStatusRules.IsVolcanicStormWeather("volcanic_storm"), "volcanic_storm ตรงชื่อ");
        Expect(WorldStatusRules.IsVolcanicSignWeather("volcanic_sign"), "volcanic_sign ตรงชื่อ");
    }

    static void CheckWaterBiomes()
    {
        Expect(WorldStatusRules.IsWaterBiome(Biome.ColdOcean), "ColdOcean เป็นน้ำ");
        Expect(WorldStatusRules.IsWaterBiome(Biome.WarmOcean), "WarmOcean เป็นน้ำ");
        Expect(WorldStatusRules.IsWaterBiome(Biome.River), "River เป็นน้ำ");
        Expect(WorldStatusRules.IsWaterBiome(Biome.Lake), "Lake เป็นน้ำ");
        Expect(!WorldStatusRules.IsWaterBiome(Biome.SandBeach), "SandBeach ไม่ใช่น้ำ");
        Expect(!WorldStatusRules.IsWaterBiome(Biome.PebbleBeach), "PebbleBeach ไม่ใช่น้ำ");
        Expect(!WorldStatusRules.IsWaterBiome(Biome.TemperateForest), "ป่าไม่ใช่น้ำ");
        Expect(!WorldStatusRules.IsWaterBiome(Biome.SwampMud), "SwampMud ไม่ใช่น้ำตาม IsWater ของเกม");
        Expect(!WorldStatusRules.IsWaterBiome(Biome.Lava), "Lava ไม่ใช่น้ำ");
    }

    static void CheckBiomeUnmask()
    {
        Expect(WorldStatusRules.UnmaskBiome((byte)Biome.Lake) == Biome.Lake, "Lake ไม่มีธงคงเป็น Lake");
        Expect(WorldStatusRules.UnmaskBiome((byte)((int)Biome.Lake | 0x80)) == Biome.Lake,
            "Lake | collidable ถอดธงแล้วเป็น Lake");
        Expect(WorldStatusRules.UnmaskBiome((byte)((int)Biome.WarmOcean | 0x40)) == Biome.WarmOcean,
            "WarmOcean | not-plantable ถอดธงแล้วเป็น WarmOcean");
        Expect(WorldStatusRules.UnmaskBiome(Biome.Invalid) == Biome.Invalid, "Invalid คง Invalid");
        Expect(WorldStatusRules.UnmaskBiome((Biome)((int)Biome.River | 0x80)) == Biome.River,
            "Biome ที่แคสต์จากไบต์มีธง ถอดแล้วเป็น River");
    }

    static void CheckShouldApplyWet()
    {
        Expect(WorldStatusRules.ShouldApplyWet("rainy", Biome.TemperateForest), "ฝนบนบกต้องติด wet");
        Expect(WorldStatusRules.ShouldApplyWet("sunny", Biome.WarmOcean), "แดดแต่อยู่มหาสมุทรต้องติด wet");
        Expect(WorldStatusRules.ShouldApplyWet("sunny", (Biome)((int)Biome.Lake | 0x80)),
            "ยืนทะเลสาบที่มีธงต้องติด wet");
        Expect(!WorldStatusRules.ShouldApplyWet("sunny", Biome.SandBeach), "แดดบนหาดไม่ติด wet");
        Expect(!WorldStatusRules.ShouldApplyWet("cloudy", Biome.Grassland), "เมฆบนหญ้าไม่ติด wet");
    }

    static void CheckTimedActive()
    {
        const double now = 1_000_000d;
        Expect(WorldStatusRules.IsActiveTimed(0, now), "Until=0 คือไม่มีอายุ ยังอยู่");
        Expect(WorldStatusRules.IsActiveTimed(now + 10, now), "Until ในอนาคตยังอยู่");
        Expect(!WorldStatusRules.IsActiveTimed(now, now), "Until เท่า now หมดแล้ว");
        Expect(!WorldStatusRules.IsActiveTimed(now - 1, now), "Until ในอดีตหมดแล้ว");
    }

    static void CheckTileFromWorld()
    {
        Point2 tile = WorldStatusRules.TileFromWorldPosition(400f, 600f);
        Expect(tile.x == 2 && tile.y == 3, "โลก (400,600) → ช่อง (2,3)");
        Point2 origin = WorldStatusRules.TileFromWorldPosition(0f, 0f);
        Expect(origin.x == 0 && origin.y == 0, "โลก (0,0) → ช่อง (0,0)");
    }

    static void CheckCatalogDurations()
    {
        StatusEffectCatalog.Template wet = StatusEffectCatalog.Get("wet", 1);
        Expect(wet != null, "โหลด template wet จาก status_effects.json");
        Expect(wet != null && wet.DurationSeconds == 120, $"wet.duration=120 (ได้ {wet?.DurationSeconds})");
        Expect(WeatherTuning.CycleSeconds == 300, $"WeatherTuning.CycleSeconds=300 (ได้ {WeatherTuning.CycleSeconds})");
        Expect(wet != null && wet.DurationSeconds < WeatherTuning.CycleSeconds,
            "wet สั้นกว่าคาบอากาศ — ต้องต่ออายุระหว่างยังฝนตก ไม่งั้นไอคอนหายกลางฝน");
        Expect(Player.HasClearOnLevelUpTag(wet), "wet มี clear_on_levelup — เลเวลขึ้นแล้วต้องใส่คืนจากฝน/น้ำ");

        StatusEffectCatalog.Template clean = StatusEffectCatalog.Get("clean", 1);
        Expect(clean != null && clean.DurationSeconds == 360, $"clean.duration=360 (ได้ {clean?.DurationSeconds})");

        StatusEffectCatalog.Template dirty = StatusEffectCatalog.Get("dirty", 1);
        Expect(dirty != null, "โหลด template dirty");
        Expect(dirty != null && dirty.DurationSeconds == null, "dirty ไม่มี duration ใน JSON (ติดค้างจนกว่าจะล้าง)");

        StatusEffectCatalog.Template storm = StatusEffectCatalog.Get("volcanic_storm", 1);
        Expect(storm != null && storm.DurationSeconds == 300, $"volcanic_storm.duration=300 (ได้ {storm?.DurationSeconds})");
    }

    static void Expect(bool cond, string title)
    {
        if (cond)
        {
            _passed++;
            Console.WriteLine($"[se-check] ✓ {title}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"[se-check] ❌ {title}");
        }
    }
}
