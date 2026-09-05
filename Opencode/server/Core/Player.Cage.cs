using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Durango.Network;
using Durango.Utils;
using JetBrains.Annotations;
using Messages;
using Newtonsoft.Json;
using Shared.Ability;
using Shared.Animal;

namespace Durango.Online;

/// <summary>
/// [5 ก.ย. 2026] โรงเลี้ยงสัตว์ (GrowCage) — เอาสัตว์ที่เชื่องแล้วไปเลี้ยง ให้อาหาร และสั่งงานผลิตของ
///
/// ═══ ลำดับที่เกมยิงจริง (client/Durango.UI/GrowCageGroup.cs) ═══
///   แตะกรง → Interaction.Cage(514) [Core/Player.cs:1038 ส่งให้แล้ว] → GrowCageGroup.Open(artifact)
///   :102 Open() เช็ค <c>PetUtil.GetGrowCage(artifact)</c> ก่อน — **ถ้า ArtifactState.Cage ไม่ใช่
///        GrowCage หน้าจอไม่เปิดเลย** (Support/CageTypes.cs เติมให้ตอนสร้าง/โหลดโลกแล้ว)
///   :186 Refresh() วาดทุกอย่างจาก <c>PetUtil.GetGrowCage(_cage)</c> ล้วน ๆ ไม่ได้ถามเซิร์ฟเลย
///   :122 OnArtifactStateChange → SetArtifact → MarkAsDirty → Refresh
/// ⇒ **ทุก handler ในไฟล์นี้ต้องจบด้วยการเขียน ArtifactState.Cage แล้วผลัก ArtifactState ออกไป**
///    ตอบ OK เฉย ๆ = ปุ่มกดติดแต่หน้าจอไม่ขยับสักนิด (ดู <see cref="WriteCage"/>)
///
/// ปุ่มบนหน้าจอ → message ที่ยิง (client/Durango.UI/GrowCagePetInfoWidget.cs:80-140):
///   ช่องว่างในรายการ  → PutInCage(809)      · ปุ่ม "เอาออก"   → TakeOutFromCage(810)
///   ปุ่มให้อาหาร      → FeedInCage(65101)   · ปุ่มผลิต/ฝึก    → GetAvailableTask(65106) แล้ว StartPetTask(65102)
///   ปุ่มหยุด         → CancelPetTask(65103) · ปุ่ม "ยืนยัน"   → FinishPetTask(65104)
/// ปุ่มชุดไหนโผล่ตัดสินจาก <c>TaskStatus</c> ของสัตว์ตัวนั้นล้วน ๆ (GrowCagePetInfoWidget:190-205):
///   ไม่มี TaskStatus = ชุดปกติ · มีแต่ยังไม่ถึง Until = ชุดกำลังทำงาน · เลย Until = ปุ่มเก็บผลงาน
///
/// ═══ เวลา ═══
/// ไม่มีจุดเกาะรายเฟรมให้เดินเวลาของงาน ⇒ ใช้ "คิดตอนถูกถาม": เก็บ Since/Until ไว้ใน TaskStatus
/// (หน่วยเดียวกับ Times.UnixTimeNow) แล้วตรวจตอน FinishPetTask ว่าถึงเวลาหรือยัง
/// ฝั่งเกมนับถอยหลังเองจาก Until เทียบ <c>GetPredictedServerTime()</c> จึงไม่ต้อง push อะไรระหว่างทาง
///
/// ═══ ข้อมูลจริงที่ไฟล์นี้ใช้ (ไม่มีตัวเลขไหนเดา เว้นที่เขียนกำกับว่า "ค่าของเรา") ═══
///   data/assets/pet/pet_task.json          48 งาน — type / animal_by_product / unlock_level /
///                                          duration / exp / hungry_required / produced_prototype /
///                                          random_prototype(_count/_rate) / product_quantity / product_level
///   data/assets/pet/pets_for_client.json   by_product ของสัตว์แต่ละชนิด (คู่กับ animal_by_product)
///   data/assets/performance.json → reins   size = สัตว์ตัวนั้นกินที่ในกรงเท่าไร
///   data/assets/performance.json → pet_food  vigor · decrease_grow_time · decrease_grow_time_ratio
///   data/assets/constants.json → pet       task_time (สูตรร่นเวลาตอนป้อนอาหารระหว่างทำงาน)
///   data/assets/tags.json                  product_quantity_plus / product_quantity_amplifier
///
/// ⚠️ **ห้ามแก้ Core/Player.Animals.cs** — handler กรงชุดเดิมที่นั่นตอบ Abort ไว้ ไฟล์นี้ทับด้วยการ
/// ลงทะเบียนใหม่ (Connection.Recv ลบ handler เดิมของ TypeCode นั้นก่อนเสมอ — Connection.cs:198)
/// ⇒ <see cref="RegisterCageHandlers"/> ต้องถูกเรียก **หลัง** RegisterAnimalHandlers()
///    และหลัง RegisterInventoryHandlers() (ตัวนั้นจอง TakeOutFromCage ไว้ที่ Player.Inventory.cs:846)
/// </summary>
public partial class Player
{
    // ══════════════════════════════════════════════════════════════════════════════════
    //  ค่าที่ "เราตั้งเอง" ของระบบโรงเลี้ยง — รวมไว้ที่เดียว
    // ══════════════════════════════════════════════════════════════════════════════════

    private static class CageTuning
    {
        /// <summary>
        /// **ค่าของเรา** — ค่าฐานของ <c>animal_product_quantity</c> (Derived.AnimalProductQuantity 307)
        ///
        /// สูตรจำนวนของที่ผลิตได้ในไฟล์จริงคือ <c>int(animal_product_quantity * 1.0)</c> ไปจนถึง
        /// <c>int(animal_product_quantity * (18.0/3))</c> (pet_task.json → product_quantity)
        /// แต่ **ค่าฐานของตัวแปรนี้ไม่มีอยู่ใน /assets เลย** — ค้นครบทุกไฟล์แล้วเจอแต่โมดิฟายเออร์
        /// (tags.json → product_quantity_plus_1 สูตร "level * 0.4" · product_quantity_amplifier_10
        ///  สูตร "0.24 + 0.21 * level") แปลว่าค่าฐานอยู่บนเซิร์ฟจริงของ NEXON
        ///
        /// ⇒ ตั้ง 1.0 เพราะเป็นค่าเดียวที่ทำให้ขนาดของโมดิฟายเออร์สมเหตุสมผล: แท็กบวกระดับ 1
        /// ให้ +0.4 (= +40%) และแท็กคูณระดับ 1 ให้ +24% ซึ่งอยู่ในระดับเดียวกันพอดี
        /// (ถ้าฐานเป็น 10 แท็กบวกจะกลายเป็น +4% ซึ่งไม่สมดุลกับแท็กคูณเลย)
        /// ผลลัพธ์: งานผลิต Lv.1 ได้ 1 ชิ้น · Lv.6 ได้ 6 ชิ้น (ก่อนคิดแท็ก)
        /// </summary>
        public const float AnimalProductQuantityBase = 1.0f;

        /// <summary>
        /// **ค่าของเรา** — เพดานเลเวลสัตว์เลี้ยง
        ///
        /// ไฟล์ข้อมูลไม่มีคีย์ "max_level" ของสัตว์ แต่ทุกอย่างหยุดที่ 60 พร้อมกัน:
        /// performance.json ทุกช่วงเป็น "[1, 60]" · constants.json → pet → milestone_level
        /// ช่องสุดท้ายคือเลเวล 60 · active_skill_levels = [60]
        /// ⇒ 60 คือเพดานที่ข้อมูลบอกเป็นนัย ไม่ใช่เลขที่คิดขึ้นมาเอง
        /// </summary>
        public const int MaxPetLevel = 60;
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ลงทะเบียน handler (ทับชุดที่ตอบ Abort ไว้ใน Player.Animals.cs / Player.Inventory.cs)
    // ══════════════════════════════════════════════════════════════════════════════════

    private void RegisterCageHandlers()
    {
        _connection.Recv(delegate(PutInCage msg, PacketHeader header)
        {
            HandlePutInCageMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(TakeOutFromCage msg, PacketHeader header)
        {
            HandleTakeOutFromCageMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(FeedInCage msg, PacketHeader header)
        {
            HandleFeedInCageMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(StartPetTask msg, PacketHeader header)
        {
            HandleStartPetTaskMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(CancelPetTask msg, PacketHeader header)
        {
            HandleCancelPetTaskMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(FinishPetTask msg, PacketHeader header)
        {
            HandleFinishPetTaskMsg(msg, header.Seq);
        });
        // GetAvailableTask — ทับของเดิมใน Player.Animals.cs:514 เพราะตัวนั้นยังไม่ครบสองข้อ:
        //   (1) ไม่ได้กรองด้วยชนิดผลผลิตของสัตว์ ⇒ วัวเห็นงาน "ถอนขนนก"/"ลอกเกล็ดอิกัวนา" ด้วย
        //   (2) กรองงานที่เลเวลยังไม่ถึงทิ้ง ทั้งที่ฝั่งเกมตั้งใจโชว์แบบกดไม่ได้
        //       (client/Durango.UI.Popup/SelectPetTaskItemWidget.cs:113-117 ตั้งปุ่มเป็น
        //        "Lv.N ขึ้นไป" + Disabled เมื่อ pet.Statistics.Level < task.UnlockLevel)
        _connection.Recv(delegate(GetAvailableTask msg, PacketHeader header)
        {
            HandleCageAvailableTaskMsg(msg, header.Seq);
        });
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  เอาสัตว์เข้า / ออก
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// เอาสัตว์เลี้ยงเข้าโรงเลี้ยง — PutInCage (809) · client/PetManager.cs:875-889 ใช้ .All(IsSuccess)
    ///
    /// สัตว์ **ไม่ได้หายไปจาก PetStore** — นี่คือดีไซน์ของเกมเอง: client/Durango.UI/GrowCageGroup.cs:230
    /// คัดสัตว์ที่ "ใส่กรงได้" ด้วยเงื่อนไข <c>CageInfo == null || RegionId ว่าง</c> จากรายการที่ได้จาก
    /// GetPetsInfo ⇒ ตัวที่อยู่ในกรงต้องยังอยู่ในรายการแต่ติด CageInfo ไว้
    /// และฝั่งเกมใช้ CageInfo ตัวเดียวกันนี้ปิดปุ่ม "เรียกออกมา/ปล่อย/แปลงเป็นบังเหียน"
    /// (client/Durango.UI/PetInfoWidget.cs:307-320) ⇒ ไม่ต้องไปแก้ HandleSpawnPetMsg
    ///
    /// ที่ในกรงคิดเป็น "ขนาด" ไม่ใช่ "จำนวนตัว" — <c>Pet.Stat.Size</c> มาจาก
    /// performance.json → reins → size (ฝั่งเกมโชว์ผลรวมนี้เอง: SelectPetPopup.cs:256
    /// <c>used + _selected.Value.Stat.Size</c> / capacity)
    /// </summary>
    private void HandlePutInCageMsg(PutInCage msg, uint seq)
    {
        GrowCage? found = _world.ArtifactManager.GetGrowCage(msg.EntityId);
        if (!found.HasValue)
        {
            Send(new Abort { Text = "สิ่งปลูกสร้างนี้ไม่ใช่โรงเลี้ยงสัตว์" }, seq);
            return;
        }
        GrowCage cage = found.Value;

        PetStore.Entry entry = PetStore.Find(EntityId, msg.PetId);
        if (entry == null)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้" }, seq);
            return;
        }
        if (IsInCage(entry.Pet) || FindCagePet(cage, msg.PetId).HasValue)
        {
            Send(new Abort { Text = "สัตว์ตัวนี้อยู่ในโรงเลี้ยงอยู่แล้ว" }, seq);
            return;
        }
        if (entry.Grazing)
        {
            // ปล่อยเล็มหญ้าอยู่ = อยู่ในทุ่งของโลก ไม่ใช่ในมือผู้เล่น ⇒ ต้องเก็บกลับก่อน
            // (ฝั่งเกมกันไว้แล้วที่ PetGroup.cs:587 แต่กันซ้ำที่เซิร์ฟ ของจะได้ไม่ไปอยู่สองที่)
            Send(new Abort { Text = "ต้องเก็บสัตว์กลับจากทุ่งเล็มหญ้าก่อน" }, seq);
            return;
        }

        int size = PetSizeOf(entry.Pet);
        if (cage.RemainSize < size)
        {
            Send(new Abort { Text = $"พื้นที่ในโรงเลี้ยงไม่พอ (ต้องการ {size} เหลือ {cage.RemainSize})" }, seq);
            return;
        }

        // เรียกออกมาเดินตามอยู่ก็ต้องเก็บก่อน ไม่งั้นตัวสัตว์ค้างบนแผนที่ทั้งที่อยู่ในกรงแล้ว
        if (entry.Pet.IsSpawned) DespawnPet(entry);

        entry.Pet.CageInfo = BuildCageInfo(msg.EntityId);
        EnsureProductQuantity(entry);

        Messages.Pet snapshot = entry.Pet;
        bool ok = WriteCage(msg.EntityId, c =>
        {
            List<Messages.Pet> pets = CagePetList(c);
            pets.Add(snapshot);
            c.Pets = new Messages.Pets { Data = pets.ToArray() };
            c.RemainSize = ClampToByte(c.RemainSize - size);
            return c;
        });
        if (!ok)
        {
            entry.Pet.CageInfo = PetStore.NotInCage;         // ย้อนกลับ — กรงหายไประหว่างทาง
            Send(new Abort { Text = "โรงเลี้ยงหลังนี้หายไปแล้ว" }, seq);
            return;
        }

        Console.WriteLine($"[กรง] {ShortId()} เอา {entry.Pet.Name} (ขนาด {size}) เข้าโรงเลี้ยง {msg.EntityId}");
        Send(default(OK), seq);
    }

    /// <summary>
    /// เอาสัตว์ออกจากโรงเลี้ยง — TakeOutFromCage (810) · client/PetManager.cs:891-905 ใช้ .All(IsSuccess)
    ///
    /// **ทับ handler ที่ Core/Player.Inventory.cs:846-855 จองไว้ตอบ Abort**
    ///
    /// ฝั่งเกมโชว์ปุ่ม "เอาออก" เฉพาะตอนที่สัตว์ไม่มีงานค้าง (GrowCagePetInfoWidget.cs:190-205
    /// SetNormalButtons ซ่อนปุ่มนี้ทันทีที่มี TaskStatus) ⇒ เซิร์ฟบังคับกฎเดียวกันไว้กันคำสั่งปลอม
    /// ไม่งั้นงานที่ทำค้างจะกลายเป็นผีค้างใน Tasks โดยไม่มีสัตว์ให้เก็บผล
    /// </summary>
    private void HandleTakeOutFromCageMsg(TakeOutFromCage msg, uint seq)
    {
        GrowCage? found = _world.ArtifactManager.GetGrowCage(msg.EntityId);
        if (!found.HasValue)
        {
            Send(new Abort { Text = "สิ่งปลูกสร้างนี้ไม่ใช่โรงเลี้ยงสัตว์" }, seq);
            return;
        }
        GrowCage cage = found.Value;

        Messages.Pet? inCage = FindCagePet(cage, msg.PetId);
        if (!inCage.HasValue)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้ในโรงเลี้ยง" }, seq);
            return;
        }
        if (inCage.Value.TamerEntityId != EntityId)
        {
            Send(new Abort { Text = "สัตว์ตัวนี้เป็นของคนอื่น" }, seq);
            return;
        }
        if (TaskOf(cage, msg.PetId).HasValue)
        {
            Send(new Abort { Text = "ต้องหยุดงานหรือเก็บผลงานให้เสร็จก่อนถึงจะเอาสัตว์ออกได้" }, seq);
            return;
        }

        int size = PetSizeOf(inCage.Value);
        bool ok = WriteCage(msg.EntityId, c =>
        {
            List<Messages.Pet> pets = CagePetList(c);
            pets.RemoveAll(p => p.EntityId == msg.PetId);
            c.Pets = new Messages.Pets { Data = pets.ToArray() };
            c.RemainSize = ClampToByte(c.RemainSize + size, c.Size);
            return c;
        });
        if (!ok)
        {
            Send(new Abort { Text = "โรงเลี้ยงหลังนี้หายไปแล้ว" }, seq);
            return;
        }

        // เอาเฉพาะ "สิ่งที่กรงเป็นเจ้าของ" กลับเข้าสโตร์ — เลเวล/exp/แท็ก/ค่าหลอดอิ่ม
        //
        // ⚠️ **ห้ามเอาก้อนในกรงทับทั้งก้อน** อย่างที่เคยทำ: ก้อนนั้นมาจาก ArtifactState.Cage
        // ซึ่งถูกเซฟ/โหลดผ่าน JSON ⇒ หลังรีสตาร์ตเซิร์ฟ หลอด Life/Hungry ในนั้นอาจไม่ครบ
        // (เพิ่งแก้ตัวกู้ที่ CageTypes.NormalizeLoaded ให้ใช้ converter ถูกชุดแล้ว แต่ก้อนใน
        //  PetStore ถูกซ่อมมาอย่างถูกต้องกว่าเสมอ — ดู Player.PetSave.cs) ⇒ ทับแล้วสัตว์
        // กลายเป็นหลอด 0 ถาวร สั่งงานไม่ได้อีกเลย
        PetStore.Entry entry = CagePetEntry(inCage.Value);
        Messages.Pet fromCage = inCage.Value;
        Messages.Pet kept = entry.Pet;
        kept.Statistics = fromCage.Statistics;               // เลเวล/exp เดินต่อในกรง
        kept.Stat = fromCage.Stat;                           // แท็ก milestone ที่ได้ระหว่างอยู่ในกรง
        entry.Pet = kept;
        entry.Pet.CageInfo = PetStore.NotInCage;
        Console.WriteLine($"[กรง] {ShortId()} เอา {inCage.Value.Name} ออกจากโรงเลี้ยง {msg.EntityId}");
        // ไม่ push PetsInfo ตามไป: ฝั่งเกมผูก handler ของ PetsInfo ไว้กับ seq ของคำขอเท่านั้น
        // (client/PetManager.cs:828-841 .On(...) หลัง Send) ⇒ ก้อนที่ push ไปเฉย ๆ ถูกทิ้ง
        // หน้าจอสัตว์เลี้ยงยิง GetPetsInfo ใหม่ทุกครั้งที่เปิดอยู่แล้ว
        Send(default(OK), seq);
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ให้อาหาร
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ป้อนอาหารสัตว์ในโรงเลี้ยง — FeedInCage (65101) · client/PetManager.cs:843-858 ใช้ .All(IsSuccess)
    ///
    /// ทำสองอย่างพร้อมกัน ตามที่หน้าต่างเลือกอาหารของเกมโชว์ไว้
    /// (client/Durango.UI.Popup/PetItemInteractionPopup.cs:446-479 OnUpdateItemByTaskFeed):
    ///   1. เติมหลอดอิ่ม — performance.json → pet_food → &lt;prototype&gt; → vigor
    ///   2. ถ้ากำลังทำงานอยู่ **ร่นเวลาจบงาน** ด้วยสูตรจริง constants.json → pet → task_time
    ///        <c>max(since, until - (total_time * t_ratio + t_time))</c>
    ///      โดย t_ratio = ผลรวม decrease_grow_time_ratio · t_time = ผลรวม decrease_grow_time
    ///      ของอาหารที่ป้อน (constants.json → pet → performance_reference บอกชื่อคีย์ไว้ตรง ๆ)
    ///      สูตรหนีบไม่ให้ต่ำกว่า since เอง ⇒ ร่นได้มากสุดคือ "จบทันที" ไม่มีทางย้อนก่อนเริ่ม
    ///
    /// ⚠️ ตอนนี้ฝั่งเกม **พรีวิวเวลาที่ร่นไม่ได้** เพราะ Core/Cheats.cs MakeItem แนบเฉพาะ vigor
    /// ลงใน Performance "pet_food" ของไอเทม (ไม่ได้แนบ decrease_grow_time*) ⇒ ConstantPet.GetTaskEndTime
    /// อ่านได้ 0 หมด เซิร์ฟจึงร่นเวลาให้จริงแต่จอโชว์ว่าไม่ร่น — ดูรายงานท้ายงาน (แก้ที่ Cheats.cs)
    /// เซิร์ฟอ่านค่าจาก performance.json ตรง ๆ จึงไม่ได้รับผลกระทบ
    /// </summary>
    private void HandleFeedInCageMsg(FeedInCage msg, uint seq)
    {
        GrowCage? found = _world.ArtifactManager.GetGrowCage(msg.EntityId);
        if (!found.HasValue)
        {
            Send(new Abort { Text = "สิ่งปลูกสร้างนี้ไม่ใช่โรงเลี้ยงสัตว์" }, seq);
            return;
        }
        GrowCage cage = found.Value;

        Messages.Pet? inCage = FindCagePet(cage, msg.PetId);
        if (!inCage.HasValue)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้ในโรงเลี้ยง" }, seq);
            return;
        }
        if (inCage.Value.TamerEntityId != EntityId)
        {
            Send(new Abort { Text = "สัตว์ตัวนี้เป็นของคนอื่น" }, seq);
            return;
        }
        PetStore.Entry entry = CagePetEntry(inCage.Value);

        // ── กินไอเทมจากกระเป๋าผู้เล่น ──
        // เงื่อนไข "กินได้ไหม" ใช้เกณฑ์เดียวกับ Feeding นอกกรง (Player.Animals.cs:805-812):
        // อยู่ในตาราง pet_food แล้วได้ค่าอิ่ม > 0 เท่านั้น ของที่ไม่ผ่านจะ **ไม่ถูกลบ** ทิ้ง
        float gained = 0f;
        double tRatio = 0.0;
        double tTime = 0.0;
        var eaten = new List<Item>();                   // เก็บทั้งก้อน ไม่ใช่แค่ id — เผื่อต้องคืนของ
        foreach (string id in msg.ItemIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(id)) continue;
            int idx = _context.InventoryItems.FindIndex(it => it.Id == id);
            if (idx < 0) continue;
            Item item = _context.InventoryItems[idx];
            float vigor = PetTables.FoodVigor(item.Prototype, item.Level);
            if (vigor <= 0f) continue;
            CageTables.FoodInfo food = CageTables.FoodOf(item.Prototype, item.Level);
            _context.InventoryItems.RemoveAt(idx);
            eaten.Add(item);
            gained += vigor;
            if (food != null)
            {
                tRatio += food.DecreaseGrowTimeRatio;
                tTime += food.DecreaseGrowTime;
            }
        }
        if (eaten.Count == 0)
        {
            Send(new Abort { Text = "ไอเทมนี้ให้สัตว์กินไม่ได้" }, seq);
            return;
        }

        EnsureHungryBounds(entry);
        FillHungry(entry, gained);
        EnsureProductQuantity(entry);

        // ── ร่นเวลางาน (ถ้ากำลังทำอยู่) ──
        Messages.TaskStatus? running = TaskOf(cage, msg.PetId);
        Messages.TaskStatus? shortened = null;
        if (running.HasValue && (tRatio > 0.0 || tTime > 0.0))
        {
            CageTables.TaskInfo def = CageTables.TaskOf(running.Value.TaskId);
            if (def != null)
            {
                double until = StatFormula.EvalOr(CageTables.TaskTimeFormula, new Dictionary<string, double>
                {
                    ["since"] = running.Value.Since,
                    ["until"] = running.Value.Until,
                    ["total_time"] = def.Duration,
                    ["t_ratio"] = tRatio,
                    ["t_time"] = tTime
                }, running.Value.Until);
                Messages.TaskStatus next = running.Value;
                next.Until = until;
                shortened = next;
            }
        }

        Messages.Pet snapshot = entry.Pet;
        bool ok = WriteCage(msg.EntityId, c =>
        {
            c = ReplaceCagePet(c, snapshot);
            if (shortened.HasValue)
            {
                c.Tasks ??= new Dictionary<string, Messages.TaskStatus>();
                c.Tasks[msg.PetId] = shortened.Value;
            }
            return c;
        });
        if (!ok)
        {
            // กรงหายไประหว่างทาง — **ต้องคืนอาหารที่ลบไปแล้ว** ไม่งั้นของหายทั้งที่ไม่ได้ให้สัตว์กิน
            _context.InventoryItems.AddRange(eaten);
            FillHungry(entry, -gained);
            Send(new Abort { Text = "โรงเลี้ยงหลังนี้หายไปแล้ว" }, seq);
            return;
        }

        Send(default(OK), seq);
        Send(new InventoryUpdated { EntityId = EntityId, RemovedItemIds = eaten.Select(it => it.Id).ToArray() });
        OnContextChanged();
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  งานของสัตว์ — เริ่ม / ยกเลิก / เก็บผล
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// สั่งสัตว์เริ่มทำงาน — StartPetTask (65102) · client/PetManager.cs:1037-1052 ใช้ .All(IsSuccess)
    ///
    /// ฝั่งเกมเช็คความอิ่มให้ก่อนยิงแล้ว (GrowCageGroup.cs:275-279 เทียบ
    /// <c>Stat.Hungry.Get() &lt; petTask.HungryRequired</c>) ⇒ เซิร์ฟเช็คซ้ำแล้ว**หักออกจริง**
    /// การหักตอนเริ่มยืนยันได้จากข้อความเตือนของเกมเองตอนกดหยุดงาน (GrowCageGroup.cs:307-314):
    /// "เวลาและอาหารที่ใช้ไปทั้งหมดจะหายไป" ⇒ แปลว่าค่าอิ่มถูกใช้ไปแล้วตั้งแต่เริ่ม
    /// </summary>
    private void HandleStartPetTaskMsg(StartPetTask msg, uint seq)
    {
        GrowCage? found = _world.ArtifactManager.GetGrowCage(msg.EntityId);
        if (!found.HasValue)
        {
            Send(new Abort { Text = "สิ่งปลูกสร้างนี้ไม่ใช่โรงเลี้ยงสัตว์" }, seq);
            return;
        }
        GrowCage cage = found.Value;

        Messages.Pet? inCage = FindCagePet(cage, msg.PetId);
        if (!inCage.HasValue)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้ในโรงเลี้ยง" }, seq);
            return;
        }
        if (inCage.Value.TamerEntityId != EntityId)
        {
            Send(new Abort { Text = "สัตว์ตัวนี้เป็นของคนอื่น" }, seq);
            return;
        }
        PetStore.Entry entry = CagePetEntry(inCage.Value);
        if (TaskOf(cage, msg.PetId).HasValue)
        {
            Send(new Abort { Text = "สัตว์ตัวนี้มีงานค้างอยู่" }, seq);
            return;
        }
        if (!PetIsAlive(entry))
        {
            Send(new Abort { Text = "สัตว์ตัวนี้ตายอยู่ ต้องชุบชีวิตก่อน" }, seq);
            return;
        }

        CageTables.TaskInfo def = CageTables.TaskOf(msg.TaskId);
        if (def == null)
        {
            Send(new Abort { Text = "ไม่รู้จักงานนี้" }, seq);
            return;
        }
        if (entry.Pet.Statistics.Level < def.UnlockLevel)
        {
            Send(new Abort { Text = $"ต้องเลเวล {def.UnlockLevel} ขึ้นไป" }, seq);
            return;
        }
        // งานผลิตผูกกับชนิดผลผลิตของสัตว์ (by_product) — งานฝึก (ByProduct = null) ทำได้ทุกตัว
        string byProduct = CageTables.ByProductOf(entry.Pet.EntityType);
        if (def.ByProduct != null && def.ByProduct != byProduct)
        {
            Send(new Abort { Text = "สัตว์ชนิดนี้ทำงานนี้ไม่ได้" }, seq);
            return;
        }

        EnsureHungryBounds(entry);
        double now = Times.UnixTimeNow();
        float hungry = entry.Pet.Stat.Hungry?.Get(now) ?? 0f;
        if (hungry < def.HungryRequired)
        {
            Send(new Abort { Text = $"ความอิ่มไม่พอ (ต้องการ {def.HungryRequired:0} มี {hungry:0})" }, seq);
            return;
        }

        FillHungry(entry, -def.HungryRequired);
        EnsureProductQuantity(entry);

        Messages.Pet snapshot = entry.Pet;
        var status = new Messages.TaskStatus
        {
            TaskId = def.Id,
            Since = now,
            Until = now + def.Duration
        };
        bool ok = WriteCage(msg.EntityId, c =>
        {
            c = ReplaceCagePet(c, snapshot);
            c.Tasks ??= new Dictionary<string, Messages.TaskStatus>();
            c.Tasks[msg.PetId] = status;
            return c;
        });
        if (!ok)
        {
            FillHungry(entry, def.HungryRequired);           // ย้อนค่าอิ่มคืน — งานไม่ได้เริ่มจริง
            Send(new Abort { Text = "โรงเลี้ยงหลังนี้หายไปแล้ว" }, seq);
            return;
        }

        Console.WriteLine($"[กรง] {ShortId()} สั่ง {entry.Pet.Name} ทำงาน {def.Id} " +
                          $"({def.Duration / 3600.0:0.#} ชม. · ใช้ความอิ่ม {def.HungryRequired:0})");
        Send(default(OK), seq);
    }

    /// <summary>
    /// ยกเลิกงาน — CancelPetTask (65103) · client/PetManager.cs:1054-1068 ใช้ .All(IsSuccess)
    ///
    /// **ไม่คืนอะไรทั้งสิ้น** — ไม่ใช่ความขี้เกียจ แต่เป็นสิ่งที่กล่องยืนยันของเกมบอกผู้เล่นไว้ตรง ๆ
    /// ก่อนกด (client/Durango.UI/GrowCageGroup.cs:307-314): "รับผลผลิตและ exp ไม่ได้
    /// และเวลากับอาหารที่ใช้ไปทั้งหมดจะหายไป"
    /// </summary>
    private void HandleCancelPetTaskMsg(CancelPetTask msg, uint seq)
    {
        GrowCage? found = _world.ArtifactManager.GetGrowCage(msg.EntityId);
        if (!found.HasValue)
        {
            Send(new Abort { Text = "สิ่งปลูกสร้างนี้ไม่ใช่โรงเลี้ยงสัตว์" }, seq);
            return;
        }
        Messages.Pet? inCage = FindCagePet(found.Value, msg.PetId);
        if (!inCage.HasValue || inCage.Value.TamerEntityId != EntityId)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้ในโรงเลี้ยง" }, seq);
            return;
        }
        if (!TaskOf(found.Value, msg.PetId).HasValue)
        {
            Send(new Abort { Text = "สัตว์ตัวนี้ไม่ได้ทำงานอยู่" }, seq);
            return;
        }

        bool ok = WriteCage(msg.EntityId, c =>
        {
            c.Tasks?.Remove(msg.PetId);
            return c;
        });
        if (!ok)
        {
            Send(new Abort { Text = "โรงเลี้ยงหลังนี้หายไปแล้ว" }, seq);
            return;
        }
        Console.WriteLine($"[กรง] {ShortId()} ยกเลิกงานของ {inCage.Value.Name}");
        Send(default(OK), seq);
    }

    /// <summary>
    /// เก็บผลงาน — FinishPetTask (65104) · client/PetManager.cs:1070-1084 ใช้ .All(IsSuccess)
    ///
    /// ลำดับที่ปลอดภัย: สร้างของก่อน → เช็คที่ว่างในกระเป๋า → ถ้าไม่พอ **ตอบ Abort โดยไม่ลบงานทิ้ง**
    /// (กฎข้อ 2 ของโปรเจกต์: ตอบ OK ทั้งที่ของไม่เข้ากระเป๋า = ผลผลิตทั้งกะหายไปเฉย ๆ)
    ///
    /// สิ่งที่ส่งกลับไปสามทาง:
    ///   OK(seq)          → ปลดล็อกปุ่มบนหน้าจอ (GrowCageGroup.cs:337-344 _waitRequest)
    ///   InventoryUpdated → ของเข้ากระเป๋าจริง
    ///   Rewarded(2065) + PetTaskFinishedEffect(2087) → หน้าต่างสรุปรางวัลของเกม
    ///                    (client/Durango.UI/AlarmGroup.cs:329-332 → ReceiveRewardsPopup:442)
    /// ส่วนหน้าจอกรงเองรีเฟรชจาก ArtifactState ที่ WriteCage ผลักออกไป (รวมข้อความ "เลเวลอัป"
    /// ที่ GrowCageGroup.cs:137-153 คิดเองจากส่วนต่างของเลเวลก่อน/หลัง)
    /// </summary>
    private void HandleFinishPetTaskMsg(FinishPetTask msg, uint seq)
    {
        GrowCage? found = _world.ArtifactManager.GetGrowCage(msg.EntityId);
        if (!found.HasValue)
        {
            Send(new Abort { Text = "สิ่งปลูกสร้างนี้ไม่ใช่โรงเลี้ยงสัตว์" }, seq);
            return;
        }
        GrowCage cage = found.Value;

        Messages.Pet? inCage = FindCagePet(cage, msg.PetId);
        if (!inCage.HasValue || inCage.Value.TamerEntityId != EntityId)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้ในโรงเลี้ยง" }, seq);
            return;
        }
        Messages.TaskStatus? running = TaskOf(cage, msg.PetId);
        if (!running.HasValue)
        {
            Send(new Abort { Text = "สัตว์ตัวนี้ไม่ได้ทำงานอยู่" }, seq);
            return;
        }
        double now = Times.UnixTimeNow();
        if (now < running.Value.Until)
        {
            Send(new Abort { Text = $"งานยังไม่เสร็จ (เหลืออีก {running.Value.Until - now:0} วินาที)" }, seq);
            return;
        }
        CageTables.TaskInfo def = CageTables.TaskOf(running.Value.TaskId);
        if (def == null)
        {
            // ไฟล์ข้อมูลเปลี่ยนไประหว่างที่งานค้างอยู่ — ปล่อยงานทิ้งดีกว่าค้างถาวร แต่ต้องบอกผู้เล่น
            WriteCage(msg.EntityId, c => { c.Tasks?.Remove(msg.PetId); return c; });
            Send(new Abort { Text = "ข้อมูลงานนี้หายไปจากไฟล์เกม — ยกเลิกงานให้แล้ว" }, seq);
            return;
        }

        PetStore.Entry entry = CagePetEntry(inCage.Value);
        Messages.Pet pet = entry.Pet;

        // ── ผลผลิต ──
        List<Item> main = RollTaskProducts(def, pet, bonus: false);
        List<Item> extra = RollTaskProducts(def, pet, bonus: true);
        int need = main.Sum(it => Math.Max(1, it.Size)) + extra.Sum(it => Math.Max(1, it.Size));
        int free = PetTuning.PlayerInventoryMaxSize - _context.InventoryItems.Sum(it => Math.Max(1, it.Size));
        if (need > free)
        {
            // ไม่แตะ Tasks เลย ⇒ ผู้เล่นเก็บของแล้วกดใหม่ได้ ผลงานไม่หาย
            Send(new Abort { Text = $"กระเป๋าไม่พอ (ต้องการ {need} ว่าง {free}) — เก็บของแล้วลองใหม่" }, seq);
            return;
        }

        // ── exp + เลเวลอัป ──
        int gainedExp = def.Exp;
        GivePetExp(entry, gainedExp);
        EnsureProductQuantity(entry);
        pet = entry.Pet;

        Messages.Pet snapshot = pet;
        bool ok = WriteCage(msg.EntityId, c =>
        {
            c = ReplaceCagePet(c, snapshot);
            c.Tasks?.Remove(msg.PetId);
            return c;
        });
        if (!ok)
        {
            Send(new Abort { Text = "โรงเลี้ยงหลังนี้หายไปแล้ว" }, seq);
            return;
        }

        var all = new List<Item>(main);
        all.AddRange(extra);
        if (all.Count > 0)
        {
            AddItems(all);
        }

        Console.WriteLine($"[กรง] {ShortId()} เก็บผลงาน {def.Id} ของ {pet.Name} — " +
                          $"ของ {all.Count} ชิ้น · exp {gainedExp} · เลเวล {pet.Statistics.Level}");
        Send(default(OK), seq);
        if (all.Count > 0)
        {
            Send(new InventoryUpdated { EntityId = EntityId, Items = all.ToArray() });
        }
        Send(new Rewarded
        {
            Effect = new PetTaskFinishedEffect
            {
                Type = Shared.System.RewardEffect.PetTaskFinished,
                TaskId = def.Id,
                PetExp = gainedExp
            },
            Reward = new RewardInfo
            {
                Items = ToRewardItems(main),
                RandomItems = ToRewardItems(extra)
            }
        });
        OnContextChanged();
    }

    /// <summary>
    /// รายการงานที่สั่งสัตว์ตัวนี้ทำได้ — GetAvailableTask (65106) ตอบ AvailableTask (65107)
    ///
    /// client/Durango.UI.Popup/SelectPetTaskPopup.cs:104-120 เปิดหน้าต่างแล้วยิงตัวนี้ เก็บ
    /// <c>result.Value.Tasks</c> ไว้ก่อนวาดรายการ — ไม่ตอบ = รายการงานว่างค้างโดยไม่มี error
    /// (มี .Rest → onResult(null) รองรับ Abort แต่ก็จะไม่มีอะไรให้เลือกอยู่ดี)
    /// จากนั้น client กรองด้วย <c>task.Type == taskType</c> เอง (ปุ่มผลิต/ปุ่มฝึกคนละใบ)
    /// ⇒ เซิร์ฟส่งไปทั้งสองชนิด แล้วปล่อยให้ client แยก
    ///
    /// เกณฑ์กรองมาจากข้อมูลจริง: <c>pet_task.json → animal_by_product</c> ต้องตรงกับ
    /// <c>pets_for_client.json → by_product</c> ของสัตว์ตัวนั้น
    /// (ค่าทั้งสองฝั่งเป็นชุดเดียวกันเป๊ะ 7 ค่า: default · mammal · mammal_no_milk · mammal_honey ·
    ///  mammal_water · feather · iguana — งานผลิตมีชนิดละ 6 ระดับพอดี ส่วนงานฝึก 6 ตัวไม่มีคีย์นี้
    ///  = ทำได้ทุกชนิด) นี่เป็นหลักฐานที่หนักกว่า cage → tag_to_generator ซึ่งเหมือนกันหมดทั้ง 6 กรง
    ///  จึงแยกอะไรไม่ได้เลย
    ///
    /// **ไม่กรองด้วยเลเวล** — ฝั่งเกมตั้งใจโชว์งานที่ยังไม่ปลดล็อกเป็นปุ่มสีเทา "Lv.N ขึ้นไป"
    /// (SelectPetTaskItemWidget.cs:113-117) เพื่อให้ผู้เล่นเห็นเป้าหมาย · เลเวลถูกบังคับจริงที่
    /// <see cref="HandleStartPetTaskMsg"/> แทน
    /// </summary>
    private void HandleCageAvailableTaskMsg(GetAvailableTask msg, uint seq)
    {
        Messages.Pet? pet = null;
        GrowCage? cage = _world.ArtifactManager.GetGrowCage(msg.EntityId);
        if (cage.HasValue) pet = FindCagePet(cage.Value, msg.PetId);
        pet ??= PetStore.Find(EntityId, msg.PetId)?.Pet;

        string byProduct = pet.HasValue ? CageTables.ByProductOf(pet.Value.EntityType) : null;
        string[] tasks = CageTables.All
            .Where(t => t.ByProduct == null || t.ByProduct == byProduct)
            .OrderBy(t => (int)t.Type)
            .ThenBy(t => t.UnlockLevel)
            .Select(t => t.Id)
            .ToArray();
        Send(new AvailableTask { Tasks = tasks }, seq);
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ตัวช่วย — เขียนสถานะกรงแล้วผลักออกไปให้เห็นทั้งเกาะ
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// แก้สถานะโรงเลี้ยง แล้วผลัก ArtifactState ที่ **มี EntityId ถูกต้อง** ออกไปให้ทุกคน
    ///
    /// ⚠️ ทำไมต้องผลักเอง ทั้งที่ ArtifactManager.UpdateGrowCage ยิง ArtifactStateUpdated ให้แล้ว:
    /// <c>AppearArtifact.States.EntityId</c> **ไม่เคยถูกตั้งค่าตอนสร้างสิ่งปลูกสร้าง**
    /// (Core/Cheats.cs:226-231 MakeAppearArtifact ตั้งแค่ BuildingState กับ Durability ·
    ///  ยืนยันจากไฟล์เซฟจริง AppData-nx/offline/main/regions/*.world ทุกหลังมี States.EntityId = null)
    /// แต่ฝั่งเกมหาสิ่งปลูกสร้างจากฟิลด์นี้ตัวเดียว: client/ArtifactManager.cs:57-60
    /// <c>UpdateArtifactState(Find(msg.EntityId), …)</c> — Find(null) คืน null แล้วทิ้งแพ็กเก็ตเงียบ ๆ
    /// ⇒ ก้อนที่ event ยิงออกไปเองไม่มีผลอะไรเลย (ไม่พัง แต่ก็ไม่ถึงจอ) ต้องส่งก้อนที่ซ่อมแล้วตามไป
    ///
    /// วิธีแก้ที่ถูกต้องคือเติมหนึ่งบรรทัดใน ArtifactManager.UpdateGrowCage/UpdateDomesticCage
    /// (<c>artifact.States.EntityId = artifact.EntityId;</c> แบบเดียวกับ Scribble:170 / OpenGate:185)
    /// แต่ไฟล์นั้นอยู่นอกขอบเขตงานนี้ — ดูรายงานท้ายงาน
    /// </summary>
    private bool WriteCage(string entityId, Func<GrowCage, GrowCage> mutate)
    {
        if (!_world.ArtifactManager.UpdateGrowCage(entityId, mutate)) return false;

        AppearArtifact? artifact = _world.ArtifactManager.Get(entityId);
        if (!artifact.HasValue) return true;
        ArtifactState states = artifact.Value.States;
        if (string.IsNullOrEmpty(states.EntityId)) states.EntityId = artifact.Value.EntityId;
        _world.BroadCast(states);
        return true;
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ตัวช่วย — อ่าน/แก้ก้อน GrowCage
    // ══════════════════════════════════════════════════════════════════════════════════

    private static List<Messages.Pet> CagePetList(GrowCage cage) =>
        cage.Pets.Data == null ? new List<Messages.Pet>() : new List<Messages.Pet>(cage.Pets.Data);

    private static Messages.Pet? FindCagePet(GrowCage cage, string petId)
    {
        if (string.IsNullOrEmpty(petId) || cage.Pets.Data == null) return null;
        foreach (Messages.Pet p in cage.Pets.Data)
        {
            if (p.EntityId == petId) return p;
        }
        return null;
    }

    /// <summary>เขียนทับก้อนสัตว์ตัวเดิมในกรงด้วยก้อนล่าสุด (ไม่เจอ = ไม่ทำอะไร)</summary>
    private static GrowCage ReplaceCagePet(GrowCage cage, Messages.Pet pet)
    {
        if (cage.Pets.Data == null) return cage;
        var data = (Messages.Pet[])cage.Pets.Data.Clone();
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i].EntityId != pet.EntityId) continue;
            data[i] = pet;
            cage.Pets = new Messages.Pets { Data = data };
            return cage;
        }
        return cage;
    }

    private static Messages.TaskStatus? TaskOf(GrowCage cage, string petId)
    {
        if (cage.Tasks == null || string.IsNullOrEmpty(petId)) return null;
        return cage.Tasks.TryGetValue(petId, out Messages.TaskStatus status) ? status : null;
    }

    /// <summary>สัตว์ตัวนี้อยู่ในโรงเลี้ยงอยู่ไหม — เกณฑ์เดียวกับฝั่งเกม (RegionId ต้องไม่ว่าง)</summary>
    private static bool IsInCage(Messages.Pet pet) =>
        pet.CageInfo.HasValue && !string.IsNullOrEmpty(pet.CageInfo.Value.RegionId);

    /// <summary>
    /// ป้ายบอกที่อยู่ของสัตว์ในกรง — ฝั่งเกมเอาไปโชว์ใต้ชื่อสัตว์ในรายการ
    /// (client/Durango.UI/PetListInfoNode.cs:54-57 โชว์ไอคอนหมุด + RegionName)
    /// RegionId ห้ามว่าง เพราะเป็นตัวชี้ขาดว่า "อยู่ในกรง" (ดู <see cref="IsInCage"/>)
    /// </summary>
    private CageInfo BuildCageInfo(string cageEntityId)
    {
        AppearArtifact? artifact = _world.ArtifactManager.Get(cageEntityId);
        // ชื่อเกาะ: สารบัญเกาะเก็บ Name = id ของเกาะอยู่แล้ว (Support/RegionCatalog.cs:187)
        // เซิร์ฟไม่มีตารางแปลภาษา จึงส่ง id ไปตรง ๆ — ดีกว่าเว้นว่างแล้วผู้เล่นไม่รู้ว่าฝากไว้ที่ไหน
        string name = RegionCatalog.TryGet(_world.TerrainId, out Messages.Region region) && !string.IsNullOrEmpty(region.Name)
            ? region.Name
            : _world.TerrainId;
        return new CageInfo
        {
            RegionId = _world.TerrainId,
            RegionName = name,
            Tile = artifact?.Tile ?? default
        };
    }

    /// <summary>ที่ที่สัตว์ตัวนี้กินในกรง — performance.json → reins → size (0 = ข้อมูลหาย จึงคิดเป็น 1)</summary>
    private static int PetSizeOf(Messages.Pet pet) => Math.Max(1, pet.Stat.Size);

    private static byte ClampToByte(int value, int max = 255) => (byte)Math.Clamp(value, 0, Math.Min(255, max));

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ตัวช่วย — ค่าสถานะของสัตว์
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// หา <c>PetStore.Entry</c> ของสัตว์ที่อยู่ในกรง — ไม่มีก็ "กู้คืน" จากก้อนที่อยู่ในกรง
    ///
    /// ⚠️ ทำไมต้องกู้: PetStore อยู่ในหน่วยความจำล้วน (Player.Animals.cs:1338-1344 เขียนไว้เอง
    /// ว่ายังไม่มีช่องเซฟใน PlayerContext) แต่ **สถานะกรงถูกเซฟลงไฟล์โลกจริง**
    /// (World.ArtifactManager_ArtifactStateUpdated → Save) ⇒ รีสตาร์ตเซิร์ฟแล้วสัตว์ยังอยู่ในกรง
    /// แต่หายจากสโตร์ · ถ้าไม่กู้คืน ปุ่ม "เอาออก" จะลบสัตว์ทิ้งถาวรโดยผู้เล่นไม่รู้ตัว
    /// และให้อาหาร/สั่งงานก็จะเด้ง Abort ตลอดไป
    ///
    /// ค่าที่ประกอบใหม่เอามาจากตัวสัตว์เองล้วน ๆ (Statistics.DerivedAbilities ที่เซฟไปกับกรง)
    /// บวกอัตราหิวจาก performance.json → reins ⇒ ไม่มีตัวเลขไหนเดา
    /// </summary>
    private PetStore.Entry CagePetEntry(Messages.Pet inCage)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, inCage.EntityId);
        if (entry != null) return entry;

        entry = new PetStore.Entry { Pet = inCage };
        entry.LifeMax = inCage.Statistics.DerivedAbilities != null &&
                        inCage.Statistics.DerivedAbilities.TryGetValue(Derived.LifeMax, out float lm) && lm > 0f
            ? lm
            : inCage.Stat.Life?.Max(Times.UnixTimeNow()) ?? 0f;
        EnsureHungryBounds(entry);
        PetStore.Add(EntityId, entry);
        Console.WriteLine($"[กรง] กู้ {inCage.Name} กลับเข้ารายการสัตว์ของ {ShortId()} (สโตร์ว่างหลังรีสตาร์ต)");
        return entry;
    }

    /// <summary>
    /// ซ่อมเพดาน/อัตราหิวใน PetStore.Entry ถ้ายังว่าง
    ///
    /// <c>Entry.HungryMax</c> / <c>HungryVelocity</c> ถูกตั้งตอน "สร้างสัตว์" ซึ่งอยู่ในระบบทำให้เชื่อง
    /// (คนละไฟล์ ทำโดย agent อีกตัว) ⇒ ถ้ายังว่างอยู่ <see cref="FillHungry"/> จะหนีบหลอดเป็น 0
    /// แล้วสัตว์หิวตลอดกาลจนสั่งงานไม่ได้เลย — ซ่อมจากข้อมูลจริงที่ติดมากับตัวสัตว์แทน
    /// </summary>
    private static void EnsureHungryBounds(PetStore.Entry entry)
    {
        if (entry.HungryMax <= 0f)
        {
            // ค่าเดียวกับที่ PetFactory ใช้ตอนสร้าง (performance.json → reins → hungry_max)
            entry.HungryMax = entry.Pet.Statistics.DerivedAbilities != null &&
                              entry.Pet.Statistics.DerivedAbilities.TryGetValue(Derived.HungryMax, out float hm)
                ? hm
                : entry.Pet.Stat.Hungry?.Max(Times.UnixTimeNow()) ?? 0f;
        }
        if (entry.HungryVelocity == 0f)
        {
            PetTables.ReinPerf perf = PetTables.PerfOf(entry.Pet.EntityType, entry.Pet.Statistics.Level);
            entry.HungryVelocity = perf?.HungryVelocity ?? 0f;
        }
    }

    /// <summary>
    /// เติมช่อง Derived.AnimalProductQuantity (307) ให้สัตว์ ถ้ายังไม่มี
    ///
    /// ฝั่งเกมอ่านช่องนี้ไปโชว์ "ผลิตได้กี่ชิ้น" ในหน้าต่างเลือกงาน
    /// (client/Durango.UI.Popup/SelectPetTaskItemWidget.cs:89,104) ⇒ ไม่มี = โชว์ 0 ทั้งที่ผลิตได้จริง
    ///
    /// ⚠️ <c>PetFactory.DerivedOf</c> (Player.Animals.cs) ยังไม่ได้คิดช่องนี้ และ
    /// <c>MapModifierToDerived</c> แม็ป product_quantity_* เป็น Derived.Invalid ⇒ แท็กสองตัวนี้
    /// ไม่มีผลกับตัวเลขบนจอ · ที่นี่จึงคิดเองจากแท็กจริงใน tags.json แล้วเขียนกลับ
    /// (RecalcPetStats จะล้างช่องนี้ทิ้งทุกครั้งที่เลเวลอัป ⇒ ต้องเรียกซ้ำหลังทุกการเปลี่ยนแปลง)
    /// วิธีแก้ถาวรอยู่ในรายงานท้ายงาน (ต้องแก้ Player.Animals.cs ซึ่งอยู่นอกขอบเขต)
    /// </summary>
    private static void EnsureProductQuantity(PetStore.Entry entry)
    {
        entry.Pet.Statistics.DerivedAbilities ??= new Dictionary<Derived, float>();
        entry.Pet.Statistics.DerivedAbilities[Derived.AnimalProductQuantity] = ProductQuantityOf(entry.Pet);
    }

    /// <summary>
    /// ค่า animal_product_quantity ของสัตว์ตัวนี้ = (ฐาน + แท็กบวก) × (1 + แท็กคูณ)
    /// ฐานเป็น **ค่าของเรา** (ดู <see cref="CageTuning.AnimalProductQuantityBase"/>)
    /// ส่วนสูตรของแท็กมาจาก tags.json ตรง ๆ ผ่าน PetTables.MilestoneTagOf(...).Amount(ระดับแท็ก)
    /// </summary>
    private static float ProductQuantityOf(Messages.Pet pet)
    {
        float plus = 0f;
        float ratio = 0f;
        if (pet.Stat.Tags != null)
        {
            foreach (KeyValuePair<string, int> tag in pet.Stat.Tags)
            {
                PetTables.MilestoneTag def = PetTables.MilestoneTagOf(tag.Key);
                if (def == null) continue;
                if (tag.Key.StartsWith("product_quantity_amplifier", StringComparison.Ordinal)) ratio += def.Amount(tag.Value);
                else if (tag.Key.StartsWith("product_quantity_plus", StringComparison.Ordinal)) plus += def.Amount(tag.Value);
            }
        }
        return Math.Max(0f, (CageTuning.AnimalProductQuantityBase + plus) * (1f + ratio));
    }

    /// <summary>
    /// ให้ exp สัตว์แล้วเลื่อนเลเวลเท่าที่พอ — เกณฑ์ต่อเลเวลจาก pet_exp.json (PetTables.RequiredExp)
    ///
    /// ฝั่งเกมไม่ได้คิดเลเวลเอง มันอ่าน <c>Statistics.Level/Exp/RequiredExp</c> ที่เซิร์ฟส่งไปตรง ๆ
    /// (หน้าจอกรงเทียบเลเวลก่อน/หลังเพื่อขึ้นข้อความ "เลเวลอัป" — GrowCageGroup.cs:137-153)
    /// ⇒ ต้องอัปเดตครบทั้งสามฟิลด์ ไม่งั้นหลอด exp บนจอเพี้ยน
    /// </summary>
    private static void GivePetExp(PetStore.Entry entry, int amount)
    {
        if (amount <= 0) return;
        entry.Pet.Statistics.Exp += amount;
        for (int guard = 0; guard < CageTuning.MaxPetLevel; guard++)
        {
            int need = entry.Pet.Statistics.RequiredExp;
            if (need <= 0 || entry.Pet.Statistics.Exp < need) break;
            if (entry.Pet.Statistics.Level >= CageTuning.MaxPetLevel) break;
            entry.Pet.Statistics.Exp -= need;
            entry.Pet.Statistics.Level++;
            RecalcPetStats(entry);                 // คิด Derived + RequiredExp ของเลเวลใหม่
        }
        if (entry.Pet.Statistics.Level >= CageTuning.MaxPetLevel && entry.Pet.Statistics.RequiredExp > 0)
        {
            // ตันเลเวลแล้ว — หนีบ exp ไว้ที่ขีดสุดท้าย ไม่ให้หลอดบนจอล้นออกนอกกรอบ
            entry.Pet.Statistics.Exp = Math.Min(entry.Pet.Statistics.Exp, entry.Pet.Statistics.RequiredExp);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ตัวช่วย — ผลผลิตของงาน
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// สุ่มของที่ได้จากงานหนึ่งครั้ง
    ///
    /// **การตีความของเรา** (ไฟล์ข้อมูลไม่มีคำอธิบาย ฝั่งเกมก็ใช้แค่คีย์ไปวาดไอคอน — ไม่ได้อ่านตัวเลข
    /// คู่ที่สอง: client/Durango.UI.Popup/SelectPetTaskItemWidget.cs:143-160):
    ///   <c>produced_prototype = { proto: [น้ำหนัก, จำนวน] }</c> น้ำหนักรวมเป็น 100 ทุกงาน
    ///     ⇒ **สุ่มเลือกมาหนึ่งอย่าง** ตามน้ำหนัก (ไม่ใช่ได้ทุกอย่างพร้อมกัน) ซึ่งตรงกับป้ายบนจอ
    ///       ที่เขียนว่า "생산 가능" = *ผลิตได้* (รายการความเป็นไปได้ ไม่ใช่รายการที่ได้แน่)
    ///   จำนวน = -1 ⇒ ใช้สูตร <c>product_quantity</c> · เป็นเลขบวก ⇒ ใช้เลขนั้นตรง ๆ
    ///     (รูปแบบ [อัตรา, จำนวน] เป็นแบบเดียวกับ item/bonus_prototypes.json ที่เขียนชัดเป็น
    ///      {"rate": …, "prototypes": [{"count": …}]} ⇒ ตัวหลังคือ "จำนวน")
    ///   <c>random_prototype</c> = ของแถม สุ่ม <c>random_prototype_count</c> ครั้ง
    ///     แต่ละครั้งติดด้วยโอกาส <c>random_prototype_rate</c> แล้วค่อยสุ่มชนิดตามน้ำหนัก
    ///     (น้ำหนักกลุ่มนี้รวมกันไม่ถึง 100 ⇒ ไม่ใช่เปอร์เซ็นต์ในตัวเอง ต้องมี rate คุมอีกชั้น)
    ///
    /// เลเวลของไอเทม = สูตร <c>product_level</c> ในไฟล์ (ของจริงคือ "level" = เลเวลสัตว์)
    /// </summary>
    private static List<Item> RollTaskProducts(CageTables.TaskInfo task, Messages.Pet pet, bool bonus)
    {
        var made = new List<Item>();
        Dictionary<string, float[]> table = bonus ? task.Random : task.Produced;
        if (table == null || table.Count == 0) return made;

        int petLevel = Math.Max(1, pet.Statistics.Level);
        int itemLevel = (int)Math.Max(1.0, Math.Round(StatFormula.EvalOr(task.ProductLevelExpr,
            new Dictionary<string, double> { ["level"] = petLevel }, petLevel)));

        float quantitySource = pet.Statistics.DerivedAbilities != null &&
                               pet.Statistics.DerivedAbilities.TryGetValue(Derived.AnimalProductQuantity, out float q) && q > 0f
            ? q
            : ProductQuantityOf(pet);
        int quantity = (int)Math.Max(0.0, StatFormula.EvalOr(task.ProductQuantityExpr,
            new Dictionary<string, double> { ["animal_product_quantity"] = quantitySource }, 0.0));

        var protos = new List<string>(table.Keys);
        var weights = new List<int>(protos.Count);
        foreach (string proto in protos)
        {
            float[] pair = table[proto];
            weights.Add(pair != null && pair.Length > 0 ? (int)Math.Round(pair[0]) : 0);
        }

        int draws = bonus ? Math.Max(0, task.RandomCount) : 1;
        for (int i = 0; i < draws; i++)
        {
            if (bonus && PetTuning.Rng.NextDouble() >= task.RandomRate) continue;
            int pick = PetTuning.PickWeighted(weights);
            if (pick < 0) continue;
            float[] pair = table[protos[pick]];
            int count = pair != null && pair.Length > 1 && pair[1] > 0f ? (int)pair[1] : quantity;
            for (int n = 0; n < count; n++)
            {
                Item? item = Cheats.MakeItem(protos[pick], itemLevel);
                if (item.HasValue) made.Add(item.Value);
            }
        }
        return made;
    }

    /// <summary>ยุบไอเทมเป็นรายการรางวัลสำหรับหน้าต่างสรุป (ชนิด+เลเวลเดียวกันนับรวมกัน)</summary>
    private static RewardItem[] ToRewardItems(List<Item> items)
    {
        if (items == null || items.Count == 0) return Array.Empty<RewardItem>();
        return items
            .GroupBy(it => (it.Prototype, it.Level))
            .Select(g => new RewardItem
            {
                PrototypeId = g.Key.Prototype,
                Level = g.Key.Level,
                Count = g.Count()
            })
            .ToArray();
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ตารางข้อมูลของระบบโรงเลี้ยง (โหลดครั้งเดียวตอนใช้ครั้งแรก)
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// อ่าน pet_task.json / pets_for_client.json / performance.json ส่วนที่ระบบกรงต้องใช้
    ///
    /// ทำไมไม่ใช้ <c>PetTables</c> ที่มีอยู่แล้วใน Player.Animals.cs: <c>PetTables.TaskDef</c>
    /// ประกาศไว้แค่ 5 ฟิลด์ (unlock_level / duration / exp / hungry_required / type) ขาด
    /// animal_by_product · produced_prototype · random_prototype · product_quantity · product_level
    /// ซึ่งเป็นหัวใจของระบบผลิต และ <c>PetDef</c> ก็ไม่มี by_product · ไฟล์นั้นห้ามแก้
    /// ⇒ อ่านไฟล์เดิมซ้ำเป็นของตัวเอง (ไฟล์ data อ่านครั้งเดียวแคชไว้ ไม่ได้แพงอะไร)
    /// </summary>
    private static class CageTables
    {
        /// <summary>งานหนึ่งอันใน pet_task.json</summary>
        public class TaskInfo
        {
            public string Id;
            public PetTaskType Type;

            /// <summary>ชนิดผลผลิตที่ต้องตรงกับ by_product ของสัตว์ — null = งานฝึก ทำได้ทุกชนิด</summary>
            public string ByProduct;

            public int UnlockLevel;
            public double Duration;
            public int Exp;
            public float HungryRequired;
            public string ProductQuantityExpr;
            public string ProductLevelExpr;
            public Dictionary<string, float[]> Produced;
            public Dictionary<string, float[]> Random;
            public int RandomCount;
            public double RandomRate;
        }

        /// <summary>ผลของอาหารหนึ่งชิ้นต่อ "เวลางาน" (performance.json → pet_food)</summary>
        public class FoodInfo
        {
            public double DecreaseGrowTime;
            public double DecreaseGrowTimeRatio;
        }

        // ── รูปไฟล์ดิบ (ประกาศเฉพาะคีย์ที่ใช้ — Newtonsoft ข้ามที่เหลือให้เอง) ────────────
        // ใช้ property ไม่ใช่ field เพื่อไม่ให้ติด warning CS0649 ("ไม่เคยถูกกำหนดค่า")
        private class TaskFileEntry
        {
            [JsonProperty("type")] public int Type { get; set; }
            [JsonProperty("animal_by_product")] public string ByProduct { get; set; }
            [JsonProperty("unlock_level")] public int UnlockLevel { get; set; }
            [JsonProperty("duration")] public double Duration { get; set; }
            [JsonProperty("exp")] public int Exp { get; set; }
            [JsonProperty("hungry_required")] public float HungryRequired { get; set; }
            [JsonProperty("product_quantity")] public string ProductQuantity { get; set; }
            [JsonProperty("product_level")] public string ProductLevel { get; set; }
            [JsonProperty("produced_prototype")] public Dictionary<string, float[]> Produced { get; set; }
            [JsonProperty("random_prototype")] public Dictionary<string, float[]> Random { get; set; }
            [JsonProperty("random_prototype_count")] public int RandomCount { get; set; }
            [JsonProperty("random_prototype_rate")] public double RandomRate { get; set; }
        }

        private class PetFileEntry
        {
            [JsonProperty("by_product")] public string ByProduct { get; set; }
        }

        private class FoodFileEntry
        {
            [JsonProperty("decrease_grow_time")] public double DecreaseGrowTime { get; set; }
            [JsonProperty("decrease_grow_time_ratio")] public double DecreaseGrowTimeRatio { get; set; }
        }

        private class PerfFileRoot
        {
            [JsonProperty("pet_food")] public Dictionary<string, Dictionary<string, FoodFileEntry>> PetFood { get; set; }
        }

        private class ConstantsFileRoot
        {
            [JsonProperty("pet")] public ConstantsPetNode Pet { get; set; }
        }

        private class ConstantsPetNode
        {
            [JsonProperty("task_time")] public string TaskTime { get; set; }
        }

        private static Dictionary<string, TaskInfo> _tasks;
        private static Dictionary<int, string> _byProduct;
        private static Dictionary<string, Dictionary<string, FoodFileEntry>> _food;
        private static string _taskTime;

        private static Dictionary<string, TaskInfo> Tasks
        {
            get
            {
                if (_tasks != null) return _tasks;
                _tasks = new Dictionary<string, TaskInfo>(StringComparer.Ordinal);
                var file = Json.ReadFromFile<Dictionary<string, TaskFileEntry>>("pet/pet_task");
                if (file == null)
                {
                    Console.WriteLine("[กรง] ⚠️ อ่าน pet/pet_task.json ไม่ได้ — สั่งงานสัตว์ไม่ได้");
                    return _tasks;
                }
                foreach (KeyValuePair<string, TaskFileEntry> pair in file)
                {
                    TaskFileEntry raw = pair.Value;
                    if (raw == null) continue;
                    _tasks[pair.Key] = new TaskInfo
                    {
                        Id = pair.Key,
                        // type ในไฟล์: 0 = Production · 1 = Training (Shared.Animal/PetTaskType.cs)
                        Type = raw.Type == (int)PetTaskType.Training ? PetTaskType.Training : PetTaskType.Production,
                        ByProduct = string.IsNullOrEmpty(raw.ByProduct) ? null : raw.ByProduct,
                        UnlockLevel = raw.UnlockLevel,
                        Duration = raw.Duration,
                        Exp = raw.Exp,
                        HungryRequired = raw.HungryRequired,
                        ProductQuantityExpr = raw.ProductQuantity,
                        ProductLevelExpr = raw.ProductLevel,
                        Produced = raw.Produced,
                        Random = raw.Random,
                        RandomCount = raw.RandomCount,
                        RandomRate = raw.RandomRate
                    };
                }
                Console.WriteLine($"[กรง] โหลดงานสัตว์ {_tasks.Count} อัน");
                return _tasks;
            }
        }

        public static IEnumerable<TaskInfo> All => Tasks.Values;

        [CanBeNull]
        public static TaskInfo TaskOf(string taskId) =>
            string.IsNullOrEmpty(taskId) ? null : Tasks.GetValueOrDefault(taskId);

        /// <summary>ชนิดผลผลิตของสัตว์ — pets_for_client.json → &lt;pet_entity_type&gt; → by_product</summary>
        [CanBeNull]
        public static string ByProductOf(ushort petEntityType)
        {
            if (_byProduct == null)
            {
                _byProduct = new Dictionary<int, string>();
                var file = Json.ReadFromFile<Dictionary<int, PetFileEntry>>("pet/pets_for_client");
                if (file != null)
                {
                    foreach (KeyValuePair<int, PetFileEntry> pair in file)
                    {
                        if (!string.IsNullOrEmpty(pair.Value?.ByProduct)) _byProduct[pair.Key] = pair.Value.ByProduct;
                    }
                }
            }
            return _byProduct.GetValueOrDefault(petEntityType);
        }

        /// <summary>
        /// ผลของอาหารต่อเวลางาน — performance.json → pet_food → &lt;prototype&gt; → &lt;ช่วงเลเวล&gt;
        /// (คีย์ชั้นในเป็นข้อความ "[minLv, maxLv]" เหมือนส่วนอื่นของไฟล์นี้)
        /// </summary>
        [CanBeNull]
        public static FoodInfo FoodOf(string prototypeId, int level)
        {
            if (_food == null)
            {
                PerfFileRoot root = Json.ReadFromFile<PerfFileRoot>("performance");
                _food = root?.PetFood ?? new Dictionary<string, Dictionary<string, FoodFileEntry>>();
            }
            if (string.IsNullOrEmpty(prototypeId)) return null;
            if (!_food.TryGetValue(prototypeId, out Dictionary<string, FoodFileEntry> byRange)) return null;
            FoodFileEntry row = PickByLevelRange(byRange, level);
            if (row == null) return null;
            return new FoodInfo
            {
                DecreaseGrowTime = row.DecreaseGrowTime,
                DecreaseGrowTimeRatio = row.DecreaseGrowTimeRatio
            };
        }

        /// <summary>
        /// สูตรร่นเวลางานตอนป้อนอาหาร — constants.json → pet → task_time
        /// ของจริง: <c>max(since, until - (total_time * t_ratio + t_time))</c>
        /// ชื่อตัวแปร t_ratio/t_time มาจาก constants.json → pet → performance_reference
        /// (t_ratio ← decrease_grow_time_ratio · t_time ← decrease_grow_time)
        /// </summary>
        public static string TaskTimeFormula
        {
            get
            {
                if (_taskTime != null) return _taskTime;
                ConstantsFileRoot root = Json.ReadFromFile<ConstantsFileRoot>("constants");
                _taskTime = root?.Pet?.TaskTime ?? string.Empty;
                if (_taskTime.Length == 0) Console.WriteLine("[กรง] ⚠️ ไม่พบ constants.json → pet → task_time");
                return _taskTime;
            }
        }

        /// <summary>
        /// เลือกแถวตามช่วงเลเวลจากคีย์ข้อความ "[minLv, maxLv]"
        /// (มีตัวเดียวกันอยู่แล้วใน Player.Animals.cs แต่มันซ้อนอยู่ใน PetTables ซึ่งเป็น private
        ///  ของคลาสนั้น ⇒ C# เข้าถึงข้ามมาไม่ได้ จึงต้องมีของตัวเอง)
        /// </summary>
        [CanBeNull]
        private static T PickByLevelRange<T>(Dictionary<string, T> byRange, int level) where T : class
        {
            if (byRange == null || byRange.Count == 0) return null;
            T fallback = null;
            foreach (KeyValuePair<string, T> pair in byRange)
            {
                fallback ??= pair.Value;
                string[] parts = pair.Key.Trim('[', ']', ' ').Split(',');
                if (parts.Length != 2) continue;
                if (int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int min) &&
                    int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) &&
                    min <= level && level <= max)
                {
                    return pair.Value;
                }
            }
            return fallback;
        }
    }
}
