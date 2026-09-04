using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Durango.Logic.Clusters;
using Durango.Utils;
using UnityEngine;

namespace Durango.Online;

public class Server
{
	private static Gateway _gateway;

	private static GameServer _gameServer;

	private static PlayerContext _localPlayer;

	public string Key { get; private set; }

	public List<Context> Contexts { get; private set; }

	public Cluster Cluster { get; private set; }

	public Server(string key, Dictionary<string, string> names)
	{
		Server server = this;
		Key = key;
		Contexts = new List<Context>();
		Cluster = new Cluster();
		Cluster.Mode = ((!(key == "free")) ? Mode.Offline : Mode.Editable);
		Cluster.Names = names;
		int num = 8190;
		Cluster.GatewayUrlRoot = "http://127.0.0.1:" + num;
		Cluster.OnRequestAccount = delegate(Action<Account> action)
		{
			Account account = new Account
			{
				Players = new List<PlayerInfo>()
			};
			foreach (Context context2 in server.Contexts)
			{
				PlayerContext player = context2.Player;
				PlayerInfo playerInfo = player.PlayerInfo;
				playerInfo.OfflineFunc = () => new Pair<PortraitBuilder.Argument, int>(player.AppearPlayer.Display.GetPortraitArgument(player.AppearPlayer.EntityId, player.AppearPlayer.IsMale()), player.AppearPlayer.Freq);
				account.Players.Add(playerInfo);
			}
			account.MaxPlayerSlotCount = Mathf.Max(2, (server.Cluster.Mode != Mode.Editable) ? account.Players.Count : 7);
			account.PlayerSlotCount = ((server.Cluster.Mode != Mode.Editable) ? account.Players.Count : 7);
			action?.Invoke(account);
		};
		Cluster.OnDeletePlayer = delegate(string entityId)
		{
			for (int i = 0; i < server.Contexts.Count; i++)
			{
				Context context = server.Contexts[i];
				if (context.EntityId == entityId)
				{
					try
					{
						File.Delete(context.Player.Path);
						File.Delete(context.World.Path);
					}
					catch (Exception)
					{
					}
					server.Contexts.RemoveAt(i);
					break;
				}
			}
		};
		Cluster.OnConfirm = delegate(string entityId)
		{
			Context context = server.Contexts.Find((Context x) => x.EntityId == entityId);
			if (context == null)
			{
				int num2 = 0;
				if (server.Contexts.Count > 0)
				{
					num2 = server.Contexts[server.Contexts.Count - 1].PlayerSlot + 1;
				}
				WorldContext worldContext = new WorldContext();
				worldContext.Initialize(WorldContext.MakePath(num2, key));
				worldContext.PlayerSlot = num2;
				PlayerContext playerContext2 = new PlayerContext();
				playerContext2.Initialize(PlayerContext.MakePath(num2, key));
				playerContext2.PlayerSlot = num2;
				context = new Context(worldContext, playerContext2);
				server.Contexts.Add(context);
			}
			BeginServer(context.World, context.Player);
		};
		string[] files = AppData.GetFiles(WorldContext.GetBasePath(Key), "*.world", SearchOption.TopDirectoryOnly);
		if (files == null)
		{
			return;
		}
		IEnumerable<WorldContext> enumerable = from x in files.Select(WorldContext.Load)
			where x != null
			select x;
		string[] files2 = AppData.GetFiles(WorldContext.GetBasePath(Key), "*.player", SearchOption.TopDirectoryOnly);
		Dictionary<int, PlayerContext> dictionary = new Dictionary<int, PlayerContext>();
		if (files2 != null)
		{
			foreach (PlayerContext item in from x in files2.Select(PlayerContext.Load)
				where x != null
				select x)
			{
				dictionary[item.PlayerSlot] = item;
			}
		}
		Contexts = new List<Context>();
		foreach (WorldContext item2 in enumerable)
		{
			PlayerContext playerContext = dictionary.Get(item2.PlayerSlot);
			if (playerContext == null)
			{
				playerContext = new PlayerContext();
				playerContext.Initialize(PlayerContext.MakePath(item2.PlayerSlot, Key));
			}
			Contexts.Add(new Context(item2, playerContext));
		}
		Contexts = Contexts.OrderBy((Context x) => x.PlayerSlot).ToList();
	}

	private static void BeginServer(WorldContext worldCtx, PlayerContext playerCtx)
	{
		EndServer();
		_gameServer = new GameServer(worldCtx, playerCtx);
		_gateway = new Gateway(_gameServer, worldCtx, playerCtx);
		_localPlayer = playerCtx;
	}

	public static void EndServer()
	{
		if (_gateway != null)
		{
			_gateway.Close();
			_gateway = null;
		}
		if (_gameServer != null)
		{
			_gameServer.Close();
			_gameServer = null;
		}
	}

	public static void Process()
	{
		if (_gateway != null)
		{
			_gateway.Process();
		}
		if (_gameServer != null)
		{
			_gameServer.Process();
		}
	}

	public static void ConnectTo(string ip)
	{
		Cluster cluster = new Cluster();
		if (ip.StartsWith("http://"))
		{
			ip = ip.Substring(7);
		}
		string gatewayUrlRoot = "http://" + ip + ":" + 8190;
		cluster.OnRequestAccount = delegate(Action<Account> action)
		{
			Account account = new Account();
			account.MaxPlayerSlotCount = 7;
			account.PlayerSlotCount = 1;
			account.Players = new List<PlayerInfo>();
			account.Players.Add(_localPlayer.PlayerInfo);
			action?.Invoke(account);
		};
		cluster.LocalPlayer = Json.Write(_localPlayer);
		cluster.GatewayUrlRoot = gatewayUrlRoot;
		cluster.Mode = Mode.Offline;
		GameManager.ConnectCluster = cluster;
		GameManager.Emigrated = GameManager.EmigratedType.Explore;
		Singleton<GameManager>.Instance().MoveToTitle();
	}
}
