using UnityEngine;

namespace Durango.Terrain;

// พอร์ตจาก nexonSRC/Durango.Terrain/Util.cs — เซิร์ฟใช้แค่ TilePositionToChunkCoords
// chunk = 16×16 tile, tile กว้าง 200 world-unit
public static class Util
{
    public static Point2 TilePositionToChunkCoords(Point2 worldTile)
    {
        return new Point2(worldTile.x * 200 / 3200, worldTile.y * 200 / 3200);
    }
}
