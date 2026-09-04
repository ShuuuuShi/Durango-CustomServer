using Durango.Terrain;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/TerrainData.cs
public class TerrainData
{
    public byte[] Biomes;

    public byte[] Ocean;

    public byte[] Rivers;

    public byte[] Landmarks;

    public byte[] Garden;

    public TerrainInfoJson Info;

    /// <summary>จุดสำคัญที่มากับเกาะ (pois.yml) — ท่าเรือ/รูวาร์ป · null ได้ถ้าเกาะไม่มีไฟล์นี้</summary>
    public TerrainPois Pois;

    /// <summary>จุดเกิดฝูงสัตว์ที่มากับเกาะ (herds.yml) · null ได้ถ้าเกาะไม่มีไฟล์นี้</summary>
    public TerrainHerds Herds;

    public int Width;

    public int Height;
}
