using System;
using Durango.Network;
using Messages;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  อีเวนต์ / เช็คอินรายวัน / ฤดูกาล / มินิเกม / กระดานเวลา (Timeline)
//
//  กลุ่มนี้เป็น "ระบบรอบนอก" ที่เกมยิงมาตอนเปิดหน้าจอต่าง ๆ แล้วเดิมเซิร์ฟไม่มี handler เลย
//  ⇒ ฝั่งเกมค้างรอคำตอบเงียบ ๆ (บางระบบตั้งธง initialized ได้ที่เดียวคือตอนได้คำตอบ)
//
//  เซิร์ฟเรายังไม่มีของจริงรองรับ: ไม่มีปฏิทินเช็คอิน · ไม่มีนิยามฤดูกาล ·
//  ไม่มีกระดานคะแนนเครื่องต่อยจริง · ไม่มีระบบ push
//  ⇒ ตอบ "โครงว่างที่ถูกต้องตามชนิดที่ฝั่งเกมรอ" แบบเดียวกับ Player.Social.cs
//     และ **ไม่แต่งของรางวัล/อีเวนต์ปลอม** (กฎ: ว่างดีกว่าหลอก)
//  ⇒ คำสั่งที่เป็น "การกระทำ" ซึ่งทำจริงไม่ได้ ตอบ Abort ที่มีข้อความเสมอ
//
//  ⚠️ DeregisterUser (1999) = "ลบบัญชีถาวร" — **ห้ามลบอะไรจริงเด็ดขาด** ปฏิเสธอย่างเดียว
//
//  ⚠️ ทุก Abort ในไฟล์นี้ต้องมี Text เสมอ (ห้าม default(Abort)):
//     client/GameManager.cs:269 ผูก global On<Abort>(DefaultAbortHandler)
//     → GameManager.cs:309-311 เรียก UIManager.SystemMsg(LimitText(msg.Text))
//     → GameManager.cs:290-297 LimitText อ่าน text.Length ตรง ๆ ⇒ Text = null คือ NRE ฝั่งเกม
//     (ผลข้างเคียงที่ตั้งใจ: ข้อความของเราจะขึ้นเป็น system message ให้ผู้เล่นเห็นเหตุผล)
//
//  ⚠️ ยังไม่ถูกต่อสาย: RegisterEventHandlers() ยังไม่มีใครเรียกใน Player.Systems.cs
//     (ไฟล์นั้นห้ามแตะจากงานนี้) ⇒ ต้องเพิ่มบรรทัด RegisterEventHandlers(); ใน
//     RegisterSystemHandlers() ไม่งั้น handler ทั้งไฟล์นี้ไม่ถูกลงทะเบียนเลย
//
//  — ทุก handler อ้างจุดยิงจริงจากต้นฉบับ (client/ = โค้ดฝั่งผู้เล่น)
//    เลขบรรทัดตรวจซ้ำแล้วกับไฟล์จริง ณ 6 ก.ย. 2026
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    // ตัวเลือกแจ้งเตือนของกระดานเวลา (ปุ่มกระดิ่งในหน้า Timeline)
    // **ค่าของเรา**: เก็บในหน่วยความจำต่อ session เท่านั้น — PlayerContext ยังไม่มีช่องเก็บ
    // และเซิร์ฟยังไม่มีระบบ push จริง ⇒ ค่าเริ่มต้น false (อนุรักษ์นิยม: ไม่เปิดแจ้งเตือน)
    private bool _timelineEstateNotification;

    private void RegisterEventHandlers()
    {
        // ── เช็คอินรายวัน (ปฏิทินอีเวนต์) ────────────────────────────────────────────
        //
        // หมายเหตุโครงสร้าง: ฝั่งเกมสร้างรายการปฏิทินได้ที่เดียวคือ push TodayAttendanceRewards
        // (client/Durango.Logic/EventSystem.cs:19,31 — global On<TodayAttendanceRewards>)
        // เราไม่ push ⇒ Calendars = null ⇒ เมนู Event ค้างปิด
        // (EventSystem.cs:18 ปิดไว้ตั้งแต่ Start · :24 เปิดเมื่อ KUtility.GetSize(Calendars) > 0)
        // สามตัวข้างล่างจึงแทบไม่ถูกยิง แต่ลงทะเบียนไว้กันค้าง/กัน log "ไม่มี handler"

        // GetAttendanceRewards (1097852) — ขอรายการรางวัลเช็คอินของหมวดนั้น
        // ยิงจาก client/Durango.Logic/EventSystem.cs:52 แล้วผูกคำตอบด้วย
        //   `.On(delegate(AttendanceRewards msg, ...))` ที่ EventSystem.cs:55
        // ⇒ ต้องตอบชนิด AttendanceRewards (1097853) ผูก seq
        // ⚠️ client/Durango.Logic.Event/Calendar.cs:100 เช็ค `if (rewards.Category == Category)`
        //    ก่อนทำอะไรต่อ ⇒ **ต้องสะท้อน Category ที่ขอมากลับไปเป๊ะ ๆ** ไม่งั้นค้างตลอด
        // Rewards/Appendices ว่าง: Calendar.InitRewards ใช้ KUtility.GetSize() รับ 0 ได้ปกติ
        // (ไม่แต่งรางวัลปลอม — เซิร์ฟไม่มีปฏิทินเช็คอินจริง)
        _connection.Recv(delegate(GetAttendanceRewards msg, PacketHeader header)
        {
            Send(new AttendanceRewards
            {
                Category = msg.Category,
                Rewards = Array.Empty<AttendanceReward>(),
                Appendices = Array.Empty<AttendanceReward>()
            }, header.Seq);
        });

        // GiveAttendanceReward (1097854) — "กดรับรางวัลเช็คอินของวันนี้"
        // ยิงจาก client/Durango.Logic/EventSystem.cs:67 แล้วผูก `.All(...)` ที่ :72 แล้วเช็ค
        // `typeCode == 1231` (OK) เท่านั้นถึงนับว่าสำเร็จ (EventSystem.cs:75-79)
        // ⇒ ตอบอะไรที่ไม่ใช่ OK = ล้มเหลวอย่างสุภาพ ฝั่งเกมไม่มาร์กว่ารับแล้ว (Calendar.cs:130-143)
        // เป็น "การกระทำ" ที่ทำจริงไม่ได้ (ไม่มีปฏิทิน/ไม่มีรางวัลผูกไว้) ⇒ Abort พร้อมข้อความ
        _connection.Recv(delegate(GiveAttendanceReward msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบรางวัลเช็คอิน" }, header.Seq);
        });

        // GiveAttendanceAppendix (1097855) — "กดรับรางวัลพิเศษท้ายปฏิทิน"
        // ยิงจาก client/Durango.Logic/EventSystem.cs:90 — เงื่อนไขสำเร็จเหมือนกันเป๊ะ
        // (`.All(...)` ที่ :94 · เช็ค typeCode == 1231 ที่ :98-101)
        // และ Calendar.cs:146-167 จะมาร์ก _appendices ต่อเมื่อ ok เท่านั้น ⇒ Abort ปลอดภัย
        _connection.Recv(delegate(GiveAttendanceAppendix msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบรางวัลพิเศษ" }, header.Seq);
        });

        // ── ฤดูกาล (Season) ─────────────────────────────────────────────────────────
        //
        // GetSeasons (871245) — ขอรายชื่อฤดูกาลใหม่ ยิงจาก
        // client/Durango.Logic/SeasonSystem.cs:41 `.On<Seasons>(OnSeasons)` ⇒ ตอบ Seasons (871246)
        // ⚠️ SeasonSystem.OnSeasons — ถ้า `msg._Seasons == null` มัน **return ทันทีที่ :48-51
        //    ก่อนถึง Initialized = true ที่ :66** ⇒ ต้องส่งอาร์เรย์ว่างที่ไม่ใช่ null เสมอ
        // ว่าง = "ตอนนี้ไม่มีฤดูกาลที่เปิดอยู่" — server/data/assets/season/ มีแต่ตารางรางวัล
        // season pass (season2_rewards_client.json) ไม่มีนิยามช่วงเวลา/แบนเนอร์ของฤดูกาลจริง
        // ⇒ ไม่เดาแต่งขึ้นเอง (ผลพลอยได้: ไม่มี Until ⇒ ฝั่งเกมไม่ตั้งเวลาขอซ้ำ = ไม่วนถาม)
        _connection.Recv(delegate(GetSeasons msg, PacketHeader header)
        {
            Send(new Seasons { _Seasons = Array.Empty<Season>() }, header.Seq);
        });

        // ── บัญชี / ข้อมูลส่วนบุคคล ──────────────────────────────────────────────────
        //
        // DeregisterUser (1999) — **ลบบัญชีถาวร** (เมนูตั้งค่า → "ถอนสมาชิก")
        // ยิงจาก client/Durango.System.Config/ConfigInstance.cs:855
        //   `.On<OK>(...)` → เรียก Platform.Leave() แล้วเด้งกลับหน้าไตเติล (ลบจริง)
        //   `.Rest(...)`   → ขึ้นกล่องข้อความ "요청을 처리하지 못했습니다" (ทำรายการไม่สำเร็จ)
        // ⇒ **ห้ามตอบ OK เด็ดขาด** — เราไม่มีระบบลบบัญชีและจะไม่ลบข้อมูลผู้เล่นใด ๆ ทั้งสิ้น
        //   ตอบ Abort (1024) ให้ตกเข้า .Rest = ปฏิเสธอย่างชัดเจน ไม่มีอะไรถูกลบ
        _connection.Recv(delegate(DeregisterUser msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ไม่รองรับการลบบัญชี — ข้อมูลของคุณไม่ถูกลบ" }, header.Seq);
        });

        // DeleteEngagementData (1444251) — ผู้เล่นปิดสวิตช์ยินยอมในป็อปอัปข้อตกลง
        // ยิงจาก client/Durango.UI.Popup/EngagementConfigPopup.cs:37
        //   `Connections.Frontend.Send(default(DeleteEngagementData));` — **ไม่ผูกรอคำตอบ**
        // (คู่กับ EngagementAgreementChanged 1444250 ที่รับไว้แล้วใน Player.Social.cs:91)
        // ⇒ รับเงียบ ๆ กัน log "ไม่มี handler" — และ **ไม่ลบข้อมูลจริงใด ๆ** เช่นกัน
        //   (เซิร์ฟไม่ได้เก็บข้อมูล engagement ไว้ตั้งแต่แรก จึงไม่มีอะไรให้ลบ)
        _connection.Recv(delegate(DeleteEngagementData msg, PacketHeader header)
        {
        });

        // ── มินิเกมเต้น ──────────────────────────────────────────────────────────────
        //
        // MiniGameDanceStarted (4625401) — แจ้งว่าเริ่มเล่นแล้ว
        // ยิงจาก client/Durango.UI/MiniGameDanceGroup.cs:333 (ใน StartGame) แบบ **ไม่ผูกรอ**
        // ⇒ รับเงียบ ๆ (มินิเกมคำนวณ/แสดงผลจบในตัวเกมเองทั้งหมด)
        _connection.Recv(delegate(MiniGameDanceStarted msg, PacketHeader header)
        {
        });

        // MiniGameDanceScore (4625400) — ส่งคะแนนรวมตอนจบเพลง
        // ยิงจาก client/Durango.UI/MiniGameDanceGroup.cs:479 แบบ **ไม่ผูกรอ**
        // แล้วเปิดหน้าสรุปผลเองทันที (KillGame + OpenWindow(Mode.End)) ไม่รอเซิร์ฟตอบ
        // ⇒ รับเงียบ ๆ (เรายังไม่มีที่เก็บสถิติ/รางวัลของมินิเกม จึงไม่แต่งผลตอบกลับ)
        _connection.Recv(delegate(MiniGameDanceScore msg, PacketHeader header)
        {
        });

        // ── กระดานคะแนนเครื่องต่อย (Punch Machine) ──────────────────────────────────
        //
        // GetPunchMachineLeaderboard (785103) — เปิดหน้ากระดานคะแนนของเครื่องต่อยเครื่องนั้น
        // ยิงจาก client/PunchingLeaderboardSystem.cs:72 แบบ **ไม่ผูก .On** — ฝั่งเกมรับคำตอบ
        // ด้วย **global On<PunchMachineLeaderboards>** (PunchingLeaderboardSystem.cs:60)
        // ⇒ ต้องส่งแบบ ReplyOf=0 (ไม่ผูก seq) ไม่งั้นไปไม่ถึงตัวรับ
        // ⚠️ OnPunchMachineLeaderboards:79-88 ไล่ `leaderboard.Contents[j].UserId` ทันที
        //    ⇒ ทั้ง 3 กระดานต้องมี Contents ที่ไม่ใช่ null (ว่างได้)
        // MyScore = null ⇒ ฟิลด์ถูก pack เป็น nil ตรงตาม LeaderboardContent? (ยังไม่เคยมีสถิติ)
        // ไม่แต่งอันดับปลอม — เซิร์ฟยังไม่เก็บคะแนนเครื่องต่อย
        _connection.Recv(delegate(GetPunchMachineLeaderboard msg, PacketHeader header)
        {
            Send(new PunchMachineLeaderboards
            {
                RegionRecentLeaderboard = new Leaderboard { Contents = Array.Empty<LeaderboardContent>() },
                RegionTotalLeaderboard = new Leaderboard { Contents = Array.Empty<LeaderboardContent>() },
                GlobalLeaderboard = new Leaderboard { Contents = Array.Empty<LeaderboardContent>() },
                MyScore = null
            });
        });

        // ── กระดานเวลา (Timeline) — ตัวเลือกแจ้งเตือน ───────────────────────────────
        //
        // GetTimelineOption (81234526) — ขอสถานะปุ่มกระดิ่งในหน้ากระดานเวลา
        // ยิงจาก client/Durango.Logic.Timeline/TimelineLogList.cs:161
        //   `.On(delegate(TimelineOption msg, ...))` ⇒ ต้องตอบชนิด TimelineOption (81234527)
        // ฝั่งเกมแคชค่าไว้ใน _option แล้วเอาไปตั้งไฟปุ่ม
        // (client/Durango.UI/TimelineLogGroup.cs:217-222 `_timelinePushButton.Selected`)
        // ⇒ ไม่ตอบ = ปุ่มค้างโปร่งใส (alpha 0) ตลอดกาล
        _connection.Recv(delegate(GetTimelineOption msg, PacketHeader header)
        {
            Send(new TimelineOption { EstateNotification = _timelineEstateNotification }, header.Seq);
        });

        // SetTimelineOption (81234528) — กดปุ่มกระดิ่งเปิด/ปิดแจ้งเตือนที่ดิน
        // ยิงจาก client/Durango.Logic.Timeline/TimelineLogList.cs:177 ด้วย `.All(...)` แล้วเช็ค
        // `Packet.IsSuccess(packet)` — ล้มเหลวเฉพาะ TypeCode 1022(Error)/1024(Abort)/3650(TimedOut)
        // (server/GameCode/Durango.Network/Packet.cs:116-127) ⇒ ตอบ OK (1231) = สำเร็จ
        // ถ้าล้มเหลวฝั่งเกมจะย้อนค่ากลับ (_option = prev) แล้วเรียก onResult(false)
        // **การตีความของเรา**: นี่คือ "ค่าตั้งค่าส่วนตัว" ไม่ใช่รางวัล ⇒ เก็บค่าไว้จริงต่อ session
        // แล้วตอบ OK ได้อย่างซื่อสัตย์ (GetTimelineOption จะอ่านค่าเดียวกันนี้กลับไป)
        // ข้อจำกัดที่รู้ตัว: ยังไม่ persist ลง PlayerContext และเซิร์ฟยังไม่มีระบบ push จริง
        _connection.Recv(delegate(SetTimelineOption msg, PacketHeader header)
        {
            _timelineEstateNotification = msg.EstateNotification;
            Send(default(OK), header.Seq);
        });
    }
}
