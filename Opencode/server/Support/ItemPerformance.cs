using System;
using System.Collections.Generic;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

/// <summary>
/// ค่าพลังของไอเทมที่ส่งไปให้ฝั่งเกมโชว์ในหน้ารายละเอียด
///
/// ═══ ทำไมต้องมี ═══
/// <c>Item.Performance</c> เดิมแนบไปแค่ "ข้อความ" ไม่กี่ตัว (ชื่อโมเดล/ช่องสวมใส่/ชนิดอาวุธ)
/// **ไม่มีบล็อกตัวเลขเลยสักตัว** ⇒ เปิดดูรายละเอียดดาบเห็นแค่บรรทัด "ช่องสวมใส่"
/// ไม่มีค่าโจมตี/ความแม่นยำ/คริติคอล · เกราะไม่มีค่าป้องกัน · อาหารไม่บอกพลังงาน
///
/// ═══ ทำไมถึงเพิ่งทำได้ ═══
/// ค่าพวกนี้ในไฟล์เป็น **สูตรตามเลเวล** เช่น <c>attack = "72.02 + (level * 1.3)"</c>
/// ก่อนหน้านี้โปรเจกต์ไม่มีตัวคิดสูตรจึงข้ามไป ตอนนี้มี <see cref="StatFormula"/> แล้ว
/// และ <c>Cheats.MakeItem</c> ก็รู้เลเวลของไอเทมอยู่แล้ว ⇒ คิดออกมาเป็นตัวเลขจริงได้
///
/// ═══ วิธีทำ — ไม่มีการเลือกฟิลด์เอง ═══
/// ไล่ทุกหมวดใน <c>performance.json</c> หา prototype ของไอเทมนั้น แล้วแปลงทุกฟิลด์:
///   ตัวเลข          → <c>Nums</c>
///   ข้อความที่เป็นสูตร → คิดที่เลเวลของไอเทม แล้วลง <c>Nums</c>
///   ข้อความอื่น      → <c>Strs</c>
/// ⇒ ได้ครบตามที่ไฟล์มี ไม่ตกหล่นและไม่ต้องมานั่งไล่ชื่อฟิลด์ทีละตัว
/// </summary>
public static class ItemPerformance
{
    private static JObject _root;

    /// <summary>
    /// บล็อกค่าพลังทั้งหมดของไอเทมชิ้นนี้ — คืนลิสต์ว่างถ้าไม่เจอในหมวดไหนเลย
    /// </summary>
    public static List<Performance> Of(string prototypeId, int level)
    {
        var result = new List<Performance>();
        if (string.IsNullOrEmpty(prototypeId)) return result;

        EnsureLoaded();
        if (_root == null) return result;

        var vars = new Dictionary<string, double> { ["level"] = level };

        foreach (JProperty category in _root.Properties())
        {
            if (category.Value is not JObject byPrototype) continue;
            if (byPrototype[prototypeId] is not JObject byLevel) continue;

            // ชั้นในเป็นช่วงเลเวล "[1, 60]" — เอาแถวแรก (ค่าที่ต่างตามเลเวลอยู่ในสูตรอยู่แล้ว)
            JObject row = null;
            foreach (JProperty range in byLevel.Properties())
            {
                if (range.Value is JObject o) { row = o; break; }
            }
            if (row == null) continue;

            var nums = new Dictionary<string, float>();
            var strs = new Dictionary<string, string>();
            foreach (JProperty field in row.Properties())
            {
                switch (field.Value.Type)
                {
                    case JTokenType.Integer:
                    case JTokenType.Float:
                        nums[field.Name] = (float)field.Value;
                        break;

                    case JTokenType.Boolean:
                        nums[field.Name] = (bool)field.Value ? 1f : 0f;
                        break;

                    case JTokenType.String:
                        string text = (string)field.Value;
                        if (StatFormula.TryEval(text, vars, out double value))
                        {
                            nums[field.Name] = (float)value;      // เป็นสูตร → คิดที่เลเวลนี้
                        }
                        else if (!string.IsNullOrEmpty(text))
                        {
                            strs[field.Name] = text;              // ข้อความล้วน เช่นชื่อโมเดล
                        }
                        break;

                    // ที่เหลือ (dict/array เช่น atk_ratio, color_tables) โปรโตคอลไม่มีที่เก็บ — ข้าม
                }
            }

            if (nums.Count == 0 && strs.Count == 0) continue;
            result.Add(new Performance
            {
                Id = category.Name,
                Nums = nums.Count > 0 ? nums : null,
                Strs = strs.Count > 0 ? strs : null
            });
        }

        return result;
    }

    /// <summary>
    /// รวมบล็อกที่คิดมาใหม่เข้ากับบล็อกที่ผู้เรียกทำเองไว้แล้ว — ของเดิมชนะเมื่อคีย์ชนกัน
    ///
    /// จำเป็นเพราะ <c>Cheats.MakeItem</c> ประกอบบางบล็อกเองด้วยมือมาก่อน (add_on/weapon/armor/
    /// instrument/pet_food/reins) และบางค่าในนั้นไม่ได้มาจาก performance.json ตรง ๆ
    /// </summary>
    public static void MergeInto(List<Performance> target, string prototypeId, int level)
    {
        if (target == null) return;
        foreach (Performance extra in Of(prototypeId, level))
        {
            int index = target.FindIndex(p => p.Id == extra.Id);
            if (index < 0) { target.Add(extra); continue; }

            Performance existing = target[index];
            if (extra.Nums != null)
            {
                existing.Nums ??= new Dictionary<string, float>();
                foreach (var pair in extra.Nums) existing.Nums.TryAdd(pair.Key, pair.Value);
            }
            if (extra.Strs != null)
            {
                existing.Strs ??= new Dictionary<string, string>();
                foreach (var pair in extra.Strs) existing.Strs.TryAdd(pair.Key, pair.Value);
            }
            target[index] = existing;
        }
    }

    private static void EnsureLoaded()
    {
        _root ??= Json.ReadFromFile<JObject>("performance");
    }
}
