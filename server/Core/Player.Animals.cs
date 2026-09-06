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
using Shared.Economy;
using Shared.Encyclopedia;
using Shared.Pet;

namespace Durango.Online;

/// <summary>
/// [5 ก.ย. 2026] ระบบสัตว์/สัตว์เลี้ยง — handler ของหมวด "สัตว์/เลี้ยง" ใน docs/protocol-coverage.md
///
/// ไล่ลำดับที่เกมยิงจริงตอนเปิดหน้าจอสัตว์เลี้ยง (client/Durango.UI/PetGroup.cs):
///   Opened() :143  → RefreshPetList() :152 → PetManager.GetPetList() (client/PetManager.cs:820)
///   → ส่ง <c>GetPetsInfo</c> แล้ว **รอ <c>PetsInfo</c> ตัวเดียว**
///   → SetPetList() :159 ปลด LoadingRing แล้วเลือกแท็บ
/// ⇒ ถ้าไม่ตอบ PetsInfo หน้าจอค้างวงกลมโหลดตลอดกาล (ไม่มี timeout ในโค้ดเกม)
/// หน้าจอนี้ไม่ยิงอย่างอื่นตามมาเลย — ตัวอื่น ๆ (GetPetInventory / GetAvailableTask /
/// GetPreviewPet) ถูกยิงจากหน้าจออื่นคนละจังหวะ ดูคอมเมนต์ที่ handler แต่ละตัว
///
/// ══════════════════════════════════════════════════════════════════════════════════════
/// ⚠️ สามข้อจำกัดใหญ่ที่ทำให้ระบบนี้ยังใช้จริงไม่ได้ครบ — ทั้งหมดอยู่ในไฟล์นอกขอบเขตของไฟล์นี้
/// ══════════════════════════════════════════════════════════════════════════════════════
///
/// 1) **เมนูกรงสัตว์ยังไม่โผล่บนจอ** ⇒ ทั้งการทำให้เชื่องและงานในกรงยังเริ่มไม่ได้เลย
///    เกมเปิดหน้ากรงจาก interaction ที่เซิร์ฟส่งมาใน Touched.Interactions เท่านั้น
///    (client/Durango.UI/GrowCageGroup.cs:62 Interaction.Cage → Open · Interaction.OpenDomesticCage)
///    แต่ Core/Player.cs:958-1027 HandleTouchMsg ยังไม่ได้ใส่สองตัวนี้ ทั้งที่ข้อมูลจริงมีครบ:
///    data/assets/entity_types/artifact.json มี component "GrowCage" 4 ตัว และ "DomesticCage" 2 ตัว
///    ⇒ ต้องเพิ่มทำนอง
///      if (blueprint.Components.Contains("GrowCage"))     list.Add(Shared.System.Interaction.Cage);
///      if (blueprint.Components.Contains("DomesticCage")) list.Add(Shared.System.Interaction.OpenDomesticCage);
///
/// 2) **สถานะกรงเก็บที่ ArtifactState (Cage / DomesticCage) ซึ่งไฟล์นี้เขียนไม่ได้**
///    UI กรงอ่านสถานะจาก artifact ตรง ๆ (client/Durango.UI/PetUtil.cs GetGrowCage(artifact))
///    ไม่ได้ถามเซิร์ฟเป็น message ⇒ ต่อให้ตอบ PutInCage/StartPetTask ว่า OK หน้าจอก็ไม่เปลี่ยน
///    เพราะ Core/ArtifactManager.cs ไม่มี API เขียน ArtifactState.Cage/DomesticCage และไม่มีที่เซฟใน
///    Core/WorldContext.cs ⇒ **ตอบ Abort ทุกตัวที่ผูกกับกรง** ตามกฎข้อ 2 ของโปรเจกต์
///    (ตอบ OK ทั้งที่ไม่ได้ทำ = อาหาร/บังเหียนหายในสายตาผู้เล่น)
///
/// 3) **ยังไม่มีทางได้สัตว์เลี้ยงตัวแรก** — ทางเดียวในเกมคือทำให้เชื่องในกรง (ติดข้อ 1+2)
///    ⇒ ตอนนี้ PetStore จะว่างเสมอ หน้าจอสัตว์เลี้ยงจึงโชว์แผง "ยังไม่มีสัตว์" (ไม่ค้าง — ถูกต้องแล้ว)
///    handler ชั้น B-E ในไฟล์นี้เขียนไว้ครบและทำงานทันทีที่มีสัตว์อยู่ในสโตร์
///
/// ══════════════════════════════════════════════════════════════════════════════════════
/// แหล่งข้อมูลจริงที่ไฟล์นี้ใช้ (ไม่มีตัวเลขไหนเดาเอาเอง เว้นที่เขียนกำกับว่า "ค่าของเรา")
/// ══════════════════════════════════════════════════════════════════════════════════════
///   data/assets/pet/pets_for_client.json        74 ชนิด — vehicle_entity_type / rein_id / available_ranks
///   data/assets/pet/pet_task.json               48 งาน  — unlock_level / duration / exp / hungry_required
///   data/assets/pet/pet_exp.json                สูตร required_exp ต่อชนิด
///   data/assets/pet/pet_active_skills.json      37 สกิล × แรงก์ — action_set / cooltime
///   data/assets/pet/pet_active_skill_conditions.json  **มี weight ของกาชาสกิลจริง** + entity_type + tag_condition
///   data/assets/entity_types/animal.json        214 ชนิด — preferred_food_tag / tamable
///   data/assets/performance.json → reins        speed / capacity / hungry_max / hungry_velocity / size
///   data/assets/performance.json → pet_food     vigor (ค่าอิ่มที่ได้จากอาหารแต่ละชนิด)
///   data/assets/tags.json (required_performance = animal_stat)  25 แท็ก milestone จริง + max_level
///   data/assets/constants.json → pet            default_grazable_count / milestone_level / active_skill_levels …
///   data/assets/costs.json                      pet_revert_rank / pet_revert_milestone / pet_revert_active_skill
/// </summary>
public partial class Player
{
    // ══════════════════════════════════════════════════════════════════════════════════
    //  ค่าที่ "เราตั้งเอง" ของระบบนี้ — รวมไว้ที่เดียวให้ปรับง่าย
    //  (ทุกตัวในบล็อกนี้ไม่มีในข้อมูลของ NEXON — เหตุผลเขียนกำกับไว้ทีละตัว)
    // ══════════════════════════════════════════════════════════════════════════════════

    private static class PetTuning
    {
        /// <summary>
        /// **ค่าของเรา** — ขนาดกระเป๋าผู้เล่นที่ใช้ตอนดึงของออกจากกระเป๋าสัตว์
        ///
        /// ต้องเป็นเลขเดียวกับที่ Core/Player.cs:1270 SendInventory ส่งไปตอนเข้าเกม (MaxSize = 200)
        /// ไม่ใช่ 100 ตามข้อมูลจริง (entity_types/players.json → player → inventory_capacity)
        /// ถ้าใช้คนละเลข ผู้เล่นจะเจอ "กระเป๋าเต็ม" ทั้งที่จอบอกว่ายังว่าง
        /// </summary>
        public const int PlayerInventoryMaxSize = 200;

        /// <summary>
        /// **ค่าของเรา** — เลี้ยงสัตว์ได้สูงสุดกี่ตัว (Statistics → Derived.MaxTamingPet 303)
        ///
        /// ค้นทั้ง /assets แล้วไม่มีค่าฐาน มีแต่โมดิฟายเออร์ <c>max_taming_pet_plus</c> ที่ default 0
        /// (แปลว่าฐานอยู่บนเซิร์ฟจริงของ NEXON) ⇒ ตั้ง 10 ไว้ก่อน = สองเท่าของ
        /// <c>constants.pet.default_grazable_count</c> (5) ซึ่งเป็นค่าจริงตัวเดียวที่ใกล้เคียงเรื่องนี้
        ///
        /// ⚠️ **ไม่ส่งค่านี้ = ป้ายนับสัตว์โชว์ "N / 0" และปุ่มเอาสัตว์ออกจากทุ่งเด้งหน้าต่าง
        /// "สลับตัว" ตลอด** (client/Durango.UI/PetGroup.cs:375,416)
        /// </summary>
        public const int MaxTamingPet = 10;

        /// <summary>
        /// **ค่าของเรา** — อายุขัยสัตว์เลี้ยง (วัน) ก่อนเข้าสถานะ "แก่" (PetStats.IsOld)
        ///
        /// ค้นข้อมูลทั้งชุดแล้วไม่พบตัวเลขอายุขัยฐานเลย (มีแค่แท็ก life_span_plus_5 ใน tags.json
        /// ที่ "เพิ่ม" อายุ 5 หน่วย/เลเวลแท็ก แปลว่าฐานต้องมีอยู่ที่ไหนสักที่ที่ไม่ได้แจกมากับ /assets)
        /// ⇒ ตั้ง 30 วันไว้ก่อน แล้วบวกด้วยแท็ก life_span_plus_5 ตามสูตรจริงในไฟล์
        ///
        /// ⚠️ **หน่วยที่คิดในเซิร์ฟกับหน่วยที่ส่งขึ้นสายคนละหน่วยกัน** — คิดเป็น "วัน" ที่นี่
        /// (หน่วยเดียวกับที่แท็กในไฟล์ใช้) แล้ว <see cref="PetFactory.DerivedOf"/> แปลงเป็นวินาที
        /// ครั้งเดียวตอนท้าย เพราะช่อง <c>Derived.LifeSpan</c> ที่ฝั่งเกมอ่านเป็น **วินาที**:
        ///   client/Durango.UI/ItemInfoView.cs:392       ตั้งชื่อตัวแปรว่า seconds แล้วยัดเข้า TimedeltaFormatter
        ///   client/Durango.UI.Popup/PetItemInteractionPopup.cs:664  เอาไป Math.Min กับ (AgingUntil − GrazedAt)
        ///   client/TimedeltaFormatter.cs:34-54          ตารางหน่วยเริ่มที่ 86400 วิ/วัน ⇒ อินพุตเป็นวินาที
        /// ⇒ ส่งเลข 30 ดิบ ๆ ขึ้นไป ป้ายอายุขัยจะขึ้นว่า "30초" (30 วินาที) และเพราะ Math.Min
        /// กับเวลาที่เหลือจริง (2,592,000 วิ) จอจะค้างที่ "30초 MAX" ตลอดกาลทั้งที่สัตว์ยังไม่แก่
        /// </summary>
        public const double LifeSpanDaysBase = 30.0;

        /// <summary>ตัวแปลงหน่วยวัน → วินาที (ไม่ใช่ค่าที่ตั้งเอง — เป็นนิยามของหน่วยเวลา)</summary>
        public const double SecondsPerDay = 24.0 * 3600.0;

        /// <summary>
        /// **ค่าของเรา** — จำนวนช่อง milestone ที่สัตว์ได้ตามแรงก์
        ///
        /// ข้อมูลจริงมีแค่ "ถ้ามี N ช่อง จะปลดที่เลเวลไหนบ้าง" (constants.json → pet → milestone_level
        /// เป็น dict "1".."6") แต่ **ไม่มีตารางบอกว่าแรงก์ไหนได้กี่ช่อง** และไม่มีตารางความน่าจะเป็นด้วย
        /// (ของจริงเซิร์ฟ NEXON เป็นคนสุ่มตอน FinishDomestication แล้วส่งมาใน DomesticationResult)
        /// ⇒ ผูกตรง ๆ กับแรงก์: D=1 · C=2 · B=3 · A=4 · S=6 (S ได้ครบทุกช่วงเลเวลใน milestone_level)
        /// </summary>
        public static int MilestoneCountOfRank(PetRank rank) => rank switch
        {
            PetRank.D => 1,
            PetRank.C => 2,
            PetRank.B => 3,
            PetRank.A => 4,
            PetRank.S => 6,
            _ => 1
        };

        /// <summary>
        /// **ค่าของเรา** — น้ำหนักสุ่มแรงก์ตอน RevertPetRank
        ///
        /// รายชื่อแรงก์ที่สัตว์ชนิดนั้น "เป็นได้" มาจากข้อมูลจริง (pets_for_client.json → available_ranks)
        /// แต่ความน่าจะเป็นไม่มีในข้อมูล ⇒ ใช้น้ำหนักลดหลั่นตามแรงก์: D 40 · C 30 · B 18 · A 9 · S 3
        /// (แรงก์ที่ไม่อยู่ใน available_ranks ของชนิดนั้นจะถูกตัดออกก่อนสุ่มเสมอ)
        /// </summary>
        public static int RankWeight(PetRank rank) => rank switch
        {
            PetRank.D => 40,
            PetRank.C => 30,
            PetRank.B => 18,
            PetRank.A => 9,
            PetRank.S => 3,
            _ => 0
        };

        /// <summary>
        /// **ค่าของเรา** — น้ำหนักสุ่ม "ระดับแท็ก" ที่ได้จาก milestone หนึ่งครั้ง
        ///
        /// ตัวแท็ก (25 ตัว) และเพดาน max_level = 10 เป็นข้อมูลจริงจาก tags.json
        /// (required_performance = "animal_stat") แต่ *ระดับที่ได้ต่อครั้ง* ไม่มีในข้อมูล
        /// ⇒ ให้ระดับ 1-3 แบบเอียงไปทางน้อย: ระดับ 1 น้ำหนัก 60 · ระดับ 2 = 30 · ระดับ 3 = 10
        /// </summary>
        public static readonly (int Level, int Weight)[] MilestoneTagLevelWeights =
        {
            (1, 60), (2, 30), (3, 10)
        };

        /// <summary>
        /// **ค่าของเรา** — จำนวนแท็กที่เอามาให้เลือกดูตอน GetMilestoneCandidate
        /// (ข้อมูลจริงไม่ได้บอกว่าโชว์กี่ตัว — client วาดตามจำนวนที่ส่งไป)
        /// </summary>
        public const int MilestoneCandidateCount = 5;

        /// <summary>ตัวสุ่มของระบบนี้ — ตัวเดียวทั้งโปรเซส เพราะ handler ทุกตัวอยู่ main loop เส้นเดียว</summary>
        public static readonly Random Rng = new();

        /// <summary>สุ่มแบบถ่วงน้ำหนัก — คืน index ที่เลือกได้ (-1 = น้ำหนักรวมเป็นศูนย์)</summary>
        public static int PickWeighted(IReadOnlyList<int> weights)
        {
            int total = 0;
            for (int i = 0; i < weights.Count; i++) total += Math.Max(0, weights[i]);
            if (total <= 0) return -1;
            int roll = Rng.Next(total);
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= Math.Max(0, weights[i]);
                if (roll < 0) return i;
            }
            return weights.Count - 1;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ลงทะเบียน handler
    // ══════════════════════════════════════════════════════════════════════════════════

    private void RegisterAnimalHandlers()
    {
        // ── ชั้น A: เปิดหน้าจอสัตว์เลี้ยงแล้วต้องไม่ค้าง ──────────────────────────────────
        _connection.Recv(delegate(GetPetsInfo msg, PacketHeader header)
        {
            SendPetsInfo(header.Seq);
        });
        // GetGrazedPets (29912240) เป็นของ Core/Player.cs:489 อยู่แล้ว (ตอบจาก World.GrazedPetList)
        // ไฟล์นั้นลงทะเบียนก่อน RegisterSystemHandlers() (Player.cs:522) ⇒ เช็คแล้วค่อยเสียบ
        // ห้ามทับ เพราะ Connection.RegisterMessageHandlerToRegistry จะ "แทนที่" ของเดิมเงียบ ๆ
        if (!_connection.HasHandler(GetGrazedPets.TypeCode))
        {
            _connection.Recv(delegate(GetGrazedPets msg, PacketHeader header)
            {
                Send(new GrazedPets { Data = _world.GetGrazedPets().ToArray() }, header.Seq);
            });
        }
        _connection.Recv(delegate(GetPreviewPet msg, PacketHeader header)
        {
            HandleGetPreviewPetMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(GetPetInventory msg, PacketHeader header)
        {
            HandleGetPetInventoryMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(GetAvailableTask msg, PacketHeader header)
        {
            HandleGetAvailableTaskMsg(msg, header.Seq);
        });
        // GetEncyclopedia (37125) จริง ๆ เป็น "สารานุกรมการเพาะปลูก" ไม่ใช่ของสัตว์
        // (client/FarmingEncyclopediaSystem.cs:20-28 ส่ง EncyclopediaCategory = Farming ตอน OnReady
        //  แล้วเก็บ msg.Data ไว้ใน _farmingEncyclopediaDictionary)
        // เอกสารจัดไว้ชั้น A ของงานนี้ จึงตอบให้ แต่กันทับระบบเพาะปลูกที่อาจมาทีหลัง
        if (!_connection.HasHandler(GetEncyclopedia.TypeCode))
        {
            _connection.Recv(delegate(GetEncyclopedia msg, PacketHeader header)
            {
                HandleGetEncyclopediaMsg(msg, header.Seq);
            });
        }

        // ── ชั้น B: เรียกออกมา / เก็บ / ขี่ / ตั้งชื่อ ────────────────────────────────────
        _connection.Recv(delegate(SpawnPet msg, PacketHeader header)
        {
            HandleSpawnPetMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(ReturnPet msg, PacketHeader header)
        {
            HandleReturnPetMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(Mount msg, PacketHeader header)
        {
            HandleMountPetMsg(boarding: true);
        });
        _connection.Recv(delegate(Unmount msg, PacketHeader header)
        {
            HandleMountPetMsg(boarding: false);
        });
        _connection.Recv(delegate(RenamePet msg, PacketHeader header)
        {
            HandleRenamePetMsg(msg, header.Seq);
        });

        // ── ชั้น C: กระเป๋าสัตว์ (ทำได้จริง) + กรง (ตอบ Abort) ───────────────────────────
        _connection.Recv(delegate(PutInItemsIntoPet msg, PacketHeader header)
        {
            HandlePutInItemsIntoPetMsg(msg);
        });
        _connection.Recv(delegate(TakeOutItemsFromPet msg, PacketHeader header)
        {
            HandleTakeOutItemsFromPetMsg(msg);
        });
        // ให้อาหารสัตว์ที่เรียกออกมาข้างตัว (ไม่ผ่านกรง) — client/PetManager.cs:869-882
        _connection.Recv(delegate(Feeding msg, PacketHeader header)
        {
            HandleFeedingMsg(msg, header.Seq);
        });

        // ── ชั้น D: ปล่อย/ชุบชีวิต/เล็มหญ้า/บันทึกการค้นพบ ──────────────────────────────
        _connection.Recv(delegate(ReleasePet msg, PacketHeader header)
        {
            HandleReleasePetMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(ResurrectPet msg, PacketHeader header)
        {
            HandleResurrectPetMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(GrazePets msg, PacketHeader header)
        {
            HandleGrazePetsMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(DiscoverAnimal msg, PacketHeader header)
        {
            HandleDiscoverAnimalMsg(msg, header.Seq);
        });

        // ── ชั้น E: กาชา (milestone / active skill / rank) ──────────────────────────────
        _connection.Recv(delegate(GetMilestoneCandidate msg, PacketHeader header)
        {
            HandleGetMilestoneCandidateMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(PickMilestone msg, PacketHeader header)
        {
            HandlePickMilestoneMsg(msg.PetId, reroll: false, header.Seq);
        });
        _connection.Recv(delegate(PickMilestoneAgain msg, PacketHeader header)
        {
            HandlePickMilestoneMsg(msg.PetId, reroll: true, header.Seq);
        });
        _connection.Recv(delegate(AcceptMilestone msg, PacketHeader header)
        {
            HandleAcceptMilestoneMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(DrawActiveSkill msg, PacketHeader header)
        {
            HandleDrawActiveSkillMsg(msg.PetId, null, header.Seq);
        });
        _connection.Recv(delegate(RedrawActiveSkill msg, PacketHeader header)
        {
            HandleDrawActiveSkillMsg(msg.PetId, msg.Skill, header.Seq);
        });
        _connection.Recv(delegate(RevertPetRank msg, PacketHeader header)
        {
            HandleRevertPetRankMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(AcceptPetRank msg, PacketHeader header)
        {
            HandleAcceptPetRankMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(UsePetActiveSkill msg, PacketHeader header)
        {
            HandleUsePetActiveSkillMsg(msg, header.Seq);
        });

        // ── กลุ่มที่ผูกกับ "กรง" ทั้งหมด — ยังทำไม่ได้จริง จึงตอบ Abort ──────────────────
        RegisterCageHandlersAsUnavailable();
    }

    /// <summary>
    /// ทุกตัวในกลุ่มนี้ต้องเขียน/อ่าน ArtifactState.Cage หรือ ArtifactState.DomesticCage
    /// ซึ่งไฟล์นี้เขียนไม่ได้ (ดูข้อจำกัดข้อ 2 ที่หัวไฟล์) ⇒ **ห้ามตอบ OK เด็ดขาด**
    ///
    /// ฝั่งเกมทุกตัวใช้ <c>.All(Packet.IsSuccess)</c> (client/PetManager.cs:890-905, :1035-1132)
    /// ⇒ Abort = onResult(false) = UI แจ้งว่าทำไม่สำเร็จและไม่ลบไอเทมทิ้ง ซึ่งตรงกับความจริง
    /// ส่วน Text จะถูกเอาไปขึ้นเป็นข้อความระบบ (client/GameManager.cs:303-306 DefaultAbortHandler)
    ///
    /// วิธีปลดล็อกเมื่อแก้ไฟล์นอกขอบเขตได้แล้ว: (1) เพิ่ม Interaction.Cage / OpenDomesticCage ใน
    /// HandleTouchMsg (2) เพิ่ม API เขียน ArtifactState.Cage/DomesticCage ใน ArtifactManager
    /// (3) เพิ่มช่องเซฟใน WorldContext แล้วค่อยแทน handler กลุ่มนี้ทีละตัว
    /// </summary>
    private void RegisterCageHandlersAsUnavailable()
    {
        const string cageMsg = "ยังไม่มีระบบกรงสัตว์";
        const string tameMsg = "ยังไม่มีระบบทำให้เชื่อง";

        _connection.Recv(delegate(PutInCage msg, PacketHeader header)
        {
            Send(new Abort { Text = cageMsg }, header.Seq);
        });
        _connection.Recv(delegate(FeedInCage msg, PacketHeader header)
        {
            Send(new Abort { Text = cageMsg }, header.Seq);
        });
        _connection.Recv(delegate(StartPetTask msg, PacketHeader header)
        {
            Send(new Abort { Text = cageMsg }, header.Seq);
        });
        _connection.Recv(delegate(CancelPetTask msg, PacketHeader header)
        {
            Send(new Abort { Text = cageMsg }, header.Seq);
        });
        _connection.Recv(delegate(FinishPetTask msg, PacketHeader header)
        {
            Send(new Abort { Text = cageMsg }, header.Seq);
        });
        _connection.Recv(delegate(PutInReinsToCage msg, PacketHeader header)
        {
            Send(new Abort { Text = tameMsg }, header.Seq);
        });
        _connection.Recv(delegate(StartDomestication msg, PacketHeader header)
        {
            Send(new Abort { Text = tameMsg }, header.Seq);
        });
        _connection.Recv(delegate(CancelDomestication msg, PacketHeader header)
        {
            Send(new Abort { Text = tameMsg }, header.Seq);
        });
        // PutItemsForDomestication ยิงทิ้งไม่รอ reply (client/PetManager.cs:1112-1121)
        // แต่ผู้เล่นเป็นคนกดใส่อาหารเอง ⇒ ต้องบอกว่าไม่สำเร็จ ไม่งั้นเข้าใจว่าอาหารถูกใช้ไปแล้ว
        _connection.Recv(delegate(PutItemsForDomestication msg, PacketHeader header)
        {
            Send(new Abort { Text = tameMsg });
        });
        // FinishDomestication รอ DomesticationResult โดยเฉพาะ แต่มี .Rest(→ onResult(null)) รองรับ
        // (client/PetManager.cs:1003-1016) ⇒ Abort ตกเข้า Rest แล้ว UI ปิดตัวเอง
        _connection.Recv(delegate(FinishDomestication msg, PacketHeader header)
        {
            Send(new Abort { Text = tameMsg }, header.Seq);
        });
        // TakeOutFromCage (810) / TakeOutReinFromCage (694359) ถูก Core/Player.Inventory.cs:846-855
        // จองไปแล้ว (ตอบ Abort เหมือนกัน) ⇒ ไม่ทับ
        if (!_connection.HasHandler(TakeOutFromCage.TypeCode))
        {
            _connection.Recv(delegate(TakeOutFromCage msg, PacketHeader header)
            {
                Send(new Abort { Text = cageMsg }, header.Seq);
            });
        }
        if (!_connection.HasHandler(TakeOutReinFromCage.TypeCode))
        {
            _connection.Recv(delegate(TakeOutReinFromCage msg, PacketHeader header)
            {
                Send(new Abort { Text = tameMsg }, header.Seq);
            });
        }
        // ReleaseReinFromCage (ปล่อยสัตว์ป่าที่ยังไม่เชื่องออกจากกรง) — client/PetManager.cs:661-679
        _connection.Recv(delegate(ReleaseReinFromCage msg, PacketHeader header)
        {
            Send(new Abort { Text = tameMsg }, header.Seq);
        });
        // ReinifyPet — แปลงสัตว์เลี้ยงกลับเป็นบังเหียน
        // ⚠️ **จงใจไม่ทำ**: ทางกลับ (บังเหียน → สัตว์) ต้องผ่านกรงทำให้เชื่องซึ่งยังใช้ไม่ได้
        // ⇒ ถ้าทำจริงตอนนี้ = สัตว์หายถาวรโดยผู้เล่นไม่รู้ตัว ยอมให้ปุ่มไม่ทำงานดีกว่า
        // (ต่างจาก ReleasePet ที่ผู้เล่นตั้งใจทิ้งและมีกล่องยืนยันของเกมเอง จึงทำให้จริง)
        _connection.Recv(delegate(ReinifyPet msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังแปลงสัตว์เป็นบังเหียนไม่ได้ (ยังไม่มีทางเอากลับคืน)" }, header.Seq);
        });
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ชั้น A — รายการสัตว์เลี้ยง
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ตอบ PetsInfo (29912233) — หัวใจของหน้าจอสัตว์เลี้ยง
    ///
    /// ฟิลด์ทั้งสาม client เอาไปใช้ตรง ๆ (client/Durango.UI/PetGroup.cs:159-178, :332-405):
    ///   Pets          = สัตว์ที่ "ดูแลอยู่" (แท็บซ้าย)
    ///   GrazedPets    = สัตว์ที่ปล่อยเล็มหญ้าไว้ (แท็บขวา)
    ///   GrazableCount = เพดานช่องเล็มหญ้า — ถ้าเต็มแล้ว UI จะเด้งหน้าต่างให้เลือก "สลับตัว"
    ///                   (ค่าจริง constants.json → pet → default_grazable_count = 5)
    /// ทั้งสองอาเรย์ว่างได้ — PetGroup:163-166 จะโชว์แผง "ยังไม่มีสัตว์" ให้เอง ไม่ค้าง
    /// </summary>
    private void SendPetsInfo(uint seq)
    {
        List<PetStore.Entry> mine = PetStore.Of(EntityId);
        Send(new PetsInfo
        {
            Pets = new Pets { Data = mine.Where(e => !e.Grazing).Select(e => e.Pet).ToArray() },
            GrazedPets = new Pets { Data = mine.Where(e => e.Grazing).Select(e => e.Pet).ToArray() },
            GrazableCount = PetTables.Constants.GrazableCount
        }, seq);
    }

    /// <summary>
    /// พรีวิวค่าสถานะสัตว์ก่อนซื้อ — GetPreviewPet (181120) ตอบ Pet (1097965)
    ///
    /// จุดเรียกจุดเดียว: client/Durango.UI.Popup/ShopBuyConfirmPopup.cs:125-140 ตอนเอาเมาส์ชี้
    /// บังเหียนในร้าน → ส่ง (PetEntityType, Rank = A, Level = ระดับของไอเทม) มาถามค่าสถานะ
    /// แล้วเอาไปโชว์ใน ItemInfoTooltip · มี .Rest(→ onResult(null)) รองรับกรณีตอบ Abort อยู่แล้ว
    /// แต่ถ้า "ไม่ตอบเลย" tooltip จะไม่โผล่ตลอดกาลโดยไม่มี error
    /// </summary>
    private void HandleGetPreviewPetMsg(GetPreviewPet msg, uint seq)
    {
        Messages.Pet? pet = PetFactory.Build(msg.PetEntityType, msg.Rank, Math.Max(1, msg.Level), EntityId);
        if (!pet.HasValue)
        {
            Send(new Abort { Text = "ไม่รู้จักสัตว์ชนิดนี้" }, seq);
            return;
        }
        Send(pet.Value, seq);
    }

    /// <summary>
    /// ของในกระเป๋าสัตว์ — GetPetInventory (49823) ตอบ PetInventory (574978)
    ///
    /// client/Durango.Logic.Item/Inventory.cs:165-170 ส่งตัวนี้ตอน Request() แล้วตั้ง
    /// State = Loading — มีแต่ Requested() (เรียกจาก UpdateTrackingInventory ตอนได้ PetInventory)
    /// ที่ปลดล็อกได้ ⇒ **ไม่ตอบ = หน้ากระเป๋าสัตว์ค้างที่ Loading ตลอดกาล**
    /// client เทียบ msg.Inven.EntityId กับ OwnerId (client/InventorySystem.cs:218-224)
    /// ⇒ ต้องใส่ EntityId ให้ตรงกับ PetId ที่ถามมา ไม่งั้นข้อมูลถูกทิ้งเงียบ ๆ
    /// </summary>
    private void HandleGetPetInventoryMsg(GetPetInventory msg, uint seq)
    {
        Send(BuildPetInventory(msg.EntityId), seq);
    }

    private PetInventory BuildPetInventory(string petId)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, petId);
        Item[] items = entry?.Bag.ToArray() ?? Array.Empty<Item>();
        return new PetInventory
        {
            Inven = new Messages.Inventory
            {
                EntityId = petId,
                InventoryItems = new InventoryItems { EntityId = petId, Items = items },
                InventoryInfos = new InventoryInfos
                {
                    EntityId = petId,
                    MaxSize = entry == null ? 0 : PetCapacityOf(entry),
                    LockedItemIds = Array.Empty<string>(),
                    ItemOrder = null,
                    ProtectedItems = new ProtectedItems { ItemIds = Array.Empty<string>() }
                },
                // ดู Core/Player.Wallet.cs — เซิร์ฟนี้ใช้สกุลเดียวคือ T Stone
                Wallet = BuildWallet()
            }
        };
    }

    /// <summary>ความจุกระเป๋าสัตว์ = Derived.InventoryCapacity ที่คิดไว้ตอนสร้าง Pet (มาจาก performance.reins.capacity)</summary>
    private static int PetCapacityOf(PetStore.Entry entry)
    {
        return entry.Pet.Statistics.DerivedAbilities != null &&
               entry.Pet.Statistics.DerivedAbilities.TryGetValue(Derived.InventoryCapacity, out float cap)
            ? (int)cap
            : 0;
    }

    /// <summary>
    /// รายการงานที่สั่งสัตว์ตัวนี้ทำได้ — GetAvailableTask (65106) ตอบ AvailableTask (65107)
    ///
    /// client/Durango.UI.Popup/SelectPetTaskPopup.cs:104-120 เปิดหน้าต่างแล้วยิงตัวนี้
    /// เก็บ result.Value.Tasks ไว้ก่อนจะวาดรายการ — ไม่ตอบ = รายการงานว่างค้างไม่มี error
    /// (มี .Rest → onResult(null) รองรับ Abort แต่ก็จะไม่มีอะไรให้เลือกอยู่ดี)
    ///
    /// เงื่อนไขที่กรองได้จากข้อมูลจริง: pet_task.json → unlock_level ≤ เลเวลสัตว์
    /// ⚠️ ของจริงน่าจะกรองด้วยชนิดกรง (performance.json → cage → tag_to_generator) ด้วย แต่ยังยืนยัน
    /// จากซอร์สเกมไม่ได้ว่ากรองที่เซิร์ฟหรือที่ client และตอนนี้กรงยังใช้ไม่ได้อยู่แล้ว จึงยังไม่กรอง
    /// </summary>
    private void HandleGetAvailableTaskMsg(GetAvailableTask msg, uint seq)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, msg.PetId);
        int level = entry?.Pet.Statistics.Level ?? 0;
        Send(new AvailableTask
        {
            Tasks = PetTables.Tasks
                .Where(pair => pair.Value.UnlockLevel <= level)
                .Select(pair => pair.Key)
                .ToArray()
        }, seq);
    }

    /// <summary>
    /// สารานุกรม — GetEncyclopedia (37125) ตอบ FarmingEncyclopedia (37126)
    ///
    /// ยิงมาครั้งเดียวตอนเข้าเกม (client/FarmingEncyclopediaSystem.cs:20-28 ใน AddOnReady)
    /// client เก็บ msg.Data ลง _farmingEncyclopediaDictionary แล้วทุกที่ที่อ่านต่อจะเช็ค null ก่อน
    /// (:33 OnFarmingEncyclopediaProgress return ทันทีถ้ายัง null)
    /// ⇒ ตอบ dict ว่าง = ระบบพร้อมรับความคืบหน้าแต่ยังไม่มีข้อมูล ซึ่งตรงกับความจริง
    /// **ห้ามส่ง Data = null** เพราะ GetFarmingEncyclopediaDataList() คืน null ให้ผู้เรียกวน foreach
    /// </summary>
    private void HandleGetEncyclopediaMsg(GetEncyclopedia msg, uint seq)
    {
        if (msg.EncyclopediaCategory != EncyclopediaType.Farming)
        {
            // enum มีค่าเดียวคือ Farming (Shared.Encyclopedia/EncyclopediaType.cs) — ตัวอื่นคือของผิด
            Send(new Abort { Text = "ไม่รู้จักสารานุกรมหมวดนี้" }, seq);
            return;
        }
        Send(new FarmingEncyclopedia
        {
            Data = new Dictionary<string, FarmingEncyclopediaData>()
        }, seq);
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ชั้น B — เรียกออกมา / เก็บ / ขี่ / ตั้งชื่อ
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// เรียกสัตว์ออกมาข้างตัว — SpawnPet (923570)
    ///
    /// client/PetManager.cs:748-767 รอ OK ที่ seq นี้ แต่ตัวที่ทำให้สัตว์โผล่บนจอจริง ๆ คือ
    /// AppearPet (94) ที่เซิร์ฟ push ตามมา (client/PetManager.cs:59 On&lt;AppearPet&gt;)
    /// ⇒ ต้องส่งทั้งคู่ · AppearPet.PetData ห้ามเป็น null ไม่งั้น OnAppearPetMsg:136 return ทิ้ง
    ///
    /// ตำแหน่งเกิด: client คำนวณเองจากตัวผู้เล่น (PetManager.cs:158-160) แล้ว Driver.SetVehicle
    /// ดึงมาชิดตัว ⇒ Move ที่ส่งไปเป็นแค่จุดตั้งต้นให้ผู้เล่นคนอื่นเห็น
    /// </summary>
    private void HandleSpawnPetMsg(SpawnPet msg, uint seq)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, msg.PetId);
        if (entry == null)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้" }, seq);
            return;
        }
        if (entry.Grazing)
        {
            Send(new Abort { Text = "สัตว์ที่ปล่อยเล็มหญ้าอยู่เรียกออกมาไม่ได้" }, seq);
            return;
        }
        // เรียกตัวใหม่ = เก็บตัวเก่าก่อน (เกมให้มีสัตว์ข้างตัวได้ตัวเดียว — PetManager._playerPetId)
        foreach (PetStore.Entry other in PetStore.Of(EntityId))
        {
            if (!ReferenceEquals(other, entry) && other.Pet.IsSpawned) DespawnPet(other);
        }
        entry.Pet.IsSpawned = true;
        entry.Pet.IsBoarding = false;
        Send(default(OK), seq);
        _world.BroadCast(new AppearPet
        {
            EntityId = entry.Pet.EntityId,
            EntityType = entry.Pet.EntityType,
            IsAlive = PetIsAlive(entry),
            Move = new Move
            {
                EntityId = entry.Pet.EntityId,
                Movements = new[]
                {
                    new Movement
                    {
                        PlaybackRate = entry.Pet.Stat.PlaybackRate,
                        Path = new[] { new Location { Position = PlayerPosition() } }
                    }
                }
            },
            Survival = new Survival
            {
                EntityId = entry.Pet.EntityId,
                Life = entry.Pet.Stat.Life,
                Gauges = new Dictionary<string, Gauge> { { "hungry", entry.Pet.Stat.Hungry } }
            },
            PetData = entry.Pet
        });
    }

    /// <summary>เรียกสัตว์กลับ — ReturnPet (808) · client/PetManager.cs:769-784 รอ OK</summary>
    private void HandleReturnPetMsg(ReturnPet msg, uint seq)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, msg.PetId);
        if (entry == null || !entry.Pet.IsSpawned)
        {
            Send(new Abort { Text = "สัตว์ตัวนี้ไม่ได้อยู่ข้างตัว" }, seq);
            return;
        }
        DespawnPet(entry);
        Send(default(OK), seq);
    }

    /// <summary>
    /// เก็บสัตว์เข้าที่ + แจ้งทุกคนในโลก — DisappearPet (198246)
    /// client/PetManager.cs:319-360 เอา TamerEntityId ไปหาว่าใครกำลังขี่อยู่ แล้วสั่งลงจากหลังให้เรียบร้อย
    /// ⇒ ต้องใส่ TamerEntityId ทุกครั้ง ไม่งั้นผู้เล่นที่ขี่อยู่จะค้างอยู่บนสัตว์ที่หายไปแล้ว
    /// </summary>
    private void DespawnPet(PetStore.Entry entry)
    {
        entry.Pet.IsSpawned = false;
        entry.Pet.IsBoarding = false;
        _world.BroadCast(new DisappearPet
        {
            EntityId = entry.Pet.EntityId,
            TamerEntityId = entry.Pet.TamerEntityId
        });
    }

    /// <summary>
    /// ขึ้น/ลงหลังสัตว์ — Mount (802) / Unmount (803) ทั้งคู่ไม่มีฟิลด์เลย
    ///
    /// เพราะเกมรู้อยู่แล้วว่าตัวไหน: มันยิงจาก interaction บนสัตว์ที่ตัวเองเรียกออกมา
    /// (client/PetManager.cs:417-464 · ตัวเลือก Mount/Dismount สร้างที่ client เอง —
    ///  client/VehiclePet.cs:221-231 ContextActionFinder) ⇒ ฝั่งเราหา "สัตว์ที่ผู้เล่นคนนี้เรียกออกมา"
    ///
    /// ไม่ต้องตอบ OK: ฝั่งเกมส่งแบบไม่รอ reply แต่รอ push ของ Pet (1097965) เพื่ออัปเดตสถานะ
    /// (client/PetManager.cs:61-67 On&lt;Pet&gt; → ProcessPetMsg) ⇒ ผลักตัวที่เปลี่ยนไปให้ทั้งโลก
    /// </summary>
    private void HandleMountPetMsg(bool boarding)
    {
        PetStore.Entry entry = PetStore.Of(EntityId).FirstOrDefault(e => e.Pet.IsSpawned);
        if (entry == null) return;
        if (boarding && !PetIsAlive(entry)) return;      // client กันไว้แล้ว แต่กันซ้ำที่เซิร์ฟด้วย
        entry.Pet.IsBoarding = boarding;
        _world.BroadCast(entry.Pet);
    }

    /// <summary>ตั้งชื่อสัตว์ — RenamePet (804) · client/PetManager.cs:786-800 รอ OK แล้วรีเฟรชรายการ</summary>
    private void HandleRenamePetMsg(RenamePet msg, uint seq)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, msg.PetId);
        if (entry == null)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้" }, seq);
            return;
        }
        if (string.IsNullOrWhiteSpace(msg.Name))
        {
            Send(new Abort { Text = "ชื่อว่างไม่ได้" }, seq);
            return;
        }
        entry.Pet.Name = msg.Name.Trim();
        Send(default(OK), seq);
        _world.BroadCast(entry.Pet);
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ชั้น C — กระเป๋าสัตว์ + ให้อาหาร
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ฝากของเข้ากระเป๋าสัตว์ — PutInItemsIntoPet (806)
    ///
    /// client/InventorySystem.cs:904-914 ส่งแล้วไม่รอ reply — มันรอ push สองใบ:
    /// InventoryUpdated (ของหายจากกระเป๋าเรา) + PetInventory (ของโผล่ในกระเป๋าสัตว์)
    ///
    /// ต่างจาก PutInItem ของตู้สิ่งปลูกสร้าง (ที่ Player.Inventory.cs ตอบ Abort ไว้) ตรงที่
    /// **ทางกลับเป็นของไฟล์นี้เอง** (TakeOutItemsFromPet 807) ⇒ ของไม่มีทางค้างจนหาย ทำจริงได้
    /// </summary>
    private void HandlePutInItemsIntoPetMsg(PutInItemsIntoPet msg)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, msg.PetId);
        if (entry == null)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้" });
            return;
        }
        int free = PetCapacityOf(entry) - entry.Bag.Sum(it => Math.Max(1, it.Size));
        var moved = new List<Item>();
        foreach (string id in msg.ItemIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(id)) continue;
            int idx = _context.InventoryItems.FindIndex(it => it.Id == id);
            if (idx < 0) continue;
            Item item = _context.InventoryItems[idx];
            int size = Math.Max(1, item.Size);
            if (size > free) break;                     // เต็มแล้ว — ที่ย้ายไปก่อนหน้ายังนับว่าสำเร็จ
            free -= size;
            _context.InventoryItems.RemoveAt(idx);
            entry.Bag.Add(item);
            moved.Add(item);
        }
        if (moved.Count == 0)
        {
            Send(new Abort { Text = "กระเป๋าสัตว์เต็ม" });
            return;
        }
        Send(new InventoryUpdated
        {
            EntityId = EntityId,
            RemovedItemIds = moved.Select(it => it.Id).ToArray()
        });
        SyncPetBag(entry);
        OnContextChanged();
    }

    /// <summary>เอาของออกจากกระเป๋าสัตว์ — TakeOutItemsFromPet (807) · client/InventorySystem.cs:946-953</summary>
    private void HandleTakeOutItemsFromPetMsg(TakeOutItemsFromPet msg)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, msg.PetId);
        if (entry == null)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้" });
            return;
        }
        int free = PetTuning.PlayerInventoryMaxSize - _context.InventoryItems.Sum(it => Math.Max(1, it.Size));
        var moved = new List<Item>();
        foreach (string id in msg.ItemIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(id)) continue;
            int idx = entry.Bag.FindIndex(it => it.Id == id);
            if (idx < 0) continue;
            Item item = entry.Bag[idx];
            int size = Math.Max(1, item.Size);
            if (size > free) break;
            free -= size;
            entry.Bag.RemoveAt(idx);
            _context.InventoryItems.Add(item);
            moved.Add(item);
        }
        if (moved.Count == 0)
        {
            Send(new Abort { Text = "กระเป๋าเต็ม" });
            return;
        }
        Send(new InventoryUpdated
        {
            EntityId = EntityId,
            Items = moved.ToArray()
        });
        SyncPetBag(entry);
        OnContextChanged();
    }

    /// <summary>ผลัก PetInventory + Pet ใหม่ (InventoryUsage เปลี่ยน) ให้ client วาดตาม</summary>
    private void SyncPetBag(PetStore.Entry entry)
    {
        entry.Pet.Stat.InventoryUsage = entry.Bag.Sum(it => Math.Max(1, it.Size));
        Send(BuildPetInventory(entry.Pet.EntityId));
        _world.BroadCast(entry.Pet);
    }

    /// <summary>
    /// ให้อาหารสัตว์ที่อยู่ข้างตัว — Feeding (805) · client/PetManager.cs:869-882 ใช้ .All(IsSuccess)
    /// เกมรอ push FeedingSuccess (813) ต่างหากเพื่อเล่นท่ากิน (client/PetManager.cs:69-76)
    ///
    /// ค่าอิ่มที่ได้มาจากข้อมูลจริง performance.json → pet_food → &lt;prototype&gt; → vigor
    /// ถ้าไอเทมไม่ได้อยู่ในตาราง pet_food ⇒ ใช้ constants.json → pet → default_feed_energy (ของจริง = 0)
    /// ซึ่งแปลว่า "กินได้แต่ไม่ได้อะไร" ⇒ เราปฏิเสธไปเลยดีกว่า ผู้เล่นจะได้ไม่เสียของฟรี
    ///
    /// ⚠️ ตอนนี้หน้าต่างเลือกอาหารของเกมจะว่างเปล่า เพราะมันกรองด้วย
    /// item.GetPerformanceData("pet_food") (client/Durango.UI/PetUtil.cs:128-133) แต่
    /// Core/Cheats.cs MakeItem ยังไม่ได้แนบ Performance "pet_food" ให้ไอเทม — ดูรายงานท้ายงาน
    /// </summary>
    private void HandleFeedingMsg(Feeding msg, uint seq)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, msg.PetId);
        if (entry == null)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้" }, seq);
            return;
        }
        float gained = 0f;
        var eaten = new List<string>();
        foreach (string id in msg.FoodIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(id)) continue;
            int idx = _context.InventoryItems.FindIndex(it => it.Id == id);
            if (idx < 0) continue;
            Item item = _context.InventoryItems[idx];
            float vigor = PetTables.FoodVigor(item.Prototype, item.Level);
            if (vigor <= 0f) continue;                  // ไม่ใช่อาหารสัตว์ — ไม่กิน ไม่หาย
            _context.InventoryItems.RemoveAt(idx);
            eaten.Add(id);
            gained += vigor;
        }
        if (eaten.Count == 0)
        {
            Send(new Abort { Text = "ไอเทมนี้ให้สัตว์กินไม่ได้" }, seq);
            return;
        }
        FillHungry(entry, gained);
        Send(default(OK), seq);
        Send(new InventoryUpdated { EntityId = EntityId, RemovedItemIds = eaten.ToArray() });
        Send(new FeedingSuccess { PetId = entry.Pet.EntityId });
        _world.BroadCast(entry.Pet);
        OnContextChanged();
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ชั้น D — ปล่อย / ชุบชีวิต / เล็มหญ้า / บันทึกการค้นพบ
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ปล่อยสัตว์ทิ้งถาวร — ReleasePet (74012) · client/PetManager.cs:645-659 รอ OK
    ///
    /// ทำจริง (ไม่ใช่ Abort) เพราะเป็นเจตนาของผู้เล่นตรง ๆ และเกมมีกล่องยืนยันให้ก่อนแล้ว
    /// (client/Durango.UI/PetGroup.cs:551) — ต่างจาก ReinifyPet ที่ผู้เล่นคาดว่าจะได้ของกลับคืน
    /// ของในกระเป๋าสัตว์คืนให้เท่าที่กระเป๋าผู้เล่นรับไหว — ที่เหลือหายไปพร้อมสัตว์ (แจ้งด้วยข้อความ)
    /// </summary>
    private void HandleReleasePetMsg(ReleasePet msg, uint seq)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, msg.PetId);
        if (entry == null)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้" }, seq);
            return;
        }
        if (entry.Pet.IsSpawned) DespawnPet(entry);
        if (entry.Grazing) SetGrazing(entry, false);
        List<Item> returned = ReturnBagToPlayer(entry);
        PetStore.Remove(EntityId, entry);
        Send(default(OK), seq);
        if (returned.Count > 0)
        {
            Send(new InventoryUpdated { EntityId = EntityId, Items = returned.ToArray() });
        }
        if (entry.Bag.Count > 0)
        {
            Send(new Abort { Text = $"ของในกระเป๋าสัตว์ {entry.Bag.Count} ชิ้นหายไปเพราะกระเป๋าเต็ม" });
        }
        OnContextChanged();
    }

    /// <summary>
    /// ชุบชีวิตสัตว์ที่ตาย — ResurrectPet (239187) · client/PetManager.cs:802-818 รอ OK
    /// (ถ้าตอบ RecommendedRecipes แทน เกมจะเด้งหน้าต่างบอกว่าต้องใช้ไอเทมอะไร — ดูหมายเหตุล่าง)
    ///
    /// ไอเทมที่ใช้ได้มาจากข้อมูลจริง constants.json → pet → resurrection_tags = ["medicine_animal"]
    /// เกมไม่ได้ส่ง ItemId มาให้ ⇒ เซิร์ฟเป็นคนเลือกไอเทมที่มีแท็กนั้นในกระเป๋าเอง (ตัวแรกที่เจอ)
    /// ไม่มีของ = ตอบ Abort · **ห้ามตอบ OK** ไม่งั้นผู้เล่นเห็นสัตว์ฟื้นแล้วหายไปตอนต่อใหม่
    ///
    /// หมายเหตุ: ตอบ RecommendedRecipes จะดีกว่า (บอกผู้เล่นว่าต้องคราฟต์อะไร) แต่ยังยืนยันไม่ได้ว่า
    /// ต้องใส่ RecipeIds ตัวไหนบ้าง (ต้องไล่ item/recipes.json หาสูตรที่ผลลัพธ์ติดแท็กนี้) จึงใช้ Abort ไปก่อน
    /// </summary>
    private void HandleResurrectPetMsg(ResurrectPet msg, uint seq)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, msg.PetId);
        if (entry == null)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้" }, seq);
            return;
        }
        if (PetIsAlive(entry))
        {
            Send(new Abort { Text = "สัตว์ตัวนี้ยังไม่ตาย" }, seq);
            return;
        }
        int idx = _context.InventoryItems.FindIndex(it => ItemHasAnyTag(it, PetTables.Constants.ResurrectionTags));
        if (idx < 0)
        {
            Send(new Abort { Text = "ต้องใช้ยาสัตว์ (medicine_animal) ในการชุบชีวิต" }, seq);
            return;
        }
        string usedId = _context.InventoryItems[idx].Id;
        _context.InventoryItems.RemoveAt(idx);
        // ฟื้นมาพร้อมหลอดชีวิตเต็ม + หลอดอิ่มเต็ม (ของจริงไม่ได้บอกสัดส่วน — ให้เต็มไปเลยเพื่อไม่ให้ตายซ้ำทันที)
        double now = Times.UnixTimeNow();
        entry.Pet.Stat.Life = new Gauge(entry.LifeMax, 0f, new[] { new GaugeNode(now, entry.LifeMax) });
        FillHungry(entry, entry.HungryMax);
        Send(default(OK), seq);
        Send(new InventoryUpdated { EntityId = EntityId, RemovedItemIds = new[] { usedId } });
        _world.BroadCast(entry.Pet);
        OnContextChanged();
    }

    /// <summary>
    /// ปล่อย/เก็บสัตว์เล็มหญ้า — GrazePets (800000) ตอบ PetsInfo
    ///
    /// **PetIdsToGraze คือ "ชุดใหม่ทั้งชุด" ไม่ใช่ตัวที่เพิ่มเข้ามา** — ยืนยันจาก
    /// client/Durango.UI/PetGroup.cs:341-343 (เอาของเดิมทั้งหมด .Concat(ตัวใหม่)) และ :378-382
    /// (เอาของเดิมทั้งหมดแล้ว .Where(id != ตัวที่เอาออก)) ⇒ ต้องทำ diff เอง ไม่ใช่ add อย่างเดียว
    ///
    /// เพดานช่องจาก constants.json → pet → default_grazable_count (=5)
    /// สัตว์ที่ถูกปล่อยจะถูกใส่เข้า World.GrazedPetList ด้วย เพื่อให้โผล่ในโลกจริงและให้
    /// GetGrazedPets ของ Core/Player.cs:489 ตอบได้ตรงกัน
    /// </summary>
    private void HandleGrazePetsMsg(GrazePets msg, uint seq)
    {
        var wanted = new HashSet<string>(msg.PetIdsToGraze ?? Array.Empty<string>());
        List<PetStore.Entry> mine = PetStore.Of(EntityId);
        if (wanted.Count > PetTables.Constants.GrazableCount)
        {
            Send(new Abort { Text = "ช่องปล่อยเล็มหญ้าไม่พอ" }, seq);
            return;
        }
        bool changed = false;
        foreach (PetStore.Entry entry in mine)
        {
            bool want = wanted.Contains(entry.Pet.EntityId);
            if (want == entry.Grazing) continue;
            if (want && entry.Pet.IsSpawned) DespawnPet(entry);
            SetGrazing(entry, want);
            changed = true;
        }
        if (changed)
        {
            _world.BroadCast(new GrazedPets { Data = _world.GetGrazedPets().ToArray() });
            _world.Save();
        }
        SendPetsInfo(seq);
    }

    /// <summary>
    /// ย้ายสัตว์เข้า/ออกโหมดเล็มหญ้า พร้อมซิงก์กับรายการของโลก
    /// (World.GrazedPetList เป็นรายการรวมของทุกคนในเกาะ — แยกกันด้วย EntityId ของสัตว์ซึ่งไม่ซ้ำ)
    /// </summary>
    private void SetGrazing(PetStore.Entry entry, bool grazing)
    {
        entry.Grazing = grazing;
        List<Messages.Pet> world = _world.GetGrazedPets();
        int idx = world.FindIndex(p => p.EntityId == entry.Pet.EntityId);
        if (grazing)
        {
            entry.Pet.Stat.GrazedAt = Times.UnixTimeNow();
            if (idx < 0) world.Add(entry.Pet);
            else world[idx] = entry.Pet;
        }
        else
        {
            entry.Pet.Stat.GrazedAt = null;
            if (idx >= 0) world.RemoveAt(idx);
        }
    }

    /// <summary>
    /// บันทึกว่าเจอสัตว์ชนิดนี้ครั้งแรก — DiscoverAnimal (5002)
    ///
    /// client/MapSystem.cs:679-693 ใช้ .All(IsSuccess): true = เด้งป้าย "พบสัตว์ชนิดใหม่",
    /// false = ถอนออกจากรายการที่จำไว้แล้วลองใหม่รอบหน้า ⇒ ตอบ OK ได้เมื่อเราจำจริง ๆ เท่านั้น
    ///
    /// ⚠️ ในทางปฏิบัติ handler นี้จะยังไม่ถูกยิงเลย เพราะ client/Durango.Logic.Map/DiscoverInfo.cs:50-60
    /// วนดูสัตว์รอบตัวก็ต่อเมื่อ DiscoveryInfo.AnimalTypes มีรายการ แต่ Core/Player.cs:369-374
    /// ตอบ AnimalTypes เป็นอาเรย์ว่าง ⇒ size == 0 == num แล้ว return ทิ้งก่อนถึงบรรทัดที่ยิง
    /// </summary>
    private void HandleDiscoverAnimalMsg(DiscoverAnimal msg, uint seq)
    {
        if (string.IsNullOrEmpty(msg.EntityId))
        {
            Send(new Abort { Text = "ไม่รู้ว่าเป็นสัตว์ตัวไหน" }, seq);
            return;
        }
        // เก็บในหน่วยความจำต่อ connection — ยังไม่มีที่เซฟ (ต้องต่อกับ DiscoveryInfo ก่อน ดูรายงาน)
        _discoveredAnimals.Add(msg.EntityId);
        Send(default(OK), seq);
    }

    /// <summary>สัตว์ที่ผู้เล่นคนนี้ "เพิ่งเจอ" ในรอบการเชื่อมต่อนี้ — ยังไม่ผูกกับ DiscoveryInfo</summary>
    private readonly HashSet<string> _discoveredAnimals = new();

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ชั้น E — กาชา (milestone / active skill / rank)
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ขอดูตัวเลือกแท็บ milestone ก่อนหมุน — GetMilestoneCandidate (800010) ตอบ MilestoneCandidates (800011)
    /// client/PetManager.cs:1123-1136 มี .Rest → onResult(null) รองรับอยู่แล้ว
    ///
    /// Result = ตัวเลือกที่จะได้ (แท็ก, น้ำหนัก) · Original = ชุดเดิมก่อนหน้า
    /// พูลแท็ก 25 ตัวมาจากข้อมูลจริง (tags.json ที่ required_performance = "animal_stat")
    /// ส่วนน้ำหนักเป็น **ค่าของเรา** (เท่ากันทุกตัว) เพราะไฟล์ข้อมูลไม่มีตารางความน่าจะเป็นของ milestone
    /// </summary>
    private void HandleGetMilestoneCandidateMsg(GetMilestoneCandidate msg, uint seq)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, msg.PetId);
        if (entry == null)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้" }, seq);
            return;
        }
        // สุ่มหยิบมาโชว์ N ตัวจากพูลจริง — ความน่าจะเป็นที่ส่งไปเท่ากันทุกตัว (1/จำนวนแท็กทั้งหมด)
        // ซึ่งตรงกับวิธีสุ่มจริงใน RollMilestoneTag() ⇒ ตัวเลขที่ผู้เล่นเห็นไม่หลอก
        int total = Math.Max(1, PetTables.MilestoneTags.Count);
        Pair<string, float>[] pool = PetTables.MilestoneTags
            .OrderBy(_ => PetTuning.Rng.Next())
            .Take(PetTuning.MilestoneCandidateCount)
            .Select(t => new Pair<string, float>(t.Id, 1f / total))
            .ToArray();
        Send(new MilestoneCandidates { Result = pool, Original = pool }, seq);
    }

    /// <summary>
    /// หมุน milestone — PickMilestone (800012) / PickMilestoneAgain (800014) ตอบ MilestoneResult (800013)
    ///
    /// ผลที่ได้ยัง **ไม่ถูกใช้จริงจนกว่าจะ AcceptMilestone** (client/Durango.UI/PetMilestonePickGroup.cs:117-130
    /// ปุ่ม "속성 확정" ⇒ AcceptMilestone) ⇒ เก็บไว้เป็น Pending ก่อน หมุนซ้ำได้เรื่อย ๆ
    ///
    /// ค่าที่ส่งกลับ: SelectedTagId (แท็กที่ได้) · OriginalStat/NewStat (ค่าสถานะก่อน-หลัง ใช้วาดตารางเทียบ
    /// client/Durango.UI/PetMilestoneResultWidget.cs:87-161) · RetryCost (ราคาหมุนซ้ำครั้งถัดไป)
    /// RetryCost คิดจากสูตรจริง costs.json → pet_revert_milestone
    /// </summary>
    private void HandlePickMilestoneMsg(string petId, bool reroll, uint seq)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, petId);
        if (entry == null)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้" }, seq);
            return;
        }
        int slot = CurrentMilestoneSlot(entry);
        if (slot < 0)
        {
            Send(new Abort { Text = "สัตว์ตัวนี้ยังไม่มีช่องคุณสมบัติให้หมุน" }, seq);
            return;
        }
        if (reroll) entry.MilestoneRedrawCount++;

        Dictionary<Derived, float> before = new(entry.Pet.Statistics.DerivedAbilities);
        (string tagId, int tagLevel) = RollMilestoneTag();
        entry.PendingMilestoneSlot = slot;
        entry.PendingMilestoneTag = tagId;
        entry.PendingMilestoneTagLevel = tagLevel;

        // คิดค่าสถานะ "ถ้ารับแท็กนี้" โดยไม่แตะของจริง — ผู้เล่นยังกดยกเลิกได้
        var preview = new Dictionary<string, int>(entry.Pet.Stat.Tags ?? new Dictionary<string, int>());
        preview[tagId] = Math.Min(PetTables.MilestoneTagMaxLevel(tagId), preview.GetValueOrDefault(tagId) + tagLevel);
        Dictionary<Derived, float> after = PetFactory.DerivedOf(entry.Pet.EntityType, entry.Pet.Statistics.Level, preview);

        Money retry = PetTables.Costs.MilestoneRetryCost(entry.Pet.Statistics.Level, entry.MilestoneRedrawCount + 1);
        entry.Pet.Stat.RetryCost = retry;
        Send(new MilestoneResult
        {
            SelectedTagId = tagId,
            OriginalStat = before,
            NewStat = after,
            RewardItemId = string.Empty,   // รางวัลปลอบใจตอนหมุนพลาด (constants.pet.milestone_failure_rewards)
                                           // ยังไม่ทำ เพราะระบบนี้ของเราไม่มี "หมุนพลาด" — ได้แท็กทุกครั้ง
            RetryCost = retry
        }, seq);
    }

    /// <summary>
    /// ยืนยันรับ milestone — AcceptMilestone (800015) ตอบ MilestoneResult
    /// client/Durango.UI/PetMilestonePickGroup.cs:124-129 ปิดหน้าต่างทันทีที่ได้คำตอบ
    /// ตัวนี้เป็นตัวที่ "ทำจริง": ติดแท็กลงสัตว์ · ปักธง Acquired ของช่องนั้น · คิดค่าสถานะใหม่
    /// </summary>
    private void HandleAcceptMilestoneMsg(AcceptMilestone msg, uint seq)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, msg.PetId);
        if (entry == null || string.IsNullOrEmpty(entry.PendingMilestoneTag))
        {
            Send(new Abort { Text = "ยังไม่ได้หมุนคุณสมบัติ" }, seq);
            return;
        }
        Dictionary<Derived, float> before = new(entry.Pet.Statistics.DerivedAbilities);
        string tagId = entry.PendingMilestoneTag;
        entry.Pet.Stat.Tags ??= new Dictionary<string, int>();
        entry.Pet.Stat.Tags[tagId] = Math.Min(PetTables.MilestoneTagMaxLevel(tagId),
            entry.Pet.Stat.Tags.GetValueOrDefault(tagId) + entry.PendingMilestoneTagLevel);

        MilestoneInfo[] slots = entry.Pet.Statistics.MilestonesInformation;
        if (slots != null && entry.PendingMilestoneSlot >= 0 && entry.PendingMilestoneSlot < slots.Length)
        {
            slots[entry.PendingMilestoneSlot].TagId = tagId;
            slots[entry.PendingMilestoneSlot].Acquired = true;
        }
        entry.Pet.Stat.LastMilestoneAccepted = true;
        entry.PendingMilestoneTag = null;
        entry.PendingMilestoneTagLevel = 0;
        entry.MilestoneRedrawCount = 0;
        RecalcPetStats(entry);

        Send(new MilestoneResult
        {
            SelectedTagId = tagId,
            OriginalStat = before,
            NewStat = new Dictionary<Derived, float>(entry.Pet.Statistics.DerivedAbilities),
            RewardItemId = string.Empty,
            RetryCost = PetTables.Costs.MilestoneRetryCost(entry.Pet.Statistics.Level, 1)
        }, seq);
        _world.BroadCast(entry.Pet);
    }

    /// <summary>
    /// หมุนสกิลแอคทีฟ — DrawActiveSkill (800101) / RedrawActiveSkill (800102) ตอบ DrawSkillResult (800103)
    ///
    /// **ตารางความน่าจะเป็นตัวนี้มีอยู่จริงในข้อมูล** (งานที่สั่งบอกว่าไม่มี แต่หาเจอแล้ว):
    /// data/assets/pet/pet_active_skill_conditions.json → &lt;skillId&gt; → &lt;rank&gt; →
    ///   weight (100/60/20 ตามแรงก์ B/A/S) · entity_type (สัตว์ชนิดไหนได้สกิลนี้บ้าง) ·
    ///   tag_condition (ต้องมีแท็ก milestone ระดับเท่าไรถึงจะสุ่มติดแรงก์นั้น) ·
    ///   for_fightable / for_ridable (จำกัดตามความสามารถของสัตว์ ดู pets_for_client.json)
    /// ⇒ ไฟล์นี้สุ่มตามน้ำหนักจริงทั้งหมด ไม่มีตัวเลขที่เราตั้งเองในส่วนนี้เลย
    ///
    /// ต่างจาก milestone ตรงที่ **ไม่มี message "ยืนยันสกิล" แยก** — client อ่านสกิลปัจจุบันจาก
    /// pet.Statistics.AvailableActiveSkill (client/Durango.UI/PetUtil.cs HasPetActiveSkill)
    /// ⇒ ต้องเขียนลงสัตว์ทันทีที่หมุนเสร็จ
    /// </summary>
    private void HandleDrawActiveSkillMsg(string petId, Messages.PetActiveSkill? current, uint seq)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, petId);
        if (entry == null)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้" }, seq);
            return;
        }
        Messages.PetActiveSkill? rolled = RollActiveSkill(entry);
        if (!rolled.HasValue)
        {
            Send(new Abort { Text = "สัตว์ชนิดนี้ไม่มีสกิลพิเศษ" }, seq);
            return;
        }
        if (current.HasValue) entry.SkillRedrawCount++;
        entry.Pet.Statistics.AvailableActiveSkill = new[] { rolled.Value };
        Money retry = PetTables.Costs.ActiveSkillRetryCost(entry.SkillRedrawCount + 1);
        Send(new DrawSkillResult { Skill = rolled.Value, RetryCost = retry }, seq);
        _world.BroadCast(entry.Pet);
    }

    /// <summary>
    /// ขอสุ่มแรงก์ใหม่ — RevertPetRank (74014) ตอบ RevertPetRankCandidate (74015)
    /// เป็นแค่ "ตัวอย่างที่จะได้" — ต้องรอ AcceptPetRank ถึงจะเปลี่ยนจริง (docs/protocol-coverage.md)
    ///
    /// พูลแรงก์มาจากข้อมูลจริง pets_for_client.json → available_ranks ของชนิดนั้น
    /// (เช่น pet_phenaco มีแค่ [12] = B เท่านั้น ⇒ หมุนยังไงก็ได้ B) ส่วน **น้ำหนักเป็นค่าของเรา**
    /// (PetTuning.RankWeight) เพราะข้อมูลไม่ได้บอกความน่าจะเป็น
    /// ค่าใช้จ่ายจริงอยู่ costs.json → pet_revert_rank (จ่ายด้วยบัตร voucher_revert_rank เท่านั้น
    /// ไม่มีช่อง amount ⇒ ยังไม่หักอะไร เพราะเซิร์ฟยังไม่มีระบบบัตร — ดูรายงาน)
    /// </summary>
    private void HandleRevertPetRankMsg(RevertPetRank msg, uint seq)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, msg.PetId);
        if (entry == null)
        {
            Send(new Abort { Text = "ไม่พบสัตว์ตัวนี้" }, seq);
            return;
        }
        PetRank[] pool = PetTables.AvailableRanks(entry.Pet.EntityType);
        if (pool.Length == 0)
        {
            Send(new Abort { Text = "สัตว์ชนิดนี้เปลี่ยนแรงก์ไม่ได้" }, seq);
            return;
        }
        int pick = PetTuning.PickWeighted(pool.Select(PetTuning.RankWeight).ToArray());
        if (pick < 0) pick = 0;
        entry.PendingRank = pool[pick];
        // Tag = แท็กแถมที่จะได้พร้อมแรงก์ใหม่ — ข้อมูลไม่มีตารางนี้ จึงส่งค่าว่าง
        // (client/Durango.UI/PetGroup.cs โชว์เป็นบรรทัดว่างเฉย ๆ ไม่พัง)
        entry.PendingRankTag = string.Empty;
        Send(new RevertPetRankCandidate { Rank = entry.PendingRank.Value, Tag = entry.PendingRankTag }, seq);
    }

    /// <summary>ยืนยันรับแรงก์ที่เพิ่งสุ่มได้ — AcceptPetRank (74016) · client/PetManager.cs:1320-1332 ใช้ .All(IsSuccess)</summary>
    private void HandleAcceptPetRankMsg(AcceptPetRank msg, uint seq)
    {
        PetStore.Entry entry = PetStore.Find(EntityId, msg.PetId);
        if (entry == null || !entry.PendingRank.HasValue)
        {
            Send(new Abort { Text = "ยังไม่ได้สุ่มแรงก์ใหม่" }, seq);
            return;
        }
        entry.Pet.Rank = entry.PendingRank.Value;
        entry.PendingRank = null;
        // เปลี่ยนแรงก์ = จำนวนช่อง milestone เปลี่ยนตาม ⇒ สร้างช่องใหม่โดยรักษาแท็กที่ได้มาแล้ว
        entry.Pet.Statistics.MilestonesInformation =
            PetFactory.BuildMilestones(entry.Pet.Rank, entry.Pet.Statistics.MilestonesInformation);
        RecalcPetStats(entry);
        Send(default(OK), seq);
        _world.BroadCast(entry.Pet);
    }

    /// <summary>
    /// สั่งสัตว์ใช้สกิลแอคทีฟ — UsePetActiveSkill (800200)
    /// client/PetManager.cs:1300-1312 ใช้ .All(IsSuccess): ไม่สำเร็จ = ยกเลิกสถานะ "จองท่า" ที่จองไว้
    /// แล้วรอ push PetActiveSkillUsed (800201) เพื่อเริ่มเล่นแอนิเมชัน (client/PetManager.cs:63-64)
    ///
    /// ClipName มาจากข้อมูลจริง pet_active_skills.json → &lt;skillId&gt; → &lt;rank&gt; → action_set
    /// (เวลาคูลดาวน์ก็อยู่ในไฟล์เดียวกัน แต่ client จับเวลาเองผ่าน PetSkillStates ⇒ เซิร์ฟไม่ต้องส่ง)
    /// </summary>
    private void HandleUsePetActiveSkillMsg(UsePetActiveSkill msg, uint seq)
    {
        PetStore.Entry entry = PetStore.Of(EntityId).FirstOrDefault(e => e.Pet.IsSpawned);
        if (entry == null)
        {
            Send(new Abort { Text = "ยังไม่ได้เรียกสัตว์ออกมา" }, seq);
            return;
        }
        Messages.PetActiveSkill[] skills = entry.Pet.Statistics.AvailableActiveSkill ?? Array.Empty<Messages.PetActiveSkill>();
        int idx = Array.FindIndex(skills, s => s.SkillId == msg.SkillId);
        if (idx < 0)
        {
            Send(new Abort { Text = "สัตว์ตัวนี้ไม่มีสกิลนั้น" }, seq);
            return;
        }
        string clip = PetTables.ActiveSkillClip(msg.SkillId, skills[idx].Rank);
        Send(default(OK), seq);
        _world.BroadCast(new PetActiveSkillUsed { SkillId = msg.SkillId, ClipName = clip });
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ตัวช่วยระดับสัตว์หนึ่งตัว
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>ยังไม่ตาย = หลอดชีวิตตอนนี้ &gt; 0 (client ใช้ AppearPet.IsAlive ตัวเดียวกัน)</summary>
    private static bool PetIsAlive(PetStore.Entry entry)
    {
        return entry.Pet.Stat.Life == null || entry.Pet.Stat.Life.Get(Times.UnixTimeNow()) > 0f;
    }

    /// <summary>
    /// เติมหลอดอิ่ม แล้วสร้างเส้นแนวโน้มใหม่ตาม hungry_velocity จริง
    /// (Gauge ที่ส่งไปคือ "เส้น" ไม่ใช่เลขนิ่ง — client เดินหลอดเองตามเวลา ดู Core/SurvivalState.cs:76)
    /// </summary>
    private static void FillHungry(PetStore.Entry entry, float amount)
    {
        double now = Times.UnixTimeNow();
        float cur = entry.Pet.Stat.Hungry?.Get(now) ?? 0f;
        float next = Math.Min(entry.HungryMax, cur + amount);
        entry.Pet.Stat.Hungry = PetFactory.HungryGauge(entry.HungryMax, entry.HungryVelocity, next, now);
    }

    /// <summary>คิดค่าสถานะทั้งชุดใหม่จากเลเวล+แท็กปัจจุบัน (เรียกหลังแท็ก/แรงก์/เลเวลเปลี่ยน)</summary>
    private static void RecalcPetStats(PetStore.Entry entry)
    {
        entry.Pet.Statistics.DerivedAbilities =
            PetFactory.DerivedOf(entry.Pet.EntityType, entry.Pet.Statistics.Level, entry.Pet.Stat.Tags);
        entry.Pet.Statistics.RequiredExp =
            PetTables.RequiredExp(entry.Pet.EntityType, entry.Pet.Statistics.Level);
    }

    /// <summary>ช่อง milestone ที่กำลังจะหมุน — ช่องแรกที่ยังไม่ได้รับ (-1 = ไม่มีช่องเหลือ)</summary>
    private static int CurrentMilestoneSlot(PetStore.Entry entry)
    {
        MilestoneInfo[] slots = entry.Pet.Statistics.MilestonesInformation;
        if (slots == null) return -1;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].Acquired) return i;
        }
        return -1;
    }

    /// <summary>สุ่มแท็ก milestone หนึ่งตัว + ระดับ — พูลแท็กจริง / น้ำหนักระดับเป็นค่าของเรา</summary>
    private static (string, int) RollMilestoneTag()
    {
        IReadOnlyList<PetTables.MilestoneTag> pool = PetTables.MilestoneTags;
        if (pool.Count == 0) return (string.Empty, 0);
        PetTables.MilestoneTag tag = pool[PetTuning.Rng.Next(pool.Count)];
        int lvIdx = PetTuning.PickWeighted(PetTuning.MilestoneTagLevelWeights.Select(w => w.Weight).ToArray());
        int level = lvIdx < 0 ? 1 : PetTuning.MilestoneTagLevelWeights[lvIdx].Level;
        return (tag.Id, level);
    }

    /// <summary>
    /// สุ่มสกิลแอคทีฟตามน้ำหนักจริงใน pet_active_skill_conditions.json
    /// กรองสามชั้นตามที่ไฟล์ข้อมูลกำหนด: ชนิดสัตว์ (entity_type) · ต้องต่อสู้/ขี่ได้ไหม · แท็กที่มีพอไหม
    /// </summary>
    private static Messages.PetActiveSkill? RollActiveSkill(PetStore.Entry entry)
    {
        PetTables.PetDef def = PetTables.PetOf(entry.Pet.EntityType);
        if (def == null) return null;
        Dictionary<string, int> tags = entry.Pet.Stat.Tags ?? new Dictionary<string, int>();
        var candidates = new List<Messages.PetActiveSkill>();
        var weights = new List<int>();
        foreach (PetTables.ActiveSkillCondition cond in PetTables.ActiveSkillConditions)
        {
            if (cond.EntityTypes == null || !cond.EntityTypes.Contains(entry.Pet.EntityType)) continue;
            if (cond.ForFightable && !def.IsFightable) continue;
            if (cond.ForRidable && !def.IsRidable) continue;
            bool ok = true;
            if (cond.TagCondition != null)
            {
                foreach (KeyValuePair<string, int> need in cond.TagCondition)
                {
                    if (tags.GetValueOrDefault(need.Key) < need.Value) { ok = false; break; }
                }
            }
            if (!ok) continue;
            candidates.Add(new Messages.PetActiveSkill { SkillId = cond.SkillId, Rank = cond.Rank });
            weights.Add(cond.Weight);
        }
        if (candidates.Count == 0) return null;
        int pick = PetTuning.PickWeighted(weights);
        return candidates[pick < 0 ? 0 : pick];
    }

    /// <summary>คืนของในกระเป๋าสัตว์กลับเข้ากระเป๋าผู้เล่นเท่าที่ใส่ไหว (ที่เหลือยังคาอยู่ใน entry.Bag)</summary>
    private List<Item> ReturnBagToPlayer(PetStore.Entry entry)
    {
        int free = PetTuning.PlayerInventoryMaxSize - _context.InventoryItems.Sum(it => Math.Max(1, it.Size));
        var moved = new List<Item>();
        for (int i = entry.Bag.Count - 1; i >= 0; i--)
        {
            int size = Math.Max(1, entry.Bag[i].Size);
            if (size > free) continue;
            free -= size;
            _context.InventoryItems.Add(entry.Bag[i]);
            moved.Add(entry.Bag[i]);
            entry.Bag.RemoveAt(i);
        }
        return moved;
    }

    /// <summary>ไอเทมชิ้นนี้ติดแท็กตัวใดตัวหนึ่งในรายการไหม (Cheats.MakeItem เติม Item.Tags จาก prototype ให้แล้ว)</summary>
    private static bool ItemHasAnyTag(Item item, IReadOnlyList<string> tagIds)
    {
        if (item.Tags == null || tagIds == null) return false;
        foreach (Messages.Tag tag in item.Tags)
        {
            for (int i = 0; i < tagIds.Count; i++)
            {
                if (tag.Id == tagIds[i]) return true;
            }
        }
        return false;
    }

    /// <summary>ตำแหน่งผู้เล่นตอนนี้ — ใช้เป็นจุดตั้งต้นของสัตว์ที่เพิ่งเรียกออกมา</summary>
    private WorldPosition PlayerPosition()
    {
        Movement[] movements = _context.AppearPlayer.Move.Movements;
        if (movements == null || movements.Length == 0) return default;
        Location[] path = movements[^1].Path;
        return path == null || path.Length == 0 ? default : path[^1].Position;
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ที่เก็บสัตว์เลี้ยง (ใช้ร่วมกันทุกผู้เล่นในโปรเซสเดียว)
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// สัตว์เลี้ยงของผู้เล่นแต่ละคน
    ///
    /// ⚠️ **อยู่ในหน่วยความจำเท่านั้น — รีสตาร์ตเซิร์ฟแล้วสัตว์หาย**
    /// ไม่ใช่เพราะขี้เกียจ: สัตว์เลี้ยงเป็นของผู้เล่น ⇒ ที่ถูกต้องคือ Core/PlayerContext.cs
    /// (ไฟล์ .player) ซึ่งอยู่นอกขอบเขตของงานนี้ ⇒ ต้องเพิ่มฟิลด์ที่นั่นก่อน (ดูรายงานท้ายงาน)
    /// เปิดเป็น public เพื่อให้เสียบ persistence ทีหลังได้โดยไม่ต้องแก้ handler
    ///
    /// ไม่ต้องล็อกเธรด เพราะ handler ทั้งหมดถูกเรียกจาก main loop เส้นเดียว
    /// (Program.cs:184 host.Process() → World.Process → Player.Process → Connection.Process)
    /// — หลักการเดียวกับ Player.WarehouseStore ใน Core/Player.Inventory.cs
    /// </summary>
    public static class PetStore
    {
        /// <summary>
        /// ค่า CageInfo ของสัตว์ที่ **ไม่ได้อยู่ในกรง** — ก้อนว่าง ไม่ใช่ null
        ///
        /// ดีไซน์จริงของ NEXON คือฟิลด์นี้มีค่าเสมอ แล้วใช้ <c>RegionId</c> ว่าง/ไม่ว่างเป็นตัวชี้
        /// ว่าอยู่ในกรงไหม — หลักฐาน 3 จุดที่ต้องเป็นจริงพร้อมกัน:
        ///   client/Durango.UI/PetGroup.cs:587      ต้อง <c>HasValue &amp;&amp; RegionId ว่าง</c> ถึงจะปล่อยเล็มหญ้าได้
        ///   client/Durango.UI/GrowCageGroup.cs:230 ใช้ <c>!HasValue || RegionId ว่าง</c>
        ///   client/Durango.UI/PetInfoWidget.cs:309 ใช้เงื่อนไขเดียวกันคุมปุ่ม Spawn/Reinify/Release
        /// ⇒ ส่ง null รายการปล่อยเล็มหญ้าจะว่างตลอดกาล กดปุ่มแล้วเด้งว่า "ไม่มีสัตว์ที่ปล่อยได้"
        /// ทั้งที่มีสัตว์อยู่ (ฟีเจอร์หายในสายตาผู้เล่น)
        /// </summary>
        public static CageInfo NotInCage => new()
        {
            RegionId = string.Empty,
            RegionName = string.Empty,
            Tile = default
        };

        /// <summary>สัตว์หนึ่งตัว = ข้อมูลที่ส่งให้ client (Pet) + สถานะฝั่งเซิร์ฟที่ client ไม่ต้องรู้</summary>
        public sealed class Entry
        {
            /// <summary>ก้อนที่ส่งให้ client ตรง ๆ — แก้ตรงนี้แล้ว BroadCast ได้เลย</summary>
            public Messages.Pet Pet;

            /// <summary>ปล่อยเล็มหญ้าอยู่ไหม (แท็บขวาของหน้าจอสัตว์เลี้ยง)</summary>
            public bool Grazing;

            /// <summary>ของในกระเป๋าสัตว์</summary>
            public readonly List<Item> Bag = new();

            /// <summary>เพดานหลอดชีวิต/อิ่ม + อัตราหิว — จำไว้เพื่อสร้าง Gauge ใหม่ตอนกิน/ฟื้น</summary>
            public float LifeMax;
            public float HungryMax;
            public float HungryVelocity;

            /// <summary>ผลหมุน milestone ที่ยังไม่กดยืนยัน</summary>
            public string PendingMilestoneTag;
            public int PendingMilestoneTagLevel;
            public int PendingMilestoneSlot = -1;

            /// <summary>จำนวนครั้งที่หมุนซ้ำ — ใช้เข้าสูตรราคาใน costs.json</summary>
            public int MilestoneRedrawCount;
            public int SkillRedrawCount;

            /// <summary>แรงก์ที่สุ่มได้แต่ยังไม่กด AcceptPetRank</summary>
            public PetRank? PendingRank;
            public string PendingRankTag;
        }

        private static readonly Dictionary<string, List<Entry>> Owned = new();

        /// <summary>สัตว์ทั้งหมดของผู้เล่นคนนี้ (คืนลิสต์จริง — แก้ได้เลย)</summary>
        public static List<Entry> Of(string ownerEntityId)
        {
            if (string.IsNullOrEmpty(ownerEntityId)) return new List<Entry>();
            if (Owned.TryGetValue(ownerEntityId, out List<Entry> list)) return list;
            list = new List<Entry>();
            Owned[ownerEntityId] = list;
            return list;
        }

        [CanBeNull]
        public static Entry Find(string ownerEntityId, string petEntityId)
        {
            if (string.IsNullOrEmpty(petEntityId)) return null;
            return Of(ownerEntityId).FirstOrDefault(e => e.Pet.EntityId == petEntityId);
        }

        public static void Add(string ownerEntityId, Entry entry) => Of(ownerEntityId).Add(entry);

        public static void Remove(string ownerEntityId, Entry entry) => Of(ownerEntityId).Remove(entry);
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  โรงงานสร้าง Messages.Pet จากข้อมูลจริง
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ประกอบ Pet หนึ่งตัวจากไฟล์ข้อมูล — ใช้ทั้งตอน GetPreviewPet และตอนสร้างสัตว์จริง
    /// (ตอนนี้มีแต่พรีวิวที่เรียกใช้ เพราะยังไม่มีทางได้สัตว์ตัวจริง — ดูข้อจำกัดข้อ 3 ที่หัวไฟล์)
    /// </summary>
    private static class PetFactory
    {
        public static Messages.Pet? Build(ushort petEntityType, PetRank rank, int level, string tamerEntityId)
        {
            PetTables.PetDef def = PetTables.PetOf(petEntityType);
            PetTables.ReinPerf perf = PetTables.PerfOf(petEntityType, level);
            if (def == null && perf == null) return null;
            double now = Times.UnixTimeNow();

            var tags = new Dictionary<string, int>();
            Dictionary<Derived, float> derived = DerivedOf(petEntityType, level, tags);
            float lifeMax = derived.GetValueOrDefault(Derived.LifeMax);
            float hungryMax = derived.GetValueOrDefault(Derived.HungryMax);
            float hungryVelocity = perf?.HungryVelocity ?? 0f;

            return new Messages.Pet
            {
                EntityId = Guid.NewGuid().ToString(),
                EntityType = petEntityType,
                TamerEntityId = tamerEntityId,
                Name = def?.Name ?? perf?.PetName ?? string.Empty,
                Rank = rank,
                Generation = 1,                    // รุ่นที่ 1 = ตัวที่เพิ่งทำให้เชื่อง (ระบบผสมพันธุ์ไม่มีในเกมนี้)
                IsBoarding = false,
                IsSpawned = false,
                Stat = new PetStats
                {
                    PlaybackRate = perf?.PlaybackRate ?? 1f,
                    Size = perf?.Size ?? 0,
                    Taste = string.Empty,          // constants.json → tastes เป็นเรื่องอาหารของผู้เล่น ไม่ใช่ของสัตว์
                    InventoryUsage = 0,
                    Life = new Gauge(lifeMax, 0f, new[] { new GaugeNode(now, lifeMax) }),
                    Hungry = HungryGauge(hungryMax, hungryVelocity, hungryMax, now),
                    EatableTags = PetTables.EatableTagsOf(petEntityType),
                    Tags = tags,
                    LastMilestoneAccepted = false,
                    RetryCost = null,
                    IsOld = false,
                    AgingSince = now,
                    AgingUntil = now + LifeSpanSeconds(derived),
                    GrazedAt = null
                },
                Statistics = new PetStatistics
                {
                    Level = level,
                    Exp = 0,
                    RequiredExp = PetTables.RequiredExp(petEntityType, level),
                    DerivedAbilities = derived,
                    MilestonesInformation = BuildMilestones(rank, null),
                    AvailableActiveSkill = Array.Empty<Messages.PetActiveSkill>()
                },
                // ⚠️ ห้ามเป็น null — ดีไซน์จริงของ NEXON คือ CageInfo มีเสมอ แล้วใช้ RegionId
                // ว่าง/ไม่ว่างเป็นตัวชี้ว่าอยู่ในกรงไหม (client/Durango.UI/PetGroup.cs:587
                // กรอง `pet.CageInfo.HasValue && string.IsNullOrEmpty(RegionId)` สำหรับปุ่ม
                // "ปล่อยเล็มหญ้า") ⇒ ส่ง null รายการนั้นจะว่างตลอดกาล ปุ่มกดแล้วเด้งว่าไม่มีสัตว์
                CageInfo = PetStore.NotInCage
            };
        }

        /// <summary>
        /// ค่าสถานะทั้งชุด — ฐานจาก performance.json → reins แล้วบวก/คูณด้วยแท็ก milestone จาก tags.json
        ///
        /// แท็กมีสองแบบตามไฟล์ข้อมูล: <c>*_plus_*</c> บวกตรง ๆ, <c>*_amplifier_*</c> คูณเป็นสัดส่วน
        /// ทั้งคู่ประกาศเป็น function = "incr" ⇒ ค่าที่สูตรให้คือ "ส่วนที่เพิ่ม" ไม่ใช่ค่าสุดท้าย
        /// (เช่น attack_amplifier สูตร "0.03 * level + 0.02" ที่ระดับ 1 = +5% ของค่าฐาน)
        ///
        /// Attack/Defense/Accuracy ฐานอยู่ใน entity_types/animal.json แต่เป็นสูตรที่ใช้ตัวแปร
        /// combat_level กับ unstable_factor ซึ่งเซิร์ฟยังไม่มีระบบ unstable factor ⇒ ใช้ 1.0
        /// (= เกาะปกติ ไม่ใช่เกาะอันตราย) และคอมเมนต์ไว้ตรงนี้ว่าเป็นสมมติฐานของเรา
        /// </summary>
        public static Dictionary<Derived, float> DerivedOf(ushort petEntityType, int level,
            [CanBeNull] Dictionary<string, int> tags)
        {
            PetTables.ReinPerf perf = PetTables.PerfOf(petEntityType, level);
            PetTables.AnimalDef animal = PetTables.AnimalOf(petEntityType);
            var d = new Dictionary<Derived, float>
            {
                [Derived.Speed] = perf?.Speed ?? 0f,
                [Derived.InventoryCapacity] = perf?.Capacity ?? 0f,
                [Derived.HungryMax] = perf?.HungryMax ?? 0f,
                [Derived.LifeMax] = animal?.LifeMax ?? 0f,
                [Derived.Attack] = animal?.Attack(level) ?? 0f,
                [Derived.Defense] = animal?.Defense(level) ?? 0f,
                [Derived.Accuracy] = animal?.Accuracy(level) ?? 0f,
                [Derived.LifeSpan] = (float)PetTuning.LifeSpanDaysBase
            };
            if (tags == null || tags.Count == 0) return d;
            foreach (KeyValuePair<string, int> tag in tags)
            {
                PetTables.MilestoneTag def = PetTables.MilestoneTagOf(tag.Key);
                if (def == null) continue;
                float amount = def.Amount(tag.Value);
                if (def.Target == Derived.Invalid) continue;
                if (def.IsRatio) d[def.Target] = d.GetValueOrDefault(def.Target) * (1f + amount);
                else d[def.Target] = d.GetValueOrDefault(def.Target) + amount;
            }
            ToWireUnits(d);
            return d;
        }

        /// <summary>
        /// แปลงหน่วยของค่าที่ "หน่วยในไฟล์ข้อมูล" ต่างจาก "หน่วยในโปรโตคอล"
        ///
        /// ⚠️ ตอนนี้มีตัวเดียวคือ <c>LifeSpan</c> — ในไฟล์ข้อมูลเป็น **วัน**
        /// (<c>tags.json → life_span_plus_5 → "5 * level"</c> = เพิ่มทีละ 5 วัน) แต่ฝั่งเกมอ่านเป็น **วินาที**:
        ///   client/Durango.UI/ItemInfoView.cs:392                    ตั้งชื่อตัวแปรว่า seconds แล้วยัดเข้า TimedeltaFormatter
        ///   client/Durango.UI.Popup/PetItemInteractionPopup.cs:664   Math.Min กับ (AgingUntil − GrazedAt) ซึ่งเป็นวินาที
        ///   client/TimedeltaFormatter.cs:34-54                       ตารางหน่วยเริ่มที่ 86400 วิ/วัน ⇒ อินพุตเป็นวินาที
        /// ⇒ ส่งเลข 30 ดิบ ๆ ขึ้นไป ป้ายอายุขัยขึ้นว่า "30초" แล้วค้างที่ค่านั้นตลอดกาล
        /// ทั้งที่สัตว์ยังมีอายุเหลืออีก 2,592,000 วินาที
        ///
        /// **ต้องแปลงตรงนี้ที่เดียว** (ท้ายสุด หลังบวกแท็กครบแล้ว) เพราะ:
        ///   • ค่าที่คืนไปถูกยัดลง <c>PetStatistics.DerivedAbilities</c> ตรง ๆ (RecalcPetStats)
        ///     ซึ่งเป็นก้อนที่ส่งขึ้นสายจริง
        ///   • ถ้าไปแปลงตอนตั้งค่าฐานแทน แท็ก <c>life_span_plus</c> ที่บวกทีหลังจะบวกเป็น "วินาที"
        ///     (+5 วิ แทนที่จะเป็น +5 วัน) ⇒ แท็กแทบไม่มีผลเลย
        /// </summary>
        private static void ToWireUnits(Dictionary<Derived, float> d)
        {
            if (d.TryGetValue(Derived.LifeSpan, out float days))
            {
                d[Derived.LifeSpan] = (float)(days * PetTuning.SecondsPerDay);
            }
        }

        /// <summary>
        /// ช่อง milestone ตามแรงก์ — เลเวลที่ปลดแต่ละช่องมาจากข้อมูลจริง
        /// constants.json → pet → milestone_level → "&lt;จำนวนช่อง&gt;" → [[เลเวล, ไอดีตาราง], …]
        /// (จำนวนช่องต่อแรงก์เป็นค่าของเรา — ดู PetTuning.MilestoneCountOfRank)
        /// keep = ช่องเดิม เพื่อไม่ให้แท็กที่ได้มาแล้วหายตอนเปลี่ยนแรงก์
        /// </summary>
        public static MilestoneInfo[] BuildMilestones(PetRank rank, [CanBeNull] MilestoneInfo[] keep)
        {
            int count = PetTuning.MilestoneCountOfRank(rank);
            (int Level, int TableId)[] table = PetTables.Constants.MilestoneLevels(count);
            var result = new MilestoneInfo[table.Length];
            for (int i = 0; i < table.Length; i++)
            {
                result[i] = new MilestoneInfo
                {
                    Level = table[i].Level,
                    MilestoneTableId = table[i].TableId,
                    TagId = keep != null && i < keep.Length ? keep[i].TagId : string.Empty,
                    Acquired = keep != null && i < keep.Length && keep[i].Acquired
                };
            }
            return result;
        }

        /// <summary>
        /// หลอดอิ่มเป็น "เส้นลาดลง" ตาม hungry_velocity จริง (หน่วยต่อวินาที ค่าติดลบ)
        /// ส่งจุดสองจุด: ตอนนี้ กับตอนที่หลอดจะถึงศูนย์ — client เดินหลอดเองระหว่างสองจุดนั้น
        /// </summary>
        public static Gauge HungryGauge(float max, float velocity, float current, double now)
        {
            float cur = Math.Clamp(current, 0f, Math.Max(max, 1f));
            if (velocity >= 0f)
            {
                return new Gauge(max, 0f, new[] { new GaugeNode(now, cur) });
            }
            double secondsToEmpty = cur / -velocity;
            return new Gauge(max, 0f, new[]
            {
                new GaugeNode(now, cur),
                new GaugeNode(now + secondsToEmpty, 0f)
            });
        }

        /// <summary>
        /// อายุขัยเป็นวินาที — ค่าใน <paramref name="derived"/> ถูกแปลงเป็นวินาทีแล้วโดย
        /// <see cref="ToWireUnits"/> ⇒ **ห้ามคูณ 86400 ซ้ำอีก** (เคยคูณซ้ำจนอายุกลายเป็น 7 แสนปี)
        /// ค่าสำรองยังเป็น "วัน" เพราะเป็นค่าคงที่ดิบก่อนผ่านตัวแปลง
        /// </summary>
        private static double LifeSpanSeconds(Dictionary<Derived, float> derived)
        {
            return derived.TryGetValue(Derived.LifeSpan, out float seconds)
                ? seconds
                : PetTuning.LifeSpanDaysBase * PetTuning.SecondsPerDay;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ตารางข้อมูลจริงของระบบสัตว์ (โหลดครั้งเดียวตอนใช้ครั้งแรก)
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// อ่านไฟล์ data/assets/ ที่เกี่ยวกับสัตว์ทั้งหมด
    /// แยกจาก Yaml.Pets (Support/YamlGameplay.cs) เพราะคลาสนั้นไม่มีช่อง rein_id / available_ranks
    /// ที่ระบบนี้ต้องใช้ และไฟล์นั้นอยู่นอกขอบเขตงาน — จึงอ่านไฟล์เดียวกันซ้ำเป็นของตัวเอง
    /// </summary>
    private static class PetTables
    {
        // ── pets_for_client.json ────────────────────────────────────────────────────
        public class PetDef
        {
            [JsonProperty("name")] public Gettext NameText { get; set; }
            [JsonProperty("type_name")] public string TypeName { get; set; }
            [JsonProperty("rein_id")] public string ReinId { get; set; }
            [JsonProperty("vehicle_entity_type")] public int VehicleEntityType { get; set; }
            [JsonProperty("available_ranks")] public int[] AvailableRanks { get; set; }
            [JsonProperty("is_ridable")] public bool IsRidable { get; set; }
            [JsonProperty("is_fightable")] public bool IsFightable { get; set; }

            public string Name => NameText ?? string.Empty;
        }

        // ── performance.json → reins ────────────────────────────────────────────────
        public class ReinPerf
        {
            [JsonProperty("speed")] public float Speed { get; set; }
            [JsonProperty("capacity")] public float Capacity { get; set; }
            [JsonProperty("size")] public int Size { get; set; }
            [JsonProperty("playback_rate")] public float PlaybackRate { get; set; }
            [JsonProperty("hungry_max")] public string HungryMaxExpr { get; set; }
            [JsonProperty("hungry_velocity")] public float HungryVelocity { get; set; }
            [JsonProperty("pet_entity_type")] public int PetEntityType { get; set; }
            [JsonProperty("pet_name")] public Gettext PetNameText { get; set; }

            public string PetName => PetNameText ?? string.Empty;
            public float HungryMax { get; set; }
        }

        // ── entity_types/animal.json ────────────────────────────────────────────────
        public class AnimalDef
        {
            [JsonProperty("preferred_food_tag")] public string PreferredFoodTag { get; set; }
            [JsonProperty("attack")] public string AttackExpr { get; set; }
            [JsonProperty("defense")] public string DefenseExpr { get; set; }
            [JsonProperty("accuracy")] public string AccuracyExpr { get; set; }
            [JsonProperty("survival")] public Dictionary<string, Dictionary<string, object>> Survival { get; set; }

            /// <summary>
            /// สูตรของ animal.json ใช้ตัวแปร combat_level กับ unstable_factor
            /// unstable_factor = 1.0 คือ **สมมติฐานของเรา** (เกาะปกติ) เพราะเซิร์ฟยังไม่มีระบบเกาะอันตราย
            /// </summary>
            private float Eval(string expr, int level)
            {
                var vars = new Dictionary<string, double>
                {
                    { "combat_level", level },
                    { "unstable_factor", 1.0 },
                    { "level", level }
                };
                return PetFormula.TryEval(expr, vars, out double v) ? (float)v : 0f;
            }

            public float Attack(int level) => Eval(AttackExpr, level);
            public float Defense(int level) => Eval(DefenseExpr, level);
            public float Accuracy(int level) => Eval(AccuracyExpr, level);

            /// <summary>หลอดชีวิตสูงสุดจาก survival.life.max ในไฟล์เดียวกัน</summary>
            public float LifeMax
            {
                get
                {
                    if (Survival == null || !Survival.TryGetValue("life", out Dictionary<string, object> life)) return 0f;
                    if (!life.TryGetValue("max", out object raw) || raw == null) return 0f;
                    return float.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
                }
            }
        }

        // ── pet_task.json ───────────────────────────────────────────────────────────
        public class TaskDef
        {
            [JsonProperty("unlock_level")] public int UnlockLevel { get; set; }
            [JsonProperty("duration")] public double Duration { get; set; }
            [JsonProperty("exp")] public int Exp { get; set; }
            [JsonProperty("hungry_required")] public float HungryRequired { get; set; }
            [JsonProperty("type")] public int Type { get; set; }
        }

        // ── tags.json (เฉพาะแท็กที่ required_performance = "animal_stat") ────────────
        public class MilestoneTag
        {
            public string Id;
            public Derived Target;
            public bool IsRatio;
            public int MaxLevel;
            public string Formula;

            /// <summary>ค่าที่แท็กระดับนี้เพิ่มให้ — สูตรอยู่ใน tags.json ตรง ๆ (ตัวแปรชื่อ level)</summary>
            public float Amount(int tagLevel)
            {
                var vars = new Dictionary<string, double> { { "level", Math.Min(tagLevel, MaxLevel) } };
                return PetFormula.TryEval(Formula, vars, out double v) ? (float)v : 0f;
            }
        }

        public class ActiveSkillCondition
        {
            public string SkillId;
            public SkillRank Rank;
            public int Weight;
            public bool ForFightable;
            public bool ForRidable;
            public HashSet<ushort> EntityTypes;
            public Dictionary<string, int> TagCondition;
        }

        // ── รูปไฟล์ดิบ (แค่ที่ต้องใช้) ────────────────────────────────────────────────
        private class TagFileEntry
        {
            [JsonProperty("required_performance")] public string RequiredPerformance { get; set; }
            [JsonProperty("max_level")] public int MaxLevel { get; set; }
            [JsonProperty("modifiers")] public Dictionary<string, TagModifier> Modifiers { get; set; }
        }

        private class TagModifier
        {
            [JsonProperty("function")] public string Function { get; set; }
            [JsonProperty("formula")] public string Formula { get; set; }
        }

        private class SkillCondFileEntry
        {
            [JsonProperty("weight")] public int Weight { get; set; }
            [JsonProperty("for_fightable")] public bool ForFightable { get; set; }
            [JsonProperty("for_ridable")] public bool ForRidable { get; set; }
            [JsonProperty("entity_type")] public int[] EntityType { get; set; }
            [JsonProperty("tag_condition")] public Dictionary<string, int> TagCondition { get; set; }
        }

        private class SkillFileEntry
        {
            [JsonProperty("action_set")] public string ActionSet { get; set; }
        }

        private class PerfFileRoot
        {
            [JsonProperty("reins")] public Dictionary<string, Dictionary<string, ReinPerf>> Reins { get; set; }
            [JsonProperty("pet_food")] public Dictionary<string, Dictionary<string, PetFoodPerf>> PetFood { get; set; }
        }

        private class PetFoodPerf
        {
            [JsonProperty("vigor")] public float Vigor { get; set; }
        }

        private class ExpFileEntry
        {
            [JsonProperty("required_exp")] public string RequiredExp { get; set; }
        }

        // ── ตัวโหลดแบบขี้เกียจ ───────────────────────────────────────────────────────
        private static Dictionary<int, PetDef> _pets;
        private static Dictionary<ushort, ReinPerf> _perfByPet;
        private static Dictionary<int, AnimalDef> _animals;
        private static Dictionary<string, TaskDef> _tasks;
        private static Dictionary<string, Dictionary<string, PetFoodPerf>> _petFood;
        private static Dictionary<string, ExpFileEntry> _exp;
        private static List<MilestoneTag> _milestoneTags;
        private static Dictionary<string, MilestoneTag> _milestoneTagById;
        private static List<ActiveSkillCondition> _skillConditions;
        private static Dictionary<string, Dictionary<string, SkillFileEntry>> _skills;

        private static Dictionary<int, PetDef> Pets =>
            _pets ??= Json.ReadFromFile<Dictionary<int, PetDef>>("pet/pets_for_client") ?? new Dictionary<int, PetDef>();

        private static Dictionary<int, AnimalDef> Animals =>
            _animals ??= Json.ReadFromFile<Dictionary<int, AnimalDef>>("entity_types/animal") ?? new Dictionary<int, AnimalDef>();

        public static Dictionary<string, TaskDef> Tasks =>
            _tasks ??= Json.ReadFromFile<Dictionary<string, TaskDef>>("pet/pet_task") ?? new Dictionary<string, TaskDef>();

        private static Dictionary<string, ExpFileEntry> Exp =>
            _exp ??= Json.ReadFromFile<Dictionary<string, ExpFileEntry>>("pet/pet_exp") ?? new Dictionary<string, ExpFileEntry>();

        [CanBeNull]
        public static PetDef PetOf(ushort petEntityType) => Pets.GetValueOrDefault(petEntityType);

        [CanBeNull]
        public static AnimalDef AnimalOf(ushort petEntityType)
        {
            // สัตว์ในโลก (animal.json) กับสัตว์เลี้ยง (pets_for_client.json) คนละเลข —
            // เชื่อมกันด้วย vehicle_entity_type (client/PetExtension.cs:33-36 GetAnimalType)
            PetDef def = PetOf(petEntityType);
            return def == null ? null : Animals.GetValueOrDefault(def.VehicleEntityType);
        }

        /// <summary>อาหารที่สัตว์ชนิดนี้กิน — animal.json → preferred_food_tag (ว่าง = กินได้ทุกอย่างที่เป็น pet_food)</summary>
        public static string[] EatableTagsOf(ushort petEntityType)
        {
            string tag = AnimalOf(petEntityType)?.PreferredFoodTag;
            return string.IsNullOrEmpty(tag) ? Array.Empty<string>() : new[] { tag };
        }

        /// <summary>แรงก์ที่สัตว์ชนิดนี้เป็นได้ — pets_for_client.json → available_ranks (ตัวเลข 10-14 ตรงกับ PetRank)</summary>
        public static PetRank[] AvailableRanks(ushort petEntityType)
        {
            int[] raw = PetOf(petEntityType)?.AvailableRanks;
            if (raw == null) return Array.Empty<PetRank>();
            return raw.Where(r => r >= (int)PetRank.D && r <= (int)PetRank.S)
                      .Select(r => (PetRank)r)
                      .ToArray();
        }

        /// <summary>ค่าจาก performance.json → reins — เข้าถึงด้วย pet entity type (ไฟล์คีย์ด้วย prototype ของบังเหียน)</summary>
        [CanBeNull]
        public static ReinPerf PerfOf(ushort petEntityType, int level)
        {
            EnsurePerf(level);
            return _perfByPet.GetValueOrDefault(petEntityType);
        }

        private static void EnsurePerf(int level)
        {
            if (_perfByPet != null) return;
            _perfByPet = new Dictionary<ushort, ReinPerf>();
            PerfFileRoot root = Json.ReadFromFile<PerfFileRoot>("performance") ?? new PerfFileRoot();
            _petFood = root.PetFood ?? new Dictionary<string, Dictionary<string, PetFoodPerf>>();
            if (root.Reins == null) return;
            foreach (KeyValuePair<string, Dictionary<string, ReinPerf>> pair in root.Reins)
            {
                ReinPerf perf = PetLevelRange.Pick(pair.Value, level);
                if (perf == null || perf.PetEntityType <= 0) continue;
                perf.HungryMax = PetFormula.TryEval(perf.HungryMaxExpr,
                    new Dictionary<string, double> { { "level", level } }, out double hm) ? (float)hm : 0f;
                _perfByPet[(ushort)perf.PetEntityType] = perf;
            }
        }

        /// <summary>ค่าอิ่มที่ได้จากอาหารหนึ่งชิ้น — performance.json → pet_food → &lt;prototype&gt; → vigor</summary>
        public static float FoodVigor(string prototypeId, int level)
        {
            EnsurePerf(level);
            if (string.IsNullOrEmpty(prototypeId) || _petFood == null) return 0f;
            if (!_petFood.TryGetValue(prototypeId, out Dictionary<string, PetFoodPerf> byRange)) return 0f;
            return PetLevelRange.Pick(byRange, level)?.Vigor ?? 0f;
        }

        /// <summary>EXP ที่ต้องใช้เลื่อนเลเวลถัดไป — pet_exp.json (สูตรมี Max() จึงใช้ PetFormula ไม่ใช่ Formula ของไฟล์ item)</summary>
        public static int RequiredExp(ushort petEntityType, int level)
        {
            ExpFileEntry entry = Exp.GetValueOrDefault(petEntityType.ToString(CultureInfo.InvariantCulture));
            if (entry == null) return 0;
            var vars = new Dictionary<string, double> { { "level", level } };
            return PetFormula.TryEval(entry.RequiredExp, vars, out double v) ? (int)Math.Max(0, v) : 0;
        }

        /// <summary>แท็ก milestone ทั้งหมด — tags.json ที่ required_performance = "animal_stat" (25 ตัว)</summary>
        public static IReadOnlyList<MilestoneTag> MilestoneTags
        {
            get
            {
                EnsureMilestoneTags();
                return _milestoneTags;
            }
        }

        [CanBeNull]
        public static MilestoneTag MilestoneTagOf(string id)
        {
            EnsureMilestoneTags();
            return id == null ? null : _milestoneTagById.GetValueOrDefault(id);
        }

        public static int MilestoneTagMaxLevel(string id) => MilestoneTagOf(id)?.MaxLevel ?? 1;

        private static void EnsureMilestoneTags()
        {
            if (_milestoneTags != null) return;
            _milestoneTags = new List<MilestoneTag>();
            _milestoneTagById = new Dictionary<string, MilestoneTag>();
            var file = Json.ReadFromFile<Dictionary<string, TagFileEntry>>("tags");
            if (file == null) return;
            foreach (KeyValuePair<string, TagFileEntry> pair in file)
            {
                if (pair.Value?.RequiredPerformance != "animal_stat") continue;
                if (pair.Value.Modifiers == null || pair.Value.Modifiers.Count == 0) continue;
                KeyValuePair<string, TagModifier> mod = pair.Value.Modifiers.First();
                var tag = new MilestoneTag
                {
                    Id = pair.Key,
                    Target = MapModifierToDerived(mod.Key),
                    // function "ratio" = คูณ, "incr" = บวก — ชื่อฟังก์ชันมาจากไฟล์ตรง ๆ
                    // ⚠️ แท็กชื่อ *_amplifier_* ในไฟล์ประกาศ function = "incr" แต่ความหมายคือสัดส่วน
                    //    (สูตรให้เลข 0.05 = 5% ไม่ใช่ +0.05 หน่วย) ⇒ ตัดสินจากชื่อแท็กแทน
                    IsRatio = mod.Key.EndsWith("_amplifier", StringComparison.Ordinal) ||
                              mod.Value?.Function == "ratio",
                    MaxLevel = Math.Max(1, pair.Value.MaxLevel),
                    Formula = mod.Value?.Formula
                };
                _milestoneTags.Add(tag);
                _milestoneTagById[tag.Id] = tag;
            }
        }

        /// <summary>ชื่อ modifier ในไฟล์ → ช่อง Derived ที่ client เอาไปโชว์ (Invalid = ยังไม่มีที่ลง)</summary>
        private static Derived MapModifierToDerived(string modifier) => modifier switch
        {
            "speed_amplifier" or "speed_plus" => Derived.Speed,
            "attack_amplifier" or "attack_plus" => Derived.Attack,
            "defense_amplifier" or "defense_plus" => Derived.Defense,
            "accuracy_amplifier" or "accuracy_plus" => Derived.Accuracy,
            "inventory_amplifier" or "inventory_plus" => Derived.InventoryCapacity,
            "hungry_max_amplifier" or "hungry_max_plus" => Derived.HungryMax,
            "life_max_amplifier" or "life_max_plus" => Derived.LifeMax,
            "life_span_plus" => Derived.LifeSpan,
            // ที่เหลือ (stamina_*, life_regen_*, hungry_velocity_*, product_quantity_*) ยังไม่มีช่อง
            // ใน PetStatistics ที่ client อ่าน ⇒ เก็บแท็กไว้แต่ยังไม่ให้ผลกับตัวเลขบนจอ
            _ => Derived.Invalid
        };

        /// <summary>เงื่อนไข+น้ำหนักกาชาสกิลแอคทีฟ — pet_active_skill_conditions.json (ข้อมูลจริง)</summary>
        public static IReadOnlyList<ActiveSkillCondition> ActiveSkillConditions
        {
            get
            {
                if (_skillConditions != null) return _skillConditions;
                _skillConditions = new List<ActiveSkillCondition>();
                var file = Json.ReadFromFile<Dictionary<string, Dictionary<string, SkillCondFileEntry>>>(
                    "pet/pet_active_skill_conditions");
                if (file == null) return _skillConditions;
                foreach (KeyValuePair<string, Dictionary<string, SkillCondFileEntry>> skill in file)
                {
                    foreach (KeyValuePair<string, SkillCondFileEntry> byRank in skill.Value)
                    {
                        if (!int.TryParse(byRank.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rank))
                        {
                            continue;
                        }
                        _skillConditions.Add(new ActiveSkillCondition
                        {
                            SkillId = skill.Key,
                            Rank = (SkillRank)rank,
                            Weight = byRank.Value.Weight,
                            ForFightable = byRank.Value.ForFightable,
                            ForRidable = byRank.Value.ForRidable,
                            EntityTypes = byRank.Value.EntityType == null
                                ? new HashSet<ushort>()
                                : new HashSet<ushort>(byRank.Value.EntityType.Select(t => (ushort)t)),
                            TagCondition = byRank.Value.TagCondition
                        });
                    }
                }
                return _skillConditions;
            }
        }

        /// <summary>ชื่อชุดแอนิเมชันของสกิล — pet_active_skills.json → &lt;skillId&gt; → &lt;rank&gt; → action_set</summary>
        public static string ActiveSkillClip(string skillId, SkillRank rank)
        {
            _skills ??= Json.ReadFromFile<Dictionary<string, Dictionary<string, SkillFileEntry>>>("pet/pet_active_skills")
                        ?? new Dictionary<string, Dictionary<string, SkillFileEntry>>();
            if (skillId == null || !_skills.TryGetValue(skillId, out Dictionary<string, SkillFileEntry> byRank))
            {
                return string.Empty;
            }
            string key = ((int)rank).ToString(CultureInfo.InvariantCulture);
            return byRank.GetValueOrDefault(key)?.ActionSet ?? string.Empty;
        }

        // ── constants.json → pet ────────────────────────────────────────────────────
        public static class Constants
        {
            private class Root
            {
                [JsonProperty("pet")] public PetNode Pet { get; set; }
            }

            private class PetNode
            {
                [JsonProperty("default_grazable_count")] public int DefaultGrazableCount { get; set; }
                [JsonProperty("reinify_tags")] public string[] ReinifyTags { get; set; }
                [JsonProperty("resurrection_tags")] public string[] ResurrectionTags { get; set; }
                [JsonProperty("default_feed_energy")] public float DefaultFeedEnergy { get; set; }
                [JsonProperty("milestone_level")] public Dictionary<string, int[][]> MilestoneLevel { get; set; }
                [JsonProperty("active_skill_levels")] public int[] ActiveSkillLevels { get; set; }
            }

            private static Root _root;
            private static Root Data => _root ??= Json.ReadFromFile<Root>("constants") ?? new Root();

            /// <summary>constants.json → pet → default_grazable_count (ของจริง = 5)</summary>
            public static int GrazableCount => Data.Pet?.DefaultGrazableCount ?? 0;

            /// <summary>constants.json → pet → resurrection_tags (ของจริง = ["medicine_animal"])</summary>
            public static IReadOnlyList<string> ResurrectionTags =>
                Data.Pet?.ResurrectionTags ?? Array.Empty<string>();

            /// <summary>constants.json → pet → reinify_tags (ของจริง = ["reinify"]) — ยังไม่ได้ใช้ เพราะ ReinifyPet ตอบ Abort</summary>
            public static IReadOnlyList<string> ReinifyTags =>
                Data.Pet?.ReinifyTags ?? Array.Empty<string>();

            /// <summary>constants.json → pet → milestone_level → "&lt;จำนวนช่อง&gt;" → [[เลเวลที่ปลด, ไอดีตาราง], …]</summary>
            public static (int Level, int TableId)[] MilestoneLevels(int count)
            {
                int[][] raw = Data.Pet?.MilestoneLevel?
                    .GetValueOrDefault(count.ToString(CultureInfo.InvariantCulture));
                if (raw == null) return Array.Empty<(int, int)>();
                return raw.Where(r => r != null && r.Length >= 2)
                          .Select(r => (r[0], r[1]))
                          .ToArray();
            }
        }

        // ── costs.json ──────────────────────────────────────────────────────────────
        public static class Costs
        {
            private class Root
            {
                [JsonProperty("pet_revert_milestone")] public CostNode RevertMilestone { get; set; }
                [JsonProperty("pet_revert_active_skill")] public CostNode RevertActiveSkill { get; set; }
                [JsonProperty("pet_revert_rank")] public CostNode RevertRank { get; set; }
            }

            private class CostNode
            {
                [JsonProperty("currency")] public int Currency { get; set; }
                [JsonProperty("amount")] public string Amount { get; set; }
            }

            private static Root _root;
            private static Root Data => _root ??= Json.ReadFromFile<Root>("costs") ?? new Root();

            /// <summary>costs.json → pet_revert_milestone → amount (สูตรใช้ตัวแปร pet_level กับ count)</summary>
            public static Money MilestoneRetryCost(int petLevel, int count) =>
                Eval(Data.RevertMilestone, new Dictionary<string, double>
                {
                    { "pet_level", petLevel },
                    { "count", count },
                    { "level", petLevel }
                });

            /// <summary>costs.json → pet_revert_active_skill → amount = "100 + min(count, 9) * 100"</summary>
            public static Money ActiveSkillRetryCost(int count) =>
                Eval(Data.RevertActiveSkill, new Dictionary<string, double> { { "count", count } });

            private static Money Eval(CostNode node, Dictionary<string, double> vars)
            {
                if (node == null) return Money.ForFree;
                var currency = (Currency)node.Currency;
                if (string.IsNullOrEmpty(node.Amount)) return new Money(0, currency);
                if (PetFormula.TryEval(node.Amount, vars, out double v)) return new Money((int)Math.Max(0, v), currency);
                // คิดสูตรไม่ได้ = ไม่คิดเงิน ดีกว่าเดาราคา — และต้องเห็นใน log
                Console.WriteLine($"[pet] คิดสูตรราคาไม่ได้: \"{node.Amount}\" — คิดเป็น 0");
                return new Money(0, currency);
            }
        }

        /// <summary>
        /// คีย์ช่วงเลเวลของ performance.json เป็นข้อความ "[minLv, maxLv]"
        /// (ของสัตว์มีช่วงเดียวคือ "[1, 60]" แต่ pet_food มีหลายช่วง)
        /// เป็นตัวเดียวกับ LevelRange ใน Player.Inventory.cs แต่ตั้งชื่อใหม่เพราะคนละไฟล์ห้ามแก้กัน
        /// </summary>
        private static class PetLevelRange
        {
            [CanBeNull]
            public static T Pick<T>(Dictionary<string, T> byRange, int level) where T : class
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

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ตัวคิดสูตรของไฟล์ข้อมูลสายสัตว์
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ทำไมไม่ใช้ <c>Formula</c> ที่มีอยู่แล้วใน Core/Player.Inventory.cs: ตัวนั้นรองรับแค่
    /// เลข/level/+-*//int() และตั้งใจ throw เมื่อเจอ <c>**</c> — แต่ไฟล์ข้อมูลสายสัตว์ใช้มากกว่านั้น
    ///   pet_exp.json      "100+Max(0, (level-20)*(level*2)) - 0.0007*(level*level*level) …"
    ///   costs.json        "100 + min(count, 9) * 100" · "int(1.25*(… pow((0.05*pet_level),2.39) …))"
    ///   animal.json       "(0.895 * ((combat_level+24) ** 2)) * unstable_factor"
    /// ⇒ ต้องรองรับเพิ่ม: ฟังก์ชันหลายอาร์กิวเมนต์ (max/min/pow) · ตัวดำเนินการ <c>**</c> ·
    ///    ตัวแปรหลายตัว (level / combat_level / unstable_factor / pet_level / count)
    ///
    /// เจอสิ่งที่ไม่รู้จัก → คืน false ให้ผู้เรียกตัดสินใจเอง **ไม่เดาค่าให้** (กฎข้อ 1 ของโปรเจกต์)
    /// </summary>
    private static class PetFormula
    {
        public static bool TryEval(string expr, IReadOnlyDictionary<string, double> vars, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(expr)) return false;
            int pos = 0;
            try
            {
                double result = Additive(expr, ref pos, vars);
                Skip(expr, ref pos);
                if (pos != expr.Length) return false;
                if (double.IsNaN(result) || double.IsInfinity(result)) return false;
                value = result;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static double Additive(string s, ref int i, IReadOnlyDictionary<string, double> vars)
        {
            double left = Multiplicative(s, ref i, vars);
            while (true)
            {
                Skip(s, ref i);
                if (i >= s.Length || (s[i] != '+' && s[i] != '-')) return left;
                char op = s[i++];
                double right = Multiplicative(s, ref i, vars);
                left = op == '+' ? left + right : left - right;
            }
        }

        private static double Multiplicative(string s, ref int i, IReadOnlyDictionary<string, double> vars)
        {
            double left = Power(s, ref i, vars);
            while (true)
            {
                Skip(s, ref i);
                if (i + 1 < s.Length && s[i] == '*' && s[i + 1] == '*') return left;   // ให้ Power จัดการ
                if (i >= s.Length || (s[i] != '*' && s[i] != '/')) return left;
                char op = s[i++];
                double right = Power(s, ref i, vars);
                left = op == '*' ? left * right : left / right;
            }
        }

        /// <summary>ยกกำลังแบบ python (<c>**</c>) — ผูกขวา เช่น 2**3**2 = 2**(3**2)</summary>
        private static double Power(string s, ref int i, IReadOnlyDictionary<string, double> vars)
        {
            double left = Unary(s, ref i, vars);
            Skip(s, ref i);
            if (i + 1 < s.Length && s[i] == '*' && s[i + 1] == '*')
            {
                i += 2;
                double right = Power(s, ref i, vars);
                return Math.Pow(left, right);
            }
            return left;
        }

        private static double Unary(string s, ref int i, IReadOnlyDictionary<string, double> vars)
        {
            Skip(s, ref i);
            if (i < s.Length && s[i] == '-')
            {
                i++;
                return -Unary(s, ref i, vars);
            }
            if (i < s.Length && s[i] == '+')
            {
                i++;
                return Unary(s, ref i, vars);
            }
            return Primary(s, ref i, vars);
        }

        private static double Primary(string s, ref int i, IReadOnlyDictionary<string, double> vars)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException();
            if (s[i] == '(')
            {
                i++;
                double inner = Additive(s, ref i, vars);
                Skip(s, ref i);
                if (i >= s.Length || s[i] != ')') throw new FormatException();
                i++;
                return inner;
            }
            if (char.IsLetter(s[i]) || s[i] == '_')
            {
                int start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                string name = s.Substring(start, i - start);
                Skip(s, ref i);
                if (i < s.Length && s[i] == '(')
                {
                    i++;
                    var args = new List<double>();
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ')') i++;
                    else
                    {
                        while (true)
                        {
                            args.Add(Additive(s, ref i, vars));
                            Skip(s, ref i);
                            if (i >= s.Length) throw new FormatException();
                            if (s[i] == ',') { i++; continue; }
                            if (s[i] == ')') { i++; break; }
                            throw new FormatException();
                        }
                    }
                    return Call(name, args);
                }
                if (vars != null && vars.TryGetValue(name, out double v)) return v;
                throw new FormatException();
            }
            int numStart = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (i == numStart) throw new FormatException();
            if (!double.TryParse(s.Substring(numStart, i - numStart), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double number))
            {
                throw new FormatException();
            }
            return number;
        }

        private static double Call(string name, List<double> args)
        {
            // ชื่อฟังก์ชันในไฟล์ปนตัวใหญ่ตัวเล็ก (Max ใน pet_exp.json แต่ min/max/pow ใน costs.json)
            switch (name.ToLowerInvariant())
            {
                case "int" when args.Count == 1: return Math.Truncate(args[0]);
                case "abs" when args.Count == 1: return Math.Abs(args[0]);
                case "exp" when args.Count == 1: return Math.Exp(args[0]);
                case "floor" when args.Count == 1: return Math.Floor(args[0]);
                case "ceil" when args.Count == 1: return Math.Ceiling(args[0]);
                case "pow" when args.Count == 2: return Math.Pow(args[0], args[1]);
                case "max" when args.Count >= 1: return args.Max();
                case "min" when args.Count >= 1: return args.Min();
                default: throw new FormatException();
            }
        }
    }
}
