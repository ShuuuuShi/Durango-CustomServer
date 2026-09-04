using System;
using System.Collections.Generic;
using Durango.Utils;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

/// <summary>
/// ตารางชนิดสัตว์ — อ่านจาก <c>data/assets/entity_types/animal.json</c> (214 ชนิด)
///
/// เก็บเฉพาะฟิลด์ที่เซิร์ฟต้องใช้จริง ที่เหลือ (โมเดล เสียง ท่าทาง สีขน) เป็นเรื่องของฝั่งเกม
///
/// ค่าสถานะในไฟล์เป็น **สูตรข้อความ** ไม่ใช่ตัวเลข เช่น
/// <code>life_max = (1.06 * ((combat_level + 24) ** 2)) * unstable_factor</code>
/// จึงเก็บเป็นสตริงไว้ แล้วให้ <see cref="StatFormula"/> คิดตอนสัตว์เกิด (เลเวลต่างกันได้ค่าต่างกัน)
///
/// โหลดครั้งเดียวตอนเรียกใช้ครั้งแรก — ไม่ต้องต่อสายจาก Program
/// </summary>
public static class AnimalTypes
{
    public class Info
    {
        public ushort EntityType;
        public string Name;            // ชื่อภายในของ NEXON เช่น "iguanodon"
        public string DisplayName;     // ชื่อที่โชว์ในเกม — คีย์แรกของ name (ข้อมูลเป็นเกาหลี)
        public string LifeMax;         // สูตร
        public string Attack;          // สูตร
        public string Defense;         // สูตร
        public int MinCombatLevel = 1;
        public int MaxCombatLevel = 99;
        public bool Tamable;
        public string PreferredFoodTag;
        public string DropItem;
        public string Kind;            // Herbivore / Carnivore / Omnivore …
        public float AttackCooltime = 2.2f;   // วินาทีต่อการโจมตีหนึ่งครั้ง

        /// <summary>
        /// ไล่กัดคนที่เดินผ่านเองไหม — ค่า <c>type</c> ในไฟล์มี 4 แบบเท่านั้น (นับจาก 214 ชนิด):
        /// Herbivore 139 (กินพืช — สู้เฉพาะตอนถูกตี) · Carnivore 69 · Scavenger 5 (กินซาก
        /// แต่เป็นสัตว์ดุ) · Sandbag 1 (หุ่นซ้อมมือ ไม่ควรตีใคร)
        /// </summary>
        public bool IsAggressive => Kind is "Carnivore" or "Scavenger";
    }

    private static Dictionary<ushort, Info> _byType;

    public static int Count
    {
        get
        {
            EnsureLoaded();
            return _byType.Count;
        }
    }

    public static Info Get(ushort entityType)
    {
        EnsureLoaded();
        return _byType.TryGetValue(entityType, out Info info) ? info : null;
    }

    private static void EnsureLoaded()
    {
        if (_byType != null) return;
        _byType = new Dictionary<ushort, Info>();

        JObject root = Json.ReadFromFile<JObject>("entity_types/animal");
        if (root == null)
        {
            Console.WriteLine("[สัตว์] ⚠️ อ่าน entity_types/animal.json ไม่ได้ — เกาะจะไม่มีสัตว์");
            return;
        }

        foreach (KeyValuePair<string, JToken> kv in root)
        {
            if (!ushort.TryParse(kv.Key, out ushort type) || kv.Value is not JObject o) continue;

            var info = new Info
            {
                EntityType = type,
                Name = (string)o["__name__"],
                LifeMax = (string)o["life_max"],
                Attack = (string)o["attack"],
                Defense = (string)o["defense"],
                Tamable = (bool?)o["tamable"] ?? false,
                PreferredFoodTag = (string)o["preferred_food_tag"],
                DropItem = (string)o["drop_item"],
                Kind = (string)o["type"],
                AttackCooltime = (float?)o["attack_cooltime"] ?? 2.2f
            };

            // "name": {"이구아노돈": null} — ฟอร์แมต gettext ของ NEXON: คีย์คือข้อความต้นทาง
            if (o["name"] is JObject nameNode)
            {
                foreach (JProperty prop in nameNode.Properties()) { info.DisplayName = prop.Name; break; }
            }

            if (o["combat_level_ranges"] is JArray range && range.Count == 2)
            {
                info.MinCombatLevel = Math.Max(1, (int?)range[0] ?? 1);
                info.MaxCombatLevel = Math.Max(info.MinCombatLevel, (int?)range[1] ?? 99);
            }

            _byType[type] = info;
        }

        Console.WriteLine($"[สัตว์] โหลดชนิดสัตว์ {_byType.Count} ชนิด");
    }
}
