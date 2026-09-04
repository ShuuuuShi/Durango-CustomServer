using System;
using System.Collections.Generic;
using System.Linq;
using Durango.UI.Control;
using Durango.Utils.Extensions;
using Messages;
using Shared.Building;
using Shared.Etc;
using UnityEngine;
using Yaml;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/Cheats.cs
// ความต่าง: GameSystem<RecipeSystem> ฝั่ง client → BlueprintStore, ItemIconTex → พอร์ตย่อ (ตารางสี .raw ไม่มีบนเซิร์ฟ)
public static class Cheats
{
    public static Item? MakeItem(string prototypeId, int level)
    {
        prototypeId = prototypeId.Replace(".", "_");
        Prototype itemPrototype = PrototypeYaml.GetItemPrototype(prototypeId);
        if (itemPrototype == null)
        {
            return null;
        }
        Item value = new()
        {
            Id = Guid.NewGuid().ToString(),
            FounderId = null,
            FounderCategory = string.Empty,
            Durability = new Gauge(1f, 0f, new[] { new GaugeNode(0.0, 1f) }),
            Size = itemPrototype.Size,
            Unstable = false,
            ModifiableCount = 0,
            ModifiedCount = 0
        };
        int hashCode = value.Id.GetHashCode();
        ItemIconTex.TryGetDefaultColor(itemPrototype.ColorR, out var col, hashCode, Color.white);
        ItemIconTex.TryGetDefaultColor(itemPrototype.ColorG, out var col2, hashCode, Color.white);
        ItemIconTex.TryGetDefaultColor(itemPrototype.ColorB, out var col3, hashCode, Color.white);
        value.ColorR = col.ToHex();
        value.ColorG = col2.ToHex();
        value.ColorB = col3.ToHex();
        value.Icon = itemPrototype.Icon;
        value.Prototype = prototypeId;
        value.Level = level;
        value.Name = itemPrototype.Name;
        value.Description = itemPrototype.Description;
        var list = new List<Messages.Tag>();
        foreach (var tag in itemPrototype.Tags)
        {
            list.Add(new Messages.Tag
            {
                Level = level,
                Id = tag.Key
            });
        }
        value.Tags = list.ToArray();
        var list2 = new List<Performance>();
        if (PerformanceYaml.TryGetAddOnModelKey(prototypeId, out var modelKey))
        {
            list2.Add(new Performance
            {
                Id = "add_on",
                Strs = new Dictionary<string, string> { { "add_on_model_key", modelKey } }
            });
        }
        PerformanceYaml.Weapon weapon = PerformanceYaml.GetWeapon(prototypeId);
        if (weapon != null)
        {
            list2.Add(new Performance
            {
                Id = "weapon",
                Strs = new Dictionary<string, string>
                {
                    { "weapon_framework", weapon.WeaponFramework },
                    { "model", weapon.Model },
                    { "slot", weapon.Slot }
                }
            });
        }
        PerformanceYaml.Armor armor = PerformanceYaml.GetArmor(prototypeId);
        if (armor != null)
        {
            list2.Add(new Performance
            {
                Id = "armor",
                Strs = new Dictionary<string, string>
                {
                    { "female_model", armor.FemaleModel },
                    { "male_model", armor.MaleModel },
                    { "slot", armor.Slot }
                }
            });
        }
        PerformanceYaml.Instrument instrument = PerformanceYaml.GetInstrument(prototypeId);
        if (instrument != null)
        {
            list2.Add(new Performance
            {
                Id = "instrument",
                Strs = new Dictionary<string, string> { { "timbre", instrument.Timbre } }
            });
        }
        value.Performance = list2.ToArray();
        return value;
    }

    public static AppearArtifact? MakeAppearArtifact(string[] arguments, out AddOns? addons)
    {
        addons = null;
        AppearArtifact appearArtifact = default;
        bool flag = arguments[0] == "immortal";
        appearArtifact.EntityId = Guid.NewGuid().ToString();
        string s = !flag ? arguments[1] : arguments[2];
        appearArtifact.EntityType = ushort.Parse(s);
        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(int.Parse(s));
        if (blueprint == null)
        {
            return null;
        }
        appearArtifact.Display.Condition = Condition.Normal;
        appearArtifact.Display.EntityId = appearArtifact.EntityId;
        appearArtifact.Display.Parts = new Dictionary<string, string>();
        for (int i = !flag ? 2 : 3; i < arguments.Length; i++)
        {
            string text = arguments[i];
            string[] array = text.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (array[0] == "floor" && array.Length == 2 && int.TryParse(array[1], out var result))
            {
                appearArtifact.Floor = result;
                continue;
            }
            switch (array[0])
            {
                case "rotation":
                    appearArtifact.Rotation = (Rotation)Enum.Parse(typeof(Rotation), array[1], ignoreCase: true);
                    continue;
                case "position":
                {
                    string[] array3 = array[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    appearArtifact.Tile = new Point2(int.Parse(array3[0]), int.Parse(array3[1]));
                    continue;
                }
                case "size":
                {
                    string[] array2 = array[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    appearArtifact.Size = new Point2(int.Parse(array2[0]), int.Parse(array2[1]));
                    continue;
                }
                case "stories":
                    if (array.Length == 2)
                    {
                        appearArtifact.Stories = array[1].ToInt();
                    }
                    continue;
                case "level":
                    continue;
            }
            if (array.Length != 2)
            {
                continue;
            }
            string slotId = array[0];
            if (blueprint.Slots == null)
            {
                continue;
            }
            Yaml.BlueprintSlot blueprintSlot = blueprint.Slots.FirstOrDefault(slot => slot.slot_id == slotId);
            if (blueprintSlot?.looks != null && blueprintSlot.looks.TryGetValue(array[1], out var look))
            {
                appearArtifact.Display.Parts.Add(slotId, look.model_key);
            }
        }
        if (appearArtifact.Rotation == Rotation.Quarter || appearArtifact.Rotation == Rotation.ThreeQuarter)
        {
            appearArtifact.Size = new Point2(appearArtifact.Size.y, appearArtifact.Size.x);
        }
        for (int num = 0; num < KUtility.GetSize(blueprint.Components); num++)
        {
            switch (blueprint.Components[num])
            {
                case "Modular":
                    appearArtifact.Display.AddOns = new Dictionary<int, Pair<string, string>>();
                    if (!appearArtifact.Stories.HasValue)
                    {
                        appearArtifact.Stories = 1;
                    }
                    break;
                case "Landmark":
                case "FourSideEnterable":
                    appearArtifact.Stories = 1;
                    SetDisplayParts(appearArtifact, blueprint);
                    break;
                default:
                    SetDisplayParts(appearArtifact, blueprint);
                    break;
            }
        }
        appearArtifact.States.BuildingState = BuildingState.Completed;
        appearArtifact.States.Durability = new Gauge(1f, 0f, new[]
        {
            new GaugeNode { Time = 0.0, Value = 1f }
        });
        if (appearArtifact.Display.Parts.Count == 0)
        {
            appearArtifact.Display.Parts.Add("common",
                !blueprint.Components.Contains("Burnable") ? blueprint.DefaultLook : blueprint.DefaultLook + "_burning");
        }
        if (appearArtifact.Display.AddOns != null)
        {
            Dictionary<string, string> parts = appearArtifact.Display.Parts;
            if (parts != null && !string.IsNullOrEmpty(parts.Get("wall")))
            {
                Item? item = MakeItem("door_01_none", 1);
                if (item.HasValue)
                {
                    addons = new AddOns
                    {
                        _AddOns = new Dictionary<int, Item> { { 0, item.Value } }
                    };
                }
            }
        }
        return appearArtifact;
    }

    private static void SetDisplayParts(AppearArtifact artifact, MergedBlueprint blueprint)
    {
        if (artifact.Display.Parts.Count != 0)
        {
            return;
        }
        foreach (Yaml.BlueprintSlot blueprintSlot in blueprint.Slots)
        {
            if (blueprintSlot.looks == null) continue;
            foreach (var item in blueprintSlot.looks)
            {
                artifact.Display.Parts.Add(blueprintSlot.slot_id, item.Value.model_key);
            }
        }
    }
}
