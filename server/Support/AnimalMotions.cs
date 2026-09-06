using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

/// <summary>
/// ชื่อ AnimationClip ของสัตว์แต่ละชนิด — ตัวที่ทำให้สัตว์ "ขยับ" บนจอ
///
/// ═══ ทำไมต้องมี ═══
/// <c>Movement.MotionName</c> ในโปรโตคอลคือ **ชื่อ clip จริง** ไม่ใช่คีย์ — ยืนยันจากฝั่งผู้เล่นเอง:
/// <c>client/LocalMoveOperator.cs:185</c> <c>GetCurrentMotionClip(out ชื่อ clip)</c> →
/// <c>MoveMsgGenerator.MotionChanged</c> → <c>Movement.MotionName</c>
/// และฝั่งรับ <c>AnimalBehavior.PlayAnimationMovement</c> (บรรทัด 1007-1015) ส่งต่อเข้า
/// <c>Anim.CrossFade(ชื่อ clip)</c> ตรง ๆ · **ถ้าชื่อว่างมัน return ทันทีโดยไม่เล่นอะไรเลย**
///
/// และ <c>AnimalBehavior.Update()</c> (บรรทัด 483-503) **ไม่มีตรรกะเล่นท่ายืนเองเลย** —
/// มีแต่ประมวลผลการเคลื่อนที่/เอฟเฟกต์ ⇒ สัตว์จะนิ่งสนิทจนกว่าเซิร์ฟจะสั่งท่ามาให้
/// (<c>ClientAnimalActor</c> ที่เดินเล่นเองใช้กับ "สัตว์ประดับ" ที่ terrain วางเท่านั้น
///  — ถูกอ้างจาก ClientAnimalGroup กับ StaticObjectPool ไม่ใช่จาก AnimalManager)
///
/// ═══ ข้อมูลมาจากไหน — ไม่มีการเดาชื่อเลย ═══
/// ชื่อ clip เก็บใน <c>AnimalFrameworkResource</c> ซึ่งเป็น ScriptableObject **อยู่ในตัว prefab
/// ของสัตว์แต่ละชนิด** ⇒ ไม่มีทางรู้จากไฟล์ JSON ที่เซิร์ฟมี และตัวช่วยตั้งชื่ออัตโนมัติ
/// (<c>AnimalFrameworkUtils.AutoFillInternal</c>) ถูก strip ทิ้งใน build จริง
/// ⇒ ให้ตัวเกมโหลด prefab ทุกชนิดแล้วอ่านออกมาให้ ด้วยคำสั่ง <c>animdump</c> ของ BotBridge
/// ได้ครบ **214 จาก 214 ชนิด** ลงไฟล์ <c>data/assets/derived/animal_motions.json</c>
/// เช่น <c>{"2020": {"stand":"Wolf_Stand","move":"Wolf_Walk","dead":"Wolf_Die", ...}}</c>
///
/// ⚠️ ไฟล์นี้เป็นข้อมูลที่ **ถอดจาก asset ของเกม** ไม่ใช่ค่าที่เราแต่งขึ้น
/// ถ้าอัปเดตตัวเกมแล้วชื่อ clip เปลี่ยน ให้ดึงใหม่ด้วย <c>bot.ps1 raw "animdump file=..."</c>
/// </summary>
public static class AnimalMotions
{
    /// <summary>ท่าที่ตารางเก็บไว้ — ชื่อคีย์ตรงกับที่ AnimalFrameworkResource ประกาศ</summary>
    public class Motions
    {
        public string Stand;        // ยืนเฉย ๆ (ท่าหลักที่ใช้ตอนสัตว์ไม่ได้ทำอะไร)
        public string Idle;         // ยืนแบบมีลูกเล่นเป็นครั้งคราว
        public string Move;         // เดิน
        public string Eat;
        public string Alert;
        public string Dead;
        public string BattleStand;
        public string Groggy;
        public string Blow;

        /// <summary>ท่าโจมตีปกติ — ไม่มีตัวนี้ สัตว์จะยืนนิ่งทั้งที่ดาเมจเข้าผู้เล่นจริง</summary>
        public string AttackNormal;

        /// <summary>ท่าโจมตีหนัก (บางชนิดไม่มี)</summary>
        public string AttackStrong;
    }

    private static Dictionary<ushort, Motions> _byType;

    /// <summary>โฟลเดอร์ assets — ใช้ตัวเดียวกับ WorkbenchTags (Program ตั้งให้ตอนบูต)</summary>
    private static string AssetsDir => WorkbenchTags.AssetsDir;

    public static Motions Of(ushort entityType)
    {
        EnsureLoaded();
        return _byType.TryGetValue(entityType, out Motions m) ? m : null;
    }

    /// <summary>ท่ายืนของสัตว์ชนิดนี้ — ตัวที่ส่งไปกับ AppearAnimal · null ถ้าไม่มีข้อมูล</summary>
    public static string StandOf(ushort entityType)
    {
        Motions m = Of(entityType);
        // เผื่อบางชนิดไม่มี stand ให้ถอยไป idle แล้ว battle_stand ตามลำดับ
        return m?.Stand ?? m?.Idle ?? m?.BattleStand;
    }

    private static void EnsureLoaded()
    {
        if (_byType != null) return;
        _byType = new Dictionary<ushort, Motions>();

        string path = Path.Combine(AssetsDir ?? "", "derived", "animal_motions.json");
        if (!File.Exists(path))
        {
            Console.WriteLine($"[สัตว์] ⚠️ ไม่พบ {path} — สัตว์จะยืนนิ่งไม่มีอนิเมชั่น " +
                              "(ดึงใหม่ด้วย bot.ps1 raw \"animdump file=<path>\")");
            return;
        }

        try
        {
            var root = JObject.Parse(File.ReadAllText(path));
            foreach (JProperty prop in root.Properties())
            {
                if (!ushort.TryParse(prop.Name, out ushort type) || prop.Value is not JObject o) continue;
                _byType[type] = new Motions
                {
                    Stand = (string)o["stand"],
                    Idle = (string)o["idle"],
                    Move = (string)o["move"],
                    Eat = (string)o["eat"],
                    Alert = (string)o["alert"],
                    Dead = (string)o["dead"],
                    BattleStand = (string)o["battle_stand"],
                    Groggy = (string)o["groggy"],
                    Blow = (string)o["blow"],
                    AttackNormal = (string)o["attack_normal"],
                    AttackStrong = (string)o["attack_strong"]
                };
            }
            Console.WriteLine($"[สัตว์] โหลดชื่อท่าทางของสัตว์ {_byType.Count} ชนิด");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[สัตว์] อ่าน {path} ไม่ได้: {e.Message}");
        }
    }
}
