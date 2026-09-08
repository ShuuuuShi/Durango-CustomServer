using System;
using Durango.Utils;
using System.Collections.Generic;
using System.Linq;
using Durango.Terrain;
using Shared.Building;
using Messages;
using Shared.Estate;
using UnityEngine;
using Yaml.Util;
using Durango.Utils.Extensions;

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

    /// <summary>คิวรื้อสิ่งปลูกสร้างที่ตั้งเวลาไว้ — (เวลาที่ครบ, entityId) drain ใน Process (main-thread)
    /// เก็บในหน่วยความจำอย่างเดียว ไม่เซฟ: destruct จบใน 5-11 วิ ถ้าเซิร์ฟรีสตาร์ตกลางคัน หลังแค่ไม่ถูกรื้อ (ปลอดภัย)</summary>
    private readonly List<(double Due, string EntityId)> _pendingDestructs = new();

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

    /// <summary>registry ที่ถือโลกนี้ — ตั้งโดย WorldRegistry.GetOrCreate</summary>
    public WorldRegistry Registry { get; set; }


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
        ArtifactManager = new ArtifactManager(_context.Artifacts, _context.ArtifactAddOns,
            _context.ArtifactMannequins, _context.Plantings, _context.ArtifactOwners,
            _context.BuildMaterials);
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

        // (id, ชนิด, ขนาด footprint, ช่อง) — ชนิดจาก data/assets/entity_types/artifact.json
        // ขนาดอ่านจากไฟล์เดียวกัน (ฟิลด์ size) ไม่ฝังเลขไว้ในโค้ด: ท่าเรือ 3x3 · รูวาร์ป 6x6 ·
        // แท่งเร่งวาร์ป 4x4 · หลุมอุกกาบาต 4x4 — ตรงกับที่เคยฝังไว้ทุกตัว แต่ถ้าข้อมูลเปลี่ยนก็ตามได้เอง
        var wanted = new List<(string Id, ushort Type, Point2 Size, Point2 Tile)>();
        for (int i = 0; i < pois.PortPoints.Count; i++)
        {
            wanted.Add(($"poi_port_{i}", (ushort)7001, SizeOf(7001, 3), pois.PortPoints[i]));       // dock 항구
        }
        for (int i = 0; i < pois.Warpholes.Count; i++)
        {
            wanted.Add(($"poi_warphole_{i}", (ushort)9450, SizeOf(9450, 6), pois.Warpholes[i]));    // neutral_warphole 중립 워프홀
        }
        for (int i = 0; i < pois.Rifts.Count; i++)
        {
            wanted.Add(($"poi_rift_{i}", (ushort)6282, SizeOf(6282, 4), pois.Rifts[i]));            // warp_accelerator 균열
        }
        // หลุมอุกกาบาต — เดิมถูกยัดรวมกับ Rifts จึงวางเป็นแท่งเร่งวาร์ปผิดชนิดมาตลอด
        // (เหตุผลเต็มที่ Support/TerrainPois.Craters)
        for (int i = 0; i < pois.Craters.Count; i++)
        {
            wanted.Add(($"poi_crater_{i}", (ushort)7037, SizeOf(7037, 4), pois.Craters[i]));        // crack_01 닫힌 크레이터
        }

        RemoveStaleTerrainPois(wanted);

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
            artifact.States.EntityId = id;
            artifact.IsAlive = true;
            if (type == 7037) artifact.States.Crack = MakeClosedCrack(RegionLevel);
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
            Console.WriteLine($"[world] วางจุดสำคัญของเกาะ {placed} จุด " +
                              $"(ท่าเรือ {pois.PortPoints.Count} · รูวาร์ป {pois.Warpholes.Count} · " +
                              $"รอยแยก {pois.Rifts.Count} · หลุมอุกกาบาต {pois.Craters.Count})");
        }
    }

    /// <summary>
    /// ลบจุดสำคัญค้างที่ไม่ตรงกับ pois.yml อีกต่อไป
    ///
    /// ═══ ทำไมต้องมี ═══
    /// id ของจุดสำคัญผูกกับ "ลำดับในไฟล์" (<c>poi_rift_0</c>, <c>poi_rift_1</c>, …) เพื่อไม่ให้
    /// เปิดเซิร์ฟรอบสองแล้ววางซ้อนเพิ่มทุกรอบ — แต่ถ้าลำดับเปลี่ยน ของเดิมจะกลายเป็นขยะทันที
    /// เกิดขึ้นจริงตอนแก้บั๊กหลุมอุกกาบาต (ดู Support/TerrainPois.Craters): เกาะที่เคยเปิดไว้มี
    /// <c>poi_rift_0..5</c> โดย 4 อันแรกยืนอยู่บนช่องของหลุมอุกกาบาต พอแยกสองอย่างออกจากกัน
    /// รายการรอยแยกจริงเหลือ 2 ⇒ <c>poi_rift_4</c>, <c>poi_rift_5</c> กลายเป็นของซ้ำบนช่องเดียวกัน
    ///
    /// กติกา: <c>poi_&lt;ชนิด&gt;_&lt;i&gt;</c> จะอยู่ต่อได้ก็ต่อเมื่อรายการที่ต้องวางรอบนี้มีตัวนั้น
    /// **และช่องตรงกัน** — ที่เหลือคือของค้างจากลำดับเก่า ลบทิ้ง
    /// (ใช้กับทุกชนิด ไม่เจาะจงรอยแยก ⇒ ถ้าไฟล์เกาะเปลี่ยนทีหลังก็ซ่อมตัวเองได้)
    /// </summary>
    private void RemoveStaleTerrainPois(List<(string Id, ushort Type, Point2 Size, Point2 Tile)> wanted)
    {
        var expected = new Dictionary<string, Point2>(wanted.Count);
        foreach ((string id, ushort _, Point2 _, Point2 tile) in wanted) expected[id] = tile;

        var stale = new List<string>();
        foreach (var pair in _context.Artifacts)
        {
            if (!pair.Key.StartsWith("poi_", StringComparison.Ordinal)) continue;
            if (!expected.TryGetValue(pair.Key, out Point2 tile)
                || pair.Value.Tile.x != tile.x || pair.Value.Tile.y != tile.y)
            {
                stale.Add(pair.Key);
            }
        }
        if (stale.Count == 0) return;

        foreach (string id in stale) ArtifactManager.RemoveArtifact(id);
        Console.WriteLine($"[world] ลบจุดสำคัญค้างที่ไม่ตรงกับไฟล์เกาะแล้ว {stale.Count} จุด: " +
                          string.Join(", ", stale));
    }

    /// <summary>ขนาด footprint จาก entity_types/artifact.json — ถอยไปค่าสำรองถ้าไฟล์ไม่มีชนิดนี้</summary>
    private static Point2 SizeOf(int entityType, int fallback)
    {
        int[] size = SingletonDict<int, Yaml.ArtifactPrototype>.Instance?.Get(entityType)?.size;
        return size is { Length: >= 2 } && size[0] > 0 && size[1] > 0
            ? new Point2(size[0], size[1])
            : new Point2(fallback, fallback);
    }

    /// <summary>
    /// สถานะเริ่มต้นของหลุมอุกกาบาตที่ "ยังปิดอยู่" — ยังไม่เปิดใช้ ยังไม่มีใครลงหินนำทาง
    ///
    /// ⚠️ ไม่ตั้งช่องนี้ = ฝั่งเกมไม่นับว่าเป็นจุดสำคัญเลย เพราะ POIUpdater.cs:122 คัดด้วย
    /// <c>artifact.ArtifactState.Crack.HasValue</c> ⇒ ไม่มีหมุดบนแผนที่ ไม่มีป้ายข้อมูล
    ///
    /// ค่าที่ใส่มาจาก data/assets/constants.json → crack ทั้งหมด ไม่ได้ตั้งเอง:
    ///   required_investment = "max(1, int(level * 0.2))"   (คิดที่เลเวลของหลุม)
    /// ส่วน PotentialBiocoms (รายชื่อ "군락ที่วาร์ปมาได้") ปล่อย null เพราะตารางชื่อของ biocom
    /// ไม่ได้อยู่ในไฟล์ที่สกัดออกมา — ฝั่งเกมเจอ null แล้วซ่อนหัวข้อนั้นไปเอง
    /// (client/ArtifactInfoMainWidget.cs:610-612) ดีกว่าเดาชื่อขึ้นมาเอง
    /// </summary>
    /// <summary>
    /// เลเวลของเกาะนี้ — จาก <c>region_templates.json → level</c> (ri35de = 35)
    /// ใช้กับสูตรที่มีตัวแปร level ระดับเกาะ เช่นหินนำทางที่ต้องใช้เปิดหลุมอุกกาบาต
    /// </summary>
    private int RegionLevel =>
        RegionCatalog.GetTemplate(_terrainData.Info?.region_template)?.Level ?? 1;

    private static Crack MakeClosedCrack(int level)
    {
        var vars = new System.Collections.Generic.Dictionary<string, double> { ["level"] = level };
        int required = StatFormula.TryEval(CrackTuning.RequiredInvestment, vars, out double value)
            ? Math.Max(1, (int)value)
            : 1;
        return new Crack
        {
            ActivatedSince = null,      // ยังไม่เปิด
            ActivatedUntil = null,
            CurrentInvestment = 0,
            RequiredInvestment = required,
            InvestmentUnit = 1,
            PotentialBiocoms = null
        };
    }

    /// <summary>
    /// สำเนารายชื่อผู้เล่นในโลกนี้ — คืนสำเนาเสมอ ไม่ใช่ลิสต์จริง
    ///
    /// เพราะการเตะ/ประกาศทำให้ผู้เล่นหลุดออกจากลิสต์ระหว่างวน (Closed → _players.Remove)
    /// ⇒ วนลิสต์จริงแล้วแก้ไปด้วยจะพัง (เคยทำเซิร์ฟดับมาแล้วที่ Process — ดูคอมเมนต์ข้างล่าง)
    /// </summary>
    public List<Player> PlayersSnapshot() => new(_players);

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

        // รื้อสิ่งปลูกสร้างที่ครบเวลาแล้ว — นอกลูปผู้เล่น + ไม่ผูกกับ players.Count เพื่อให้จบแม้คนสุดท้ายออกไป
        ProcessDestructs(Gauge.CurrentTime);

        // สัตว์เดินเล่น — ต้องอยู่นอกลูปผู้เล่น เพราะเป็นเรื่องของสัตว์ ไม่ใช่ของใครคนใดคนหนึ่ง
        // (ถ้าไม่มีใครอยู่บนเกาะก็ไม่ต้องเดิน จะได้ไม่เปลืองแรงเปล่า)
        if (_players.Count > 0)
        {
            AnimalManager?.Process(Gauge.CurrentTime, BroadCast, OnCorpseDisposed);
            ProcessWeather(Gauge.CurrentTime);
            ArtifactManager?.ProcessFarming(Gauge.CurrentTime);
            ProcessRegrow(Gauge.CurrentTime);
        }
    }

    // ── สภาพอากาศ ───────────────────────────────────────────────────────────────────

    private IReadOnlyList<string> _weatherSequence;
    private int _weatherStep = -1;
    private double _nextWeatherAt;

    /// <summary>
    /// หมุนสภาพอากาศของเกาะตามลำดับของภูมิอากาศเกาะนี้
    ///
    /// ⚠️ ไม่มีตัวนี้ = <c>Weather</c> ว่างตลอด ⇒ <c>SendInitialState</c> ข้ามการส่งไปเลย
    /// ⇒ ฝั่งเกมไม่เคยได้รับ <c>Weather</c>(2028) ⇒ **ท้องฟ้าแจ่มใสตลอดกาลทุกเกาะ**
    /// ที่มาของลำดับกับจังหวะ ดูที่ <see cref="WeatherTuning"/> (ชื่อสถานการณ์เป็นข้อมูลจริง
    /// ของ NEXON · ลำดับกับจังหวะเป็นของเรา เพราะไฟล์ไม่ได้บอกไว้)
    /// </summary>
    private void ProcessWeather(double now)
    {
        _weatherSequence ??= WeatherTuning.SequenceFor(
            RegionCatalog.GetTemplate(_terrainData.Info?.region_template)?.Weather);
        if (_weatherSequence.Count == 0) return;
        if (_weatherStep >= 0 && now < _nextWeatherAt) return;

        _weatherStep = (_weatherStep + 1) % _weatherSequence.Count;
        _nextWeatherAt = now + WeatherTuning.CycleSeconds;
        ChangeWeather(_weatherSequence[_weatherStep]);
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
            // ถอด event ให้แน่ใจอีกชั้น — Player.Detach() เรียกซ้ำได้ไม่พัง
            player.Detach();
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
            // ผู้เล่นเพิ่งเข้าเกาะ — ใส่ SE จากอากาศปัจจุบันด้วย (ไม่งั้นต้องรอรอบหมุนถัดไป)
            player.SyncWeatherStatusEffects(Weather);
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

    /// <summary>
    /// [7 ก.ย. 2026] ซากครบเวลาแล้วและสัตว์ตัวนั้นคืนชีพที่จุดเกิด — บอกฝั่งเกมให้อัปเดตตาม
    ///
    /// ต้องทำสามอย่างครบ ไม่งั้นเห็นผลครึ่ง ๆ:
    ///   1. DisappearEntity — ลบซากออกจากจอ (ไม่ส่ง = ศพค้างอยู่ทั้งที่เซิร์ฟถือว่าฟื้นแล้ว)
    ///   2. ล้างประวัติการแล่ — ไม่ล้าง = ตัวที่เกิดใหม่แล่ไม่ได้เลยเพราะระบบจำว่าเก็บครบแล้ว
    ///   3. ให้ทุกคนลืมว่าเคยเห็นตัวนี้ — SyncAnimalVisibility จะได้ส่ง AppearAnimal ตัวใหม่ให้
    /// </summary>
    private void OnCorpseDisposed(AnimalManager.Animal animal)
    {
        BroadCast(new DisappearEntity { EntityId = animal.EntityId });
        ForgetHarvests(animal.EntityId);
        foreach (Player player in _players)
        {
            player.ForgetAnimal(animal.EntityId);
        }
        Console.WriteLine($"[สัตว์] ซาก {animal.EntityId} หายไปแล้ว — เกิดใหม่ที่ " +
                          $"[{animal.HomeTile.x},{animal.HomeTile.y}]");
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

    public void ConstructArtifact(AppearArtifact artifact, AddOns? addon, string ownerEntityId = null)
    {
        ArtifactManager.AddArtifact(artifact);
        // จำว่าใครสร้าง — ไม่จำ = ไม่มีใครเป็นเจ้าของ แล้วรื้อไม่ได้ (ดู WorldContext.ArtifactOwners)
        ArtifactManager.SetOwner(artifact.EntityId, ownerEntityId);
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

    /// <summary>ตั้งเวลารื้อสิ่งปลูกสร้างอีก <paramref name="seconds"/> วินาที (ระหว่างนั้น client เล่นหลอด+ท่าทุบ)</summary>
    public void ScheduleDestruct(string entityId, double seconds)
    {
        if (string.IsNullOrEmpty(entityId)) return;
        double due = Gauge.CurrentTime + seconds;
        // ช่องเดิมที่ค้างคิวอยู่แล้ว ไม่ซ้ำ — เอาเวลาที่ครบก่อน (กันกดรัว)
        int i = _pendingDestructs.FindIndex(e => e.EntityId == entityId);
        if (i >= 0)
        {
            if (due < _pendingDestructs[i].Due) _pendingDestructs[i] = (due, entityId);
            return;
        }
        _pendingDestructs.Add((due, entityId));
    }

    /// <summary>ลบหลังที่ครบเวลาแล้ว — เรียกจาก Process (main-thread) เหมือน ProcessRegrow</summary>
    private void ProcessDestructs(double now)
    {
        if (_pendingDestructs.Count == 0) return;
        for (int i = _pendingDestructs.Count - 1; i >= 0; i--)
        {
            if (now < _pendingDestructs[i].Due) continue;
            string entityId = _pendingDestructs[i].EntityId;
            _pendingDestructs.RemoveAt(i);
            DestructArtifact(entityId);   // ลบจริง + broadcast ArtifactDisappeared + Save()
        }
    }

    public void ExtendFloor(string entityId, bool withRoof)
    {
        AppearArtifact? appearArtifact = ArtifactManager.ExtendFloor(entityId, withRoof);
        if (appearArtifact.HasValue)
        {
            OnArtifactAppeared(appearArtifact.Value);
            Save();
        }
    }

    // ══ ระบบนิเวศ: ของธรรมชาติงอกกลับ ══════════════════════════════════════════
    //
    // ต้นฉบับฝั่ง offline ไม่มีระบบนี้ (AddNatural ถูกเรียกจาก cheat จุดเดียว — เหมือนกันทั้ง
    // nexonSRC/Durango.Offline/Player.cs:624 และของเรา Core/Player.cs:1013)
    // แต่เซิร์ฟจริงของ NEXON มี — ดูคอมเมนต์ที่ GameCode/Durango.Online/Connection.cs:159
    // "เจอจริง: natural regrowth ส่ง AppearEntityOnTile"
    //
    // ท่อส่งข่าวมีครบอยู่แล้ว: AddNatural → NaturalAdded → Core/Player.cs:89 ส่ง GardenDiff(202)
    // ให้ทุกคนที่อยู่ในโลก ⇒ ที่ขาดคือ "ตัวสั่งให้งอก" เท่านั้น

    /// <summary>จองคิวให้ของธรรมชาติงอกกลับที่ช่องเดิม</summary>
    private void ScheduleRegrow(Point2 tile, ushort entityType)
    {
        if (entityType == 0) return;                       // ไม่รู้ว่าเดิมเป็นอะไร — งอกกลับไม่ได้
        double seconds = WorldTuning.NaturalRegrowSeconds; // ปรับได้ที่ config.json → World
        if (seconds <= 0) return;                          // ตั้ง 0 = ปิดระบบงอกกลับ

        List<NaturalRegrowEntry> queue = _context.NaturalRegrow;
        if (queue == null) return;
        // ช่องเดิมที่ค้างคิวอยู่แล้ว ให้ทับของเดิม (กันคิวบวมตอนเก็บ-งอก-เก็บซ้ำที่เดิม)
        NaturalRegrowEntry entry = queue.Find(e => e.X == tile.x && e.Y == tile.y);
        if (entry == null)
        {
            entry = new NaturalRegrowEntry { X = tile.x, Y = tile.y };
            queue.Add(entry);
        }
        entry.EntityType = entityType;
        entry.DueAt = Gauge.CurrentTime + seconds;
    }

    /// <summary>
    /// ถึงเวลาแล้วก็ปลูกกลับ — เรียกจาก <see cref="Process"/> ทุกเฟรม (งานจริงน้อยมาก
    /// เพราะคิวว่างเกือบตลอด และ AddNatural เรียกเฉพาะตอนถึงกำหนดจริง)
    /// </summary>
    private void ProcessRegrow(double now)
    {
        List<NaturalRegrowEntry> queue = _context.NaturalRegrow;
        if (queue == null || queue.Count == 0) return;
        for (int i = queue.Count - 1; i >= 0; i--)
        {
            NaturalRegrowEntry entry = queue[i];
            if (entry == null) { queue.RemoveAt(i); continue; }
            if (now < entry.DueAt) continue;
            queue.RemoveAt(i);
            var tile = new Point2(entry.X, entry.Y);
            AddNatural(tile, entry.EntityType);   // broadcast GardenDiff ให้เอง + Save()
            Console.WriteLine($"[นิเวศ] ของธรรมชาติชนิด {entry.EntityType} งอกกลับที่ ({entry.X},{entry.Y})");
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

    private static readonly List<string> NoHarvest = new();

    /// <summary>
    /// generator ที่ถูกเก็บไปแล้วของเป้าหนึ่งชิ้น
    /// คีย์: "x,y" สำหรับของธรรมชาติ · entity id สำหรับซากสัตว์ (ดู Player.Gathering.cs HarvestKeyOf)
    /// </summary>
    public IReadOnlyList<string> HarvestedGenerators(string targetKey)
        => _context.NaturalHarvests.TryGetValue(targetKey, out List<string> list) ? list : NoHarvest;

    /// <summary>
    /// บันทึกว่าเก็บ generator ตัวนี้ไปอีกหนึ่งครั้ง
    ///
    /// ⚠️ ใส่ซ้ำได้ตั้งใจ — จำนวนที่ซ้ำ = จำนวนครั้งที่เก็บไปแล้ว (ดู WorldContext.NaturalHarvests)
    /// เดิมกันไม่ให้ซ้ำ ⇒ generator ตัวนั้นถูกตัดออกจากเมนูตั้งแต่เก็บครั้งแรก
    /// </summary>
    public void MarkGeneratorHarvested(string targetKey, string generatorId)
    {
        if (!_context.NaturalHarvests.TryGetValue(targetKey, out List<string> list))
        {
            list = new List<string>();
            _context.NaturalHarvests[targetKey] = list;
        }
        list.Add(generatorId);
    }

    public void ForgetHarvests(string targetKey) => _context.NaturalHarvests.Remove(targetKey);

    public void DestroyNatural(Point2 tile)
    {
        if (RemoveNaturalFromGarden(tile, out ushort removedType))
        {
            // [7 ก.ย. 2026] ระบบนิเวศ — จองคิวให้ของงอกกลับที่เดิม
            // ไม่ทำ = เก็บของแล้วช่องนั้นว่างถาวร ของหมดเกาะไปเรื่อย ๆ
            ScheduleRegrow(tile, removedType);
            // ของหายจากโลกแล้ว ประวัติการเก็บไม่มีความหมายอีกต่อไป
            // ⚠️ ไม่ล้าง = ช่องนี้งอกของใหม่ขึ้นมาแล้วเก็บไม่ได้เลยสักตัว
            _context.NaturalHarvests.Remove($"{tile.x},{tile.y}");
            if (!_removedNatural.Contains(tile)) _removedNatural.Add(tile);
            int num = _addedNatural.FindIndex(t => t.X == tile.x && t.Y == tile.y);
            if (num != -1) _addedNatural.RemoveAt(num);
            NaturalDestroyed?.Invoke(tile);
            Save();
        }
    }

    private bool RemoveNaturalFromGarden(Point2 tile, out ushort removedType)
    {
        removedType = 0;
        Point2 point = Util.TilePositionToChunkCoords(tile);
        if (point.x < 0 || point.x >= NumChunksX || point.y < 0 || point.y >= NumChunksY) return false;
        if (_chunkData[point.x, point.y].Garden == null) return false;
        var list = NaturalInfo.FromBytes(_chunkData[point.x, point.y].Garden).ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].X == tile.x && list[i].Y == tile.y)
            {
                // จำชนิดไว้ให้ระบบนิเวศงอกกลับเป็นตัวเดิม (ดู ScheduleRegrow)
                removedType = list[i].EntityType;
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

    /// <summary>
    /// ไบโอมของช่องนั้น — อ่านจากตาราง whole.biomes ของ terrain โดยตรง
    ///
    /// การจัดเรียงเป็น <c>x + y * width</c> ยืนยันจาก <see cref="CopyChunk"/> ที่ใช้สูตรเดียวกัน
    /// (ตัวนั้นส่งข้อมูลไบโอมให้ฝั่งเกมวาดพื้นอยู่แล้ว ⇒ ถ้าสูตรผิด พื้นดินจะเพี้ยนตั้งแต่แรก)
    /// </summary>
    public Shared.Region.Biome BiomeAt(Point2 tile)
    {
        byte[] biomes = _terrainData.Biomes;
        if (biomes == null || biomes.Length == 0) return Shared.Region.Biome.Invalid;

        int width = (int)Math.Sqrt(biomes.Length);
        if (width <= 0) return Shared.Region.Biome.Invalid;
        int x = Math.Clamp(tile.x, 0, width - 1);
        int y = Math.Clamp(tile.y, 0, width - 1);

        int index = x + y * width;
        return index >= 0 && index < biomes.Length
            ? (Shared.Region.Biome)biomes[index]
            : Shared.Region.Biome.Invalid;
    }

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
        // ⚠️ ชื่อนอกชุดที่ฝั่งเกมรู้จักทำให้มัน Debug.LogError แล้วค้างที่ Weather.Invalid
        // (client/Durango.Environment/WeatherManager.cs:148-150) — กันไว้ที่ต้นทางดีกว่า
        if (!WeatherTuning.IsKnown(weather))
        {
            Console.WriteLine($"[อากาศ] ⚠️ ไม่รู้จักสภาพอากาศ '{weather}' — ไม่เปลี่ยน");
            return;
        }
        Weather = weather;
        BroadCast(new Weather { _Weather = weather });
        // [7 ก.ย. 2026] ซิงก์บัพ/ดีบัพจากอากาศให้ทุกคนบนเกาะ (ฝน→wet · ภูเขาไฟ→volcanic_*)
        foreach (Player player in _players)
        {
            player.SyncWeatherStatusEffects(weather);
        }
        Console.WriteLine($"[อากาศ] {TerrainId} → {weather}");
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

    public const int EstateGridSize = 4;

    public static string EstateCellKey(int cx, int cy) => cx + "," + cy;

    public static Point2 TileFromCell(Point2 cell) => new Point2(cell.x * EstateGridSize, cell.y * EstateGridSize);

    /// <summary>ช่องที่ดิน 4×4 ที่ครอบคลุม tile นี้ — หารลงสู่ลบด้วย (ไม่ใช่ตัดเข้าหาศูนย์)</summary>
    public static Point2 CellFromTile(Point2 tile) =>
        new Point2(FloorDiv(tile.x, EstateGridSize), FloorDiv(tile.y, EstateGridSize));

    private static int FloorDiv(int value, int divisor)
    {
        int q = value / divisor;
        if (value < 0 && value % divisor != 0) q--;
        return q;
    }

    /// <summary>
    /// footprint ทั้งก้อนอยู่บนที่ดิน (ส่วนตัว / เมือง / แคลน / ระบบ) หรือไม่
    ///
    /// ค่าเก็บแคปซูลใน constants แยก <c>inside</c>/<c>outside</c> ตามที่ดิน
    /// (client/UITable.cs WarningEstateOut: นอกที่ดิน/แคลนเก็บแล้วคิดเงิน)
    /// ถ้าแม้ช่องเดียวอยู่นอกที่ดิน = นอก — ตรงกับที่เกมเตือนว่าคร่อมเขตแล้วไม่มีกรรมสิทธิ์
    /// </summary>
    public bool IsFootprintOnEstate(Point2 tile, Point2 size)
    {
        int width = Math.Max(1, size.x);
        int height = Math.Max(1, size.y);
        for (int dx = 0; dx < width; dx++)
        {
            for (int dy = 0; dy < height; dy++)
            {
                Point2 cell = CellFromTile(new Point2(tile.x + dx, tile.y + dy));
                if (!TryGetEstateIdAtCell(cell, out _)) return false;
            }
        }
        return true;
    }

    public EstateRecord GetEstate(string estateId)
    {
        if (string.IsNullOrEmpty(estateId) || _context.Estates == null) return null;
        return _context.Estates.TryGetValue(estateId, out EstateRecord rec) ? rec : null;
    }

    public IEnumerable<KeyValuePair<string, EstateRecord>> EnumerateEstates()
    {
        if (_context.Estates == null) yield break;
        foreach (var kv in _context.Estates) yield return kv;
    }

    public IEnumerable<EstateRecord> EstatesOfOwner(string ownerId)
    {
        if (_context.Estates == null || string.IsNullOrEmpty(ownerId)) yield break;
        foreach (KeyValuePair<string, EstateRecord> kv in _context.Estates)
        {
            if (string.Equals(kv.Value.OwnerId, ownerId, StringComparison.Ordinal))
            {
                yield return kv.Value;
            }
        }
    }

    public bool TryGetEstateIdAtCell(Point2 cell, out string estateId)
    {
        estateId = null;
        if (_context.EstateCells == null) return false;
        return _context.EstateCells.TryGetValue(EstateCellKey(cell.x, cell.y), out estateId);
    }

    public EstateLicense ToLicense(string estateId, EstateRecord rec)
    {
        return new EstateLicense
        {
            EstateId = estateId,
            Type = (Shared.Estate.OwnerType)rec.Type,
            OwnerId = rec.OwnerId,
            ActivatedAt = rec.ActivatedAt,
            ExpiresAt = rec.ExpiresAt,
            Size = rec.Size,
            RegionId = rec.RegionId,
            Tile = new Point2(rec.TileX, rec.TileY),
            AccessRights = rec.AccessForOthers.HasValue
                ? new Messages.AccessRights { ForOthers = (Shared.Estate.AccessRights)rec.AccessForOthers.Value }
                : null
        };
    }

    /// <summary>ประกาศที่ดิน 1 ช่อง — คืน license หรือ null ถ้าช่องถูกจองแล้ว/ผิดเงื่อนไข</summary>
    public EstateLicense? DeclareEstate(string ownerId, Shared.Estate.OwnerType type, Point2 cell, string regionId)
    {
        _context.Estates ??= new Dictionary<string, EstateRecord>();
        _context.EstateCells ??= new Dictionary<string, string>();
        string key = EstateCellKey(cell.x, cell.y);
        if (_context.EstateCells.ContainsKey(key))
        {
            return null;
        }
        // ผู้เล่นหนึ่งคนต่อประเภท หนึ่งแปลงก่อน (ง่าย/ชัด) — เมืองกับส่วนตัวแยกกันได้
        foreach (KeyValuePair<string, EstateRecord> kv in _context.Estates)
        {
            if (kv.Value.OwnerId == ownerId && kv.Value.Type == (int)type)
            {
                return null;
            }
        }
        string estateId = Guid.NewGuid().ToString("N");
        Point2 tile = TileFromCell(cell);
        var rec = new EstateRecord
        {
            Type = (int)type,
            OwnerId = ownerId,
            ActivatedAt = Times.UnixTimeNow(),
            ExpiresAt = null,
            Size = 1,
            RegionId = regionId,
            TileX = tile.x,
            TileY = tile.y,
            Cells = new List<string> { key },
            AccessForOthers = 0
        };
        _context.Estates[estateId] = rec;
        _context.EstateCells[key] = estateId;
        Save();
        return ToLicense(estateId, rec);
    }

    public EstateLicense? ExpandEstate(string estateId, string ownerId, Point2 cell, int maxSize)
    {
        if (!_context.Estates.TryGetValue(estateId, out EstateRecord rec)) return null;
        if (rec.OwnerId != ownerId) return null;
        if (rec.Size >= maxSize) return null;
        string key = EstateCellKey(cell.x, cell.y);
        if (_context.EstateCells.ContainsKey(key)) return null;
        // ต้องติดกับเซลล์เดิม
        bool adjacent = false;
        foreach (string existing in rec.Cells)
        {
            string[] parts = existing.Split(',');
            int ex = int.Parse(parts[0]);
            int ey = int.Parse(parts[1]);
            if (Math.Abs(ex - cell.x) + Math.Abs(ey - cell.y) == 1)
            {
                adjacent = true;
                break;
            }
        }
        if (!adjacent) return null;
        rec.Cells.Add(key);
        rec.Size = rec.Cells.Count;
        _context.EstateCells[key] = estateId;
        Save();
        return ToLicense(estateId, rec);
    }

    public EstateLicense? ShrinkEstate(string estateId, string ownerId, Point2 cell)
    {
        if (!_context.Estates.TryGetValue(estateId, out EstateRecord rec)) return null;
        if (rec.OwnerId != ownerId) return null;
        string key = EstateCellKey(cell.x, cell.y);
        if (!rec.Cells.Contains(key)) return null;
        if (rec.Cells.Count <= 1) return null; // เหลือช่องเดียวให้ใช้ Remove
        rec.Cells.Remove(key);
        rec.Size = rec.Cells.Count;
        _context.EstateCells.Remove(key);
        Save();
        return ToLicense(estateId, rec);
    }

    public bool RemoveEstate(string estateId, string ownerId)
    {
        if (!_context.Estates.TryGetValue(estateId, out EstateRecord rec)) return false;
        if (rec.OwnerId != ownerId) return false;
        foreach (string key in rec.Cells)
        {
            _context.EstateCells.Remove(key);
        }
        _context.Estates.Remove(estateId);
        Save();
        return true;
    }

    public EstateGrids BuildEstateGridsForChunks(IEnumerable<Point2> chunks)
    {
        var chunkList = new List<Point2>(chunks);
        var cells = new Dictionary<Point2, string>();
        var licenses = new Dictionary<string, EstateLicense>();
        if (_context.EstateCells != null && _context.Estates != null)
        {
            var chunkSet = new HashSet<string>();
            foreach (Point2 ch in chunkList)
            {
                chunkSet.Add(ch.x + "," + ch.y);
            }
            foreach (KeyValuePair<string, string> kv in _context.EstateCells)
            {
                string[] parts = kv.Key.Split(',');
                int cx = int.Parse(parts[0]);
                int cy = int.Parse(parts[1]);
                // cell -> tile -> chunk (16 tiles)
                int tileX = cx * EstateGridSize;
                int tileY = cy * EstateGridSize;
                int chunkX = tileX / 16;
                int chunkY = tileY / 16;
                if (!chunkSet.Contains(chunkX + "," + chunkY)) continue;
                cells[new Point2(cx, cy)] = kv.Value;
                if (!licenses.ContainsKey(kv.Value) && _context.Estates.TryGetValue(kv.Value, out EstateRecord rec))
                {
                    licenses[kv.Value] = ToLicense(kv.Value, rec);
                }
            }
        }
        return new EstateGrids
        {
            Chunks = chunkList.ToArray(),
            Cells = cells,
            EstateLicenses = licenses.Values.ToArray()
        };
    }
}
