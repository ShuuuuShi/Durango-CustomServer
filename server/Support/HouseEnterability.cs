using Yaml;

namespace Durango.Online;

/// <summary>
/// บ้านที่เดินเข้าได้ — ตรงกับคอมโพเนนต์ที่ฝั่งเกมสร้าง <c>EnterableArtifact</c>
/// (client/ArtifactManager.cs: Modular / FourSideEnterable / Landmark)
/// </summary>
public static class HouseEnterability
{
    public static bool IsEnterable(string[] components)
    {
        if (components == null) return false;
        foreach (string c in components)
        {
            if (c == "Modular" || c == "FourSideEnterable" || c == "Landmark") return true;
        }
        return false;
    }

    public static bool IsEnterable(int entityType)
    {
        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(entityType);
        return IsEnterable(blueprint?.Components);
    }

    public static bool ContainsTile(Point2 origin, Point2 size, Point2 tile)
    {
        int width = size.x > 0 ? size.x : 1;
        int height = size.y > 0 ? size.y : 1;
        return tile.x >= origin.x && tile.y >= origin.y
               && tile.x < origin.x + width && tile.y < origin.y + height;
    }
}
