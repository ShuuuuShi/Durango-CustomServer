using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils.Extensions;
using Messages;
using UnityEngine;
using Yaml;
using Yaml.Util;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/ArtifactManager.cs
// ความต่าง: GameSystem<RecipeSystem> ฝั่ง client → BlueprintStore (ข้อมูลชุดเดียวกันจาก data/assets)
public class ArtifactManager
{
    private readonly Dictionary<string, AppearArtifact> _artifacts;

    private readonly Dictionary<string, AddOns> _addOns;

    private readonly Dictionary<string, Messages.Mannequin> _mannequins;

    public static readonly string[] AddOnTags = { "door", "window", "wall_deco", "empty_door" };

    public event Action<ArtifactDisplay> ArtifactDisplayUpdated;

    public event Action<ArtifactState> ArtifactStateUpdated;

    public ArtifactManager(Dictionary<string, AppearArtifact> artifacts, Dictionary<string, AddOns> addons,
        Dictionary<string, Messages.Mannequin> mannequins)
    {
        _artifacts = artifacts;
        _addOns = addons;
        _mannequins = mannequins;

        // โลกที่โหลดจากไฟล์เซฟมีสิ่งปลูกสร้างเก่าที่ยังไม่มีแท็ก (เซฟก่อนหน้านี้ไม่เคยเก็บ)
        // ⇒ เติมให้ตอนเปิดโลก ไม่งั้นโต๊ะที่สร้างไว้ก่อนจะคราฟต์ไม่ได้ตลอดไป
        if (_artifacts != null && _artifacts.Count > 0)
        {
            var keys = new List<string>(_artifacts.Keys);
            int patched = 0;
            foreach (string key in keys)
            {
                AppearArtifact artifact = _artifacts[key];
                if (!WorkbenchTags.Apply(ref artifact)) continue;
                _artifacts[key] = artifact;
                patched++;
            }
            if (patched > 0) Console.WriteLine($"[โต๊ะคราฟต์] เติมแท็กให้สิ่งปลูกสร้างเดิม {patched} หลัง");
        }
    }

    public void AddArtifact(AppearArtifact artifact)
    {
        // โต๊ะคราฟต์ต้องมีแท็กติดไปด้วย ไม่งั้นฝั่งเกมถือว่า "ไม่มีโต๊ะ" (ดู Support/WorkbenchTags.cs)
        WorkbenchTags.Apply(ref artifact);
        _artifacts.Add(artifact.EntityId, artifact);
    }

    public AppearArtifact? Get(string entityId)
    {
        if (_artifacts.TryGetValue(entityId, out var value)) return value;
        return null;
    }

    public IEnumerable<AppearArtifact> Enumerable(Predicate<AppearArtifact> func) =>
        from pair in _artifacts where func(pair.Value) select pair.Value;

    public AppearArtifact? RemoveArtifact(string entityId)
    {
        _addOns.Remove(entityId);
        if (_artifacts.TryGetValue(entityId, out var value))
        {
            _artifacts.Remove(entityId);
            return value;
        }
        return null;
    }

    public Messages.Mannequin? GetMannequin(string entityId)
    {
        if (_mannequins.TryGetValue(entityId, out var value)) return value;
        return null;
    }

    public void SeedPlant(string entityId, string prototypeId)
    {
        Crop crop = CropYaml.Get(prototypeId);
        if (crop != null && _artifacts.TryGetValue(entityId, out var value))
        {
            value.Display.Crop = crop.GrownLooks[KUtilityNx.GetRandomHash(value.Tile.x, value.Tile.y) % crop.GrownLooks.Length];
            _artifacts[entityId] = value;
            ArtifactDisplayUpdated?.Invoke(value.Display);
        }
    }

    public void ChargeEffect(string entityId)
    {
        if (_artifacts.TryGetValue(entityId, out var value))
        {
            value.States.Effector = new Effector { RemainCount = 100 };
            value.Display.Decorations = new Dictionary<string, Pair<string, string>>
            {
                { "incense", new Pair<string, string>("clan_thurible_incense", string.Empty) }
            };
            _artifacts[entityId] = value;
            ArtifactDisplayUpdated?.Invoke(value.Display);
        }
    }

    public void Scribble(Scribble scribble)
    {
        if (_artifacts.TryGetValue(scribble.EntityId, out var value))
        {
            value.States.EntityId = value.EntityId;
            value.States.Scribble = new ScribbleContent
            {
                Data = scribble.Data,
                Type = scribble.Type
            };
            _artifacts[scribble.EntityId] = value;
            ArtifactStateUpdated?.Invoke(value.States);
        }
    }

    public void OpenGate(PropKey key, bool open)
    {
        if (_artifacts.TryGetValue(key.EntityId, out var value) && value.States.GateOpened != open)
        {
            value.States.EntityId = value.EntityId;
            value.States.GateOpened = open;
            _artifacts[key.EntityId] = value;
            ArtifactStateUpdated?.Invoke(value.States);
        }
    }

    public void ChangeDecoration(string entityId)
    {
        if (!_artifacts.TryGetValue(entityId, out var value)) return;
        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(value.EntityType);
        if (blueprint == null) return;
        List<string[]> list = RecipeDict.GetDecorations(blueprint.Id);
        if (KUtility.GetSize(list) == 0) return;
        if (value.Display.Decorations == null)
        {
            value.Display.Decorations = new Dictionary<string, Pair<string, string>>();
        }
        if (value.Display.Decorations.TryGetValue("deco", out var curDeco) && string.IsNullOrEmpty(curDeco.Item2))
        {
            List<string[]> list2 = list.Where(o => o[0] != curDeco.Item1 || !string.IsNullOrEmpty(o[1])).ToList();
            if (list2.Count > 0) list = list2;
        }
        string[] array = list[UnityEngine.Random.Range(0, list.Count)];
        string item = !string.IsNullOrEmpty(array[1]) ? NxRandom.ColorHSV().ToHex() : string.Empty;
        value.Display.Decorations["deco"] = new Pair<string, string>(array[0], item);
        _artifacts[entityId] = value;
        ArtifactDisplayUpdated?.Invoke(value.Display);
    }

    public AddOns GetAddons(string entityId)
    {
        if (!_addOns.ContainsKey(entityId))
        {
            _addOns.Add(entityId, default);
        }
        return _addOns[entityId];
    }

    public AppearArtifact? PlaceAddOns(string entityId, Dictionary<int, Item> placements)
    {
        if (_artifacts.TryGetValue(entityId, out var value))
        {
            var dictionary = new Dictionary<int, Pair<string, string>>();
            foreach (var placement in placements)
            {
                Performance performance = placement.Value.Performance.FirstOrDefault(o => o.Id == "add_on");
                if (performance.Strs == null) continue;
                string modelKey = performance.Strs.Get("add_on_model_key");
                Messages.Tag[] tags = placement.Value.Tags;
                string tag = AddOnTags.FirstOrDefault(o => tags.Any(p => o == p.Id));
                dictionary.Add(placement.Key, new Pair<string, string>(modelKey, tag));
            }
            value.Display.AddOns = dictionary;
            _artifacts[entityId] = value;
            _addOns[entityId] = new AddOns { _AddOns = placements };
            ArtifactDisplayUpdated?.Invoke(value.Display);
            return value;
        }
        return null;
    }

    public void UpdateArtifactDisplay(ArtifactDisplay display)
    {
        if (_artifacts.TryGetValue(display.EntityId, out var value))
        {
            value.Display = display;
            _artifacts[display.EntityId] = value;
            ArtifactDisplayUpdated?.Invoke(value.Display);
        }
    }

    public AppearArtifact? ExtendFloor(string entityId, bool withRoof)
    {
        if (_artifacts.TryGetValue(entityId, out var value))
        {
            if (!value.Stories.HasValue) return null;
            value.Stories++;
            value.HasRoof = withRoof;
            _artifacts[entityId] = value;
            return value;
        }
        return null;
    }

    public void TurnOnMusic(string entityId)
    {
        if (!_artifacts.TryGetValue(entityId, out var value)) return;
        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(value.EntityType);
        if (blueprint != null)
        {
            string[] musics = blueprint.Musics;
            if (KUtility.GetSize(musics) > 0)
            {
                int num = UnityEngine.Random.Range(0, musics.Length);
                value.Display.Music = new Pair<string, double>(musics[num], 0.0);
                _artifacts[entityId] = value;
            }
            ArtifactDisplayUpdated?.Invoke(value.Display);
        }
    }

    public void TurnOffMusic(string entityId)
    {
        if (!_artifacts.TryGetValue(entityId, out var value)) return;
        if (value.Display.Music.HasValue)
        {
            value.Display.Music = null;
            _artifacts[entityId] = value;
            ArtifactDisplayUpdated?.Invoke(value.Display);
        }
    }

    public bool TakeOutItems(string entityId, string[] ids)
    {
        if (ids == null) return false;
        if (!_artifacts.ContainsKey(entityId)) return false;
        Messages.Mannequin? mannequin = GetMannequin(entityId);
        if (!mannequin.HasValue) return false;
        Messages.Mannequin value2 = mannequin.Value;
        string slot = null;
        Item? head = value2.Head;
        if (head.HasValue && ids.Contains(value2.Head.Value.Id))
        {
            slot = "head";
            value2.Head = null;
        }
        else
        {
            Item? body = value2.Body;
            if (body.HasValue && ids.Contains(value2.Body.Value.Id))
            {
                slot = "body";
                value2.Body = null;
            }
        }
        if (string.IsNullOrEmpty(slot)) return false;
        if (value2.Head.HasValue || value2.Body.HasValue)
        {
            _mannequins[entityId] = value2;
        }
        else
        {
            _mannequins.Remove(entityId);
        }
        TakeOffMannequin(entityId, slot);
        return true;
    }

    public bool TakeOffMannequin(string entityId, string slot)
    {
        if (!_artifacts.TryGetValue(entityId, out var value)) return false;
        ArtifactDisplay display = value.Display;
        MannequinDisplayInfo info = display.MannequinInfo.GetValueOrDefault();
        Messages.Mannequin value2 = _mannequins.Get(entityId);
        switch (slot)
        {
            case "head":
                info.Head = null;
                info.HeadColor = null;
                value2.Head = null;
                break;
            case "body":
                info.Body = null;
                info.BodyColor = null;
                value2.Body = null;
                break;
            default:
                return false;
        }
        value2.EntityId = entityId;
        _mannequins[entityId] = value2;
        display.MannequinInfo = info;
        value.Display = display;
        _artifacts[entityId] = value;
        ArtifactDisplayUpdated?.Invoke(value.Display);
        return true;
    }

    public bool ChangeMannequin(string entityId, string slot, Item item)
    {
        if (!_artifacts.TryGetValue(entityId, out var value)) return false;
        if (!SingletonDict<int, ArtifactPrototype>.TryGetValue(value.EntityType, out var value2)) return false;
        bool isMale;
        switch (value2.gender)
        {
            case "male": isMale = true; break;
            case "female": isMale = false; break;
            default: return false;
        }
        string model = null;
        string key = !isMale ? "female_model" : "male_model";
        if (item.Performance != null)
        {
            foreach (Performance performance in item.Performance)
            {
                if (performance.Strs != null && performance.Strs.TryGetValue(key, out var value3))
                {
                    model = value3;
                    break;
                }
            }
        }
        if (string.IsNullOrEmpty(model) || model.Equals("None", StringComparison.OrdinalIgnoreCase)) return false;
        ArtifactDisplay display = value.Display;
        MannequinDisplayInfo info = display.MannequinInfo.GetValueOrDefault();
        Messages.Mannequin value4 = _mannequins.Get(entityId);
        switch (slot)
        {
            case "head":
                info.Head = model;
                info.HeadColor = new[] { item.ColorR, item.ColorG, item.ColorB };
                value4.Head = item;
                break;
            case "body":
                info.Body = model;
                info.BodyColor = new[] { item.ColorR, item.ColorG, item.ColorB };
                value4.Body = item;
                break;
            default:
                return false;
        }
        value4.EntityId = entityId;
        _mannequins[entityId] = value4;
        display.MannequinInfo = info;
        value.Display = display;
        _artifacts[entityId] = value;
        ArtifactDisplayUpdated?.Invoke(value.Display);
        return true;
    }
}
