using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Network;
using Durango.Terrain;
using Durango.Utils;
using Durango.Utils.Extensions;
using Messages;
using Shared.Item;
using UnityEngine;
using Yaml;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
// ระบบเก็บเกี่ยวของธรรมชาติ (ตัดไม้/เก็บพืช/ทุบหิน) — [5 ก.ย. 2026]
//
// ลำดับ message ที่ "ตัวเกมคาดหวัง" (ยืนยันจากซอร์ส client จริง ไม่ได้เดา):
//
//   1) client → Touch(2021) {EntityId, Tile, EntityType}
//      server → Touched(2020) — ⚠️ **ตัวสำคัญคือฟิลด์ Collectible ไม่ใช่ Interactions**
//        client/InteractionSystem.cs:628 เรียก GatheringSystem.SetCollectible(LastTouched.Collectible)
//        แล้ว SetCollectible (client/GatheringSystem.cs:237-249) **ลบเมนู Collect ทุกตัวทิ้งก่อน**
//        แล้วค่อยเติมใหม่หนึ่งปุ่มต่อหนึ่ง Generator ⇒ ส่ง 506 ไปเปล่า ๆ โดยไม่มี Generators
//        = ไม่มีปุ่มให้กด (ปุ่มถูกลบทิ้งแล้วไม่มีอะไรมาแทน)
//        เราส่ง 506 ไปด้วยตามความหมายเดิมของ protocol (เผื่อ target ไม่ตรงจน SetCollectible
//        return ก่อน — client/GatheringSystem.cs:202) แต่ตัวที่ทำให้เก็บได้จริงคือ Collectible
//
//   2) client → Collect(2026) {EntityId, Tile, GeneratorId, Level, ToolItemId}
//        (client/GatheringSystem.cs:355 ReadyForGathering — ยิงหลังเดินไปถึงเป้าแล้ว)
//      server → **ต้องตอบที่ seq ของ Collect เดียวกันทั้งชุด** เพราะ handler ผูกกับ seq นั้น:
//        · Timer(1134)         → ตั้งเวลาหลอดความคืบหน้า (OnGatheringTimer :519)
//        · EnergyWarning(3648) → หยุดหลอดถามผู้เล่นก่อน (LowEnergyWarning)
//        · ToolNeeded(3647)    → popup "ต้องใช้เครื่องมือ"
//        · SkillNeeded(2449)   → popup "ต้องเรียนสกิลก่อน"
//        · Collected(104)      → ผลลัพธ์ + ของที่ได้
//      ⚠️ client/GatheringSystem.cs:399-412 มี .All() ที่ถือว่า **typecode อื่นนอกจาก
//         104/1134/2019/3648 = ล้มเหลว** ⇒ ห้ามตอบ OK/Error กลางคัน (Abort ใช้ได้เฉพาะตอน
//         ต้องการยกเลิกจริง ๆ)
//
// ⚠️ กับดักเดียวกับระบบคราฟต์: ปกติ client ลบ handler ของ seq ทิ้งทันทีที่ได้คำตอบตัวแรก
//    (client/Durango.Network/Connection.cs:905-908) ⇒ ส่ง Timer แล้ว Collected จะไม่ถึง
//    handler ของ seq อีก ต้องคร่อมด้วยแพ็กเก็ต TypeCode 0 (<see cref="ReplySequenceMark"/>
//    ประกาศไว้ที่ Core/Player.Crafting.cs:91) เปิด/ปิด "ชุดคำตอบต่อเนื่อง"
//    หมายเหตุ: Collected เองมี global handler อยู่แล้ว (client/GatheringSystem.cs:67) และ
//    client/Durango.Network/Connection.cs:883-887 fallback ไป global handler ให้เมื่อ seq
//    ไม่มี handler ของ typecode นั้น ⇒ ตอบที่ seq เดิมครอบคลุมทั้งสองทาง
//
// ── ข้อมูล "เก็บแล้วได้อะไร" อยู่ไหน (สำรวจแล้ว ไม่ได้เดา) ────────────────────────────
//   มีจริง:
//     · data/assets/entity_types/natural.json → collectible_id ของแต่ละ entity type
//       (เข้าถึงผ่าน DataHelper.GetBiomeSpriteInfo(entityType).CollectibleId ที่โหลดไว้แล้ว)
//     · data/assets/item/recipes.json → slots[].source_info ที่ type == 2 บอกคู่
//       (collectible_id → generator_id) ของจริง 702 รายการ ⇒ ครอบคลุม collectible 80/357 ตัว
//       (= natural entity type 223/711) เช่น tree_sandalwood → wood_log / wood_bough / leaf_small
//     · data/assets/item/generator_client_data.json → ชื่อ+ไอคอนของ generator (792 ตัว)
//     · data/assets/item/collectible_names.json → ชื่อของ collectible (607 ตัว)
//     · data/assets/constants.json → effort_standard.collect / duration_formula /
//       energy_formula / fatigue_cost.collect (สูตรเวลา-แรง-ความเหนื่อยของจริง)
//     · data/assets/item/prototype_data.json → tags ของไอเทม (ใช้ตัดสินว่าต้องใช้เครื่องมืออะไร)
//
//   ⚠️ **ไม่มีจริง** — ค้นครบทั้ง server/data และ game/Durango_Data/*.assets แล้ว:
//     ตารางของ generator เอง (ได้ไอเทมอะไร กี่ชิ้น เลเวลเท่าไร ต้องใช้เครื่องมือแท็กไหน)
//     ไม่มีอยู่ทั้งใน data ของเซิร์ฟและใน asset ของตัวเกม (grep "tool_requirements",
//     "generators", "collectible_data" = 0 ครั้ง) — ของจริงเป็นข้อมูลฝั่งเซิร์ฟของ Nexon ล้วน ๆ
//     ที่ไม่เคยหลุดออกมากับ client ⇒ ส่วนนั้นเราตั้งเอง ดู <see cref="GatheringTuning"/>
//     (data/gathering_tools.json ที่มีอยู่เป็นไฟล์ที่ทำค้างไว้ 5 บรรทัด ใช้ชื่อแท็ก "axe"/"knife"
//      ซึ่ง "axe" ไม่มีอยู่จริงใน data/assets/tags.json ⇒ ไม่เอามาใช้)
// ═══════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// [5 ก.ย. 2026] ค่าที่ "เราตั้งเอง" ของระบบเก็บเกี่ยว — **ไม่มีในข้อมูลต้นฉบับ** รวมไว้ที่เดียวตรงนี้
///
/// ทุกตัวเลขที่มาจากไฟล์เกมจริงอยู่ที่ data/assets/constants.json และ
/// data/assets/item/prototype_data.json — ห้ามย้ายมาไว้ที่นี่
/// </summary>
public static class GatheringTuning
{
    /// <summary>
    /// **ค่าของเรา** — จำนวนชิ้นที่ได้ต่อการเก็บหนึ่งครั้ง (ของจริงอยู่ในตาราง generator ที่ไม่มี)
    ///
    /// ⚠️ เดิมเป็นค่าคงที่ 1 ⇒ เก็บอะไรก็ได้ทีละชิ้นทั้งเกม ซึ่งผิดจากเกมจริงที่ได้ 1-5 ชิ้น
    /// ยกวิธีคิดมาจากโปรเจกต์ Opencode (ServerPlayer.Gathering.MakeGenerators กับ ButcheryData)
    /// ซึ่งใช้หลักเดียวกันสองข้อ:
    ///   • generator ตัวแรกของเป้า = "ของหลัก" ได้เยอะกว่าตัวรอง (3 · 2 · 3 · 2 …)
    ///   • ทุก <see cref="BonusPerLevel"/> เลเวลได้เพิ่มอีก 1 ชิ้น ⇒ ของเลเวลสูงคุ้มกว่า
    ///
    /// ฝั่งเกมไม่ได้เอา Generator.Amount ไปโชว์เป็นตัวเลข ใช้แค่เลือกไอคอนดาว
    /// (client/Durango.UI/InteractionMenuWidgetBase.cs:232 <c>Amount == 1</c>)
    /// ⇒ จำนวนที่ผู้เล่นได้จริงมาจากจำนวน Item ใน Collected เท่านั้น — ต้องส่งให้ตรงกันเอง
    /// </summary>
    public static int AmountFor(int order, int level) =>
        Mathf.Clamp(BaseAmount(order) + Mathf.Max(0, level - 1) / BonusPerLevel, 1, MaxAmount);

    /// <summary>ของหลัก (ตัวแรก) ได้ 3 · ตัวรองสลับ 2/3 — รูปแบบ 3 - (i % 2) ของ Opencode</summary>
    private static int BaseAmount(int order) => 3 - (Mathf.Max(0, order) % 2);

    /// <summary>**ค่าของเรา** — ทุกกี่เลเวลได้ของเพิ่มอีก 1 ชิ้น</summary>
    private const int BonusPerLevel = 5;

    /// <summary>เพดานกันข้อมูลเพี้ยนทำให้ได้ของทีเดียวเป็นร้อยชิ้นจนกระเป๋าแตก</summary>
    public const int MaxAmount = 5;

    /// <summary>
    /// **ค่าของเรา** — ระยะที่ยอมให้เก็บได้ (หน่วย tile)
    ///
    /// client เดินไปหาเป้าก่อนยิง Collect อยู่แล้ว (CalcInteractionDistance) เราแค่กันการยิงข้ามแมพ
    /// 5 tile = กว้างพอให้เผื่อ lag/ตำแหน่งไม่ตรงเป๊ะ แต่ยังไม่เปิดช่องให้เก็บของที่มองไม่เห็น
    /// </summary>
    public const int RangeTiles = 5;

    /// <summary>
    /// **ค่าของเรา** — เพดานเวลาเก็บที่ยอมหน่วงคำตอบ Collected (วินาที)
    /// กันข้อมูลเพี้ยนทำให้ผู้เล่นค้างหลอด (ค่าจากสูตรจริงที่เลเวล 1 คือ 2.5 วิ)
    /// </summary>
    public const float MaxCollectSeconds = 60f;

    /// <summary>
    /// **ค่าของเรา** — เวลาเก็บต่ำสุดหลังหักโบนัสสกิลแล้ว (วินาที)
    /// สั้นกว่านี้ท่าเก็บของยังเล่นไม่ทันจบ ⇒ เห็นเป็นตัวกระตุก
    /// </summary>
    public const float MinCollectSeconds = 0.5f;

    /// <summary>
    /// **ค่าของเรา** — เลเวลขั้นต่ำของเครื่องมือที่ต้องมี
    ///
    /// ของจริงเป็นตัวเลขต่อ generator (ยิ่งของแข็ง ยิ่งต้องการเครื่องมือเลเวลสูง) ซึ่งไม่มีในข้อมูล
    /// ⇒ ใช้ 1 = "มีเครื่องมือชนิดนั้นก็พอ" ทั้งหมด จะได้ไม่ล็อกผู้เล่นด้วยตัวเลขที่เราเดาเอง
    /// </summary>
    public const int ToolLevel = 1;
}

public partial class Player
{
    /// <summary>
    /// ของธรรมชาติที่ผู้เล่นคนนี้ "แตะ" ไว้ล่าสุด — **ช่อง (tile) → ชนิด**
    ///
    /// ทำไมต้องจำเอง: Collect(2026)/GetCollectible(2017) ส่งมาแค่ EntityId + Tile ไม่มี EntityType
    /// และ <see cref="World"/> ไม่มี API อ่าน "ของธรรมชาติที่ช่องนี้" (garden เป็น byte[] private
    /// ใน ChunkData — Core/World.cs:25) ส่วน Core/World.cs เป็นไฟล์ที่ระบบนี้แตะไม่ได้
    /// (มี agent อื่นทำงานพร้อมกัน)
    /// ⇒ เก็บจากตอน Touch ซึ่ง client ยิงมาก่อนเสมอ: ทั้งการกดเลือกเป้าปกติ
    ///   (client/InteractionSystem.cs:544 SendTouchMsg → .On&lt;Touched&gt;) และตัวบอททดสอบ
    ///   (client/BotBridge.cs:415 StartInteraction → SendTouchMsg แล้วค่อยกดเมนู)
    ///
    /// ⚠️ **ต้องคีย์ด้วย tile ไม่ใช่ EntityId** — ของธรรมชาติไม่มี entity id ในเกม:
    ///    InteractionObject.EntityId → ObjectIdentifier.GetEntityId → ImmovableBase.EntityId
    ///    ซึ่งของ NaturalObject เป็นสตริงว่าง (ยืนยันจาก state ของ BotBridge: naturals ทุกตัว
    ///    ได้ "id":"") ⇒ เคยคีย์ด้วย EntityId แล้วหาไม่เจอทุกครั้ง กดเก็บแล้วไม่มีอะไรเกิดขึ้น
    /// </summary>
    private readonly Dictionary<Point2, ushort> _touchedNaturals = new();

    /// <summary>
    /// นาฬิกาที่นัดส่ง <see cref="Collected"/> เมื่อครบเวลา
    ///
    /// เหตุผลเดียวกับ <see cref="_craftTimers"/> ของระบบคราฟต์: จุดเสียบงานรายเฟรมคือ
    /// <c>Player.Process()</c> ซึ่งอยู่ใน Core/Player.cs — ไฟล์ที่ระบบนี้แตะได้เฉพาะ HandleTouchMsg
    /// **callback ทำแค่ <c>Send</c>** ซึ่งปลอดภัยข้ามเธรด (GameCode/Durango.Online/Connection.cs:146
    /// ล็อก _sendLock ทั้งก้อนแล้วเขียนลงบัฟเฟอร์เฉย ๆ ส่วนการยิงออก socket ยังเป็นงานของลูปหลัก)
    /// การแก้ inventory/โลก/หลอด ทำเสร็จตั้งแต่ตอนรับ Collect บนเธรดหลักแล้ว
    ///
    /// ⚠️ ผลข้างเคียงที่ยอมรับไว้: ของธรรมชาติหายจากจอ **ตอนเริ่มเก็บ** ไม่ใช่ตอนเก็บเสร็จ
    ///    เพราะ World.DestroyNatural แก้ chunk data + Save() ⇒ เรียกจากเธรดนาฬิกาไม่ได้
    /// </summary>
    private readonly List<System.Threading.Timer> _collectTimers = new();

    private void RegisterGatheringHandlers()
    {
        // เกมยิงตัวนี้เมื่อเซิร์ฟ push CollectibleChanged มาบอกว่า "ของชิ้นนี้เปลี่ยนไปแล้ว"
        // (client/GatheringSystem.cs:96-103) — ตอบด้วยชุดเดิมที่คำนวณจาก entity type
        _connection.Recv(delegate(GetCollectible msg, PacketHeader header)
        {
            HandleGetCollectibleMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(Collect msg, PacketHeader header)
        {
            HandleCollectMsg(msg, header.Seq);
        });
        _connection.ConnetionClosed += ClearCollectTimers;
    }

    // ── ตอน Touch: บอกเกมว่าของชิ้นนี้เก็บอะไรได้บ้าง ────────────────────────────────

    /// <summary>
    /// สร้าง <see cref="Collectible"/> ของของธรรมชาติหนึ่งชิ้น + จำไว้ใช้ตอน Collect
    ///
    /// เรียกจาก <c>HandleTouchMsg</c> (Core/Player.cs) — จุดเดียวที่ระบบนี้แตะไฟล์นั้น
    /// </summary>
    internal Collectible BuildCollectibleFor(string entityId, ushort entityType, Point2 tile)
    {
        // กันโตไม่รู้จบตอนเล่นยาว ๆ — ของที่แตะแล้วไม่ได้เก็บไม่มีความหมายอีกต่อไป
        // (client แตะใหม่ทุกครั้งก่อนกดเก็บอยู่แล้ว) 256 = เผื่อไว้เยอะกว่าที่หน้าจอเดียวจะมีได้
        if (_touchedNaturals.Count > 256) _touchedNaturals.Clear();
        _touchedNaturals[tile] = entityType;

        // ซากสัตว์แล่ได้กี่ครั้งขึ้นกับเลเวลตัวมัน — ตัวใหญ่/เลเวลสูงให้ของมากกว่า
        AnimalManager.Animal animal = _world.AnimalManager?.Get(entityId);
        int animalLevel = animal != null && !animal.IsAlive ? animal.CombatLevel : 0;

        return CollectibleTable.Build(entityId, entityType,
                                      _world.HarvestedGenerators(HarvestKeyOf(entityId, tile)),
                                      animalLevel, UnlockedCollectibleCategories());
    }

    /// <summary>
    /// คีย์ที่ใช้จำว่าเป้านี้ถูกเก็บอะไรไปแล้ว
    ///
    /// ซากสัตว์ใช้ entity id เพราะมี id จริงและฝั่งเกมส่ง Tile มาเป็น (-1,-1) ตอนชำแหละ
    /// ของธรรมชาติใช้พิกัดช่อง เพราะ id ที่ส่งมาเป็นของที่ฝั่งเกมตั้งเอง เชื่อไม่ได้
    /// </summary>
    private string HarvestKeyOf(string entityId, Point2 tile)
        => _world.AnimalManager?.Get(entityId) != null ? entityId : $"{tile.x},{tile.y}";

    private void HandleGetCollectibleMsg(GetCollectible msg, uint seq)
    {
        _touchedNaturals.TryGetValue(msg.Tile, out ushort entityType);
        AnimalManager.Animal animal = _world.AnimalManager?.Get(msg.EntityId);
        if (animal != null && !animal.IsAlive) entityType = animal.EntityType;
        int animalLevel = animal != null && !animal.IsAlive ? animal.CombatLevel : 0;

        Send(CollectibleTable.Build(msg.EntityId, entityType,
                                    _world.HarvestedGenerators(HarvestKeyOf(msg.EntityId, msg.Tile)),
                                    animalLevel, UnlockedCollectibleCategories()), seq);
    }

    // ── ตอน Collect: ตรวจ → ตอบ Timer → ครบเวลาส่ง Collected ────────────────────────

    private void HandleCollectMsg(Collect msg, uint seq)
    {
        // ซากสัตว์: ฝั่งเกมส่ง Tile มาเป็น (-1,-1) เพราะสัตว์ไม่ได้อยู่กลางช่องเหมือนต้นไม้
        // ⇒ ถ้าหาด้วย tile ไม่เจอ ให้ลองหาด้วย EntityId (สัตว์มี id จริง ต่างจากของธรรมชาติ)
        AnimalManager.Animal carcass = _world.AnimalManager?.Get(msg.EntityId);
        if (carcass != null && carcass.IsAlive) carcass = null;         // ยังไม่ตาย = ชำแหละไม่ได้
        if (carcass != null && carcass.Butchered) carcass = null;       // ชำแหละไปแล้ว = ไม่มีอะไรเหลือ

        ushort entityType;
        if (carcass != null)
        {
            entityType = carcass.EntityType;
        }
        else if (!_touchedNaturals.TryGetValue(msg.Tile, out entityType))
        {
            // ไม่เคยแตะ = ไม่รู้ว่ามันคืออะไร (หรือเก็บไปแล้วเมื่อกี้) — ยกเลิกสะอาด
            RejectCollect(seq, "ไม่รู้จักของธรรมชาติชิ้นนี้", msg);
            return;
        }

        // ระยะ: client เดินไปถึงก่อนยิงอยู่แล้ว ตรงนี้แค่กันการยิงข้ามแมพ
        if (!IsWithinCollectRange(carcass?.Tile ?? msg.Tile))
        {
            RejectCollect(seq, "อยู่ไกลเกินไป", msg);
            return;
        }

        CollectibleTable.GeneratorSpec spec = CollectibleTable.FindGenerator(entityType, msg.GeneratorId);
        if (spec == null)
        {
            RejectCollect(seq, $"ไม่มี generator '{msg.GeneratorId}' ของชนิด {entityType}", msg);
            return;
        }

        // [7 ก.ย. 2026] ปลดสกิลของหมวดนี้แล้วหรือยัง
        //
        // ตอบ SkillNeeded(2449) ไม่ใช่ Abort — ฝั่งเกมเอาไปเปิดป๊อปอัพ "ต้องมีสกิล X"
        // พร้อมปุ่มเปิดหน้าสกิลไปที่โหนดนั้น (client/Durango.Logic/SkillSystem.cs:151-165)
        // ⇒ ผู้เล่นรู้ทันทีว่าต้องไปเรียนอะไร แทนที่จะกดแล้วเงียบ
        string genCategory = CollectibleTable.CategoryOfGenerator(spec);
        if (genCategory != null && !UnlockedCollectibleCategories().ContainsKey(genCategory))
        {
            Console.WriteLine($"[gather] {ShortId()} ยังไม่ได้ปลดหมวด '{genCategory}' (gen={spec.Id})");
            Send(BuildSkillNeededFor(genCategory), seq);
            return;
        }

        // generator ตัวนี้เหลือให้เก็บอีกไหม — ไม่งั้นกดรัวเอาของฟรีไม่จำกัด
        // (ฝั่งเกมทำปุ่มจางให้แล้วจาก Enabled=false แต่ห้ามเชื่อฝั่งเกม)
        //
        // ⚠️ เดิมเช็คแค่ Contains = "เคยเก็บไหม" ⇒ ต้นไม้ที่ให้กิ่งไม้ได้ 3 ครั้ง
        // เก็บได้ครั้งเดียวแล้วโดนปฏิเสธ ทั้งที่เมนูยังโชว์ว่าเหลืออยู่
        string harvestKey = HarvestKeyOf(carcass?.EntityId ?? msg.EntityId, carcass?.Tile ?? msg.Tile);
        int allowed = carcass != null
            ? CollectibleTable.AmountForCarcass(spec, carcass.CombatLevel)
            : spec.Amount;
        int already = 0;
        foreach (string id in _world.HarvestedGenerators(harvestKey))
        {
            if (string.Equals(id, spec.Id, StringComparison.Ordinal)) already++;
        }
        if (already >= allowed)
        {
            RejectCollect(seq, $"เก็บ '{spec.Id}' จากเป้านี้ครบ {allowed} ครั้งแล้ว", msg);
            return;
        }

        // เครื่องมือ: RequiredTools ที่ส่งไปกับ Generator คือชุดเดียวกับที่ตรวจตรงนี้
        // client เลือกเครื่องมือที่ดีที่สุดในกระเป๋าให้เองแล้ว (FindBestTool) แล้วส่ง id มาใน ToolItemId
        // ToolItemId ว่าง = "ใช้มือเปล่า" ⇒ ผ่านได้เฉพาะ generator ที่รับ bare_hands
        if (!HasRequiredTool(spec, msg.ToolItemId))
        {
            Send(BuildToolNeeded(spec), seq);
            return;
        }

        // แรง/ความเหนื่อย — สูตรจาก data/assets/constants.json ทั้งหมด ดู CollectibleTable
        // [7 ก.ย. 2026] คูณด้วยตัวคูณจากสกิลหมวดเอาชีวิตรอด (ดู Player.SkillEffects.cs)
        // ⚠️ ไม่คูณ = เรียนสกิลไปเท่าไรก็เปลืองพลังงานเท่าเดิม
        float energyCost = CollectibleTable.EnergyCost(spec.Effort) * EnergyCostScale();
        float fatigueCost = CollectibleTable.FatigueCost(energyCost);
        double now = Gauge.CurrentTime;
        bool lowEnergy = _survival.ValueAt(SurvivalState.KeyEnergy, now) < energyCost;

        // เปิดชุดคำตอบต่อเนื่องของ seq นี้ ไม่งั้น Collected ที่ตามมาทีหลังจะไม่ถึง handler
        Send(default(ReplySequenceMark), seq);
        if (lowEnergy)
        {
            // เตือนอย่างเดียว — client หยุดหลอดไว้ถามผู้เล่นแล้วเล่นต่อเองถ้ากดยืนยัน
            // (client/GatheringSystem.cs:381-397) ⇒ ฝั่งเซิร์ฟทำต่อตามปกติ
            // **นี่เป็นการตีความของเรา**: ข้อมูลจริงไม่ได้บอกว่าแรงไม่พอแล้วห้ามเก็บหรือแค่เตือน
            Send(default(EnergyWarning), seq);
        }
        // [7 ก.ย. 2026] สกิลทำให้เก็บเร็วขึ้น — ซากใช้หมวดชำแหละ ของธรรมชาติใช้หมวดเก็บของ
        // ⚠️ เวลาที่บอก client กับที่เซิร์ฟหน่วงจริงต้องเป็นค่าเดียวกัน ไม่งั้นหลอดวิ่งไม่ตรงของ
        float gatherDuration = Math.Max(GatheringTuning.MinCollectSeconds,
            spec.Duration * (carcass != null ? ButcheryDurationScale() : GatherDurationScale()));

        Send(new Messages.Timer { Duration = gatherDuration }, seq);

        // จอง generator ทันทีกันกดรัวระหว่างรอท่า (แบบ OpenCode TryReserveGenerator)
        // แต่ยังไม่ให้ของ / ไม่รีเฟรชปุ่ม / ไม่ลบเป้า — เลื่อนไปตอนครบเวลา
        _world.MarkGeneratorHarvested(harvestKey, spec.Id);

        // [7 ก.ย. 2026] "หมดเป้า" = เก็บครบทุกครั้งของ **ทุก** generator แล้ว
        // ⚠️ เดิมนับจำนวน generator ที่แตะ ⇒ ต้นไม้หายทั้งต้นตั้งแต่เก็บกิ่งไม้ครั้งแรก
        // ซากสัตว์คิดครั้งจากเลเวลตัวสัตว์ ของธรรมชาติใช้ตามที่ spec คิดไว้
        int taken = _world.HarvestedGenerators(harvestKey).Count;
        int total = 0;
        foreach (CollectibleTable.GeneratorSpec s in CollectibleTable.AllSpecs(entityType))
        {
            total += carcass != null
                ? CollectibleTable.AmountForCarcass(s, carcass.CombatLevel)
                : s.Amount;
        }
        bool ranOut = total > 0 && taken >= total;

        // หักแรงตอนเริ่มเก็บ (ลงมือแล้ว) — ของเข้ากระเป๋าเลื่อนไปตอนจบ
        _survival.Add(SurvivalState.KeyEnergy, -energyCost);
        _survival.Add(SurvivalState.KeyFatigue, fatigueCost);
        FlushSurvival();

        // [7 ก.ย. 2026] สกิลสูงมีโอกาสได้ของเพิ่มอีกชิ้น (ดู Player.SkillEffects.cs)
        int itemCount = CollectibleTable.ItemsPerCollect;
        bool bonus = carcass != null ? RollButcheryBonus() : RollGatherBonus();
        if (bonus) itemCount++;

        var items = new List<Item>();
        for (int i = 0; i < itemCount; i++)
        {
            Item? item = Cheats.MakeItem(spec.PrototypeId, spec.Level);
            if (!item.HasValue) continue;
            Item value = item.Value;
            // ผูกที่มาไว้กับตัวไอเทม — เกมใช้ตอนนับภารกิจ/สารานุกรม (Messages/Item.cs:53-55)
            value.CollectibleId = spec.CollectibleId;
            value.GeneratorId = spec.Id;
            items.Add(value);
        }
        if (items.Count == 0)
        {
            // prototype หาย — ถอนจองแล้วยกเลิก ไม่ปล่อยให้ผู้เล่นค้างหลอด
            UnreserveGenerator(harvestKey, spec.Id);
            Send(new Abort { Text = "ไม่พบไอเทมที่ควรจะได้" }, seq);
            Send(default(ReplySequenceMark), seq);
            return;
        }

        var collected = new Collected
        {
            Items = items.ToArray(),
            Result = Result.Success,
            ActionInfo = new ActionInfo
            {
                ActionLevel = spec.Level,
                PotentialLevel = spec.Level,
                // ยังไม่มีระบบสกิล/ความสามารถจริง (Core/Player.cs:283-294 ตอบ GetSkills เป็นชุดว่าง)
                // ⇒ ส่ง Invalid ตามที่ ActionInfo.Unpack รองรับ แล้วให้สำเร็จ 100%
                RelatedCategory = Shared.Skill.Category.Invalid,
                RelatedAbility = Shared.Ability.Derived.Invalid,
                SuccessRatio = 1f
            },
            // true เฉพาะตอนเก็บครบทุก generator แล้วจริง ๆ — ฝั่งเกมใช้ตัวนี้สั่ง TargetRunOut()
            RanOut = ranOut
        };
        Console.WriteLine($"[gather] {EntityId[..Math.Min(8, EntityId.Length)]} เริ่มเก็บ {spec.Id} x{items.Count} " +
                          $"จาก {spec.CollectibleId} ที่ ({msg.Tile.x},{msg.Tile.y}) — รอ {gatherDuration:0.#} วิ " +
                          $"· จองแล้ว {taken}/{total}" + (ranOut ? " (จะหมด)" : ""));

        Point2 tile = carcass?.Tile ?? msg.Tile;
        string entityId = msg.EntityId;
        bool isCarcass = carcass != null;
        ScheduleCollectFinish(collected, items, seq, gatherDuration, harvestKey, tile, entityId, isCarcass,
                              ranOut, msg.ToolItemId);
        OnContextChanged();
    }

    /// <summary>
    /// ยกเลิกคำขอเก็บ + เขียนเหตุผลลง log
    ///
    /// ต้องเห็นเหตุผลใน log จริง ๆ เพราะฝั่งเกมไม่โชว์อะไรเลยเมื่อเจอ Abort — client แค่หยุดหลอด
    /// เงียบ ๆ (client/GatheringSystem.cs:399-412 .All → OnGatheringFailed) กดแล้วไม่มีอะไรเกิดขึ้น
    /// </summary>
    private void RejectCollect(uint seq, string reason, Collect msg)
    {
        Console.WriteLine($"[gather] ปฏิเสธคำขอเก็บที่ ({msg.Tile.x},{msg.Tile.y}) gen='{msg.GeneratorId}': {reason}");
        Send(new Abort { Text = reason }, seq);
    }

    /// <summary>ส่งผลการเก็บแล้วปิดชุดคำตอบต่อเนื่องของ seq นั้น (ไม่ปิด = handler ฝั่ง client ค้าง)</summary>
    private void FinishCollect(Collected collected, uint seq)
    {
        Send(collected, seq);
        Send(default(ReplySequenceMark), seq);
    }

    /// <summary>ถอนจอง generator หนึ่งตัวออกจากรายการที่ Mark ไว้แล้ว</summary>
    private void UnreserveGenerator(string harvestKey, string generatorId)
    {
        List<string> harvested = new List<string>(_world.HarvestedGenerators(harvestKey));
        harvested.Remove(generatorId);
        _world.ForgetHarvests(harvestKey);
        foreach (string id in harvested)
        {
            _world.MarkGeneratorHarvested(harvestKey, id);
        }
    }

    /// <summary>
    /// หลังครบเวลา: ให้ของ + รีเฟรชปุ่ม/ลบเป้า + ส่ง Collected
    /// (เทียบ OpenCode: deferred callback หลัง Timer)
    /// </summary>
    private void CompleteCollectAfterDelay(
        Collected collected, List<Item> items, uint seq,
        string harvestKey, Point2 tile, string entityId, bool isCarcass, bool ranOut,
        string toolItemId)
    {
        AddItems(items);
        // ⚠️ ReplyOf = 0 (global push) — ถ้าตอบที่ seq ของ Collect ฝั่งเกมจะนับเป็น
        // "packet ที่ไม่ใช่ 104/1134/2019/3648" แล้วสั่ง StopCollectTimer + OnGatheringFailed
        // (client/GatheringSystem.cs:389-403 .All) ⇒ หลอดเก็บดับกลางคัน
        Send(new InventoryUpdated { EntityId = EntityId, Items = items.ToArray() });

        // [7 ก.ย. 2026] เครื่องมือสึกตอน "ได้ของจริง" เท่านั้น (ดู Player.ToolWear.cs)
        // ⚠️ สึกตอนกดเริ่ม = ยกเลิกกลางคัน/โดนปฏิเสธก็เสียความทนทานฟรี
        WearTool(toolItemId);

        // [7 ก.ย. 2026] ให้ exp ตอนของเข้ากระเป๋าจริง — ไม่ให้ตอนเริ่มหลอด/ตอนส่ง Collected
        // ชำแหละซากใช้หมวด Butchery · เก็บของธรรมชาติใช้ Gathering
        if (isCarcass)
        {
            AddExpForAction(SkillTuning.ButcherWeight, Shared.Skill.Category.Butchery, "ชำแหละ");
        }
        else
        {
            AddExpForAction(SkillTuning.GatherWeight, Shared.Skill.Category.Gathering, "เก็บของ");
        }

        if (ranOut && !isCarcass)
        {
            _world.DestroyNatural(tile);
            _touchedNaturals.Remove(tile);
        }
        else if (ranOut)
        {
            AnimalManager.Animal carcass = _world.AnimalManager?.Get(entityId);
            if (carcass != null)
            {
                carcass.Butchered = true;
            }
            _world.ForgetHarvests(harvestKey);
        }

        FinishCollect(collected, seq);

        // [7 ก.ย. 2026] ⚠️ ต้องส่ง **หลัง** Collected เท่านั้น
        //
        // CollectibleChanged ทำให้ฝั่งเกมยิง GetCollectible แล้วเอาผลไป SetCollectible
        // ซึ่งอ่าน InteractionSystem.Target ตอนนั้น (client/GatheringSystem.cs:96-111,193)
        // ถ้าส่งก่อน Collected รายการใหม่จะมาถึงตอนที่ CurrentGatheringData ยังค้างค่าเก่าอยู่
        // แล้ว Collected ตามมาล้างทีหลัง ⇒ เมนูกะพริบปิด-เปิด เก็บต่อเนื่องไม่ลื่น
        //
        // เรียงแบบนี้แล้วฝั่งเกมอัปเดตรายการในที่ ไม่ Reset ทั้งแผง
        // (InteractionMenuList.Clear ไม่แตะ ResetFrame ⇒ widget ไม่ RemoveAll)
        if (!ranOut)
        {
            Send(new CollectibleChanged { EntityId = entityId });
        }
        OnContextChanged();
        Console.WriteLine($"[gather] {EntityId[..Math.Min(8, EntityId.Length)]} จบเก็บครบเวลา · RanOut={ranOut}");
    }

    private void ScheduleCollectFinish(
        Collected collected, List<Item> items, uint seq, float duration,
        string harvestKey, Point2 tile, string entityId, bool isCarcass, bool ranOut,
        string toolItemId)
    {
        if (duration <= 0f || duration > GatheringTuning.MaxCollectSeconds)
        {
            CompleteCollectAfterDelay(collected, items, seq, harvestKey, tile, entityId, isCarcass, ranOut,
                                      toolItemId);
            return;
        }

        // จับค่าไว้ใน local กัน timer ปิดทับ
        Collected collectedCopy = collected;
        List<Item> itemsCopy = items;
        string harvestKeyCopy = harvestKey;
        Point2 tileCopy = tile;
        string entityIdCopy = entityId;
        bool isCarcassCopy = isCarcass;
        bool ranOutCopy = ranOut;
        uint seqCopy = seq;
        string toolCopy = toolItemId;

        System.Threading.Timer timer = null;
        timer = new System.Threading.Timer(delegate
        {
            try
            {
                // ส่ง packet + แตะ inventory/โลก — รูปแบบเดียวกับ craft/build ของเรา
                // (Send ปลอดภัยข้ามเธรด; AddItems/DestroyNatural ใช้บน timer ตามแพทเทิร์นเดิมของโปรเจกต์นี้)
                CompleteCollectAfterDelay(collectedCopy, itemsCopy, seqCopy, harvestKeyCopy, tileCopy,
                                          entityIdCopy, isCarcassCopy, ranOutCopy, toolCopy);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[gather] จบการเก็บไม่สำเร็จ: {e.Message}");
                try { FinishCollect(collectedCopy, seqCopy); } catch { /* ignore */ }
            }
            finally
            {
                lock (_collectTimers)
                {
                    _collectTimers.Remove(timer);
                }
                timer?.Dispose();
            }
        }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        lock (_collectTimers)
        {
            _collectTimers.Add(timer);
        }
        timer.Change((int)(duration * 1000f), System.Threading.Timeout.Infinite);
    }

    private void ClearCollectTimers()
    {
        lock (_collectTimers)
        {
            foreach (System.Threading.Timer timer in _collectTimers) timer.Dispose();
            _collectTimers.Clear();
        }
    }

    // ── การตรวจเงื่อนไข ─────────────────────────────────────────────────────────────

    /// <summary>ผู้เล่นอยู่ใกล้ช่องนั้นพอจะเก็บได้ไหม (1 tile = 200 world unit — Core/Player.cs:534)</summary>
    private bool IsWithinCollectRange(Point2 tile)
    {
        Movement[] movements = _context.AppearPlayer.Move.Movements;
        if (movements == null || movements.Length == 0 || movements[0].Path == null || movements[0].Path.Length == 0)
        {
            return true;    // ยังไม่รู้ตำแหน่ง — ไม่บล็อก ดีกว่าบล็อกผิด
        }
        WorldPosition pos = movements[0].Path[0].Position;
        float dx = pos.x / 200f - tile.x;
        float dy = pos.y / 200f - tile.y;
        return dx * dx + dy * dy <= GatheringTuning.RangeTiles * GatheringTuning.RangeTiles;
    }

    /// <summary>
    /// มีเครื่องมือตรงตามที่ generator ต้องการไหม
    ///
    /// เทียบแบบเดียวกับฝั่ง client (client/InteractionData/GatheringData.cs:117-129
    /// CanGateringWithThisTool): ไอเทมต้องมีแท็กชื่อตรงกันและเลเวลแท็ก >= ที่ต้องการ
    /// </summary>
    private bool HasRequiredTool(CollectibleTable.GeneratorSpec spec, string toolItemId)
    {
        if (spec.ToolRequirements.ContainsKey(CollectibleTable.BareHands)) return true;
        if (string.IsNullOrEmpty(toolItemId)) return false;
        int idx = _context.InventoryItems.FindIndex(it => it.Id == toolItemId);
        if (idx < 0) return false;
        Messages.Tag[] tags = _context.InventoryItems[idx].Tags;
        if (tags == null) return false;
        foreach (Messages.Tag tag in tags)
        {
            if (tag.Id != null && spec.ToolRequirements.TryGetValue(tag.Id, out int need) && tag.Level >= need)
            {
                return true;
            }
        }
        return false;
    }

    private static ToolNeeded BuildToolNeeded(CollectibleTable.GeneratorSpec spec)
    {
        // TagNames = ข้อความที่ popup เอาไปแปะตรง ๆ (client/GatheringSystem.cs:427-435)
        // RecipeIds ว่าง = ไม่โชว์ปุ่ม "ทำเครื่องมือ" (ยังไม่มีตัวเลือกสูตรที่ยืนยันได้ว่าถูกตัว)
        return new ToolNeeded
        {
            RecipeIds = Array.Empty<string>(),
            Skills = new Dictionary<string, Messages.Skill>(),
            TagNames = string.Join(", ", spec.ToolRequirements.Keys.Select(CollectibleTable.ToolDisplayName)),
            Tags = new Dictionary<string, int>(spec.ToolRequirements)
        };
    }
}

// ═══════════════════════════════════════════════════════════════════════════════════
/// <summary>
/// ตารางว่า "ของธรรมชาติชิ้นไหนเก็บแล้วได้อะไร" — ประกอบจากไฟล์ data จริงเป็นหลัก
///
/// ลำดับการหา generator ของ collectible หนึ่งตัว (หยุดที่ข้อแรกที่ได้ผล):
///   1. **ข้อมูลจริง** — คู่ (collectible_id → generator_id) ที่ขุดจาก
///      data/assets/item/recipes.json → slots[].source_info ที่ type == 2
///      (SourceDescription.Collect — client/Yaml/SlotSourceInfo.cs + client/Durango.UI.Popup/
///       SlotSourceWidget.cs:74 "เก็บ {generator} จาก {collectible}")
///      กรองเอาเฉพาะตัวที่เป็น prototype จริงใน prototype_data.json
///   2. **ข้อมูลจริง** — ถ้า collectible_id เองเป็น prototype id (เช่น sulphur, basalt, granite)
///      ก็ใช้ตัวมันเอง
///   3. **ค่าของเรา** — เดาตามหมวดจากคำนำหน้าของ collectible_id (tree_/bush_/grass_/rock_/ore_/…)
///      ตารางในข้อ 3 ไม่ได้เดาลอย ๆ แต่ถอดมาจากสถิติของข้อ 1: collectible ที่ขึ้นต้นด้วย tree_
///      และมีข้อมูลจริง ให้ wood_log/wood_bough มากที่สุด, bush_ ให้ wood_bough/leaf_small,
///      rock_ ให้ stone, ore_ ให้ ore_* ฯลฯ
///
/// เครื่องมือที่ต้องใช้ **เป็นค่าของเราทั้งหมด** (ไม่มีในข้อมูลไหนเลย) แต่ตัดสินจาก tags ของ
/// ไอเทมที่จะได้ ซึ่งเป็นข้อมูลจริงใน prototype_data.json — ดู <see cref="ToolsFor"/>
/// ชื่อแท็กเครื่องมือทุกตัวยืนยันว่ามีจริงใน data/assets/tags.json และตรงกับตารางท่าทางของเกม
/// (game/Durango_Data/resources.assets → MotionInfos/gathering_motion_map:
///  bare_hands / knife / pickaxe / shovel / axe_onehand_tool / axe_twohand_tool / saw / …)
/// </summary>
internal static class CollectibleTable
{
    public const string BareHands = "bare_hands";

    /// <summary>ขนาดของเป้า — ค่าที่เกมรู้จักมีสามค่านี้เท่านั้น (gathering_motion_map → "size")</summary>
    private const string SizeLow = "low";
    private const string SizeMiddle = "middle";
    private const string SizeHigh = "high";

    private static Dictionary<string, string[]> _collectibleGenerators;   // จาก recipes.json (ข้อมูลจริง)
    private static Dictionary<string, GeneratorClientData> _generatorNames;
    private static readonly Dictionary<int, Collectible> _cache = new();

    public sealed class GeneratorSpec
    {
        public string Id;
        public string CollectibleId;
        public string PrototypeId;
        public string Name;
        public string Icon;
        public int Level;
        public int Amount;

        /// <summary>ลำดับในเป้า (0 = ของหลัก) — ใช้คิดจำนวนชิ้นตอนแล่ซากที่ต้องใช้เลเวลสัตว์</summary>
        public int Order;

        public float Effort;
        public float Duration;
        public Dictionary<string, int> ToolRequirements;
    }

    // ── สูตรจาก data/assets/constants.json (ข้อมูลจริงล้วน) ──────────────────────────

    /// <summary>effort_standard.collect = "2.5 + (level - 1) * 0.25"</summary>
    public static float Effort(int level) => 2.5f + (level - 1) * 0.25f;

    /// <summary>duration_formula = "e" — เวลาที่ใช้ (วินาที) เท่ากับ effort ตรง ๆ</summary>
    public static float Duration(float effort) => effort;

    /// <summary>energy_formula = "e * 0.7"</summary>
    public static float EnergyCost(float effort) => effort * 0.7f;

    /// <summary>
    /// fatigue_cost.collect = "0.4 * e ** 0.5"
    ///
    /// ⚠️ **การตีความของเรา**: ไฟล์ไม่ได้บอกว่า e ในสูตรนี้คือ effort หรือ energy ที่ใช้จริง
    /// เลือกใช้ "energy ที่ใช้ไป" เพราะ fatigue_cost อยู่คู่กับกลุ่มค่าที่คิดจากแรงที่เสียไป
    /// (combat = "4 * e", default = "e") ⇒ ที่เลเวล 1: energy 1.75 → fatigue ≈ 0.53
    /// </summary>
    public static float FatigueCost(float energy) => 0.4f * Mathf.Sqrt(Mathf.Max(0f, energy));

    // ── การประกอบ Collectible ───────────────────────────────────────────────────────

    /// <param name="animalLevel">
    /// เลเวลของสัตว์ถ้าเป้านี้เป็นซาก (0 = ของธรรมชาติ) — ซากแล่ได้กี่ครั้งขึ้นกับเลเวลตัวสัตว์
    /// </param>
    /// <param name="unlockedCategories">
    /// [7 ก.ย. 2026] หมวดของที่ปลดจากสกิลแล้ว (หมวด → ระดับสูงสุด) — null = ไม่กรอง
    ///
    /// ⚠️ ต้องกรองตั้งแต่ตอนประกอบเมนู ไม่ใช่ปฏิเสธตอนกด เพราะฝั่งเกมไม่แสดงข้อความใด ๆ
    /// เมื่อการเก็บถูก Abort ⇒ โชว์ปุ่มที่กดไม่ได้ = ผู้เล่นกดแล้วเงียบโดยไม่รู้เหตุผล
    /// </param>
    public static Collectible Build(string entityId, ushort entityType,
                                    IReadOnlyList<string> harvested = null, int animalLevel = 0,
                                    Dictionary<string, int> unlockedCategories = null)
    {
        if (!_cache.TryGetValue(entityType, out Collectible template))
        {
            template = BuildTemplate(entityType);
            _cache[entityType] = template;
        }
        template.EntityId = entityId;

        // ซาก: จำนวนครั้งที่แล่ได้คิดจากเลเวลตัวสัตว์ ไม่ใช่เลเวลไอเทม (ดู AmountForCarcass)
        // ⚠️ ต้องสร้างอาเรย์ใหม่ ห้ามแก้ของเดิม — template ที่แคชไว้ใช้ร่วมกันทุกผู้เล่น
        if (animalLevel > 0 && template.Generators != null)
        {
            List<GeneratorSpec> specs = SpecsFor(entityType);
            var scaled = new Generator[template.Generators.Length];
            for (int i = 0; i < template.Generators.Length; i++)
            {
                Generator gen = template.Generators[i];
                if (i < specs.Count) gen.Amount = AmountForCarcass(specs[i], animalLevel);
                scaled[i] = gen;
            }
            template.Generators = scaled;
        }

        // [7 ก.ย. 2026] ของที่ยังไม่ได้ปลดสกิล — **คงปุ่มไว้ในเมนู แต่ปิดไม่ให้ใช้**
        //
        // ⚠️ ห้ามตัดออกจากรายการ: ผู้เล่นต้องเห็นว่าซากนี้ยังมีอะไรให้เอาอีก จะได้รู้ว่า
        //    ควรไปปลดสกิลอะไร ถ้าตัดทิ้งเลยจะไม่มีทางรู้ว่ามีของซ่อนอยู่
        // Enabled = false ทำให้ฝั่งเกมหรี่ปุ่มลง (client/Durango.UI/InteractionMenuWidgetBase.cs:213
        //    IsAvailableForGathering() = false ⇒ Alpha = _alphaBgDisabled)
        // แล้วตอนกดจริง เซิร์ฟตอบ SkillNeeded(2449) ⇒ เกมเด้งป๊อปอัพ "ต้องมีสกิล X"
        //    พร้อมปุ่ม "ดูสกิล" ที่เปิดหน้าสกิลไปที่โหนดนั้นให้เลย (SkillSystem.cs:151-165)
        if (unlockedCategories != null && template.Generators != null)
        {
            List<GeneratorSpec> specs = SpecsFor(entityType);
            var marked = new Generator[template.Generators.Length];
            for (int i = 0; i < template.Generators.Length; i++)
            {
                Generator gen = template.Generators[i];
                string category = i < specs.Count ? CategoryOfGenerator(specs[i]) : null;
                // ไม่มีหมวดคุม = ของพื้นฐาน เก็บได้เสมอ (กิ่งไม้/ใบไม้/หญ้า)
                if (category != null && !unlockedCategories.ContainsKey(category)) gen.Enabled = false;
                marked[i] = gen;
            }
            template.Generators = marked;
        }

        // generator ที่เก็บไปแล้ว ต้อง **ตัดออกจากรายการ** ไม่ใช่แค่ปิด Enabled
        //
        // ⚠️ เคยลองใช้ Enabled=false แล้วไม่ได้ผล: ฝั่งเกมตั้ง Disabled=false ให้ปุ่มเก็บของเสมอ
        //    (client/Durango.UI/InteractionMenuWidgetBase.cs:199,213 — Enabled แค่หรี่ Alpha)
        //    แล้ว ReadyForGathering ก็ยิง Collect ออกมาทุกกรณี ใช้ IsAvailableForGathering()
        //    แค่ตัดสินว่าจะโชว์หลอดคาดการณ์ไหม (client/GatheringSystem.cs:349)
        //    ⇒ ผู้เล่นกดปุ่มเดิมซ้ำได้เรื่อย ๆ แล้วโดนเซิร์ฟปฏิเสธรัว ๆ
        //
        // ตัดออกแล้วฝั่งเกมลบปุ่มให้เอง: SetCollectible ตั้ง IsValid=false ทั้งลิสต์
        // แล้วเปิดคืนเฉพาะตัวที่มากับ Collectible จากนั้นลบตัวที่ IsValid ยังเป็น false ทิ้ง
        // (client/GatheringSystem.cs:207-238)
        //
        // ⚠️ ต้องสร้างอาเรย์ใหม่ ห้ามแก้ของเดิม — template ที่แคชไว้ใช้ร่วมกันทุกผู้เล่นทุกชิ้น
        if (harvested != null && harvested.Count > 0 && template.Generators != null)
        {
            var left = new List<Generator>(template.Generators.Length);
            foreach (Generator gen in template.Generators)
            {
                // [7 ก.ย. 2026] เหลืออีกกี่ครั้ง = จำนวนครั้งที่ให้ได้ − ครั้งที่เก็บไปแล้ว
                // ⚠️ เดิมเช็คแค่ Contains ⇒ เก็บครั้งเดียวก็หายจากเมนูทั้งที่ยังมีเหลือ
                int taken = 0;
                foreach (string id in harvested)
                {
                    if (string.Equals(id, gen.Id, StringComparison.Ordinal)) taken++;
                }
                int remain = gen.Amount - taken;
                if (remain <= 0) continue;

                // บอกฝั่งเกมว่าเหลืออีกกี่ครั้ง — struct จึงคัดลอกค่าไป ไม่กระทบ template ที่แคชไว้
                Generator copy = gen;
                copy.Amount = remain;
                left.Add(copy);
            }
            template.Generators = left.ToArray();
        }
        return template;
    }

    /// <summary>เป้าชนิดนี้มี generator ทั้งหมดกี่ตัว — ใช้ตัดสินว่าเก็บครบแล้วหรือยัง</summary>
    public static int GeneratorCount(ushort entityType) => SpecsFor(entityType).Count;

    /// <summary>generator ทั้งชุดของเป้าชนิดนี้ — ใช้รวมจำนวนครั้งที่เก็บได้ทั้งหมด</summary>
    public static IReadOnlyList<GeneratorSpec> AllSpecs(ushort entityType) => SpecsFor(entityType);

    /// <summary>
    /// [7 ก.ย. 2026] หมวดของที่ generator ตัวนี้สังกัด — ใช้ตรวจว่าผู้เล่นปลดสกิลแล้วหรือยัง
    ///
    /// ที่มาเป็น **ข้อมูลจริงล้วน**: rewards.json (type 0) ใช้ชื่อหมวดชุดเดียวกับ tag ของไอเทม
    /// ใน item/prototype_data.json — ตรวจแล้ว 29 จาก 41 หมวดมี tag ชื่อตรงกันเป๊ะ
    /// (fat → tag "fat" · meat → tag "meat" · leather → tag "leather" · ore/stone/wood/…)
    /// ⇒ อ่าน tag ของไอเทมที่จะได้ แล้วเทียบกับหมวดที่รางวัลสกิลปลดไว้
    ///
    /// คืน null = ไม่มีหมวดไหนคุมของชิ้นนี้ ⇒ เก็บได้เสมอ (ของพื้นฐานอย่างกิ่งไม้/ใบไม้)
    /// ⚠️ ต้องคืน null ไม่ใช่ปิดตาย — ไม่งั้นผู้เล่นใหม่เก็บอะไรไม่ได้เลยสักอย่าง
    /// </summary>
    public static string CategoryOfGenerator(GeneratorSpec spec)
    {
        if (spec == null) return null;
        if (_categoryOfPrototype.TryGetValue(spec.PrototypeId, out string cached)) return cached;

        string found = null;
        Prototype proto = PrototypeYaml.GetItemPrototype(spec.PrototypeId);
        if (proto?.Tags != null)
        {
            foreach (string tag in proto.Tags.Keys)
            {
                if (SkillDataStore.CollectibleCategories.Contains(tag)) { found = tag; break; }
            }
        }
        _categoryOfPrototype[spec.PrototypeId] = found;
        return found;
    }

    private static readonly Dictionary<string, string> _categoryOfPrototype = new(StringComparer.Ordinal);

    /// <summary>
    /// ได้ไอเทมกี่ชิ้นต่อการกดเก็บ **หนึ่งครั้ง**
    ///
    /// 1 ชิ้นต่อครั้ง แล้วให้กดซ้ำได้ตามจำนวนที่ <c>Generator.Amount</c> บอก
    /// (แบบเดียวกับโปรเจกต์ Opencode — ServerWorld.TryReserveGenerator หัก Amount ทีละ 1)
    /// ⇒ ต้นไม้ที่ให้กิ่งไม้ 3 = กดเก็บได้ 3 ครั้ง ครั้งละกิ่ง ไม่ใช่กดทีเดียวได้ 3 กิ่งแล้วหาย
    /// </summary>
    public const int ItemsPerCollect = 1;

    /// <summary>
    /// ซากสัตว์แล่ได้กี่ครั้งต่อชิ้นส่วน — คิดจากเลเวลของสัตว์ ไม่ใช่เลเวลของไอเทม
    ///
    /// วัตถุดิบธรรมชาติทุกตัวมี min_level = 1 ⇒ ใช้ spec.Level จะได้เท่ากันหมดทุกตัว
    /// ⇒ ล่าไดโนเสาร์ตัวใหญ่ไม่คุ้มกว่าล่ากิ้งก่าเลย ซึ่งขัดกับที่เกมออกแบบไว้
    /// </summary>
    public static int AmountForCarcass(GeneratorSpec spec, int animalLevel) =>
        GatheringTuning.AmountFor(spec.Order, animalLevel);

    public static GeneratorSpec FindGenerator(ushort entityType, string generatorId)
    {
        foreach (GeneratorSpec spec in SpecsFor(entityType))
        {
            if (spec.Id == generatorId) return spec;
        }
        return null;
    }

    private static Collectible BuildTemplate(ushort entityType)
    {
        List<GeneratorSpec> specs = SpecsFor(entityType);
        string collectibleId = CollectibleIdOf(entityType);
        return new Collectible
        {
            CollectibleId = collectibleId,
            Size = SizeOf(collectibleId),
            Generators = specs.Select(ToMessage).ToArray(),
            // ตัวที่เกมไฮไลต์ว่าเป็น "ของหลัก" ของเป้านี้ (client/GatheringSystem.cs:215)
            CriticalGenerator = specs.Count > 0 ? specs[0].Id : string.Empty
        };
    }

    private static Generator ToMessage(GeneratorSpec spec) => new()
    {
        Id = spec.Id,
        Level = spec.Level,
        Name = spec.Name,
        Icon = spec.Icon,
        Amount = spec.Amount,
        Effort = spec.Effort,
        Duration = spec.Duration,
        ToolRequirements = spec.ToolRequirements,
        Enabled = true
    };

    /// <summary>
    /// ของที่แล่ได้จากซากสัตว์ทุกชนิด — **ค่าของเรา** (ดูเหตุผลเต็มใน SpecsFor)
    /// เรียงตามที่ผู้เล่นน่าจะอยากได้ก่อน: เนื้อ → หนัง → กระดูก → ไขมัน
    /// </summary>
    private static readonly string[] CarcassGenerators = { "meat", "leather_raw", "bone_leg", "fat" };

    private static readonly Dictionary<ushort, List<GeneratorSpec>> _specCache = new();

    private static List<GeneratorSpec> SpecsFor(ushort entityType)
    {
        if (_specCache.TryGetValue(entityType, out List<GeneratorSpec> cached)) return cached;
        var list = new List<GeneratorSpec>();
        string collectibleId = CollectibleIdOf(entityType);
        if (!string.IsNullOrEmpty(collectibleId))
        {
            var ids = new List<string>(PrototypesFor(collectibleId));

            // [7 ก.ย. 2026] ⚠️ ซากสัตว์ส่วนใหญ่แล่ไม่ได้เลย เพราะไม่มี generator สักตัว
            //
            // ที่มาของ generator ปกติคือ item/recipes.json (source_info type=2) ซึ่งครอบคลุม
            // collectible แค่ 115 ตัวจาก 607 ตัวใน item/collectible_names.json
            // ⇒ สัตว์อย่าง raptor_coward / iguanodon ไม่มีสูตรไหนอ้างถึงเลย ได้ลิสต์ว่าง
            //   (phenaco โชคดีมี bone_leg + leather_raw เพราะบังเอิญมีสูตรใช้)
            // และ FamilyFallback คัดตามชื่อของพืช/หิน/แร่ ⇒ ชื่อสัตว์ไม่เข้าเงื่อนไขไหนเลย
            // ผลคือฝั่งเกมไม่มีปุ่มแล่สักปุ่ม = "แล่เนื้อไม่ได้"
            //
            // **ค่าของเรา** — เติมชุดพื้นฐานของซากสัตว์ให้ทุกชนิด
            // ไม่ได้คิดขึ้นเอง: คอมเมนต์ของ CollectibleIdOf ระบุไว้ตั้งแต่แรกว่าปลายทางควรเป็น
            // "meat / leather_raw / bone_leg / fat" และทั้งสี่มีอยู่จริงใน item/prototype_data.json
            // (ตรวจแล้ว) ⇒ ถ้าสูตรให้ generator อะไรมาก็ใช้ของจริงก่อน แล้วเติมที่ขาด
            if (AnimalTypes.Get(entityType) != null)
            {
                foreach (string id in CarcassGenerators)
                {
                    if (!ids.Contains(id)) ids.Add(id);
                }
            }

            foreach (string prototypeId in ids)
            {
                // ลำดับในลิสต์ = ความสำคัญ (ตัวแรกคือของหลักที่เกมไฮไลต์เป็น CriticalGenerator)
                // ส่งเข้าไปให้ MakeSpec คิดจำนวนชิ้น — ของหลักได้เยอะกว่าของรอง
                GeneratorSpec spec = MakeSpec(collectibleId, prototypeId, list.Count);
                if (spec != null) list.Add(spec);
            }
        }
        _specCache[entityType] = list;
        return list;
    }

    /// <summary>collectible_id ของ entity type — จาก data/assets/entity_types/natural.json (ข้อมูลจริง)</summary>
    private static string CollectibleIdOf(ushort entityType)
    {
        // [5 ก.ย. 2026] ซากสัตว์ใช้ทางเดียวกับของธรรมชาติ — ต่างแค่ที่มาของ collectible id
        // animal.json → drop_item เป็น collectible id จริง ๆ: เอาไปหาใน recipes.json
        // (source_info type=2) แล้วได้ generator เป็น meat / leather_raw / bone_leg / fat
        // ⇒ เสียบตรงนี้จุดเดียว ระบบเก็บทั้งชุด (เครื่องมือ แรง เวลา ของที่ได้) ใช้ต่อได้เลย
        AnimalTypes.Info animal = AnimalTypes.Get(entityType);
        if (animal?.DropItem != null) return animal.DropItem;

        BiomeSpriteInfo info = DataHelper.GetBiomeSpriteInfo(entityType);
        return info?.CollectibleId;
    }

    /// <param name="order">ลำดับที่เท่าไรในเป้านี้ (0 = ของหลัก) — ใช้คิดจำนวนชิ้นที่ได้</param>
    private static GeneratorSpec MakeSpec(string collectibleId, string prototypeId, int order)
    {
        Prototype proto = PrototypeYaml.GetItemPrototype(prototypeId);
        if (proto == null) return null;
        // เลเวลของ generator = min_level ของไอเทมที่จะได้ (ข้อมูลจริง — ทุกวัตถุดิบธรรมชาติเป็น 1)
        int level = Mathf.Max(1, proto.MinLevel);
        float effort = Effort(level);
        GeneratorClientData client = GeneratorNames().Get(prototypeId);
        return new GeneratorSpec
        {
            Id = prototypeId,
            CollectibleId = collectibleId,
            PrototypeId = prototypeId,
            // ชื่อ/ไอคอนจาก generator_client_data.json ถ้ามี ไม่มีก็ใช้ของไอเทมเอง (จริงทั้งคู่)
            Name = client?.name != null ? client.name.ToString() : proto.Name?.ToString() ?? prototypeId,
            Icon = !string.IsNullOrEmpty(client?.icon) ? client.icon : proto.Icon,
            Level = level,
            Amount = GatheringTuning.AmountFor(order, level),
            Order = order,
            Effort = effort,
            Duration = Duration(effort),
            ToolRequirements = ToolsFor(proto)
        };
    }

    // ── ข้อ 1-3: หา prototype ที่ควรได้จาก collectible หนึ่งตัว ──────────────────────

    private static IEnumerable<string> PrototypesFor(string collectibleId)
    {
        // รวมทั้งสามชั้น ไม่ใช่หยุดที่ชั้นแรก — เพราะข้อมูลจริงในข้อ 1 มัก "ไม่ครบ":
        // recipes.json บอกแค่ของที่ **มีสูตรใช้มันเป็นวัตถุดิบ** เท่านั้น เช่น tree_redfir มีแต่
        // resin (เพราะมีสูตรที่ใช้ยางไม้) ทั้งที่ต้นไม้ต้องให้ท่อนซุง/กิ่งไม้ด้วย
        // ⇒ เอาของจริงขึ้นก่อน (เป็นตัวหลัก = CriticalGenerator) แล้วเติมของหมวดตามหลัง
        var result = new List<string>();
        void Add(string id)
        {
            if (!string.IsNullOrEmpty(id) && !result.Contains(id)) result.Add(id);
        }
        // 1) ข้อมูลจริงจาก recipes.json
        string[] fromRecipes = CollectibleGenerators().Get(collectibleId);
        if (fromRecipes != null)
        {
            foreach (string id in fromRecipes) Add(id);
        }
        // 2) ชื่อ collectible เป็น prototype อยู่แล้ว (sulphur / basalt / granite / clay_gray …)
        if (PrototypeYaml.GetItemPrototype(collectibleId) != null) Add(collectibleId);
        // 3) ของประจำหมวด (ค่าของเรา)
        foreach (string id in FamilyFallback(collectibleId)) Add(id);
        return result;
    }

    /// <summary>
    /// **ค่าของเรา** — หมวดสำรองเมื่อไม่มีข้อมูลจริง (ถอดจากสถิติของข้อมูลจริงในข้อ 1)
    ///
    /// ครอบคลุมส่วนใหญ่ของโลก: จาก 358 collectible_id ใน natural.json เป็น tree_ 102,
    /// grass_ 99, rock_ 64, bush_ 55, ore_ 26 = 346 ตัว
    /// </summary>
    private static string[] FamilyFallback(string collectibleId)
    {
        string id = collectibleId.ToLowerInvariant();
        if (id.StartsWith("tree") || id.Contains("tree") || id.StartsWith("timber") || id.StartsWith("dead"))
            return new[] { "wood_log", "wood_bough" };
        if (id.StartsWith("bush") || id.StartsWith("vine"))
            return new[] { "wood_bough", "leaf_small" };
        if (id.StartsWith("mushroom"))
            return new[] { "mushroom" };
        if (id.StartsWith("grass") || id.StartsWith("flower") || id.StartsWith("cactus") || id.StartsWith("moss"))
            return new[] { "leaf" };
        if (id.StartsWith("ore") || id.StartsWith("jewel") || id.StartsWith("silver") || id.StartsWith("metal"))
            return new[] { "ore_iron" };
        if (id.StartsWith("rock") || id.StartsWith("stone") || id.StartsWith("marble") || id.StartsWith("obsidian")
            || id.StartsWith("granite") || id.StartsWith("basalt") || id.StartsWith("crater") || id.StartsWith("cliff"))
            return new[] { "stone" };
        if (id.StartsWith("mud") || id.StartsWith("clay") || id.StartsWith("dirt") || id.StartsWith("sand"))
            return new[] { "clay" };
        if (id.StartsWith("bone"))
            return new[] { "bone_leg" };
        // ที่เหลือ (กล่อง/รถ/แผงลอย/ของอีเวนต์ ฯลฯ) ไม่รู้จริง ๆ ว่าให้อะไร — ไม่ให้เก็บ ดีกว่าให้ของมั่ว
        return Array.Empty<string>();
    }

    /// <summary>
    /// **ค่าของเรา** — เครื่องมือที่ต้องใช้ ตัดสินจาก tags จริงของไอเทมที่จะได้
    ///
    /// หลักคิด: ของที่ต้องออกแรงทุบ/ฟันถึงจะได้ ต้องมีเครื่องมือ · ของที่เด็ดมือเปล่าได้ ให้ bare_hands
    /// ⇒ ผู้เล่นเกิดใหม่มือเปล่าเก็บหญ้า/กิ่งไม้/หินก้อนเล็กได้ทันที เอาไปทำขวานกับพลั่วต่อ
    ///   ส่วนต้นไม้ใหญ่กับสายแร่ต้องมีเครื่องมือก่อน (= ทางที่ ToolNeeded ถูกใช้จริง)
    /// </summary>
    private static Dictionary<string, int> ToolsFor(Prototype proto)
    {
        int lv = GatheringTuning.ToolLevel;
        bool Has(string tag) => proto.Tags != null && proto.Tags.ContainsKey(tag);

        // สายแร่/อัญมณี — ต้องมีพลั่วหรือค้อน
        if (Has("ore") || Has("jewel"))
        {
            return new Dictionary<string, int> { { "pickaxe", lv }, { "hammer_twohand", lv }, { "hammer_onehand", lv } };
        }
        // หินก้อนใหญ่ (chunk_big + stone: basalt/granite/lava_dried) — ต้องทุบ
        if (Has("stone") && Has("chunk_big"))
        {
            return new Dictionary<string, int> { { "pickaxe", lv }, { "hammer_twohand", lv }, { "hammer_onehand", lv } };
        }
        // ท่อนซุง (pillar_normal = ต้นไม้ทั้งต้น) — ต้องมีขวาน/เลื่อย
        if (Has("pillar_normal"))
        {
            return new Dictionary<string, int>
            {
                { "axe_onehand_tool", lv }, { "axe_twohand_tool", lv }, { "saw", lv }, { "chainsaw", lv }
            };
        }
        // ไม้/กิ่งไม้ — มือเปล่าก็หักได้ มีมีดหรือขวานยิ่งดี (ท่าทางจะเปลี่ยนตามเครื่องมือ)
        if (Has("wood"))
        {
            return new Dictionary<string, int> { { BareHands, lv }, { "knife", lv }, { "axe_onehand_tool", lv } };
        }
        // ดิน/โคลน — มือเปล่าหรือพลั่ว
        if (Has("dirt"))
        {
            return new Dictionary<string, int> { { BareHands, lv }, { "shovel", lv } };
        }
        // ที่เหลือ (หญ้า/ใบไม้/ผล/เห็ด/หินก้อนเล็ก) — มือเปล่าได้ มีมีดยิ่งดี
        return new Dictionary<string, int> { { BareHands, lv }, { "knife", lv } };
    }

    /// <summary>**ค่าของเรา** — ขนาดเป้า มีผลกับท่าทางที่ client เล่นเท่านั้น</summary>
    private static string SizeOf(string collectibleId)
    {
        if (string.IsNullOrEmpty(collectibleId)) return SizeLow;
        string id = collectibleId.ToLowerInvariant();
        if (id.StartsWith("tree") || id.Contains("tree")) return SizeHigh;
        if (id.StartsWith("bush") || id.StartsWith("cactus")) return SizeMiddle;
        return SizeLow;
    }

    /// <summary>ชื่อเครื่องมือที่เอาไปโชว์ใน popup "ต้องใช้เครื่องมือ" — จาก data/assets/tags.json (ข้อมูลจริง)</summary>
    public static string ToolDisplayName(string tagId)
    {
        TagInfo tag = Tags().Get(tagId);
        return tag?.name != null ? tag.name.ToString() : tagId;
    }

    // ── การโหลดไฟล์ data (ครั้งเดียว ตอนถูกใช้ครั้งแรก) ─────────────────────────────

    private static Dictionary<string, string[]> CollectibleGenerators()
    {
        if (_collectibleGenerators != null) return _collectibleGenerators;
        _collectibleGenerators = new Dictionary<string, string[]>();
        var raw = Json.ReadFromFile<Dictionary<string, SourceProbeRecipe>>("item/recipes");
        if (raw == null)
        {
            Console.WriteLine("[gather] ⚠️ ไม่พบ item/recipes.json — ใช้หมวดสำรองทั้งหมด");
            return _collectibleGenerators;
        }
        var acc = new Dictionary<string, List<string>>();
        foreach (SourceProbeRecipe recipe in raw.Values)
        {
            if (recipe?.slots == null) continue;
            foreach (SourceProbeSlot slot in recipe.slots)
            {
                if (slot?.source_info == null) continue;
                foreach (SlotSourceInfo info in slot.source_info)
                {
                    // type 2 = SourceDescription.Collect ("เก็บ {generator} จาก {collectible}")
                    if (info == null || info.type != 2) continue;
                    if (string.IsNullOrEmpty(info.collectible_id) || string.IsNullOrEmpty(info.generator_id)) continue;
                    // generator ที่ไม่ใช่ prototype จริง (ของอีเวนต์/season ที่ถูกตัดออกจากไฟล์ item)
                    // ให้ทิ้ง ไม่งั้นจะได้ Generator ที่กดแล้วไม่มีของ
                    if (PrototypeYaml.GetItemPrototype(info.generator_id) == null) continue;
                    if (!acc.TryGetValue(info.collectible_id, out List<string> list))
                    {
                        list = new List<string>();
                        acc[info.collectible_id] = list;
                    }
                    if (!list.Contains(info.generator_id)) list.Add(info.generator_id);
                }
            }
        }
        foreach (var pair in acc) _collectibleGenerators[pair.Key] = pair.Value.ToArray();
        Console.WriteLine($"[gather] ขุดคู่ collectible→generator จาก recipes.json ได้ {_collectibleGenerators.Count} ตัว");
        return _collectibleGenerators;
    }

    private static Dictionary<string, GeneratorClientData> GeneratorNames()
    {
        return _generatorNames ??= Json.ReadFromFile<Dictionary<string, GeneratorClientData>>("item/generator_client_data")
                                   ?? new Dictionary<string, GeneratorClientData>();
    }

    private static Dictionary<string, TagInfo> _tags;

    private static Dictionary<string, TagInfo> Tags()
    {
        return _tags ??= Json.ReadFromFile<Dictionary<string, TagInfo>>("tags") ?? new Dictionary<string, TagInfo>();
    }

    // ── data class ของไฟล์ JSON (ชื่อฟิลด์ snake_case ตรงกับไฟล์ ⇒ Newtonsoft อ่านตรง ๆ) ──
    // อ่านเฉพาะฟิลด์ที่ระบบนี้ใช้ — ฟิลด์อื่นใน JSON ถูกข้ามไปเอง
    // 649 = "ฟิลด์ไม่เคยถูก assign" — จริงตามที่คอมไพเลอร์เห็น เพราะคนที่ใส่ค่าคือ Newtonsoft
    // ตอน deserialize ไม่ใช่โค้ดในไฟล์นี้
#pragma warning disable 649

    public class SourceProbeRecipe
    {
        public SourceProbeSlot[] slots;
    }

    public class SourceProbeSlot
    {
        public SlotSourceInfo[] source_info;
    }

    public class GeneratorClientData
    {
        public Gettext name;
        public string icon;
    }

    public class TagInfo
    {
        public Gettext name;
    }

#pragma warning restore 649
}
