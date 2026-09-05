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

        // อาหารสัตว์ — ฝั่งเกมกรองไอเทมที่ป้อนสัตว์ได้ด้วยหมวดนี้ (client/Durango.UI/PetUtil.cs:128-133)
        // ไม่แนบไป หน้าต่างเลือกอาหารสัตว์จะว่างเปล่าทั้งที่มีอาหารอยู่ในกระเป๋า
        [JsonProperty("pet_food")]
        public Dictionary<string, Dictionary<string, PetFood>> PetFoodDict;
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

        // ลูกกระสุน/ลูกศร — ไม่ส่งไปฝั่งเกมจะไม่มีลูกศรติดคันธนู ไม่มีลูกพุ่งออกไป ไม่มีเสียง
        // (client/ProjectileController.cs:100-104 เจอ Projectile ว่างแล้ว return ทันที
        //  ⇒ MakeArrow คืน null · ShootProjectile ออกก่อน = ยิงลม)
        // performance.json → weapon มีคีย์นี้จริง 26 รายการ เช่น bow_metal_01 → arrow / 3000
        [JsonProperty("projectile")]
        public string Projectile;

        [JsonProperty("projectile_speed")]
        public float? ProjectileSpeed;

        // ความเร็วเดินตอนอยู่ในโหมดต่อสู้ (300/350/400 ตามชนิดอาวุธ) — ใช้กับ SetBaseMoveSpeed
        [JsonProperty("battle_speed")]
        public float? BattleSpeed;
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

    public class PetFood
    {
        [JsonProperty("vigor")]
        public float Vigor;
    }

    /// <summary>
    /// บังเหียน — performance.json → reins → &lt;prototype&gt; → "[1, 60]"
    ///
    /// ต้นฉบับฝั่งเกม (client/Durango.Online/PerformanceYaml.cs:65-75) อ่านแค่ 3 คีย์แรก
    /// เพราะฝั่งเกมใช้คลาสนี้เฉพาะโหมด offline · แต่ **เซิร์ฟต้องใช้มากกว่านั้น** เพราะเป็นคนประกอบ
    /// ก้อน <c>Item.Ext = Messages.Reins</c> ให้ ซึ่งมีช่อง VehicleEntityType กับ Size อยู่ในโปรโตคอล
    /// (server/GameCode/Messages/Reins.cs:9-13) ⇒ ไฟล์จริงมี 10 คีย์ ตรวจแล้วครบทั้ง 101 รายการ
    ///
    /// ไม่มี <c>size</c> ⇒ ส่ง Size = 0 ให้ฝั่งเกม แล้วหน้าต่างใส่สัตว์เข้ากรงจะโชว์ "크기 0"
    /// และเช็ค "กรงมีที่ว่างพอไหม" ผ่านตลอด (client/Durango.UI.Popup/PetItemInteractionPopup.cs:597-608
    /// เทียบ <c>target.Value.Size &gt; cage.RemainSize</c>) ⇒ ยัดสัตว์เข้ากรงได้ไม่จำกัด
    /// </summary>
    public class Rein
    {
        [JsonProperty("pet_name")]
        public Gettext PetName;

        [JsonProperty("pet_entity_type")]
        public int PetEntityType;

        [JsonProperty("playback_rate")]
        public float PlaybackRate;

        /// <summary>
        /// ชนิด entity ของ "ตัวสัตว์" ที่เอาไปหาโมเดล/รูปหน้า — คนละเลขกับ pet_entity_type
        /// (เช่น reins_retriever_labrador: pet 3072 / vehicle 2131)
        ///
        /// ฝั่งเกมหาโมเดลด้วย <c>Yaml.Pet.VehicleEntityType</c> ที่ค้นจาก pet_entity_type อีกที
        /// (client/Durango.UI.Popup/PetItemInteractionPopup.cs:573-575) แต่ช่องนี้ยังต้องส่งให้ตรง
        /// เพราะเซิร์ฟเองใช้แยกสัตว์ยาก/ง่ายจาก constants.json → pet → advanced_tameable
        /// ซึ่งเป็นรายการของ vehicle_entity_type ไม่ใช่ pet_entity_type
        /// </summary>
        [JsonProperty("vehicle_entity_type")]
        public int VehicleEntityType;

        /// <summary>ที่ที่สัตว์ตัวนี้กินในกรง (7-130) — ไม่ใช่ขนาดไอเทมในกระเป๋า</summary>
        [JsonProperty("size")]
        public int Size;

        /// <summary>ความเร็ววิ่ง — ลง Derived.Speed ของสัตว์เลี้ยง</summary>
        [JsonProperty("speed")]
        public float Speed;

        /// <summary>ช่องกระเป๋าของสัตว์ — ฝั่งเกมโชว์ที่ไอคอนไอเทม (client/Durango.UI/ItemIconWidget.cs:528)</summary>
        [JsonProperty("capacity")]
        public float Capacity;

        /// <summary>
        /// เพดานหลอดอิ่ม — ในไฟล์เป็น **สตริง** ("300.0" ทั้ง 101 รายการ) เพราะช่องนี้เป็นสูตรตามเลเวล
        /// ⇒ ประกาศเป็น string แล้วให้ผู้เรียกคิดสูตรเอง (ประกาศเป็น float ไว้ วันหน้าเจอสูตรจริงจะ throw)
        /// </summary>
        [JsonProperty("hungry_max")]
        public string HungryMaxExpr;

        /// <summary>อัตราหิวต่อวินาที (ค่าติดลบ เช่น -0.05)</summary>
        [JsonProperty("hungry_velocity")]
        public float HungryVelocity;

        /// <summary>Carnivore / Herbivore — ฝั่งเกมเอาไปโชว์เป็น "รสนิยม" (client/Durango.UI/PetUtil.cs:PetTasteToString)</summary>
        [JsonProperty("type")]
        public string Type;
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

    public static PetFood GetPetFood(string prototypeId)
    {
        if (string.IsNullOrEmpty(prototypeId) || Performances?.PetFoodDict == null) return null;
        if (Performances.PetFoodDict.TryGetValue(prototypeId, out var value))
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
