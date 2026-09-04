using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Durango.Logic.Clusters;
using Durango.Utils;

namespace Durango.Online;

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

    public GameServer GameServer { get; private set; }

    public Gateway Gateway { get; private set; }

    public IReadOnlyList<Context> Contexts => _contexts;

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
    }

    public void Start(int gamePort, int gatewayPort, string publicHost, string androidBundlesDir, string assetsDir)
    {
        GameServer = new GameServer(_worldCtx, _fallbackPlayer);
        GameServer.Start(gamePort);
        foreach (Context context in _contexts)
        {
            GameServer.Register(context.Player);
        }
        Gateway = new Gateway(this, GameServer, _worldCtx, _fallbackPlayer)
        {
            PublicHost = publicHost,
            AssetBundleAndroidDir = androidBundlesDir,
            AssetsDir = assetsDir
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

    public void SaveAll()
    {
        _worldCtx?.Save(persistent: false);
        foreach (Context context in _contexts)
        {
            context.Player.Save();
        }
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

    /// <summary>รายชื่อตัวละครสำหรับ /accounts (เทียบเท่า Cluster.OnRequestAccount ต้นฉบับ)</summary>
    public Account BuildAccount()
    {
        var account = new Account();
        foreach (Context context in _contexts)
        {
            account.Players.Add(context.Player.PlayerInfo);
        }
        account.PlayerSlotCount = account.Players.Count;
        account.MaxPlayerSlotCount = Math.Max(2, account.Players.Count);
        return account;
    }
}
