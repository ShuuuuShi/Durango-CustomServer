using System;
using System.Collections.Generic;
using System.Globalization;
using Durango.Utils;   // Json.ReadFromFile — ตัวช่วยอ่าน data/assets ของโปรเจกต์ (Support/Json.cs)
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

/// <summary>
/// อ่าน <c>data/assets/survival/status_effects.json</c> สำหรับ duration / deactivates / ผล type-1
/// ไม่แต่งตัวเลขเอง — ใช้ค่าในไฟล์ตรง ๆ
/// </summary>
public static class StatusEffectCatalog
{
    public sealed class Template
    {
        public string Id;
        public int MinLevel = 1;
        public int MaxLevel = 1;
        public double? DurationSeconds;
        public string[] Deactivates = Array.Empty<string>();
        public string[] Tags = Array.Empty<string>();
        /// <summary>ผล type=1 (velocity ต่อวินาทีของหลอด) จาก JSON — key เป็นชื่อหลอด survival</summary>
        public Dictionary<string, float> Type1Velocities = new();
    }

    private static Dictionary<string, List<Template>> _byId;

    private static void EnsureLoaded()
    {
        if (_byId != null) return;
        _byId = new Dictionary<string, List<Template>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var root = Json.ReadFromFile<Dictionary<string, JToken>>("survival/status_effects");
            if (root == null) return;
            foreach (KeyValuePair<string, JToken> kv in root)
            {
                if (kv.Value is not JArray arr) continue;
                var list = new List<Template>();
                foreach (JToken node in arr)
                {
                    if (node is not JObject obj) continue;
                    Template t = Parse(kv.Key, obj);
                    if (t != null) list.Add(t);
                }
                if (list.Count > 0) _byId[kv.Key] = list;
            }
            Console.WriteLine($"[สถานะ] โหลด status_effects {_byId.Count} ชนิด");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[สถานะ] อ่าน status_effects.json ไม่สำเร็จ: {e.Message}");
        }
    }

    private static Template Parse(string id, JObject obj)
    {
        var t = new Template
        {
            Id = id,
            MinLevel = obj.Value<int?>("min_level") ?? 1,
            MaxLevel = obj.Value<int?>("max_level") ?? 1
        };
        if (obj["duration"] != null &&
            double.TryParse(obj.Value<string>("duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out double dur) &&
            dur > 0)
        {
            t.DurationSeconds = dur;
        }
        if (obj["deactivates"] is JArray deact)
        {
            var ids = new List<string>();
            foreach (JToken x in deact)
            {
                string s = x?.ToString();
                if (!string.IsNullOrEmpty(s)) ids.Add(s);
            }
            t.Deactivates = ids.ToArray();
        }
        if (obj["tags"] is JArray tags)
        {
            var ids = new List<string>();
            foreach (JToken x in tags)
            {
                string s = x?.ToString();
                if (!string.IsNullOrEmpty(s)) ids.Add(s);
            }
            t.Tags = ids.ToArray();
        }
        if (obj["effects"] is JArray effects)
        {
            foreach (JToken effect in effects)
            {
                if (effect is not JObject eo) continue;
                int type = eo.Value<int?>("type") ?? 0;
                if (type != 1) continue; // รอบนี้ใช้แค่ velocity ต่อหลอด
                string key = eo.Value<string>("key");
                string value = eo.Value<string>("value");
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) continue;
                // ค่าในไฟล์ส่วนใหญ่เป็นตัวเลขคงที่; สูตรมี level ค่อยรองรับตอนเลือกเลเวล
                if (StatFormula.TryEval(value, "level", t.MinLevel, out double v))
                {
                    t.Type1Velocities[key] = (float)v;
                }
            }
        }
        return t;
    }

    public static Template Get(string id, int level = 1)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(id) || !_byId.TryGetValue(id, out List<Template> list) || list.Count == 0)
        {
            return null;
        }
        Template best = null;
        foreach (Template t in list)
        {
            if (level < t.MinLevel || level > t.MaxLevel) continue;
            if (best == null || t.MinLevel > best.MinLevel) best = t;
        }
        return best ?? list[0];
    }

    public static double DurationOrDefault(string id, int level, double? overrideSeconds)
    {
        if (overrideSeconds.HasValue && overrideSeconds.Value > 0) return overrideSeconds.Value;
        Template t = Get(id, level);
        return t?.DurationSeconds ?? 0;
    }
}
