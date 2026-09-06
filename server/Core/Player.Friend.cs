using System;
using Durango.Network;
using Messages;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  เพื่อน / ติดตาม / บล็อก / สนทนา (แชทส่วนตัว)
//
//  เดิมเซิร์ฟรับแต่ GetSocial (2402 — Player.Social.cs:29) ที่ตอบ "รายชื่อว่าง" อย่างเดียว
//  ส่วน "การกระทำ" ทั้งหมด (ขอเป็นเพื่อน/รับ/ปฏิเสธ/ยกเลิก/เลิกเป็นเพื่อน/ติดตาม/บล็อก/
//  ชวนคุยส่วนตัว) ไม่มี handler เลย ⇒ ผู้เล่นกดปุ่มแล้ว **เงียบสนิท** ไม่มีอะไรเกิดขึ้น
//  และไม่มีข้อความบอกว่าทำไม
//
//  ── ทำไมยังตอบของจริงไม่ได้ ─────────────────────────────────────────────────────
//  ระบบเพื่อน/บล็อก/ติดตาม ต้องมีที่เก็บ "ข้ามผู้เล่น" และต้องอ่านกลับทาง GetSocial
//  แต่ GetSocial ตอบค่าคงที่ว่าง ๆ อยู่ที่ Player.Social.cs:29-40 และตัวเกมรีเฟรชรายชื่อ
//  ด้วยการยิง GetSocial ซ้ำทุกครั้งหลังทำรายการ (client/SocialSystem.cs:1066, 1100)
//  ⇒ ต่อให้เราจำไว้ในหน่วยความจำ คำขอ GetSocial ถัดไปก็ลบทิ้งทันที = หลอกผู้เล่นเปล่า ๆ
//  ⇒ ตามกฎ ROADMAP: คำสั่งที่ "ทำจริงไม่ได้" ให้ตอบ Abort ที่มีข้อความ ดีกว่าเงียบหรือโกหก
//     (ฝั่งเกมเอา Abort ไปขึ้นเป็นข้อความระบบ — client/GameManager.cs:309 DefaultAbortHandler)
//  ⚠️ ห้ามส่ง default(Abort) — Text เป็น null แล้วฝั่งเกมแครชที่ LimitText(null).Length
//
//  ── ข้อควรรู้เรื่องสาย (สำคัญมาก) ───────────────────────────────────────────────
//  ตัวเกมแยกสองการเชื่อมต่อ (client/Durango.Network/Connections.cs):
//    • Connections.Frontend  = เซิร์ฟเกม (ตัวนี้ — _connection ของเรา)
//    • Connections.Radiotower = เซิร์ฟแชทแยกต่างหาก
//  ของเราตอบ /entry เฉพาะ frontend_addresses ไม่มี radiotower_addresses
//  (server/Core/Gateway.cs:263) ⇒ ตัวเกมได้ endpoint แชทเป็นลิสต์ว่าง แล้ว Connect() ข้าม
//  ไปเลย (client/SocialSystem.cs:113 `if (_endpoints.Count != 0)`) ⇒ **message กลุ่ม
//  Radiotower ยังมาไม่ถึงเซิร์ฟเราเลยในตอนนี้** — ลงทะเบียนไว้เผื่อวันที่เปิดพอร์ตแชท
//  (หรือชี้ radiotower_addresses มาที่พอร์ตเดิม) จะได้ไม่เงียบ · ทุกตัวมีหมายเหตุกำกับว่าตัวไหน
//
//  หมายเหตุ: GetAvailableEmotions (9592634) **มี handler อยู่แล้ว** ที่ Player.cs:431
//  (ตอบท่าทาง/อีโมติคอนจาก DataStore.Emotions ของจริง) ⇒ ไม่ลงซ้ำที่นี่ เพราะ
//  Connection.Recv ตัวที่ลงทีหลังจะ **ทับ** ตัวเดิมของ TypeCode เดียวกัน
//  (server/GameCode/Durango.Online/Connection.cs:198-200)
//  และ GetSocial (2402) เป็นของ Player.Social.cs — ห้ามลงซ้ำเช่นกัน
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    // ข้อความเดียวกันทุกคำสั่งในกลุ่ม "เพื่อน" — ฝั่งเกมเอาไปขึ้นเป็นข้อความระบบ 4 วินาที
    private const string FriendUnavailableText = "ยังไม่เปิดใช้งานระบบเพื่อน";

    // กลุ่ม "ติดตาม/บล็อก" แยกข้อความ เพราะผู้เล่นเห็นเป็นคนละปุ่มคนละความหมาย
    private const string FollowUnavailableText = "ยังไม่เปิดใช้งานระบบติดตาม/บล็อกผู้เล่น";

    // กลุ่ม "สนทนาส่วนตัว" (แชทกลุ่มย่อย) — ต้องมีเซิร์ฟแชทถึงจะทำได้
    private const string ConversationUnavailableText = "ยังไม่เปิดใช้งานระบบสนทนาส่วนตัว";

    // รายการเกาะโปรด — ใช้ทั้งตอนเพิ่มและตอนลบ ต้องเป็นข้อความเดียวกัน
    // (เดิมพิมพ์สตริงซ้ำสองที่ แก้ที่เดียวลืมอีกที่ได้)
    private const string FavoriteRegionUnavailableText = "ยังไม่เปิดใช้งานรายการเกาะโปรด";

    private void RegisterFriendHandlers()
    {
        // ── เพื่อน (สาย Frontend — มาถึงเซิร์ฟเราจริง) ─────────────────────────────

        // RequestFriend (1451212) — ขอเป็นเพื่อน
        // จุดยิง: client/SocialSystem.cs:1023 RequestFriend(enable: true) รอ .On<Social>
        //         แล้วเอาไป SetSocial() ทั้งชุด (:1026-1029)
        // ปุ่มจริง: client/Durango.UI/SocialGroup.cs:230 · client/Durango.UI.Popup/PlayerInfoPopup.cs:335
        // ทำจริงไม่ได้ (ไม่มีที่เก็บคำขอข้ามผู้เล่น) ⇒ Abort พร้อมข้อความ
        _connection.Recv(delegate(RequestFriend msg, PacketHeader header)
        {
            Send(new Abort { Text = FriendUnavailableText }, header.Seq);
        });

        // AcceptFriendRequest (1451215) — รับคำขอเป็นเพื่อน
        // จุดยิง: client/SocialSystem.cs:984 รอ .On<Social> และมี .Rest(GetSocial) ต่อท้าย (:990)
        //         ⇒ พอได้ Abort (ไม่ตรงชนิด Social) ตัวเกมจะยิง GetSocial ตามเองเพื่อรีเฟรช
        //         ซึ่ง Player.Social.cs:29 ตอบรายชื่อว่าง = ตรงกับความจริง ไม่มีอะไรค้าง
        _connection.Recv(delegate(AcceptFriendRequest msg, PacketHeader header)
        {
            Send(new Abort { Text = FriendUnavailableText }, header.Seq);
        });

        // RefuseFriendRequest (1451216) — ปฏิเสธคำขอเป็นเพื่อน
        // จุดยิง: client/SocialSystem.cs:1010 รอ .On<Social> (ปุ่ม: SocialGroup.cs:218 RejectFriend)
        _connection.Recv(delegate(RefuseFriendRequest msg, PacketHeader header)
        {
            Send(new Abort { Text = FriendUnavailableText }, header.Seq);
        });

        // CancelFriendRequest (1451220) — ยกเลิกคำขอที่เราส่งไป
        // จุดยิง: client/SocialSystem.cs:998 รอ .On<Social>
        // ⚠️ ตัวเกมอ่าน `msg.FriendEntities.ContainsKey(entityId)` ตรง ๆ (:1003) ไม่เช็ค null
        //    ⇒ ถ้าวันหลังเปลี่ยนมาตอบ Social ต้องตั้ง FriendEntities = new() เสมอ ห้ามปล่อย null
        //    (แบบเดียวกับ Player.Social.cs:34) · ตอนนี้ตอบ Abort จึงไม่เข้าเส้นทางนั้น
        _connection.Recv(delegate(CancelFriendRequest msg, PacketHeader header)
        {
            Send(new Abort { Text = FriendUnavailableText }, header.Seq);
        });

        // RemoveFriend (1451217) — เลิกเป็นเพื่อน
        // จุดยิง: client/SocialSystem.cs:1033 RequestFriend(enable: false) รอ .On<Social>
        //         (ปุ่ม: client/Durango.UI.Popup/PlayerInfoPopup.cs:322 หลังกดยืนยัน)
        _connection.Recv(delegate(RemoveFriend msg, PacketHeader header)
        {
            Send(new Abort { Text = FriendUnavailableText }, header.Seq);
        });

        // SetFriendType (908134) — เลื่อนขั้นเพื่อน (JustFriend ⇄ BestFriend)
        // ชนิดค่า: Shared.Player.FriendType { Invalid = -1, JustFriend = 0, BestFriend = 1 }
        //          (server/GameCode/Shared.Player/FriendType.cs)
        // จุดยิง: client/SocialSystem.cs:972 ChangeFriendType รอ .On<Social>
        //         (ปุ่ม: client/Durango.UI.Popup/AccessRightsSettingPopup.cs:157 — ตั้งสิทธิ์เข้าที่ดิน)
        _connection.Recv(delegate(SetFriendType msg, PacketHeader header)
        {
            Send(new Abort { Text = FriendUnavailableText }, header.Seq);
        });

        // GetMyFriendType (78209743) — "ฉันเป็นเพื่อนระดับไหนในสายตาเจ้าของที่ดินคนนี้"
        // เป็น **คำถาม** ไม่ใช่การกระทำ ⇒ ต้องตอบชนิด FriendType (78209744) ที่ฝั่งเกมรอ
        // จุดยิง: client/SocialSystem.cs:1045 รอ .On<Messages.FriendType>
        // ผู้ใช้ผลลัพธ์:
        //   • client/Durango.UI/ArtifactInfoRights.cs:117 — ตั้ง _friendTypeLoaded = false ไว้ก่อนยิง
        //     **ไม่ตอบ = รายการสิทธิ์ค้างโหลดถาวร** ⇒ ต้องตอบเสมอ
        //   • client/Artifact.cs:1466 — เอาไปตัดสิน CheckEstatePermission/CheckArtifactPermission
        // ตอบ Invalid = "ไม่ใช่เพื่อน" ซึ่งเป็นค่าที่ตัวเกมใช้เองอยู่แล้วสำหรับกรณีคนแปลกหน้า
        // (Artifact.cs:1474 ส่ง FriendType.Invalid เข้าเช็คสิทธิ์ตรง ๆ · ArtifactInfoRights.cs:105
        //  ตั้งค่าเริ่มต้นเป็น Invalid) ⇒ ตรงกับความจริงของเซิร์ฟ (ยังไม่มีใครเป็นเพื่อนกัน)
        // หมายเหตุ: ตัวเกมยิงตัวนี้เฉพาะตอน IsFriend(ownerId) เป็นจริงเท่านั้น ⇒ ในทางปฏิบัติ
        // ตอนนี้แทบไม่ถูกยิงเลย (รายชื่อเพื่อนว่าง) แต่ลงไว้กันค้างถ้ามีเส้นทางอื่นเรียก
        _connection.Recv(delegate(GetMyFriendType msg, PacketHeader header)
        {
            Send(new FriendType { _FriendType = Shared.Player.FriendType.Invalid }, header.Seq);
        });

        // ── รายการเกาะโปรด (สาย Frontend) ─────────────────────────────────────────

        // AddFavoriteRegionOwners (20011) — เพิ่มเจ้าของเกาะเข้ารายการโปรด (สูงสุด 20 คน)
        // จุดยิง: client/SocialSystem.cs:1107 รอ .On<Social> → SetSocial ทั้งชุด
        //         (หน้าจอ: client/Durango.UI/FavoriteIslandsPage.cs:84 หลังเลือกจากช่องค้นหา)
        // เก็บถาวรไม่ได้: ค่านี้อยู่ใน Social.FavoriteRegionOwners ซึ่งอ่านกลับทาง GetSocial
        // (Player.Social.cs:38 ตอบ Array.Empty) และที่เก็บถาวรอยู่ใน PlayerContext ที่ห้ามแตะ
        // ⇒ Abort ดีกว่าตอบ Social ว่าง เพราะตอบว่างเฉย ๆ ผู้เล่นจะเห็นรายการไม่เพิ่มโดยไม่รู้สาเหตุ
        _connection.Recv(delegate(AddFavoriteRegionOwners msg, PacketHeader header)
        {
            Send(new Abort { Text = FavoriteRegionUnavailableText }, header.Seq);
        });

        // RemoveFavoriteRegionOwners (20012) — เอาออกจากรายการโปรด
        // จุดยิง: client/SocialSystem.cs:1118 รอ .On<Social> (FavoriteIslandsPage.cs:66 หลังกดยืนยัน)
        _connection.Recv(delegate(RemoveFavoriteRegionOwners msg, PacketHeader header)
        {
            Send(new Abort { Text = FavoriteRegionUnavailableText }, header.Seq);
        });

        // ── ตัวเลือกสังคม (สาย Frontend) ──────────────────────────────────────────

        // SetSocialOptions (24002) — สลับตัวเลือก AllowOutlanderConversation
        // (ยอมให้คนที่ไม่ใช่เพื่อนทักแชทส่วนตัวได้ — Shared.Social/SocialOptionType.cs)
        // จุดยิง: client/SocialSystem.cs:1548 — ยิงแบบ **ไม่ผูกรอ** ไม่มี .On ใด ๆ
        //         และตัวเกมอัปเดตค่าในเครื่องตัวเองไปแล้วก่อนยิง (:1542-1546)
        // ⇒ ห้ามตอบ Abort ที่นี่ ไม่งั้นผู้เล่นจะโดนข้อความระบบเด้งทุกครั้งที่กดสวิตช์ทั้งที่
        //   สวิตช์ในเครื่องทำงานปกติ · รับเงียบ ๆ กัน log "ไม่มี handler" ก็พอ
        // จำถาวรยังไม่ได้: ค่าเดินทางกลับไปตอนล็อกอินผ่าน Welcome.SocialOptions
        // (client/SocialSystem.cs:342 OnWelcome) ซึ่งประกอบที่ server/Core/GameServer.cs:322
        // SendWelcome และเก็บใน PlayerContext — สองไฟล์นั้นอยู่นอกขอบเขตไฟล์นี้
        _connection.Recv(delegate(SetSocialOptions msg, PacketHeader header)
        {
        });

        // ── ติดตาม / บล็อก (สาย Radiotower — ยังมาไม่ถึงเซิร์ฟเรา ดูหมายเหตุหัวไฟล์) ──

        // Follow (2401) — เพิ่มผู้เล่นเข้า "รายการโปรด/ติดตาม"
        // จุดยิง: client/SocialSystem.cs:1062 ผ่าน Connections.Radiotower
        //         รอด้วย .All(packet => if (Packet.IsSuccess(packet)) GetSocial())  (:1065-1071)
        //         Packet.IsSuccess เป็นเท็จเมื่อ TypeCode = Error(1022)/Abort(1024)/TimedOut(3650)
        //         (client/Durango.Network/Packet.cs:90) ⇒ ตอบ Abort แล้วตัวเกมจะไม่ยิง GetSocial ซ้ำ
        //         และผู้เล่นได้เห็นข้อความว่าทำไมกดแล้วไม่ขึ้น
        _connection.Recv(delegate(Follow msg, PacketHeader header)
        {
            Send(new Abort { Text = FollowUnavailableText }, header.Seq);
        });

        // Unfollow (2410) — เลิกติดตาม
        // จุดยิง: client/SocialSystem.cs:1059 (Radiotower) — เส้นทางรับผลเดียวกับ Follow
        //         ปุ่ม: client/Durango.UI/SocialGroup.cs:242 CancelFollow
        _connection.Recv(delegate(Unfollow msg, PacketHeader header)
        {
            Send(new Abort { Text = FollowUnavailableText }, header.Seq);
        });

        // Block (4016) — บล็อกผู้เล่น
        // จุดยิง: client/SocialSystem.cs:1084 (Radiotower) — ก่อนหน้านั้นตัวเกมยิง KickVisitor
        //         ไปทาง Frontend แยกต่างหาก (:1079) ซึ่งมี handler ของตัวเองอยู่แล้วที่
        //         server/Core/Player.Estate.cs (บล็อก KickVisitor 20424 — ไฟล์นั้นยังถูกแก้อยู่
        //         เลขบรรทัดเลื่อนตลอด จึงไม่อ้างเลขบรรทัด) ⇒ คนละสาย ไม่เกี่ยวกัน
        //         ปุ่ม: client/Durango.UI.Popup/BlockPopup.cs:48
        _connection.Recv(delegate(Block msg, PacketHeader header)
        {
            Send(new Abort { Text = FollowUnavailableText }, header.Seq);
        });

        // Unblock (4017) — เลิกบล็อก
        // จุดยิง: client/SocialSystem.cs:1091 (Radiotower) — เส้นทางรับผลเดียวกับ Block
        _connection.Recv(delegate(Unblock msg, PacketHeader header)
        {
            Send(new Abort { Text = FollowUnavailableText }, header.Seq);
        });

        // ── แชท/สนทนาส่วนตัว (สาย Radiotower — ยังมาไม่ถึงเซิร์ฟเรา) ────────────────

        // Tune (2400) — "ยืนยันตัวตน" ของสายแชท (EntityId + SessionToken + SyncedAt)
        // จุดยิง: client/SocialSystem.cs:127 RequestAuth() ทันทีที่สายแชทต่อติด
        //         รอ .On<Conversations> (:132) แล้วจึงตั้ง State = Ready + ยิง event Ready
        //         ⇒ ไม่ตอบ = สายแชทค้างที่ Authenticating ตลอด และ GetLatestChatLog
        //         ไม่ถูกเรียกเลย (:394 อยู่ใน ConnectionHelper_Ready)
        // ตอบ Conversations ว่าง = "ยืนยันตัวตนผ่าน แต่ยังไม่มีห้องสนทนาส่วนตัวค้างอยู่"
        // เป็นโครงว่างที่ถูกต้อง ไม่ใช่ข้อมูลปลอม
        // หมายเหตุ: ถ้าวันหลังชี้ radiotower_addresses มาที่พอร์ตเกม ต้องดูเรื่องการยืนยัน
        // ตัวตนของสายใหม่ที่ GameServer ด้วย — handler นี้ผูกกับ Player ที่ล็อกอินแล้วเท่านั้น
        _connection.Recv(delegate(Tune msg, PacketHeader header)
        {
            Send(new Conversations { _Conversations = Array.Empty<Conversation>() }, header.Seq);
        });

        // GetLatestChatLog (25) — ประวัติแชทล่าสุดของแต่ละช่อง
        // จุดยิง: client/SocialSystem.cs:413 — ยิงทีละช่อง 5 ช่อง (Region/Clan/ClanWar/Party/
        //         PersonalRegions ที่ :403-410) แต่ละครั้งรอ .On<ChatLogs> (:416)
        //         → ReceiveChatLog() เอาไปเติมกล่องแชท
        // เซิร์ฟเราไม่เก็บประวัติแชท (SayInExclusiveChannel ที่ Player.cs:438 กระจายสดอย่างเดียว
        // ไม่ได้บันทึกลงที่ไหน) ⇒ ตอบชุดว่าง = ตรงกับความจริง
        _connection.Recv(delegate(GetLatestChatLog msg, PacketHeader header)
        {
            Send(new ChatLogs { Logs = Array.Empty<Message_>() }, header.Seq);
        });

        // InviteToConversation (2411) — เปิด/ชวนคนเข้าห้องสนทนาส่วนตัว
        // จุดยิง 2 แห่ง (client/SocialSystem.cs):
        //   • :1193 InviteToConversation() — ชวนเพิ่มเข้าห้องเดิม ยิงแบบไม่ผูกรอ
        //   • :1281 RequestConversation() — เปิดห้องใหม่ รอ .On<OK> (:1285) เพื่อรีเฟรชรายการห้อง
        // เปิดห้องจริงไม่ได้ (ต้องมีเซิร์ฟแชทเก็บห้อง + ส่ง Conversation กลับไปให้ทุกคนในห้อง)
        // ⇒ Abort พร้อมข้อความ · ตอบ OK จะเป็นการโกหก เพราะรายการห้องจะไม่มีอะไรเพิ่มขึ้นจริง
        _connection.Recv(delegate(InviteToConversation msg, PacketHeader header)
        {
            Send(new Abort { Text = ConversationUnavailableText }, header.Seq);
        });

        // ExitConversation (4010) — ออกจากห้องสนทนา
        // จุดยิง: client/SocialSystem.cs:1202 — ยิงแบบ **ไม่ผูกรอ** และตัวเกมลบห้องออกจาก
        //         รายการในเครื่องตัวเองทันทีหลังยิง (:1204-1205) ⇒ หน้าจอปิดห้องไปแล้ว
        // ⇒ ห้ามตอบ Abort จะกลายเป็นข้อความเด้งทั้งที่ผู้เล่นออกจากห้องสำเร็จในสายตาตัวเอง
        //   รับเงียบ ๆ กัน log "ไม่มี handler"
        _connection.Recv(delegate(ExitConversation msg, PacketHeader header)
        {
        });

        // ToggleConversationNotification (4011) — เปิด/ปิดแจ้งเตือนของห้องสนทนา
        // จุดยิง: client/SocialSystem.cs:1252 AllowConversationPush() — ยิงแบบไม่ผูกรอ
        //         และตั้งค่าในเครื่องตัวเองไปก่อนแล้ว (:1249 value.PushEnabled = allowPush)
        // ⇒ เหตุผลเดียวกับ ExitConversation: รับเงียบ ๆ
        _connection.Recv(delegate(ToggleConversationNotification msg, PacketHeader header)
        {
        });
    }
}
