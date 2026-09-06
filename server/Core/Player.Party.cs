using System;
using Durango.Network;
using Messages;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  ปาร์ตี้ (Party) — ตั้งปาร์ตี้ / ชวน / เข้าร่วม / ปฏิเสธ / ออก / เตะ / โอนหัวหน้า
//  + เส้นทางเดินเรือที่หัวหน้าปาร์ตี้แชร์ให้ (GetRoutesOfParty)
//
//  เดิมเซิร์ฟไม่มี handler กลุ่มนี้เลย ⇒ ผู้เล่นกดปุ่มในหน้าปาร์ตี้แล้ว "เงียบสนิท"
//  ไม่มีทั้งผลลัพธ์และข้อความบอกเหตุผล (client/Durango.UI/PartyGroup.cs:98-218 ทุกปุ่มยิงแล้วจบ
//  ไม่ผูก .On รออะไรกลับ — สถานะปาร์ตี้เดินทางมาทาง push Party (20002) ตัวเดียว)
//
//  ⚠️ เซิร์ฟยังไม่มีระบบปาร์ตี้จริง (ไม่มีที่เก็บสมาชิก · ไม่มีช่องคุยปาร์ตี้ · ไม่มีการ broadcast
//  ข้าม Player instance) ⇒ ทำจริงไม่ได้ทั้งชุด **การตีความของเรา** จึงแบ่งการตอบเป็น 2 กลุ่ม:
//
//    (ก) คำสั่งที่ต้อง "สร้าง/แก้" ปาร์ตี้จริงถึงจะสำเร็จ — MakeParty / InviteIntoParty /
//        JoinIntoParty / ElectPartyLeader ⇒ ตอบ Abort พร้อมข้อความ ตามกฎ "การกระทำที่ทำจริง
//        ไม่ได้ ต้องบอก ไม่ใช่เงียบ" · client/GameManager.cs:269+309 มี global On<Abort> →
//        DefaultAbortHandler → UIManager.SystemMsg(...) เด้ง toast 4 วินาที ⇒ ผู้เล่นรู้ทันที
//        ห้ามส่ง default(Abort) เด็ดขาด: Text=null → UnpackGettextFromMsgPack คืน null →
//        LimitText(null).Length ระเบิด NRE ที่ GameManager.cs:311
//
//    (ข) คำสั่งที่ "ผลลัพธ์ปลายทางเป็นจริงอยู่แล้ว" คือ *ไม่ได้อยู่ปาร์ตี้* — RejectPartyInvitation /
//        LeaveParty / KickPartyMember ⇒ push Party ที่ Info=null กลับไป = ประกาศสถานะจริงว่า
//        "ไม่มีปาร์ตี้" ซึ่งตรงกับที่ผู้เล่นสั่ง ไม่ใช่การแต่งข้อมูล และยังช่วย resync ฝั่งเกม
//        (client/Durango.Logic/PartySystem.cs:112-132 OnParty: Info ไม่มีค่า → เคลียร์รายชื่อ
//        สมาชิก · LeaderEntityId/LeaderName="" · IsInvited=false · IsAcceptedInParty=false
//        แล้วยิง MembersUpdated ⇒ หน้าจอปาร์ตี้/ป้ายคำเชิญปิดตัวเอง)
//
//  ทุกคำสั่งฝั่งเกมยิงแบบ "ไม่ผูก seq" (Connections.Frontend.Send(...) เฉย ๆ ไม่มี .On)
//  ⇒ คำตอบต้องไปให้ถึง global handler · client/Durango.Network/Connection.cs:868-914 HandleMsg
//  จะ fallback ไป _packetHandlers ตาม TypeCode เมื่อไม่มี handler ผูก ReplyOf อยู่
//  (จุด fallback จริงอยู่ที่ Connection.cs:883-886 `if (packetHandler == null) packetHandler =
//   _packetHandlers.Get(typeCode);`) ⇒ ตอบผูก header.Seq หรือ push เฉย ๆ ก็ถึง global เหมือนกัน
//  ⇒ Party (20002) ส่งแบบ push (Send(msg) ไม่ผูก seq) ให้ตรงกับที่ฝั่งเกมดักไว้เป็น global
//  (PartySystem.cs:78 `Connections.Frontend.On<Messages.Party>(OnParty)`)
//
//  หมายเหตุ: GetParty (20001) มี handler อยู่แล้วที่ server/Core/Player.Social.cs:23 — ไม่ลงซ้ำ
//  (Connection.Recv ตัวที่ลงทีหลังจะทับตัวเดิมของ TypeCode เดียวกัน —
//   server/GameCode/Durango.Online/Connection.cs:198-202 ContainsKey → Remove → Add)
//
//  ต่อสายแล้ว: server/Core/Player.Systems.cs:53 เรียก RegisterPartyHandlers() ในชุดแพ็กเกจ
//  18 ระบบ (ถัดจาก RegisterAllyHandlers()) ⇒ handler ทั้ง 8 ตัวในไฟล์นี้ถูกลงทะเบียนจริง
//  ห้ามเพิ่มบรรทัดเรียกซ้ำอีก
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    private void RegisterPartyHandlers()
    {
        // ข้อความ Abort ใช้ร่วมกันทั้งกลุ่ม (ก) — ต้องมีข้อความเสมอ ห้ามเป็น null
        // แต่ละจุดต่อท้ายด้วยว่า "สั่งอะไรไม่ได้" เพื่อให้ toast บอกได้ว่ามาจากปุ่มไหน
        //
        // ประกาศเป็น const "ในเมธอด" ไม่ใช่สมาชิกของคลาส เพราะ Player เป็น partial class ที่ถูก
        // เขียนพร้อมกันหลายไฟล์ — ตั้งชื่อสมาชิกซ้ำข้ามไฟล์เมื่อไหร่คอมไพล์พังทันที ส่วน const
        // ในเมธอดเป็นของเฉพาะเมธอดนี้ ชนกับไฟล์อื่นไม่ได้
        // (แพตเทิร์นเดียวกับ server/Core/Player.Ally.cs:52 AllyNotAvailableText ซึ่งอธิบายเหตุผล
        //  เดียวกันไว้ที่ Player.Ally.cs:53-55)
        const string PartyNotAvailableText = "ยังไม่เปิดใช้งานระบบปาร์ตี้บนเซิร์ฟเวอร์นี้";

        // ── กลุ่ม (ก) คำสั่งที่ต้องมีระบบปาร์ตี้จริง → Abort พร้อมข้อความ ────────────────

        // MakeParty (20003) — ปุ่ม "ตั้งปาร์ตี้" ในหน้าปาร์ตี้
        // (client/Durango.UI/PartyGroup.cs:98 → PartySystem.cs:241-244
        //  `Connections.Frontend.Send(default(MakeParty))` ไม่ผูก .On)
        // ตัว message ไม่มีฟิลด์เลย (server/GameCode/Messages/MakeParty.cs:9 TypeCode=20003u ·
        // :6 [StructLayout(Size=1)]) ⇒ ไม่มีอะไรให้อ่าน
        _connection.Recv(delegate(MakeParty msg, PacketHeader header)
        {
            Send(new Abort { Text = PartyNotAvailableText + " — ยังตั้งปาร์ตี้ไม่ได้" }, header.Seq);
        });

        // InviteIntoParty (20004) — ชวนผู้เล่นเข้าปาร์ตี้ · ฟิลด์เดียว InviteeEntityId (string)
        // (server/GameCode/Messages/InviteIntoParty.cs:7 TypeCode=20004u · :9 ฟิลด์ InviteeEntityId)
        // ยิงจาก 3 ที่: client/Durango.UI/PartyGroup.cs:187 (หน้าปาร์ตี้) ·
        // client/Durango.UI/InteractionGroup.cs:372 (เมนูคลิกตัวผู้เล่น) ·
        // client/Durango.UI.Popup/PlayerInfoPopup.cs:396 (หน้าโปรไฟล์) — ทั้งหมดไม่ผูก .On
        // ⇒ ชวนจริงไม่ได้ (ไม่มีทางส่งคำเชิญข้าม Player instance) ต้องบอก ไม่ใช่เงียบ
        _connection.Recv(delegate(InviteIntoParty msg, PacketHeader header)
        {
            Send(new Abort { Text = PartyNotAvailableText + " — ยังชวนเข้าปาร์ตี้ไม่ได้" }, header.Seq);
        });

        // JoinIntoParty (20006) — ปุ่ม "ตอบรับคำเชิญ" (client/Durango.UI/PartyGroup.cs:114 →
        // PartySystem.cs:246-249) · message ไม่มีฟิลด์ (JoinIntoParty.cs:9 TypeCode=20006u)
        // ในทางปฏิบัติกดไม่ได้อยู่แล้ว เพราะปุ่มโผล่เมื่อ IsInvited=true ซึ่งตั้งได้จาก
        // Party ที่มีสมาชิกเท่านั้น (PartySystem.cs:148-149) และเราไม่เคยส่งแบบนั้น —
        // ลงทะเบียนไว้กัน log "ไม่มี handler" และกันสถานะค้างฝั่งเกม
        _connection.Recv(delegate(JoinIntoParty msg, PacketHeader header)
        {
            Send(new Abort { Text = PartyNotAvailableText + " — ยังเข้าร่วมปาร์ตี้ไม่ได้" }, header.Seq);
        });

        // ElectPartyLeader (20010) — โอนตำแหน่งหัวหน้าให้สมาชิกคนอื่น
        // ฟิลด์เดียว MemberEntityId (ElectPartyLeader.cs:7 TypeCode=20010u · :9 ฟิลด์ MemberEntityId)
        // ยิงจาก client/Durango.UI/PartyGroup.cs:218 → PartySystem.cs:264-270 (ไม่ผูก .On)
        _connection.Recv(delegate(ElectPartyLeader msg, PacketHeader header)
        {
            Send(new Abort { Text = PartyNotAvailableText + " — ยังโอนตำแหน่งหัวหน้าไม่ได้" }, header.Seq);
        });

        // ── กลุ่ม (ข) คำสั่งที่ปลายทาง = "ไม่อยู่ปาร์ตี้" ซึ่งเป็นจริงอยู่แล้ว → push Party ว่าง ──
        //
        // payload ที่ใช้ร่วมกันทั้ง 3 ตัวคือสถานะปาร์ตี้ "ว่าง" ตามชนิดที่ฝั่งเกมรอ —
        // Party (20002 · server/GameCode/Messages/Party.cs:7)
        //   · Info=null  → Pack เป็น nil (Party.cs:36-38 `if (!val.Info.HasValue) PackNull()`)
        //     ฝั่งเกมเช็ค `if (!info.HasValue || ...)` แล้วเคลียร์รายชื่อ
        //     (client/Durango.Logic/PartySystem.cs:117-131)
        //   · Id=null    → Pack เป็น nil (Party.cs:24-26) และ Unpack อ่านกลับเป็น null ได้
        //     (Party.cs:50-56) ⇒ ไม่ทำให้ฝั่งเกมพัง
        // เขียนซ้ำ 3 จุดแทนการทำเมธอดช่วยระดับคลาส ด้วยเหตุผลเดียวกับ const ข้างบน
        // (กันชื่อสมาชิกชนกับ partial ไฟล์อื่นที่เขียนพร้อมกัน) — ถ้าจะเปลี่ยนให้ทั้ง 3 ตัว
        // ตอบ Abort เหมือนกลุ่ม (ก) ต้องแก้ทั้ง 3 จุดนี้

        // RejectPartyInvitation (20007) — ใช้ 2 ความหมายตาม InviteeEntityId
        // (RejectPartyInvitation.cs:7 TypeCode=20007u · :9 ฟิลด์ InviteeEntityId —
        //  Pack แปลง null เป็น nil ที่ :22-24 · Unpack อ่าน nil กลับเป็น null ที่ :40-42
        //  ⇒ ฝั่งเกมส่ง null มาได้จริง และฝั่งเราอ่านได้ถูกต้อง)
        //   · null  = "ฉันปฏิเสธคำเชิญที่ได้รับ" (PartyGroup.cs:119 → PartySystem.cs:283-289)
        //   · มีค่า = "หัวหน้ายกเลิกคำเชิญที่ส่งไปหาคนนี้" (PartyGroup.cs:209 → PartySystem.cs:291-296)
        // ทั้งสองแบบผลลัพธ์ที่ผู้เล่นต้องการคือ "คำเชิญหายไป / ไม่อยู่ปาร์ตี้" ⇒ ตอบสถานะจริง
        // ไม่ใช่ Abort เพราะสิ่งที่เขาสั่งถือว่าสำเร็จแล้วโดยปริยาย **การตีความของเรา**
        _connection.Recv(delegate(RejectPartyInvitation msg, PacketHeader header)
        {
            Send(new Messages.Party { Id = null, Info = null });
        });

        // LeaveParty (20008) — ปุ่ม "ออกจากปาร์ตี้" (client/Durango.UI/PartyGroup.cs:107 →
        // PartySystem.cs:251-254) · message ไม่มีฟิลด์ (LeaveParty.cs:9 TypeCode=20008u)
        // ออกจากปาร์ตี้ = จบลงที่ "ไม่มีปาร์ตี้" ซึ่งเป็นสถานะจริงของเซิร์ฟอยู่แล้ว ⇒ ยืนยันกลับไป
        _connection.Recv(delegate(LeaveParty msg, PacketHeader header)
        {
            Send(new Messages.Party { Id = null, Info = null });
        });

        // KickPartyMember (20009) — หัวหน้าเตะสมาชิกออก · ฟิลด์เดียว MemberEntityId
        // (KickPartyMember.cs:7 TypeCode=20009u · :9 ฟิลด์ MemberEntityId)
        // ยิงจาก client/Durango.UI/PartyGroup.cs:199 → PartySystem.cs:256-262 (ไม่ผูก .On)
        // เซิร์ฟไม่มีสมาชิกให้เตะอยู่แล้ว ⇒ ส่งรายชื่อจริง (ว่าง) กลับไปให้ฝั่งเกม resync
        _connection.Recv(delegate(KickPartyMember msg, PacketHeader header)
        {
            Send(new Messages.Party { Id = null, Info = null });
        });

        // ── เส้นทางเดินเรือของปาร์ตี้ ──────────────────────────────────────────────────

        // GetRoutesOfParty (20300) — "เส้นทางที่หัวหน้าปาร์ตี้แชร์"
        // (GetRoutesOfParty.cs:7 TypeCode=20300u · :9 ฟิลด์ EntityId · :11 ฟิลด์ Tile)
        // เปิดจากท่าเรือชนิด Interaction.SailingRoutesOfParty เท่านั้น:
        // client/Durango.UI/ExploreGroup.cs:247-249 (RouteType.Shared) → ExploreSystem.cs:380-386
        // RequestRoutesOfParty (ไม่ผูก .On) · คำตอบเข้า global On<Routes> (ExploreSystem.cs:44)
        //
        // ⇒ ต้องตอบชนิด Routes (2032 · Routes.cs:9) เท่านั้น ไม่ใช่ Abort — เพราะหน้าจอเส้นทาง
        // เปิดค้างรอผลอยู่ และไม่มีที่อื่นตั้งค่า _routes ให้ได้อีก
        //
        // ⚠️ ArchipelagoRoutes ห้ามเป็น null เด็ดขาด: client/ExploreSystem.cs:320-321 วน
        // `routes.ArchipelagoRoutes.Length` โดยไม่เช็ค null ⇒ null = NRE ทันที
        // ส่วน _Routes ปล่อย null ได้ เพราะ Pack แปลง null เป็น map ว่าง (Routes.cs:26-28) และ
        // ฝั่งเกมเช็ค `if (_routes != null)` ก่อนใช้ (ExploreSystem.cs:290)
        //
        // ผลข้างเคียงที่ยอมรับแล้ว: OnRoutes ใช้ตัวแปรชุดเดียวกับ GetRoutes ปกติ
        // (ExploreSystem.cs:285-286 `_routes = routes._Routes`) ⇒ เปิดท่าเรือปาร์ตี้ครั้งหนึ่ง
        // จะล้างแคชเส้นทางเดินเรือปกติ แต่การเปิดท่าเรือปกติยิง GetRoutes ใหม่ทุกครั้งอยู่แล้ว
        // (ExploreGroup.cs:244-245) จึงเติมกลับเอง — และ HasRoutes() ยังคืน true เพราะ
        // _archipelagoRoutes ไม่เป็น null (ExploreSystem.cs:118-121)
        // ยิ่งกว่านั้น ตอนนี้ผลข้างเคียงยังเกิดไม่ได้เลย: เซิร์ฟใส่ให้ท่าเรือแต่ Interaction.SailingRoutes
        // เท่านั้น (server/Core/Player.cs:1203) ไม่เคยใส่ SailingRoutesOfParty (=308 ·
        // server/GameCode/Shared.System/Interaction.cs:27) ⇒ ฝั่งเกมไม่มีทางยิง 20300 มา
        // handler ตัวนี้จึงเป็นตัวกันเหนียวไว้ก่อน (และกัน log "ไม่มี handler")
        //
        // ตอบ "ว่าง" เพราะไม่มีปาร์ตี้ ⇒ ไม่มีใครแชร์เส้นทางให้ — ห้ามยัดรายการเกาะปกติลงไป
        // เพราะจะกลายเป็นเส้นทางที่อ้างว่า "หัวหน้าปาร์ตี้แชร์" ทั้งที่ไม่มีหัวหน้า และตอนเลือก
        // เกาะ ExploreGroup.cs:213 จะส่ง partierId = LeaderEntityId ที่เป็นค่าว่างไปกับการเดินทาง
        _connection.Recv(delegate(GetRoutesOfParty msg, PacketHeader header)
        {
            Send(new Routes
            {
                _Routes = null,                                   // Pack → map ว่าง (ไม่ใช่ nil)
                ArchipelagoRoutes = Array.Empty<ArchipelagoRoute>()
            }, header.Seq);
        });
    }
}
