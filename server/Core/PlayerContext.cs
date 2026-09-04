using System;
using System.Collections.Generic;
using System.IO;
using Durango.Logic.Clusters;
using Durango.Logic.Encyclopedia;
using Durango.Utils;
using JetBrains.Annotations;
using Messages;
using Newtonsoft.Json;
using UnityEngine;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/PlayerContext.cs — ฟอร์แมตเซฟตรงต้นฉบับ (.player JSON)
// ความต่าง:
//  1) ต้นฉบับสุ่มหน้าตาผ่าน EditPlayerDisplayProxy (ฝั่ง UI client) — ที่นี่ตั้งค่า default เรียบ ๆ
//     (client สร้างหน้าตาจริงเองเสมอผ่าน POST /players model_info ตอน prologue)
//  2) blob "encyclopedia" ต้นฉบับเติม memo ที่มีข้อความทั้งหมด — เซิร์ฟไม่มีตารางภาษา เริ่มว่าง (MemoStorageDefaults)
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
    public string EntityId => PlayerInfo == null ? string.Empty : PlayerInfo.PlayerEntityId;

    public void Initialize(string path)
    {
        Path = path;
        if (PlayerInfo == null)
        {
            PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
            {
                PlayerLevel = 60
            };
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
            var gauge = new Gauge(100f, 0f, new[]
            {
                new GaugeNode { Time = 0.0, Value = 100f }
            });
            AppearPlayer.Survival.Life = gauge;
            AppearPlayer.Survival.Gauges = new Dictionary<string, Gauge> { { "stamina", gauge } };
            var fatigue = new Gauge(100f, 0f, new[]
            {
                new GaugeNode { Time = 0.0, Value = 0f }
            });
            AppearPlayer.Survival.Gauges.Add("fatigue", fatigue);
            AppearPlayer.Display.Body = "Models/PC/Male/Body/m_body_nothing.FBX";
            AppearPlayer.Display.DefaultBody = AppearPlayer.Display.Body;
            AppearPlayer.Display.DefaultInner = "Models/PC/Male/Inner/m_inner_basic.FBX";
            AppearPlayer.Display.BodySize = 0.5f;
            AppearPlayer.Display.EntityId = text;
        }
        InventoryItems ??= new List<Item>();
        EquippedItems ??= new Dictionary<string, string>();
        if (KUtility.GetSize(Storage) != 0) return;
        Storage = new Dictionary<string, byte[]>();
        var data = MemoStorageDefaults.Empty();
        Storage[MemoStorageDefaults.StorageKey] = Json.WriteToBytes(data);
    }

    [CanBeNull]
    public static PlayerContext Load(string path)
    {
        PlayerContext playerContext = null;
        try
        {
            byte[] data = File.ReadAllBytes(path);
            playerContext = Json.Read<PlayerContext>(data);
            if (playerContext == null) return null;
            playerContext.Initialize(path);
        }
        catch (Exception e)
        {
            Console.WriteLine("[player-context] " + e.Message);
        }
        return playerContext;
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(Path)) return;
        try
        {
            File.WriteAllBytes(Path, Json.WriteToBytes(this, indented: true));
        }
        catch (Exception e)
        {
            Console.WriteLine("[player-context] เซฟไม่สำเร็จ: " + e.Message);
        }
    }

    public static string MakePath(int slot, string clusterKey)
    {
        return System.IO.Path.Combine(AppData.CombinePath(WorldContext.GetBasePath(clusterKey)), slot + ".player");
    }
}
