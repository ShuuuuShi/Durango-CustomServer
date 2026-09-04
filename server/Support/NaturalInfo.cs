using System.Collections.Generic;
using Durango.Utils.Extensions;

namespace Durango.Terrain;

// พอร์ตจาก nexonSRC/Durango.Terrain/NaturalInfo.cs — บรรทัดละ 6 ไบต์ (x,y,entityType uint16)
public class NaturalInfo : CoordInfo
{
    public const int RawNaturalDataSize = 6;

    public ushort EntityType;

    public override string ToString() => $"Natural: {EntityType} ({X},{Y})";

    private static void ToBytes(NaturalInfo info, BinaryWriter w)
    {
        w.Write(info.X);
        w.Write(info.Y);
        w.Write(info.EntityType);
    }

    private static NaturalInfo FromBytes(BinaryReader r)
    {
        return new NaturalInfo { X = r.ReadUInt16(), Y = r.ReadUInt16(), EntityType = r.ReadUInt16() };
    }

    public static byte[] ToBytes(IList<NaturalInfo> infos)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            for (int i = 0; i < infos.Count; i++) ToBytes(infos[i], w);
        }
        return ms.ToArray();
    }

    public static NaturalInfo[] FromBytes(byte[] raw)
    {
        int count = raw.Length / RawNaturalDataSize;
        var result = new NaturalInfo[count];
        using var r = new BinaryReader(new MemoryStream(raw));
        for (int i = 0; i < count; i++) result[i] = FromBytes(r);
        return result;
    }
}
