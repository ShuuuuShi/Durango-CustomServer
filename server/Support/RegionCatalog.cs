using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Messages;
using Shared.Region;

namespace Durango.Online;

/// <summary>
/// รายชื่อเกาะทั้งหมดที่เซิร์ฟนี้มี — สร้างจากไฟล์ terrain ที่วางอยู่จริงใน data/terrains
///
/// ทำไมต้องมี: ระบบล่องเรือของเกมถามเซิร์ฟว่า "จากท่าเรือนี้ไปไหนได้บ้าง" (GetRoutes)
/// แล้วขอรายละเอียดของแต่ละปลายทางต่อ (GetRegion) ⇒ เซิร์ฟต้องมีสารบัญเกาะก่อน
/// เซิร์ฟในตัวของเกมไม่มีสารบัญนี้เพราะมันมีโลกเดียวเสมอ
///
/// เกาะ 1 ลูก = terrain zip 1 ไฟล์ · RegionId ใช้ชื่อไฟล์ (เช่น "ri35te") เพื่อให้
/// อ่านออกใน log และผูกกับไฟล์เซฟได้ตรง ๆ ไม่ต้องมีตารางแปลงอีกชั้น
/// </summary>
public static class RegionCatalog
{
    private static readonly List<Region> _regions = new();
    private static readonly Dictionary<string, Region> _byId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>เกาะทั้งหมดเรียงตามชื่อไฟล์</summary>
    public static IReadOnlyList<Region> All => _regions;

    /// <summary>
    /// สแกนโฟลเดอร์ terrain แล้วสร้างสารบัญ — เรียกครั้งเดียวตอนบูต หลังตั้ง TerrainLoader.TerrainDir
    /// </summary>
    public static void Load()
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

            var region = new Region
            {
                Id = id,
                TerrainId = id,
                // TemplateId ต้องตรงกับคีย์ใน data/assets/region_templates.json ไม่งั้นเกมข้ามเกาะนี้ทิ้ง
                // (client/ExploreSystem.cs:307 — SingletonDict<string, RegionTemplate>.Get(templateId) == null ⇒ ไม่แสดง)
                TemplateId = data?.Info?.region_template ?? id,
                Role = Role.Rural,
                Name = id,
                CreatedAt = 0.0
            };
            _regions.Add(region);
            _byId[id] = region;
        }

        Console.WriteLine($"[region] สารบัญเกาะ {_regions.Count} ลูก: {string.Join(", ", _regions.Select(r => r.Id))}");
    }

    public static bool TryGet(string regionId, out Region region) =>
        _byId.TryGetValue(regionId ?? "", out region);

    /// <summary>เกาะอื่นทั้งหมดที่ไม่ใช่เกาะที่ยืนอยู่ตอนนี้ — ใช้เป็นปลายทางของเส้นทางเดินเรือ</summary>
    public static IEnumerable<Region> Others(string currentRegionId) =>
        _regions.Where(r => !string.Equals(r.Id, currentRegionId, StringComparison.OrdinalIgnoreCase));
}
