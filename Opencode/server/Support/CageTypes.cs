using System;
using System.Collections.Generic;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Yaml;

namespace Durango.Online;

/// <summary>
/// กรงเลี้ยงสัตว์และกรงฝึกให้เชื่อง — ตัวเปิดทางไปสู่ "สัตว์เลี้ยงตัวแรก"
///
/// ═══ ทำไมต้องมี ═══
/// ระบบสัตว์เลี้ยงทั้งชุด (Core/Player.Animals.cs) พร้อมใช้งานแล้ว แต่**ยังไม่มีทางได้สัตว์
/// ตัวแรกเลย** เพราะทุกเส้นทางผ่านกรง แล้วกรงยังใช้ไม่ได้ด้วย 2 เหตุผล:
///   1. แตะกรงแล้วไม่มีเมนู — <c>HandleTouchMsg</c> ไม่เคยใส่ <c>Interaction.Cage</c>(514)
///      หรือ <c>OpenDomesticCage</c>(533) ลงไป (ฝั่งเกมเชื่อรายการที่เซิร์ฟส่งล้วน ๆ)
///   2. หน้าจอกรงอ่านสถานะจาก <c>ArtifactState.Cage</c> / <c>.DomesticCage</c> ตรง ๆ
///      ไม่ได้ถามเป็น message ⇒ ถ้าเซิร์ฟไม่เคยเติมช่องนี้ หน้าจอเปิดมาก็ว่างเปล่า
///
/// ไฟล์นี้แก้ข้อ 2 — เติมสถานะกรงให้สิ่งปลูกสร้างที่เป็นกรง (ข้อ 1 อยู่ใน Player.cs)
///
/// ═══ ข้อมูลจริงทั้งหมด ═══
/// <c>performance.json → cage → &lt;prototype&gt;</c> มี 6 ชนิด ให้ค่า:
///   <c>cage_size</c> — ความจุรวม เป็นสูตรตามเลเวล เช่น
///                      <c>"min(150, 60 + 15 * int(level/10))"</c>
///   <c>feedbox_size</c> · <c>hunger_recovery_speed</c> · <c>tag_to_generator</c>
/// (ตัวหลังยังไม่ได้ใช้ — เป็นเรื่องของระบบให้อาหาร/ผลผลิตซึ่งอยู่ใน Player.Animals.cs)
///
/// ⚠️ ความจุที่ส่งไปเป็น "ขนาดรวม" ไม่ใช่ "จำนวนตัว" — สัตว์แต่ละตัวกินที่ตามฟิลด์
/// <c>size</c> ของบังเหียนมัน (performance.json → reins → size) เหมือนของในกระเป๋า
/// </summary>
public static class CageTypes
{
    private class Info
    {
        public string CageSizeFormula;
        public int FeedboxSize;
        public bool IsDomestication;
    }

    private static Dictionary<string, Info> _byPrototype;

    /// <summary>
    /// ซ่อมค่า <c>ArtifactState.Cage</c> ที่โหลดกลับมาจากไฟล์เซฟให้เป็นชนิดที่ถูกต้อง
    ///
    /// ⚠️ **ถ้าไม่ทำ = โปรโตคอลพังทั้งแพ็กเก็ต ไม่ใช่แค่กรงหาย**
    /// <c>ArtifactState.Cage</c> ประกาศเป็น <c>object</c> (ต้นฉบับของ NEXON) เพราะช่องนี้ใส่ได้
    /// สองชนิดคือ <c>Cage</c> กับ <c>GrowCage</c> ⇒ Newtonsoft ตอนอ่านกลับไม่รู้ว่าเป็นชนิดไหน
    /// จึงคืนมาเป็น <c>JObject</c> · แล้ว <c>ArtifactState.Pack</c> (บรรทัด 148-159) เขียนแบบ
    /// <code>
    /// if (Cage == null) PackNull(); else if (Cage is Cage) ... else if (Cage is GrowCage) ...
    /// </code>
    /// ไม่มี else ⇒ เจอ JObject แล้ว**ไม่เขียนอะไรลงไปเลยสักไบต์** ทำให้ฟิลด์ที่เหลือทั้งหมด
    /// (DomesticCage / Crack / Effector / Inventory / Stats) เลื่อนตำแหน่งไปหนึ่งช่อง
    /// ⇒ ฝั่งเกมอ่านสถานะสิ่งปลูกสร้างเพี้ยนทั้งก้อน
    ///
    /// แยกสองชนิดด้วยฟิลด์ <c>Tasks</c> ซึ่งมีเฉพาะใน <c>GrowCage</c>
    /// </summary>
    public static void NormalizeLoaded(Dictionary<string, AppearArtifact> artifacts)
    {
        if (artifacts == null || artifacts.Count == 0) return;

        var keys = new List<string>(artifacts.Keys);
        int fixedCount = 0;
        foreach (string key in keys)
        {
            AppearArtifact artifact = artifacts[key];
            if (artifact.States.Cage is not JObject node) continue;
            try
            {
                // ⚠️ ต้องใช้ serializer ชุดเดียวกับที่เขียนไฟล์ (Json.Setting)
                // ToObject() เปล่า ๆ ใช้ JsonSerializer.CreateDefault() ซึ่ง **ไม่มี GaugeConverter**
                // ⇒ หลอด Life/Hungry ของสัตว์ในกรงกลับมาเป็นก้อนเปล่า (Get() = 0) แบบไม่มี error
                // ผลจริง: สั่งงานสัตว์ไม่ได้เพราะเซิร์ฟคิดว่ามันตายและหิวตลอดเวลา
                var serializer = Newtonsoft.Json.JsonSerializer.Create(Json.Setting);
                artifact.States.Cage = node["Tasks"] != null
                    ? node.ToObject<GrowCage>(serializer)
                    : node.ToObject<Messages.Cage>(serializer);
                artifacts[key] = artifact;
                fixedCount++;
            }
            catch (Exception e)
            {
                // อ่านไม่ออกก็ทิ้งไปเลยดีกว่าปล่อย JObject ค้างไว้แล้วแพ็กเก็ตพัง
                Console.WriteLine($"[กรง] ⚠️ สถานะกรงของ {key} เสีย ({e.Message}) — ล้างทิ้งแล้วสร้างใหม่");
                artifact.States.Cage = null;
                artifacts[key] = artifact;
            }
        }
        if (fixedCount > 0) Console.WriteLine($"[กรง] แปลงสถานะกรงจากไฟล์เซฟ {fixedCount} หลัง");
    }

    /// <summary>ความจุกรงที่เลเวลนั้น — คืน 0 ถ้าไม่ใช่กรง</summary>
    public static int CapacityOf(string prototypeId, int level)
    {
        EnsureLoaded();
        if (prototypeId == null || !_byPrototype.TryGetValue(prototypeId, out Info info)) return 0;
        double value = StatFormula.EvalOr(info.CageSizeFormula,
            new Dictionary<string, double> { ["level"] = level }, 0.0);
        // Cage.Size เป็น byte ⇒ ความจุจริงสูงสุด 150 ตามข้อมูล แต่หนีบไว้กันล้นถ้าข้อมูลเปลี่ยน
        return (int)Math.Clamp(value, 0, 255);
    }

    /// <summary>
    /// เติมสถานะกรงให้สิ่งปลูกสร้าง ถ้ามันเป็นกรงและยังไม่มีสถานะ — คืน true เมื่อเปลี่ยนแปลง
    ///
    /// แยกกรง 2 แบบตาม component ในข้อมูล:
    ///   <c>GrowCage</c> (4 ชนิด) → โรงเลี้ยง ใส่สัตว์ที่เชื่องแล้วเข้าไปเลี้ยง/สั่งงาน
    ///   <c>DomesticCage</c> (2 ชนิด) → กรงฝึก ใส่บังเหียนแล้วป้อนอาหารจนกลายเป็นสัตว์เลี้ยง
    /// </summary>
    public static bool Apply(ref AppearArtifact artifact)
    {
        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(artifact.EntityType);
        if (blueprint?.Components == null || blueprint.Id == null) return false;

        bool grow = Array.IndexOf(blueprint.Components, "GrowCage") >= 0;
        bool domestic = Array.IndexOf(blueprint.Components, "DomesticCage") >= 0;
        if (!grow && !domestic) return false;

        // เลเวลของสิ่งปลูกสร้าง — ยังไม่ได้เก็บต่อหลัง ใช้เลเวลสูงสุดของแบบแปลนไปก่อน
        // (เหตุผลเดียวกับแท็กโต๊ะคราฟต์ ดู Support/WorkbenchTags.cs)
        int capacity = CapacityOf(blueprint.Id, MaxBlueprintLevel);
        if (capacity <= 0) return false;

        if (grow)
        {
            if (artifact.States.Cage != null) return false;
            artifact.States.Cage = new GrowCage
            {
                Size = (byte)capacity,
                RemainSize = (byte)capacity,
                Pets = new Messages.Pets { Data = Array.Empty<Messages.Pet>() },
                Tasks = new Dictionary<string, Messages.TaskStatus>()
            };
            return true;
        }

        if (artifact.States.DomesticCage.HasValue) return false;
        artifact.States.DomesticCage = new DomesticCage
        {
            Size = (byte)capacity,
            RemainSize = (byte)capacity,
            Reins = Array.Empty<DomesticationInfo>()
        };
        return true;
    }

    /// <summary>
    /// **ค่าของเรา** — เลเวลที่ใช้คิดความจุกรง
    ///
    /// เซิร์ฟยังไม่ได้เก็บ "เลเวลของสิ่งปลูกสร้างแต่ละหลัง" ⇒ ถ้าใช้เลเวลต่ำ ความจุจะน้อยกว่า
    /// ที่ควรเป็นโดยผู้เล่นไม่มีทางเพิ่มได้เลย ซึ่งดูเหมือนบั๊กมากกว่าเป็นการจำกัด
    /// </summary>
    private const int MaxBlueprintLevel = 60;

    private static void EnsureLoaded()
    {
        if (_byPrototype != null) return;
        _byPrototype = new Dictionary<string, Info>(StringComparer.Ordinal);

        JObject root = Json.ReadFromFile<JObject>("performance");
        if (root?["cage"] is not JObject cages)
        {
            Console.WriteLine("[กรง] ⚠️ อ่าน performance.json → cage ไม่ได้ — กรงจะใช้งานไม่ได้");
            return;
        }

        foreach (JProperty prototype in cages.Properties())
        {
            // คีย์ชั้นในเป็นช่วงเลเวล "[1, 60]" — เอาแถวแรก ค่าที่ต่างกันตามเลเวลอยู่ในสูตรอยู่แล้ว
            if (prototype.Value is not JObject byLevel) continue;
            foreach (JProperty row in byLevel.Properties())
            {
                if (row.Value is not JObject v) continue;
                _byPrototype[prototype.Name] = new Info
                {
                    CageSizeFormula = (string)v["cage_size"],
                    FeedboxSize = (int?)v["feedbox_size"] ?? 0,
                    IsDomestication = prototype.Name.Contains("domestication")
                };
                break;
            }
        }

        Console.WriteLine($"[กรง] โหลดข้อมูลกรง {_byPrototype.Count} ชนิด");
    }
}
