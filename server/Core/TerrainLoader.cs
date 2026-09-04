using System;
using System.IO;
using System.IO.Compression;
using Durango.Terrain;
using Durango.Utils;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/TerrainLoader.cs
// ความต่างจุดเดียวที่เลี่ยงไม่ได้: ต้นฉบับโหลด zip จาก Unity Resources ("offline/terrains/{id}")
// ที่นี่โหลดจากดิสก์ <terrainDir>/{id}.zip — container ฟอร์แมตเดียวกัน (whole.biomes/ocean/rivers/
// landmarks/garden + info.yml) อ่านด้วย System.IO.Compression แทน SharpZipLib
public static class TerrainLoader
{
    /// <summary>โฟลเดอร์ที่มี terrain zip — Program ตั้งจาก CLI</summary>
    public static string TerrainDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "terrains");

    /// <summary>terrain ที่ใช้เมื่อ id ไม่ตรงไฟล์ (เกมขอ Region.TerrainId = "1" เสมอ)</summary>
    public static string DefaultTerrainFile { get; set; } = "pe10gr_1";

    public static TerrainData Load(string terrainId)
    {
        var terrainData = new TerrainData();
        LoadZip(terrainId, terrainData);
        if (terrainData.Info == null)
        {
            terrainData.Info = new TerrainInfoJson
            {
                tile_count = new[] { 256, 256 },
                lake_biome = "grassland",
                ocean_biome = "warm_ocean",
                river_biome = "temperate_forest",
                color_set = "grassland",
                region_template = "pe10gr_1",
                tile_set = "grassland",
                entry_points = new[] { new[] { 63, 71 } }
            };
        }
        terrainData.Width = terrainData.Info.tile_count[0];
        terrainData.Height = terrainData.Info.tile_count[1];
        if (terrainData.Biomes == null) terrainData.Biomes = new byte[terrainData.Width * terrainData.Height];
        int edge = (terrainData.Width + 1) * (terrainData.Height + 1);
        if (terrainData.Ocean == null) terrainData.Ocean = new byte[edge];
        if (terrainData.Rivers == null) terrainData.Rivers = new byte[edge * 3];
        return terrainData;
    }

    private static void LoadZip(string terrainId, TerrainData data)
    {
        data.Biomes = null;
        data.Ocean = null;
        data.Rivers = null;
        data.Landmarks = null;
        data.Garden = null;
        data.Pois = null;
        string path = ResolvePath(terrainId);
        if (path == null) return;
        try
        {
            using var stream = File.OpenRead(path);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (CheckEntry(entry, "whole.biomes")) data.Biomes = LoadEntry(entry);
                else if (CheckEntry(entry, "whole.ocean")) data.Ocean = LoadEntry(entry);
                else if (CheckEntry(entry, "whole.rivers")) data.Rivers = LoadEntry(entry);
                else if (CheckEntry(entry, "whole.landmarks")) data.Landmarks = LoadEntry(entry);
                else if (CheckEntry(entry, "whole.garden")) data.Garden = LoadEntry(entry);
                else if (CheckEntry(entry, "info.yml")) data.Info = Json.Read<TerrainInfoJson>(LoadEntry(entry));
                // [5 ก.ย. 2026] pois.yml บอกตำแหน่งท่าเรือ/รูวาร์ปที่มากับเกาะ — เซิร์ฟเป็นคนวางลงโลก
                // (เกมเป็น client ล้วน ไม่ได้วางเอง) เกาะที่ generate เองบางลูกไม่มีไฟล์นี้ ⇒ Pois = null
                else if (CheckEntry(entry, "pois.yml")) data.Pois = TerrainPois.Parse(LoadEntry(entry));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[terrain] อ่าน {path} ไม่สำเร็จ: {e.Message}");
        }
    }

    private static string ResolvePath(string terrainId)
    {
        foreach (string candidate in new[] { terrainId, DefaultTerrainFile })
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            string p = Path.Combine(TerrainDir, candidate + ".zip");
            if (File.Exists(p)) return p;
        }
        Console.WriteLine($"[terrain] ⚠️ ไม่พบ terrain '{terrainId}' ใน {TerrainDir}");
        return null;
    }

    private static bool CheckEntry(ZipArchiveEntry entry, string name) =>
        entry.Name.EndsWith(name, StringComparison.CurrentCultureIgnoreCase);

    private static byte[] LoadEntry(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
