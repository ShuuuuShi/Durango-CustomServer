using System;
using System.Collections.Generic;
using Durango.Utils.Extensions;
using Shared.Region;
using Yaml;

namespace Durango.Terrain;

// พอร์ตจาก nexonSRC/Durango.Terrain/DataHelper.cs + BiomeSpriteInfoData.cs (รวมไฟล์เดียว)
public static class DataHelper
{
    private static BiomeSpriteInfoData _biomeSpriteInfoData;

    public static bool IsNaturalObject(int entityType) => entityType is >= 10000 and < 21000;

    public static BiomeSpriteInfo GetBiomeSpriteInfo(int objectTypeId) => _biomeSpriteInfoData?.GetBiomeSpriteInfo(objectTypeId);

    public static void Initialize(Dictionary<int, Natural> yaml)
    {
        _biomeSpriteInfoData = new BiomeSpriteInfoData();
        _biomeSpriteInfoData.Load(yaml);
    }

    public static bool IsMajorBiome(Biome biome) => biome switch
    {
        Biome.TemperateForest or Biome.TropicalForest or Biome.Desert or Biome.Tundra
            or Biome.SnowField or Biome.Grassland or Biome.SwampMud or Biome.Volcanic => true,
        _ => false
    };

    private class BiomeSpriteInfoData
    {
        private readonly Dictionary<int, BiomeSpriteInfo> _dict = new();

        public BiomeSpriteInfo GetBiomeSpriteInfo(int objectTypeId)
        {
            _dict.TryGetValue(objectTypeId, out var v);
            return v;
        }

        public void Load(Dictionary<int, Natural> yml)
        {
            foreach (var (key, natural) in yml)
            {
                var info = ToInfo(natural);
                if (info == null) continue;
                info.EntityType = key;
                if (!_dict.ContainsKey(info.EntityType)) _dict.Add(info.EntityType, info);
            }
        }

        private static BiomeSpriteInfo ToInfo(Natural json)
        {
            var info = new BiomeSpriteInfo
            {
                SpriteNames = json.sprite_names,
                CollectibleId = json.collectible_id,
                Icon = json.icon,
                Name = json.name,
                Additive = json.additive,
                Particle = json.particle,
                IsCraft = json.is_craft
            };
            if (json.survivability != null)
            {
                var list = new List<Biome>();
                foreach (var (key, ok) in json.survivability)
                {
                    if (ok && key.ToCamelCase().ToEnum<Biome>(Biome.Invalid) is var b && b != Biome.Invalid)
                    {
                        list.Add(b);
                    }
                }
                if (list.Count > 0) info.Survivability = list.ToArray();
            }
            return info;
        }
    }
}
