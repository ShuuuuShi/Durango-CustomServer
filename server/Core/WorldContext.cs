using System;
using System.Collections.Generic;
using System.IO;
using Durango.Terrain;
using Durango.Utils;
using JetBrains.Annotations;
using Messages;
using Newtonsoft.Json;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/WorldContext.cs — ฟอร์แมตเซฟตรงต้นฉบับ (.world JSON)
public class WorldContext
{
    [JsonProperty("player_slot")]
    public int PlayerSlot;

    [JsonProperty("terrain_id")]
    public string TerrainId;

    [JsonProperty("artifacts")]
    public Dictionary<string, AppearArtifact> Artifacts;

    [JsonProperty("artifact_addOns")]
    public Dictionary<string, AddOns> ArtifactAddOns;

    [JsonProperty("artifact_mannequins")]
    public Dictionary<string, Messages.Mannequin> ArtifactMannequins;

    [JsonProperty("added_natural")]
    public List<NaturalInfo> AddedNatural;

    [JsonProperty("removed_natural")]
    public List<Point2> RemovedNatural;

    [JsonProperty("grazed_pet_list")]
    public List<Pet> GrazedPetList;

    [JsonProperty("garden")]
    public byte[] Garden;

    [JsonProperty("persistent")]
    public bool Persistent;

    /// <summary>
    /// [5 ก.ย. 2026] แปลงไหนปลูกเมล็ดอะไร — entity ของแปลง → prototype ของเมล็ด
    ///
    /// ต้องจำแยกเพราะ <c>Messages.Farming</c> ไม่มีช่องเก็บชนิดเมล็ด (มีแต่ชื่อที่โชว์)
    /// แล้วตอนพืชโตเต็มที่ เซิร์ฟต้องรู้ว่าจะสลับเป็นโมเดลชุดไหนของ crops.json
    /// เก็บลงไฟล์เกาะเพราะแปลงเป็นของโลก ไม่ใช่ของคนปลูก (คนอื่นเดินผ่านต้องเห็นเหมือนกัน)
    /// </summary>
    [JsonProperty("plantings", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, string> Plantings;

    // ของในตู้/คลังของสิ่งปลูกสร้างบนเกาะนี้ (ดู Player.WarehouseStore)
    // เดิมอยู่ในหน่วยความจำอย่างเดียว รีสตาร์ตแล้วของหายเกลี้ยง
    [JsonProperty("warehouses", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, Player.WarehouseStore.Box> Warehouses;

    [JsonIgnore]
    public string Path { get; private set; }

    public void Initialize(string path)
    {
        Artifacts ??= new Dictionary<string, AppearArtifact>();
        ArtifactAddOns ??= new Dictionary<string, AddOns>();
        ArtifactMannequins ??= new Dictionary<string, Messages.Mannequin>();
        Plantings ??= new Dictionary<string, string>();
        AddedNatural ??= new List<NaturalInfo>();
        RemovedNatural ??= new List<Point2>();
        GrazedPetList ??= new List<Pet>();
        Path = path;
        Player.WarehouseStore.Import(Warehouses);
        // ⚠️ ต้องทำก่อนที่ artifact จะถูกส่งออกไปหาใคร — ดูเหตุผลเต็มที่ CageTypes.NormalizeLoaded
        CageTypes.NormalizeLoaded(Artifacts);
        NormalizeLoadedItems();
    }

    /// <summary>
    /// ซ่อม <c>Item.Ext</c> ของไอเทมที่ผูกกับสิ่งปลูกสร้างบนเกาะนี้
    ///
    /// ไฟล์ .world เก็บไอเทมไว้ 3 ที่ และทุกที่ผ่าน JSON เหมือนกันหมด ⇒ <c>Ext</c> กลับมาเป็น
    /// <c>JObject</c> ซึ่ง <c>Item.Pack</c> ไม่มี else รองรับ = ไม่เขียนอะไรลงไปเลยสักไบต์
    /// ทำให้อีก 6 ฟิลด์ท้ายของไอเทมเลื่อนตำแหน่งกันหมด (เหตุผลเต็มที่หัวคลาส ItemExtRepair)
    ///   • Warehouses      ของในตู้/คลัง — ซ่อมที่ Player.WarehouseStore.Import
    ///   • ArtifactAddOns  ประตู/หน้าต่างที่ติดกับบ้าน
    ///   • ArtifactMannequins เสื้อผ้าบนหุ่นโชว์
    /// </summary>
    private void NormalizeLoadedItems()
    {
        if (ArtifactAddOns != null)
        {
            foreach (string entityId in new List<string>(ArtifactAddOns.Keys))
            {
                AddOns addons = ArtifactAddOns[entityId];
                if (addons._AddOns == null) continue;
                foreach (int slot in new List<int>(addons._AddOns.Keys))
                {
                    addons._AddOns[slot] = ItemExtRepair.Fix(addons._AddOns[slot], "ของติดบ้าน");
                }
                ArtifactAddOns[entityId] = addons;
            }
        }

        if (ArtifactMannequins == null) return;
        foreach (string entityId in new List<string>(ArtifactMannequins.Keys))
        {
            Messages.Mannequin mannequin = ArtifactMannequins[entityId];
            if (mannequin.Head.HasValue) mannequin.Head = ItemExtRepair.Fix(mannequin.Head.Value, "หุ่นโชว์");
            if (mannequin.Body.HasValue) mannequin.Body = ItemExtRepair.Fix(mannequin.Body.Value, "หุ่นโชว์");
            ArtifactMannequins[entityId] = mannequin;
        }
    }

    [CanBeNull]
    public static WorldContext Load(string path)
    {
        // อ่านไฟล์หลักก่อน ถ้าพังถอยไป .bak — เดิมคืน null เฉย ๆ แล้วผู้เรียกสร้างโลกใหม่ทับ
        // ⇒ ไฟล์เสียครั้งเดียวเกาะหายถาวรโดยไม่มีใครรู้
        WorldContext worldContext = SafeSave.ReadWithBackup(path, "world-context", data => Json.Read<WorldContext>(data));
        worldContext?.Initialize(path);
        return worldContext;
    }

    public void Save(bool persistent = false)
    {
        if (Persistent && !persistent) return;
        if (persistent) Persistent = true;
        // เก็บของในตู้ของสิ่งปลูกสร้างบนเกาะนี้ลงไปด้วย (กรองด้วยรายชื่อ artifact ของเกาะเอง)
        Warehouses = Player.WarehouseStore.Export(Artifacts?.Keys);
        // เขียนแบบสลับเข้าที่ + เก็บ .bak — ปิดเซิร์ฟกลางเซฟแล้วไฟล์ยังอยู่ครบ (ดู SafeSave)
        SafeSave.WriteAtomic(Path, Json.WriteToBytes(this, indented: false), "world-context");
    }

    public static string MakePath(int slot, string clusterKey)
    {
        return System.IO.Path.Combine(AppData.CombinePath(GetBasePath(clusterKey)), slot + ".world");
    }

    public static string GetBasePath(string clusterKey) => System.IO.Path.Combine("offline", clusterKey);
}
