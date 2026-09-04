using System.Collections.Generic;
using Durango.Utils;
using Newtonsoft.Json;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/PerformanceYaml.cs
// ต้นฉบับอ่าน Resources "offline/assets/performance" — ที่นี่อ่าน data/assets/performance.json
public static class PerformanceYaml
{
    public class PerformanceRoot
    {
        [JsonProperty("add_on")]
        public Dictionary<string, Dictionary<string, AddOn>> AddOnDict;

        [JsonProperty("weapon")]
        public Dictionary<string, Dictionary<string, Weapon>> WeaponDict;

        [JsonProperty("armor")]
        public Dictionary<string, Dictionary<string, Armor>> ArmorDict;

        [JsonProperty("instrument")]
        public Dictionary<string, Dictionary<string, Instrument>> InstrumentDict;

        [JsonProperty("reins")]
        public Dictionary<string, Dictionary<string, Rein>> ReinsDict;
    }

    public class AddOn
    {
        [JsonProperty("add_on_model_key")]
        public string AddOnModelKey;
    }

    public class Weapon
    {
        [JsonProperty("weapon_framework")]
        public string WeaponFramework;

        [JsonProperty("model")]
        public string Model;

        [JsonProperty("slot")]
        public string Slot;
    }

    public class Armor
    {
        [JsonProperty("emotional_motions")]
        public string[] EmotionalMotions;

        [JsonProperty("female_model")]
        public string FemaleModel;

        [JsonProperty("male_model")]
        public string MaleModel;

        [JsonProperty("slot")]
        public string Slot;
    }

    public class Instrument
    {
        [JsonProperty("timbre")]
        public string Timbre;
    }

    public class Rein
    {
        [JsonProperty("pet_name")]
        public Gettext PetName;

        [JsonProperty("pet_entity_type")]
        public int PetEntityType;

        [JsonProperty("playback_rate")]
        public float PlaybackRate;
    }

    private static PerformanceRoot _performances;

    private static PerformanceRoot Performances =>
        _performances ??= Json.ReadFromFile<PerformanceRoot>("offline/assets/performance");

    public static bool TryGetAddOnModelKey(string prototypeId, out string modelKey)
    {
        if (Performances?.AddOnDict != null && Performances.AddOnDict.TryGetValue(prototypeId, out var value))
        {
            using var enumerator = value.GetEnumerator();
            if (enumerator.MoveNext())
            {
                modelKey = enumerator.Current.Value.AddOnModelKey;
                return true;
            }
        }
        modelKey = null;
        return false;
    }

    public static Armor GetArmor(string prototypeId)
    {
        if (string.IsNullOrEmpty(prototypeId) || Performances?.ArmorDict == null) return null;
        if (Performances.ArmorDict.TryGetValue(prototypeId, out var value))
        {
            using var enumerator = value.GetEnumerator();
            if (enumerator.MoveNext()) return enumerator.Current.Value;
        }
        return null;
    }

    public static Weapon GetWeapon(string prototypeId)
    {
        if (string.IsNullOrEmpty(prototypeId) || Performances?.WeaponDict == null) return null;
        if (Performances.WeaponDict.TryGetValue(prototypeId, out var value))
        {
            using var enumerator = value.GetEnumerator();
            if (enumerator.MoveNext()) return enumerator.Current.Value;
        }
        return null;
    }

    public static Instrument GetInstrument(string prototypeId)
    {
        if (string.IsNullOrEmpty(prototypeId) || Performances?.InstrumentDict == null) return null;
        if (Performances.InstrumentDict.TryGetValue(prototypeId, out var value))
        {
            using var enumerator = value.GetEnumerator();
            if (enumerator.MoveNext()) return enumerator.Current.Value;
        }
        return null;
    }

    public static Rein GetRein(string prototypeId)
    {
        if (string.IsNullOrEmpty(prototypeId) || Performances?.ReinsDict == null) return null;
        if (Performances.ReinsDict.TryGetValue(prototypeId, out var value))
        {
            using var enumerator = value.GetEnumerator();
            if (enumerator.MoveNext()) return enumerator.Current.Value;
        }
        return null;
    }
}
