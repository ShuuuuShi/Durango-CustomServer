using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Durango.Network;
using Durango.Utils;
using JetBrains.Annotations;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Ability;
using Shared.Animal;

namespace Durango.Online;

/// <summary>
/// [5 ก.ย. 2026] ระบบ "ทำให้เชื่อง" — ทางเดียวในเกมที่จะได้สัตว์เลี้ยงตัวแรก
///
/// ═══ ลำดับที่ฝั่งเกมเดินจริง (ไล่จากซอร์ส ไม่ได้เดา) ═══
///   แตะกรง → Interaction.OpenDomesticCage(533) → client/Durango.UI/DomesticCageGroup.cs:63
///   Open(artifact) :100 **เปิดได้ก็ต่อเมื่อ ArtifactState.DomesticCage มีค่า** (Support/CageTypes.cs เติมให้แล้ว)
///
///   [+] ในรายการ  → OnAddRein :167   → PutInReinsToCage (694351)  → รอ IsSuccess
///   ปุ่ม 길들이기   → OnStartDomestication :218 → StartDomestication (694357) → รอ IsSuccess
///   ปุ่ม 먹이주기   → OnFeed :291      → PutItemsForDomestication (694352) **ยิงทิ้งไม่รอ reply**
///   ปุ่ม 취소       → OnStopDomestication :230 → CancelDomestication (694354) → รอ IsSuccess
///   ปุ่ม 결과 확인  → OnFinishDomestication :241 → FinishDomestication (694355)
///                     → **รอ DomesticationResult (694356) โดยเฉพาะ** (.Rest = ถือว่าล้มเหลว)
///   ปุ่ม 가방에 넣기 → OnTakeOutRein :275 → TakeOutReinFromCage (694359) → รอ IsSuccess
///   ปุ่ม 풀어주기   → OnReleaseRein :192 → ReleaseReinFromCage (694353) → **รอ OK เท่านั้น**
///
/// ⚠️ หน้าจอทั้งหมดนี้ refresh จาก <c>ArtifactState.DomesticCage</c> ตรง ๆ ไม่ได้ถามเป็น message
/// (DomesticCageGroup.OnArtifactStateChange :129 → MarkAsDirty → Refresh :137)
/// ⇒ **ทุกครั้งที่สถานะกรงเปลี่ยนต้องเรียก <c>_world.ArtifactManager.UpdateDomesticCage</c>**
/// ตัวนั้นยิง ArtifactStateUpdated ให้เอง (Core/Player.cs:89 ส่งต่อให้ client · Core/World.cs:234 เซฟโลก)
///
/// ═══ สูตรทั้งหมดมาจากไฟล์ข้อมูลจริง ไม่มีตัวไหนคิดเลขเอง ═══
///   data/assets/constants.json → pet
///     domesticate_probability = "min(max_prob, current_prob + d_success_rate)"
///     domesticate_time        = "max(starts_at + total_time * decrease_limit,
///                                    ends_at - (total_time * d_ratio + d_time))"
///     domesticate_decrease_limit = 0.5      (ลดเวลาได้มากสุดครึ่งหนึ่งของเวลาฐาน)
///     performance_reference      = ชื่อตัวแปรในสูตร → ชื่อ attribute บนไอเทม
///     advanced_tameable          = [2010,2047,2088,2025,2023,2020] (เลข vehicle_entity_type
///                                  ของสัตว์ 6 ชนิดที่ "ยาก" — ยืนยันแล้วว่ามีครบใน animal.json)
///   data/assets/performance.json → reins    pet_entity_type / vehicle_entity_type / size
///   data/assets/performance.json → pet_food decrease_domesticate_time_ratio /
///                                           decrease_domesticate_time /
///                                           increase_domesticate_success_rate
///   data/assets/pet/pets_for_client.json    rein_id / available_ranks / vehicle_entity_type
///
/// ⚠️ **สองค่าที่ไม่มีในไฟล์ข้อมูลเลย** (ค้นทั้ง /assets แล้ว — ของจริงอยู่บนเซิร์ฟของ NEXON):
/// "เวลาฐานในการทำให้เชื่อง" กับ "โอกาสสำเร็จตั้งต้น" ⇒ ตั้งเองใน <see cref="DomesticationTuning"/>
/// พร้อมเหตุผลกำกับทีละตัว
///
/// ═══ กลไก "คิดตอนถูกถาม" ═══
/// เซิร์ฟไม่มี timer เดินจริงสำหรับกรง — เก็บแค่ <c>DomesticateUntil</c> ไว้ แล้ว
/// <see cref="HandleFinishDomesticationMsg"/> ค่อยตรวจว่าเลยเวลาหรือยัง (แบบเดียวกับงานวิจัยใน
/// Core/Player.Skills.cs) ฝั่งเกมเดินแถบความคืบหน้าเองจาก DomesticateSince/Until/TotalTime
/// </summary>
public partial class Player
{
    // ══════════════════════════════════════════════════════════════════════════════════
    //  ค่าที่ "เราตั้งเอง" — ทุกตัวในบล็อกนี้ไม่มีในข้อมูลของ NEXON
    // ══════════════════════════════════════════════════════════════════════════════════

    private static class DomesticationTuning
    {
        /// <summary>
        /// **ค่าของเรา** — เวลาฐานในการทำให้เชื่อง (วินาที) ของสัตว์ทั่วไป
        ///
        /// ค้น /assets ทั้งชุดแล้วไม่มีเลขนี้ที่ไหนเลย (มีแต่ "ตัวลด" คือ pet_food →
        /// decrease_domesticate_time = 10/30/60 วินาที และ decrease_domesticate_time_ratio
        /// = 0.10-0.30 ของเวลาฐาน) ⇒ เวลาฐานต้องอยู่บนเซิร์ฟจริงของ NEXON
        ///
        /// เลือก 1800 วินาที (30 นาที) เพราะทำให้ "ตัวลดที่มีในไฟล์จริง" มีน้ำหนักพอดี:
        /// อาหารสัดส่วน 0.3 ตัวเดียว = ลด 9 นาที · อาหารแบบวินาทีคงที่ 60 วิ ต้องใช้หลายชิ้น
        /// ซึ่งตรงกับที่ UI ให้เลือกอาหารได้ไม่จำกัดจำนวน (SelectableCount = -1)
        /// </summary>
        public const double BaseSeconds = 1800.0;

        /// <summary>
        /// **ค่าของเรา** — เวลาฐานของสัตว์ในรายการ <c>constants.json → pet → advanced_tameable</c>
        /// (แองคีโลซอรัส / เสือเขี้ยวดาบ 2 ชนิด / กัลลิมิมัส / ยูทาห์แรปเตอร์ / ไดร์วูล์ฟ)
        ///
        /// รายชื่อ 6 ชนิดเป็นข้อมูลจริง แต่ไฟล์ไม่ได้บอกว่า "advanced" แล้วต่างยังไง
        /// ⇒ ตีความว่ายากกว่า จึงให้เวลา 3 เท่าและโอกาสสำเร็จตั้งต้นครึ่งเดียว
        /// </summary>
        public const double AdvancedSeconds = 5400.0;

        /// <summary>
        /// **ค่าของเรา** — โอกาสสำเร็จตั้งต้น (ยังไม่ป้อนอาหาร) ของสัตว์ทั่วไป
        ///
        /// ไม่มีในไฟล์เช่นกัน · ล้มเหลว = สัตว์หนีหายถาวร (ข้อความในเกมบอกไว้ชัด
        /// client/Durango.UI/DomesticCagePetListWidget.cs:135 "길들이기에 실패하면 동물을 잃게됩니다")
        /// ⇒ ตั้ง 0.5 ให้ "ป้อนอาหารแล้วคุ้ม" แต่ไม่ถึงกับต้องป้อนถึงจะมีหวัง
        /// (อาหารจริงเพิ่มทีละ 0.025-0.10 ⇒ ป้อนหลายชิ้นดันขึ้นไปใกล้ 1.0 ได้)
        /// </summary>
        public const float BaseSuccessRate = 0.5f;

        /// <summary>**ค่าของเรา** — โอกาสสำเร็จตั้งต้นของสัตว์ในรายการ advanced_tameable</summary>
        public const float AdvancedSuccessRate = 0.25f;

        /// <summary>
        /// **ค่าของเรา** — เพดานโอกาสสำเร็จ (ตัวแปร <c>max_prob</c> ในสูตรจริง)
        /// ไฟล์ไม่ได้บอกเพดาน ⇒ ให้ 1.0 คือป้อนอาหารมากพอแล้วสำเร็จแน่นอน
        /// (ถ้าตั้งต่ำกว่านี้ ผู้เล่นจะเสียบังเหียนแบบสุ่มทั้งที่ทำครบทุกอย่างแล้ว)
        /// </summary>
        public const float MaxSuccessRate = 1.0f;
    }

    // ── ทางเข้าให้ Core/Cheats.cs ใช้ค่าชุดเดียวกัน ────────────────────────────────────
    //
    // Cheats.MakeItem เป็นโรงงานไอเทมตัวเดียวของทั้งเซิร์ฟ (จับสัตว์ · คราฟต์ · ร้านค้า · cheat)
    // และต้องเติม Item.Ext = Reins ให้บังเหียนตั้งแต่ตอนสร้าง ⇒ ต้องรู้เวลา/โอกาสฐานด้วย
    // ถ้าไปก๊อบตัวเลขไปไว้อีกที่ ผู้เล่นจะเห็นเวลาทำให้เชื่องในหน้าต่างเลือกสัตว์ (ที่อ่านจาก
    // Reins.DomesticateDuration — client/Durango.UI/DomesticRatioWidget.cs:137-142)
    // ไม่ตรงกับเวลาที่นับจริงตอนกดเริ่ม ⇒ เปิดทางเข้ามาที่ตารางเดียวกันแทนการทำสำเนา

    /// <summary>เวลาฐานในการทำให้เชื่อง (วินาที) ของสัตว์ที่ใช้ vehicle_entity_type นี้</summary>
    internal static double BaseDomesticateSecondsOf(int vehicleEntityType) =>
        DomesticationTables.BaseSecondsOf(vehicleEntityType);

    /// <summary>โอกาสสำเร็จตั้งต้น (ยังไม่ป้อนอาหาร) ของสัตว์ที่ใช้ vehicle_entity_type นี้</summary>
    internal static float BaseDomesticateSuccessRateOf(int vehicleEntityType) =>
        DomesticationTables.BaseSuccessRateOf(vehicleEntityType);

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ลงทะเบียน handler — ไฟล์นี้ถูกเรียกหลัง RegisterAnimalHandlers()/RegisterInventoryHandlers()
    //  จึง "ทับ" ตัวที่ตอบ Abort ค้างไว้ได้เลย (Connection.Recv ลบของเดิมก่อนเสมอ —
    //  server/GameCode/Durango.Online/Connection.cs:198)
    // ══════════════════════════════════════════════════════════════════════════════════

    private void RegisterDomesticationHandlers()
    {
        // ซ่อมบังเหียนในกระเป๋าก่อนที่ SendInventory() จะส่งชุดแรกออกไป (Core/Player.cs:521-524)
        NormalizeReinItems();

        _connection.Recv(delegate(PutInReinsToCage msg, PacketHeader header)
        {
            HandlePutInReinsToCageMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(StartDomestication msg, PacketHeader header)
        {
            HandleStartDomesticationMsg(msg, header.Seq);
        });
        // CancelDomestication = 694354 (server/GameCode/Messages/CancelDomestication.cs:7)
        _connection.Recv(delegate(CancelDomestication msg, PacketHeader header)
        {
            HandleCancelDomesticationMsg(msg, header.Seq);
        });
        // ยิงทิ้งไม่รอ reply (client/PetManager.cs:1134-1143) ⇒ ตอบสำเร็จเงียบ ๆ ด้วย
        // InventoryUpdated + ArtifactState · ล้มเหลวค่อยส่ง Abort แบบไม่มี seq ให้ขึ้นข้อความระบบ
        _connection.Recv(delegate(PutItemsForDomestication msg, PacketHeader header)
        {
            HandlePutItemsForDomesticationMsg(msg);
        });
        _connection.Recv(delegate(FinishDomestication msg, PacketHeader header)
        {
            HandleFinishDomesticationMsg(msg, header.Seq);
        });
        // ⚠️ TakeOutReinFromCage ถูกจองไว้ตอบ Abort ถึงสองที่ (Core/Player.Inventory.cs:851
        // และ Core/Player.Animals.cs:393) — ตรงนี้ทับทั้งคู่
        _connection.Recv(delegate(TakeOutReinFromCage msg, PacketHeader header)
        {
            HandleTakeOutReinFromCageMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(ReleaseReinFromCage msg, PacketHeader header)
        {
            HandleReleaseReinFromCageMsg(msg, header.Seq);
        });
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ใส่บังเหียนเข้ากรง
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// PutInReinsToCage (694351) — ย้ายบังเหียนจากกระเป๋าเข้าไปเป็นสัตว์หนึ่งช่องในกรง
    ///
    /// ฝั่งเกมใช้ <c>.All(Packet.IsSuccess)</c> (client/PetManager.cs:1021-1034) ⇒ ตอบ OK
    /// เท่านั้นที่แปลว่าสำเร็จ · ตอบ OK ทั้งที่ไม่ได้ทำ = บังเหียนหายในสายตาผู้เล่น (กฎข้อ 2)
    ///
    /// ที่ในกรงคิดเป็น "ขนาด" ไม่ใช่ "จำนวนตัว" — ขนาดของสัตว์แต่ละชนิดมาจาก
    /// performance.json → reins → &lt;prototype&gt; → size (ฝั่งเกมเช็คเงื่อนไขเดียวกันตอนวาดปุ่ม:
    /// client/Durango.UI.Popup/PetItemInteractionPopup.cs:597-608 IsValidRein)
    /// </summary>
    private void HandlePutInReinsToCageMsg(PutInReinsToCage msg, uint seq)
    {
        DomesticCage? cageOpt = _world.ArtifactManager.GetDomesticCage(msg.EntityId);
        if (!cageOpt.HasValue)
        {
            Send(new Abort { Text = "ที่นี่ไม่ใช่กรงฝึกให้เชื่อง" }, seq);
            return;
        }
        int idx = _context.InventoryItems.FindIndex(it => it.Id == msg.ItemId);
        if (idx < 0)
        {
            Send(new Abort { Text = "ไม่พบบังเหียนในกระเป๋า" }, seq);
            return;
        }
        Item item = _context.InventoryItems[idx];
        DomesticationTables.ReinInfo rein = DomesticationTables.ReinOf(item.Prototype);
        if (rein == null)
        {
            Send(new Abort { Text = "ไอเทมชิ้นนี้ไม่ใช่บังเหียน" }, seq);
            return;
        }

        DomesticCage cage = cageOpt.Value;
        DomesticationInfo[] reins = cage.Reins ?? Array.Empty<DomesticationInfo>();
        if (Array.FindIndex(reins, r => r.ItemId == msg.ItemId) >= 0)
        {
            Send(new Abort { Text = "บังเหียนอันนี้อยู่ในกรงอยู่แล้ว" }, seq);
            return;
        }
        if (rein.Size > cage.RemainSize)
        {
            Send(new Abort { Text = "ที่ในกรงไม่พอสำหรับสัตว์ตัวนี้" }, seq);
            return;
        }

        DomesticationInfo info = NewDomesticationInfo(item, rein);
        bool ok = _world.ArtifactManager.UpdateDomesticCage(msg.EntityId, c =>
        {
            DomesticationInfo[] list = c.Reins ?? Array.Empty<DomesticationInfo>();
            c.Reins = list.Append(info).ToArray();
            c.RemainSize = (byte)Math.Max(0, c.RemainSize - rein.Size);
            return c;
        });
        if (!ok)
        {
            Send(new Abort { Text = "ใส่บังเหียนเข้ากรงไม่สำเร็จ" }, seq);
            return;
        }

        _context.InventoryItems.RemoveAt(idx);
        _lockedItemIds.Remove(item.Id);
        Send(default(OK), seq);
        Send(new InventoryUpdated { EntityId = EntityId, RemovedItemIds = new[] { item.Id } });
        OnContextChanged();
    }

    /// <summary>
    /// สร้างสถานะ "สัตว์ป่าในกรง" หนึ่งช่องจากบังเหียนหนึ่งอัน
    ///
    /// เลขสองตัวที่ห้ามสลับกัน (ฝั่งเกมเปิดคนละตาราง):
    ///   <c>EntityType</c>    = vehicle_entity_type → เปิด <c>Animal</c> (entity_types/animal.json)
    ///                          ใช้วาดรูป/ชื่อ (client/Durango.UI/DomesticCagePetListItemWidget.cs:69)
    ///   <c>PetEntityType</c> = pet_entity_type → เปิด <c>Yaml.Pet</c> (pet/pets_for_client.json)
    ///                          ใช้บอกชนิดอาหารที่ชอบ (PetItemInteractionPopup.cs:628)
    ///
    /// <c>TotalTime</c> ต้องใส่ตั้งแต่ยังไม่เริ่ม เพราะการ์ดสถานะ "ป่า" เอาไปโชว์เป็นเวลาที่จะใช้
    /// (client/Durango.UI/PetUtil.cs:122 ConvertInfoToRemainingTime → CageStatus.Wild)
    /// </summary>
    private static DomesticationInfo NewDomesticationInfo(Item item, DomesticationTables.ReinInfo rein)
    {
        var petType = (ushort)rein.PetEntityType;
        return new DomesticationInfo
        {
            ItemId = item.Id,
            EntityType = (ushort)rein.VehicleEntityType,
            PetEntityType = petType,
            Level = item.Level,
            // รุ่นที่ 1 เสมอ — เกมนี้ไม่มีระบบผสมพันธุ์ (เหมือน PetFactory.Build ใน Player.Animals.cs)
            Generation = 1,
            // ยังไม่รู้แรงก์จนกว่าจะทำให้เชื่องสำเร็จ — null ทำให้ฝั่งเกมโชว์ชื่อเปล่า ๆ ไม่มีป้ายแรงก์
            // (client/Durango.UI/DomesticCagePetInfoWidget.cs:134)
            Rank = null,
            TotalTime = DomesticationTables.BaseSecondsOf(rein.VehicleEntityType),
            DomesticateSince = 0.0,
            DomesticateUntil = 0.0,
            DomesticateSuccessRate = DomesticationTables.BaseSuccessRateOf(rein.VehicleEntityType),
            DomesticationSuccessMaxRate = DomesticationTuning.MaxSuccessRate,
            EatableTags = PetTables.EatableTagsOf(petType),
            Domesticated = false,
            DomesticationInProgress = false
        };
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  เริ่ม / ยกเลิก
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// StartDomestication (694357) — กดปุ่ม "길들이기" เริ่มจับเวลา
    /// ไม่มี timer เดินจริง: เก็บแค่ช่วงเวลา แล้ว FinishDomestication ค่อยตรวจว่าครบหรือยัง
    /// </summary>
    private void HandleStartDomesticationMsg(StartDomestication msg, uint seq)
    {
        if (!TryFindRein(msg.EntityId, msg.ItemId, out DomesticationInfo info, out string error))
        {
            Send(new Abort { Text = error }, seq);
            return;
        }
        if (info.Domesticated)
        {
            Send(new Abort { Text = "สัตว์ตัวนี้เชื่องแล้ว" }, seq);
            return;
        }
        if (info.DomesticationInProgress)
        {
            Send(new Abort { Text = "กำลังทำให้เชื่องอยู่แล้ว" }, seq);
            return;
        }

        double now = Times.UnixTimeNow();
        bool ok = MutateRein(msg.EntityId, msg.ItemId, r =>
        {
            r.DomesticationInProgress = true;
            r.DomesticateSince = now;
            r.DomesticateUntil = now + r.TotalTime;
            return r;
        });
        if (!ok)
        {
            Send(new Abort { Text = "เริ่มทำให้เชื่องไม่สำเร็จ" }, seq);
            return;
        }
        Send(default(OK), seq);
    }

    /// <summary>
    /// CancelDomestication (694354) — กดยกเลิกระหว่างทาง
    ///
    /// กล่องยืนยันของเกมบอกผู้เล่นไว้แล้วว่า "지금까지의 시간과 먹이는 보존되지 않습니다"
    /// (เวลาและอาหารที่ใส่ไปไม่ถูกเก็บไว้ — client/Durango.UI/DomesticCageGroup.cs:232)
    /// ⇒ ต้องคืนกลับเป็นสถานะป่าทั้งชุด รวมถึงโอกาสสำเร็จที่ป้อนอาหารดันขึ้นมา
    /// ไม่ใช่แค่หยุดนาฬิกา (ถ้าเก็บโอกาสไว้ ผู้เล่นจะกดยกเลิก-เริ่มใหม่วนไปเพื่อรีเซ็ตเวลาฟรี ๆ)
    /// </summary>
    private void HandleCancelDomesticationMsg(CancelDomestication msg, uint seq)
    {
        if (!TryFindRein(msg.EntityId, msg.ItemId, out DomesticationInfo info, out string error))
        {
            Send(new Abort { Text = error }, seq);
            return;
        }
        if (info.Domesticated || !info.DomesticationInProgress)
        {
            Send(new Abort { Text = "ตอนนี้ยังไม่ได้ทำให้เชื่องอยู่" }, seq);
            return;
        }

        float baseRate = DomesticationTables.BaseSuccessRateOf(info.EntityType);
        bool ok = MutateRein(msg.EntityId, msg.ItemId, r =>
        {
            r.DomesticationInProgress = false;
            r.DomesticateSince = 0.0;
            r.DomesticateUntil = 0.0;
            r.DomesticateSuccessRate = baseRate;
            return r;
        });
        if (!ok)
        {
            Send(new Abort { Text = "ยกเลิกไม่สำเร็จ" }, seq);
            return;
        }
        Send(default(OK), seq);
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ป้อนอาหาร
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// PutItemsForDomestication (694352) — ป้อนอาหารเพื่อ "ลดเวลา + เพิ่มโอกาสสำเร็จ"
    ///
    /// ⚠️ ฝั่งเกมยิงทิ้งไม่รอ reply (client/PetManager.cs:1134-1143) แต่ผู้เล่นเป็นคนกดเลือก
    /// อาหารเอง ⇒ ทำไม่ได้ต้องส่ง Abort แบบไม่มี seq เพื่อให้ขึ้นข้อความระบบ
    /// (client/GameManager.cs DefaultAbortHandler) ไม่ใช่เงียบไปเฉย ๆ
    ///
    /// สูตรทั้งสองมาจาก constants.json → pet ตรง ๆ และคิดด้วย Support/StatFormula.cs
    /// **ห้ามคิดเลขเอง** — ตัวแปร d_ratio / d_time / d_success_rate ก็ไม่ได้ hard-code ชื่อไว้
    /// แต่อ่านจาก <c>performance_reference</c> ในไฟล์เดียวกัน (ฝั่งเกมทำแบบเดียวกันเป๊ะ:
    /// client/Yaml/ConstantPet.cs:66-88 GetDomesticationProbability)
    /// </summary>
    private void HandlePutItemsForDomesticationMsg(PutItemsForDomestication msg)
    {
        if (!TryFindRein(msg.EntityId, msg.ReinId, out DomesticationInfo info, out string error))
        {
            Send(new Abort { Text = error });
            return;
        }
        if (info.Domesticated || !info.DomesticationInProgress)
        {
            Send(new Abort { Text = "ให้อาหารได้เฉพาะตอนกำลังทำให้เชื่อง" });
            return;
        }

        // คัดเฉพาะของที่ "อยู่ในกระเป๋าจริง + เป็นอาหารสัตว์ + สัตว์ตัวนี้กินได้"
        // เงื่อนไขชุดเดียวกับที่ฝั่งเกมใช้กรองรายการให้เลือก (client/Durango.UI/PetUtil.cs:126-165)
        var eaten = new List<Item>();
        var eatenIds = new List<string>();
        foreach (string id in msg.ItemIds ?? Array.Empty<string>())
        {
            int idx = _context.InventoryItems.FindIndex(it => it.Id == id);
            if (idx < 0) continue;
            Item item = _context.InventoryItems[idx];
            if (!DomesticationTables.IsPetFood(item.Prototype)) continue;
            if (info.EatableTags is { Length: > 0 } && !ItemHasAnyTag(item, info.EatableTags)) continue;
            eaten.Add(item);
            eatenIds.Add(item.Id);
        }
        if (eaten.Count == 0)
        {
            Send(new Abort { Text = "ไม่มีอาหารที่สัตว์ตัวนี้กินได้ในรายการที่เลือก" });
            return;
        }

        // ตัวแปรของสูตร = ผลรวมค่าจากอาหารทุกชิ้นที่ป้อน (ฝั่งเกมบวกแบบเดียวกัน)
        Dictionary<string, double> vars = DomesticationTables.SumPerformanceReference(eaten);
        vars["starts_at"] = info.DomesticateSince;
        vars["total_time"] = info.TotalTime;
        vars["ends_at"] = info.DomesticateUntil;
        vars["decrease_limit"] = DomesticationTables.DecreaseLimit;
        vars["max_prob"] = info.DomesticationSuccessMaxRate;
        vars["current_prob"] = info.DomesticateSuccessRate;

        // คิดสูตรไม่ผ่าน = ข้อมูลเปลี่ยนรูปไปจากที่สำรวจไว้ ⇒ **ห้ามเดาค่าแทน** ต้องคืนของให้ผู้เล่น
        if (!StatFormula.TryEval(DomesticationTables.TimeExpr, vars, out double until) ||
            !StatFormula.TryEval(DomesticationTables.ProbabilityExpr, vars, out double prob))
        {
            Console.WriteLine("[ทำให้เชื่อง] ⚠️ คิดสูตร domesticate_time/domesticate_probability ไม่ได้ — ไม่กินอาหาร");
            Send(new Abort { Text = "ข้อมูลสูตรทำให้เชื่องผิดพลาด — ยังให้อาหารไม่ได้" });
            return;
        }

        // สูตรลดเวลาได้อย่างเดียว (มี max() คุมพื้นอยู่แล้ว) — กันข้อมูลแปลกทำให้เวลาเดินถอยหลัง
        double nextUntil = Math.Min(info.DomesticateUntil, until);
        var nextRate = (float)Math.Clamp(prob, info.DomesticateSuccessRate, info.DomesticationSuccessMaxRate);

        bool ok = MutateRein(msg.EntityId, msg.ReinId, r =>
        {
            r.DomesticateUntil = nextUntil;
            r.DomesticateSuccessRate = nextRate;
            return r;
        });
        if (!ok)
        {
            Send(new Abort { Text = "ให้อาหารไม่สำเร็จ" });
            return;
        }

        foreach (string id in eatenIds)
        {
            int idx = _context.InventoryItems.FindIndex(it => it.Id == id);
            if (idx >= 0) _context.InventoryItems.RemoveAt(idx);
            _lockedItemIds.Remove(id);
        }
        Send(new InventoryUpdated { EntityId = EntityId, RemovedItemIds = eatenIds.ToArray() });
        OnContextChanged();
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ดูผล
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// FinishDomestication (694355) — กด "결과 확인" ตอนเวลาครบ ⇒ **ต้องตอบ DomesticationResult(694356)**
    ///
    /// client/PetManager.cs:1005-1019 ลงทะเบียนรับ DomesticationResult ตัวเดียว ที่เหลือเข้า
    /// .Rest → onResult(null) แล้ว UI เงียบไปเฉย ๆ ⇒ ตอบ OK เปล่า ๆ = ผู้เล่นกดแล้วไม่มีอะไรเกิดขึ้น
    ///
    /// ⚠️ ฟิลด์ <c>Stat</c>/<c>Original</c> ห้ามเป็น null ตอนสำเร็จ — หน้าต่างรางวัลอ่านด้วย
    /// indexer ตรง ๆ (<c>result.Stat[type]</c>, client/Durango.UI.Popup/DomesticationRewardPopup.cs:306)
    /// และต้องมีครบ 7 ค่า: InventoryCapacity / LifeMax / Speed / LifeSpan / Attack / Defense / Accuracy
    /// (PetFactory.DerivedOf ใน Player.Animals.cs ให้ครบทั้ง 7 พอดี)
    ///
    /// ล้มเหลว = สัตว์หนีหายถาวร (ตัวเกมเตือนผู้เล่นไว้แล้วในทูลทิปของหน้าจอกรง) ⇒ ลบออกจากกรง
    /// </summary>
    private void HandleFinishDomesticationMsg(FinishDomestication msg, uint seq)
    {
        if (!TryFindRein(msg.EntityId, msg.ItemId, out DomesticationInfo info, out string error))
        {
            Send(new Abort { Text = error }, seq);
            return;
        }
        if (info.Domesticated)
        {
            Send(new Abort { Text = "สัตว์ตัวนี้เชื่องแล้ว" }, seq);
            return;
        }
        if (!info.DomesticationInProgress)
        {
            Send(new Abort { Text = "ยังไม่ได้เริ่มทำให้เชื่อง" }, seq);
            return;
        }
        if (Times.UnixTimeNow() < info.DomesticateUntil)
        {
            Send(new Abort { Text = "ยังไม่ถึงเวลา" }, seq);
            return;
        }

        bool success = PetTuning.Rng.NextDouble() <
                       Math.Clamp(info.DomesticateSuccessRate, 0f, info.DomesticationSuccessMaxRate);
        Dictionary<Derived, float> derived = PetFactory.DerivedOf(info.PetEntityType, info.Level, null);

        if (!success)
        {
            // สัตว์หนี — เอาออกจากกรงและคืนที่ว่างให้กรง ไม่มีอะไรกลับเข้ากระเป๋า
            // ลบไม่สำเร็จแล้วยังตอบว่า "ล้มเหลว" = สัตว์ค้างในกรงให้กดดูผลซ้ำได้เรื่อย ๆ (กฎข้อ 2)
            if (!RemoveReinFromCage(msg.EntityId, msg.ItemId))
            {
                Send(new Abort { Text = "อัปเดตสถานะกรงไม่สำเร็จ" }, seq);
                return;
            }
            Send(new DomesticationResult
            {
                Domesticated = false,
                Rank = null,
                MilestoneCount = null,
                Tags = new Dictionary<string, int>(),
                Stat = derived,
                Original = derived,
                ReinId = msg.ItemId
            }, seq);
            return;
        }

        PetRank rank = RollDomesticatedRank(info.PetEntityType);
        bool marked = MutateRein(msg.EntityId, msg.ItemId, r =>
        {
            r.Domesticated = true;
            r.DomesticationInProgress = false;
            r.Rank = rank;
            return r;
        });
        // เขียนสถานะไม่ติด = ผู้เล่นจะเห็นหน้าต่างรางวัลแล้วกด "가방에 넣기" ไม่ได้ ⇒ อย่าเพิ่งบอกว่าสำเร็จ
        if (!marked)
        {
            Send(new Abort { Text = "อัปเดตสถานะกรงไม่สำเร็จ" }, seq);
            return;
        }
        Send(new DomesticationResult
        {
            Domesticated = true,
            Rank = rank,
            // "성장 횟수" = จำนวนช่อง milestone ที่สัตว์แรงก์นี้จะได้ (ค่าต่อแรงก์เป็นค่าของเรา
            // — ดู PetTuning.MilestoneCountOfRank ใน Player.Animals.cs)
            MilestoneCount = PetTuning.MilestoneCountOfRank(rank),
            // แท็กที่ "ค้นพบ" ตอนทำให้เชื่อง — ข้อมูลจริงไม่มีตารางนี้ ⇒ ส่งชุดว่าง
            // (หน้าต่างรางวัลซ่อนแถบแท็กเองเมื่อว่าง — DomesticationRewardPopup.cs:272-277)
            Tags = new Dictionary<string, int>(),
            // ยังไม่มีแท็ก milestone ⇒ ค่า "หลังเชื่อง" กับ "ค่าเดิม" เท่ากัน หน้าต่างจะไม่โชว์ส่วนต่าง
            Stat = derived,
            Original = derived,
            ReinId = msg.ItemId
        }, seq);
    }

    /// <summary>
    /// สุ่มแรงก์ของสัตว์ที่เพิ่งเชื่อง — พูลมาจากข้อมูลจริง pets_for_client.json → available_ranks
    /// (เช่น pet_phenaco มีแค่ [12] = B ⇒ สุ่มยังไงก็ได้ B) ส่วน **น้ำหนักเป็นค่าของเรา**
    /// ใช้ตัวเดียวกับ RevertPetRank เพื่อไม่ให้สองทางเข้าให้ผลต่างกัน (PetTuning.RankWeight)
    /// </summary>
    private static PetRank RollDomesticatedRank(ushort petEntityType)
    {
        PetRank[] pool = PetTables.AvailableRanks(petEntityType);
        // ชนิดที่ไม่มีในตาราง (4 ตัว "_elite" ใน performance.json ที่ไม่มีใน pets_for_client.json)
        // ⇒ ให้แรงก์ต่ำสุดไปก่อน ดีกว่าส่ง PetRank.Invalid ที่ฝั่งเกมเอาไปโชว์เป็นป้ายว่าง
        if (pool.Length == 0) return PetRank.D;
        int pick = PetTuning.PickWeighted(pool.Select(PetTuning.RankWeight).ToArray());
        return pool[pick < 0 ? 0 : pick];
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  เอาออกจากกรง
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TakeOutReinFromCage (694359) — ปุ่ม "가방에 넣기" / ปุ่มเอาสัตว์ป่าออกจากกรง
    ///
    /// ฝั่งเกมใช้ <c>.All(Packet.IsSuccess)</c> (client/PetManager.cs:1118-1132) ⇒ ตอบ OK
    /// (หมายเหตุ: เอกสารสั่งงานเขียนว่าให้ตอบ MilestoneCandidates — **ซอร์สเกมไม่ได้รอตัวนั้น**
    ///  MilestoneCandidates ถูกรอเฉพาะตอน GetMilestoneCandidate เท่านั้น client/PetManager.cs:1145-1158
    ///  จึงยึดตามซอร์สตามกฎข้อ 1)
    ///
    /// สองกรณีต่างกันคนละเรื่อง:
    ///   ยังไม่เชื่อง (CageStatus.Wild) → คืน "บังเหียน" กลับเข้ากระเป๋า เอาไปใส่กรงอื่นต่อได้
    ///   เชื่องแล้ว                     → ได้ **สัตว์เลี้ยงจริง** เข้า PetStore ของ Player.Animals.cs
    ///
    /// **[6 ก.ย. 2026] ตรงกับของจริงแล้ว** — เดิมยัดสัตว์เข้า PetStore ตรงนี้เลย เพราะตอนนั้น
    /// <c>UseItem</c> ยังตอบ Abort กับของที่ไม่ใช่อาหาร ⇒ คืนเป็นไอเทมแล้วสัตว์จะค้างใช้ไม่ได้
    /// ตอนนี้ Core/Player.Inventory.cs รับบังเหียนแล้ว (ดู TryImprintRein) จึงคืนเป็น
    /// "บังเหียนที่มีสัตว์อยู่ข้างใน" ตามลำดับของเกมจริง:
    ///   เอาออกจากกรง → ได้ไอเทมเข้ากระเป๋า → กด 귀속 (Imprint) → ถึงได้เป็นสัตว์เลี้ยง
    /// (client/Durango.UI/InventoryContainerBase.cs:1090-1136 DoImprinting)
    ///
    /// ข้อดีที่ตามมา: ผู้เล่นเห็นสัตว์ในกระเป๋าก่อนตัดสินใจ · ย้าย/ขายก่อนผูกพันได้เหมือนของจริง
    /// (หลังผูกพันแล้วเกมห้ามขาย — ข้อความยืนยันของเกมบอกไว้เอง)
    /// </summary>
    private void HandleTakeOutReinFromCageMsg(TakeOutReinFromCage msg, uint seq)
    {
        if (!TryFindRein(msg.EntityId, msg.ItemId, out DomesticationInfo info, out string error))
        {
            Send(new Abort { Text = error }, seq);
            return;
        }
        if (info.DomesticationInProgress && !info.Domesticated)
        {
            Send(new Abort { Text = "กำลังทำให้เชื่องอยู่ เอาออกไม่ได้" }, seq);
            return;
        }

        // ── เชื่องแล้ว: คืนเป็นบังเหียนที่ "มีสัตว์อยู่ข้างใน" รอผู้เล่นกดผูกพัน ─────────────
        if (info.Domesticated)
        {
            // ลำดับสำคัญ: ประกอบให้ครบก่อน → เช็คกระเป๋า → เอาออกจากกรง → ค่อยใส่กระเป๋า
            // สลับลำดับเมื่อไหร่จะได้สัตว์ซ้ำสองตัว หรือสัตว์หายเฉย ๆ ตอนประกอบไม่สำเร็จ
            PetStore.Entry entry = BuildTamedPetEntry(info);
            if (entry == null)
            {
                Send(new Abort { Text = "สร้างสัตว์เลี้ยงไม่สำเร็จ (ไม่พบข้อมูลสัตว์ชนิดนี้)" }, seq);
                return;
            }
            Item? withPet = RebuildReinItem(info);
            if (!withPet.HasValue)
            {
                Send(new Abort { Text = "สร้างบังเหียนคืนไม่ได้ (ไม่พบแบบไอเทมของสัตว์ชนิดนี้)" }, seq);
                return;
            }

            Item tamedItem = withPet.Value;
            // ยัดสัตว์ที่ประกอบไว้ลงในบังเหียน — นี่คือสิ่งที่ทำให้ปุ่ม "귀속" โผล่ฝั่งเกม
            // (client/Durango.Logic.Item/ItemData.cs:552 IsDomesticatedPet = Reins.Domesticated)
            tamedItem.Ext = new Reins
            {
                PetEntityType = (ushort)info.PetEntityType,
                VehicleEntityType = (ushort)(PetTables.PetOf(info.PetEntityType)?.VehicleEntityType ?? 0),
                Size = (ushort)DomesticationTables.SizeOfPet(info.PetEntityType),
                Pet = entry.Pet,
                Domesticated = true,
                DomesticateDuration = 0f,
                DomesticateSuccessRate = 1f
            };

            int usedSize = _context.InventoryItems.Sum(it => Math.Max(1, it.Size));
            if (usedSize + Math.Max(1, tamedItem.Size) > PetTuning.PlayerInventoryMaxSize)
            {
                Send(new Abort { Text = "กระเป๋าเต็ม" }, seq);
                return;
            }
            if (!RemoveReinFromCage(msg.EntityId, msg.ItemId))
            {
                Send(new Abort { Text = "เอาสัตว์ออกจากกรงไม่สำเร็จ" }, seq);
                return;
            }

            _context.InventoryItems.Add(tamedItem);
            Console.WriteLine($"[ทำให้เชื่อง] {EntityId[..Math.Min(8, EntityId.Length)]} ได้บังเหียนที่มีสัตว์ " +
                              $"{entry.Pet.Name} แรงก์ {entry.Pet.Rank} เลเวล {info.Level} — รอกดผูกพัน");
            Send(default(OK), seq);
            Send(new InventoryUpdated { EntityId = EntityId, Items = new[] { tamedItem } });
            OnContextChanged();
            return;
        }

        // ── ยังไม่เชื่อง: คืนบังเหียนเปล่ากลับกระเป๋า ────────────────────────────────────
        Item? rebuilt = RebuildReinItem(info);
        if (!rebuilt.HasValue)
        {
            Send(new Abort { Text = "สร้างบังเหียนคืนไม่ได้ (ไม่พบแบบไอเทมของสัตว์ชนิดนี้)" }, seq);
            return;
        }
        Item item = rebuilt.Value;
        int used = _context.InventoryItems.Sum(it => Math.Max(1, it.Size));
        if (used + Math.Max(1, item.Size) > PetTuning.PlayerInventoryMaxSize)
        {
            Send(new Abort { Text = "กระเป๋าเต็ม" }, seq);
            return;
        }

        if (!RemoveReinFromCage(msg.EntityId, msg.ItemId))
        {
            Send(new Abort { Text = "เอาบังเหียนออกจากกรงไม่สำเร็จ" }, seq);
            return;
        }
        _context.InventoryItems.Add(item);
        Send(default(OK), seq);
        Send(new InventoryUpdated { EntityId = EntityId, Items = new[] { item } });
        OnContextChanged();
    }

    /// <summary>
    /// ReleaseReinFromCage (694353) — ปุ่ม "풀어주기" ปล่อยกลับสู่ป่า
    ///
    /// ⚠️ ตัวนี้ **รอ OK เท่านั้น** ไม่ใช่ IsSuccess (client/PetManager.cs:689-706 ใช้ .On&lt;OK&gt;)
    /// ⇒ ตอบอย่างอื่นแล้วสำเร็จจริง ผู้เล่นจะไม่เห็นข้อความยืนยันเลย
    ///
    /// กล่องยืนยันของเกมบอกไว้ชัดว่า "풀어준 동물은 야생으로 돌아가 사라집니다"
    /// (สัตว์ที่ปล่อยจะกลับสู่ป่าและหายไป — client/Durango.UI/DomesticCageGroup.cs:194)
    /// ⇒ **ไม่คืนบังเหียน** ผู้เล่นรู้อยู่แล้วว่ากดแล้วของหาย
    /// </summary>
    private void HandleReleaseReinFromCageMsg(ReleaseReinFromCage msg, uint seq)
    {
        if (!TryFindRein(msg.EntityId, msg.ItemId, out DomesticationInfo _, out string error))
        {
            Send(new Abort { Text = error }, seq);
            return;
        }
        if (!RemoveReinFromCage(msg.EntityId, msg.ItemId))
        {
            Send(new Abort { Text = "ปล่อยสัตว์ไม่สำเร็จ" }, seq);
            return;
        }
        Send(default(OK), seq);
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ตัวช่วยระดับกรง
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>หาสัตว์หนึ่งช่องในกรง — คืนเหตุผลเป็นข้อความไทยพร้อมส่งเป็น Abort ให้เลย</summary>
    private bool TryFindRein(string entityId, string itemId, out DomesticationInfo info, out string error)
    {
        info = default;
        DomesticCage? cage = _world.ArtifactManager.GetDomesticCage(entityId);
        if (!cage.HasValue)
        {
            error = "ที่นี่ไม่ใช่กรงฝึกให้เชื่อง";
            return false;
        }
        DomesticationInfo[] reins = cage.Value.Reins ?? Array.Empty<DomesticationInfo>();
        int idx = Array.FindIndex(reins, r => r.ItemId == itemId);
        if (idx < 0)
        {
            error = "ไม่พบสัตว์ตัวนี้ในกรง";
            return false;
        }
        info = reins[idx];
        error = null;
        return true;
    }

    /// <summary>
    /// แก้สถานะสัตว์หนึ่งช่องในกรงแล้วกระจายให้ทุกคนบนเกาะเห็น
    ///
    /// ⚠️ <c>DomesticCage.Reins</c> เป็นอาเรย์ของ struct — ต้อง clone แล้วเขียนกลับทั้งอาเรย์
    /// การแก้ตัวที่หยิบออกมาจาก <c>reins[i]</c> ไม่มีผลใด ๆ (สำเนาค่า) ซึ่งเป็นกับดักที่เงียบมาก
    /// </summary>
    private bool MutateRein(string entityId, string itemId, Func<DomesticationInfo, DomesticationInfo> mutate)
    {
        bool found = false;
        bool updated = _world.ArtifactManager.UpdateDomesticCage(entityId, cage =>
        {
            DomesticationInfo[] reins = cage.Reins ?? Array.Empty<DomesticationInfo>();
            int idx = Array.FindIndex(reins, r => r.ItemId == itemId);
            if (idx < 0) return cage;
            var copy = (DomesticationInfo[])reins.Clone();
            copy[idx] = mutate(copy[idx]);
            cage.Reins = copy;
            found = true;
            return cage;
        });
        return updated && found;
    }

    /// <summary>เอาสัตว์ออกจากกรงพร้อมคืนที่ว่างตามขนาดของสัตว์ชนิดนั้น</summary>
    private bool RemoveReinFromCage(string entityId, string itemId)
    {
        bool found = false;
        bool updated = _world.ArtifactManager.UpdateDomesticCage(entityId, cage =>
        {
            DomesticationInfo[] reins = cage.Reins ?? Array.Empty<DomesticationInfo>();
            int idx = Array.FindIndex(reins, r => r.ItemId == itemId);
            if (idx < 0) return cage;
            int size = DomesticationTables.SizeOfPet(reins[idx].PetEntityType);
            cage.Reins = reins.Where((_, i) => i != idx).ToArray();
            cage.RemainSize = (byte)Math.Min(cage.Size, cage.RemainSize + size);
            found = true;
            return cage;
        });
        return updated && found;
    }

    /// <summary>
    /// ประกอบสัตว์เลี้ยงหนึ่งตัวให้พร้อมใส่ <c>PetStore</c> ของ Core/Player.Animals.cs — null = ประกอบไม่ได้
    /// (ใช้ PetFactory ตัวเดียวกับที่หน้าร้านใช้พรีวิว ⇒ ค่าสถานะที่ผู้เล่นเห็นตอนพรีวิวกับตอนได้จริงตรงกัน)
    /// </summary>
    [CanBeNull]
    private PetStore.Entry BuildTamedPetEntry(DomesticationInfo info)
    {
        PetRank rank = info.Rank ?? PetRank.D;
        Messages.Pet? built = PetFactory.Build(info.PetEntityType, rank, info.Level, EntityId);
        if (!built.HasValue) return null;

        Messages.Pet pet = built.Value;
        PetTables.ReinPerf perf = PetTables.PerfOf(info.PetEntityType, info.Level);
        return new PetStore.Entry
        {
            Pet = pet,
            Grazing = false,
            // เพดานหลอดที่ Player.Animals.cs ใช้ตอนป้อนอาหาร/ชุบชีวิต — ต้องตรงกับที่ประกอบ Pet ไว้
            LifeMax = pet.Statistics.DerivedAbilities?.GetValueOrDefault(Derived.LifeMax) ?? 0f,
            HungryMax = pet.Statistics.DerivedAbilities?.GetValueOrDefault(Derived.HungryMax) ?? 0f,
            HungryVelocity = perf?.HungryVelocity ?? 0f
        };
    }

    /// <summary>
    /// ประกอบ <c>PetStore.Entry</c> จากบังเหียนที่ "มีสัตว์อยู่ข้างในแล้ว" — ใช้ตอนผู้เล่นกดผูกพัน
    ///
    /// ต่างจาก <see cref="BuildTamedPetEntry"/> ตรงที่ **ไม่สร้างสัตว์ใหม่**: สัตว์ตัวนี้ถูกประกอบ
    /// ไว้ตั้งแต่ตอนออกจากกรงฝึกแล้ว (ค่าสถานะ/แท็ก/หลอด เป็นของตัวนั้นจริง ๆ)
    /// สร้างใหม่ = ผู้เล่นฝึกมาทั้งเรื่องแล้วได้สัตว์คนละตัวกับที่เห็นในหน้าต่างยืนยัน
    ///
    /// เพดานหลอดต้องคิดจากตัวสัตว์เอง ไม่ใช่จากตาราง เพราะแท็กที่ได้ระหว่างฝึกมีผลกับ
    /// <c>DerivedAbilities</c> ไปแล้ว (เกณฑ์เดียวกับ BuildTamedPetEntry)
    /// </summary>
    [CanBeNull]
    private static PetStore.Entry BuildPetEntryFromReins(Reins reins)
    {
        if (!reins.Pet.HasValue) return null;
        Messages.Pet pet = reins.Pet.Value;
        if (string.IsNullOrEmpty(pet.EntityId)) return null;

        PetTables.ReinPerf perf = PetTables.PerfOf(pet.EntityType, pet.Statistics.Level);
        return new PetStore.Entry
        {
            Pet = pet,
            Grazing = false,
            LifeMax = pet.Statistics.DerivedAbilities?.GetValueOrDefault(Derived.LifeMax) ?? 0f,
            HungryMax = pet.Statistics.DerivedAbilities?.GetValueOrDefault(Derived.HungryMax) ?? 0f,
            HungryVelocity = perf?.HungryVelocity ?? 0f
        };
    }

    /// <summary>
    /// ผู้เล่นกด "ผูกพัน" (귀속) กับบังเหียนที่มีสัตว์อยู่ข้างใน — เรียกจาก UseItem
    ///
    /// คืน <c>true</c> เมื่อผูกพันสำเร็จ (ผู้เรียกต้องลบไอเทมออกจากกระเป๋าแล้วตอบ OK)
    /// คืน <c>false</c> พร้อม <paramref name="error"/> เมื่อไอเทมนี้ไม่ใช่บังเหียนที่ผูกพันได้
    ///
    /// เกณฑ์ "ผูกพันได้" ตรงกับฝั่งเกม: <c>ItemData.IsDomesticatedPet()</c> =
    /// <c>Reins.HasValue &amp;&amp; Reins.Value.Domesticated</c>
    /// (client/Durango.Logic.Item/ItemData.cs:552 · Useable.cs:128-131 → UseType.Imprint)
    /// </summary>
    private bool TryImprintRein(Item item, out string error)
    {
        error = null;
        if (item.Ext is not Reins reins) { error = null; return false; }   // ไม่ใช่บังเหียน — ให้ผู้เรียกไปทางอื่น

        if (!reins.Domesticated || !reins.Pet.HasValue)
        {
            error = "บังเหียนนี้ยังไม่มีสัตว์ที่เชื่องแล้วอยู่ข้างใน";
            return false;
        }

        PetStore.Entry entry = BuildPetEntryFromReins(reins);
        if (entry == null)
        {
            error = "ข้อมูลสัตว์ในบังเหียนเสียหาย";
            return false;
        }

        List<PetStore.Entry> store = PetStore.Of(EntityId);
        // กันกดซ้ำ/แพ็กเก็ตซ้ำ — สัตว์ตัวเดิมเข้าสองครั้งจะได้สัตว์ผีที่ลบไม่ออก
        if (store.Any(e => e.Pet.EntityId == entry.Pet.EntityId))
        {
            error = "ผูกพันสัตว์ตัวนี้ไปแล้ว";
            return false;
        }

        store.Add(entry);
        Console.WriteLine($"[ทำให้เชื่อง] {EntityId[..Math.Min(8, EntityId.Length)]} ผูกพัน " +
                          $"{entry.Pet.EntityId[..Math.Min(8, entry.Pet.EntityId.Length)]} " +
                          $"(ชนิด {entry.Pet.EntityType} เลเวล {entry.Pet.Statistics.Level})");
        return true;
    }

    /// <summary>
    /// ประกอบไอเทมบังเหียนคืนจากสถานะในกรง (ใช้ตอนเอาสัตว์ที่ยังไม่เชื่องออก)
    ///
    /// ไม่ได้เก็บไอเทมตัวเดิมไว้ เพราะของที่เก็บในหน่วยความจำจะหายตอนรีสตาร์ทเซิร์ฟ
    /// แต่สถานะกรงถูกเซฟลงไฟล์ ⇒ ประกอบใหม่จากข้อมูลจริงแทน แล้วคง <c>Id</c> เดิมไว้
    /// (Id เดิมคือคีย์ที่ฝั่งเกมใช้อ้างสัตว์ตัวนี้มาตลอด และทำให้ล็อกไอเทมเดิมยังตรงกัน)
    ///
    /// prototype ของบังเหียนหาจาก pets_for_client.json → &lt;pet_entity_type&gt; → rein_id
    /// (ตรวจแล้วว่า rein_id ของสัตว์ทั้ง 74 ชนิดมีอยู่ครบใน performance.json → reins)
    /// </summary>
    private static Item? RebuildReinItem(DomesticationInfo info)
    {
        string prototypeId = PetTables.PetOf(info.PetEntityType)?.ReinId;
        if (string.IsNullOrEmpty(prototypeId)) return null;
        Item? made = Cheats.MakeItem(prototypeId, Math.Max(1, info.Level));
        if (!made.HasValue) return null;

        Item item = made.Value;
        item.Id = info.ItemId;
        item.Ext = DomesticationTables.MakeReinsExt(prototypeId);
        return item;
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ซ่อมบังเหียนในกระเป๋า
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// เติม <c>Item.Ext = Reins</c> ให้บังเหียนทุกอันในกระเป๋า — **ไม่ทำ = ระบบนี้เริ่มไม่ได้เลย**
    ///
    /// หน้าต่างเลือกสัตว์ใส่กรงกรองรายการด้วย <c>data.Reins.HasValue</c> ตรง ๆ
    /// (client/Durango.UI.Popup/PetItemInteractionPopup.cs:400) และ <c>ItemData.Reins</c> มาจาก
    /// <c>Item.Ext</c> เท่านั้น (client/Durango.Logic.Item/ItemData.cs:293-296)
    /// ⇒ บังเหียนที่ไม่มี Ext จะ **ไม่โผล่ในรายการเลย** ทั้งที่อยู่ในกระเป๋า
    /// (Core/Cheats.cs:MakeItem ตั้ง Ext ให้ตั้งแต่ตอนสร้างแล้ว — ตัวกวาดนี้ยังต้องมีเพราะไอเทม
    ///  ที่อยู่ในไฟล์เซฟเก่าก่อนหน้านั้นยังไม่มี Ext และเพราะเหตุผล JObject ข้างล่าง)
    ///
    /// ⚠️ อีกเหตุผลที่ต้องกวาดทุกครั้งที่เข้าเกม: <c>Item.Ext</c> ประกาศเป็น <c>object</c> พอเซฟลง
    /// ไฟล์แล้วอ่านกลับ Newtonsoft คืนมาเป็น <c>JObject</c> · <c>Item.Pack</c> (บรรทัด 213-244)
    /// เขียนเฉพาะชนิดที่รู้จักและ **ไม่มี else** ⇒ เจอ JObject แล้วไม่เขียนอะไรลงไปเลยสักไบต์
    /// ทำให้ฟิลด์ที่เหลือของไอเทมเลื่อนตำแหน่งทั้งก้อน (อาการเดียวกับบั๊กสถานะกรงใน
    /// Support/CageTypes.cs:NormalizeLoaded) ⇒ สร้างใหม่จากข้อมูลจริงทุกครั้ง ปลอดภัยกว่าแปลงกลับ
    ///
    /// เรียกจาก RegisterDomesticationHandlers() ซึ่งอยู่ก่อน SendInventory() ในตัวสร้าง Player
    ///
    /// ⚠️ **[6 ก.ย. 2026] บรรทัด `if (item.Ext is Reins) continue;` ข้างล่างสำคัญกว่าที่เห็น**
    /// ตั้งแต่ระบบผูกพันใช้งานได้ บังเหียนที่ออกจากกรงฝึกจะ **มีสัตว์อยู่ข้างใน**
    /// (<c>Reins.Pet</c> + <c>Domesticated = true</c>) ซึ่งสร้างใหม่จาก prototype ไม่ได้เลย
    /// ⇒ เอาบรรทัดนั้นออกเมื่อไหร่ = สัตว์ที่ผู้เล่นฝึกมาทั้งเรื่องหายทุกครั้งที่เข้าเกม
    ///
    /// ของที่โหลดจากไฟล์กู้ไปแล้วก่อนถึงตรงนี้: <c>PlayerContext.Initialize</c> เรียก
    /// <c>ItemExtRepair.Normalize(InventoryItems)</c> ซึ่งรู้จัก <c>Reins</c> และแปลงกลับด้วย
    /// <c>Json.Setting</c> ชุดเดียวกับตอนเขียน (ได้ GaugeConverter ⇒ หลอดของสัตว์ไม่หาย)
    /// ⇒ มาถึงตรงนี้ Ext เป็น <c>Reins</c> จริงแล้ว ไม่ใช่ JObject
    /// </summary>
    private void NormalizeReinItems()
    {
        List<Item> items = _context.InventoryItems;
        int fixedCount = 0;
        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            // ที่ถูกต้องอยู่แล้ว (ยังไม่เคยผ่านการเซฟ) — ห้ามทับ เผื่อวันหน้ามีบังเหียนที่พกสัตว์มาด้วย
            if (item.Ext is Reins) continue;
            object ext = DomesticationTables.MakeReinsExt(item.Prototype);
            if (ext != null)
            {
                if (item.Ext is JObject)
                {
                    Console.WriteLine($"[ทำให้เชื่อง] บังเหียน {item.Prototype} โหลดกลับมาเป็น JObject (ไม่มีสัตว์ข้างใน) — สร้างใหม่จากข้อมูลจริง");
                }
                item.Ext = ext;
                items[i] = item;
                fixedCount++;
                continue;
            }
            // ไม่ใช่บังเหียนแต่มี Ext ค้างมาจากไฟล์เซฟเป็น JObject = ระเบิดเวลาของ Item.Pack
            // ไฟล์นี้ไม่รู้ว่าเดิมมันเป็นชนิดไหน จึงไม่แตะ แค่ส่งเสียงให้เห็นใน log (ดูรายงาน)
            if (item.Ext is JObject)
            {
                Console.WriteLine($"[ทำให้เชื่อง] ⚠️ ไอเทม {item.Prototype} มี Ext เป็น JObject — " +
                                  "Item.Pack จะข้ามช่องนี้ทำให้แพ็กเก็ตเลื่อนทั้งก้อน");
            }
        }
        if (fixedCount > 0) Console.WriteLine($"[ทำให้เชื่อง] เติมข้อมูลบังเหียนให้ไอเทม {fixedCount} ชิ้น");
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ตารางข้อมูลจริงของระบบนี้ (โหลดครั้งเดียวตอนใช้ครั้งแรก)
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// อ่าน <c>performance.json</c> (reins / pet_food) กับ <c>constants.json → pet</c>
    ///
    /// ทำไมยังอ่านไฟล์เดียวกันซ้ำเป็นของตัวเองแทนที่จะใช้ <c>Core/PerformanceYaml.cs</c>:
    /// ระบบนี้ต้องค้นย้อนจาก <c>pet_entity_type</c> → ขนาดที่กินในกรง (<see cref="SizeOfPet"/>)
    /// ตอนคืนที่ว่างให้กรง แต่ PerformanceYaml ทำดัชนีด้วย prototype ของไอเทมอย่างเดียว
    /// (เหตุผลเดียวกับที่ PetTables ใน Player.Animals.cs ทำ) — ตัวเลขมาจากไฟล์เดียวกันจึงตรงกันเสมอ
    /// </summary>
    private static class DomesticationTables
    {
        public sealed class ReinInfo
        {
            public string PrototypeId;
            public int PetEntityType;
            public int VehicleEntityType;

            /// <summary>ที่ที่สัตว์ตัวนี้กินในกรง (performance.json → reins → size) ไม่ใช่ขนาดไอเทมในกระเป๋า</summary>
            public int Size;
        }

        private static Dictionary<string, ReinInfo> _byPrototype;
        private static Dictionary<int, ReinInfo> _byPetType;
        private static HashSet<string> _petFoodPrototypes;
        private static Dictionary<string, Dictionary<string, Dictionary<string, double>>> _petFoodNums;

        private static string _timeExpr;
        private static string _probabilityExpr;
        private static double _decreaseLimit;
        private static Dictionary<string, string[]> _performanceReference;
        private static HashSet<int> _advancedTameable;

        /// <summary>constants.json → pet → domesticate_time</summary>
        public static string TimeExpr
        {
            get { EnsureLoaded(); return _timeExpr; }
        }

        /// <summary>constants.json → pet → domesticate_probability</summary>
        public static string ProbabilityExpr
        {
            get { EnsureLoaded(); return _probabilityExpr; }
        }

        /// <summary>constants.json → pet → domesticate_decrease_limit (ของจริง = 0.5)</summary>
        public static double DecreaseLimit
        {
            get { EnsureLoaded(); return _decreaseLimit; }
        }

        [CanBeNull]
        public static ReinInfo ReinOf(string prototypeId)
        {
            EnsureLoaded();
            return prototypeId == null ? null : _byPrototype.GetValueOrDefault(prototypeId);
        }

        /// <summary>ขนาดที่สัตว์ชนิดนี้กินในกรง — 0 = ไม่รู้จัก (จะไม่คืนที่ว่างให้กรงเกินจริง)</summary>
        public static int SizeOfPet(int petEntityType)
        {
            EnsureLoaded();
            return _byPetType.GetValueOrDefault(petEntityType)?.Size ?? 0;
        }

        public static bool IsPetFood(string prototypeId)
        {
            EnsureLoaded();
            return prototypeId != null && _petFoodPrototypes.Contains(prototypeId);
        }

        /// <summary>
        /// เวลาฐานตามชนิดสัตว์ — แยกยาก/ง่ายด้วย constants.json → pet → advanced_tameable
        /// (รายชื่อเป็นข้อมูลจริง แต่ตัวเลขเวลาเป็นค่าของเรา ดู DomesticationTuning)
        /// </summary>
        public static double BaseSecondsOf(int vehicleEntityType)
        {
            EnsureLoaded();
            return _advancedTameable.Contains(vehicleEntityType)
                ? DomesticationTuning.AdvancedSeconds
                : DomesticationTuning.BaseSeconds;
        }

        /// <summary>โอกาสสำเร็จตั้งต้นตามชนิดสัตว์ — เกณฑ์แยกเดียวกับ <see cref="BaseSecondsOf"/></summary>
        public static float BaseSuccessRateOf(int vehicleEntityType)
        {
            EnsureLoaded();
            return _advancedTameable.Contains(vehicleEntityType)
                ? DomesticationTuning.AdvancedSuccessRate
                : DomesticationTuning.BaseSuccessRate;
        }

        /// <summary>
        /// ก้อน <c>Item.Ext</c> ของบังเหียน — คืน null ถ้า prototype นี้ไม่ใช่บังเหียน
        ///
        /// <c>Size</c> ตัวนี้คือที่ที่กินในกรง ฝั่งเกมเอาไปเทียบกับ <c>DomesticCage.RemainSize</c>
        /// เพื่อเปิด/ปิดปุ่มก่อนส่งมาถึงเซิร์ฟ (PetItemInteractionPopup.cs:597-608)
        /// <c>Pet</c> เป็น null เสมอในเซิร์ฟนี้ — สัตว์ที่เชื่องแล้วเข้า PetStore ตรง ๆ ไม่ผ่านไอเทม
        /// (ดูเหตุผลที่ HandleTakeOutReinFromCageMsg)
        /// </summary>
        [CanBeNull]
        public static object MakeReinsExt(string prototypeId)
        {
            ReinInfo rein = ReinOf(prototypeId);
            if (rein == null) return null;
            return new Reins
            {
                PetEntityType = (ushort)rein.PetEntityType,
                VehicleEntityType = (ushort)rein.VehicleEntityType,
                Size = (ushort)rein.Size,
                Pet = null,
                Domesticated = false,
                DomesticateDuration = (float)BaseSecondsOf(rein.VehicleEntityType),
                DomesticateSuccessRate = BaseSuccessRateOf(rein.VehicleEntityType)
            };
        }

        /// <summary>
        /// รวมค่าจากอาหารทุกชิ้นให้เป็นตัวแปรของสูตร ตาม
        /// <c>constants.json → pet → performance_reference</c>
        /// (เช่น "d_time" → ["decrease_domesticate_time"] แล้วบวกค่าของอาหารทุกชิ้นเข้าด้วยกัน)
        ///
        /// อ่านจาก <c>Item.Performance</c> ของไอเทมก่อน (เป็นชุดเดียวกับที่ฝั่งเกมใช้ทำนายผลให้
        /// ผู้เล่นเห็นก่อนกดยืนยัน) ถ้าไอเทมไม่ได้พกมา ค่อยเปิด performance.json เอง —
        /// ปกติ Support/ItemPerformance.cs:MergeInto เติมคีย์ pet_food ครบทุกตัวให้ตั้งแต่ตอนสร้าง
        /// ไอเทมแล้ว ทางสำรองนี้ไว้กันไอเทมเก่าในไฟล์เซฟที่ประกอบก่อนมี MergeInto
        /// </summary>
        public static Dictionary<string, double> SumPerformanceReference(IReadOnlyList<Item> items)
        {
            EnsureLoaded();
            var result = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string[]> pair in _performanceReference)
            {
                double sum = 0.0;
                foreach (string key in pair.Value ?? Array.Empty<string>())
                {
                    for (int i = 0; i < items.Count; i++) sum += Attribute(items[i], key);
                }
                result[pair.Key] = sum;
            }
            return result;
        }

        /// <summary>ค่า attribute หนึ่งตัวของไอเทม — ตรรกะเดียวกับ ItemData.GetFloatAttribute(key)</summary>
        private static double Attribute(Item item, string key)
        {
            if (item.Performance != null)
            {
                foreach (Performance p in item.Performance)
                {
                    if (p.Nums != null && p.Nums.TryGetValue(key, out float v)) return v;
                }
            }
            EnsureLoaded();
            if (item.Prototype == null || !_petFoodNums.TryGetValue(item.Prototype, out var byRange)) return 0.0;
            Dictionary<string, double> row = PickByLevel(byRange, item.Level);
            return row != null && row.TryGetValue(key, out double raw) ? raw : 0.0;
        }

        /// <summary>
        /// คีย์ชั้นในของ performance.json เป็นช่วงเลเวลเขียนเป็นข้อความ "[1, 60]"
        /// (pet_food มีหลายช่วงจริง ๆ เช่น "[1, 29]" / "[30, 59]" / "[60, 70]")
        /// </summary>
        [CanBeNull]
        private static T PickByLevel<T>(Dictionary<string, T> byRange, int level) where T : class
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

        private static void EnsureLoaded()
        {
            if (_byPrototype != null) return;
            _byPrototype = new Dictionary<string, ReinInfo>(StringComparer.Ordinal);
            _byPetType = new Dictionary<int, ReinInfo>();
            _petFoodPrototypes = new HashSet<string>(StringComparer.Ordinal);
            _petFoodNums = new Dictionary<string, Dictionary<string, Dictionary<string, double>>>(StringComparer.Ordinal);
            _performanceReference = new Dictionary<string, string[]>(StringComparer.Ordinal);
            _advancedTameable = new HashSet<int>();
            _timeExpr = null;
            _probabilityExpr = null;
            _decreaseLimit = 0.0;

            LoadPerformance();
            LoadConstants();

            Console.WriteLine($"[ทำให้เชื่อง] โหลดบังเหียน {_byPrototype.Count} ชนิด · " +
                              $"อาหารสัตว์ {_petFoodPrototypes.Count} ชนิด · " +
                              $"สัตว์ระดับสูง {_advancedTameable.Count} ชนิด");
        }

        private static void LoadPerformance()
        {
            JObject root = Json.ReadFromFile<JObject>("performance");
            if (root == null)
            {
                Console.WriteLine("[ทำให้เชื่อง] ⚠️ อ่าน performance.json ไม่ได้ — ใส่บังเหียนเข้ากรงไม่ได้เลย");
                return;
            }

            if (root["reins"] is JObject reins)
            {
                foreach (JProperty prototype in reins.Properties())
                {
                    if (prototype.Value is not JObject byLevel) continue;
                    foreach (JProperty row in byLevel.Properties())
                    {
                        // ค่าของบังเหียนเท่ากันทุกช่วงเลเวล (ไฟล์จริงมีช่วงเดียวคือ "[1, 60]") เอาแถวแรกพอ
                        if (row.Value is not JObject v) continue;
                        var info = new ReinInfo
                        {
                            PrototypeId = prototype.Name,
                            PetEntityType = (int?)v["pet_entity_type"] ?? 0,
                            VehicleEntityType = (int?)v["vehicle_entity_type"] ?? 0,
                            Size = (int?)v["size"] ?? 0
                        };
                        if (info.PetEntityType <= 0) break;
                        _byPrototype[prototype.Name] = info;
                        _byPetType[info.PetEntityType] = info;
                        break;
                    }
                }
            }

            if (root["pet_food"] is not JObject foods) return;
            foreach (JProperty prototype in foods.Properties())
            {
                if (prototype.Value is not JObject byLevel) continue;
                _petFoodPrototypes.Add(prototype.Name);
                var ranges = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
                foreach (JProperty row in byLevel.Properties())
                {
                    if (row.Value is not JObject v) continue;
                    var nums = new Dictionary<string, double>(StringComparer.Ordinal);
                    foreach (JProperty field in v.Properties())
                    {
                        // ค่าในไฟล์ปนกันทั้งตัวเลขและข้อความ ("0.025") ⇒ แปลงเท่าที่แปลงได้ ที่เหลือข้าม
                        if (field.Value.Type == JTokenType.Integer || field.Value.Type == JTokenType.Float)
                        {
                            nums[field.Name] = (double)field.Value;
                        }
                        else if (field.Value.Type == JTokenType.String &&
                                 double.TryParse((string)field.Value, NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out double parsed))
                        {
                            nums[field.Name] = parsed;
                        }
                    }
                    ranges[row.Name] = nums;
                }
                _petFoodNums[prototype.Name] = ranges;
            }
        }

        private static void LoadConstants()
        {
            JObject root = Json.ReadFromFile<JObject>("constants");
            if (root?["pet"] is not JObject pet)
            {
                Console.WriteLine("[ทำให้เชื่อง] ⚠️ อ่าน constants.json → pet ไม่ได้ — สูตรทำให้เชื่องหายหมด");
                return;
            }
            _timeExpr = (string)pet["domesticate_time"];
            _probabilityExpr = (string)pet["domesticate_probability"];
            _decreaseLimit = (double?)pet["domesticate_decrease_limit"] ?? 0.0;

            if (pet["performance_reference"] is JObject refs)
            {
                foreach (JProperty p in refs.Properties())
                {
                    _performanceReference[p.Name] = p.Value is JArray arr
                        ? arr.Select(t => (string)t).Where(s => s != null).ToArray()
                        : Array.Empty<string>();
                }
            }
            if (pet["advanced_tameable"] is JArray advanced)
            {
                foreach (JToken t in advanced)
                {
                    if (t.Type == JTokenType.Integer) _advancedTameable.Add((int)t);
                }
            }
        }
    }
}
