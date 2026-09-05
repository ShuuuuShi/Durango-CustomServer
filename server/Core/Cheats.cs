using System;
using System.Collections.Generic;
using System.Globalization;
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

        // เงื่อนไขการซ่อม — ไม่แนบ ⇒ ฝั่งเกมถือว่า "ซ่อมไม่ได้" แล้วหน้าต่างซ่อมไม่เปิดเลย
        // (client/Durango.Logic.Item/ItemData.cs:107 IsRepairable) ดูเหตุผลเต็มที่ RepairTuning
        value.RepairRequirement = RepairTuning.Of(itemPrototype, level);

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
        // อาหารสัตว์ — ฝั่งเกมคัดไอเทมที่ป้อนสัตว์ได้จากหมวดนี้ (client/Durango.UI/PetUtil.cs:128-133)
        // ไม่แนบ ⇒ หน้าต่าง "เลือกอาหาร" ว่างเปล่าทั้งที่มีอาหารอยู่ในกระเป๋า
        PerformanceYaml.PetFood petFood = PerformanceYaml.GetPetFood(prototypeId);
        if (petFood != null)
        {
            list2.Add(new Performance
            {
                Id = "pet_food",
                Nums = new Dictionary<string, float> { { "vigor", petFood.Vigor } }
            });
        }

        // บังเหียน — ถ้าไม่มี pet_entity_type ฝั่งเกมไม่ถือว่าไอเทมนี้ "เป็นสัตว์"
        // (client/Durango.Logic.Item/Util.cs:298-306) ⇒ ร้านค้าไม่ยิง GetPreviewPet มาเลย
        PerformanceYaml.Rein rein = PerformanceYaml.GetRein(prototypeId);
        if (rein != null)
        {
            list2.Add(new Performance
            {
                Id = "reins",
                Nums = new Dictionary<string, float> { { "pet_entity_type", rein.PetEntityType } },
                Strs = new Dictionary<string, string> { { "playback_rate", rein.PlaybackRate.ToString(CultureInfo.InvariantCulture) } }
            });
        }

        // เติมค่าพลังที่เหลือทั้งหมดจาก performance.json (คิดสูตรที่เลเวลของไอเทมชิ้นนี้)
        // ไม่มีบล็อกตัวเลขพวกนี้ = หน้ารายละเอียดไอเทมไม่โชว์ค่าโจมตี/ป้องกัน/พลังงานเลยสักบรรทัด
        ItemPerformance.MergeInto(list2, prototypeId, level);

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

        // ⚠️ ArtifactState.EntityId เป็นฟิลด์ของตัวมันเอง ไม่ได้สืบจาก AppearArtifact.EntityId
        // ฝั่งเกมหา artifact ด้วยค่านี้ (client/ArtifactManager.cs:57-60 Find(msg.EntityId))
        // ⇒ ไม่ตั้ง = ข้อความอัปเดตสถานะถูกทิ้งเงียบทุกครั้ง
        appearArtifact.States.EntityId = appearArtifact.EntityId;

        // เลเวลของสิ่งปลูกสร้าง — ไม่ตั้งจะโชว์ "Lv.0" บนป้ายชื่อและกรอบเป้าหมายทุกหลัง
        // ใช้ max_level ของแบบแปลน (ข้อมูลจริง building/blueprints.json) ด้วยเหตุผลเดียวกับ
        // แท็กโต๊ะคราฟต์/ความจุกรง: เซิร์ฟยังไม่ได้เก็บเลเวลรายหลัง ถ้าให้ต่ำไว้ผู้เล่นเพิ่มไม่ได้เลย
        appearArtifact.States.Level = (byte)Math.Clamp(blueprint.MaxLevel, 1, 255);

        // **ค่าของเรา** — ข้อมูลเกมไม่มี HP ของสิ่งปลูกสร้างเลย (artifact_stats ว่างทั้ง 249 รายการ
        // และ blueprints.json ไม่มีฟิลด์ durability/hp) แต่ไม่ตั้งแล้วหลอดเลือดเป้าอ่านเป็น 0/0
        // (client/ArtifactDamageableEntity.cs:87 ใช้ MaxHealth / Durability.Max() เป็นสเกลหลอด)
        // ⇒ ใช้ 100 เพราะ Durability ของเราเป็นอัตราส่วน 0-1 อยู่แล้ว ตัวเลขจะอ่านเป็นเปอร์เซ็นต์พอดี
        appearArtifact.States.MaxHealth = ArtifactMaxHealth;
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

    /// <summary>**ค่าของเรา** — เลขที่หลอดความทนทานของสิ่งปลูกสร้างอ่านเป็น "เต็ม" (ดูเหตุผลที่จุดใช้งาน)</summary>
    internal const float ArtifactMaxHealth = 100f;

    private static void SetDisplayParts(AppearArtifact artifact, MergedBlueprint blueprint)
    {
        if (artifact.Display.Parts.Count != 0)
        {
            return;
        }
        // ⚠️ หนึ่งช่องมีได้หลายหน้าตา (blueprints.json มี 68 ช่องที่มีมากกว่าหนึ่ง look
        // เช่น gate2/main = {wood, bone, stone}) — Parts เป็น Dictionary คีย์ slot_id
        // ⇒ วนใส่ทุก look แล้ว Add ซ้ำคีย์เดิม = ArgumentException ตัวที่สอง
        // exception ถูกกลืนที่ GameCode/Durango.Online/Connection.cs:486-489 (เขียนแค่ log ฝั่งเซิร์ฟ)
        // ⇒ **สิ่งปลูกสร้าง 43 ชนิดวางแล้วไม่โผล่บนจอเลยโดยไม่มีข้อความบอกผู้เล่น**
        // (เตียง 7007 · โต๊ะ 6226 · เก้าอี้ 6227 · ชั้นวาง 6228 · ตู้เซฟ 7009 · gate3 · fence3 ...)
        // และ blueprint.Slots เป็น null ได้ (BlueprintStore.cs:59 `Slots = bp?.slots`)
        // อีก 4 ชนิดที่ไม่มีใน blueprints.json เลยพังด้วย NullReferenceException
        if (blueprint.Slots == null) return;

        foreach (Yaml.BlueprintSlot blueprintSlot in blueprint.Slots)
        {
            if (blueprintSlot.looks == null || blueprintSlot.slot_id == null) continue;
            if (artifact.Display.Parts.ContainsKey(blueprintSlot.slot_id)) continue;

            // เอาหน้าตาแรกที่มีโมเดลจริง — ผู้เล่นเลือกแบบอื่นได้ทีหลังผ่านระบบดัดแปลง
            foreach (var item in blueprintSlot.looks)
            {
                string modelKey = item.Value?.model_key;
                if (string.IsNullOrEmpty(modelKey)) continue;
                artifact.Display.Parts[blueprintSlot.slot_id] = modelKey;
                break;
            }
        }
    }
}
