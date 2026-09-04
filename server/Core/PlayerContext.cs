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

    /// <summary>
    /// [5 ก.ย. 2026] เกาะที่ผู้เล่นอยู่ตอนนี้ (= terrain id ดู RegionCatalog) — ว่าง = เกาะตั้งต้น
    ///
    /// ต้นฉบับไม่มีฟิลด์นี้เพราะเซิร์ฟในตัวของเกมมีโลกเดียวเสมอ พอทำระบบล่องเรือแล้ว
    /// ผู้เล่นแต่ละคนอยู่คนละเกาะได้ ⇒ ต้องจำไว้กับตัวผู้เล่น ไม่ใช่กับเซิร์ฟ
    ///
    /// การย้ายเกาะทำผ่านการต่อใหม่: เซิร์ฟส่ง Emigrated แล้วเกมตัดการเชื่อมต่อเอง
    /// (client/GameManager.cs:316-331 EmigratedReceived → Connections.Frontend.Close())
    /// รอบต่อไปที่ต่อเข้ามา เซิร์ฟอ่านค่านี้แล้วส่งเข้าโลกของเกาะปลายทาง
    /// </summary>
    [JsonProperty("region_id")]
    public string RegionId;

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
            AppearPlayer.Display.Body = "Models/PC/Male/Body/m_body_nothing.FBX";
            AppearPlayer.Display.DefaultBody = AppearPlayer.Display.Body;
            AppearPlayer.Display.DefaultInner = "Models/PC/Male/Inner/m_inner_basic.FBX";
            AppearPlayer.Display.BodySize = 0.5f;
            AppearPlayer.Display.EntityId = text;
        }
        InventoryItems ??= new List<Item>();
        EquippedItems ??= new Dictionary<string, string>();
        // [5 ก.ย. 2026] สร้าง/ซ่อมหลอดสถานะจากข้อมูลจริงทุกครั้งที่เปิด context ไม่ใช่แค่ตอนสร้างใหม่
        //
        // ทำไมต้องทำตอนโหลดด้วย: GaugeConverter ย่อ Gauge เป็น {min,max,cur} ตอนเขียนไฟล์เซฟ
        // (Support/GaugeConverter.cs:13-20) ⇒ เส้นแนวโน้มหายหมด เหลือ node เดียวที่ Time = 0
        // ถ้าไม่สร้างใหม่ หลอดจะค้างนิ่งตลอดเกม · SurvivalState หยิบค่า cur ที่เซฟไว้ไปตั้งต้นให้เอง
        //
        // ⚠️ ของเดิมเอา Gauge ก้อนเดียวใส่ทั้ง Survival.Life และ Gauges["stamina"] ⇒ HP กับ
        // ความอึดเดินพร้อมกันเป๊ะ · ตอนนี้แยกก้อนตามนิยามจริงใน entity_types/players.json
        SurvivalState.Reset(this);
        if (KUtility.GetSize(Storage) != 0) return;
        Storage = new Dictionary<string, byte[]>();
        var data = MemoStorageDefaults.Empty();
        Storage[MemoStorageDefaults.StorageKey] = Json.WriteToBytes(data);
    }

    [CanBeNull]
    public static PlayerContext Load(string path)
    {
        PlayerContext playerContext = SafeSave.ReadWithBackup(path, "player-context", data => Json.Read<PlayerContext>(data));
        playerContext?.Initialize(path);
        return playerContext;
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(Path)) return;
        SafeSave.WriteAtomic(Path, Json.WriteToBytes(this, indented: false), "player-context");
    }

    public static string MakePath(int slot, string clusterKey)
    {
        return System.IO.Path.Combine(AppData.CombinePath(WorldContext.GetBasePath(clusterKey)), slot + ".player");
    }
}
