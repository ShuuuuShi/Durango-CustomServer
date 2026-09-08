using System;
using System.Collections.Generic;
using Messages;
using Yaml;

namespace Durango.Online;

/// <summary>
/// ของขวัญแจกผู้เล่นทุกคน — แจกครั้งเดียวตอน login (flag <c>ClaimedGifts</c> กันซ้ำ)
///
/// แจกเป็น "แคปซูล" (<c>ArtifactCapsule</c>) ที่วางแล้วกลายเป็นสิ่งปลูกสร้างพร้อมใช้งานทันที —
/// ผู้เล่นวางที่ไหนก็ได้ · สถานะกรง (GrowCage/DomesticCage) ถูกเติมอัตโนมัติตอนวาง
/// (World.ConstructArtifact -> ArtifactManager.AddArtifact -> CageTypes.Apply)
///
/// คนที่ online อยู่ได้ตอน relog · คนที่ยังไม่เคยเข้าได้ตอนเข้าครั้งแรก
/// </summary>
public partial class Player
{
    /// <summary>ชุดของขวัญ: (ไอดีล็อตกันแจกซ้ำ, entity_type ของสิ่งปลูกสร้างที่แจก)</summary>
    private static readonly (string Id, ushort[] EntityTypes)[] GiftDrops =
    {
        // 8 ก.ย. 2026 — กรงสัตว์ cage_01_6 (8014) + คอกเพาะพันธุ์ cage_domestication_4 (6003)
        ("cage_2026_09_08", new ushort[] { 8014, 6003 }),
    };

    /// <summary>แจกของขวัญที่ยังไม่เคยรับ — เรียกครั้งเดียวตอนโหลดผู้เล่น</summary>
    private void GrantPendingGifts()
    {
        // รันตอน login — ห้าม throw หลุดออกไป ไม่งั้นผู้เล่นเข้าเกมไม่ได้
        // ของขวัญแจกไม่สำเร็จยอมได้ แต่ต้องไม่บล็อกการเข้าเกม
        try
        {
            _context.ClaimedGifts ??= new List<string>();
            var granted = new List<Item>();
            foreach ((string id, ushort[] types) in GiftDrops)
            {
                if (_context.ClaimedGifts.Contains(id)) continue;
                foreach (ushort type in types)
                {
                    Item? capsule = MakeGiftCapsule(type);
                    if (capsule.HasValue) granted.Add(capsule.Value);
                }
                // ทำเครื่องหมายแม้สร้างบางชิ้นไม่ได้ — กันวนพยายามแจกซ้ำทุก login
                _context.ClaimedGifts.Add(id);
            }
            if (granted.Count > 0)
            {
                AddItems(granted);   // เข้ากระเป๋า + OnContextChanged (เซฟ ClaimedGifts + ของ)
                Console.WriteLine($"[ของขวัญ] {Short(EntityId)} รับของขวัญ {granted.Count} ชิ้น");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ของขวัญ] ⚠️ แจกให้ {Short(EntityId)} ไม่สำเร็จ (ข้ามไป ไม่บล็อก login): {e.Message}");
        }
    }

    /// <summary>
    /// ห่อสิ่งปลูกสร้างเป็น "แคปซูล" จาก entity_type — ใช้ตัวสร้างเดียวกับคำสั่ง prop
    /// (<c>Cheats.MakeAppearArtifact</c>) ให้ได้ Display/State ที่ถูกต้อง แล้วห่อเป็น
    /// <c>ArtifactCapsule</c> เหมือนตอนผู้เล่นเก็บสิ่งปลูกสร้างเอง (HandleCapsulateArtifactMsg)
    /// </summary>
    internal static Item? MakeGiftCapsule(ushort entityType)
    {
        AppearArtifact? built = Cheats.MakeAppearArtifact(new[] { "prop", entityType.ToString() }, out _);
        if (!built.HasValue) return null;
        AppearArtifact art = built.Value;

        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(art.EntityType);
        if (blueprint == null) return null;

        Item? made = Cheats.MakeItem(CapsuleProtoId, Math.Max(1, (int)art.States.Level));
        if (!made.HasValue) return null;

        Item capsule = made.Value;
        capsule.Ext = new ArtifactCapsule
        {
            EntityId = art.EntityId,
            BlueprintId = blueprint.Id,
            ArtifactLevel = art.States.Level,
            Tags = art.Tags._Tags ?? Array.Empty<Tag>(),
            Performance = Array.Empty<Messages.Performance>(),
            Display = art.Display,
            State = art.States,
            LookNames = new Dictionary<string, string>(),
            OccupySize = blueprint.Size
        };
        return capsule;
    }
}
