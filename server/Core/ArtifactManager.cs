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

    /// <summary>แปลง → prototype ของเมล็ดที่ปลูกไว้ (ดู WorldContext.Plantings)</summary>
    private readonly Dictionary<string, string> _plantings;

    public static readonly string[] AddOnTags = { "door", "window", "wall_deco", "empty_door" };

    public event Action<ArtifactDisplay> ArtifactDisplayUpdated;

    public event Action<ArtifactState> ArtifactStateUpdated;

    public ArtifactManager(Dictionary<string, AppearArtifact> artifacts, Dictionary<string, AddOns> addons,
        Dictionary<string, Messages.Mannequin> mannequins, Dictionary<string, string> plantings = null)
    {
        _artifacts = artifacts;
        _addOns = addons;
        _mannequins = mannequins;
        _plantings = plantings ?? new Dictionary<string, string>();

        // โลกที่โหลดจากไฟล์เซฟมีสิ่งปลูกสร้างเก่าที่ยังไม่มีแท็ก (เซฟก่อนหน้านี้ไม่เคยเก็บ)
        // ⇒ เติมให้ตอนเปิดโลก ไม่งั้นโต๊ะที่สร้างไว้ก่อนจะคราฟต์ไม่ได้ตลอดไป
        if (_artifacts != null && _artifacts.Count > 0)
        {
            var keys = new List<string>(_artifacts.Keys);
            int patched = 0;
            foreach (string key in keys)
            {
                AppearArtifact artifact = _artifacts[key];
                bool changed = WorkbenchTags.Apply(ref artifact);
                changed |= CageTypes.Apply(ref artifact);

                // เติมฟิลด์ที่เซฟรุ่นเก่าไม่มี — ไม่เติมแล้วของเดิมบนเกาะจะยัง "Lv.0" และ
                // ข้อความอัปเดตสถานะถูกทิ้งเงียบตลอดไป (ดูเหตุผลเต็มที่ Cheats.MakeAppearArtifact)
                if (string.IsNullOrEmpty(artifact.States.EntityId))
                {
                    artifact.States.EntityId = artifact.EntityId;
                    changed = true;
                }
                if (artifact.States.Level == 0)
                {
                    MergedBlueprint bp = BlueprintStore.GetBlueprint(artifact.EntityType);
                    artifact.States.Level = (byte)Math.Clamp(bp?.MaxLevel ?? 1, 1, 255);
                    changed = true;
                }
                if (artifact.States.MaxHealth <= 0f)
                {
                    artifact.States.MaxHealth = Cheats.ArtifactMaxHealth;
                    changed = true;
                }
                if (!changed) continue;
                _artifacts[key] = artifact;
                patched++;
            }
            if (patched > 0) Console.WriteLine($"[โต๊ะคราฟต์] เติมแท็ก/สถานะกรงให้สิ่งปลูกสร้างเดิม {patched} หลัง");
        }
    }

    public void AddArtifact(AppearArtifact artifact)
    {
        // โต๊ะคราฟต์ต้องมีแท็กติดไปด้วย ไม่งั้นฝั่งเกมถือว่า "ไม่มีโต๊ะ" (ดู Support/WorkbenchTags.cs)
        WorkbenchTags.Apply(ref artifact);
        // กรงต้องมีสถานะความจุ ไม่งั้นหน้าจอกรงเปิดมาว่างเปล่า (ดู Support/CageTypes.cs)
        CageTypes.Apply(ref artifact);
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

    // ══════════════════════════════════════════════════════════════════════════════════
    //  กรงสัตว์ — อ่าน/แก้สถานะแล้วบอกทุกคนบนเกาะ
    //
    //  ทำไมต้องมี API แยก: หน้าจอกรงฝั่งเกม **อ่านสถานะจากตัว artifact ตรง ๆ**
    //  (client/Durango.UI/PetUtil.cs → GetGrowCage(artifact)) ไม่ได้ถามเซิร์ฟเป็น message
    //  ⇒ ตอบ OK ให้ PutInCage/StartPetTask เฉย ๆ หน้าจอจะไม่เปลี่ยนอะไรเลย
    //  ต้องแก้ ArtifactState.Cage / .DomesticCage แล้วยิง ArtifactStateUpdated ออกไปด้วย
    //
    //  ⚠️ ArtifactState.Cage เป็น object ที่ใส่ได้ทั้ง Cage และ GrowCage — ในเซิร์ฟนี้ใช้
    //  GrowCage อย่างเดียว (โรงเลี้ยงที่สั่งงานได้) ดู Support/CageTypes.cs
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ยิง ArtifactStateUpdated พร้อมประทับ EntityId ให้ก่อนเสมอ
    ///
    /// ⚠️ <c>ArtifactState.EntityId</c> เป็นฟิลด์ของตัวมันเอง ไม่ได้ถูกเติมจาก
    /// <c>AppearArtifact.EntityId</c> ที่ห่อมันอยู่ ⇒ เซิร์ฟไม่เคยตั้ง = ว่างตลอด
    /// แล้วฝั่งเกมหา artifact ด้วย <c>Find(msg.EntityId)</c> (client/ArtifactManager.cs:57-60)
    /// ⇒ **หาไม่เจอ ทิ้งข้อความเงียบ ๆ ทุกครั้ง** หน้าจอกรง/ตู้/ประตูจึงไม่รีเฟรชเลย
    /// </summary>
    private void RaiseStateUpdated(string entityId, ArtifactState state)
    {
        state.EntityId = entityId;
        ArtifactStateUpdated?.Invoke(state);
    }

    public GrowCage? GetGrowCage(string entityId) =>
        entityId != null && _artifacts.TryGetValue(entityId, out var a) && a.States.Cage is GrowCage cage
            ? cage
            : null;

    public DomesticCage? GetDomesticCage(string entityId) =>
        entityId != null && _artifacts.TryGetValue(entityId, out var a) ? a.States.DomesticCage : null;

    /// <summary>
    /// แก้สถานะโรงเลี้ยงแล้วกระจายให้เห็นทั้งเกาะ — คืน false ถ้าไม่ใช่โรงเลี้ยง
    ///
    /// รับเป็นฟังก์ชันแปลงค่าเพื่อให้ "อ่าน-แก้-เขียนกลับ" อยู่ในที่เดียว ผู้เรียกจะลืมเขียนกลับไม่ได้
    /// (AppearArtifact เป็น struct — แก้ตัวที่ดึงออกมาแล้วไม่เขียนกลับ = ไม่มีอะไรเกิดขึ้น
    ///  ซึ่งเป็นกับดักที่เงียบมาก)
    /// </summary>
    public bool UpdateGrowCage(string entityId, Func<GrowCage, GrowCage> mutate)
    {
        if (mutate == null || entityId == null) return false;
        if (!_artifacts.TryGetValue(entityId, out var artifact)) return false;
        if (artifact.States.Cage is not GrowCage cage) return false;

        artifact.States.Cage = mutate(cage);
        _artifacts[entityId] = artifact;
        RaiseStateUpdated(entityId, artifact.States);      // → Player ส่งต่อให้ client + World เซฟ
        return true;
    }

    /// <summary>แก้สถานะกรงฝึกให้เชื่องแล้วกระจายให้เห็นทั้งเกาะ — คืน false ถ้าไม่ใช่กรงฝึก</summary>
    public bool UpdateDomesticCage(string entityId, Func<DomesticCage, DomesticCage> mutate)
    {
        if (mutate == null || entityId == null) return false;
        if (!_artifacts.TryGetValue(entityId, out var artifact)) return false;
        if (!artifact.States.DomesticCage.HasValue) return false;

        artifact.States.DomesticCage = mutate(artifact.States.DomesticCage.Value);
        _artifacts[entityId] = artifact;
        RaiseStateUpdated(entityId, artifact.States);
        return true;
    }

    /// <summary>
    /// ปลูกเมล็ดลงแปลง — ตั้งทั้ง "หน้าตา" และ "สถานะการเติบโต"
    ///
    /// ⚠️ ต้นฉบับที่พอร์ตมา (client/Durango.Online/ArtifactManager.cs:80) ตั้งแค่
    /// <c>Display.Crop = grown_looks[…]</c> ⇒ **ปลูปแล้วโตเต็มที่ทันทีในเฟรมเดียว**
    /// และ <c>ArtifactState.Farming</c> ไม่เคยถูกตั้ง ⇒ ฝั่งเกม:
    ///   • <c>Farm.UpdateGrowTimer</c> (client/Farm.cs:159-165) return ทันที = ไม่มีหลอดเวลาโต
    ///   • <c>Farm.GetName()</c> คืน null = แปลงไม่มีชื่อพืช
    ///   • <c>ArtifactInfoMainWidget.FillFarming</c> (:512-515) return = ป้ายข้อมูลไม่มีอะไรเลย
    ///     (ไม่มีบรรทัด 작물 / 수확까지 / 필요 물의 양 / 기후 적합성 / 비옥도 / 퇴비)
    ///   • เมนู รดน้ำ/ใส่ปุ๋ย/เร่งโต ทั้งสามตัวเช็ค Farming.HasValue ก่อน ⇒ กดไม่ได้
    /// นั่นคือพฤติกรรมของ "เซิร์ฟในตัวโหมดแซนด์บ็อกซ์" ไม่ใช่ของเซิร์ฟจริง
    ///
    /// ค่าทุกตัวมาจาก <c>data/assets/crops.json</c> ของ NEXON — ไม่มีตัวไหนตั้งเอง
    /// </summary>
    public void SeedPlant(string entityId, string prototypeId, int level, Shared.Region.Biome biome)
    {
        Crop crop = CropYaml.Get(prototypeId);
        if (crop == null || !_artifacts.TryGetValue(entityId, out var value)) return;

        double now = Gauge.CurrentTime;
        var vars = new Dictionary<string, double> { ["level"] = level };
        double seconds = StatFormula.TryEval(crop.GrowsUntil, vars, out double evaluated) && evaluated > 0
            ? evaluated
            : 0.0;

        value.States.Farming = new Farming
        {
            // ชื่อที่โชว์บนป้าย = ชื่อของ "ผลผลิต" ไม่ใช่ของเมล็ด (ป้ายเขียนว่า 작물 = พืชผล)
            PlantName = CropDisplayName(crop, prototypeId),
            PlantedAt = now,
            GrowsUntil = now + seconds,
            // Vector2(น้ำที่รดแล้ว, น้ำที่ต้องการ) — ฝั่งเกมคิดส่วนต่างเอง (ArtifactInteractions.cs:980)
            Water = new UnityEngine.Vector2(0f, crop.RequiredWater),
            BiomeFitness = FitnessOf(crop.PreferenceLand, biome),
            FertilizedRatio = 0f,
            FertilizerAmount = 0f,
            RequiredFertilizer = crop.RequiredFertilizer,
            AppliedCropBooster = null,
            BoosterLevel = 0,
            // ค่าเร่งโตด้วยเพชร — ปล่อย null ฝั่งเกมจะไม่โชว์ตัวเลือกนั้น
            // (client/ArtifactInteractions.cs:1051-1053 return ทันทีถ้า null) ยังไม่มีระบบเพชร
            RapidGrowthCost = null
        };

        // ต้นอ่อนก่อน แล้วค่อยสลับเป็นต้นโตตอนครบเวลา (ดู ProcessFarming)
        // ถ้าไฟล์ไม่มีโมเดลต้นอ่อน ก็ใช้ต้นโตไปเลยดีกว่าปล่อยว่าง (ว่าง = ไม่มีอะไรบนแปลง)
        value.Display.Crop = seconds > 0
            ? crop.GrowingLook ?? crop.GrownLookAt(value.Tile.x, value.Tile.y)
            : crop.GrownLookAt(value.Tile.x, value.Tile.y);

        _plantings[entityId] = prototypeId;
        _artifacts[entityId] = value;
        ArtifactDisplayUpdated?.Invoke(value.Display);
        RaiseStateUpdated(entityId, value.States);
    }

    /// <summary>
    /// สลับต้นอ่อน → ต้นโตเมื่อครบเวลา · เรียกจาก World.Process
    ///
    /// เช็คจาก <c>Display.Crop</c> เทียบกับโมเดลต้นอ่อนของพืชชนิดนั้น จึงไม่ต้องเก็บธงเพิ่ม
    /// และปลอดภัยเมื่อโหลดจากไฟล์เซฟ (แปลงที่ปลูกค้างไว้ก่อนรีสตาร์ตจะโตต่อเองถูกต้อง)
    /// </summary>
    public void ProcessFarming(double now)
    {
        List<string> ripened = null;
        foreach (var pair in _artifacts)
        {
            AppearArtifact artifact = pair.Value;
            if (artifact.States.Farming is not { } farming) continue;
            if (farming.GrowsUntil <= 0.0 || now < farming.GrowsUntil) continue;

            if (!_plantings.TryGetValue(pair.Key, out string seed)) continue;
            Crop crop = CropYaml.Get(seed);
            string grown = crop?.GrownLookAt(artifact.Tile.x, artifact.Tile.y);
            if (grown == null || artifact.Display.Crop == grown) continue;

            (ripened ??= new List<string>()).Add(pair.Key);
        }
        if (ripened == null) return;

        foreach (string entityId in ripened)
        {
            AppearArtifact artifact = _artifacts[entityId];
            Crop crop = CropYaml.Get(_plantings[entityId]);
            artifact.Display.Crop = crop.GrownLookAt(artifact.Tile.x, artifact.Tile.y);
            Console.WriteLine($"[ปลูก] {entityId[..Math.Min(8, entityId.Length)]} " +
                              $"{artifact.States.Farming?.PlantName} โตเต็มที่แล้ว");
            _artifacts[entityId] = artifact;
            ArtifactDisplayUpdated?.Invoke(artifact.Display);
        }
    }

    /// <summary>
    /// ชื่อพืชที่โชว์บนป้าย — ใช้ชื่อของผลผลิต (<c>grows_to</c>) ถ้าหาได้
    /// ถอยไปชื่อเมล็ดเมื่อไม่มี prototype ของผลผลิตในตาราง
    /// </summary>
    private static string CropDisplayName(Crop crop, string seedPrototypeId)
    {
        Prototype product = string.IsNullOrEmpty(crop.GrowsTo)
            ? null
            : PrototypeYaml.GetItemPrototype(crop.GrowsTo);
        string name = product?.Name?.ToString();
        if (!string.IsNullOrEmpty(name)) return name;

        return PrototypeYaml.GetItemPrototype(seedPrototypeId)?.Name?.ToString() ?? seedPrototypeId;
    }

    /// <summary>
    /// ความเหมาะสมของไบโอม — <c>preference_land</c> ในไฟล์เป็นชื่อไบโอมตรง ๆ
    /// (grassland · temperate_forest · tropical_forest · tundra · desert)
    ///
    /// ระดับที่ฝั่งเกมมีคือ Bad/Normal/Good/Excelent (Shared.Etc.Fitness)
    /// **การจับคู่ระดับเป็นของเรา** เพราะไฟล์บอกแค่ไบโอมที่ชอบ ไม่ได้บอกตารางคะแนน:
    ///   ตรงกับที่ชอบ → Excelent · ไบโอมบกอื่น → Normal · หาด/น้ำ/ลาวา → Bad
    /// </summary>
    private static Shared.Etc.Fitness FitnessOf(string preferenceLand, Shared.Region.Biome biome)
    {
        if (biome is Shared.Region.Biome.PebbleBeach or Shared.Region.Biome.SandBeach
            or Shared.Region.Biome.ColdOcean or Shared.Region.Biome.WarmOcean
            or Shared.Region.Biome.River or Shared.Region.Biome.Lake or Shared.Region.Biome.Lava)
        {
            return Shared.Etc.Fitness.Bad;
        }
        return BiomeNameOf(biome) == preferenceLand
            ? Shared.Etc.Fitness.Excelent
            : Shared.Etc.Fitness.Normal;
    }

    /// <summary>ชื่อไบโอมแบบที่ crops.json ใช้ — ตรงกับ info.yml ของ terrain (lake_biome ฯลฯ)</summary>
    private static string BiomeNameOf(Shared.Region.Biome biome) => biome switch
    {
        Shared.Region.Biome.TemperateForest => "temperate_forest",
        Shared.Region.Biome.TropicalForest => "tropical_forest",
        Shared.Region.Biome.Desert => "desert",
        Shared.Region.Biome.Tundra => "tundra",
        Shared.Region.Biome.SnowField => "snowfield",
        Shared.Region.Biome.Grassland => "grassland",
        Shared.Region.Biome.SwampMud => "swamp_mud",
        Shared.Region.Biome.Volcanic => "volcanic",
        _ => null
    };

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
            RaiseStateUpdated(scribble.EntityId, value.States);
        }
    }

    public void OpenGate(PropKey key, bool open)
    {
        if (_artifacts.TryGetValue(key.EntityId, out var value) && value.States.GateOpened != open)
        {
            value.States.EntityId = value.EntityId;
            value.States.GateOpened = open;
            _artifacts[key.EntityId] = value;
            RaiseStateUpdated(key.EntityId, value.States);
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
