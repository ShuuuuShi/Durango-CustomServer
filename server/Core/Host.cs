using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Durango.Logic.Clusters;
using Durango.Utils;

namespace Durango.Online;

/// <summary>
/// [5 ก.ย. 2026] ตัววัดสุขภาพเซิร์ฟ — เก็บที่เดียว ให้ <c>/health</c> อ่าน
///
/// ทำไมต้องมี: ก่อนหน้านี้ไม่มีตัวเลขให้ดูเลยสักตัว เวลาเซิร์ฟหน่วงต้องเดาเอาว่าเป็นเพราะอะไร
/// (อาการ "tps 120 → 2" ตอนมือถือโหลด bundle กว่าจะรู้ว่าเป็น GC ก็ไล่หาอยู่หลายวัน)
/// มีตัวเลขแล้วดูออกทันทีว่ารอบเกมช้าจริงไหม เซฟค้างไหม พังกี่ครั้ง
///
/// ออกแบบให้เบา: เก็บเวลาลง ring buffer ที่จองไว้ครั้งเดียว (ไม่ alloc ต่อรอบ)
/// เก็บเป็น "ไมโครวินาที" ไม่ใช่ ms เพราะรอบปกติสั้นกว่า 1 ms ⇒ ปัดเป็น ms แล้วจะเป็น 0 หมด
/// การเรียงลำดับหาค่า p50/p99 ทำเฉพาะตอนมีคนขอ /health เท่านั้น ไม่ได้ทำทุกรอบ
///
/// ⚠️ เขียนจาก main loop และอ่านจาก /health ซึ่งก็รันบน main loop เดียวกัน (Gateway.Process
/// ถูกเรียกใน Host.Process) ⇒ ไม่ต้องล็อก
/// </summary>
public static class ServerMetrics
{
    /// <summary>จำนวนรอบที่เก็บย้อนหลัง — 512 รอบ ≈ 4 วินาทีที่ 120 tps (พอเห็นอาการกระตุกสด ๆ)</summary>
    private const int SampleCount = 512;

    private static readonly double _usPerTimestamp = 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>เวลาต่อรอบเต็ม (เริ่มรอบ → เริ่มรอบถัดไป รวม Thread.Sleep) — บอกว่า tps ตกจริงไหม</summary>
    private static readonly int[] _tickUs = new int[SampleCount];

    /// <summary>เวลาที่ใช้ทำงานจริงในรอบ (host.Process อย่างเดียว) — บอกว่างานล้นงบเวลาไหม</summary>
    private static readonly int[] _workUs = new int[SampleCount];

    private static int _sampleAt;

    private static int _sampleFilled;

    private static long _bootAt;

    private static long _lastSaveAt;

    private static int _saveFailures;

    private static int _loopErrors;

    public static void MarkBoot() => _bootAt = Environment.TickCount64;

    /// <summary>เรียกท้ายทุกรอบของ main loop — รับค่าเป็น timestamp ดิบของ Stopwatch (แปลงหน่วยที่นี่)</summary>
    public static void RecordTick(long loopTimestamps, long workTimestamps)
    {
        int i = _sampleAt;
        _tickUs[i] = ToMicros(loopTimestamps);
        _workUs[i] = ToMicros(workTimestamps);
        _sampleAt = (i + 1) % SampleCount;
        if (_sampleFilled < SampleCount) _sampleFilled++;
    }

    private static int ToMicros(long timestamps)
    {
        if (timestamps <= 0) return 0;
        double us = timestamps * _usPerTimestamp;
        return us >= int.MaxValue ? int.MaxValue : (int)us;
    }

    public static void RecordSaveOk() => _lastSaveAt = Environment.TickCount64;

    public static void RecordSaveFailed() => _saveFailures++;

    public static void RecordLoopError() => _loopErrors++;

    public static long UptimeSec => _bootAt == 0 ? 0 : (Environment.TickCount64 - _bootAt) / 1000;

    /// <summary>เซฟสำเร็จครั้งล่าสุดเมื่อกี่วินาทีที่แล้ว — -1 = ยังไม่เคยเซฟสำเร็จเลยตั้งแต่บูต</summary>
    public static long LastSaveAgoSec => _lastSaveAt == 0 ? -1 : (Environment.TickCount64 - _lastSaveAt) / 1000;

    public static int SaveFailures => _saveFailures;

    public static int LoopErrors => _loopErrors;

    public static int Samples => _sampleFilled;

    public static void TickStats(out double p50, out double p99, out double max) => Stats(_tickUs, out p50, out p99, out max);

    public static void WorkStats(out double p50, out double p99, out double max) => Stats(_workUs, out p50, out p99, out max);

    /// <summary>คืนค่าเป็น "มิลลิวินาที" (ตัวเลขที่คนอ่านเข้าใจ) จากตัวอย่างที่เก็บเป็นไมโครวินาที</summary>
    private static void Stats(int[] src, out double p50, out double p99, out double max)
    {
        int n = _sampleFilled;
        if (n == 0)
        {
            p50 = p99 = max = 0.0;
            return;
        }
        // ตัวอย่างเรียงจาก index 0 เสมอ: ตอนยังไม่เต็ม _sampleFilled == _sampleAt
        // ตอนเต็มแล้ววนทับของเก่า ⇒ ทั้งอาเรย์คือของจริงทั้งหมด (ลำดับไม่สำคัญเพราะจะ sort อยู่แล้ว)
        int[] sorted = new int[n];
        Array.Copy(src, sorted, n);
        Array.Sort(sorted);
        p50 = Math.Round(sorted[n / 2] / 1000.0, 2);
        p99 = Math.Round(sorted[Math.Min(n - 1, (int)(n * 0.99))] / 1000.0, 2);
        max = Math.Round(sorted[n - 1] / 1000.0, 2);
    }
}

// แทน nexonSRC/Durango.Online/Server.cs + Servers.cs (โฮสต์ฝั่ง client)
// ความต่างจากต้นฉบับ (เอกสารเต็มใน docs/server/ServerNx.md):
//  - ต้นฉบับ: 1 สล็อต = 1 โลก (offline โฮสต์คนเดียว) — ที่นี่: โลกเดียว (สล็อต 0) + ผู้เล่นหลายคน
//  - ตัด Cluster.OnRequestAccount ฝั่ง PortraitBuilder/UI ออก (/accounts อยู่ที่ Gateway)
//  - รูปร่างไฟล์เซฟ .player/.world คงต้นฉบับ (AppData/offline/{cluster}/)
public class Host
{
    /// <summary>cluster_mode ที่ /knock+/entry ตอบ
    ///
    /// [5 ก.ย. 2026] เปลี่ยนค่าตั้งต้นจาก Offline เป็น **Online** — โปรเจกต์นี้ทำเวอร์ชันออนไลน์เท่านั้น
    /// ค่านี้ถึง client 2 ทาง: (1) /entry → TitleMenuGroup.cs:895 อ่านเมื่อมาทาง Server.ConnectTo
    /// (2) cluster ที่เลือกบนหน้า Title → TitleMenuUserControlBase.cs:148 (Mode ของ cluster เอง)
    /// ⚠️ Mode ที่ client รู้จักมี 5 ค่า (Online/Offline/Editable/SingleMode/MultiMode ดู
    /// client/Durango.Online/GameServer.cs:159-160) แต่ enum ฝั่งนี้พอร์ตมาแค่ 3 ตามที่ต้นฉบับ
    /// ของเซิร์ฟใช้จริง — ส่งค่าที่ไม่มีใน enum ของ client จะถูก fallback ทิ้ง</summary>
    public static Mode ClusterMode = Mode.Online;

    private readonly string _clusterKey;

    private readonly List<Context> _contexts = new();

    private WorldContext _worldCtx;

    private PlayerContext _fallbackPlayer;

    /// <summary>โลกของทุกเกาะ (ระบบล่องเรือ) — สร้างหลัง GameServer ใน Start()</summary>
    public WorldRegistry Worlds { get; private set; }

    public GameServer GameServer { get; private set; }

    public Gateway Gateway { get; private set; }

    public IReadOnlyList<Context> Contexts => _contexts;

    /// <summary>
    /// [5 ก.ย. 2026] เพดานผู้เล่นออนไลน์พร้อมกัน (--max-players) — 0 หรือติดลบ = ไม่จำกัด
    /// เดิม Program รับค่ามาแล้วพิมพ์ออกจอเฉย ๆ ไม่มีที่ไหนเอาไปใช้เลย (ดู Gateway /entry)
    /// </summary>
    public int MaxPlayers { get; set; }

    /// <summary>
    /// รหัสผ่านของเส้นทางสำหรับคนดูแล (/health) — ว่าง = ให้เฉพาะเครื่องตัวเองเรียกได้
    /// ตั้งด้วย --admin-token หรือ env DURANGO_ADMIN_TOKEN (ดู Gateway.IsAdminAllowed)
    /// </summary>
    public string AdminToken { get; set; }

    /// <summary>เวอร์ชันตัวเกมต่ำสุดที่ยอมให้เข้า — null = รับทุกเวอร์ชัน (ดู Gateway /knock)</summary>
    public string MinClientVersion { get; set; }

    /// <summary>ลิงก์โหลดตัวเกมใหม่ — ต้องมีถ้าจะเปิดด่านเวอร์ชัน</summary>
    public string DownloadUrl { get; set; }

    public Host(string clusterKey)
    {
        _clusterKey = string.IsNullOrEmpty(clusterKey) ? "nx" : clusterKey;
    }

    /// <summary>โหลดสล็อตจากดิสก์ (เทียบเท่า Server ctor ต้นฉบับ) + เตรียมโลกสล็อต 0</summary>
    public void Load()
    {
        string basePath = WorldContext.GetBasePath(_clusterKey);
        string[] worldFiles = AppData.GetFiles(basePath, "*.world", SearchOption.TopDirectoryOnly) ?? Array.Empty<string>();
        string[] playerFiles = AppData.GetFiles(basePath, "*.player", SearchOption.TopDirectoryOnly) ?? Array.Empty<string>();

        var playersBySlot = new Dictionary<int, PlayerContext>();
        foreach (string file in playerFiles)
        {
            PlayerContext player = PlayerContext.Load(file);
            if (player != null) playersBySlot[player.PlayerSlot] = player;
        }

        var worlds = new SortedDictionary<int, WorldContext>();
        foreach (string file in worldFiles)
        {
            WorldContext world = WorldContext.Load(file);
            if (world != null) worlds[world.PlayerSlot] = world;
        }

        // โลก = สล็อต 0 (สร้างถ้าไม่มี) — ผู้เล่น = สล็อต >= 1
        if (!worlds.TryGetValue(0, out _worldCtx))
        {
            _worldCtx = new WorldContext();
            _worldCtx.Initialize(WorldContext.MakePath(0, _clusterKey));
            _worldCtx.PlayerSlot = 0;
            if (string.IsNullOrEmpty(_worldCtx.TerrainId))
            {
                _worldCtx.TerrainId = TerrainLoader.DefaultTerrainFile;
            }
            _worldCtx.Save(persistent: false);
            Console.WriteLine($"[host] สร้างโลกใหม่ (terrain {_worldCtx.TerrainId}) → {_worldCtx.Path}");
        }

        foreach (var pair in worlds)
        {
            if (pair.Key == 0) continue;
            playersBySlot.TryGetValue(pair.Key, out var player);
            if (player == null)
            {
                player = new PlayerContext();
                player.Initialize(PlayerContext.MakePath(pair.Key, _clusterKey));
                player.PlayerSlot = pair.Key;
            }
            _contexts.Add(new Context(pair.Value, player));
        }
        foreach (var pair in playersBySlot)
        {
            if (pair.Key == 0) continue;
            if (_contexts.Any(c => c.PlayerSlot == pair.Key)) continue;
            _contexts.Add(new Context(_worldCtx, pair.Value));
        }
        _contexts.Sort((a, b) => a.PlayerSlot.CompareTo(b.PlayerSlot));

        // fallback player ตามต้นฉบับ (context ของสล็อตแรก หรือสร้างใหม่ถ้ายังไม่มีใคร)
        _fallbackPlayer = _contexts.FirstOrDefault()?.Player;
        if (_fallbackPlayer == null)
        {
            _fallbackPlayer = CreatePlayerContext(NextSlot());
            _contexts.Add(new Context(_worldCtx, _fallbackPlayer));
        }

        Console.WriteLine($"[host] cluster '{_clusterKey}': ผู้เล่น {_contexts.Count} สล็อต โหลดจาก {AppData.CombinePath(basePath)}");
        // รายชื่อที่ถูกแบน — เก็บข้างไฟล์เซฟของ cluster นี้ (คนละ cluster คนละรายชื่อ)
        BanList.Load(System.IO.Path.Combine(AppData.CombinePath(basePath), "bans.json"));
    }

    public void Start(int gamePort, int gatewayPort, string publicHost, string androidBundlesDir, string assetsDir)
    {
        GameServer = new GameServer(_worldCtx, _fallbackPlayer);
        // โลกของเกาะตั้งต้น (ไฟล์ 0.world ของต้นฉบับ) ใช้ต่อเป็นเกาะแรกของสารบัญ
        Worlds = new WorldRegistry(_clusterKey, GameServer.World, _worldCtx?.TerrainId);
        GameServer.Worlds = Worlds;
        GameServer.Start(gamePort);
        foreach (Context context in _contexts)
        {
            GameServer.Register(context.Player);
        }
        Gateway = new Gateway(this, GameServer, _worldCtx, _fallbackPlayer)
        {
            PublicHost = publicHost,
            AssetBundleAndroidDir = androidBundlesDir,
            AssetsDir = assetsDir,
            AdminToken = this.AdminToken,
            MinClientVersion = this.MinClientVersion,
            DownloadUrl = this.DownloadUrl
        };
        Gateway.Start(gatewayPort);
    }

    public void Process()
    {
        Gateway?.Process();
        GameServer?.Process();
    }

    public void Close()
    {
        Gateway?.Close();
        GameServer?.Close();
        // เซฟโลกทุกสล็อตก่อนปิด (ต้นฉบับ: Ctrl+C ผ่าน Server.EndServer → World.Save)
        _worldCtx?.Save(persistent: false);
    }

    /// <summary>
    /// เซฟทุกอย่าง — เซฟทีละส่วน ส่วนไหนพังก็ไปต่อ
    ///
    /// [แก้เอง] 5 ก.ย. 2026 — เดิมไม่มี try/catch เลย: ถ้าเซฟผู้เล่นคนแรกพัง (ดิสก์เต็ม/ไฟล์ถูกล็อก)
    /// คนที่เหลือ **ไม่ได้เซฟเลยสักคน** แล้ว exception ยังเด้งขึ้นไปถึง main loop กลายเป็น loop error
    /// ปนกับปัญหาอื่น ⇒ ตอนนี้แยกนับเป็น save_failures ให้เห็นชัดใน /health
    /// </summary>
    public void SaveAll()
    {
        bool ok = true;
        try
        {
            _worldCtx?.Save(persistent: false);
        }
        catch (Exception e)
        {
            ok = false;
            Console.WriteLine("[save] ⚠️ เซฟโลกหลักไม่สำเร็จ: " + e.Message);
        }
        try
        {
            Worlds?.SaveAll();
        }
        catch (Exception e)
        {
            ok = false;
            Console.WriteLine("[save] ⚠️ เซฟโลกของเกาะไม่สำเร็จ: " + e.Message);
        }
        foreach (Context context in _contexts)
        {
            try
            {
                context.Player.Save();
            }
            catch (Exception e)
            {
                ok = false;
                Console.WriteLine($"[save] ⚠️ เซฟผู้เล่นสล็อต {context.PlayerSlot} ไม่สำเร็จ: {e.Message}");
            }
        }
        if (ok)
        {
            ServerMetrics.RecordSaveOk();
        }
        else
        {
            ServerMetrics.RecordSaveFailed();
        }
    }

    /// <summary>ตัวชี้ฟิลด์ผู้เล่นใน World — ค้นหาครั้งเดียวแล้วเก็บไว้ (ดู PlayersOnline)</summary>
    private static System.Reflection.FieldInfo _worldPlayersField;

    private static bool _worldPlayersFieldMissing;

    /// <summary>
    /// จำนวนผู้เล่นที่อยู่ในโลกจริงตอนนี้ รวมทุกเกาะ — คืน -1 เมื่อ "นับไม่ได้"
    ///
    /// ⚠️ ทำไมต้องส่องด้วย reflection: World เก็บผู้เล่นไว้ใน <c>private readonly List&lt;Player&gt; _players</c>
    /// (Core/World.cs:44) และไม่เปิด public ให้เลยสักทาง ส่วน GameServer ก็เก็บ _connections เป็น private
    /// (Core/GameServer.cs:27) — ทั้งสองไฟล์อยู่นอกขอบเขตที่งานรอบนี้แก้ได้
    /// **วิธีที่ถูกต้องกว่าคือเพิ่มบรรทัดเดียวใน World.cs: `public int PlayerCount => _players.Count;`
    /// แล้วเปลี่ยนมาเรียกอันนั้นแทน** — ที่นี่อ่านอย่างเดียว ไม่แก้ค่า และแคช FieldInfo ไว้
    /// ⇒ ต้นทุนต่อครั้ง = อ่านฟิลด์ + .Count ต่อ 1 เกาะ และเรียกเฉพาะตอนมีคนขอ /health เท่านั้น
    ///
    /// เรื่องเธรด: ลิสต์นี้ถูกแก้จาก main loop และผู้เรียก (/health, /entry) ก็รันบน main loop เดียวกัน
    /// </summary>
    public int PlayersOnline()
    {
        if (_worldPlayersFieldMissing) return -1;
        if (_worldPlayersField == null)
        {
            _worldPlayersField = typeof(World).GetField("_players",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (_worldPlayersField == null)
            {
                _worldPlayersFieldMissing = true;
                Console.WriteLine("[health] นับผู้เล่นออนไลน์ไม่ได้ — World._players หายไป (เปลี่ยนชื่อ?)");
                return -1;
            }
        }
        try
        {
            int total = 0;
            if (Worlds != null)
            {
                foreach (KeyValuePair<string, World> kv in Worlds.Loaded)
                {
                    if (_worldPlayersField.GetValue(kv.Value) is System.Collections.ICollection players)
                    {
                        total += players.Count;
                    }
                }
            }
            else if (GameServer?.World != null && _worldPlayersField.GetValue(GameServer.World) is System.Collections.ICollection one)
            {
                total = one.Count;
            }
            return total;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>จำนวนเกาะที่เปิดอยู่ในหน่วยความจำตอนนี้ (โลกถูกสร้างแบบ lazy เมื่อมีคนไปถึง)</summary>
    public int WorldsLoaded()
    {
        if (Worlds == null) return GameServer?.World != null ? 1 : 0;
        int n = 0;
        foreach (KeyValuePair<string, World> _ in Worlds.Loaded) n++;
        return n;
    }

    public PlayerContext FindContextByEntityId(string entityId) =>
        _contexts.FirstOrDefault(c => c.EntityId == entityId)?.Player;

    public int NextSlot()
    {
        int max = 0;
        foreach (Context context in _contexts)
        {
            if (context.PlayerSlot > max) max = context.PlayerSlot;
        }
        return max + 1;
    }

    /// <summary>สร้าง context ชั่วคราว (ยังไม่เซฟ — จะกลายเป็นสล็อตจริงเมื่อ /players หรือเข้าโลกแล้วมีการเปลี่ยนแปลง)</summary>
    public PlayerContext CreateTemporaryContext(string entityId, string name, int? level)
    {
        var context = new PlayerContext();
        context.Initialize(null);
        if (!string.IsNullOrEmpty(entityId))
        {
            context.PlayerInfo.PlayerEntityId = entityId;
            context.AppearPlayer.EntityId = entityId;
            context.AppearPlayer.Title.EntityId = entityId;
            context.AppearPlayer.Member.EntityId = entityId;
            context.AppearPlayer.Move.EntityId = entityId;
            context.AppearPlayer.Survival.EntityId = entityId;
        }
        if (!string.IsNullOrEmpty(name))
        {
            context.PlayerInfo.PlayerName = name;
            context.AppearPlayer.Name = name;
        }
        if (level is > 0)
        {
            context.PlayerInfo.PlayerLevel = level.Value;
            context.AppearPlayer.Level = level.Value;
        }
        return context;
    }

    /// <summary>เลื่อน context ชั่วคราวขึ้นเป็นสล็อตจริงบนดิสก์ (/players เรียก)</summary>
    public PlayerContext PersistAsSlot(PlayerContext context)
    {
        if (!string.IsNullOrEmpty(context.Path)) return context; // เป็นสล็อตจริงแล้ว
        int slot = NextSlot();
        context.PlayerSlot = slot;
        context.Initialize(PlayerContext.MakePath(slot, _clusterKey));
        context.Save();
        _contexts.Add(new Context(_worldCtx, context));
        Console.WriteLine($"[host] ผู้เล่น '{context.PlayerInfo.PlayerName}' ({context.EntityId}) → สล็อต {slot}");
        return context;
    }

    private PlayerContext CreatePlayerContext(int slot)
    {
        var context = new PlayerContext();
        context.Initialize(PlayerContext.MakePath(slot, _clusterKey));
        context.PlayerSlot = slot;
        context.Save();
        return context;
    }

    /// <summary>
    /// [5 ก.ย. 2026] เปิดให้บัญชีแรกที่เข้ามารับ "ตัวละครกำพร้า" ไปเป็นของตัวเอง
    ///
    /// ตัวละครกำพร้า = เซฟที่สร้างไว้ก่อนมีระบบบัญชี จึงไม่มี <c>owner_key</c>
    /// โดยปริยายมันจะ **มองไม่เห็นและเข้าไม่ได้เลย** ซึ่งถูกต้องด้านความปลอดภัย
    /// แต่ทำให้เซฟเดิมของเซิร์ฟทดสอบใช้ต่อไม่ได้ ⇒ เปิดสวิตช์นี้ตอนย้ายข้อมูล **ครั้งเดียว**
    ///
    /// ⚠️ **ห้ามเปิดค้างไว้ตอนเปิดให้คนนอกเล่น** — เปิดอยู่แปลว่าใครก็ตามที่ต่อเข้ามาเป็นคนแรก
    /// จะได้ตัวละครที่ยังไม่มีเจ้าของไปทั้งหมด ซึ่งก็คือช่องโหว่เดิมในรูปแบบที่แคบลงเท่านั้น
    /// ค่าตั้งต้นจึงเป็นปิด และเซิร์ฟจะเตือนทุกครั้งที่บูตขึ้นมาพร้อมสวิตช์นี้
    /// </summary>
    public static bool AdoptOrphans { get; set; }

    /// <summary>
    /// โหมดปิดปรับปรุง — คนใหม่เข้าไม่ได้ แต่คนที่เล่นอยู่ยังเล่นต่อได้
    ///
    /// ตั้งใจไม่เตะคนที่เล่นอยู่ทันที เพื่อให้ประกาศก่อนแล้วรอคนทยอยออกเองได้
    /// (audit: เดิมไม่มีสวิตช์อะไรเลย จะปิดปรับปรุงต้องดับทั้งเซิร์ฟทันที)
    /// </summary>
    public static bool Maintenance { get; set; }

    /// <summary>ใครออนไลน์อยู่บ้าง (สำหรับหน้าแอดมิน) — ต้องรู้ก่อนถึงจะเตะถูกคน</summary>
    public List<Dictionary<string, object>> DescribeOnline()
    {
        var result = new List<Dictionary<string, object>>();
        foreach (World world in WorldsOf())
        {
            foreach (Player player in world.PlayersSnapshot())
            {
                result.Add(new Dictionary<string, object>
                {
                    ["entity_id"] = player.EntityId,
                    ["name"] = player.Name ?? "",
                    ["region"] = world.TerrainId ?? "",
                    ["banned"] = BanList.IsBanned(FindContextByEntityId(player.EntityId)?.OwnerKey)
                });
            }
        }
        return result;
    }

    /// <summary>เตะผู้เล่นออกจากเกม — คืน false ถ้าไม่ได้ออนไลน์อยู่</summary>
    public bool KickPlayer(string entityId, string reason)
    {
        if (string.IsNullOrEmpty(entityId)) return false;
        foreach (World world in WorldsOf())
        {
            foreach (Player player in world.PlayersSnapshot())
            {
                if (player.EntityId != entityId) continue;
                Console.WriteLine($"[ดูแล] เตะ {entityId} — {reason}");
                player.KickWith(reason);
                return true;
            }
        }
        return false;
    }

    /// <summary>ประกาศถึงทุกคนที่ออนไลน์ — คืนจำนวนคนที่ได้รับ</summary>
    public int Announce(string text)
    {
        int sent = 0;
        foreach (World world in WorldsOf())
        {
            foreach (Player player in world.PlayersSnapshot())
            {
                player.SendNotice(text);
                sent++;
            }
        }
        Console.WriteLine($"[ดูแล] ประกาศถึง {sent} คน: {text}");
        return sent;
    }

    /// <summary>โลกทั้งหมดที่เปิดอยู่ (เกาะเดียวหรือหลายเกาะแล้วแต่โหมด)</summary>
    private IEnumerable<World> WorldsOf()
    {
        if (Worlds != null)
        {
            foreach (KeyValuePair<string, World> pair in Worlds.Loaded)
            {
                if (pair.Value != null) yield return pair.Value;
            }
            yield break;
        }
        if (GameServer?.World != null) yield return GameServer.World;
    }

    /// <summary>บัญชีเปล่า — ใช้ตอบคำขอที่ไม่มีกุญแจบัญชี</summary>
    public static Account EmptyAccount() => new() { PlayerSlotCount = 0, MaxPlayerSlotCount = 2 };

    /// <summary>
    /// รายชื่อตัวละคร **ของบัญชีนี้เท่านั้น** (เทียบเท่า Cluster.OnRequestAccount ต้นฉบับ)
    ///
    /// ⚠️ เดิมคืนตัวละครทุกตัวบนเซิร์ฟให้ทุกคน ⇒ ใครก็กดเข้าเล่นตัวละครคนอื่นได้จากหน้า Title
    /// (เหตุผลเต็มที่ Core/Gateway.cs เส้น /accounts)
    ///
    /// <c>MaxPlayerSlotCount</c> ต้องมากกว่าจำนวนตัวที่มีเสมอ ไม่งั้นปุ่ม "สร้างตัวใหม่" หายไป —
    /// ฝั่งเกมโชว์ปุ่มนั้นเฉพาะช่องที่ index &lt; availableSlotCount
    /// (client/Durango.UI/TitlePlayerSelectionGroupBase.cs:89-97)
    /// </summary>
    public Account BuildAccount(string ownerKey)
    {
        var account = new Account();
        if (string.IsNullOrEmpty(ownerKey)) return EmptyAccount();

        foreach (Context context in _contexts)
        {
            PlayerContext player = context.Player;

            // ตัวละครกำพร้า — รับเป็นของบัญชีแรกที่เข้ามา เฉพาะตอนเปิดสวิตช์ย้ายข้อมูล
            if (string.IsNullOrEmpty(player.OwnerKey) && AdoptOrphans)
            {
                player.OwnerKey = ownerKey;
                player.Save();
                Console.WriteLine($"[บัญชี] ตัวละครกำพร้า '{player.PlayerInfo.PlayerName}' " +
                                  $"({player.EntityId}) → บัญชี {AccountKeys.ForLog(ownerKey)}");
            }

            if (!AccountKeys.Same(player.OwnerKey, ownerKey)) continue;
            account.Players.Add(player.PlayerInfo);
        }

        account.PlayerSlotCount = account.Players.Count;
        // +1 เสมอเพื่อให้มีช่องว่างให้กดสร้างตัวใหม่ (ขั้นต่ำ 2 ตามเดิม)
        account.MaxPlayerSlotCount = Math.Max(2, account.Players.Count + 1);
        return account;
    }
}
