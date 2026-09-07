using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Online;
using Yaml;

namespace DurangoServerNx;

/// <summary>
/// [7 ก.ย. 2026] ตรวจ "ข้อมูลที่เซิร์ฟจะส่งให้เกม" โดยไม่ต้องเปิดเซิร์ฟ/เข้าเกม
/// เรียกด้วย <c>DurangoServer --check-data</c>
///
/// ═══ ทำไมต้องมี ═══
/// เมนูและปุ่มเก็บของ **ฝั่งเกมไม่ได้ตัดสินใจเอง** มันเชื่อรายการที่เซิร์ฟส่งมาล้วน ๆ
/// ⇒ บั๊กกลุ่มนี้ไม่มีข้อความ error เลยสักบรรทัด มันหายไปเงียบ ๆ (ปุ่มไม่ขึ้น/ของไม่มีในเมนู)
/// เจอได้ทางเดียวคือเข้าเกมไปยืนหน้าของชิ้นนั้น ซึ่งช้าและมองข้ามง่าย
///
/// ตัวนี้เรียก <c>CollectibleTable</c> กับตารางแท็กโต๊ะ **ตัวจริงที่เซิร์ฟใช้ตอนรัน**
/// ไม่ใช่สคริปต์ที่อ่าน JSON ซ้ำ ⇒ ผลที่ออกมาคือสิ่งที่ผู้เล่นจะเห็นจริง
///
/// คืน 0 = ผ่าน · 1 = มีข้อที่ต้องดู
/// </summary>
internal static class DataCheck
{
    public static int Run()
    {
        Console.WriteLine("═══ ตรวจข้อมูลที่เซิร์ฟส่งให้เกม ═══");
        Console.WriteLine();

        int problems = 0;
        problems += CheckGathering();
        Console.WriteLine();
        problems += CheckArtifactMenus();

        Console.WriteLine();
        Console.WriteLine(problems == 0
            ? "✅ ผ่านทั้งหมด"
            : $"⚠️ มี {problems} ข้อที่ต้องดู");
        return problems == 0 ? 0 : 1;
    }

    // ── ระบบเก็บเกี่ยว ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ของธรรมชาติที่ต้อง "เก็บได้ของที่ถูกต้อง" — คู่ (ชนิด entity, prototype ที่ต้องมีในเมนู)
    ///
    /// เลือกจากบั๊กที่เคยเจอจริง ไม่ใช่สุ่มมา:
    ///   · 11002 grass_reed = 갈대 ต้นกก — เคยได้แต่ "ใบไม้" เพราะ generator "reed" ถูกทิ้ง
    ///     ทั้งที่มันคือไอเทม stem (줄기 ลำต้น) ที่เอาไปทำเชือก/หลังคาทั้งเกม
    ///   · 11055 grass_cattail = 부들 ต้นธูปฤๅษี — ตระกูลเดียวกัน
    ///   · 11088 grass_reed อีกสไปรต์ — ยืนยันว่าแก้ทั้งชนิด ไม่ใช่ตัวเดียว
    /// </summary>
    private static readonly (ushort EntityType, string Expect, string What)[] GatherExpectations =
    {
        (11002, "stem", "ต้นกก (갈대) ต้องให้ลำต้น"),
        (11088, "stem", "ต้นกกอีกสไปรต์ ต้องให้ลำต้น"),
        (11034, "stem", "ต้นกกอีกสไปรต์ ต้องให้ลำต้น"),
    };

    private static int CheckGathering()
    {
        Console.WriteLine("── ระบบเก็บเกี่ยว: ของธรรมชาติให้อะไรบ้าง ──");
        int bad = 0;

        foreach (var (entityType, expect, what) in GatherExpectations)
        {
            IReadOnlyList<CollectibleTable.GeneratorSpec> specs = CollectibleTable.AllSpecs(entityType);
            string[] ids = specs.Select(s => s.Id).ToArray();
            bool ok = ids.Contains(expect);
            if (!ok) bad++;
            Console.WriteLine($"  {(ok ? "✓" : "✗")} {entityType} {what}");
            Console.WriteLine($"      ได้: {(ids.Length == 0 ? "(ไม่มีอะไรเลย)" : string.Join(" · ", specs.Select(s => $"{s.Id} \"{s.Name}\"")))}");
        }

        // ของธรรมชาติที่ "แตะแล้วไม่มีอะไรให้เก็บเลย" — ไม่ใช่ error เสมอไป (กล่อง/รถ/ของอีเวนต์
        // ที่ข้อมูลไม่บอกว่าให้อะไร) แต่ตัวเลขที่โตขึ้นผิดปกติแปลว่ามีอะไรพัง ⇒ รายงานไว้ดู
        // ของธรรมชาติเก็บใน DataHelper (ไม่ใช่ SingletonDict) ⇒ กวาดช่วง entity type ของมัน
        // แล้วถามทีละตัวว่ารู้จักไหม (IsNaturalObject = 10000..20999 — Support/DataHelper.cs:14)
        int empty = 0, total = 0;
        for (int entityType = 10000; entityType <= ushort.MaxValue; entityType++)
        {
            if (Durango.Terrain.DataHelper.GetBiomeSpriteInfo(entityType) == null) continue;
            total++;
            if (CollectibleTable.GeneratorCount((ushort)entityType) == 0) empty++;
        }
        Console.WriteLine($"  · ของธรรมชาติ {total} ชนิด · เก็บอะไรไม่ได้เลย {empty} ชนิด");
        return bad;
    }

    // ── เมนูตอนแตะสิ่งปลูกสร้าง ──────────────────────────────────────────────────────

    /// <summary>
    /// component → เมนูที่ต้องโผล่คู่กัน (ตรงกับที่ <c>Player.HandleTouchMsg</c> ใส่ให้)
    ///
    /// ⚠️ ฝั่งเกมเชื่อรายการที่เซิร์ฟส่งมาล้วน ๆ ⇒ ลืมใส่บรรทัดเดียว = ปุ่มนั้นหายทั้งเกม
    /// โดยไม่มีข้อความบอก (เคยเกิดกับปุ่มรื้อ · ปุ่มเขียนป้าย · ปุ่มจุดไฟ)
    /// </summary>
    private static readonly (string Component, string Menu)[] MenuExpectations =
    {
        ("Workbench", "제작 (คราฟต์)"),
        ("Burnable", "불 붙이기 (จุดไฟ)"),
        ("Shelter", "휴식 (พักผ่อน)"),
        ("Home", "귀환 지점 (ตั้งจุดกลับ)"),
        ("GrowCage", "동물 관리 (กรง)"),
        ("Washable", "씻기 (ล้าง)"),
        ("Port", "항로 (เส้นทางเดินเรือ)"),
    };

    private static int CheckArtifactMenus()
    {
        Console.WriteLine("── เมนูตอนแตะสิ่งปลูกสร้าง ──");

        // นับว่าแต่ละ component มีสิ่งปลูกสร้างกี่ชนิด — ศูนย์ = ชื่อ component พิมพ์ผิด
        // (เทียบกับข้อมูลจริง ไม่ใช่รายชื่อที่เราจำไว้)
        int bad = 0;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (MergedBlueprint bp in BlueprintStore.GetAllBlueprints())
        {
            foreach (string component in bp.Components ?? Array.Empty<string>())
            {
                counts[component] = counts.GetValueOrDefault(component) + 1;
            }
        }

        foreach (var (component, menu) in MenuExpectations)
        {
            int n = counts.GetValueOrDefault(component);
            if (n == 0) bad++;
            Console.WriteLine($"  {(n > 0 ? "✓" : "✗")} {component,-12} {n,3} ชนิด → {menu}");
        }

        // ── แท็กโต๊ะคราฟต์ ────────────────────────────────────────────────────────
        // สูตรบอกว่า "ต้องมีโต๊ะที่มีแท็ก X ระดับ ≥ N" แล้วฝั่งเกมเช็คจาก Tags ที่เซิร์ฟส่งมา
        // ⇒ แท็กที่ไม่มีโต๊ะไหนให้เลย = สูตรกลุ่มนั้น **คราฟต์ไม่ได้ทั้งกลุ่ม** โดยไม่มีข้อความบอก
        // (ดู Support/WorkbenchTags.cs) — วัดจากฝั่ง "สูตรขออะไร" ไม่ใช่ "หลังไหนไม่มีแท็ก"
        // เพราะของอย่างลู่วิ่ง (treadmill_01) ติด component Workbench ไว้แต่ไม่มีสูตรไหนใช้มัน
        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (CraftRecipeData recipe in CraftRecipeStore.All())
        {
            foreach (string tag in recipe?.workbench_tags?.Keys ?? Enumerable.Empty<string>()) needed.Add(tag);
        }
        var provided = new HashSet<string>(StringComparer.Ordinal);
        foreach (MergedBlueprint bp in BlueprintStore.GetAllBlueprints())
        {
            foreach (Messages.Tag tag in WorkbenchTags.Of(bp.EntityType) ?? Array.Empty<Messages.Tag>())
            {
                provided.Add(tag.Id);
            }
        }
        string[] orphan = needed.Except(provided).OrderBy(t => t).ToArray();
        if (orphan.Length > 0)
        {
            bad++;
            Console.WriteLine($"  ✗ แท็กที่สูตรขอแต่ไม่มีโต๊ะไหนให้ {orphan.Length} ตัว: {string.Join(", ", orphan)}");
            Console.WriteLine("      (สูตรที่ขอแท็กนี้คราฟต์ไม่ได้ — เติมใน data/assets/derived/workbench_tags.json)");
        }
        else
        {
            Console.WriteLine($"  ✓ แท็กที่สูตรขอครบทั้ง {needed.Count} ตัว มีโต๊ะรองรับหมด");
        }
        return bad;
    }
}
