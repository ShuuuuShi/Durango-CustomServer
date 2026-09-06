using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Durango.Utils.Extensions;
using Yaml.Util;

namespace Yaml;

// เทียบเท่า RecipeSystem.RecipeContainer ฝั่ง client: merge Yaml.Blueprint (craft data: slots/looks)
// กับ ArtifactPrototype (entity data: components/musics/size/is_craft) — คีย์เชื่อมคือ __name__ ↔ blueprint id
// ต้นฉบับอยู่ที่ nexonSRC/Building/Blueprint.cs (SetPrototypeInfo) — เซิร์ฟใช้เฉพาะฟิลด์ที่ handler แท้ใช้
public class MergedBlueprint
{
    public string Id;

    public Gettext Name;

    public string[] Components;

    public string[] Musics;

    public string DefaultLook;

    public bool IsShowCraftMode;

    public Yaml.BlueprintSlot[] Slots;

    public int EntityType;

    /// <summary>เลเวลสูงสุดของแบบแปลน (building/blueprints.json → max_level) — 1 ถ้าไม่มีแบบแปลน</summary>
    public int MaxLevel = 1;

    // ── [6 ก.ย. 2026] ฟิลด์ที่ระบบสร้างสิ่งปลูกสร้างต้องใช้ (Core/Player.Building.cs) ──────
    // ทั้งหมดมาจาก building/blueprints.json ตรง ๆ ไม่มีตัวไหนตั้งเอง

    /// <summary>เลเวลต่ำสุดที่สร้างได้ (min_level)</summary>
    public int MinLevel = 1;

    /// <summary>พลังงานที่เสียตอนลงมือสร้าง — ในไฟล์เป็นสตริงตัวเลข เช่น "23"</summary>
    public string Energy;

    /// <summary>วินาทีที่ต้องรอ "มาร์มูรี" หลังสร้างเสร็จ (postprocess_time) — 220/556 หลังเป็น 0 = เสร็จทันที</summary>
    public int PostprocessTime;

    /// <summary>คนอื่นมาช่วยมาร์มูรีได้กี่คน (postprocess_helper_max)</summary>
    public int PostprocessHelperMax;

    /// <summary>เครื่องมือที่ใช้สร้างได้ — tag → เลเวลขั้นต่ำ (tool_tags)</summary>
    public Dictionary<string, int> ToolTags;

    /// <summary>ขนาดที่กินบนพื้น (entity_types/artifact → size) — ใช้คิดพลังงาน/เวลาจองที่</summary>
    public Point2 Size;

    /// <summary>ความสูง (height) — ส่งไปกับ AppearArtifact</summary>
    public int Height;

    /// <summary>ขนาดปรับได้ไหม (is_size_variable) — ถ้าใช่ client ส่ง Size ที่ผู้เล่นลากมาเอง</summary>
    public bool IsSizeVariable;

    /// <summary>หมุนได้กี่ทิศ (rotatable_directions) — 0/1 = หมุนไม่ได้</summary>
    public int RotatableDirections;

    /// <summary>ของถาวร รื้อไม่ได้ (permanent)</summary>
    public bool Permanent;

    /// <summary>
    /// เก็บใส่กระเป๋าได้ไหม (entity_types/artifact.json → capsulizable)
    /// ข้อมูลจริง: false 101 จาก 560 ชนิด (สระว่ายน้ำ · ห้องเรียนโมดูลาร์ · แล็บแคลน ฯลฯ)
    /// ⚠️ ไม่เช็ค = เก็บของที่ NEXON บอกว่าเก็บไม่ได้ ได้
    /// </summary>
    public bool Capsulizable = true;
}

public static class BlueprintStore
{
    private static Dictionary<int, MergedBlueprint> _byEntityType;

    /// <summary>
    /// ค้นด้วยชื่อแบบแปลน เช่น <c>bed_01</c>
    ///
    /// ⚠️ จำเป็นเพราะ <c>OccupyArtifactSite.BlueprintId</c> ที่ client ส่งมาเป็น**ชื่อ** ไม่ใช่ entity type
    /// (client/BuildSystem.cs:513 <c>result.BlueprintId</c> = <c>Blueprint.Id</c>)
    /// ⇒ ไม่มีตารางนี้ = แปลคำขอสร้างไม่ออกเลยสักหลัง
    /// </summary>
    private static Dictionary<string, MergedBlueprint> _byId;

    private static List<MergedBlueprint> _all;

    public static void Initialize(Dictionary<int, ArtifactPrototype> artifacts = null, Dictionary<string, Blueprint> blueprints = null)
    {
        _byEntityType = new Dictionary<int, MergedBlueprint>();
        _byId = new Dictionary<string, MergedBlueprint>(StringComparer.Ordinal);
        _all = new List<MergedBlueprint>();
        artifacts ??= SingletonDict<int, ArtifactPrototype>.Instance;
        blueprints ??= new Dictionary<string, Blueprint>();
        if (artifacts == null) return;

        foreach (var pair in artifacts)
        {
            int entityType = pair.Key;
            ArtifactPrototype proto = pair.Value;
            blueprints.TryGetValue(proto.__name__ ?? "", out var bp);

            var merged = new MergedBlueprint
            {
                Id = proto.__name__ ?? bp?.preview ?? entityType.ToString(),
                EntityType = entityType,
                // ต้นฉบับ: Components = components + client_only_components (SetPrototypeInfo)
                Components = MergeComponents(proto),
                Musics = proto.musics,
                DefaultLook = bp?.default_look,
                IsShowCraftMode = proto.is_craft,
                Slots = bp?.slots,
                MaxLevel = bp?.max_level ?? 1,

                // ── ข้อมูลสำหรับระบบสร้าง ────────────────────────────────────────────
                MinLevel = bp?.min_level ?? 1,
                Energy = bp?.energy,
                PostprocessTime = bp?.postprocess_time ?? 0,
                PostprocessHelperMax = bp?.postprocess_helper_max ?? 0,
                ToolTags = bp?.tool_tags,
                // ขนาด/ความสูงอยู่ฝั่ง prototype ไม่ใช่ฝั่ง blueprint
                Size = SizeOf(proto),
                Height = proto.height,
                IsSizeVariable = proto.is_size_variable,
                RotatableDirections = proto.rotatable_directions,
                Permanent = proto.permanent,
                Capsulizable = proto.capsulizable
            };
            // ชื่อ: blueprint มี name (Gettext) ก่อน ถ้าไม่มีใช้ prototype ไม่มี fallback ชื่อจาก __name__
            merged.Name = bp?.name ?? new Gettext(proto.__name__);

            _byEntityType[entityType] = merged;
            if (!string.IsNullOrEmpty(merged.Id)) _byId[merged.Id] = merged;
            _all.Add(merged);
        }
        Console.WriteLine($"[blueprint] merge blueprint×artifact: {_all.Count} รายการ");
    }

    private static string[] MergeComponents(ArtifactPrototype proto)
    {
        int n = proto.components?.Length ?? 0;
        int m = proto.client_only_components?.Length ?? 0;
        var result = new string[n + m];
        for (int i = 0; i < n; i++) result[i] = proto.components[i];
        for (int i = 0; i < m; i++) result[n + i] = proto.client_only_components[i];
        return result;
    }

    /// <summary>
    /// ขนาดบนพื้นจาก prototype — ไฟล์เก็บเป็นอาเรย์ <c>[กว้าง, ยาว]</c>
    /// ไม่มี/ผิดรูป ⇒ 1×1 (ของชิ้นเล็กหลายตัวไม่ได้เขียน size ไว้)
    /// </summary>
    private static Point2 SizeOf(ArtifactPrototype proto)
    {
        int[] size = proto?.size;
        if (size == null || size.Length < 2) return new Point2(1, 1);
        return new Point2(Math.Max(1, size[0]), Math.Max(1, size[1]));
    }

    /// <summary>เทียบเท่า RecipeContainer.GetBlueprint(entityType)</summary>
    public static MergedBlueprint GetBlueprint(int entityType)
    {
        return _byEntityType.Get(entityType);
    }

    /// <summary>เทียบเท่า RecipeContainer.GetBlueprint(blueprintId) — ใช้ตอนรับคำขอสร้างจาก client</summary>
    public static MergedBlueprint GetBlueprint(string blueprintId)
    {
        return string.IsNullOrEmpty(blueprintId) ? null : _byId.Get(blueprintId);
    }

    /// <summary>เทียบเท่า RecipeContainer.GetAllBlueprints()</summary>
    public static IEnumerable<MergedBlueprint> GetAllBlueprints() => _all;
}
