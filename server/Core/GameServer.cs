using System;
using System.Collections.Generic;
using System.Net.Sockets;
using Durango.Network;
using Durango.Utils;
using Durango.Utils.Extensions;
using JetBrains.Annotations;
using Messages;
using Shared.Region;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/GameServer.cs (TCP 8191, handshake แท้ GetClock→Auth→Ready)
// ความต่างจากต้นฉบับ (เอกสารใน docs/server/ServerNx.md):
//  1) เซิร์ฟนี้รับหลายผู้เล่นในโลกเดียว — ต้นฉบับ offline โฮสต์ 1 คน/สล็อต จึงเซฟเฉพาะ _playerCtx
//     ที่นี่ ContextChanged เซฟ context ของคนนั้นถ้ามี Path (สล็อตจริงบนดิสก์)
//  2) IssueSession: token→entityId สำหรับ /sessions (ต้นฉบับไม่มี session token จริง)
public class GameServer
{
    public const int DefaultPort = 8191;

    private readonly Listener _listener;

    private readonly PlayerContext _playerCtx;

    private readonly Dictionary<string, PlayerContext> _playerContexts = new();

    private readonly List<Connection> _connections = new();

    private readonly Dictionary<Connection, string> _connectionDict = new();

    private readonly Dictionary<string, string> _sessionTokens = new();

    /// <summary>
    /// token → กุญแจบัญชีของผู้ถือ — ตัวที่ทำให้ "ย้าย token ไปชี้ตัวละครไหนก็ได้" หมดไป
    ///
    /// ⚠️ ไม่มีตารางนี้ = <c>BindSessionToEntity</c> เช็คได้แค่ว่า "token นี้เซิร์ฟออกให้จริงไหม"
    /// ไม่ได้เช็คว่าตัวละครปลายทางเป็นของผู้ถือ token หรือเปล่า ⇒ curl 2 บรรทัดยึดตัวละครใครก็ได้:
    /// <c>POST /sessions</c> (ขอ token ฟรี) → <c>GET /entry?entity_id=&lt;ของเหยื่อ&gt;</c> → Auth ผ่านเป็นเหยื่อ
    /// </summary>
    private readonly Dictionary<string, string> _sessionOwners = new();

    public World World { get; }

    /// <summary>
    /// [5 ก.ย. 2026] โลกของทุกเกาะ — Host ตั้งให้หลังสร้าง GameServer
    /// ผู้เล่นแต่ละคนเข้าโลกตาม PlayerContext.RegionId ไม่ใช่โลกเดียวร่วมกันแบบต้นฉบับ
    /// </summary>
    public WorldRegistry Worlds { get; set; }

    /// <summary>โลกที่ผู้เล่นคนนี้อยู่ — ตกไปที่โลกตั้งต้นถ้ายังไม่มีระบบหลายเกาะ</summary>
    public World WorldOf(PlayerContext context) =>
        Worlds == null ? World : Worlds.GetOrCreate(context?.RegionId);

    public int Port { get; private set; }

    public GameServer(WorldContext worldCtx, PlayerContext playerCtx)
    {
        _listener = new Listener();
        Port = 8191;
        _playerCtx = playerCtx;
        World = new World(worldCtx);
    }

    public void Start(int port)
    {
        Port = port;
        _listener.Start(port);
        _listener.ClientAccepted += Listener_ClientAccepted;
    }

    public void Close()
    {
        try
        {
            _listener.Close();
            for (int num = _connections.Count - 1; num >= 0; num--)
            {
                _connections[num].Close();
            }
            _connections.Clear();
            if (Worlds != null) Worlds.StopAll(); else World.Stop();
        }
        catch (Exception)
        {
        }
    }

    public void Process()
    {
        _listener.Process();
        for (int num = _connections.Count - 1; num >= 0; num--)
        {
            _connections[num].Process();
        }
        DropStaleUnauthenticated();
        if (Worlds != null) Worlds.ProcessAll(); else World.Process();
    }

    /// <summary>
    /// ตัดสายที่ต่อเข้ามาแล้วไม่ยอมผ่าน Auth ภายในเวลาที่กำหนด
    ///
    /// ⚠️ ไม่มีตัวนี้ = เปิด TCP ค้างไว้เฉย ๆ ก็จองบัฟเฟอร์ ~4 MB ต่อเส้นได้ตลอดกาล
    /// โดยไม่ต้องมี token ไม่ต้องมีบัญชี ไม่ต้องทำอะไรเลย
    /// </summary>
    private void DropStaleUnauthenticated()
    {
        if (_pendingAuth.Count == 0) return;
        double now = Gauge.CurrentTime;

        List<Connection> stale = null;
        foreach (KeyValuePair<Connection, double> pair in _pendingAuth)
        {
            if (now - pair.Value < UnauthenticatedTimeoutSeconds) continue;
            (stale ??= new List<Connection>()).Add(pair.Key);
        }
        if (stale == null) return;

        foreach (Connection connection in stale)
        {
            Console.WriteLine("[auth] ตัดสายที่ไม่ผ่าน Auth ภายในเวลาที่กำหนด");
            _pendingAuth.Remove(connection);
            try { connection.Close(); } catch (Exception) { }
            _connections.Remove(connection);
        }
    }

    /// <summary>ลงทะเบียน context (สล็อตจริงหรือชั่วคราว) — /sessions เรียก</summary>
    public bool Register(PlayerContext context)
    {
        if (context != null && !string.IsNullOrEmpty(context.EntityId))
        {
            _playerContexts[context.EntityId] = context;
            return true;
        }
        return false;
    }

    /// <summary>ออก session token ให้ผู้ถือกุญแจบัญชี <paramref name="ownerKey"/></summary>
    public void IssueSession(string entityId, string token, string ownerKey)
    {
        _sessionTokens[token] = entityId;
        _sessionOwners[token] = ownerKey;
    }

    /// <summary>กุญแจบัญชีของผู้ถือ token นี้ — null ถ้าไม่รู้จัก token</summary>
    public string OwnerOfSession(string token) => _sessionOwners.Get(token ?? "");

    public bool TryGetSessionEntityId(string token, out string entityId)
    {
        return _sessionTokens.TryGetValue(token ?? "", out entityId);
    }

    /// <summary>
    /// [5 ก.ย. 2026] ย้าย session token ที่ออกไว้แล้ว ให้ชี้ตัวละครที่ผู้เล่นเลือกบนหน้า Title
    ///
    /// ทำไมต้องมี: ในโหมด Online ตัวเกม **ไม่ส่ง** ฟิลด์ "player" มากับ /sessions
    /// (client/Durango.UI/TitleMenuGroup.cs:334-340 ใส่ "player" เฉพาะตอน GameManager.ConnectCluster != null
    /// คือทาง LAN/ConnectTo เท่านั้น) ⇒ ตอนออก token เซิร์ฟยังไม่รู้ว่าจะเล่นตัวไหน
    /// ตัวละครที่เลือกถูกบอกทีหลังที่ /entry?entity_id=… ซึ่งยิงมาแบบ auth:true
    /// (client/Durango.UI/TitleMenuGroup.cs:1046 RquestEntry → Http.cs:36 ใส่ header Authorization)
    /// ⇒ ผูกที่นี่ได้อย่างปลอดภัย เพราะต้องถือ token ที่เซิร์ฟออกให้เท่านั้นถึงจะย้ายได้
    ///
    /// คืน false เมื่อ token ไม่รู้จัก — ผู้เรียกไม่ต้องทำอะไรต่อ (Auth จะปฏิเสธเองอยู่แล้ว)
    /// </summary>
    public bool BindSessionToEntity(string token, string entityId)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(entityId)
            || !_sessionTokens.ContainsKey(token))
        {
            return false;
        }

        // ⚠️ ด่านที่ขาดไปตั้งแต่ต้น — เดิมเช็คแค่ว่า token นี้เซิร์ฟออกให้จริงไหม แล้วย้ายให้เลย
        // ซึ่งไม่ได้กันอะไรเลย เพราะ token ขอฟรีได้ที่ /sessions โดยไม่ต้องยืนยันตัวตน
        // ⇒ ต้องเช็คว่า "ตัวละครปลายทางเป็นของบัญชีเดียวกับผู้ถือ token" ด้วย
        string owner = _sessionOwners.Get(token);
        PlayerContext target = _playerContexts.Get(entityId);

        // ตัวละครที่เซิร์ฟยังไม่รู้จัก = ตัวที่เพิ่งขอ session มาในรอบนี้ (ยังไม่ผ่าน /players)
        // ปล่อยผ่านได้เพราะยังไม่มีใครเป็นเจ้าของ และ /players จะประทับเจ้าของให้ตอนสร้างจริง
        if (target == null) { _sessionTokens[token] = entityId; return true; }

        if (!AccountKeys.Same(owner, target.OwnerKey))
        {
            Console.WriteLine($"[auth] ปฏิเสธการผูก session: บัญชี {AccountKeys.ForLog(owner)} " +
                              $"ไม่ใช่เจ้าของตัวละคร {entityId} (เจ้าของ {AccountKeys.ForLog(target.OwnerKey)})");
            return false;
        }
        _sessionTokens[token] = entityId;
        return true;
    }

    /// <summary>
    /// context ของตัวละครนี้ — <c>null</c> ถ้าเซิร์ฟไม่รู้จัก
    ///
    /// ⚠️ เดิมถอยไปที่ <c>_playerCtx</c> (สล็อตแรกของเซิร์ฟ) เมื่อหาไม่เจอ
    /// ซึ่งเป็นมรดกจากเซิร์ฟ offline ที่มีผู้เล่นคนเดียว แต่ในโหมดหลายคนมันคือช่องโหว่:
    /// ใส่ <c>entity_id</c> มั่ว ๆ ที่ไม่มีจริง = **ได้ตัวละครของคนแรกไปเล่น** โดยไม่ต้องรู้ id ใครเลย
    /// แล้ว autosave เขียนความเสียหายลงไฟล์จริงภายใน 60 วินาที
    /// ⇒ คืน null แล้วให้ผู้เรียกปฏิเสธการเชื่อมต่อ
    /// </summary>
    [CanBeNull]
    public PlayerContext GetPlayerContext(string entityId) => _playerContexts.Get(entityId);

    /// <summary>
    /// เพดานจำนวนสายที่เปิดค้างพร้อมกัน — **ค่าของเรา**
    ///
    /// ⚠️ <c>--max-players</c> กันได้แค่ประตูหน้า (HTTP /entry) แต่ทางเข้าจริงคือ TCP
    /// ซึ่งเดิม**ไม่มีเพดานเลย** ⇒ เปิดสาย TCP ค้างไว้เฉย ๆ โดยไม่ต้อง Auth ก็จองบัฟเฟอร์
    /// ~4 MB ต่อเส้นได้ไม่จำกัด (Connection.cs จอง 7 ก้อน ก้อนละ 512 KB ตั้งแต่ตอน accept)
    /// ⇒ ไม่กี่ร้อยสายก็ทำ RAM หมด
    ///
    /// ตั้งเป็น 3 เท่าของเพดานผู้เล่น เผื่อช่วงที่คนกำลังต่อใหม่ทับกับคนเก่าที่ยังไม่หลุด
    /// </summary>
    public static int MaxPlayersHint { get; set; } = 200;

    private static int MaxConnections => Math.Max(32, MaxPlayersHint * 3);

    /// <summary>
    /// เวลาที่ยอมให้สายหนึ่งค้างอยู่โดยยังไม่ผ่าน Auth (วินาที) — **ค่าของเรา**
    /// ตัวเกมจริงส่ง Auth ทันทีหลังต่อ ⇒ 30 วินาทีเหลือเฟือแม้เน็ตแย่
    /// </summary>
    private const double UnauthenticatedTimeoutSeconds = 30.0;

    /// <summary>สายที่ยังไม่ผ่าน Auth → เวลาที่ต่อเข้ามา (ใช้ตัดสายที่จองบัฟเฟอร์ทิ้งไว้เฉย ๆ)</summary>
    private readonly Dictionary<Connection, double> _pendingAuth = new();

    private void Listener_ClientAccepted(Socket socket)
    {
        if (_connections.Count >= MaxConnections)
        {
            Console.WriteLine($"[auth] ปฏิเสธสายใหม่ — เต็มเพดาน ({_connections.Count}/{MaxConnections})");
            try { socket.Close(); } catch (Exception) { }
            return;
        }

        Connection connection = new(socket);
        connection.Recv(delegate(GetClock getClock, PacketHeader header)
        {
            Clock msg = default;
            msg.ClientTime = getClock.Time;
            msg.ServerTime = Times.UnixTimeNow();
            connection.Send(msg, header.Seq);
        });
        connection.Recv(delegate(Auth auth, PacketHeader header)
        {
            // [4 ก.ย. 2026] ก่อนหน้านี้เชื่อ auth.EntityId ตรง ๆ — ใครก็ยิง Auth อ้างเป็น entity id ใครก็ได้
            // สวมรอยตัวละครคนอื่นได้ทันที ⇒ ต้องผูกกับ token ที่ /sessions ออกให้เท่านั้น (เหมือน server/ หลัก)
            if (!TryGetSessionEntityId(auth.SessionToken, out string sessionEntityId)
                || !string.Equals(sessionEntityId, auth.EntityId, StringComparison.Ordinal))
            {
                Console.WriteLine($"[auth] ปฏิเสธ: token ไม่ตรงกับ entity ที่อ้าง ({auth.EntityId})");
                connection.Send(new Abort { Text = "การยืนยันตัวตนไม่ผ่าน" }, header.Seq);
                connection.Close();
                return;
            }
            string entityId = auth.EntityId;
            PlayerContext playerContext = GetPlayerContext(entityId);
            if (playerContext == null)
            {
                // เดิมตรงนี้ถอยไปใช้ตัวละครสล็อตแรกให้เลย (ดู GetPlayerContext) ⇒ ใส่ id มั่วก็เข้าเล่นได้
                Console.WriteLine($"[auth] ปฏิเสธ: ไม่รู้จักตัวละคร {entityId}");
                connection.Send(new Abort { Text = "ไม่พบตัวละครนี้" }, header.Seq);
                connection.Close();
                return;
            }
            _connectionDict[connection] = entityId;
            _pendingAuth.Remove(connection);      // ผ่านด่านแล้ว ไม่ต้องนับเวลาอีก
            SendWelcome(connection, entityId, playerContext.PlayerInfo.PlayerName, header.Seq);
        });
        connection.Recv(delegate(Ready ready, PacketHeader readyHeader)
        {
            string text = _connectionDict.Get(connection);
            if (string.IsNullOrEmpty(text))
            {
                connection.Close();
            }
            else
            {
                PlayerContext playerContext = GetPlayerContext(text);
                if (playerContext == null)
                {
                    // ปกติไม่ควรเกิด (Auth กรองไปแล้ว) — กันไว้เพราะเดิมจุดนี้ NullReference ไม่ได้
                    // เพราะมี fallback อยู่ ตอนตัด fallback ออกจึงต้องมีด่านตรงนี้ด้วย
                    Console.WriteLine($"[auth] Ready: ไม่รู้จักตัวละคร {text} — ตัดสาย");
                    connection.Close();
                    return;
                }
                connection.Send(default(OK), readyHeader.Seq);
                bool flag = playerContext.EntityId == text;
                World playerWorld = WorldOf(playerContext);
                Player player = new(text, connection, playerWorld, playerContext, flag);
                if (flag)
                {
                    player.ContextChanged += delegate
                    {
                        // ต้นฉบับเซฟเฉพาะ _playerCtx (offline โฮสต์คนเดียว) — ที่นี่เซฟ context ของสล็อตนั้น
                        // ถ้าเป็นสล็อตจริงบนดิสก์ (context ชั่วคราวที่ยังไม่ผ่าน /players ไม่มี Path จึงไม่เซฟ)
                        if (!string.IsNullOrEmpty(playerContext.Path))
                        {
                            playerContext.Save();
                        }
                    };
                }
                playerWorld.AddPlayer(player);
            }
            _connections.Remove(connection);
            _connectionDict.Remove(connection);
        });
        connection.ConnetionClosed += delegate
        {
            _connections.Remove(connection);
            _connectionDict.Remove(connection);
            _pendingAuth.Remove(connection);
        };
        connection.StartReceive();
        _connections.Add(connection);
        _pendingAuth[connection] = Gauge.CurrentTime;
    }

    private void SendWelcome(Connection connection, string entityId, string name, uint seq)
    {
        Welcome msg = new()
        {
            UserId = entityId,
            Name = name
        };
        PlayerContext playerContext = GetPlayerContext(entityId);
        msg.Storage.Data = playerContext.Storage;
        // [5 ก.ย. 2026] บอกเกาะที่ผู้เล่นอยู่จริง — ต้นฉบับ hardcode "1" ได้เพราะมีโลกเดียว
        // Id ใช้ระบุเกาะในระบบล่องเรือ (ตรงกับ RegionCatalog) ส่วน TerrainId ยังเป็น "1" เพราะ
        // ตัวเกมเอาค่านี้ไปประกอบ URL ขอแผนที่ /terrains/<TerrainId>/… ซึ่ง Gateway เสิร์ฟที่เส้น
        // "/terrains/1" ให้ตามโลกของผู้เล่นที่ขออยู่แล้ว ⇒ ไม่ต้องแตะฝั่ง client
        World playerWorld = WorldOf(playerContext);
        msg.Region.CreatedAt = 0.0;
        // Region.Id/Role ต้องบอก client ว่าตอนนี้อยู่เกาะส่วนตัวหรือไม่
        // UI ที่ดินเทียบ GameManager.Region.Role/Id กับ PersonalRegion.Region.Id
        // เควส MoveToRegionToDo(Personal) ก็เช็ค Role() == Personal
        string regionId = playerContext.RegionId;
        bool onPersonal = !string.IsNullOrEmpty(regionId) &&
                          regionId.StartsWith("personal_", StringComparison.OrdinalIgnoreCase);
        msg.Region.Id = onPersonal
            ? regionId
            : (string.IsNullOrEmpty(regionId) ? (playerWorld.TerrainId ?? "1") : regionId);
        msg.Region.Name = null;
        msg.Region.TemplateId = onPersonal
            ? (playerContext.PersonalRegionTemplateId ?? playerWorld.TerrainInfo.region_template)
            : playerWorld.TerrainInfo.region_template;
        // TerrainId = ชื่อไฟล์ terrain จริง สำหรับโหลดแผนที่/chunk
        msg.Region.TerrainId = playerWorld.TerrainId ?? "1";
        msg.Region.Role = onPersonal ? Role.Personal : Role.Rural;
        // เกาะส่วนตัวของผู้เล่น (ว่างได้ถ้ายังไม่สร้าง)
        msg.PersonalRegionId = string.IsNullOrEmpty(playerContext.PersonalRegionId)
            ? null
            : playerContext.PersonalRegionId;
        Console.WriteLine(
            $"[welcome] {entityId[..Math.Min(8, entityId.Length)]} Region.Id={msg.Region.Id} Role={msg.Region.Role} TerrainId={msg.Region.TerrainId} TemplateId={msg.Region.TemplateId} PersonalRegionId={msg.PersonalRegionId ?? "(ว่าง)"}");
        msg.Options.Bool = new[]
        {
            new BoolOption { Key = "market.ui_enabled", Value = true }
        };
        msg.Options.Int = new[]
        {
            new IntegerOption { Key = "market.search.limit", Value = 20L }
        };
        connection.Send(msg, seq);
    }
}
