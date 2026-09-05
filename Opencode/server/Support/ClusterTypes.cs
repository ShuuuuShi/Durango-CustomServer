using System.Collections.Generic;
using Newtonsoft.Json;

namespace Durango.Logic.Clusters;

// พอร์ตจาก nexonSRC/Durango.Logic.Clusters — เฉพาะ type ที่เซิร์ฟแท้ใช้
// (Mode enum, PlayerInfo สำหรับเซฟ/บัญชี, Account สำหรับ /sessions เส้นทาง LAN)
// ตัด OfflineFunc/GetPlayerInfoText (ฝั่ง UI/PotraitBuilder ของ client) ออก
public enum Mode
{
    Online,
    Offline,
    Editable
}

public class PlayerInfo
{
    [JsonProperty("player_level")]
    public int PlayerLevel;

    [JsonProperty("disconnected_at")]
    public double DisconnectedAt;

    [JsonProperty("player_name")]
    public string PlayerName;

    [JsonProperty("player_entity_id")]
    public string PlayerEntityId;

    [JsonProperty("deletes_at")]
    public double? DeletesAt;

    [JsonIgnore]
    public bool IsSoftDeleted => DeletesAt.HasValue;
}

public class Account
{
    [JsonProperty("players")]
    public List<PlayerInfo> Players = new();

    [JsonProperty("player_slot_count")]
    public int PlayerSlotCount;

    [JsonProperty("max_player_slot_count")]
    public int MaxPlayerSlotCount;
}
