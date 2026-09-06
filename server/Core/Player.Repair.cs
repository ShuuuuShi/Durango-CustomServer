using System.Collections.Generic;
using Durango.Network;
using Messages;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  ซ่อมสิ่งปลูกสร้าง + เสริมเทค (Repair & TechSupport)
//
//  สองระบบนี้อยู่ไฟล์เดียวกันเพราะฝั่งเกมเรียกจากหน้าจอเดียวกัน (หน้าต่างซ่อม/หน้าต่างเสริมเทค
//  ที่โต๊ะทำงาน) และทั้งคู่ "ค้างเงียบ" แบบเดียวกันเมื่อเซิร์ฟไม่ตอบ
//
//  ═══ สภาพจริงของเซิร์ฟตอนนี้ (ตรวจแล้ว ไม่ได้เดา) ═══
//
//  1) **ความทนทานของสิ่งปลูกสร้างไม่เคยลดลงเลย**
//     ทุกที่ที่สร้าง artifact ตั้ง Durability เป็นเส้นแบนเต็มหลอดตายตัว:
//       · Core/Player.Building.cs:304 → FullDurability() (นิยามที่ :343)
//       · Core/Cheats.cs:265 MakeAppearArtifact
//     และค้นทั้ง server/Core + server/Support แล้ว **ไม่มีที่ไหนเขียน States.Durability ให้ลดลง**
//     (คอมเมนต์ที่ Player.Building.cs:337-342 ยืนยันเอง: สูตรผุใน constants.json → build → destruct
//      ยังไม่ได้ทำ เพราะกลัวบ้านผู้เล่นหายเอง)
//     ⇒ "ซ่อม" ตอนนี้ไม่มีอะไรให้ซ่อมจริง ๆ
//
//  2) **ไม่มีทางเขียนความทนทานกลับเข้าโลก**
//     Core/ArtifactManager.cs เปิด API ไว้เฉพาะ SetBuildingState / UpdateGrowCage /
//     UpdateDomesticCage / SeedPlant ฯลฯ — **ไม่มีตัวไหนแก้ States.Durability** และ Get() คืน
//     struct ที่ก๊อบออกมา (บรรทัด 111) แก้แล้วไม่กลับเข้า dictionary
//     ⇒ ต่อให้เขียนสูตรซ่อมไว้ก็เซฟไม่ลง
//
//  3) **ไอเทมของเซิร์ฟนี้ไม่มีช่องปรับปรุง (ReformSlots) เลยสักชิ้น**
//     Messages/Item.cs:63 มีฟิลด์นี้อยู่ แต่ค้นทั้ง server/Core แล้วไม่มีที่ไหนเซ็ตค่า
//     (Cheats.MakeItem ไม่แตะ) และ Yaml Recipe ที่พอร์ตมามีแค่ add_on
//     (Support/YamlArtifact.cs:10-13) ไม่มีข้อมูล RecipeReform / TagRareness
//     ⇒ ประเมินผลเสริมเทคจริงคิดไม่ได้ — ตรงกับที่ระบบคราฟต์สรุปไว้แล้ว
//     (Core/Player.Crafting.cs:388-393 ปฏิเสธสูตรชนิด Modify/Reform ด้วยเหตุผลเดียวกัน)
//
//  ═══ ดังนั้นไฟล์นี้ทำสองอย่าง ═══
//    · คำสั่งที่เป็น "การกระทำ" (ซ่อม / เสริมเทค / ถอดของประดับ) → ตอบ Abort ที่มีข้อความไทยเสมอ
//      ห้ามเงียบ และ **ห้ามกินชุดซ่อม/วัสดุของผู้เล่นทิ้ง** เพราะทำงานจริงไม่ได้
//      Abort จะไปโผล่เป็นข้อความระบบให้ผู้เล่นเห็นด้วย เพราะเกมมี global handler
//      (client/GameManager.cs:269 On<Abort> → :309 DefaultAbortHandler → UIManager.SystemMsg)
//      และยังตกไปเข้า .Rest() ของ seq นั้นพร้อมกัน — ดูกลไกที่
//      client/Durango.Network/Connection.cs:867-895 (หา global handler ก่อน แล้วค่อยเรียก
//      reply handler ต่อ ⇒ ได้ทั้งข้อความระบบและการปิดหลอด/ปิดวงแหวนโหลด)
//    · คำสั่งที่เป็น "คำถาม" (ขอชุดผลประเมิน) → ตอบชุดว่างที่ถูกโครงสร้าง ไม่แต่งของปลอม
//
//  ⚠️ ไม่รวมสองตัวนี้ไว้ในไฟล์: **RequestTechSupport (59144)** และ **Bleach (3669)**
//     ระบบคราฟต์ลงทะเบียนไปแล้วที่ Core/Player.Crafting.cs:314 และ :320 (ผ่าน RecvFallback)
//     Connection.Recv ตัวหลังทับตัวหน้าเสมอ ⇒ ใส่ซ้ำที่นี่มีแต่จะแย่งกันเอง
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    private void RegisterRepairHandlers()
    {
        // ── RepairArtifact (2055) ────────────────────────────────────────────────────
        // ยิงจากสองที่:
        //   · client/Durango.UI/RepairGroup.cs:242 (กดปุ่มซ่อมโดยใส่ชุดซ่อม — KitItemIds มีของ)
        //   · client/Durango.Logic.Interactions/ArtifactInteractions.cs:769
        //     (ซ่อมด้วยวาร์ปเจม — ส่ง **KitItemIds = null** มาเลย)
        // ทั้งคู่วิ่งผ่าน client/RepairSystem.cs:18-26 → RegisterPostRepairEvents (:28-62)
        // ซึ่งรอ .On<Timer>(1134) ที่ seq นี้ = สำเร็จ · .On<EnergyWarning> = ถามพลังงานก่อน
        // · .Rest(...) = ล้มเหลว (ปิดวงแหวนโหลดที่ RepairGroup.cs:343-346 OnArtifactRepair)
        // ⇒ ตอบ Abort ปลอดภัย: .Rest เก็บให้เรียบร้อย ไม่มีหลอดค้าง
        _connection.Recv(delegate(RepairArtifact msg, PacketHeader header)
        {
            HandleRepairArtifactMsg(msg, header.Seq);
        });

        // ── RepairImmediate (2056) ───────────────────────────────────────────────────
        // "จ่ายวาร์ปเจมเพื่อจบการซ่อมทันที" — client/RepairSystem.cs:64-72 ส่งแบบ
        // **ไม่ผูก .On อะไรเลย** (fire and forget) จุดเรียกเดียวคือ
        // client/Durango.Logic.Interactions/ArtifactInteractions.cs:741-761 RepairArtifactImmediately
        // ซึ่งเข้าเงื่อนไขได้ก็ต่อเมื่อ artifact.ArtifactState.IsRepairing() เป็นจริง
        // = ต้องมี ArtifactState.Repairement (Messages/ArtifactState.cs:18) ค้างอยู่
        //
        // เซิร์ฟนี้ไม่เคยตั้ง Repairement ให้หลังไหนเลย ⇒ ทางนี้ปกติจะไม่ถูกยิงด้วยซ้ำ
        // แต่ต้องลงทะเบียนไว้ กัน log "ไม่มี handler" และกันกรณีเซฟเก่า/ไคลเอนต์ดัดแปลง
        //
        // ตอบ Abort ที่ seq: ไม่มีใครรอ seq นี้ ⇒ ตกไปเข้า global DefaultAbortHandler
        // ขึ้นเป็นข้อความระบบให้ผู้เล่นรู้ว่าไม่ได้เกิดอะไรขึ้น (และไม่ได้ถูกหักวาร์ปเจม)
        // ⚠️ ห้ามหักเงินตาม msg.Cost เด็ดขาด — เราไม่ได้ทำให้การซ่อมเสร็จจริง
        _connection.Recv(delegate(RepairImmediate msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่รองรับการเร่งซ่อมด้วยวาร์ปเจม (ไม่ได้หักวาร์ปเจมของคุณ)" },
                header.Seq);
        });

        // ── GetTechSupportEstimates (59138) ──────────────────────────────────────────
        // **ตัวสำคัญที่สุดในไฟล์นี้** — client/TechSupportSystem.cs:34-56 RequestAllEstimates
        // ส่งแบบไม่ผูก .On แล้วไปรับด้วย **global On<TechSupportEstimates>(59139)** ที่ผูกไว้
        // ตั้งแต่ Awake (TechSupportSystem.cs:25) → OnTechSupportEstimates (:169-177)
        //
        // ⚠️ **ต้องตอบด้วย ReplyOf = 0 เท่านั้น** — ใช้ Send(msg) ไม่ใส่ seq
        //
        // ⚠️ ธง EstimatesLoaded ตั้งค่าเป็น true ได้ที่เดียวคือใน OnTechSupportEstimates
        //    (อีกทางคือ :54 กรณีไม่มีไอเทมเข้าเกณฑ์เลย) ⇒ **ไม่ตอบ = หน้าจอเสริมเทคติด
        //    วงแหวนโหลดค้างตลอดกาล** (client/Durango.UI/TechSupportEstimatePageWidget.cs:119
        //    ถ้า !EstimatesLoaded จะไม่มีวันเรียก LoadingRing.DetachFromWidget ที่ :143)
        //
        // ตอบ "แผนที่ว่าง" ไม่ใช่ของปลอม: client เก็บลง _estimatesDict ตรง ๆ (:171) แล้ว
        // GetEstimateInfo (:64) จะ TryGetValue ไม่เจอ → คืน null ซึ่งหน้าจอรองรับอยู่แล้ว
        // (:132 RefreshEstimate(estimate) และ :134 _estimateEffectsAndMaterialsWidget.Refresh(...)
        //  รับ TechSupportEstimate? ที่เป็น null ได้ — นิยาม RefreshEstimate ที่ :185 มีสาขา else
        //  ที่โชว์ "ยังไม่มีใบประเมิน" ไม่ใช่ NRE)
        // เหตุผลที่ไม่แต่งผลประเมิน: ไม่มีข้อมูล RecipeReform/TagRareness บนเซิร์ฟ (ดูหัวไฟล์ ข้อ 3)
        _connection.Recv(delegate(GetTechSupportEstimates msg, PacketHeader header)
        {
            Send(new TechSupportEstimates
            {
                Estimates = new Dictionary<string, Dictionary<int, TechSupportEstimateInfo>>()
            });
        });

        // ── RequestTechSupportEstimate (59141) ───────────────────────────────────────
        // "ขอสุ่มผลประเมินใหม่ของช่องนี้ (ล็อกแท็กบางตัวไว้)" — client/TechSupportSystem.cs:77-104
        // รอ .On<TechSupportEstimateResult>(59142) ที่ seq นี้ และมี .Rest(:97-103) ที่ยิง
        // EstimateUpdated(itemId, null) ⇒ ตอบ Abort แล้วหน้าจอกลับสู่สภาพ "ยังไม่มีผลประเมิน" สะอาด ๆ
        //
        // ทำจริงไม่ได้เพราะ TechSupportEstimate (Messages/TechSupportEstimate.cs:12-20) ต้องการ
        // RecipeId + Tags[] + Decorator + TagRareness ซึ่งมาจากตาราง recipe ชนิด reform
        // ที่ไม่ได้ถูกพอร์ตมา (Support/YamlArtifact.cs:10-13 Recipe มีแค่ add_on)
        // ⇒ แต่งค่าเอง = ผู้เล่นเห็นผลประเมินที่ไม่มีวันเกิดขึ้นจริง — ห้ามเด็ดขาด
        _connection.Recv(delegate(RequestTechSupportEstimate msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่รองรับการประเมินผลเสริมเทค" }, header.Seq);
        });

        // ── RequestResetReformSlot (59145) ───────────────────────────────────────────
        // "ถอดของประดับออกจากช่องปรับปรุง" — client/TechSupportSystem.cs:106-125 RemoveDecoration
        // รอ **.On<OK>(1231) อย่างเดียว ไม่มี .Rest** ⇒ ตอบอย่างอื่นแล้วปุ่มจะไม่ทำอะไร
        // (event DecorationRemoved ไม่ยิง) ซึ่งตรงกับสภาพจริง และ Abort ยังขึ้นข้อความระบบบอกเหตุผล
        // ดีกว่าเงียบให้ดูเหมือนเซิร์ฟแฮงก์ — แนวเดียวกับ CancelCrafting/SkipEntrustedCraft
        // (Core/Player.Crafting.cs:295-304)
        //
        // ⚠️ **ห้ามตอบ OK** ทั้งที่ไม่ได้ถอดจริง: client จะปิดหน้าต่างแล้วรีเฟรชจาก ItemData เดิม
        // ผู้เล่นจะเห็นของประดับยังอยู่ทั้งที่ระบบบอกว่าถอดสำเร็จ
        // ทำจริงไม่ได้เพราะไอเทมของเซิร์ฟนี้ไม่มี ReformSlots สักชิ้น (ดูหัวไฟล์ ข้อ 3)
        _connection.Recv(delegate(RequestResetReformSlot msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่รองรับการถอดของประดับออกจากช่องปรับปรุง" }, header.Seq);
        });
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ซ่อมสิ่งปลูกสร้าง
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ซ่อมสิ่งปลูกสร้าง — RepairArtifact (2055)
    ///
    /// **ไม่กินชุดซ่อมของผู้เล่นสักชิ้นในทุกเส้นทาง** (จงใจ) เพราะซ่อมจริงไม่ได้ —
    /// เทียบกับ <see cref="HandleRepairItemMsg"/> (Core/Player.Inventory.cs:471) ที่ซ่อม
    /// **ไอเทม** ได้จริงจึงกินชุดซ่อมได้ ส่วนสิ่งปลูกสร้างเขียนความทนทานกลับไม่ได้ (หัวไฟล์ ข้อ 2)
    ///
    /// ข้อมูลจริงที่ **ยังไม่ได้ใช้** (บันทึกไว้ให้คนต่อยอด ไม่ใช่ลืม) — constants.json → repair → artifact:
    ///   · time = 5.0                → เวลาเล่นท่าซ่อม ใช้เป็น Timer.Duration ตอนทำสำเร็จ
    ///                                 (คู่ขนานกับ repair → item → time = 3.0 ที่ ItemConstants.RepairItemTime)
    ///   · energy = "required_perf * 0.8"
    ///   · recover_duration = "((dur_max - dur) / dur_max) * postprocess_time"
    ///                                 → ความยาวช่วงซ่อม ที่จะไปลง ArtifactState.Repairement
    ///                                   แล้วทำให้ปุ่ม "เร่งซ่อมด้วยวาร์ปเจม" ใช้งานได้จริง
    ///   · limit_durability = 2 · success_ratio · durability_result = "max_durability"
    /// ทั้งหมดนี้ใช้ไม่ได้จนกว่า ArtifactManager จะมี API เขียน States.Durability/Repairement
    /// และมีระบบความสามารถ (required_perf) ⇒ **ห้ามใส่ Timer ปลอม** เพราะ .On&lt;Timer&gt; แปลว่า
    /// "ซ่อมสำเร็จ" (client/RepairSystem.cs:31-38 เล่นท่าซ่อมแล้วเรียก onResult(true))
    /// ผู้เล่นจะเห็นตัวละครซ่อมเสร็จแล้วหลอดไม่ขยับ — หลอกกว่าการปฏิเสธตรง ๆ
    /// </summary>
    private void HandleRepairArtifactMsg(RepairArtifact msg, uint seq)
    {
        // หาแบบเดียวกับ HandleGetArtifactMsg (Core/Player.Building.cs:350)
        if (_world.ArtifactManager.Get(msg.EntityId) is not { } artifact)
        {
            Send(new Abort { Text = "ไม่พบสิ่งปลูกสร้างที่จะซ่อม" }, seq);
            return;
        }

        // Durability ของสิ่งปลูกสร้างเป็นอัตราส่วน 0..1 เหมือนของไอเทม
        // (Core/Player.Building.cs:343 FullDurability = Gauge(1, 0, [node(0,1)]))
        // Gauge.Get()/Max() ปลอดภัยแม้ Determination ว่าง (GameCode/Gauge.cs:116-122 คืน 0)
        // แต่ตัว Gauge เองเป็น class ⇒ ต้องกัน null ก่อน
        Gauge durability = artifact.States.Durability;
        if (durability == null || durability.Get() >= durability.Max())
        {
            Send(new Abort { Text = "สิ่งปลูกสร้างหลังนี้ยังไม่ผุ ไม่ต้องซ่อม (ไม่ได้ใช้ชุดซ่อมของคุณ)" }, seq);
            return;
        }

        // ทางนี้ยังไปไม่ถึงในเซิร์ฟปัจจุบัน (ความทนทานไม่เคยลด — หัวไฟล์ ข้อ 1)
        // เขียนไว้เพื่อว่าถ้าวันหนึ่งมีระบบความผุแล้วยังไม่มี API เขียนกลับ ผู้เล่นจะได้คำตอบที่ตรง
        Send(new Abort { Text = "ยังไม่รองรับการซ่อมสิ่งปลูกสร้าง (ไม่ได้ใช้ชุดซ่อมของคุณ)" }, seq);
    }
}
