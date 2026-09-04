using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Durango.Utils.Extensions;
using Yaml.Util;

namespace Yaml;

// เทียบเท่า RecipeSystem.RecipeContainer ฝั่ง client: merge Yaml.Blueprint (craft data: slots/looks)
// กับ ArtifactPrototype (entity data: components/musics/size/is_craft) — คีย์เชื่อมคือ __name__ ↔ blueprint id
// ต้นฉบับอยู่ที่ nexonSRC/Building/Blueprint.cs (SetPrototypeInfo) — เซิร์ฟใช้เฉพาะฟิลด์ที่ handler แท้ใช้
public class MergedBlueprint
{
    public string Id;

    public Gettext Name;

    public string[] Components;

    public string[] Musics;

    public string DefaultLook;

    public bool IsShowCraftMode;

    public Yaml.BlueprintSlot[] Slots;

    public int EntityType;
}

public static class BlueprintStore
{
    private static Dictionary<int, MergedBlueprint> _byEntityType;
    private static List<MergedBlueprint> _all;

    public static void Initialize(Dictionary<int, ArtifactPrototype> artifacts = null, Dictionary<string, Blueprint> blueprints = null)
    {
        _byEntityType = new Dictionary<int, MergedBlueprint>();
        _all = new List<MergedBlueprint>();
        artifacts ??= SingletonDict<int, ArtifactPrototype>.Instance;
        blueprints ??= new Dictionary<string, Blueprint>();
        if (artifacts == null) return;

        foreach (var pair in artifacts)
        {
            int entityType = pair.Key;
            ArtifactPrototype proto = pair.Value;
            blueprints.TryGetValue(proto.__name__ ?? "", out var bp);

            var merged = new MergedBlueprint
            {
                Id = proto.__name__ ?? bp?.preview ?? entityType.ToString(),
                EntityType = entityType,
                // ต้นฉบับ: Components = components + client_only_components (SetPrototypeInfo)
                Components = MergeComponents(proto),
                Musics = proto.musics,
                DefaultLook = bp?.default_look,
                IsShowCraftMode = proto.is_craft,
                Slots = bp?.slots
            };
            // ชื่อ: blueprint มี name (Gettext) ก่อน ถ้าไม่มีใช้ prototype ไม่มี fallback ชื่อจาก __name__
            merged.Name = bp?.name ?? new Gettext(proto.__name__);

            _byEntityType[entityType] = merged;
            _all.Add(merged);
        }
        Console.WriteLine($"[blueprint] merge blueprint×artifact: {_all.Count} รายการ");
    }

    private static string[] MergeComponents(ArtifactPrototype proto)
    {
        int n = proto.components?.Length ?? 0;
        int m = proto.client_only_components?.Length ?? 0;
        var result = new string[n + m];
        for (int i = 0; i < n; i++) result[i] = proto.components[i];
        for (int i = 0; i < m; i++) result[n + i] = proto.client_only_components[i];
        return result;
    }

    /// <summary>เทียบเท่า RecipeContainer.GetBlueprint(entityType)</summary>
    public static MergedBlueprint GetBlueprint(int entityType)
    {
        return _byEntityType.Get(entityType);
    }

    /// <summary>เทียบเท่า RecipeContainer.GetAllBlueprints()</summary>
    public static IEnumerable<MergedBlueprint> GetAllBlueprints() => _all;
}
