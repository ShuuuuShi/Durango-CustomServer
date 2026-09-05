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
        string theme = null;
        data.Biomes = null;
        data.Ocean = null;
        data.Rivers = null;
        data.Landmarks = null;
        data.Garden = null;
        data.Pois = null;
        data.Herds = null;
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
                // [5 ก.ย. 2026] herds.yml บอกจุดที่ฝูงสัตว์เกิด — คู่กับ region_templates.json ที่บอกชนิด
                else if (CheckEntry(entry, "herds.yml")) data.Herds = TerrainHerds.Parse(LoadEntry(entry));
                // config.yml มีเฉพาะเกาะที่ tools/gen-island.py สร้าง — ใช้ธีมเติม tile_set ให้ (ดู FillTileSet)
                else if (CheckEntry(entry, "config.yml")) theme = ReadTheme(LoadEntry(entry));
            }
            FillTileSet(terrainId, theme, data.Info);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[terrain] อ่าน {path} ไม่สำเร็จ: {e.Message}");
        }
    }

    /// <summary>ธีมของตัวสร้างเกาะ — <c>config.yml</c> → <c>theme</c> (null ถ้าไม่มี/อ่านไม่ได้)</summary>
    private static string ReadTheme(byte[] bytes)
    {
        try
        {
            return (string)Newtonsoft.Json.Linq.JObject.Parse(
                System.Text.Encoding.UTF8.GetString(bytes))["theme"];
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// เติม <c>tile_set</c>/<c>color_set</c> ให้เกาะที่ไฟล์ปล่อยว่างไว้
    ///
    /// ⚠️ ค่าว่าง = ตัวเกมหาชุดสีของเกาะไม่เจอ แล้วใช้ค่าเริ่มต้นแทนทั้งหมด
    /// ⇒ **เกาะหิมะเรนเดอร์เป็นทุ่งหญ้า · ทะเลทรายก็เขียว** โดยไม่มี error ให้เห็น
    /// (เหตุผลเต็ม + ที่มาของรายชื่อ ดูที่ <see cref="TileSets"/>)
    ///
    /// เขียนทับเฉพาะตอนที่ไฟล์ปล่อยว่าง — เกาะที่ NEXON ใส่ค่ามาแล้วไม่แตะ
    /// </summary>
    private static void FillTileSet(string terrainId, string theme, TerrainInfoJson info)
    {
        if (info == null) return;
        bool needTile = string.IsNullOrEmpty(info.tile_set);
        bool needColor = string.IsNullOrEmpty(info.color_set);
        if (!needTile && !needColor) return;

        string guess = TileSets.Guess(terrainId, theme);
        if (!TileSets.IsKnown(guess))
        {
            Console.WriteLine($"[terrain] ⚠️ {terrainId} ไม่มี tile_set และเดาไม่ได้ (theme={theme ?? "-"}) " +
                              "— เกาะจะใช้โทนสีเริ่มต้น");
            return;
        }
        if (needTile) info.tile_set = guess;
        if (needColor) info.color_set = guess;
        Console.WriteLine($"[terrain] {terrainId}: เติม tile_set/color_set = {guess}");
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
