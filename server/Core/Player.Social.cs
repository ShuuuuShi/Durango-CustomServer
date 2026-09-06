using System;
using Durango.Network;
using Messages;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  สังคม: ปาร์ตี้ / เพื่อน / บันทึก / แคลน / กลุ่มผู้สนับสนุน / โนมัด / ผู้กลับ
//
//  เดิมเซิร์ฟไม่มี handler กลุ่มนี้เลย ⇒ เกมยิงมาตอนเข้าเกม/เปิดหน้าจอแล้วเงียบ
//  (log VPS 6 ก.ย.: "ไม่มี handler" type 20001/2402/2439/3667/2347809/100000/3450983/5015/1444250)
//  เซิร์ฟยังไม่มีระบบปาร์ตี้/แคลน/เพื่อนจริง ⇒ ตอบ "ค่าว่างแต่ถูกโครงสร้าง" ตามที่ client ยอม
//  — ทุก handler อ้างจุดยิง/จุดรับจากต้นฉบับ NEXON (nexonSRC) ทั้งหมด
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    private void RegisterSocialHandlers()
    {
        // GetParty (20001) — client ยิงแบบ **ไม่ผูกรอ** อะไรกลับ (nexonSRC/Durango.Logic/PartySystem.cs:203
        // `Connections.Frontend.Send(default(GetParty))` ไม่มี .On) — สถานะปาร์ตี้เดินทางมาทาง
        // push อื่น ⇒ ลงทะเบียนเปล่าไว้กัน log "ไม่มี handler"
        _connection.Recv(delegate(GetParty msg, PacketHeader header)
        {
        });

        // GetSocial (2402) — รายชื่อเพื่อน/คำขอเป็นเพื่อน/บล็อก — client รอ .On<Social>
        // (nexonSRC/SocialSystem.cs:908) ⇒ ตอบ Social ว่างทุกชุด (ยังไม่มีระบบเพื่อน)
        _connection.Recv(delegate(GetSocial msg, PacketHeader header)
        {
            Send(new Social
            {
                FollowingEntityIds = Array.Empty<string>(),
                FriendEntities = new(),
                ReceivedFriendRequests = Array.Empty<string>(),
                SentFriendRequests = Array.Empty<string>(),
                BlockedEntityIds = Array.Empty<string>(),
                FavoriteRegionOwners = Array.Empty<string>()
            }, header.Seq);
        });

        // GetMemos (2439) — บันทึก (สัตว์/พืช/แร่ ที่เคยเจอ) — client รอ .On<Memos>
        // (nexonSRC/MemoSystem.cs:46) ⇒ ตอบชุดว่าง (MemoStorage ยังไม่ผูก — เก็บทีหลังถ้าต้องการ)
        _connection.Recv(delegate(GetMemos msg, PacketHeader header)
        {
            Send(new Memos(), header.Seq);
        });

        // GetClanCreationCosts (3667) — ค่าสร้างแคลน — client รอ .On<Costs>
        // (nexonSRC/ClanSystem.cs:552 GetClanMakeCost) ข้อมูล costs.json จริง **ไม่มี** บล็อก
        // ค่าสร้างแคลน (มีแต่ clan_warphole_visit) ⇒ ตอบ Costs ว่าง ไม่เดาตัวเลขเอง (กฎ ROADMAP ข้อ 6)
        _connection.Recv(delegate(GetClanCreationCosts msg, PacketHeader header)
        {
            Send(new Costs(), header.Seq);
        });

        // GetSupportRequests (2347809) — คำขอสนับสนุนกลุ่ม (faction) — ฝั่งเกมรับด้วย
        // **global On<SupportRequests>** (nexonSRC/FactionSystem.cs:110) ไม่ได้ผูก .On กับคำขอ
        // ⇒ ตอบแบบ ReplyOf=0 ชุดว่าง (EndAt=0 = ไม่มีรอบขอสนับสนุนอยู่)
        _connection.Recv(delegate(GetSupportRequests msg, PacketHeader header)
        {
            Send(new SupportRequests());
        });

        // GetNomadInfo (100000) — สถานะ "โนมัด" (เก็บของข้ามเกาะชั่วคราว) — ฝั่งเกมรับ global
        // On(NomadInfo) (nexonSRC/PlayGuideSystem.cs:241-248: IsNomad/NomadCount) ⇒ ตอบ
        // ไม่ใช่โนมัด (ยังไม่มีระบบนี้)
        _connection.Recv(delegate(GetNomadInfo msg, PacketHeader header)
        {
            Send(new NomadInfo { IsNomad = false, NomadCount = 0 });
        });

        // GetReturnerInfo (3450983) — สถานะ "ผู้กลับ" (ผู้เล่นที่ห่างหายไปแล้วกลับมา — ได้โบนัส)
        // ฝั่งเกมรับ global On(ReturnerInfo) (nexonSRC/PlayGuideSystem.cs:206-220: IsReturner/Until
        // แล้วตั้งตัวจับเวลาขอซ้ำเมื่อ Until หมด) ⇒ ตอบไม่ใช่ผู้กลับ · Until=0 ไม่กระตุ้นให้ขอซ้ำ
        _connection.Recv(delegate(GetReturnerInfo msg, PacketHeader header)
        {
            Send(new ReturnerInfo { IsReturner = false, Since = 0.0, Until = 0.0, ReturnerCount = 0 });
        });

        // GetExpiredProducts (5015) — ประกาศขายของตัวเองที่หมดเวลา (ตลาด: มุม "ของที่หมดประกาศ")
        // client ยิงทันทีตอน AddOnReady (nexonSRC/MarketSystem.cs:81-89 OnReady → GetExpiredProduct)
        // และรอ .On<Products> ⇒ ตอบ Products ว่าง (เราไม่มีระบบประกาศขายของผู้เล่นจริง)
        _connection.Recv(delegate(GetExpiredProducts msg, PacketHeader header)
        {
            Send(new Products { _Products = Array.Empty<Product>() }, header.Seq);
        });

        // EngagementAgreementChanged (1444250) — client ส่งสถานะยินยอม (สัญญา/ข้อตกลง) แบบ
        // **ไม่ผูกรอ** (nexonSRC/EngagementSystem.cs:55 UpdateEngagement) ⇒ รับเงียบ ๆ กัน log
        _connection.Recv(delegate(EngagementAgreementChanged msg, PacketHeader header)
        {
        });
    }
}
