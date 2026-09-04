using System;
using System.Collections.Generic;
using Durango.Terrain;
using Durango.Utils;

namespace Yaml.Util;

// เทียบเท่ากับ Loader.CoLoadingYmls ฝั่ง client (nexonSRC/Yaml.Util/Loader.cs)
// client โหลด /assets/* จาก gateway ตอน Online หรือจาก Resources ตอน Offline — เซิร์ฟโหลดจากดิสก์ data/assets/
// เรียกครั้งเดียวตอนบูตก่อนเปิดรับ connection
public static class DataStore
{
    public static bool Loaded { get; private set; }

    public static Emotions Emotions { get; private set; }

    public static void Load(string dataDir)
    {
        Json.DataDir = dataDir;
        var counts = new (string name, int count)[10];

        // ลำดับ/ไฟล์ = ชุดย่อยของ Loader ต้นฉบับที่เซิร์ฟแท้ใช้จริง
        var prototypes = Json.ReadFromFile<Dictionary<string, List<Prototype>>>("item/prototype_data");
        new PrototypeYaml().Initialize(prototypes);

        var constants = Json.ReadFromFile<Constants>("constants");
        constants?.Initialize(constants);

        // ตารางเพดาน exp ต้านทาน — ใช้ตอนตอบ GetResistanceExpCaps (Player.cs)
        var playerStats = Json.ReadFromFile<PlayerStatistics>("statistics/player");
        playerStats?.Initialize(playerStats);

        // ค่าสมดุลของตัวผู้เล่น (หลอด life/stamina/fatigue/groggy/energy/health + online_momenta)
        // — SurvivalState เอาไปสร้างหลอดจริง ดู Core/SurvivalState.cs
        var playerTypes = Json.ReadFromFile<Dictionary<string, PlayerType>>("entity_types/players");
        new PlayerTypes().Initialize(playerTypes);

        var naturals = Json.ReadFromFile<Dictionary<int, Natural>>("entity_types/natural");
        if (naturals != null) DataHelper.Initialize(naturals);

        var artifacts = Json.ReadFromFile<Dictionary<int, ArtifactPrototype>>("entity_types/artifact");
        new ArtifactPrototypeDict().Initialize(artifacts);

        var pets = Json.ReadFromFile<Dictionary<int, Pet>>("pet/pets_for_client");
        new Pets().Initialize(pets);

        var stories = Json.ReadFromFile<Dictionary<string, Chapters>>("quests/epics_for_client");
        new StoryYaml().Initialize(stories);

        var recipes = Json.ReadFromFile<Dictionary<string, Recipe>>("item/recipes");
        new RecipeDict().Initialize(recipes);

        var blueprints = Json.ReadFromFile<Dictionary<string, Blueprint>>("building/blueprints");

        Emotions = Json.ReadFromFile<Emotions>("emotions");

        // merge blueprint×artifact (เทียบเท่า RecipeContainer ฝั่ง client)
        BlueprintStore.Initialize(artifacts, blueprints);

        Report("prototype_data", prototypes, counts, 0);
        Report("constants", new Dictionary<string, int> { { "ok", constants != null ? 1 : 0 } }, counts, 1);
        Report("entity_types/natural", naturals, counts, 2);
        Report("entity_types/artifact", artifacts, counts, 3);
        Report("pet/pets_for_client", pets, counts, 4);
        Report("quests/epics_for_client", stories, counts, 5);
        Report("item/recipes", recipes, counts, 6);
        Report("emotions", new Dictionary<string, int> { { "ok", Emotions != null ? 1 : 0 } }, counts, 7);
        Report("statistics/player", playerStats?.ResistanceExpGrownCaps, counts, 8);
        Report("entity_types/players", playerTypes, counts, 9);

        Console.WriteLine("[data] โหลด game data จาก " + dataDir + "/assets เสร็จ:");
        foreach (var (name, count) in counts)
        {
            Console.WriteLine($"[data]   {name,-28} {(count > 0 ? count + " รายการ" : "⚠️ ว่าง/ไม่พบ")}");
        }
        Loaded = true;
    }

    private static void Report<TK, TV>(string name, Dictionary<TK, TV> dict, (string, int)[] counts, int idx)
    {
        counts[idx] = (name, dict?.Count ?? 0);
    }
}
