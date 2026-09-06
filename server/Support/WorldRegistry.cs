using System;
using System.Collections.Generic;
using System.IO;
using Durango.Utils;

namespace Durango.Online;

/// <summary>
/// โลกของแต่ละเกาะ — เกาะ 1 ลูก = World 1 ตัว = ไฟล์เซฟ 1 ไฟล์
///
/// ทำไมต้องมี: เซิร์ฟในตัวของเกมมีโลกเดียวเสมอ (Server.BeginServer สร้าง GameServer ตัวเดียว
/// จาก WorldContext ตัวเดียว) แต่ระบบล่องเรือทั้งระบบตั้งอยู่บนสมมติฐานว่ามีหลายเกาะ
/// แล้วเดินทางไปมาได้ ⇒ ต้องถือหลายโลกพร้อมกัน
///
/// โหลดแบบ lazy: สร้างโลกของเกาะเมื่อมีคนไปถึงจริงเท่านั้น ไม่ได้เปิดทั้ง 14 เกาะค้างไว้
/// (แต่ละโลกกิน chunk data ของ terrain เต็มแผ่น)
///
/// ไฟล์เซฟ: <c>offline/&lt;cluster&gt;/regions/&lt;regionId&gt;.world</c>
/// แยกจากไฟล์ <c>0.world</c> ของต้นฉบับที่เป็นสล็อตของโลกเดี่ยว — ของเดิมยังอ่านได้เหมือนเดิม
/// </summary>
public class WorldRegistry
{
    private readonly string _clusterKey;
    private readonly Dictionary<string, World> _worlds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>เกาะตั้งต้นสำหรับผู้เล่นที่ยังไม่เคยไปไหน</summary>
    public string DefaultRegionId { get; }

    public WorldRegistry(string clusterKey, World defaultWorld, string defaultRegionId)
    {
        _clusterKey = clusterKey;
        DefaultRegionId = string.IsNullOrEmpty(defaultRegionId) ? TerrainLoader.DefaultTerrainFile : defaultRegionId;
        // โลกตั้งต้นมาจาก Host (ไฟล์ 0.world ของต้นฉบับ) — ใช้ต่อเลย ไม่สร้างซ้ำ
        if (defaultWorld != null)
        {
            defaultWorld.Registry = this;
            _worlds[DefaultRegionId] = defaultWorld;
        }
    }

    public IEnumerable<KeyValuePair<string, World>> Loaded => _worlds;

    /// <summary>
    /// โลกของเกาะที่ระบุ — สร้างขึ้นถ้ายังไม่เคยเปิด
    /// คืนโลกตั้งต้นเมื่อ regionId ว่างหรือไม่มีเกาะนั้นในสารบัญ
    /// </summary>
    /// <summary>
    /// แผนที่ region id ของเกาะส่วนตัว → template terrain จริง (pe10gr_1 ฯลฯ)
    /// ต้องมีเพราะ GetOrCreate เดิมรับเฉพาะ id ที่อยู่ใน RegionCatalog
    /// </summary>
    private readonly Dictionary<string, string> _personalTemplates = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterPersonalRegion(string regionId, string templateId)
    {
        if (string.IsNullOrEmpty(regionId) || string.IsNullOrEmpty(templateId)) return;
        _personalTemplates[regionId] = templateId;
    }

    public bool TryGetPersonalTemplate(string regionId, out string templateId) =>
        _personalTemplates.TryGetValue(regionId ?? "", out templateId);

    public bool IsPersonalRegion(string regionId) =>
        !string.IsNullOrEmpty(regionId) && _personalTemplates.ContainsKey(regionId);

    public World GetOrCreate(string regionId)
    {
        string terrainFile = null;
        if (string.IsNullOrEmpty(regionId))
        {
            regionId = DefaultRegionId;
        }
        else if (RegionCatalog.TryGet(regionId, out _))
        {
            terrainFile = regionId; // catalog ใช้ชื่อไฟล์ terrain เป็น region id
        }
        else if (_personalTemplates.TryGetValue(regionId, out string personalTemplate))
        {
            terrainFile = personalTemplate;
        }
        else
        {
            // ไม่รู้จักและไม่ใช่เกาะส่วนตัวที่ลงทะเบียนไว้ — ถอยไปเกาะตั้งต้น
            regionId = DefaultRegionId;
        }

        if (_worlds.TryGetValue(regionId, out World existing))
        {
            return existing;
        }

        string path = MakeRegionPath(_clusterKey, regionId);
        // WorldContext.Save เขียนไฟล์ตรง ๆ ไม่สร้างโฟลเดอร์ให้ (ต้นฉบับเขียนลง offline/<cluster>/
        // ที่มีอยู่แล้วเสมอ) — โฟลเดอร์ regions/ เป็นของใหม่ จึงต้องสร้างเองก่อน
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        WorldContext context = WorldContext.Load(path);
        if (context == null)
        {
            context = new WorldContext();
            context.Initialize(path);
            Console.WriteLine($"[world] สร้างโลกใหม่ของเกาะ {regionId}" +
                              (terrainFile != null && terrainFile != regionId ? $" (template {terrainFile})" : ""));
        }
        // โลกใช้ไฟล์ terrain จริง (pe10gr_*) แต่จำ region id ของตัวเองแยกได้ผ่าน registry key
        context.TerrainId = terrainFile ?? regionId;

        var world = new World(context);
        world.Registry = this;
        _worlds[regionId] = world;
        return world;
    }

    public void ProcessAll()
    {
        foreach (KeyValuePair<string, World> kv in _worlds)
        {
            kv.Value.Process();
        }
    }

    public void SaveAll()
    {
        foreach (KeyValuePair<string, World> kv in _worlds)
        {
            kv.Value.Save();
        }
    }

    public void StopAll()
    {
        foreach (KeyValuePair<string, World> kv in _worlds)
        {
            kv.Value.Stop();
        }
    }

    private static string MakeRegionPath(string clusterKey, string regionId) =>
        Path.Combine(AppData.CombinePath(WorldContext.GetBasePath(clusterKey)), "regions", regionId + ".world");
}
