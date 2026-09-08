using System;
using System.Collections.Generic;
using Durango.Network;
using Durango.Utils;
using Messages;
using Newtonsoft.Json;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  ชีวิตประจำวัน: กินน้ำ / อาบน้ำ / ฟื้นคืนชีพด้วย CPR / ฉายา / คลังของ / เครื่องประดับ
//
//  รวม handler ที่เหลืออยู่ของ "สิ่งที่ผู้เล่นทำกับตัวเองและกับของรอบตัว" ซึ่งเซิร์ฟยังไม่เคย
//  ลงทะเบียนเลย ⇒ ตัวเกมยิงมาแล้วเงียบ และหลายตัวทำให้ UI ค้างถาวรโดยไม่มี error ให้เห็น
//
//  แบ่งได้ 3 กลุ่มตามระดับที่เซิร์ฟรองรับจริง:
//    (ก) ทำได้จริงครบ — กินน้ำ/อาบน้ำ/มองบรรยากาศ (แค่ตัวจับเวลา) · CPR ชุบชีวิต ·
//        จัดแท็บคลังของ (เปลี่ยนชื่อ/ลบ/เรียงลำดับ) · ถอดเครื่องประดับ · หาตำแหน่งเป้าหมายให้ไกด์
//    (ข) ตอบ "โครงว่างที่ถูกชนิด" — Tool_Collectibles (หน้าต่าง cheat ของนักพัฒนา)
//    (ค) ทำจริงไม่ได้ ⇒ ตอบ Abort ที่**มีข้อความเสมอ** (default(Abort) ทำฝั่งเกมแครช) —
//        ตั้งชื่อสิ่งปลูกสร้าง · reacting prop · รับรางวัลที่ปรึกษา · ติดเครื่องประดับ · เลือกฉายา
//
//  ⚠️ ตัวที่ตอบ Abort ทั้งหมดเป็นข้อความแบบ "ยิงแล้วไม่รอคำตอบ" ของฝั่งเกม แต่ Abort ยังไปถึง
//     ผู้เล่นอยู่ดี เพราะ client/Durango.Network/Connection.cs:882-885 ตกกลับไปหา handler กลาง
//     เมื่อ seq นั้นไม่มี handler เฉพาะ แล้ว client/GameManager.cs:309-312 เอา Text ไปขึ้น
//     SystemMsg ⇒ ผู้เล่นได้รู้ว่า "ยังไม่เปิดใช้งาน" แทนที่จะกดแล้วเงียบ
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    // ── สถานะการช่วยชีวิต (CPR) ที่ค้างอยู่ของ "ผู้เล่นที่ตาย" คนนี้ ────────────────────
    //
    // ⚠️ อยู่ในหน่วยความจำต่อ connection เท่านั้น — ตั้งใจ เพราะข้อเสนอช่วยชีวิตมีอายุไม่กี่วินาที
    // (เหตุผลเดียวกับ _deathCount ใน Core/Player.Combat.cs:313)

    /// <summary>entity ของคนที่กำลังปั๊มหัวใจให้เราอยู่ — null = ไม่มีใครเสนอ</summary>
    private string _lifeRescuerId;

    /// <summary>ข้อเสนอช่วยชีวิตหมดอายุเมื่อไหร่ (unix time)</summary>
    private double _lifeRescueValidUntil;

    /// <summary>
    /// id ของที่ผู้เล่นตั้งไว้เป็น "ค่าตอบแทนคนที่มาช่วยชีวิต" — client/InventorySystem.cs:597-603
    /// เก็บไว้เพื่อส่งต่อใน <c>ResurrectionReady.RewardItemIds</c> ให้ถูกตามโปรโตคอล
    /// </summary>
    private string[] _lifeRewardItemIds;

    private void RegisterLifeHandlers()
    {
        // ══════════════════════════════════════════════════════════════════════════
        //  กลุ่ม 1 — ท่าทางที่ใช้เวลา: ตอบ Timer(1134) แล้วจบ
        // ══════════════════════════════════════════════════════════════════════════

        // DrinkWater (3492) — client/InteractionSystem.cs:884-893 DrinkWater()
        //   Send(default(DrinkWater)).On<Messages.Timer>(msg => _drinkWaterTimer.Play(msg.Duration))
        //   .Rest(() => _drinkWaterTimer.Stop())
        // ⇒ ไม่ตอบ = หลอดที่ตั้งไว้ล่วงหน้า (Ping + 10 วิ) ถูก Rest สั่งหยุดทันที ท่าดื่มน้ำไม่เล่น
        //
        // เซิร์ฟ **ไม่มีหลอดความกระหาย** (Core/SurvivalState.cs:86-91 มีแค่ life/health/stamina/
        // energy/fatigue/groggy) และ constants.json → drink_water มีแค่ {"duration": 5}
        // ไม่มี energy/effort เหมือน put_water_in_container ⇒ ไม่มีอะไรให้หัก ไม่มีอะไรให้เติม
        // ตอบเวลาอย่างเดียวคือคำตอบที่ครบแล้วสำหรับข้อมูลที่มี
        //
        // ไม่ตรวจว่ายืนอยู่ในน้ำจริงไหม: ฝั่งเกมกันไว้แล้วที่ client/InteractionSystem.cs:758-769
        // (ต้อง Floor 0 + น้ำไม่เกินเอว + IsDrinkable(biome)) และเพราะไม่มีสถานะฝั่งเซิร์ฟเปลี่ยน
        // ยิงซ้ำก็ไม่ได้อะไร ⇒ ด่านฝั่งเซิร์ฟไม่คุ้มกับความเสี่ยงที่จะปฏิเสธคนที่ยืนถูกที่
        _connection.Recv(delegate(DrinkWater msg, PacketHeader header)
        {
            Send(new Messages.Timer { Duration = LifeConstants.DrinkWaterDuration }, header.Seq);
            // [7 ก.ย. 2026] ใส่ drink_water ตาม duration ใน status_effects.json (180 วิ)
            ApplyTimedStatusEffect("drink_water", 1);
            SendStatusEffects();
        });

        // WashBody (3494) — client/InteractionSystem.cs:872-881 รูปแบบเดียวกับ DrinkWater เป๊ะ
        // constants.json → wash_body = {"duration": 5}
        // ล้าง dirty แล้วใส่ clean ตาม duration ใน status_effects.json (360 วิ)
        _connection.Recv(delegate(WashBody msg, PacketHeader header)
        {
            Send(new Messages.Timer { Duration = LifeConstants.WashBodyDuration }, header.Seq);
            ClearTimedStatusEffect("dirty");
            ApplyTimedStatusEffect("clean", 1);
            SendStatusEffects();
        });

        // LookAroundMood (234789) — "มองบรรยากาศ" ในบ้านที่ตกแต่งไว้
        // client/InteractionSystem.cs:896-909 ArtifactLookAround → .On<Messages.Timer>
        // (เรียกจาก client/Durango.UI/InteractionGroup.cs:558)
        // เวลาเป็นข้อมูลจริง: constants.json → artifact_mood.looking_around_time = 5
        //
        // **ยังไม่ให้ผลอะไรต่อ**: ของจริงมองแล้วได้สถานะจาก interior_mood (artifact_mood
        // .required_stat_type = "comfort" · tag_category = "interior_mood") ซึ่งต้องมีระบบ
        // status effect + ค่าสถิติของสิ่งปลูกสร้างก่อน — เซิร์ฟยังไม่มีทั้งคู่
        // ⇒ ตอบเวลาให้ท่าเล่นครบ ดีกว่าเงียบแล้วตัวละครยืนค้าง
        _connection.Recv(delegate(LookAroundMood msg, PacketHeader header)
        {
            Send(new Messages.Timer { Duration = LifeConstants.LookAroundTime }, header.Seq);
            // มองบรรยากาศตอนยืนในบ้าน — ใส่บัพ inside ถ้ายังไม่มี (เดินเข้าก็ใส่แล้ว)
            _insideCheckedTile = new Point2(int.MinValue, int.MinValue);
            if (SyncInsideStatusEffect()) SendStatusEffects();
        });

        // ══════════════════════════════════════════════════════════════════════════
        //  กลุ่ม 2 — ช่วยชีวิตเพื่อน (CPR)
        // ══════════════════════════════════════════════════════════════════════════

        // Resurrect (132) — **คนช่วยเป็นคนยิง** หลังเล่นมินิเกมปั๊มหัวใจจบ
        // client/CPRSystem.cs:176-188 CPRResult(score) → Send(new Resurrect{EntityId=คนตาย, Score})
        // (score มาจาก client/Durango.UI/CPRGroup.cs:436 _cprGauge.Get() — หลอด 0..100)
        // ไม่ผูก .On ⇒ ผลลัพธ์เดินทางไปหา "คนตาย" เป็น push คนละ connection
        _connection.Recv(delegate(Resurrect msg, PacketHeader header)
        {
            HandleResurrectMsg(msg);
        });

        // ConfirmResurrection (56238474) — **คนตายเป็นคนยิง** หลังกดตกลงบนกล่องข้อความ
        // client/CPRSystem.cs:209-236 OnResurrectionReady → MessageBox → ConfirmResurrection
        _connection.Recv(delegate(ConfirmResurrection msg, PacketHeader header)
        {
            HandleConfirmResurrectionMsg(msg);
        });

        // SetResurrectionRewards (133) — ผู้เล่นเลือกของในกระเป๋าไว้เป็นค่าตอบแทนคนมาช่วย
        // client/InventorySystem.cs:597-603 SetResurrectionReward(string[]) ยิงเฉย ๆ ไม่รอคำตอบ
        _connection.Recv(delegate(SetResurrectionRewards msg, PacketHeader header)
        {
            HandleSetResurrectionRewardsMsg(msg);
        });

        // ══════════════════════════════════════════════════════════════════════════
        //  กลุ่ม 3 — คลังของ: จัดการแท็บ (rename / remove / reorder)
        // ══════════════════════════════════════════════════════════════════════════
        //
        // ทั้งสามตัวยิงแบบไม่รอคำตอบ แล้ว UI รอ **push SectionUpdated(5298430)** อย่างเดียว
        // (client/InventorySystem.cs:81 On<SectionUpdated> → :226 → :509 UpdateSection)
        // ⇒ ไม่มี handler = กดเปลี่ยนชื่อ/ลบแท็บแล้วหน้าจอไม่ขยับเลยสักนิด

        // RenameWarehouseSection (3696) — client/InventorySystem.cs:667-678
        _connection.Recv(delegate(RenameWarehouseSection msg, PacketHeader header)
        {
            HandleRenameWarehouseSectionMsg(msg, header.Seq);
        });

        // RemoveSection (3687) — client/InventorySystem.cs:714-746 RemoveWarehouseCategory
        // ฝั่งเกมกันไว้แล้วว่าแท็บต้องว่าง (:733-738) — เราตรวจซ้ำฝั่งเซิร์ฟ ห้ามเชื่อ client
        _connection.Recv(delegate(RemoveSection msg, PacketHeader header)
        {
            HandleRemoveSectionMsg(msg, header.Seq);
        });

        // SetSectionOrder (3688) — client/InventorySystem.cs:682-692 SetWarehouseCategoryList
        _connection.Recv(delegate(SetSectionOrder msg, PacketHeader header)
        {
            HandleSetSectionOrderMsg(msg, header.Seq);
        });

        // ══════════════════════════════════════════════════════════════════════════
        //  กลุ่ม 4 — ฉายา (title) / ที่ปรึกษา (advisor)
        // ══════════════════════════════════════════════════════════════════════════

        // SelectTitle (2046) — client/StatisticsSystem.cs:133-140 (กดจาก
        // client/Durango.UI/EquipWidgetBase.cs:101) ยิงแบบไม่รอคำตอบ
        //   TitleId = (title == null || !title.Enabled) ? null : title.Id
        // "Enabled" ถูกตั้งจาก Titles(msg.TitleIds) ที่เซิร์ฟส่งเท่านั้น
        // (client/StatisticsSystem.cs:195-213) และเซิร์ฟตอบชุดว่างอยู่
        // (Core/Player.cs:300-303 GetTitles → Titles{TitleIds = ว่าง})
        // ⇒ ตามข้อมูลของเกมเอง **ยังไม่มีใครมีฉายาสักอัน** ค่าที่จะได้รับจริง ๆ คือ null = "ถอดฉายา"
        _connection.Recv(delegate(SelectTitle msg, PacketHeader header)
        {
            HandleSelectTitleMsg(msg, header.Seq);
        });

        // CancelTargetTitle (3901) — ยกเลิก "วิชาที่กำลังเรียน" ของระบบไกด์
        // client/Durango.Logic/LearningGuideSystem.cs:105-116 CancelCurriculum
        //   Send(default(CancelTargetTitle)).On<OK>(...) ⇒ **ต้องตอบ OK** ไม่งั้นข้อความ
        //   "ยกเลิกไกด์แล้ว" ไม่ขึ้น และ ClearTargetAdvice() ไม่ถูกเรียก = ปุ่มค้างสถานะเดิมตลอด
        //
        // เซิร์ฟตอบ TargetTitle{TitleId = null} อยู่แล้ว (Core/Player.cs:372-375) = ไม่มีวิชา
        // ที่ตั้งไว้ ⇒ "ยกเลิก" สำเร็จโดยไม่ต้องทำอะไร ตอบ OK จึงเป็นความจริง ไม่ใช่การหลอก
        _connection.Recv(delegate(CancelTargetTitle msg, PacketHeader header)
        {
            Send(default(OK), header.Seq);
        });

        // ReceiveAdvisorReward (3908) — รับรางวัลของวิชาที่เรียนจบ
        // client/Durango.Logic/LearningGuideSystem.cs:118-127 ReceiveReward(titleId).On<OK>
        //
        // ⚠️ **ห้ามตอบ OK** — OK แปลว่า "จ่ายรางวัลแล้ว" แล้วฝั่งเกมจะ ClearTargetAdvice() +
        // UpdateAchievementInfo() ทิ้งสิทธิ์รางวัลนั้นไปโดยผู้เล่นไม่ได้ของอะไรเลย
        // เซิร์ฟตอบ AdvisorTargets{RemainingRewards = ว่าง} (Core/Player.cs:367-373) = ไม่มี
        // รางวัลค้างอยู่จริง ⇒ Abort พร้อมข้อความ ตรงกับสภาพจริงและผู้เล่นได้เห็นเหตุผล
        _connection.Recv(delegate(ReceiveAdvisorReward msg, PacketHeader header)
        {
            Console.WriteLine($"[ไกด์] {Short(EntityId)} ขอรับรางวัลที่ปรึกษา '{msg.TitleId}' — ยังไม่มีระบบรางวัล");
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบรางวัลที่ปรึกษา" }, header.Seq);
        });

        // ══════════════════════════════════════════════════════════════════════════
        //  กลุ่ม 5 — เครื่องประดับ (ธงประดับหลังตัวละคร)
        // ══════════════════════════════════════════════════════════════════════════

        // AttachAccessory (9823459) — client/EquipSystem.cs:265-275 (กดจาก
        // client/Durango.UI/CharacterInfoWidget.cs:113) ยิงแบบไม่รอคำตอบ
        //
        // เซิร์ฟตอบ AttachableAccessories ชุดว่าง (Core/Player.Inventory.cs:309-323) เพราะ
        // accessories.json มี 3 รายการและทั้งหมดปลดล็อกด้วย "ป้องกันฐานแคลนสำเร็จติดกัน N ครั้ง"
        // ซึ่งยังไม่มีระบบแคลน ⇒ **ไม่มี id ไหนที่ผู้เล่นมีสิทธิ์ติดได้ตามนิยามของข้อมูลเอง**
        // รับ id อะไรก็ได้จาก client = แจกของที่ข้อมูลเกมบอกว่าต้องหามาด้วยเงื่อนไข
        _connection.Recv(delegate(AttachAccessory msg, PacketHeader header)
        {
            Console.WriteLine($"[เครื่องประดับ] {Short(EntityId)} ขอติด '{msg.AccessoryId}' — ยังไม่มีสิทธิ์ (ต้องมีระบบแคลน)");
            Send(new Abort { Text = "ยังไม่มีเครื่องประดับที่ติดได้" }, header.Seq);
        });

        // ResetAccessory (9823460) — client/EquipSystem.cs:267-271 (ส่งเมื่อเลือก "ไม่ติดอะไร")
        // ตัวนี้ทำได้จริง เพราะ "ถอด" = ล้างช่อง PlayerDisplay.Accessory (GameCode/Messages/
        // PlayerDisplay.cs:66) ซึ่งเซิร์ฟเป็นเจ้าของอยู่แล้ว
        _connection.Recv(delegate(ResetAccessory msg, PacketHeader header)
        {
            HandleResetAccessoryMsg();
        });

        // ══════════════════════════════════════════════════════════════════════════
        //  กลุ่ม 6 — เบ็ดเตล็ด
        // ══════════════════════════════════════════════════════════════════════════

        // GiveUpDistribution (451390) — "สละสิทธิ์แบ่งของ" ของกองที่คนอื่นเก็บอยู่
        // client/GatheringSystem.cs:438-444 (เมนูจาก client/Durango.UI/InteractionGroup.cs:386-397)
        // ยิงแบบไม่รอคำตอบและไม่มี push ตัวไหนตามมา
        //
        // เซิร์ฟยังไม่มีระบบ "สิทธิ์แบ่งของ" เลย (grep "Distribution" ใน server/Core/ ไม่เจออะไร)
        // — การเก็บของเป็นของใครของมันอยู่แล้ว (Core/Player.Gathering.cs) ⇒ ไม่มีสิทธิ์ให้สละ
        // รับเงียบ ๆ กัน log "ไม่มี handler" · **ห้ามตอบ Abort** เพราะจะเด้ง SystemMsg ให้ผู้เล่น
        // ทั้งที่เขาไม่ได้ทำอะไรผิด
        _connection.Recv(delegate(GiveUpDistribution msg, PacketHeader header)
        {
        });

        // Rename (324) — ตั้ง/เปลี่ยนชื่อสิ่งปลูกสร้าง
        // client/Durango.Logic.Interactions/ArtifactInteractions.cs:678-691 (ตั้งชื่อครั้งแรก)
        // และ :696-731 (เปลี่ยนชื่อ เสียค่าใช้จ่ายตาม costs.json → ArtifactRename)
        _connection.Recv(delegate(Rename msg, PacketHeader header)
        {
            HandleRenameArtifactMsg(msg, header.Seq);
        });

        // ContactReactingProp (78452083) — แตะ "prop ที่โต้ตอบได้" (ช่วยคนด้วยอาหาร/น้ำ/ยา ·
        // ยืนยันตัวตน · ส่งเอกสาร · ภารกิจ epic)
        // client/Durango.Logic.Interactions/ReactingPropInteractions.cs:156-198 รอถึง 3 ชนิด
        // บน seq เดียว: Timer(1134) → OK(1) → ReactingPropRewarded และมี .Rest คอยยกเลิกท่า
        //
        // เมนูนี้จะโผล่ได้ต่อเมื่อเซิร์ฟส่ง Touched.ReactingProp มาให้ (:75-79 อ่านจาก
        // LastTouched.ReactingProp) — grep "ReactingProp" ใน server/Core/ **ไม่เจอเลยสักที่**
        // ⇒ เข้าถึงไม่ได้จริงในตอนนี้ ลงทะเบียนไว้เป็นด่านกันพลาด
        //
        // ตอบ Abort ที่ seq เดิม ⇒ .Rest ของฝั่งเกม (:194-197 !Packet.IsSuccess) เรียก
        // onCancelAction() คืนท่าทางให้ตัวละคร = ทางลงที่เกมออกแบบไว้เอง ไม่ใช่ค้างกลางอากาศ
        _connection.Recv(delegate(ContactReactingProp msg, PacketHeader header)
        {
            Console.WriteLine($"[prop] {Short(EntityId)} แตะ reacting prop {Short(msg.EntityId)} — เซิร์ฟยังไม่มีระบบนี้");
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบนี้" }, header.Seq);
        });

        // FindTargetEntityPosition (3950) — ไกด์สั่ง "ไปหาของชนิดนี้" แล้วขอพิกัดมาปักลูกศร
        // client/Durango.UI/PlayGuideHelperGroupBase.cs:336-346
        //   Send(new FindTargetEntityPosition{EntityType, ReasonFindTarget.PlayGuide})
        //     .On<TargetEntityPosition>(msg => OnHelperTileChanged(helper, msg.Tile))
        // Tile เป็น nullable — ฝั่งเกมรับ null ได้ (:363-368 → GetHelperTileClientPosition คืน
        // Vector3.zero) ⇒ ตอบ null ปลอดภัยกว่าไม่ตอบ (ไม่ตอบ = ลูกศรค้างจุดเดิมตลอดบท)
        _connection.Recv(delegate(FindTargetEntityPosition msg, PacketHeader header)
        {
            Send(new TargetEntityPosition { Tile = FindNearestEntityTile(msg.EntityType) }, header.Seq);
        });

        // Tool_Collectibles (328) — หน้าต่าง cheat ของนักพัฒนา
        // client/Durango.UI/GatheringCheatWidget.cs:44 On<Tool_Collectibles> (**handler กลาง**)
        // และ :54-57 ยิงคำขอด้วยลิสต์ว่าง ⇒ ต้องตอบแบบ ReplyOf = 0 ไม่ใช่ผูก seq
        //
        // ตอบชุดว่าง: รายชื่อของที่เก็บได้ต้องมาพร้อม "ชื่อที่แปลแล้ว" ซึ่งฝั่งเกมแกะด้วย
        // LocalizeSystem.UnpackGettextFromMsgPack (GameCode/Messages/Tool_Collectibles.cs:60)
        // เซิร์ฟไม่มีตารางแปลชื่อ ⇒ ส่งไปก็ได้ชื่อผิด/ว่าง · และนี่เป็นเครื่องมือ cheat
        // ฝั่งเกมรองรับลิสต์ว่างอยู่แล้ว (:69-82 วนอาเรย์เปล่าแล้ว UpdateCollectibles(""))
        _connection.Recv(delegate(Tool_Collectibles msg, PacketHeader header)
        {
            Send(new Tool_Collectibles { Collectibles = Array.Empty<Pair<string, string>>() });
        });
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ช่วยชีวิตด้วย CPR
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ลำดับที่เกมออกแบบไว้ (client/CPRSystem.cs ทั้งไฟล์):
    ///   1. คนช่วยเดินไปข้าง ๆ คนตาย → เล่นมินิเกมปั๊มหัวใจ (Durango.UI/CPRGroup.cs)
    ///   2. จบเกม → <c>Resurrect{EntityId = คนตาย, Score}</c>            ← ตัวนี้
    ///   3. เซิร์ฟ push <c>ResurrectionReady</c> ให้ **คนตาย** (:35 On&lt;ResurrectionReady&gt;)
    ///   4. คนตายกดตกลง → <c>ConfirmResurrection{HelperEntityId}</c>
    ///   5. เซิร์ฟชุบชีวิตจริง
    ///
    /// **การตีความของเรา — เกณฑ์คะแนน**: หลอด CPR เต็ม 100 (CPRGroup.cs:169) เพิ่มทีละ
    /// 100/จำนวนโน้ต ต่อการกดตรงจังหวะ และ **ลบ 5 เมื่อพลาด** (CPRGroup.cs:369-390)
    /// ⇒ 0 = "ไม่ได้อะไรเลย" เป็นขอบเขตธรรมชาติเพียงจุดเดียวในข้อมูล
    /// ไม่มีไฟล์ไหนบอกเกณฑ์ผ่าน (หา "cpr"/"resurrect" ใน data/assets แล้วไม่เจอตัวเลข)
    /// ⇒ ใช้ &gt; 0 · ตั้งเกณฑ์สูงกว่านี้เท่ากับแต่งตัวเลขเอง และผลคือ "ช่วยไม่ได้เลย" แบบเงียบ ๆ
    /// </summary>
    private void HandleResurrectMsg(Resurrect msg)
    {
        Player target = LifeFindPlayer(msg.EntityId);
        if (target == null)
        {
            Console.WriteLine($"[ช่วยชีวิต] {Short(EntityId)} ปั๊มหัวใจ {Short(msg.EntityId)} ไม่ได้ — ไม่พบคนนั้นบนเกาะนี้");
            return;
        }
        if (target._context.AppearPlayer.IsAlive)
        {
            // เกิดได้ปกติ: ตัวเองเพิ่งฟื้นด้วยวิธีอื่นระหว่างที่อีกฝ่ายเล่นมินิเกมอยู่ ⇒ เงียบพอ
            return;
        }
        // ด่านระยะ — ใช้ระยะเดียวกับที่ระบบสิทธิ์สิ่งปลูกสร้างใช้ (Core/Player.cs ArtifactReachTiles)
        // ไม่มีด่านนี้ = ยิง Resurrect ใส่ id ที่ได้ฟรีจากแพ็กเก็ต AppearPlayer ได้จากทั่วเกาะ
        if (!IsWithinTiles(LifeTileOf(target), ArtifactReachTiles))
        {
            Console.WriteLine($"[ช่วยชีวิต] {Short(EntityId)} ปั๊มหัวใจ {Short(msg.EntityId)} ไม่ได้ — อยู่ไกลเกินไป");
            return;
        }
        if (!(msg.Score > 0f))
        {
            Console.WriteLine($"[ช่วยชีวิต] {Short(EntityId)} ปั๊มหัวใจ {Short(msg.EntityId)} ไม่สำเร็จ (คะแนน {msg.Score:0.#})");
            return;
        }

        target._lifeRescuerId = EntityId;
        target._lifeRescueValidUntil = Times.UnixTimeNow() + LifeTuning.ResurrectionOfferSeconds;

        // RewardItemIds = ของที่ "คนตาย" ตั้งไว้เอง (ไม่ใช่ของที่เราแจก) ⇒ ไม่ใช่ข้อมูลแต่ง
        // ⚠️ ฝั่งเกมยังไม่ได้ใช้ฟิลด์นี้ (client/CPRSystem.cs:209-228 อ่านแค่ HelperEntityId
        //    กับ ValidUntil) และ **เซิร์ฟยังไม่โอนของให้คนช่วยจริง** — ดูหมายเหตุที่
        //    HandleSetResurrectionRewardsMsg
        target.Send(new ResurrectionReady
        {
            HelperEntityId = EntityId,
            ValidUntil = target._lifeRescueValidUntil,
            RewardItemIds = target._lifeRewardItemIds ?? Array.Empty<string>()
        });
        Console.WriteLine($"[ช่วยชีวิต] {Short(EntityId)} ปั๊มหัวใจสำเร็จ (คะแนน {msg.Score:0.#}) — เสนอชุบชีวิต {Short(msg.EntityId)}");
    }

    /// <summary>
    /// คนตายกดตกลง — ชุบชีวิตตรงจุดที่ล้ม
    ///
    /// ใช้ <c>HandleReviveMsg(normal: false)</c> ตัวเดียวกับ "ฟื้นทันที"
    /// (Core/Player.Combat.cs:705-741) เพราะทั้งคู่คือ "ฟื้นตรงที่ตาย ไม่ย้ายกลับจุดเกิด"
    /// ⇒ ได้หลอดคืนตามสัดส่วนจริง constants.json → revive_immediately.gauge_ratio = 0.7
    /// และไม่ต้องมีตรรกะชุบชีวิตสองชุด (หลักการเดียวกับ Core/Player.Hunting.cs:37-45)
    ///
    /// **การตีความของเรา**: ของจริง CPR น่าจะคืนหลอดคนละสัดส่วนกับการจ่ายเงินฟื้นทันที
    /// แต่ constants.json มีบล็อกสัดส่วนหลอดแค่สองบล็อก (death_penalty กับ revive_immediately)
    /// และไม่มีบล็อกไหนพูดถึง CPR เลย ⇒ เลือกบล็อกที่ "ฟื้นตรงที่" เหมือนกัน
    /// </summary>
    private void HandleConfirmResurrectionMsg(ConfirmResurrection msg)
    {
        if (_context.AppearPlayer.IsAlive) return;

        if (string.IsNullOrEmpty(_lifeRescuerId))
        {
            Console.WriteLine($"[ช่วยชีวิต] {Short(EntityId)} ยืนยันการชุบชีวิต แต่ไม่มีใครเสนอไว้");
            Send(new Abort { Text = "ไม่มีใครกำลังช่วยชีวิตคุณอยู่" });
            return;
        }
        // เทียบชื่อคนช่วยด้วย — กัน client ยืนยันข้อเสนอเก่าที่ถูกทับไปแล้ว
        if (!string.Equals(_lifeRescuerId, msg.HelperEntityId, StringComparison.Ordinal) ||
            Times.UnixTimeNow() > _lifeRescueValidUntil)
        {
            Console.WriteLine($"[ช่วยชีวิต] {Short(EntityId)} ยืนยันช้าเกินไป/ไม่ตรงคน — ยกเลิก");
            _lifeRescuerId = null;
            Send(new Abort { Text = "หมดเวลาช่วยชีวิตแล้ว" });
            return;
        }

        string rescuer = _lifeRescuerId;
        _lifeRescuerId = null;
        _lifeRescueValidUntil = 0.0;

        HandleReviveMsg(normal: false);
        Console.WriteLine($"[ช่วยชีวิต] {Short(EntityId)} ฟื้นแล้วด้วยความช่วยเหลือของ {Short(rescuer)}");
    }

    /// <summary>
    /// จำ id ของที่ตั้งเป็นค่าตอบแทนคนช่วย — กรองเฉพาะของที่อยู่ในกระเป๋าจริงตอนนี้
    ///
    /// ⚠️ **ยังไม่โอนของให้คนช่วยตอนฟื้น** และนี่เป็นการตัดสินใจ ไม่ใช่ของที่ลืม:
    /// การโอนของข้ามผู้เล่นต้องผ่านด่านของที่ล็อกไว้ (<c>_lockedItemIds</c>) · ของที่ถูกป้องกัน ·
    /// และเพดานกระเป๋าของคนรับ ซึ่งอยู่ใน Core/Player.Inventory.cs (ไฟล์นอกขอบเขตงานนี้)
    /// ทำครึ่ง ๆ กลาง ๆ = ของหายจากกระเป๋าคนตายโดยคนช่วยไม่ได้รับ
    /// ⇒ เก็บรายการไว้ให้ถูกตามโปรโตคอลก่อน ค่อยต่อการโอนทีหลัง
    /// </summary>
    private void HandleSetResurrectionRewardsMsg(SetResurrectionRewards msg)
    {
        if (msg.ItemIds == null || msg.ItemIds.Length == 0)
        {
            _lifeRewardItemIds = Array.Empty<string>();
            return;
        }

        var kept = new List<string>();
        foreach (string id in msg.ItemIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (_context.InventoryItems.FindIndex(item => item.Id == id) < 0) continue;
            kept.Add(id);
        }
        _lifeRewardItemIds = kept.ToArray();
        Console.WriteLine($"[ช่วยชีวิต] {Short(EntityId)} ตั้งค่าตอบแทนคนช่วยไว้ {kept.Count} ชิ้น (ยังไม่โอนจริง)");
    }

    /// <summary>หาผู้เล่นที่ต่ออยู่บนเกาะเดียวกัน — ต่างจาก TryResolveVictim ตรงที่ "ตายแล้วก็คืน"</summary>
    private Player LifeFindPlayer(string entityId)
    {
        if (string.IsNullOrEmpty(entityId) || entityId == EntityId) return null;
        Player other;
        lock (LivePlayers)
        {
            LivePlayers.TryGetValue(entityId, out other);
        }
        // คนละเกาะ = คนละ World (Core/GameServer.cs WorldOf) ⇒ ยุ่งกันไม่ได้
        if (other == null || !ReferenceEquals(other._world, _world)) return null;
        return other;
    }

    /// <summary>ช่องที่ผู้เล่นคนนั้นยืนอยู่ — อ่านจากเส้นทางเดินชุดล่าสุดเหมือน IsWithinTiles</summary>
    private static Point2 LifeTileOf(Player player)
    {
        Movement[] movements = player._context.AppearPlayer.Move.Movements;
        if (movements == null || movements.Length == 0 ||
            movements[0].Path == null || movements[0].Path.Length == 0)
        {
            return new Point2(-1000, -1000);   // ไม่รู้ตำแหน่ง ⇒ ให้ตกด่านระยะไปเลย
        }
        WorldPosition pos = movements[0].Path[0].Position;
        return new Point2((int)(pos.x / 200f), (int)(pos.y / 200f));
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  แท็บคลังของ
    // ══════════════════════════════════════════════════════════════════════════════════
    //
    // ⚠️ หมายเหตุสำคัญสำหรับคนที่มาต่อ: <see cref="WarehouseStore"/> (Core/Player.Inventory.cs:1012)
    // **ยังไม่มีเมธอดลบ/เปลี่ยนชื่อแท็บ** มีแค่ MakeSection/Items/SectionNames/UsedSize
    // ตัวที่แก้ลำดับได้คือ <c>SectionNames()</c> ซึ่ง ณ ตอนนี้คืน <c>store.Order</c> **ตัวจริง**
    // (ไม่ใช่สำเนา — Player.Inventory.cs:1048-1052) ⇒ แคสต์กลับเป็น List&lt;string&gt; แล้วแก้ได้
    //
    // ผลข้างเคียงที่ยอมรับไว้: ลบแท็บออกจาก Order ได้ แต่ลบคีย์ออกจาก <c>Sections</c> ไม่ได้
    // ⇒ **สร้างแท็บชื่อเดิมซ้ำไม่ได้จนกว่าจะรีสตาร์ต** (MakeSection เช็ค Sections.ContainsKey
    //    แล้วคืน false) · หายเองตอนโหลดโลกใหม่ เพราะ Import วนตาม Order ไม่ใช่ Sections
    //    (Player.Inventory.cs:1122-1140) ⇒ คีย์กำพร้าถูกทิ้งไปเอง
    // ถ้าจะแก้ให้ขาด ต้องเพิ่ม WarehouseStore.RemoveSection/RenameSection ในไฟล์นั้น

    /// <summary>ลิสต์ลำดับแท็บตัวจริงของหลังนี้ — null ถ้ายังไม่เคยเปิดคลัง หรือรูปแบบเปลี่ยนไป</summary>
    private static List<string> LifeSectionOrder(string entityId) =>
        WarehouseStore.SectionNames(entityId) as List<string>;

    private void HandleRenameWarehouseSectionMsg(RenameWarehouseSection msg, uint seq)
    {
        if (!MayTouchArtifact(msg.EntityId, "เปลี่ยนชื่อแท็บคลัง"))
        {
            Send(new Abort { Text = "ทำกับสิ่งปลูกสร้างนี้ไม่ได้" }, seq);
            return;
        }
        if (string.IsNullOrWhiteSpace(msg.NewName))
        {
            Send(new Abort { Text = "ต้องตั้งชื่อแท็บ" }, seq);
            return;
        }

        List<string> order = LifeSectionOrder(msg.EntityId);
        if (order == null || !order.Contains(msg.SectionName))
        {
            Send(new Abort { Text = "ไม่พบแท็บนี้" }, seq);
            return;
        }
        if (order.Contains(msg.NewName))
        {
            Send(new Abort { Text = "มีแท็บชื่อนี้อยู่แล้ว" }, seq);
            return;
        }

        // create: true อาจ (ก) สร้างใหม่แล้วต่อท้าย Order ให้เอง หรือ (ข) คืนคีย์กำพร้าที่ว่างอยู่
        // โดยไม่แตะ Order — จัดการทั้งสองทางเหมือนกันด้วยการ Remove ก่อนแล้วค่อยวางที่เดิม
        List<Item> destination = WarehouseStore.Items(msg.EntityId, msg.NewName, create: true);
        if (destination == null)
        {
            Send(new Abort { Text = "เปลี่ยนชื่อแท็บไม่สำเร็จ" }, seq);
            return;
        }
        if (destination.Count > 0)
        {
            // ไม่ควรเกิด (ชื่อนี้ไม่อยู่ใน Order แต่กลับมีของ) — หยุดไว้ก่อนดีกว่าเอาของไปปนกัน
            Console.WriteLine($"[คลัง] ชื่อ '{msg.NewName}' มีของค้างอยู่แต่ไม่อยู่ในลำดับแท็บ — ไม่เปลี่ยนชื่อ");
            Send(new Abort { Text = "ชื่อนี้ใช้ไม่ได้" }, seq);
            return;
        }

        List<Item> source = WarehouseStore.Items(msg.EntityId, msg.SectionName, create: false);
        if (source != null && source.Count > 0)
        {
            destination.AddRange(source);
            source.Clear();
        }

        order.Remove(msg.NewName);                       // ถ้า Items เพิ่งต่อท้ายให้ ให้เอาออกก่อน
        int index = order.IndexOf(msg.SectionName);
        if (index >= 0) order[index] = msg.NewName;      // วางแทนที่เดิม ลำดับที่ผู้เล่นจัดไว้ไม่เพี้ยน
        else order.Add(msg.NewName);

        _world.Save();
        Console.WriteLine($"[คลัง] {Short(EntityId)} เปลี่ยนชื่อแท็บ '{msg.SectionName}' → '{msg.NewName}' ใน {Short(msg.EntityId)}");

        // ⚠️ ReplyOf = 0 — ฝั่งเกมรับด้วย handler กลาง On<SectionUpdated> (client/InventorySystem.cs:81)
        // ไม่ได้ผูก .On กับคำขอ (:667-678 ยิงแล้วจบ)
        Send(new SectionUpdated
        {
            EntityId = msg.EntityId,
            Tile = msg.Tile,
            Renamed = new Pair<string, string>(msg.SectionName, msg.NewName)
        });
    }

    private void HandleRemoveSectionMsg(RemoveSection msg, uint seq)
    {
        if (!MayTouchArtifact(msg.EntityId, "ลบแท็บคลัง"))
        {
            Send(new Abort { Text = "ทำกับสิ่งปลูกสร้างนี้ไม่ได้" }, seq);
            return;
        }

        List<string> order = LifeSectionOrder(msg.EntityId);
        if (order == null || !order.Contains(msg.SectionName))
        {
            Send(new Abort { Text = "ไม่พบแท็บนี้" }, seq);
            return;
        }

        // ฝั่งเกมกันไว้แล้ว (client/InventorySystem.cs:733-738) แต่ห้ามเชื่อ client —
        // ลบแท็บที่ยังมีของ = ของหายทั้งชุดตอนเซฟรอบถัดไป (Export วนตาม Order เท่านั้น)
        List<Item> items = WarehouseStore.Items(msg.EntityId, msg.SectionName, create: false);
        if (items != null && items.Count > 0)
        {
            Send(new Abort { Text = "ต้องขนของออกจากแท็บให้หมดก่อน" }, seq);
            return;
        }

        order.Remove(msg.SectionName);
        _world.Save();
        Console.WriteLine($"[คลัง] {Short(EntityId)} ลบแท็บ '{msg.SectionName}' ใน {Short(msg.EntityId)}");

        Send(new SectionUpdated
        {
            EntityId = msg.EntityId,
            Tile = msg.Tile,
            Removed = msg.SectionName
        });
    }

    /// <summary>
    /// เรียงลำดับแท็บใหม่ตามที่ผู้เล่นลากจัด — ไม่ต้อง push อะไรกลับ
    /// เพราะฝั่งเกมจัดลำดับใน UI ของตัวเองไปแล้วก่อนยิง (client/InventorySystem.cs:682-692)
    /// </summary>
    private void HandleSetSectionOrderMsg(SetSectionOrder msg, uint seq)
    {
        if (!MayTouchArtifact(msg.EntityId, "จัดลำดับแท็บคลัง"))
        {
            Send(new Abort { Text = "ทำกับสิ่งปลูกสร้างนี้ไม่ได้" }, seq);
            return;
        }

        List<string> order = LifeSectionOrder(msg.EntityId);
        if (order == null || order.Count == 0) return;

        var sorted = new List<string>(order.Count);
        foreach (string name in msg.SectionOrder ?? Array.Empty<string>())
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (!order.Contains(name) || sorted.Contains(name)) continue;
            sorted.Add(name);
        }
        // แท็บที่ client ไม่ได้ส่งมา (เพิ่งสร้างจากอีกหน้าจอ / เวอร์ชันไม่ตรงกัน) ต้องไม่หายไป
        // — หายจาก Order = หายจากไฟล์เซฟพร้อมของข้างในทั้งหมด
        foreach (string name in order)
        {
            if (!sorted.Contains(name)) sorted.Add(name);
        }

        order.Clear();
        order.AddRange(sorted);
        _world.Save();
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ฉายา / เครื่องประดับ
    // ══════════════════════════════════════════════════════════════════════════════════

    private void HandleSelectTitleMsg(SelectTitle msg, uint seq)
    {
        if (!string.IsNullOrEmpty(msg.TitleId))
        {
            // ไม่มีฉายาไหนที่เซิร์ฟเคยมอบให้ (GetTitles ตอบชุดว่าง) ⇒ ใส่ได้เท่ากับแจกฟรี
            Console.WriteLine($"[ฉายา] {Short(EntityId)} ขอใส่ฉายา '{msg.TitleId}' — ยังไม่ได้รับฉายานี้");
            Send(new Abort { Text = "ยังไม่ได้รับฉายานี้" }, seq);
            return;
        }

        if (string.IsNullOrEmpty(_context.AppearPlayer.Title.TitleId)) return;   // ไม่มีอะไรให้ถอด

        _context.AppearPlayer.Title.EntityId = EntityId;
        _context.AppearPlayer.Title.TitleId = null;      // Title.Pack รองรับ null ตรง ๆ (PackNull)
        // ⚠️ _Title ต้องไม่เป็น null — Pack ยิง PackString(null) → ฝั่งเกมแกะด้วย
        // UnpackGettextFromMsgPack ได้ null (กับดักเดียวกับ Abort.Text)
        _context.AppearPlayer.Title._Title = string.Empty;

        // ฝั่งเกมรับด้วย handler กลาง On<Messages.Title> (client/PlayerManager.cs:363-371)
        // ⇒ ต้องกระจายให้ทุกคนบนเกาะ ไม่ใช่ตอบเฉพาะเจ้าตัว (คนอื่นจะเห็นฉายาเก่าค้างอยู่)
        _world.BroadCast(_context.AppearPlayer.Title);
        OnContextChanged();
        Console.WriteLine($"[ฉายา] {Short(EntityId)} ถอดฉายาออก");
    }

    private void HandleResetAccessoryMsg()
    {
        if (string.IsNullOrEmpty(_context.AppearPlayer.Display.Accessory)) return;

        _context.AppearPlayer.Display.Accessory = null;
        // กระจายทั้งก้อน PlayerDisplay แบบเดียวกับที่ระบบสวมใส่ทำ (Core/Player.Inventory.cs:594)
        // ฝั่งเกมรับด้วย handler กลาง On<PlayerDisplay> (client/PlayerManager.cs:372-381)
        _world.BroadCast(_context.AppearPlayer.Display);
        OnContextChanged();
        Console.WriteLine($"[เครื่องประดับ] {Short(EntityId)} ถอดเครื่องประดับออก");
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ตั้งชื่อสิ่งปลูกสร้าง
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ชื่อสิ่งปลูกสร้างอยู่ที่ <c>ArtifactState.ChangedName</c> (GameCode/Messages/ArtifactState.cs:60)
    ///
    /// ⚠️ **เก็บถาวรไม่ได้จากไฟล์นี้**: <see cref="ArtifactManager"/> (Core/ArtifactManager.cs)
    /// มีเมธอดแก้สถานะเฉพาะเรื่อง (Scribble/OpenGate/SetBuildingState/…) ไม่มีตัวตั้งชื่อ และ
    /// <c>Get()</c> คืน <c>AppearArtifact</c> ที่เป็น struct = **สำเนา** แก้แล้วไม่กลับเข้า
    /// dictionary · ทางอ้อมอย่าง RemoveArtifact+AddArtifact ล้างเจ้าของ/เมล็ดที่ปลูก/วัสดุก่อสร้าง
    /// ไปด้วย (ArtifactManager.cs:120-133) = เสียหายมากกว่าได้ชื่อ
    ///
    /// ⇒ ปฏิเสธพร้อมข้อความ ดีกว่ารับแล้วชื่อไม่เปลี่ยนโดยผู้เล่นไม่รู้ตัว
    /// **ถ้าจะทำต่อ**: เพิ่ม <c>ArtifactManager.SetChangedName(entityId, name)</c> ที่เรียก
    /// <c>RaiseStateUdated</c> แบบเดียวกับ <c>Scribble</c> (ArtifactManager.cs:469-482) แล้วมาต่อตรงนี้
    /// </summary>
    private void HandleRenameArtifactMsg(Rename msg, uint seq)
    {
        // ตรวจสิทธิ์ก่อนเสมอ ถึงจะยังทำไม่ได้ — จะได้ไม่มีวันหลุดเป็นช่องให้ตั้งชื่อของคนอื่น
        // ตอนมีคนมาต่อโค้ดส่วนที่เหลือ
        if (!MayTouchArtifact(msg.EntityId, "ตั้งชื่อสิ่งปลูกสร้าง"))
        {
            Send(new Abort { Text = "ทำกับสิ่งปลูกสร้างนี้ไม่ได้" }, seq);
            return;
        }
        Console.WriteLine($"[สิ่งปลูกสร้าง] {Short(EntityId)} ขอตั้งชื่อ {Short(msg.EntityId)} เป็น '{msg.Name}' — ยังเก็บชื่อไม่ได้");
        Send(new Abort { Text = "ยังไม่เปิดใช้งานการตั้งชื่อสิ่งปลูกสร้าง" }, seq);
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  หาตำแหน่งเป้าหมายให้บทไกด์
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// หาช่องของ entity ชนิดที่ขอ ที่ใกล้ผู้เล่นที่สุดบนเกาะนี้ — null ถ้าไม่มี
    ///
    /// ค้นสองแหล่งที่เซิร์ฟรู้จริง:
    ///   • สิ่งปลูกสร้าง — <c>ArtifactManager.Enumerable</c> (Core/ArtifactManager.cs:117)
    ///   • สัตว์ที่ยังไม่ตาย — <c>AnimalManager.All</c> (Core/AnimalManager.cs:219)
    ///
    /// ⚠️ **ไม่ครอบคลุมของธรรมชาติ** (ต้นไม้/หิน) เพราะมันเก็บเป็นไบต์อยู่ใน Garden ของแต่ละ
    /// chunk และ <c>World._chunkData</c> เป็น private ไม่มีตัวอ่านสาธารณะ (Core/World.cs:520-563
    /// มีแต่ AddNatural/DestroyNatural) ⇒ ไกด์ที่สั่งหา "ต้นไม้ชนิดนี้" จะได้ null
    /// ซึ่งฝั่งเกมรับได้ (ลูกศรไม่ขึ้น) ดีกว่าชี้ผิดที่
    /// </summary>
    private Point2? FindNearestEntityTile(ushort entityType)
    {
        Point2 from = LifeTileOf(this);
        Point2? best = null;
        long bestDistance = long.MaxValue;

        void Consider(Point2 tile)
        {
            long dx = tile.x - from.x;
            long dy = tile.y - from.y;
            long distance = dx * dx + dy * dy;
            if (distance >= bestDistance) return;
            bestDistance = distance;
            best = tile;
        }

        foreach (AppearArtifact artifact in _world.ArtifactManager.Enumerable(
                     a => a.IsAlive && a.EntityType == entityType))
        {
            Consider(artifact.Tile);
        }

        IReadOnlyList<AnimalManager.Animal> animals = _world.AnimalManager?.All;
        if (animals != null)
        {
            foreach (AnimalManager.Animal animal in animals)
            {
                if (!animal.IsAlive || animal.EntityType != entityType) continue;
                Consider(animal.Tile);
            }
        }
        return best;
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ค่าจากไฟล์ / ค่าที่เราตั้งเอง
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>ค่าที่ **ไม่มีในข้อมูลต้นฉบับ** ของระบบนี้ — รวมไว้ที่เดียวเหมือน SurvivalTuning</summary>
    private static class LifeTuning
    {
        /// <summary>
        /// **ค่าของเรา** — ข้อเสนอชุบชีวิตมีอายุกี่วินาที
        ///
        /// ฝั่งเกมใช้ <c>ValidUntil</c> เป็นเวลาปิดกล่องข้อความอัตโนมัติ
        /// (client/CPRSystem.cs:213-227 SetHideTimer · และไม่โชว์เลยถ้าเวลานั้นผ่านไปแล้ว)
        /// หาในไฟล์ data แล้วไม่มีค่าไหนเป็นอายุของข้อเสนอนี้ (death_penalty
        /// .death_point_remaining_duration = 300 เป็นอายุ "จุดที่ตาย" คนละเรื่อง)
        /// ⇒ 30 วิ = นานพอให้อ่านข้อความและกด แต่สั้นพอที่คนช่วยจะไม่เดินจากไปก่อน
        /// </summary>
        public const double ResurrectionOfferSeconds = 30.0;
    }

    /// <summary>
    /// ค่าจาก data/assets/constants.json ที่ระบบนี้ต้องใช้
    ///
    /// อ่านไฟล์เองด้วยเหตุผลเดียวกับ <c>ItemConstants</c> ใน Core/Player.Inventory.cs:
    /// Support/YamlConstants.cs พอร์ต Constants มาแค่บางส่วน (PersonalRegion/Market/
    /// Resistance/Energy) ยังไม่มี drink_water/wash_body/artifact_mood — และไฟล์นั้นอยู่
    /// นอกขอบเขตงานนี้ ⇒ อ่านคีย์ที่ต้องใช้จากไฟล์เดิมตรง ๆ ค่าเดียวกันเป๊ะ ไม่ได้ตั้งเลขเอง
    /// </summary>
    private static class LifeConstants
    {
        // ประกาศเป็น property ไม่ใช่ field เพื่อไม่ให้ CS0649 เตือน (Newtonsoft เป็นคนเซ็ตค่าให้)
        private class Root
        {
            [JsonProperty("drink_water")] public DurationNode DrinkWater { get; set; }
            [JsonProperty("wash_body")] public DurationNode WashBody { get; set; }
            [JsonProperty("artifact_mood")] public MoodNode ArtifactMood { get; set; }
        }

        private class DurationNode
        {
            [JsonProperty("duration")] public float Duration { get; set; }
        }

        private class MoodNode
        {
            [JsonProperty("looking_around_time")] public float LookingAroundTime { get; set; }
        }

        private static Root _root;

        private static Root Data => _root ??= Json.ReadFromFile<Root>("constants") ?? new Root();

        /// <summary>
        /// ค่าสำรองเมื่ออ่าน constants.json ไม่ได้ — **ไม่ใช่ค่าสมดุลของเกม**
        ///
        /// ทั้งสามตัวในไฟล์จริงเป็น 5 เท่ากันหมด (drink_water.duration · wash_body.duration ·
        /// artifact_mood.looking_around_time) ⇒ ใช้ 5 เป็นตัวสำรอง
        /// ส่ง 0 ไม่ได้ เพราะฝั่งเกมจะหยุดหลอดทันทีแล้วท่าทางไม่เล่น (ดู PredictTimer.Play)
        /// </summary>
        private const float FallbackDuration = 5f;

        private static float Positive(float value) => value > 0f ? value : FallbackDuration;

        /// <summary>constants.json → drink_water → duration (ของจริง = 5)</summary>
        public static float DrinkWaterDuration => Positive(Data.DrinkWater?.Duration ?? 0f);

        /// <summary>constants.json → wash_body → duration (ของจริง = 5)</summary>
        public static float WashBodyDuration => Positive(Data.WashBody?.Duration ?? 0f);

        /// <summary>constants.json → artifact_mood → looking_around_time (ของจริง = 5)</summary>
        public static float LookAroundTime => Positive(Data.ArtifactMood?.LookingAroundTime ?? 0f);
    }
}
