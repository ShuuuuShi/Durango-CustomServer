using System;
using System.Collections.Generic;
using Durango.Utils;
using Messages;

namespace Durango.Online;

/// <summary>
/// [5 ก.ย. 2026] ตัวเชื่อมระหว่าง "ของที่อยู่ในหน่วยความจำ" กับ "ไฟล์เซฟของผู้เล่น"
///
/// ═══ ปัญหาที่ไฟล์นี้แก้ ═══
/// สองอย่างนี้เดิมอยู่ในหน่วยความจำล้วน รีสตาร์ตเซิร์ฟทีเดียวหายหมด:
///   1. <c>Player.PetStore</c> (Core/Player.Animals.cs:1364) — สัตว์เลี้ยงทั้งหมดของทุกคน
///      เจ้าของไฟล์นั้นเปิดเป็น public ไว้แล้วโดยเขียนกำกับว่า "ให้เสียบ persistence ทีหลังได้
///      โดยไม่ต้องแก้ handler" ⇒ ไฟล์นี้คือตัวที่มาเสียบ ไม่ต้องแตะ Player.Animals.cs เลย
///   2. <c>_deathCount</c> (Core/Player.Combat.cs:313) — จำนวนครั้งที่ตาย ที่ใช้เปิดแถวตาราง
///      death_penalty · ต่อใหม่แล้วนับ 0 ใหม่ = บทลงโทษไม่สะสม ผู้เล่นได้เปรียบ
///      (แตะฟิลด์ private ของอีกไฟล์ได้เพราะเป็น <c>partial class Player</c> คลาสเดียวกัน
///       ⇒ ไม่ต้องแก้ Player.Combat.cs ซึ่งเป็นไฟล์ต้องห้าม)
///
/// ═══ เกาะกับ hook ที่มีอยู่แล้ว ไม่สร้างใหม่ ═══
///   • <c>ContextChanged</c> (Core/Player.cs:63) — event ที่ Core/GameServer.cs:186 เอาไปเซฟไฟล์
///     ไฟล์นี้สมัครเข้า event เดียวกันจาก <see cref="RegisterPetSaveHandlers"/> ซึ่งถูกเรียกใน
///     ตัวสร้าง Player (ผ่าน RegisterSystemHandlers) — **ก่อน** ที่ GameServer จะสมัครของตัวเอง
///     (GameServer สมัครหลัง <c>new Player(...)</c> คืนค่าแล้ว) ⇒ ลำดับการเรียกของ event คือ
///     ตามลำดับสมัคร ⇒ เราเขียนข้อมูลลง context เสร็จก่อน ไฟล์ถึงจะถูกเขียน ทุกครั้ง
///   • <c>_connection.ConnetionClosed</c> — เซฟรอบสุดท้ายตอนหลุด (เหตุผลที่ต้องมี ดูที่เมธอด)
/// </summary>
public partial class Player
{
    private void RegisterPetSaveHandlers()
    {
        LoadPersistedState();
        ContextChanged += FlushPersistedState;
        _connection.ConnetionClosed += SaveOnDisconnect;
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  โหลด: ไฟล์เซฟ → PetStore / _deathCount
    // ══════════════════════════════════════════════════════════════════════════════════

    private void LoadPersistedState()
    {
        _deathCount = _context.DeathCount;

        List<PetStore.Entry> store = PetStore.Of(EntityId);

        // context ที่ไม่มี Path คือ context ชั่วคราว (ยังไม่ผ่าน /players — ดู Core/Gateway.cs:112)
        // มันไม่มีวันถูกเขียนลงดิสก์ ⇒ _context.Pets ว่างเสมอ · ถ้าล้างสโตร์ตามนั้นด้วย สัตว์ที่
        // ผู้เล่นเพิ่งได้มาในเซสชันนี้จะหายทั้งที่ยังต่ออยู่ ⇒ กรณีนี้ปล่อยของในหน่วยความจำไว้
        if (string.IsNullOrEmpty(_context.Path) && store.Count > 0) return;

        store.Clear();
        // ปกติ PlayerContext.Initialize สร้างลิสต์ให้แล้วทุกทาง — เช็ค null ไว้เผื่อวันหน้ามีคนสร้าง
        // context โดยไม่เรียก Initialize (NRE ตรงนี้ = ผู้เล่นเข้าเกมไม่ได้เลย ราคาสูงเกินกว่าจะเสี่ยง)
        foreach (PetSaveData saved in _context.Pets ?? new List<PetSaveData>())
        {
            PetStore.Entry entry = FromSave(saved);
            if (entry != null) store.Add(entry);
        }
        if (store.Count > 0)
        {
            Console.WriteLine($"[สัตว์] {EntityId[..Math.Min(8, EntityId.Length)]} " +
                              $"โหลดสัตว์เลี้ยงจากไฟล์เซฟ {store.Count} ตัว (ตายมาแล้ว {_deathCount} ครั้ง)");
        }
    }

    /// <summary>ประกอบ <c>PetStore.Entry</c> กลับจากข้อมูลในไฟล์ — null = ข้อมูลเสีย ข้ามตัวนี้ไป</summary>
    private static PetStore.Entry FromSave(PetSaveData saved)
    {
        if (saved == null || string.IsNullOrEmpty(saved.Pet.EntityId)) return null;

        var entry = new PetStore.Entry
        {
            Pet = saved.Pet,
            Grazing = saved.Grazing,
            LifeMax = saved.LifeMax,
            HungryMax = saved.HungryMax,
            HungryVelocity = saved.HungryVelocity,
            PendingMilestoneTag = saved.PendingMilestoneTag,
            PendingMilestoneTagLevel = saved.PendingMilestoneTagLevel,
            PendingMilestoneSlot = saved.PendingMilestoneSlot,
            MilestoneRedrawCount = saved.MilestoneRedrawCount,
            SkillRedrawCount = saved.SkillRedrawCount,
            PendingRank = saved.PendingRank,
            PendingRankTag = saved.PendingRankTag
        };
        // Bag ประกาศเป็น readonly List ใน PetStore.Entry ⇒ เติมเข้าไป ไม่ใช่แทนที่ทั้งลิสต์
        if (saved.Bag != null) entry.Bag.AddRange(saved.Bag);

        // ── ธงที่ต้องรีเซ็ต: หลังรีสตาร์ตไม่มีสัตว์ตัวไหนอยู่ในโลกแล้ว ──────────────────
        // ถ้าปล่อย IsSpawned = true ค้างไว้ ผู้เล่นจะเรียกสัตว์ตัวอื่นออกมาแล้ว
        // HandleSpawnPetMsg (Player.Animals.cs:578-581) จะไล่ DespawnPet ให้ "ผี" ตัวนี้ก่อน
        // แล้ว BroadCast DisappearPet ของสัตว์ที่ไม่เคยโผล่ · และตัวมันเองก็เรียกออกมาไม่ได้
        // เพราะ HandleReturnPetMsg เชื่อว่ามันอยู่ข้างตัวอยู่แล้ว
        entry.Pet.IsSpawned = false;
        entry.Pet.IsBoarding = false;

        // ── หลอดสองอันต้องสร้างใหม่ ห้ามใช้ก้อนที่โหลดมาตรง ๆ ─────────────────────────
        // GaugeConverter ย่อ Gauge เหลือ {min,max,cur} ตอนเขียนไฟล์ (Support/GaugeConverter.cs:13-20)
        // ⇒ "เส้นแนวโน้ม" หายหมด เหลือ node เดียวที่ Time = 0 · หลอดอิ่มจะค้างนิ่งไม่ลดอีกเลย
        // เหตุผลเดียวกับที่ PlayerContext.Initialize ต้องเรียก SurvivalState.Reset ทุกครั้งที่โหลด
        //
        // ค่า cur ที่ converter เก็บไว้คือค่า ณ วินาทีที่เซฟ ⇒ ความหิวหยุดเดินระหว่างเซิร์ฟดับ
        // (ตั้งใจ — ตรงกับหลอดของผู้เล่นที่ Freeze ตอนหลุด ดู Core/Player.cs:511-515)
        double now = Times.UnixTimeNow();
        float lifeCur = saved.Pet.Stat.Life?.Get(now) ?? saved.LifeMax;
        float hungryCur = saved.Pet.Stat.Hungry?.Get(now) ?? saved.HungryMax;
        entry.Pet.Stat.Life = new Gauge(saved.LifeMax, 0f, new[] { new GaugeNode(now, lifeCur) });
        entry.Pet.Stat.Hungry = PetFactory.HungryGauge(saved.HungryMax, saved.HungryVelocity, hungryCur, now);

        // ── ราคาหมุนซ้ำ: คิดใหม่ ไม่ใช่อ่านจากไฟล์ ───────────────────────────────────
        // Money (server/GameCode/Money.cs:14-16) มีแต่ฟิลด์ readonly ⇒ Newtonsoft เขียนลงไฟล์ได้
        // แต่อ่านกลับไม่ได้ (ฟิลด์ readonly ถือว่าเขียนไม่ได้) ⇒ จะได้ Money(0, TStone) = โชว์ "ฟรี"
        // ⇒ คิดใหม่จากสูตรจริง costs.json → pet_revert_milestone เมื่อยังมีผลหมุนค้างรอกดยืนยัน
        entry.Pet.Stat.RetryCost = entry.PendingMilestoneSlot >= 0
            ? PetTables.Costs.MilestoneRetryCost(entry.Pet.Statistics.Level, entry.MilestoneRedrawCount + 1)
            : null;

        return entry;
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  เขียนกลับ: PetStore / _deathCount → context (แล้ว GameServer เขียนลงไฟล์ต่อ)
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// สมัครเข้า <c>ContextChanged</c> ⇒ ทำงานทุกครั้งก่อนไฟล์ถูกเขียน
    /// ห้ามเรียก <c>OnContextChanged()</c> ในนี้เด็ดขาด (จะวนไม่รู้จบ)
    /// </summary>
    private void FlushPersistedState()
    {
        _context.DeathCount = _deathCount;

        List<PetStore.Entry> store = PetStore.Of(EntityId);
        var list = new List<PetSaveData>(store.Count);
        foreach (PetStore.Entry entry in store) list.Add(ToSave(entry));
        _context.Pets = list;
    }

    private static PetSaveData ToSave(PetStore.Entry entry)
    {
        return new PetSaveData
        {
            // Item เป็น struct ⇒ ก๊อบปี้ลิสต์พอ ไม่ต้องโคลนทีละชิ้น
            // (ยกเว้นช่อง Ext ที่เป็น object แต่ระบบไม่เคยแก้ของเดิม มีแต่สร้างใหม่ทับ)
            Pet = entry.Pet,
            Grazing = entry.Grazing,
            Bag = new List<Item>(entry.Bag),
            LifeMax = entry.LifeMax,
            HungryMax = entry.HungryMax,
            HungryVelocity = entry.HungryVelocity,
            PendingMilestoneTag = entry.PendingMilestoneTag,
            PendingMilestoneTagLevel = entry.PendingMilestoneTagLevel,
            PendingMilestoneSlot = entry.PendingMilestoneSlot,
            MilestoneRedrawCount = entry.MilestoneRedrawCount,
            SkillRedrawCount = entry.SkillRedrawCount,
            PendingRank = entry.PendingRank,
            PendingRankTag = entry.PendingRankTag
        };
    }

    /// <summary>
    /// เซฟรอบสุดท้ายตอนสายหลุด — **จำเป็น ไม่ใช่เผื่อไว้เฉย ๆ**
    ///
    /// การเปลี่ยนแปลงของสัตว์หลายอย่างไม่ได้เรียก <c>OnContextChanged</c> เอง เพราะตอนเขียน
    /// Core/Player.Animals.cs ยังไม่มีที่เซฟ: ขึ้น/ลงหลังสัตว์ (HandleMountPetMsg),
    /// ตั้งชื่อ (HandleRenamePetMsg), เรียกออกมา/เก็บ (HandleSpawnPetMsg/HandleReturnPetMsg),
    /// กาชา milestone/สกิล/แรงก์ ทั้งชั้น E · และทาง "ออกเรือ" (Core/Player.cs:1592) ก็เรียก
    /// <c>_context.Save()</c> ตรง ๆ โดยไม่ผ่าน ContextChanged
    /// ⇒ ถ้าไม่มีจุดนี้ ของที่เปลี่ยนหลัง ContextChanged ครั้งสุดท้ายจะหายไปเงียบ ๆ
    ///
    /// ลำดับตอนหลุด: Core/Player.cs:511 แช่หลอดผู้เล่นก่อน (สมัครไว้ก่อนเรา) → มาถึงตัวนี้
    /// → OnContextChanged → FlushPersistedState → GameServer เขียนไฟล์
    /// </summary>
    private void SaveOnDisconnect()
    {
        OnContextChanged();
    }
}
