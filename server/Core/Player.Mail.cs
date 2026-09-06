using System;
using Durango.Network;
using Messages;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  จดหมาย (Mail) — กล่องจดหมายของผู้เล่น
//
//  ═══ จดหมายในเกมนี้ทำงานยังไง ═══
//  ฝั่งเกมเก็บรายการจดหมายไว้ในลิสต์ของตัวเอง (client/MailSystem.cs:10 `_mails`) และลิสต์นั้น
//  **โตได้จากทางเดียวเท่านั้น** คือเซิร์ฟ push ลงมา (client/MailSystem.cs:34-36):
//      MailPut(2074)     → จดหมายระบบ 1 ฉบับ        (OnMailPut     บรรทัด 79)
//      UserMailPut(9786525) → จดหมายจากผู้เล่น 1 ฉบับ (OnUserMailPut บรรทัด 84)
//      Mails(2073)       → ทั้งกล่องรวดเดียว          (OnMails       บรรทัด 39)
//  ทั้งสามตัวเป็น **เซิร์ฟ→เกม** ล้วน ๆ ไม่ต้องมี handler ฝั่งเรา
//
//  ⚠ GetMails(2072) — **ไม่ต้องเขียน handler และห้ามเขียน** ไม่ใช่ "ยังไม่มีใครทำ"
//    ตรวจแล้ว: ทั้ง client/ และ nexonSRC/ ไม่มีที่ไหนยิง GetMails ออกมาเลยสักจุด
//    (เจอแต่ตัวนิยาม client/Messages/GetMails.cs) กล่องจดหมายจึงมาทาง push 3 ตัวข้างบนอย่างเดียว
//    ถ้าใครเผลอเติม handler แล้วตอบ Mails ขึ้นมาเอง = แต่งจดหมายปลอม ผิดกฎ "ห้ามแต่งข้อมูล"
//
//  ⇒ เซิร์ฟเรายังไม่มีที่เก็บจดหมายจริง (ไม่มี MailStorage ใน PlayerContext) จึงไม่เคย push
//    อะไรลงไป กล่องจดหมายฝั่งเกมจึงว่างตลอด และคำสั่งกลุ่ม Accept/Delete/MarkAsRead ก็จะ
//    ถูกกันไว้ตั้งแต่ต้นทาง (client/MailSystem.cs:118,155 `GetSize(mails) == 0` แล้ว return)
//    แต่เรายังต้องลงทะเบียนให้ครบ เพราะ (ก) กัน log "ไม่มี handler" (ข) ถ้าวันหน้าเปิดระบบ
//    จริงจะได้มีที่ต่อ (ค) ผู้เล่นกดปุ่มแล้วต้องได้คำตอบ ไม่ใช่ค้างหมุน
//    (เซิร์ฟจำลองของ NEXON เองก็ไม่มี handler จดหมายสักตัว — nexonSRC/Durango.Offline/ ไม่มี
//     คำว่า AcceptMails/DeleteMails/MailPut เลย ⇒ การไม่ทำระบบจริงตรงนี้ตรงกับต้นฉบับ
//     แต่ของเราลงทะเบียนเพิ่มเพื่อไม่ให้ผู้เล่นกดปุ่มแล้วเงียบ)
//
//  ═══ ทำไม "รับของแนบ/ลบ" ตอบ Abort ไม่ใช่ OK ═══
//  ฝั่งเกมรอคำตอบด้วย `registrar.All(p => onResult(Packet.IsSuccess(p)))`
//  (client/MailSystem.cs:131-134, 146-149, 168-171) และ Packet.IsSuccess
//  (client/Durango.Network/Packet.cs:90-99) นับว่า **ล้มเหลว** เฉพาะ 3 TypeCode:
//      1022 Error · 1024 Abort · 3650 TimedOut     — นอกนั้นถือว่าสำเร็จหมด
//  ถ้าเราตอบ OK(1231) = โกหกว่า "รับของแนบสำเร็จ" ทั้งที่ไม่ได้ให้ของอะไรเลย
//  (จะให้ของจริงต้องผ่าน AddItems ตามของที่แนบมาจริง ๆ — ห้ามแต่งของขึ้นเอง)
//  จึงตอบ Abort ซึ่ง (1) ปลดล็อกปุ่มให้ผู้เล่น: UI แค่เคลียร์ `_isWaiting` ไม่สนใจ true/false
//  (nexonSRC/Durango.UI/MailContentsView.cs:186-200, MailListView.cs:92-95 — ทั้งสองที่ตรงกับ
//   ตัวที่ใช้จริงใน client/Durango.UI/ บรรทัดเดียวกัน)
//  และ (2) เด้งข้อความบอกผู้เล่นด้วย — ตรงนี้ต้องดู client/Durango.Network/Connection.cs:868
//  `HandleMsg` ให้ละเอียด เพราะ `.All(...)` ลงทะเบียนแค่ช่อง `Handler` ไม่ได้ใส่ TypeCode ลง
//  `Dictionary` ของ seq นั้น ⇒ ตัวแปร `flag` ยังเป็น false ⇒ บรรทัด 885 ตกไปหยิบ handler กลาง
//  `_packetHandlers[1024]` มายิงด้วย แล้วบรรทัด 893 ค่อยยิง `.All` ต่ออีกที
//  ⇒ **ทำงานทั้งคู่**: SystemMsg เด้ง + onResult(false) ปลดปุ่ม
//  handler กลางของ Abort คือ client/GameManager.cs:308 `On<Abort>(DefaultAbortHandler)`
//  ตัว handler อยู่ที่ GameManager.cs:348-351 → `UIManager.SystemMsg(LimitText(msg.Text), 4f)`
//  ⚠ ด้วยเหตุนี้ Abort ต้องมี Text เสมอ — `default(Abort)` ทำให้ Text=null แล้วเกมแครชที่
//    `text.Length` ใน LimitText (client/GameManager.cs:329-332 — ไม่ได้เช็ค null)
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    private void RegisterMailHandlers()
    {
        // ── ข้อความเดียวที่บอกผู้เล่นว่าระบบยังไม่เปิด (ใช้ซ้ำทุก handler ที่เป็น "การกระทำ") ──
        // **ข้อความของเรา** — ต้นฉบับ NEXON ไม่มีเคสนี้ (ของเขามีระบบจดหมายจริง)
        const string notReadyMsg = "ยังไม่เปิดใช้งานระบบจดหมาย";

        // SendMail (2077) — ส่งจดหมายหาผู้เล่นคนอื่น (RecipientId + Text + ItemIds)
        // จุดยิง: client/MailSystem.cs:198-205 `Connections.Frontend.Send(new SendMail{...})`
        // ทิ้ง registrar ทันที ⇒ ไม่ได้จอง .On อะไรไว้ คำตอบจึงตกไป handler กลางเสมอ
        // (บิลด์นี้ยังไม่มี UI ตัวไหนเรียก MailSystem.SendMail — ค้นทั้ง client/ และ nexonSRC
        //  เจอแค่ตัวนิยาม — แต่ลงทะเบียนไว้ตามโปรโตคอล)
        // ⇒ ส่งจริงไม่ได้ (ไม่มีกล่องจดหมายให้ปลายทาง + ItemIds ต้องหักของจากกระเป๋าจริง)
        //   ตอบ Abort เพื่อไม่ให้ผู้เล่นเข้าใจผิดว่าจดหมายถูกส่งออกไปแล้ว
        _connection.Recv(delegate(SendMail msg, PacketHeader header)
        {
            Send(new Abort { Text = notReadyMsg }, header.Seq);
        });

        // AcceptMails (2075) — "รับของแนบ" ของจดหมายระบบ (MailIds เป็นชุด รับทีเดียวหลายฉบับได้)
        // จุดยิง: client/MailSystem.cs:116-136 — ปุ่ม "รับ" ในหน้าอ่านจดหมาย
        // (nexonSRC/Durango.UI/MailContentsView.cs:196) และปุ่ม "รับทั้งหมด"
        // (nexonSRC/Durango.UI/MailListView.cs:92)
        // ⇒ เราไม่มีจดหมายจริงจึงไม่รู้ว่าของแนบคืออะไร จะเรียก AddItems ก็ไม่มีข้อมูลให้เติม
        //   **ห้ามแต่งของขึ้นมาเอง** ⇒ ตอบ Abort (ปุ่มปลดล็อก + ผู้เล่นเห็นเหตุผล)
        _connection.Recv(delegate(AcceptMails msg, PacketHeader header)
        {
            Send(new Abort { Text = notReadyMsg }, header.Seq);
        });

        // DeleteMails (2076) — ลบจดหมายระบบ (MailIds เป็นชุด)
        // จุดยิง: client/MailSystem.cs:153-173 — ปุ่มเดียวกับ "รับ" แต่สลับเป็น "ลบ" เมื่อ
        // จดหมายฉบับนั้น Accepted แล้ว (nexonSRC/Durango.UI/MailContentsView.cs:186-191)
        // ⇒ ไม่มีที่เก็บให้ลบ ⇒ Abort (ตอบ OK จะทำให้เกมเข้าใจว่าลบสำเร็จทั้งที่ฝั่งเซิร์ฟ
        //   ไม่มีอะไรเปลี่ยน — พอขอ Mails ใหม่จดหมายจะโผล่กลับมา ผู้เล่นจะงง)
        _connection.Recv(delegate(DeleteMails msg, PacketHeader header)
        {
            Send(new Abort { Text = notReadyMsg }, header.Seq);
        });

        // MarkMailsAsRead (98712435) — ทำเครื่องหมาย "อ่านแล้ว" ของจดหมายระบบ
        // จุดยิง: client/MailSystem.cs:175-196 — ยิงตอนเปิดอ่านจดหมาย
        // (nexonSRC/Durango.UI/MailContentsView.cs:176) แบบ **ยิงแล้วไม่รอคำตอบ**
        // (บรรทัด 190 ทิ้ง registrar ทิ้ง) และฝั่งเกมตั้ง `_mails[num].IsRead = true` เองไปแล้ว
        // ตั้งแต่บรรทัด 180 ก่อนยิงด้วยซ้ำ
        // ⇒ รับเงียบ ๆ ไม่ต้องตอบ — **ห้ามตอบ Abort ตรงนี้** เพราะไม่มีใครจอง TypeCode ไว้
        //   คำตอบจะตกไป handler กลางแล้วเด้ง SystemMsg รบกวนผู้เล่นทุกครั้งที่เปิดอ่านจดหมาย
        //   (ต่อระบบจดหมายจริงเมื่อไหร่ค่อยมาบันทึกสถานะอ่านแล้วลง PlayerContext ตรงนี้)
        _connection.Recv(delegate(MarkMailsAsRead msg, PacketHeader header)
        {
        });

        // AcceptUserMails (9786523) — "รับของแนบ" ของจดหมายที่ผู้เล่นคนอื่นส่งมา
        // จุดยิง: client/MailSystem.cs:125-128 (สาขา IsUserMail ของ AcceptMails) และ
        // client/MailSystem.cs:138-151 (AcceptUserMails ตรง ๆ) — รอคำตอบแบบเดียวกับ AcceptMails
        // ⇒ เหตุผลเดียวกับ AcceptMails: ไม่มีของจริงให้ AddItems ⇒ Abort
        _connection.Recv(delegate(AcceptUserMails msg, PacketHeader header)
        {
            Send(new Abort { Text = notReadyMsg }, header.Seq);
        });

        // DeleteUserMails (9786524) — ลบจดหมายจากผู้เล่นคนอื่น
        // จุดยิง: client/MailSystem.cs:162-165 (สาขา IsUserMail ของ DeleteMails)
        // ⇒ เหตุผลเดียวกับ DeleteMails ⇒ Abort
        _connection.Recv(delegate(DeleteUserMails msg, PacketHeader header)
        {
            Send(new Abort { Text = notReadyMsg }, header.Seq);
        });

        // MarkUserMailsAsRead (98712436) — "อ่านแล้ว" ของจดหมายจากผู้เล่นคนอื่น
        // จุดยิง: client/MailSystem.cs:183-186 (สาขา IsUserMail ของ MarkMailsAsRead)
        // ⇒ ยิงแล้วไม่รอคำตอบเช่นกัน ⇒ รับเงียบ ๆ (เหตุผลเดียวกับ MarkMailsAsRead ด้านบน)
        _connection.Recv(delegate(MarkUserMailsAsRead msg, PacketHeader header)
        {
        });
    }
}
