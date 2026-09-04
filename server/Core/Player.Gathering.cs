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
    /// <summary>**ค่าของเรา** — จำนวนชิ้นที่ได้ต่อการเก็บหนึ่งครั้ง (ของจริงอยู่ในตาราง generator ที่ไม่มี)</summary>
    public const int Amount = 1;

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
        return CollectibleTable.Build(entityId, entityType);
    }

    private void HandleGetCollectibleMsg(GetCollectible msg, uint seq)
    {
        _touchedNaturals.TryGetValue(msg.Tile, out ushort entityType);
        Send(CollectibleTable.Build(msg.EntityId, entityType), seq);
    }

    // ── ตอน Collect: ตรวจ → ตอบ Timer → ครบเวลาส่ง Collected ────────────────────────

    private void HandleCollectMsg(Collect msg, uint seq)
    {
        // ซากสัตว์: ฝั่งเกมส่ง Tile มาเป็น (-1,-1) เพราะสัตว์ไม่ได้อยู่กลางช่องเหมือนต้นไม้
        // ⇒ ถ้าหาด้วย tile ไม่เจอ ให้ลองหาด้วย EntityId (สัตว์มี id จริง ต่างจากของธรรมชาติ)
        AnimalManager.Animal carcass = _world.AnimalManager?.Get(msg.EntityId);
        if (carcass != null && carcass.IsAlive) carcass = null;         // ยังไม่ตาย = ชำแหละไม่ได้

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

        // เครื่องมือ: RequiredTools ที่ส่งไปกับ Generator คือชุดเดียวกับที่ตรวจตรงนี้
        // client เลือกเครื่องมือที่ดีที่สุดในกระเป๋าให้เองแล้ว (FindBestTool) แล้วส่ง id มาใน ToolItemId
        // ToolItemId ว่าง = "ใช้มือเปล่า" ⇒ ผ่านได้เฉพาะ generator ที่รับ bare_hands
        if (!HasRequiredTool(spec, msg.ToolItemId))
        {
            Send(BuildToolNeeded(spec), seq);
            return;
        }

        // แรง/ความเหนื่อย — สูตรจาก data/assets/constants.json ทั้งหมด ดู CollectibleTable
        float energyCost = CollectibleTable.EnergyCost(spec.Effort);
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
        Send(new Messages.Timer { Duration = spec.Duration }, seq);

        // ── แก้สถานะจริงบนเธรดหลัก (ดูหมายเหตุที่ _collectTimers) ──
        _survival.Add(SurvivalState.KeyEnergy, -energyCost);
        _survival.Add(SurvivalState.KeyFatigue, fatigueCost);
        FlushSurvival();

        var items = new List<Item>();
        for (int i = 0; i < spec.Amount; i++)
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
            // prototype หายจากไฟล์ data — ปิดชุดคำตอบแล้วยกเลิก ไม่ปล่อยให้ผู้เล่นค้างหลอด
            Send(new Abort { Text = "ไม่พบไอเทมที่ควรจะได้" }, seq);
            Send(default(ReplySequenceMark), seq);
            return;
        }

        AddItems(items);
        Send(new InventoryUpdated { EntityId = EntityId, Items = items.ToArray() });

        if (carcass == null)
        {
            // ของชิ้นนี้หมดแล้ว — ลบออกจากโลก (broadcast DisappearEntityOnTile ให้ทุกคนเอง
            // ผ่าน World.NaturalDestroyed ที่ Core/Player.cs:94-97)
            _world.DestroyNatural(msg.Tile);
            _touchedNaturals.Remove(msg.Tile);
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
            // ซากสัตว์ชำแหละได้หลายส่วน (เนื้อ หนัง กระดูก ไขมัน) ⇒ ยังไม่หมดในครั้งเดียว
            RanOut = carcass == null
        };
        Console.WriteLine($"[gather] {EntityId[..Math.Min(8, EntityId.Length)]} เก็บ {spec.Id} x{items.Count} " +
                          $"จาก {spec.CollectibleId} ที่ ({msg.Tile.x},{msg.Tile.y}) — {spec.Duration:0.#} วิ");
        ScheduleCollected(collected, seq, spec.Duration);
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

    private void ScheduleCollected(Collected collected, uint seq, float duration)
    {
        if (duration <= 0f || duration > GatheringTuning.MaxCollectSeconds)
        {
            FinishCollect(collected, seq);
            return;
        }
        System.Threading.Timer timer = null;
        timer = new System.Threading.Timer(delegate
        {
            try
            {
                FinishCollect(collected, seq);      // Send เท่านั้น — ไม่แตะ inventory/โลก
            }
            catch (Exception e)
            {
                Console.WriteLine($"[gather] ส่งผลการเก็บไม่สำเร็จ: {e.Message}");
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

    public static Collectible Build(string entityId, ushort entityType)
    {
        if (!_cache.TryGetValue(entityType, out Collectible template))
        {
            template = BuildTemplate(entityType);
            _cache[entityType] = template;
        }
        template.EntityId = entityId;
        return template;
    }

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

    private static readonly Dictionary<ushort, List<GeneratorSpec>> _specCache = new();

    private static List<GeneratorSpec> SpecsFor(ushort entityType)
    {
        if (_specCache.TryGetValue(entityType, out List<GeneratorSpec> cached)) return cached;
        var list = new List<GeneratorSpec>();
        string collectibleId = CollectibleIdOf(entityType);
        if (!string.IsNullOrEmpty(collectibleId))
        {
            foreach (string prototypeId in PrototypesFor(collectibleId))
            {
                GeneratorSpec spec = MakeSpec(collectibleId, prototypeId);
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

    private static GeneratorSpec MakeSpec(string collectibleId, string prototypeId)
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
            Amount = GatheringTuning.Amount,
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
