using System;
using System.Collections.Generic;
using System.Net.Sockets;
using Durango.Network;
using Durango.Utils;
using JetBrains.Annotations;
using Messages;
using Shared.Region;

namespace Durango.Online;

public class GameServer
{
	public const int DefaultPort = 8191;

	private readonly Listener _listener;

	private readonly PlayerContext _playerCtx;

	private readonly Dictionary<string, PlayerContext> _playerContexts = new Dictionary<string, PlayerContext>();

	private readonly List<Connection> _connections = new List<Connection>();

	private readonly Dictionary<Connection, string> _connectionDict = new Dictionary<Connection, string>();

	public World World { get; private set; }

	public int Port { get; private set; }

	public GameServer(WorldContext worldCtx, PlayerContext playerCtx)
	{
		_listener = new Listener();
		Port = 8191;
		_listener.Start(Port);
		_listener.ClientAccepted += Listener_ClientAccepted;
		World = new World(worldCtx);
		_playerCtx = playerCtx;
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

	public bool Register(PlayerContext context)
	{
		if (context != null && !string.IsNullOrEmpty(context.EntityId))
		{
			_playerContexts[context.EntityId] = context;
			return true;
		}
		return false;
	}

	private void Listener_ClientAccepted(Socket socket)
	{
		Connection connection = new Connection(socket);
		connection.Recv(delegate(GetClock getClock, PacketHeader header)
		{
			Clock msg = default(Clock);
			msg.ClientTime = getClock.Time;
			msg.ServerTime = Times.UnixTimeNow();
			connection.Send(msg, header.Seq);
		});
		connection.Recv(delegate(Auth auth, PacketHeader header)
		{
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
				Player player = new Player(text, connection, World, playerContext, flag);
				if (flag)
				{
					player.ContextChanged += delegate
					{
						_playerCtx.Save();
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

	[NotNull]
	private PlayerContext GetPlayerContext(string entityId)
	{
		PlayerContext playerContext = _playerContexts.Get(entityId);
		if (playerContext == null)
		{
			playerContext = _playerCtx;
		}
		return playerContext;
	}

	private void SendWelcome(Connection connection, string entityId, string name, uint seq)
	{
		Welcome msg = new Welcome
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
		BoolOption[] array = new BoolOption[1]
		{
			new BoolOption
			{
				Key = "market.ui_enabled",
				Value = true
			}
		};
		IntegerOption[] array2 = new IntegerOption[1]
		{
			new IntegerOption
			{
				Key = "market.search.limit",
				Value = 20L
			}
		};
		msg.Options.Bool = array;
		msg.Options.Int = array2;
		connection.Send(msg, seq);
	}
}
