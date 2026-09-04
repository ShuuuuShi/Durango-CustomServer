using System;
using System.Collections.Generic;
using Durango.Utils.Extensions;
using Shared.Region;

namespace Durango.Terrain;

// พอร์ตย่อจาก nexonSRC/Durango.Terrain/BiomeSpriteInfo.cs — เซิร์ฟใช้แค่ชื่อ/ไอคอน (ฝั่ง client วาดเอง)
public class BiomeSpriteInfo
{
    public int EntityType;

    public string[] SpriteNames;

    public string CollectibleId;

    public string Icon;

    public string Name;

    public bool Additive;

    public string Particle;

    public Biome[] Survivability;

    public bool IsCraft;

    public bool HasSprite(string spriteName)
    {
        if (SpriteNames == null) return false;
        foreach (string s in SpriteNames)
        {
            if (s == spriteName) return true;
        }
        return false;
    }
}
