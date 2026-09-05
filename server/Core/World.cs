using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Terrain;
using Shared.Building;
using Messages;
using UnityEngine;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/World.cs
public class World
{
    public enum ChunkVisit
    {
        None,
        Visit,
        Sent
    }

    private class ChunkData
    {
        public byte[] Landmarks;

        public byte[] Garden;
    }

    private const int BiomesPerChunk = 324;

    public readonly ArtifactManager ArtifactManager;

    public readonly MarketManager MarketManager;

    /// <summary>สัตว์ป่าบนเกาะนี้ — เกิดจาก herds.yml + แม่แบบภูมิภาค (ดู AnimalManager)</summary>
    public readonly AnimalManager AnimalManager;

    private readonly TerrainData _terrainData;

    private readonly ChunkData[,] _chunkData;

    private readonly WorldContext _context;

    private readonly List<NaturalInfo> _addedNatural;

    private readonly List<Point2> _removedNatural;

    private readonly List<Player> _players = new();

    public int NumChunksX { get; private set; }

    public int NumChunksY { get; private set; }

    public int NumTilesX => _terrainData.Width;

    public int NumTilesY => _terrainData.Height;

    public Point2 EntryPoint
    {
        get
        {
            int size = KUtility.GetSize(_terrainData.Info.entry_points);
            if (size >= 1 && KUtility.GetSize(_terrainData.Info.entry_points[0]) >= 2)
            {
                return new Point2(_terrainData.Info.entry_points[0][0], _terrainData.Info.entry_points[0][1]);
            }
            return new Point2(NumTilesX / 2, NumTilesY / 2);
        }
    }

    public TerrainInfoJson TerrainInfo => _terrainData.Info;

    /// <summary>ชื่อ terrain ของโลกนี้ (= RegionId ในสารบัญเกาะ ดู RegionCatalog)</summary>
    public string TerrainId => _context.TerrainId;

    public byte[] Biomes => _terrainData.Biomes;

    public string Weather { get; private set; }

    public event Action<AppearArtifact> ArtifactAppeared;

    public event Action<AppearArtifact> ArtifactDisappeared;

    public event Action<Player> PlayerAppeared;

    public event Action<Player> PlayerDisappeared;

    public event Action<Point2, byte[]> NaturalAdded;

    public event Action<Point2> NaturalDestroyed;

    public World(WorldContext context)
    {
        _context = context;
        ArtifactManager = new ArtifactManager(_context.Artifacts, _context.ArtifactAddOns, _context.ArtifactMannequins);
        ArtifactManager.ArtifactStateUpdated += ArtifactManager_ArtifactStateUpdated;
        ArtifactManager.ArtifactDisplayUpdated += ArtifactManager_ArtifactDisplayUpdated;
        _addedNatural = _context.AddedNatural;
        _removedNatural = _context.RemovedNatural;
        _terrainData = TerrainLoader.Load(context.TerrainId);
        MarketManager = new MarketManager();
        _terrainData.Info.global_landmarks = null;
        NumChunksX = _terrainData.Width / 16;
        NumChunksY = _terrainData.Height / 16;
        _chunkData = new ChunkData[NumChunksX, NumChunksY];
        AssignChunkData();
        PlaceTerrainPois();
        // สัตว์ป่า — เกิดหลังจากรู้ข้อมูลเกาะแล้ว เพราะต้องใช้ทั้ง herds.yml และแม่แบบของเกาะนี้
        AnimalManager = new AnimalManager(_terrainData, RegionCatalog.GetTemplate(_terrainData.Info?.region_template));
    }

    /// <summary>
    /// วางสิ่งปลูกสร้างประจำเกาะตามพิกัดใน pois.yml — ท่าเรือ/รูวาร์ป
    ///
    /// ทำไมเซิร์ฟต้องวาง: เกมของ NEXON เป็น client ล้วน มันรอรับ AppearArtifact จากเซิร์ฟ
    /// ไม่ได้อ่าน pois.yml เอง (ยืนยันแล้วว่าไม่มีจุดไหนในซอร์สเกมแตะไฟล์นี้)
    /// ถ้าไม่วาง ผู้เล่นจะไม่เจอท่าเรือ ⇒ กดล่องเรือไม่ได้เลยทั้งเกาะ
    ///
    /// ⚠️ id ต้อง **คงที่ผูกกับลำดับในไฟล์** (poi_port_0, poi_port_1, …) ห้ามไล่เลขตอนวาง
    /// ไม่งั้นเปิดเซิร์ฟรอบสองจะไม่รู้ว่าของเดิมคืออันไหน แล้ววางซ้อนเพิ่มทุกรอบ
    /// </summary>
    private void PlaceTerrainPois()
    {
        TerrainPois pois = _terrainData.Pois;
        if (pois == null)
        {
            return;
        }

        // (id, blueprint, entity type, ขนาด footprint) — ชนิดจาก data/assets/entity_types/artifact.json
        var wanted = new List<(string Id, ushort Type, Point2 Size, Point2 Tile)>();
        for (int i = 0; i < pois.PortPoints.Count; i++)
        {
            wanted.Add(($"poi_port_{i}", (ushort)7001, new Point2(3, 3), pois.PortPoints[i]));   // dock 항구
        }
        for (int i = 0; i < pois.Warpholes.Count; i++)
        {
            wanted.Add(($"poi_warphole_{i}", (ushort)9450, new Point2(6, 6), pois.Warpholes[i])); // neutral_warphole
        }
        for (int i = 0; i < pois.Rifts.Count; i++)
        {
            wanted.Add(($"poi_rift_{i}", (ushort)6282, new Point2(4, 4), pois.Rifts[i]));        // warp_accelerator
        }

        int placed = 0;
        foreach ((string id, ushort type, Point2 size, Point2 tile) in wanted)
        {
            if (ArtifactManager.Get(id).HasValue)
            {
                continue;   // มีอยู่แล้วจากรอบก่อน — ไม่วางซ้ำ
            }

            // ใช้ตัวสร้างเดียวกับ cheat "prop" ของต้นฉบับ (Cheats.MakeAppearArtifact) แทนที่จะประกอบ
            // struct เอง เพราะมันเติมของที่ client ต้องใช้เรนเดอร์ให้ครบ:
            //   Display.Parts["common"] = blueprint.DefaultLook   ← ไม่มีอันนี้ = ไม่มีโมเดล มองไม่เห็น
            //   States.BuildingState = Completed · States.Durability = เต็มหลอด
            //   Stories/AddOns ตาม component ของ blueprint
            AppearArtifact? made = Cheats.MakeAppearArtifact(
                new[] { "prop", type.ToString(), $"position:{tile.x},{tile.y}", $"size:{size.x},{size.y}" },
                out AddOns? addons);
            if (!made.HasValue)
            {
                Console.WriteLine($"[world] ⚠️ ไม่รู้จัก blueprint {type} — ข้าม {id}");
                continue;
            }

            AppearArtifact artifact = made.Value;
            // id ต้องเป็นของเรา (คงที่ตามลำดับในไฟล์) ไม่ใช่ Guid สุ่มที่ตัวสร้างแจกมา
            artifact.EntityId = id;
            artifact.Display.EntityId = id;
            artifact.IsAlive = true;
            ArtifactManager.AddArtifact(artifact);
            if (addons.HasValue)
            {
                ArtifactManager.PlaceAddOns(id, addons.Value._AddOns);
            }
            placed++;
        }

        if (placed > 0)
        {
            Save();
            Console.WriteLine($"[world] วางจุดสำคัญของเกาะ {placed} จุด (ท่าเรือ {pois.PortPoints.Count})");
        }
    }

    public void Process()
    {
        for (int num = _players.Count - 1; num >= 0; num--)
        {
            // ⚠️ ต้องหยิบตัวผู้เล่นเก็บไว้ก่อน ห้ามอ้าง _players[num] ซ้ำ:
            // Process() ทำให้ผู้เล่นหลุดออกจากลิสต์ได้ (คอนเนกชันปิด → event Closed → _players.Remove)
            // แล้ว _players[num] บรรทัดถัดมาจะหลุดขอบทันทีเมื่อ num == Count
            // (เคยทำเซิร์ฟดับมาแล้วจริง — ArgumentOutOfRangeException ใน WorldRegistry.ProcessAll)
            Player player = _players[num];
            player.Process();
            // สัตว์รอบตัว — ตัวเกมทำลายสัตว์ที่อยู่ไกลทิ้งเอง เซิร์ฟจึงต้องส่งใหม่ตอนเดินกลับเข้าระยะ
            // (ตัวมันเองหน่วงเวลาอยู่แล้ว ไม่ได้ทำงานจริงทุกเฟรม — ดู Player.Hunting.cs)
            player.SyncAnimalVisibility();
        }
    }

    public void Stop()
    {
        for (int num = _players.Count - 1; num >= 0; num--) _players[num].Stop();
        _players.Clear();
    }

    public void AddPlayer(Player player)
    {
        player.Closed += delegate
        {
            _players.Remove(player);
            PlayerDisappeared?.Invoke(player);
        };
        foreach (Player player2 in _players)
        {
            player.SendAppear(player2);
        }
        if (!string.IsNullOrEmpty(Weather))
        {
            player.Send(new Weather { _Weather = Weather });
        }
        _players.Add(player);
        PlayerAppeared?.Invoke(player);
    }

    public void BroadCast<T>(T msg)
    {
        foreach (Player player in _players)
        {
            player.Send(msg);
        }
    }

    public void Save() => _context.Save();

    private void ArtifactManager_ArtifactDisplayUpdated(ArtifactDisplay obj) => Save();

    private void ArtifactManager_ArtifactStateUpdated(ArtifactState state) => Save();

    private void AssignChunkData()
    {
        for (int i = 0; i < NumChunksX; i++)
        for (int j = 0; j < NumChunksY; j++)
        {
            _chunkData[i, j] = new ChunkData();
        }
        if (_terrainData.Landmarks != null)
        {
            var array = CreateByteMap(_terrainData.Landmarks, 16);
            if (array != null)
            {
                AggregateByteMap(array, 16, delegate (ChunkData chunk, byte[] bytes) { chunk.Landmarks = bytes; });
            }
        }
        byte[] garden = _terrainData.Garden;
        if (_context.Garden != null)
        {
            garden = _context.Garden;
        }
        if (garden == null) return;
        var list = (from g in NaturalInfo.FromBytes(garden)
            where _removedNatural.All(t => t.x != g.X || t.y != g.Y)
            select g).ToList();
        foreach (NaturalInfo natural in _addedNatural)
        {
            NaturalInfo naturalInfo = list.Find(t => t.X == natural.X && t.Y == natural.Y);
            if (naturalInfo != null) naturalInfo.EntityType = natural.EntityType;
            else list.Add(natural);
        }
        var array2 = CreateByteMap(NaturalInfo.ToBytes(list), 6);
        if (array2 != null)
        {
            AggregateByteMap(array2, 6, delegate (ChunkData chunk, byte[] bytes) { chunk.Garden = bytes; });
        }
    }

    private List<byte[]>[,] CreateByteMap(byte[] bytes, int stride)
    {
        if (bytes.Length % stride != 0)
        {
            Console.WriteLine("[world] Invalid byte size: " + bytes.Length);
            return null;
        }
        int num = bytes.Length / stride;
        var array = new List<byte[]>[NumChunksX, NumChunksY];
        for (int i = 0; i < num; i++)
        {
            int offset = i * stride;
            int x = BitConverter.ToUInt16(bytes, offset);
            int y = BitConverter.ToUInt16(bytes, offset + 2);
            Point2 point = Util.TilePositionToChunkCoords(new Point2(x, y));
            var list = array[point.x, point.y];
            if (list == null)
            {
                list = new List<byte[]>();
                array[point.x, point.y] = list;
            }
            var entry = new byte[stride];
            Array.Copy(bytes, offset, entry, 0, stride);
            list.Add(entry);
        }
        return array;
    }

    private void AggregateByteMap(List<byte[]>[,] byteMap, int stride, Action<ChunkData, byte[]> onFill)
    {
        for (int i = 0; i < NumChunksX; i++)
        for (int j = 0; j < NumChunksY; j++)
        {
            ChunkData arg = _chunkData[i, j];
            var list = byteMap[i, j];
            if (list == null) continue;
            var array = new byte[list.Count * stride];
            for (int k = 0; k < list.Count; k++)
            {
                Array.Copy(list[k], 0, array, k * stride, stride);
            }
            onFill(arg, array);
        }
    }

    public List<Chunk> CreateChunkMessages(int centerX, int centerY, ChunkVisit[,] chunkVisited)
    {
        var list = new List<Chunk>();
        for (int i = centerX - 1; i <= centerX + 1; i++)
        for (int j = centerY - 1; j <= centerY + 1; j++)
        {
            if (i >= 0 && i < NumChunksX && j >= 0 && j < NumChunksY && chunkVisited[i, j] == ChunkVisit.Visit)
            {
                list.Add(CreateChunk(i, j));
                chunkVisited[i, j] = ChunkVisit.Sent;
            }
        }
        return list;
    }

    public void ConstructArtifact(AppearArtifact artifact, AddOns? addon)
    {
        ArtifactManager.AddArtifact(artifact);
        if (addon.HasValue)
        {
            AppearArtifact? appearArtifact = ArtifactManager.PlaceAddOns(artifact.EntityId, addon.Value._AddOns);
            if (appearArtifact.HasValue) artifact = appearArtifact.Value;
        }
        OnArtifactAppeared(artifact);
        Save();
    }

    private void OnArtifactAppeared(AppearArtifact aa) => ArtifactAppeared?.Invoke(aa);

    public void DestructArtifact(string entityId)
    {
        AppearArtifact? appearArtifact = ArtifactManager.RemoveArtifact(entityId);
        if (appearArtifact.HasValue)
        {
            OnArtifactDisappeared(appearArtifact.Value);
            Save();
        }
    }

    private void OnArtifactDisappeared(AppearArtifact aa) => ArtifactDisappeared?.Invoke(aa);

    public void ExtendFloor(string entityId, bool withRoof)
    {
        AppearArtifact? appearArtifact = ArtifactManager.ExtendFloor(entityId, withRoof);
        if (appearArtifact.HasValue)
        {
            OnArtifactAppeared(appearArtifact.Value);
            Save();
        }
    }

    public void AddNatural(Point2 tile, ushort entityType)
    {
        if (AddNaturalToGarden(tile, entityType))
        {
            _removedNatural.Remove(tile);
            NaturalInfo naturalInfo = _addedNatural.Find(t => t.X == tile.x && t.Y == tile.y);
            if (naturalInfo != null) naturalInfo.EntityType = entityType;
            else
            {
                _addedNatural.Add(new NaturalInfo
                {
                    X = (ushort)tile.x,
                    Y = (ushort)tile.y,
                    EntityType = entityType
                });
            }
            NaturalAdded?.Invoke(Util.TilePositionToChunkCoords(tile), _chunkData[Util.TilePositionToChunkCoords(tile).x, Util.TilePositionToChunkCoords(tile).y].Garden);
            Save();
        }
    }

    private bool AddNaturalToGarden(Point2 tile, ushort entityType)
    {
        Point2 point = Util.TilePositionToChunkCoords(tile);
        if (point.x < 0 || point.x >= NumChunksX || point.y < 0 || point.y >= NumChunksY) return false;
        if (!DataHelper.IsNaturalObject(entityType)) return false;
        bool replaced = false;
        byte[] garden = _chunkData[point.x, point.y].Garden;
        var list = garden != null ? NaturalInfo.FromBytes(garden).ToList() : new List<NaturalInfo>();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].X == tile.x && list[i].Y == tile.y)
            {
                list[i].EntityType = entityType;
                replaced = true;
                break;
            }
        }
        if (!replaced)
        {
            list.Add(new NaturalInfo { X = (ushort)tile.x, Y = (ushort)tile.y, EntityType = entityType });
        }
        _chunkData[point.x, point.y].Garden = NaturalInfo.ToBytes(list);
        return true;
    }

    public void DestroyNatural(Point2 tile)
    {
        if (RemoveNaturalFromGarden(tile))
        {
            if (!_removedNatural.Contains(tile)) _removedNatural.Add(tile);
            int num = _addedNatural.FindIndex(t => t.X == tile.x && t.Y == tile.y);
            if (num != -1) _addedNatural.RemoveAt(num);
            NaturalDestroyed?.Invoke(tile);
            Save();
        }
    }

    private bool RemoveNaturalFromGarden(Point2 tile)
    {
        Point2 point = Util.TilePositionToChunkCoords(tile);
        if (point.x < 0 || point.x >= NumChunksX || point.y < 0 || point.y >= NumChunksY) return false;
        if (_chunkData[point.x, point.y].Garden == null) return false;
        var list = NaturalInfo.FromBytes(_chunkData[point.x, point.y].Garden).ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].X == tile.x && list[i].Y == tile.y)
            {
                list.RemoveAt(i);
                break;
            }
        }
        _chunkData[point.x, point.y].Garden = NaturalInfo.ToBytes(list);
        return true;
    }

    public DefoggedChunks CreateDefoggedChunks()
    {
        DefoggedChunks result = default;
        int num = NumChunksX * NumChunksY;
        result.Chunks = new Point2[num];
        for (int i = 0; i < NumChunksX; i++)
        for (int j = 0; j < NumChunksY; j++)
        {
            int num2 = j * NumChunksX + i;
            result.Chunks[num2] = new Point2(i, j);
        }
        return result;
    }

    public byte[] GetChunkBiomes(Point2 pos)
    {
        var array = new byte[324];
        CopyChunk(pos.x, pos.y, _terrainData.Biomes, array, 1, 1, 1);
        return array;
    }

    public byte[] GetChunkOcean(Point2 pos)
    {
        var array = new byte[289];
        CopyChunk(pos.x, pos.y, _terrainData.Ocean, array, 1, 0, 1);
        return array;
    }

    public byte[] GetChunkRiver(Point2 pos)
    {
        var array = new byte[867];
        CopyChunk(pos.x, pos.y, _terrainData.Rivers, array, 3, 0, 1);
        return array;
    }

    public byte[] GetChunkLandmark(Point2 pos) => _chunkData[pos.x, pos.y].Landmarks;

    private Chunk CreateChunk(int chunkX, int chunkY)
    {
        Chunk result = default;
        result._Chunk = new Point2(chunkX, chunkY);
        result.Garden = _chunkData[chunkX, chunkY].Garden;
        if (result.Garden == null) result.Garden = new byte[0];
        return result;
    }

    public void ChangeWeather(string weather)
    {
        if (Weather == weather) return;
        Weather = weather;
        BroadCast(new Weather { _Weather = weather });
    }

    public List<Pet> GetGrazedPets() => _context.GrazedPetList;

    private static void CopyChunk(int chunkX, int chunkY, byte[] src, byte[] dst, int count, int prevOffset, int postOffset)
    {
        int num = chunkX * 16;
        int num2 = chunkY * 16;
        int num3 = (int)Math.Sqrt((double)src.Length / count);
        for (int i = -prevOffset; i < 16 + postOffset; i++)
        for (int j = -prevOffset; j < 16 + postOffset; j++)
        {
            int num4 = Math.Clamp(num + i, 0, num3 - 1);
            int num5 = Math.Clamp(num2 + j, 0, num3 - 1);
            int num6 = num4 + num5 * num3;
            num6 *= count;
            int num7 = i + prevOffset;
            int num8 = j + prevOffset;
            int num9 = 16 + postOffset + prevOffset;
            int num10 = num7 + num8 * num9;
            num10 *= count;
            for (int k = 0; k < count; k++)
            {
                dst[num10 + k] = src[num6 + k];
            }
        }
    }
}
