using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Region;
using Shared.Survival;

namespace Durango.Online;

/// <summary>
/// สารบัญเกาะของเซิร์ฟ — สร้างจาก terrain zip ที่วางอยู่จริง ผูกกับ region template ของเกม
///
/// ทำไมต้องมี: ระบบล่องเรือถามเซิร์ฟว่า "จากท่าเรือนี้ไปไหนได้บ้าง" (GetRoutes) แล้วขอ
/// รายละเอียดปลายทางต่อ (GetRegion / GetArchipelago) ⇒ เซิร์ฟต้องมีสารบัญก่อน
/// เซิร์ฟในตัวของเกมไม่มีเพราะมันมีโลกเดียวเสมอ
///
/// เกาะ 1 ลูก = terrain zip 1 ไฟล์ · RegionId ใช้ชื่อไฟล์ (เช่น "ri35te") ให้ผูกกับไฟล์เซฟตรง ๆ
///
/// **template สำคัญกว่าที่คิด** — ตัวเกมอ่าน level/role/biome จาก region_templates.json ของตัวเอง
/// แล้วใช้จัดหน้า UI ทั้งหมด (client/Durango.UI/WorldRoutesUnstableArea.cs:236 จับคู่โซนด้วย
/// Role + Level + MajorBiome ของ template) ⇒ Routes ที่เราส่งต้องใช้ค่าเดียวกันเป๊ะ
/// ไม่งั้นเกาะจะไม่ไปโผล่ในโซนไหนเลย
/// </summary>
public static class RegionCatalog
{
    /// <summary>ข้อมูล template ที่ UI ใช้จัดโซน — อ่านจาก data/assets/region_templates.json</summary>
    public class TemplateInfo
    {
        public string Id;
        public int Level;
        public Role Role = Role.Rural;
        public Biome Biome = Biome.Invalid;

        /// <summary>ฝูงสัตว์ที่เกิดบนเกาะแบบนี้ · ชื่อกลุ่ม (land/beach/…) → รายการฝูง</summary>
        public Dictionary<string, List<HerdSpawn>> Herds = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// ไบโอมบนเกาะแบบนี้ให้ความเหนื่อยหมวดไหน — จาก <c>region_templates.json → biome_effects</c>
        /// (ครบทั้ง 267 แม่แบบ) เช่น <c>volcanic → [volcanic_heat]</c> · <c>swamp_mud → [humid]</c>
        /// คู่ไบโอม↔หมวดตรงกับ <c>constants.json → resistance.types_by_biome</c> ที่ NEXON
        /// แม็ปเป็น <c>Dictionary&lt;Biome, Derived&gt;</c> เองอยู่แล้ว (client/Yaml/Resistance.cs)
        /// </summary>
        public Dictionary<Biome, FatigueCategory[]> BiomeEffects = new();

        /// <summary>
        /// ชื่อสถานการณ์สภาพอากาศของเกาะแบบนี้ — ค่าจริงจาก <c>region_templates.json → weather</c>
        /// เช่น <c>ending_climate_snowy</c> · <c>volcanic_normal</c> · <c>always_volcanic_ash</c>
        /// (null = ไม่ระบุ) แปลงเป็นลำดับสภาพอากาศจริงที่ <see cref="WeatherTuning"/>
        /// </summary>
        public string Weather;
    }

    /// <summary>
    /// ฝูงหนึ่งฝูงที่แม่แบบสั่งให้เกิด — ถอดจากตัวเลขก้อนเดียวใน <c>region_templates.json</c>
    ///
    /// ในไฟล์ <c>herds.land.spawns</c> เป็นลิสต์ของเลข 6 หลัก เช่น <c>204220</c> โดย
    /// **จำนวนสมาชิกในลิสต์เท่ากับ <c>total_count</c> เป๊ะทั้ง 310 กลุ่มในไฟล์** ⇒ หนึ่งเลข = หนึ่งฝูง
    ///
    /// การถอดเลข <c>204220</c> → ชนิดสัตว์ <c>2042</c> + เลเวลต่อสู้ <c>20</c> ยืนยันด้วยข้อมูลจริง:
    /// • <c>เลข/100</c> เป็นชนิดที่มีจริงใน <c>entity_types/animal.json</c> **10,606 จาก 10,616 ครั้ง**
    ///   (ชนิดสัตว์มี 214 ตัวกระจายในช่วง 2000-2999 ⇒ ถ้าถอดผิดจะพลาดเกือบหมด ไม่ใช่ 99.9%)
    /// • <c>เลข%100</c> ตกอยู่ในช่วง <c>combat_level_ranges</c> ของสัตว์ชนิดนั้นเอง **127 จาก 136 คู่**
    /// • และมันไม่ใช่เลเวลของเกาะ (เกาะ lv5 มีสัตว์ lv10/20 · เกาะ lv40 มีตั้งแต่ lv1 ถึง 21)
    ///
    /// ⚠️ ที่เหลือ ~7% ถอดแล้วหลุดช่วง — เราหนีบเข้าช่วงของสัตว์ตัวนั้นแทนที่จะทิ้ง (ดู AnimalManager)
    /// </summary>
    public readonly struct HerdSpawn
    {
        public readonly ushort EntityType;
        public readonly int CombatLevel;

        public HerdSpawn(ushort entityType, int combatLevel)
        {
            EntityType = entityType;
            CombatLevel = combatLevel;
        }

        public static HerdSpawn FromPacked(int packed) => new((ushort)(packed / 100), packed % 100);
    }

    private static readonly List<Region> _regions = new();
    private static readonly Dictionary<string, Region> _byId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, TemplateInfo> _templates = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<Region> All => _regions;

    public static void Load(string assetsDir)
    {
        LoadTemplates(assetsDir);
        LoadRegions();
    }

    /// <summary>
    /// อ่าน region_templates.json — เอาเฉพาะ level / role / biome ที่ UI ใช้จัดโซน
    /// biome มาจากคีย์แรกของ <c>biome_effects</c> (เช่น "temperate_forest" → Biome.TemperateForest)
    /// ซึ่งเป็นทางเดียวกับที่ตัวเกมหา MajorBiome (client/Yaml/RegionTemplate.cs:105-120)
    /// </summary>
    private static void LoadTemplates(string assetsDir)
    {
        _templates.Clear();
        string path = Path.Combine(assetsDir ?? "", "region_templates.json");
        if (!File.Exists(path))
        {
            Console.WriteLine($"[region] ⚠️ ไม่พบ {path} — เกาะจะไม่มีข้อมูล level/biome");
            return;
        }
        try
        {
            var root = JObject.Parse(File.ReadAllText(path));
            foreach (KeyValuePair<string, JToken> kv in root)
            {
                var info = new TemplateInfo { Id = kv.Key };
                if (kv.Value is JObject o)
                {
                    info.Level = (int?)o["level"] ?? 0;
                    info.Weather = (string)o["weather"];
                    if ((int?)o["role"] is { } roleValue && Enum.IsDefined(typeof(Role), roleValue))
                    {
                        info.Role = (Role)roleValue;
                    }
                    if (o["biome_effects"] is JObject effects)
                    {
                        info.Biome = ParseBiome(effects.Properties().FirstOrDefault()?.Name);
                        foreach (JProperty be in effects.Properties())
                        {
                            Biome biome = ParseBiome(be.Name);
                            if (biome == Biome.Invalid || be.Value is not JArray cats) continue;
                            FatigueCategory[] list = cats
                                .Select(x => FatigueTuning.ParseCategory(x?.ToString()))
                                .Where(c => c != FatigueCategory.Invalid)
                                .ToArray();
                            if (list.Length > 0) info.BiomeEffects[biome] = list;
                        }
                    }
                    if (o["herds"] is JObject herds)
                    {
                        foreach (JProperty group in herds.Properties())
                        {
                            if (group.Value["spawns"] is not JArray spawns || spawns.Count == 0) continue;
                            var list = new List<HerdSpawn>(spawns.Count);
                            foreach (JToken packed in spawns)
                            {
                                if ((int?)packed is { } value) list.Add(HerdSpawn.FromPacked(value));
                            }
                            if (list.Count > 0) info.Herds[group.Name] = list;
                        }
                    }
                }
                _templates[kv.Key] = info;
            }
            Console.WriteLine($"[region] อ่าน region template {_templates.Count} รายการ");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[region] อ่าน region_templates.json ไม่สำเร็จ: {e.Message}");
        }
    }

    /// <summary>"temperate_forest" → Biome.TemperateForest (ชื่อใน json เป็น snake_case)</summary>
    private static Biome ParseBiome(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return Biome.Invalid;
        }
        string pascal = string.Concat(name.Split('_').Select(p =>
            p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p.Substring(1)));
        return Enum.TryParse(pascal, ignoreCase: true, out Biome biome) ? biome : Biome.Invalid;
    }

    private static void LoadRegions()
    {
        _regions.Clear();
        _byId.Clear();

        string dir = TerrainLoader.TerrainDir;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            Console.WriteLine($"[region] ⚠️ ไม่พบโฟลเดอร์ terrain: {dir}");
            return;
        }

        foreach (string path in Directory.GetFiles(dir, "*.zip").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            TerrainData data;
            try
            {
                data = TerrainLoader.Load(id);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[region] ข้าม {id}: {e.Message}");
                continue;
            }

            string templateId = data?.Info?.region_template ?? id;
            TemplateInfo template = GetTemplate(templateId);
            if (template == null)
            {
                // template ที่เกมไม่รู้จัก ⇒ client ข้ามทิ้งทั้งกลุ่ม (client/ExploreSystem.cs:307)
                Console.WriteLine($"[region] ⚠️ ข้าม {id}: เกมไม่รู้จัก template '{templateId}'");
                continue;
            }

            var region = new Region
            {
                Id = id,
                TerrainId = id,
                TemplateId = templateId,
                Role = template.Role,
                Name = id,
                CreatedAt = 0.0
            };
            _regions.Add(region);
            _byId[id] = region;
        }

        Console.WriteLine($"[region] สารบัญเกาะ {_regions.Count} ลูก: " +
                          string.Join(", ", _regions.Select(r => $"{r.Id}(lv{GetTemplate(r.TemplateId)?.Level})")));
    }

    public static bool TryGet(string regionId, out Region region) =>
        _byId.TryGetValue(regionId ?? "", out region);

    public static TemplateInfo GetTemplate(string templateId) =>
        templateId != null && _templates.TryGetValue(templateId, out TemplateInfo info) ? info : null;

    /// <summary>เกาะอื่นทั้งหมดที่ไม่ใช่เกาะที่ยืนอยู่ตอนนี้ — ปลายทางของเส้นทางเดินเรือ</summary>
    public static IEnumerable<Region> Others(string currentRegionId) =>
        _regions.Where(r => !string.Equals(r.Id, currentRegionId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// id ของหมู่เกาะที่รวมเกาะระดับ+ไบโอมเดียวกันไว้ — ตั้งเองเพราะเซิร์ฟจริงของ NEXON
    /// เป็นคนสร้างหมู่เกาะแบบไดนามิก (มีอายุ หมดแล้วสร้างใหม่) ซึ่งเรายังไม่ได้ทำ
    /// </summary>
    public static string ArchipelagoIdOf(TemplateInfo template) =>
        template == null ? null : $"arch_{(int)template.Role}_{template.Level}_{(int)template.Biome}";
}
