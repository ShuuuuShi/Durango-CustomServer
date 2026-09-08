using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Online;
using Messages;
using Shared.Building;
using Shared.Region;
using Yaml;

namespace DurangoServerNx;

/// <summary>
/// Offline assertions for the farm harvest loop (plant → mature → Collect → items).
/// Run: <c>DurangoServer --farm-check [--data &lt;dataDir&gt;]</c>
/// </summary>
internal static class FarmHarvestCheck
{
    static int _failed;
    static int _passed;

    public static int Run(string dataDir)
    {
        _failed = 0;
        _passed = 0;
        Durango.Utils.Json.DataDir = dataDir;

        // PrototypeYaml เป็น SingletonDict ที่ DataStore.Load เติมตอนบูตเซิร์ฟ
        // --farm-check ไม่เปิดเซิร์ฟ ⇒ ต้องโหลดตารางไอเทมเอง ไม่งั้น ProductPrototypes คืนลิสต์ว่าง
        var prototypes = Durango.Utils.Json.ReadFromFile<Dictionary<string, List<Prototype>>>("item/prototype_data");
        new PrototypeYaml().Initialize(prototypes);
        Expect(prototypes != null && prototypes.ContainsKey("corn_seed"),
            "โหลด prototype_data.json มี corn_seed");
        Expect(prototypes != null && prototypes.ContainsKey("corn_infertility"),
            "โหลด prototype_data.json มี corn_infertility");

        CheckCornSeedAsset();
        CheckProductMapping();
        CheckMaturePredicate();
        CheckCollectibleForClient();
        CheckMakeItemForHarvestProducts();
        CheckArtifactPlantGrowHarvest();

        Console.WriteLine($"[farm-check] ผ่าน {_passed} · ตก {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    static void CheckCornSeedAsset()
    {
        Crop corn = CropYaml.Get("corn_seed");
        Expect(corn != null, "โหลด corn_seed จาก crops.json");
        Expect(string.Equals(corn?.GrowsTo, "corn_crop", StringComparison.Ordinal),
            "corn_seed.grows_to = corn_crop");
        Expect(corn?.GrownLooks is { Length: > 0 }, "corn_seed มี grown_looks");
        Expect(!string.IsNullOrEmpty(corn?.GrowingLook), "corn_seed มี look.growing");
    }

    static void CheckProductMapping()
    {
        Crop corn = CropYaml.Get("corn_seed");
        string[] cornProducts = FarmHarvest.ProductPrototypes("corn_seed", corn);
        Expect(cornProducts.Contains("corn_infertility"),
            "เก็บข้าวโพดได้ corn_infertility (อาหาร — grows_to ไม่ใช่ prototype)");
        Expect(cornProducts.Contains("corn_seed"),
            "เก็บข้าวโพดได้ corn_seed คืน (ปลูกต่อได้)");

        Crop pumpkin = CropYaml.Get("pumpkin_seed");
        string[] pumpkinProducts = FarmHarvest.ProductPrototypes("pumpkin_seed", pumpkin);
        Expect(pumpkinProducts.Contains("pumpkin_seed"),
            "ฟักทองไม่มี *_infertility — ได้เมล็ดที่ปลูกคืนเป็นผลผลิต");
        Expect(!pumpkinProducts.Contains("pumpkin_infertility"),
            "ไม่มี prototype pumpkin_infertility ก็ไม่เสกชื่อปลอม");

        Expect(FarmHarvest.ProductPrototypes("corn_seed", null).Length == 0,
            "crop ว่าง ⇒ ไม่มีผลผลิต");

        foreach (string seed in new[] { "onion_seed", "potato_seed", "flax_seed", "wheat_seed" })
        {
            Crop crop = CropYaml.Get(seed);
            string[] products = FarmHarvest.ProductPrototypes(seed, crop);
            Expect(products.Length > 0, $"{seed} มีผลผลิตอย่างน้อย 1 ชิ้น ({string.Join(",", products)})");
        }
    }

    static void CheckMaturePredicate()
    {
        var growing = new Farming { GrowsUntil = 1_000 };
        Expect(!FarmHarvest.IsMature(growing, 999), "ยังไม่ถึง GrowsUntil = ยังไม่โต");
        Expect(FarmHarvest.IsMature(growing, 1_000), "ถึง GrowsUntil พอดี = เก็บได้");
        Expect(FarmHarvest.IsMature(growing, 1_001), "เลย GrowsUntil = เก็บได้");
        Expect(!FarmHarvest.IsMature(new Farming { GrowsUntil = 0 }, 9_999),
            "GrowsUntil=0 ไม่ถือว่าโต (ยังไม่ปลูกจริง)");
    }

    static void CheckCollectibleForClient()
    {
        Crop corn = CropYaml.Get("corn_seed");
        Collectible msg = FarmHarvest.BuildCollectible("farm-1", "corn_seed", corn);
        Expect(string.Equals(msg.EntityId, "farm-1", StringComparison.Ordinal),
            "Collectible.EntityId เป็น id แปลง");
        Expect(string.Equals(msg.CollectibleId, "corn_crop", StringComparison.Ordinal),
            "CollectibleId = grows_to (corn_crop)");
        Expect(msg.Generators is { Length: 1 }, "เก็บเกี่ยวแปลง = generator เดียว (กดครั้งเดียวได้ของครบ)");
        if (msg.Generators is { Length: > 0 })
        {
            Generator gen = msg.Generators[0];
            Expect(string.Equals(gen.Id, FarmHarvest.GeneratorId(corn), StringComparison.Ordinal),
                "Generator.Id = grows_to ให้ Collect.GeneratorId ตรงกัน");
            Expect(gen.Enabled, "ปุ่มเก็บ Enabled");
            Expect(gen.ToolRequirements != null && gen.ToolRequirements.ContainsKey(CollectibleTable.BareHands),
                "เก็บเกี่ยวใช้มือเปล่าได้");
            Expect(gen.Duration > 0f, "มี Duration ให้ฝั่งเกมเดินหลอด Collect");
        }
    }

    static void CheckMakeItemForHarvestProducts()
    {
        Crop corn = CropYaml.Get("corn_seed");
        foreach (string proto in FarmHarvest.ProductPrototypes("corn_seed", corn))
        {
            Item? item = Cheats.MakeItem(proto, 1);
            Expect(item.HasValue, $"Cheats.MakeItem('{proto}') สำเร็จ — ของเข้ากระเป๋าได้จริง");
        }
    }

    static void CheckArtifactPlantGrowHarvest()
    {
        var artifacts = new Dictionary<string, AppearArtifact>();
        var plantings = new Dictionary<string, string>();
        var mgr = new ArtifactManager(artifacts, new Dictionary<string, AddOns>(),
            new Dictionary<string, Mannequin>(), plantings);

        const string id = "farm-harvest-check";
        artifacts[id] = new AppearArtifact
        {
            EntityId = id,
            EntityType = 6254,
            Tile = new Point2(4, 4),
            Size = new Point2(1, 1),
            Display = new ArtifactDisplay { EntityId = id, Parts = new Dictionary<string, string>() },
            States = new ArtifactState { EntityId = id, BuildingState = BuildingState.Completed }
        };

        mgr.SeedPlant(id, "corn_seed", 1, Biome.Grassland);
        AppearArtifact? planted = mgr.Get(id);
        Expect(planted?.States.Farming.HasValue == true, "SeedPlant ตั้ง ArtifactState.Farming");
        Expect(string.Equals(mgr.PlantedSeed(id), "corn_seed", StringComparison.Ordinal),
            "จำ prototype เมล็ดไว้ใน plantings");
        Expect(!string.IsNullOrEmpty(planted?.Display.Crop), "โชว์โมเดลต้นบนแปลง");

        Farming farming = planted.Value.States.Farming.Value;
        double now = Gauge.CurrentTime;
        Expect(!FarmHarvest.IsMature(farming, now), "เพิ่งปลูก = ยังเก็บไม่ได้");
        Expect(!mgr.TryGetMatureCrop(id, now, out _, out _), "TryGetMatureCrop ปฏิเสธแปลงที่ยังไม่โต");

        double later = farming.GrowsUntil + 1.0;
        mgr.ProcessFarming(later);
        AppearArtifact? grown = mgr.Get(id);
        Crop corn = CropYaml.Get("corn_seed");
        string grownLook = corn.GrownLookAt(4, 4);
        Expect(string.Equals(grown?.Display.Crop, grownLook, StringComparison.Ordinal),
            "ProcessFarming สลับโมเดลเป็นต้นโต");

        Expect(mgr.TryGetMatureCrop(id, later, out string seed, out Crop crop),
            "โตแล้ว TryGetMatureCrop เจอเมล็ด+crop");
        Expect(string.Equals(seed, "corn_seed", StringComparison.Ordinal), "seed ที่เก็บ = corn_seed");
        Expect(string.Equals(crop?.GrowsTo, "corn_crop", StringComparison.Ordinal), "crop.grows_to ยังเป็น corn_crop");

        Expect(mgr.ClearFarming(id), "ClearFarming ล้างแปลงหลังเก็บ");
        AppearArtifact? empty = mgr.Get(id);
        Expect(empty?.States.Farming.HasValue != true, "ล้าง Farming แล้ว (ไม่ให้ ProcessFarming เสกต้นกลับ)");
        Expect(string.IsNullOrEmpty(empty?.Display.Crop), "ล้างโมเดลต้นบนแปลง");
        Expect(mgr.PlantedSeed(id) == null, "ล้าง plantings คู่กัน");
        Expect(!mgr.TryGetMatureCrop(id, later, out _, out _), "เก็บไปแล้วเก็บซ้ำไม่ได้");
    }

    static void Expect(bool cond, string title)
    {
        if (cond)
        {
            _passed++;
            Console.WriteLine($"[farm-check] ✓ {title}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"[farm-check] ❌ {title}");
        }
    }
}
