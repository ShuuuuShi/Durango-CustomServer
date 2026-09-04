using System;
using System.Collections.Generic;
using System.IO;
using Messages;
using Newtonsoft.Json.Linq;
using Yaml;

namespace Durango.Online;

/// <summary>
/// แท็กที่โต๊ะคราฟต์แต่ละแบบมอบให้ — ตัวปลดล็อกการคราฟต์ 587 จาก 720 สูตร
///
/// ═══ ทำไมต้องมี ═══
/// สูตรส่วนใหญ่บอกว่า "ต้องมีโต๊ะที่มีแท็ก X ระดับ ≥ N" (recipes.json → workbench_tags)
/// ฝั่งเกมเช็คเองที่ <c>client/Crafting/Recipe.cs:59-68</c> ด้วย
/// <c>workbench.GetTag(id).Level &gt;= level</c> โดยแท็กมาจาก <c>AppearArtifact.Tags</c>
/// ที่เซิร์ฟส่งมาอย่างเดียว (<c>client/Artifact.cs:545 SetTagList</c>)
///
/// **ถ้าไม่ส่ง Tags มาเลย ผลคือปุ่มคราฟต์กดไม่ขึ้นโดยไม่มีข้อความบอกอะไร** — เกมจะบอกแค่
/// "ต้องมีโต๊ะทำงานอยู่ใกล้ ๆ" ทั้งที่ผู้เล่นยืนอยู่หน้าโต๊ะพอดี
///
/// ═══ ตารางนี้เราสร้างเอง ═══
/// ข้อมูลเกมที่มีไม่ได้บอกว่าโต๊ะตัวไหนให้แท็กอะไร (ตรวจครบทุกไฟล์แล้ว — รายละเอียดและ
/// หลักฐานที่ใช้อนุมานอยู่ใน <c>tools/derive-workbench-tags.py</c>)
/// ⇒ สร้างเป็นไฟล์ <c>data/assets/derived/workbench_tags.json</c> ที่แก้ได้โดยไม่ต้องแก้โค้ด
/// และมีสนาม <c>_ที่มา</c> กำกับความมั่นใจของทุกแท็กไว้ในไฟล์นั้นเลย
///
/// ไม่มีไฟล์ ⇒ ไม่พัง แค่คราฟต์ที่ต้องใช้โต๊ะไม่ได้ (เตือนออกล็อกครั้งเดียว)
/// </summary>
public static class WorkbenchTags
{
    private static Dictionary<string, Tag[]> _byPrototype;

    /// <summary>โฟลเดอร์ assets — ตั้งจาก Program ตอนบูต (ทางเดียวกับ RegionCatalog.Load)</summary>
    public static string AssetsDir { get; set; }

    /// <summary>
    /// แท็กของสิ่งปลูกสร้างชนิดนี้ — คืน null ถ้าไม่ใช่โต๊ะคราฟต์
    /// (คืน null ไม่ใช่อาเรย์ว่าง เพราะฝั่งเกมนับ <c>Tags.Length</c> แล้ววนอ่าน ไม่ได้แยกสองกรณี)
    /// </summary>
    public static Tag[] Of(int entityType)
    {
        EnsureLoaded();
        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(entityType);
        if (blueprint?.Id == null) return null;
        return _byPrototype.TryGetValue(blueprint.Id, out Tag[] tags) ? tags : null;
    }

    /// <summary>
    /// เติมแท็กลงใน AppearArtifact ถ้ายังไม่มี — คืน true เมื่อมีการเปลี่ยนแปลง
    ///
    /// เขียนทับเฉพาะตอนที่ยังว่าง เผื่อวันหนึ่งมีที่อื่นตั้งแท็กเองแล้วจะได้ไม่ถูกลบ
    /// </summary>
    public static bool Apply(ref AppearArtifact artifact)
    {
        if (artifact.Tags._Tags is { Length: > 0 }) return false;
        Tag[] tags = Of(artifact.EntityType);
        if (tags == null || tags.Length == 0) return false;
        artifact.Tags = new Tags { EntityId = artifact.EntityId, _Tags = tags };
        return true;
    }

    private static void EnsureLoaded()
    {
        if (_byPrototype != null) return;
        _byPrototype = new Dictionary<string, Tag[]>(StringComparer.Ordinal);

        string path = Path.Combine(AssetsDir ?? "", "derived", "workbench_tags.json");
        if (!File.Exists(path))
        {
            Console.WriteLine($"[โต๊ะคราฟต์] ⚠️ ไม่พบ {path} — สูตรที่ต้องใช้โต๊ะจะคราฟต์ไม่ได้ " +
                              "(สร้างใหม่ด้วย: python tools/derive-workbench-tags.py)");
            return;
        }

        try
        {
            var root = JObject.Parse(File.ReadAllText(path));
            if (root["โต๊ะ"] is not JObject benches)
            {
                Console.WriteLine($"[โต๊ะคราฟต์] ⚠️ {path} ไม่มีคีย์ \"โต๊ะ\"");
                return;
            }

            foreach (JProperty bench in benches.Properties())
            {
                if (bench.Value is not JObject tagLevels) continue;
                var list = new List<Tag>();
                foreach (JProperty pair in tagLevels.Properties())
                {
                    if ((int?)pair.Value is { } level) list.Add(new Tag { Id = pair.Name, Level = level });
                }
                if (list.Count > 0) _byPrototype[bench.Name] = list.ToArray();
            }

            Console.WriteLine($"[โต๊ะคราฟต์] โหลดแท็กของโต๊ะ {_byPrototype.Count} แบบ");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[โต๊ะคราฟต์] อ่าน {path} ไม่ได้: {e.Message}");
        }
    }
}
