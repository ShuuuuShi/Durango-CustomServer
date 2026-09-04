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

    [JsonIgnore]
    public string Path { get; private set; }

    public void Initialize(string path)
    {
        Artifacts ??= new Dictionary<string, AppearArtifact>();
        ArtifactAddOns ??= new Dictionary<string, AddOns>();
        ArtifactMannequins ??= new Dictionary<string, Messages.Mannequin>();
        AddedNatural ??= new List<NaturalInfo>();
        RemovedNatural ??= new List<Point2>();
        GrazedPetList ??= new List<Pet>();
        Path = path;
    }

    [CanBeNull]
    public static WorldContext Load(string path)
    {
        WorldContext worldContext = null;
        try
        {
            byte[] data = File.ReadAllBytes(path);
            worldContext = Json.Read<WorldContext>(data);
            if (worldContext == null) return null;
            worldContext.Initialize(path);
        }
        catch (Exception e)
        {
            Console.WriteLine("[world-context] " + e.Message);
        }
        return worldContext;
    }

    public void Save(bool persistent = false)
    {
        if (Persistent && !persistent) return;
        if (persistent) Persistent = true;
        try
        {
            File.WriteAllBytes(Path, Json.WriteToBytes(this, indented: true));
        }
        catch (Exception e)
        {
            Console.WriteLine("[world-context] เซฟไม่สำเร็จ: " + e.Message);
        }
    }

    public static string MakePath(int slot, string clusterKey)
    {
        return System.IO.Path.Combine(AppData.CombinePath(GetBasePath(clusterKey)), slot + ".world");
    }

    public static string GetBasePath(string clusterKey) => System.IO.Path.Combine("offline", clusterKey);
}
