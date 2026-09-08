using System;
using System.Collections.Generic;
using System.Globalization;
using Durango.Utils;   // Json.ReadFromFile — ตัวช่วยอ่าน data/assets ของโปรเจกต์ (Support/Json.cs)
using Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared.StatusEffect;

namespace Durango.Online;

/// <summary>
/// อ่าน <c>data/assets/survival/status_effects.json</c> สำหรับ duration / deactivates / ผลทุก type
/// ไม่แต่งตัวเลขเอง — ใช้ค่าในไฟล์ตรง ๆ แล้วคิดสูตรด้วย <see cref="StatFormula"/>
/// </summary>
public static class StatusEffectCatalog
{
    public sealed class EffectSpec
    {
        public int Type;
        public string Key;
        public string ValueExpr;
    }

    public sealed class Template
    {
        public string Id;
        public int MinLevel = 1;
        public int MaxLevel = 1;
        public double? DurationSeconds;
        public string[] Deactivates = Array.Empty<string>();
        public string[] Tags = Array.Empty<string>();
        /// <summary>ผลจาก JSON ทั้งก้อน — type/key/สูตร ยังไม่คิดเลขจนกว่าจะแพ็กตามเลเวล</summary>
        public List<EffectSpec> EffectSpecs = new();
        /// <summary>ผล type=1 (velocity ต่อวินาทีของหลอด) คิดที่ MinLevel — ใช้ตอนเซิร์ฟปรับหลอด</summary>
        public Dictionary<string, float> Type1Velocities = new();

        public Dictionary<string, float> GetType1Velocities(int level)
        {
            var d = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (EffectSpecs == null) return d;
            foreach (EffectSpec spec in EffectSpecs)
            {
                if (spec.Type != (int)EffectType.Survival) continue;
                if (string.IsNullOrEmpty(spec.Key) || string.IsNullOrEmpty(spec.ValueExpr)) continue;
                if (!StatFormula.TryEval(spec.ValueExpr, "level", level, out double v)) continue;
                d[spec.Key] = (float)v;
            }
            return d;
        }

        public EffectDetail[] PackDetails(int level)
        {
            if (EffectSpecs == null || EffectSpecs.Count == 0) return Array.Empty<EffectDetail>();
            var list = new List<EffectDetail>(EffectSpecs.Count);
            foreach (EffectSpec spec in EffectSpecs)
            {
                // Unpack ฝั่งเกมรับได้แค่ 0..14 (Messages/EffectDetail.cs) — ข้าม None/Invalid
                if (spec.Type <= (int)EffectType.None || spec.Type > 14) continue;
                if (string.IsNullOrEmpty(spec.Key) || string.IsNullOrEmpty(spec.ValueExpr)) continue;
                if (!StatFormula.TryEval(spec.ValueExpr, "level", level, out double v)) continue;
                list.Add(new EffectDetail
                {
                    Type = (EffectType)spec.Type,
                    Key = spec.Key,
                    Value = (float)v
                });
            }
            return list.ToArray();
        }
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
                string key = eo.Value<string>("key");
                string value = eo.Value<string>("value");
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) continue;
                t.EffectSpecs.Add(new EffectSpec { Type = type, Key = key, ValueExpr = value });
                // type=1 = Survival velocity ต่อหลอด — คิดที่ MinLevel ไว้ให้โค้ดเก่าที่อ่านฟิลด์นี้
                if (type == (int)EffectType.Survival &&
                    StatFormula.TryEval(value, "level", t.MinLevel, out double v))
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

    /// <summary>
    /// แพ็ก <see cref="EffectDetail"/> ให้ client โชว์ทูลทิป / คิด fatigue / modifier
    /// ไอคอนขึ้นได้จาก yaml แม้ Effects ว่าง แต่ตัวเลขจริงอ่านจากฟิลด์นี้เท่านั้น
    /// </summary>
    public static EffectDetail[] PackDetails(string id, int level)
    {
        Template t = Get(id, level);
        if (t == null) return Array.Empty<EffectDetail>();
        return t.PackDetails(level);
    }
}
