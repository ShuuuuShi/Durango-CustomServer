using System;
using System.Collections.Generic;
using System.Net.Sockets;
using Durango.Network;
using Durango.Utils;
using Durango.Utils.Extensions;
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

    public World World { get; }

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
            World.Stop();
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
        World.Process();
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

    public void IssueSession(string entityId, string token)
    {
        _sessionTokens[token] = entityId;
    }

    public bool TryGetSessionEntityId(string token, out string entityId)
    {
        return _sessionTokens.TryGetValue(token ?? "", out entityId);
    }

    public PlayerContext GetPlayerContext(string entityId)
    {
        PlayerContext playerContext = _playerContexts.Get(entityId);
        if (playerContext == null)
        {
            playerContext = _playerCtx;
        }
        return playerContext;
    }

    private void Listener_ClientAccepted(Socket socket)
    {
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
            _connectionDict[connection] = entityId;
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
                connection.Send(default(OK), readyHeader.Seq);
                PlayerContext playerContext = GetPlayerContext(text);
                bool flag = playerContext.EntityId == text;
                Player player = new(text, connection, World, playerContext, flag);
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
                World.AddPlayer(player);
            }
            _connections.Remove(connection);
            _connectionDict.Remove(connection);
        });
        connection.ConnetionClosed += delegate
        {
            _connections.Remove(connection);
            _connectionDict.Remove(connection);
        };
        connection.StartReceive();
        _connections.Add(connection);
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
        msg.Region.CreatedAt = 0.0;
        msg.Region.Id = "1";
        msg.Region.Name = null;
        msg.Region.TemplateId = World.TerrainInfo.region_template;
        msg.Region.TerrainId = "1";
        msg.Region.Role = Role.Rural;
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
