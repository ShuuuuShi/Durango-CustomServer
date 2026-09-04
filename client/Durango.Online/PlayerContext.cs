using System;
using System.Collections.Generic;
using System.IO;
using Durango.Logic.Clusters;
using Durango.Logic.Encyclopedia;
using Durango.UI;
using Durango.Utils;
using JetBrains.Annotations;
using Messages;
using Newtonsoft.Json;
using Shared.Player;
using UnityEngine;

namespace Durango.Online;

public class PlayerContext
{
	[JsonProperty("player_slot")]
	public int PlayerSlot;

	[JsonProperty("appear_player")]
	public AppearPlayer AppearPlayer;

	[JsonProperty("player_info")]
	public Durango.Logic.Clusters.PlayerInfo PlayerInfo;

	[JsonProperty("inventory_items")]
	public List<Item> InventoryItems;

	[JsonProperty("equipped_items")]
	public Dictionary<string, string> EquippedItems;

	[JsonProperty("musics")]
	public Dictionary<int, Music> Musics;

	[JsonProperty("storage")]
	public Dictionary<string, byte[]> Storage;

	[JsonIgnore]
	public string Path { get; private set; }

	[JsonIgnore]
	public string EntityId => (PlayerInfo == null) ? string.Empty : PlayerInfo.PlayerEntityId;

	public void Initialize(string path)
	{
		Path = path;
		if (PlayerInfo == null)
		{
			PlayerInfo = new Durango.Logic.Clusters.PlayerInfo();
			PlayerInfo.PlayerLevel = 60;
			string text = Guid.NewGuid().ToString();
			PlayerInfo.PlayerEntityId = text;
			PlayerInfo.PlayerName = text.Substring(0, 8);
			AppearPlayer.Name = PlayerInfo.PlayerName;
			AppearPlayer.Level = PlayerInfo.PlayerLevel;
			AppearPlayer.EntityId = text;
			AppearPlayer.IsAlive = true;
			AppearPlayer.Title.EntityId = text;
			AppearPlayer.Title._Title = string.Empty;
			AppearPlayer.Member.EntityId = text;
			AppearPlayer.Member.ClanId = string.Empty;
			AppearPlayer.Member.ClanName = string.Empty;
			AppearPlayer.Member.RoleId = -1;
			AppearPlayer.Move.EntityId = text;
			AppearPlayer.Survival.EntityId = text;
			Gauge gauge = new Gauge(100f, 0f, new GaugeNode[1]
			{
				new GaugeNode
				{
					Time = 0.0,
					Value = 100f
				}
			});
			AppearPlayer.Survival.Life = gauge;
			AppearPlayer.Survival.Gauges = new Dictionary<string, Gauge>();
			AppearPlayer.Survival.Gauges.Add("stamina", gauge);
			Gauge value = new Gauge(100f, 0f, new GaugeNode[1]
			{
				new GaugeNode
				{
					Time = 0.0,
					Value = 0f
				}
			});
			AppearPlayer.Survival.Gauges.Add("fatigue", value);
			Job[] array = Enums<Job>.Greater(Job.Invalid);
			Job job = array[UnityEngine.Random.Range(0, array.Length)];
			bool flag = UnityEngine.Random.Range(0, 2) == 1;
			EditPlayerDisplayProxy.FillRandomPlayerDisplayData(flag, job, ref AppearPlayer.Display);
			AppearPlayer.Display.DefaultBody = ((!flag) ? "Models/PC/Female/Body/f_body_nothing.FBX" : "Models/PC/Male/Body/m_body_nothing.FBX");
			AppearPlayer.Display.DefaultInner = ((!flag) ? "Models/PC/Female/Inner/f_inner_basic.FBX" : "Models/PC/Male/Inner/m_inner_basic.FBX");
			AppearPlayer.Display.Body = AppearPlayer.Display.DefaultBody;
			AppearPlayer.Display.EntityId = text;
		}
		if (InventoryItems == null)
		{
			InventoryItems = new List<Item>();
		}
		if (EquippedItems == null)
		{
			EquippedItems = new Dictionary<string, string>();
		}
		if (KUtility.GetSize(Storage) != 0)
		{
			return;
		}
		Storage = new Dictionary<string, byte[]>();
		MemoSystem.EncyclopediaStorage data = default(MemoSystem.EncyclopediaStorage);
		data.Memo.Memos = new List<KeyValuePair<MemoType, List<int>>>();
		List<int> list = new List<int>();
		for (int i = 0; i <= 227; i++)
		{
			if (!string.IsNullOrEmpty(MemoSystem.GetMemoText(MemoType.Tooltip, i)))
			{
				list.Add(i);
			}
		}
		data.Memo.Memos.Add(new KeyValuePair<MemoType, List<int>>(MemoType.Tooltip, list));
		list = new List<int>();
		for (int j = 1; j <= 243; j++)
		{
			if (!string.IsNullOrEmpty(MemoSystem.GetMemoText(MemoType.Fiction, j)))
			{
				list.Add(j);
			}
		}
		data.Memo.Memos.Add(new KeyValuePair<MemoType, List<int>>(MemoType.Fiction, list));
		Storage["encyclopedia"] = Json.WriteToBytes(data);
	}

	[CanBeNull]
	public static PlayerContext Load(string path)
	{
		PlayerContext playerContext = null;
		try
		{
			byte[] data = File.ReadAllBytes(path);
			playerContext = Json.Read<PlayerContext>(data);
			if (playerContext == null)
			{
				return null;
			}
			playerContext.Initialize(path);
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
		}
		return playerContext;
	}

	public void Save()
	{
		if (string.IsNullOrEmpty(Path))
		{
			return;
		}
		try
		{
			byte[] bytes = Json.WriteToBytes(this, indented: true);
			File.WriteAllBytes(Path, bytes);
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
		}
	}

	public static string MakePath(int slot, string clusterKey)
	{
		return global::System.IO.Path.Combine(AppData.CombinePath(WorldContext.GetBasePath(clusterKey)), slot + ".player");
	}
}
