using System.Collections.Generic;
using Durango.Network;
using Messages;
using Shared.Chat;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  แคลน (Clan / "부족" = เผ่า) — คำสั่งจัดการเผ่าทั้งชุดที่ฝั่งเกมยิงเข้ามา
//
//  ═══ ทำไมเซิร์ฟนี้ยังมี "เผ่าจริง" ไม่ได้ ═══
//  ข้อมูลเผ่าของ NEXON **ไม่ได้เดินทางมาทางสาย TCP ของเกมเลย** ตัวเผ่าถูกดึงผ่าน HTTP gateway:
//    client/ClanSystem.cs:116-140  RequestClanInfo → GET {GatewayUrl}/clans/{id}?detail
//    client/ClanSystem.cs:155-185  ค้นหาเผ่า        → GET {GatewayUrl}/clans?keyword=...
//    client/ClanSystem.cs:217-239  RequestPlayerClan → ถาม /clans ด้วย ClanId ของตัวละคร
//  สาย TCP มีแต่ "คำสั่ง" (สร้าง/เข้า/ออก/เตะ) แล้วฝั่งเกมจะไปโหลดผลลัพธ์จาก gateway ต่อเสมอ
//  ⇒ server/Core/Gateway.cs ของเราไม่มีเส้น /clans และ PlayerContext ไม่มีที่เก็บเผ่า
//    ⇒ ต่อให้ตอบ OK ให้ MakeClan/JoinClan ฝั่งเกมก็จะได้เผ่ากลับมาเป็น null อยู่ดี
//      (client/ClanSystem.cs:263-293 UpdateLocalplayerMember: หาตัวเองในเผ่าไม่เจอ = ล้าง Clan ทิ้ง)
//    ⇒ ตอบ OK = โกหกผู้เล่น (ขึ้นป้าย "สร้างเผ่าสำเร็จ" ที่ ClanSystem.cs:543 แล้วไม่มีเผ่าจริง)
//  ⇒ ทุกคำสั่งที่เป็น "การกระทำ" ในไฟล์นี้จึงตอบ Abort พร้อมข้อความ ไม่ใช่เงียบและไม่ใช่ OK
//
//  ═══ ฝั่งเกมแปลผลลัพธ์ยังไง (ทั้งสองทางมอง Abort = ล้มเหลว ตรงกัน) ═══
//    - client/Durango.Network/Packet.cs:90-101  IsSuccess → 1022(Error)/1024(Abort)/3650(TimedOut) = ล้มเหลว
//    - client/ClanSystem.cs:479-482             HandleResult → สำเร็จ = TypeCode 1231 (OK) เท่านั้น
//  ⚠️ Abort ต้องมี Text เสมอ — client/GameManager.cs:309-312 DefaultAbortHandler เรียก
//     LimitText(msg.Text) ทันที ถ้า Text เป็น null ฝั่งเกมแครช (nil → UnpackGettext คืน null)
//     ตรวจแล้วว่าส่งเป็นสตริงเปล่า ๆ ได้ ไม่ต้องเป็น msgid: client/LocalizeSystem.cs:583-586
//     รับ UnderlyingType == string ตรง ๆ
//
//  ═══ 3 ตัวท้าย (แจ้งเตือน/สมัครช่องแชทเผ่า) มาทางสายอื่น ═══
//  ToggleClanNotification · GetClanNotificationEnabled · ResubscribeClanChannel ฝั่งเกมยิงผ่าน
//  **Connections.Radiotower** ไม่ใช่ Frontend (client/SocialSystem.cs:395, 1228, 1341)
//  และ gateway ของเรา (server/Core/Gateway.cs:261-265) แจกแต่ "frontend_addresses"
//  ไม่มี "radiotower_addresses" ที่ client/Durango.UI/TitleMenuGroup.cs:918-922 ใช้ตั้ง endpoint
//  ⇒ สาย radiotower ไม่เคยต่อติด ตอนนี้ยังมาไม่ถึง handler พวกนี้
//    แต่ลงทะเบียนไว้ให้ถูกชนิดเผื่อวันหลังชี้ radiotower มาที่พอร์ตเกมเดียวกัน (เรามี Connection
//    เดียวต่อผู้เล่น) — ไม่มีผลเสียถ้ายังไม่ต่อ และกัน log "ไม่มี handler" ล่วงหน้า
//
//  ℹ️ ข้อมูลจริงที่มี: server/data/assets/clan.json มีแค่ level_thresholds + level_rewards
//     (ของฝั่ง client ใช้คำนวณแถบ exp เผ่า — client/ClanSystem.cs:363-383) **ไม่มี** ค่าสร้างเผ่า
//     ไม่มีคลังเงินเผ่า ไม่มีรายชื่อสมาชิก ⇒ ไม่มีอะไรให้เอามาตอบเป็นข้อมูลจริงได้เลย
//  ℹ️ ต้นฉบับเซิร์ฟจำลองของ NEXON (nexonSRC/Durango.Offline/Player.cs) ไม่มีคำว่า Clan สักตัว
//     — ระบบเผ่าเป็นของโหมด Online ล้วน ๆ จึงไม่มีสไตล์ต้นฉบับให้ลอก
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    // สวิตช์แจ้งเตือนแชทช่องเผ่า (Clan / ClanWar) ที่ผู้เล่นกดเปิด-ปิดเอง
    // เก็บไว้ "ในเซสชันนี้เท่านั้น" — ยังไม่บันทึกลง PlayerContext เพราะงานนี้ห้ามแก้ไฟล์อื่น
    // ⇒ ออกจากเกมแล้วค่าจะกลับเป็นว่าง (= ไม่ได้เปิดช่องไหนเลย) **การตีความของเรา**
    // ค่าเริ่มต้น null แปลว่า "ผู้เล่นยังไม่เคยตั้ง" ตอบกลับเป็นแมปว่างซึ่งฝั่งเกมอ่านว่าปิดหมด
    // (client/SocialSystem.cs:1240-1244 IsClanPushEnabled → dict.Get(key, defaultValue: false))
    private Dictionary<ChannelType, bool> _clanChannelNotifications;

    private void RegisterClanHandlers()
    {
        // ── กลุ่มที่ 1: คำสั่งจัดการเผ่า (ทำจริงไม่ได้ → Abort พร้อมข้อความ) ────────────

        // MakeClan (3651) — ปุ่ม "สร้างเผ่า" · client/ClanSystem.cs:535-548
        // ยิงพร้อม ClanName + Currency แล้ว .On<OK> จะเด้งป้าย "สร้างเผ่า <ชื่อ> แล้ว"
        // ต่อด้วย .All → HandleResult(ClanSystem.cs:479-482) ปิดหน้าต่างถ้าสำเร็จ
        // ⇒ ต้องไม่ตอบ OK เด็ดขาด ไม่งั้นป้ายขึ้นแต่เผ่าไม่มีอยู่จริง
        _connection.Recv(delegate(MakeClan msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังสร้างเผ่าไม่ได้" }, header.Seq);
        });

        // JoinClan (3655) — ขอเข้าเผ่า (จากหน้าค้นหาเผ่า) · client/ClanSystem.cs:484-496
        // .On<OK> เด้งป้าย "ยื่นใบสมัครแล้ว" · .All → HandleResult
        _connection.Recv(delegate(JoinClan msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังสมัครเข้าเผ่าไม่ได้" }, header.Seq);
        });

        // LeaveClan (3652) — ออกจากเผ่า · client/ClanSystem.cs:558-576
        // ฝั่งเกมกันไว้ชั้นหนึ่งแล้ว (PlayerClan == null → ไม่ยิง) ⇒ ที่มาถึงตรงนี้แปลว่า
        // ฝั่งเกมคิดว่ามีเผ่าอยู่ แต่ฝั่งเราไม่มีที่เก็บ ⇒ บอกตรง ๆ ว่าทำให้ไม่ได้
        _connection.Recv(delegate(LeaveClan msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังออกจากเผ่าไม่ได้" }, header.Seq);
        });

        // RenameClan (36510) — เปลี่ยนชื่อเผ่า · client/ClanSystem.cs:453-465
        // .All → ถ้าสำเร็จเรียก RefreshPlayerClan() ไปโหลดเผ่าใหม่จาก gateway
        _connection.Recv(delegate(RenameClan msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังเปลี่ยนชื่อเผ่าไม่ได้" }, header.Seq);
        });

        // SetClanInfo (3699) — ป้ายประกาศ (Notice) + คำแนะนำเผ่า (Intro)
        // client/ClanSystem.cs:433-451 SetClanComment ← เรียกจากปุ่มส่งของกระดานสองอัน
        // (client/Durango.UI/ClanInfoPage.cs:155-168 Intro · :170-181 Notice — ผูกปุ่มไว้ที่ :91,:93)
        // .All → onResult(false) จะคาโหมดแก้ไขไว้ให้ผู้เล่นเห็นว่าไม่ได้บันทึก — ถูกต้องแล้ว
        _connection.Recv(delegate(SetClanInfo msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังบันทึกป้ายประกาศเผ่าไม่ได้" }, header.Seq);
        });

        // SetClanEmblem (3695) — ตราเผ่า (ส่งมาเป็น byte[]) · client/ClanSystem.cs:520-533
        // รอ .On<OK> อย่างเดียว (ไม่มี .All) ⇒ ตอบ Abort = ไม่มีอะไรถูกเรียกต่อ
        // ผู้เล่นเห็นข้อความจาก DefaultAbortHandler แทน
        _connection.Recv(delegate(SetClanEmblem msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังเปลี่ยนตราเผ่าไม่ได้" }, header.Seq);
        });

        // KickClanMember (3661) — เตะสมาชิกออกจากเผ่า · client/ClanSystem.cs:578-590
        _connection.Recv(delegate(KickClanMember msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังจัดการสมาชิกเผ่าไม่ได้" }, header.Seq);
        });

        // SetClanMemberRole (3662) — ย้ายตำแหน่งสมาชิก · client/ClanSystem.cs:678-692
        // (TargetId + RoleId) รอ .On<OK> แล้วค่อย RefreshPlayerClan
        _connection.Recv(delegate(SetClanMemberRole msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังเปลี่ยนตำแหน่งสมาชิกไม่ได้" }, header.Seq);
        });

        // ── กลุ่มที่ 1ข: จัดการ "ตำแหน่ง" (role) ในเผ่า — หน้าต่างตั้งค่าตำแหน่ง ────────────
        // ทั้งสามตัวนี้ฝั่งเกมยิงจาก client/Durango.UI/ClanRoleManageGroup.cs (ผูก event ที่ :32-34)
        // และ **รอคำตอบทุกตัว** ผ่าน .All(...) ⇒ ไม่ลงทะเบียน = เซิร์ฟเงียบสนิท = callback
        // ไม่ถูกเรียกเลย ซึ่งอันตรายกว่าตอบล้มเหลว (ดูหมายเหตุ SetMemberRoleInfo ข้างล่าง)

        // SetMemberRoleGrades (792252) — ลากสลับลำดับชั้นของตำแหน่ง
        // ฟิลด์จริง (server/GameCode/Messages/SetMemberRoleGrades.cs:9): RoleOrder[] RoleOrders
        //   (RoleOrder = RoleId + Grade — server/GameCode/Messages/RoleOrder.cs:7-9)
        // จุดยิง: client/Durango.UI/ClanRoleManageGroup.cs:87-90 OnUpdateRoleOrder
        //         → client/ClanSystem.cs:603-633 .All → onResult(IsSuccess) + RefreshPlayerClan
        //         (ที่นี่ส่ง onResult = null มา ⇒ ล้มเหลวแล้วเงียบ ไม่มี UI ค้าง)
        _connection.Recv(delegate(SetMemberRoleGrades msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังจัดลำดับตำแหน่งในเผ่าไม่ได้" }, header.Seq);
        });

        // SetMemberRoleInfo (3681) — แก้ชื่อ/สิทธิ์ของตำแหน่ง
        // ฟิลด์จริง (server/GameCode/Messages/SetMemberRoleInfo.cs:9-11): int RoleId · MemberRole Info
        // จุดยิง: client/Durango.UI/ClanRoleManageGroup.cs:92-107 OnChangeRole
        //         → client/ClanSystem.cs:636-654 .All → onResult(IsSuccess) + RefreshPlayerClan
        // ⚠️ ตัวนี้ "ห้ามเงียบ" เด็ดขาด: ClanRoleManageGroup.cs:98 ตั้ง _isModifying = true ก่อนยิง
        //    แล้วปลดล็อกกลับเป็น false ได้ที่เดียวคือ :101 ซึ่งอยู่ใน callback ของคำตอบ
        //    ⇒ ไม่ตอบ = _isModifying ค้าง true ตลอดกาล → :94 ดีดออกทุกครั้ง → ผู้เล่นแก้ตำแหน่ง
        //      ไม่ได้อีกเลยจนกว่าจะปิดเกม (ตอบ Abort = IsSuccess false แต่ callback ยังทำงาน ปลดล็อกได้)
        _connection.Recv(delegate(SetMemberRoleInfo msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังแก้ไขตำแหน่งในเผ่าไม่ได้" }, header.Seq);
        });

        // RemoveMemberRole (792253) — ลบตำแหน่งทิ้ง แล้วย้ายคนในตำแหน่งนั้นไปตำแหน่งอื่น
        // ฟิลด์จริง (server/GameCode/Messages/RemoveMemberRole.cs:9-11): int RoleId · int MoveToRoleId
        // จุดยิง: client/Durango.UI/ClanRoleManageGroup.cs:109+ OnRemoveRole (:158 หลังเลือกปลายทาง)
        //         → client/ClanSystem.cs:657-676 .All → onResult(IsSuccess) + RefreshPlayerClan
        _connection.Recv(delegate(RemoveMemberRole msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังลบตำแหน่งในเผ่าไม่ได้" }, header.Seq);
        });

        // ℹ️ GetClanEstateLicense (3697) **ไม่ต้องมี handler** — ตรวจทั้ง client/ แล้วไม่มีจุดไหน
        //    ส่งมันออกมาเลย (เจอแต่ตัวนิยาม client/Messages/GetClanEstateLicense.cs) = message ตายแล้ว
        //    ส่วนใบอนุญาตที่ดินของเผ่าเป็นงานของ server/Core/Player.Estate.cs ไม่ใช่ไฟล์นี้

        // InviteToClan (3660) — ชวนคนที่ยืนอยู่ตรงหน้าเข้าเผ่า
        // ยิงจากเมนูปฏิสัมพันธ์ Interaction.InviteToClan (client/ClanSystem.cs:59-66 → :592-601)
        // รอ .On<OK> เพื่อเด้งป้าย "ชวน <ชื่อ> เข้าเผ่าแล้ว"
        _connection.Recv(delegate(InviteToClan msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังชวนคนเข้าเผ่าไม่ได้" }, header.Seq);
        });

        // ApproveClanApplier (3657) — รับใบสมัครเข้าเผ่า · client/ClanSystem.cs:498-507
        // .All เรียก RefreshPlayerClan() ทุกกรณี (ไม่สนสำเร็จหรือไม่) ⇒ ตอบ Abort ปลอดภัย
        _connection.Recv(delegate(ApproveClanApplier msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังรับสมาชิกใหม่ไม่ได้" }, header.Seq);
        });

        // DropClanApplier (3659) — ปฏิเสธใบสมัครเข้าเผ่า · client/ClanSystem.cs:509-518
        _connection.Recv(delegate(DropClanApplier msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังจัดการใบสมัครไม่ได้" }, header.Seq);
        });

        // CancelClanJoinRequest (1923487521) — ผู้เล่นถอนใบสมัครที่ยื่นค้างไว้เอง
        // client/ClanSystem.cs:734-743 CancelWaitingClan รอ .On<OK> เพื่อเด้งป้ายยืนยัน
        // ⚠️ ยิงได้ก็ต่อเมื่อ WaitingClan ไม่ว่าง ซึ่งของเราไม่มีทางเกิด (ไม่มีเส้น /clans)
        _connection.Recv(delegate(CancelClanJoinRequest msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังยกเลิกใบสมัครไม่ได้" }, header.Seq);
        });

        // ── กลุ่มที่ 2: คลังเงินเผ่า ──────────────────────────────────────────────────

        // GetClanFund (3678) — ขอยอดคลังเงินเผ่า · client/ClanSystem.cs:395-405
        // รอ .On<Costs> (TypeCode 4024) ⇒ เป็น "คำถาม" ไม่ใช่การกระทำ ⇒ ตอบโครงว่างที่ถูกชนิด
        // จุดเรียก: ClanInfoPage.cs:152 (หน้าเผ่า) · EstateGridGroup.cs:656 (ซื้อที่ดินด้วยเงินเผ่า)
        //           PresetCurrencyWidget.cs:173 (ป้ายยอดเงิน)
        // ทั้งสามจุดเป็น callback ทางเดียว ไม่ตอบ = ป้ายยอดเงินค้างเป็นค่าเดิมตลอด
        // ⚠️ ห้ามแต่งยอดเงิน — Costs ว่าง = คลังเผ่าว่าง (ซึ่งเป็นความจริง เพราะไม่มีคลัง)
        //    ฝั่งเกม Unpack สร้าง Dictionary ว่างให้เองเสมอ (client/Messages/Costs.cs:37-52)
        //    ⇒ ส่ง _Costs = null ได้ ตัว Pack แปลงเป็น map ขนาด 0 ให้ (Messages/Costs.cs:23-27)
        // หมายเหตุ: ClanSystem.GetClanFund ยิงเฉพาะตอน PlayerClan != null ⇒ ในทางปฏิบัติ
        //           ยังมาไม่ถึงจนกว่าจะมีเผ่าจริง แต่ลงทะเบียนไว้ให้ครบชนิด
        _connection.Recv(delegate(GetClanFund msg, PacketHeader header)
        {
            Send(new Costs(), header.Seq);
        });

        // DonateToClanFund (3679) — บริจาคเงิน (TStone) เข้าคลังเผ่า
        // client/Durango.UI/ClanInfoPage.cs:383-401 ตรวจยอดเงินในกระเป๋าเองก่อน แล้วยิงมา
        // พร้อม Costs แล้วรอ .On<Costs> เพื่อเอายอดคลังใหม่ไปแสดง
        // ⇒ เป็น "การกระทำ" ที่ทำจริงไม่ได้ (ไม่มีคลังเผ่า) ⇒ Abort — สำคัญมากที่ต้องไม่ตอบ
        //   Costs กลับไป เพราะจะกลายเป็นว่าผู้เล่นเห็นยอดคลังขยับทั้งที่ไม่มีอะไรเกิดขึ้น
        //   (เงินในกระเป๋าฝั่งเซิร์ฟไม่ถูกหัก เพราะเราไม่ได้แตะ Wallet เลย — ถูกต้องแล้ว)
        _connection.Recv(delegate(DonateToClanFund msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังบริจาคเข้าคลังเผ่าไม่ได้" }, header.Seq);
        });

        // ── กลุ่มที่ 3: ของรางวัล/บัฟของเผ่า (ยิงมาแบบไม่รอคำตอบ) ─────────────────────

        // RequestClanRewards (3706) — ขอรับของรางวัลระดับเผ่า · client/ClanSystem.cs:295-303
        // ⚠️ ฝั่งเกม `Connections.Frontend.Send(default(RequestClanRewards))` **ไม่มี .On และ
        //    ไม่มี global handler ของผลลัพธ์เลย** (ไล่ทั้ง client แล้วไม่เจอตัวรับ)
        //    มันถูกเรียกจาก push ClanRewardsUpdated(3705) ที่มาทาง Radiotower เท่านั้น
        //    ⇒ ของจริงคือเซิร์ฟหยอดของเข้ากระเป๋า/กล่องจดหมาย ไม่ได้ตอบ message กลับ
        // เราไม่มีเผ่า ⇒ ไม่มีรางวัลให้ ⇒ **ห้ามแจกของมั่ว** รับไว้เฉย ๆ กัน log "ไม่มี handler"
        // (และเราไม่เคยส่ง ClanRewardsUpdated ⇒ ในทางปฏิบัติตัวนี้จะไม่ถูกยิงมาเลย)
        _connection.Recv(delegate(RequestClanRewards msg, PacketHeader header)
        {
        });

        // RequestClanStatusEffects (3704) — ขอบัฟ (status effect) ที่ได้จากเผ่า
        // client/ClanSystem.cs:305-313 ยิงแบบไม่รอคำตอบเช่นกัน ตัวรับจริงคือ global
        // Connections.Frontend.On<Messages.StatusEffects> (client/StatusEffectSystem.cs:29)
        // ⚠️ ห้าม push StatusEffects เปล่ากลับไป — client/Durango.Logic/StatusEffects.cs
        //    แทนที่ list ทั้งก้อนต่อ 1 EntityId (ดูคอมเมนต์ที่ server/Core/Player.cs:310-315)
        //    ⇒ ส่งชุดว่างจะไปล้างบัฟจริงของผู้เล่น (อาหาร/ไฟ/สภาพอากาศ) ทิ้งหมด
        // เราไม่มีบัฟเผ่า ⇒ ไม่ต้องส่งอะไร ชุดบัฟที่ถูกต้องถูกส่งอยู่แล้วโดย SendStatusEffects()
        // (server/Core/Player.cs:681) ⇒ รับไว้เฉย ๆ กัน log
        _connection.Recv(delegate(RequestClanStatusEffects msg, PacketHeader header)
        {
        });

        // ── กลุ่มที่ 4: ช่องแชทเผ่า (มาทางสาย Radiotower — ดูหมายเหตุหัวไฟล์) ──────────

        // ToggleClanNotification (4025) — ผู้เล่นกดเปิด/ปิดแจ้งเตือนแชทช่องเผ่า
        // client/SocialSystem.cs:1224-1232 ToggleClanPush: สลับค่าในเครื่องก่อน แล้วส่งทั้งแมป
        // มาให้เซิร์ฟจำ **ไม่รอคำตอบ** ⇒ ห้ามตอบอะไรกลับ แค่จำไว้
        _connection.Recv(delegate(ToggleClanNotification msg, PacketHeader header)
        {
            _clanChannelNotifications = msg.ChannelNotificationsEnabled;
            // [7 ก.ย. 2026] เซฟลงไฟล์ผู้เล่นด้วย — เดิมอยู่แต่ในหน่วยความจำ ออกเกมแล้วค่าหาย
            var saved = new Dictionary<int, bool>();
            if (msg.ChannelNotificationsEnabled != null)
            {
                foreach (var pair in msg.ChannelNotificationsEnabled)
                {
                    saved[(int)pair.Key] = pair.Value;
                }
            }
            _context.ClanChannelNotifications = saved;
            OnContextChanged();
        });

        // GetClanNotificationEnabled (4027) — ถามค่าที่จำไว้ ตอนต่อห้องแชทติดใหม่
        // client/SocialSystem.cs:395-398 (ในสาย ConnectionHelper_Ready) รอ **.On<ToggleClanNotification>**
        // แล้วเอา msg.ChannelNotificationsEnabled ไปวางทับ _clanChannelPushEnabled ตรง ๆ
        // ⇒ ต้องตอบด้วยชนิด ToggleClanNotification (ไม่ใช่ OK) และ **ห้ามส่งแมปเป็น null**
        //    เพราะ SocialSystem.cs:1227 และ :1243 เรียก .Get(key, false) บนตัวนั้นทันที
        //    (ค่าตั้งต้นฝั่งเกมเป็น Dictionary ว่างอยู่แล้ว — SocialSystem.cs:179 — ถ้าเราส่ง null ไปทับจะพัง)
        _connection.Recv(delegate(GetClanNotificationEnabled msg, PacketHeader header)
        {
            // เพิ่งต่อเข้ามา — กู้ค่าจากไฟล์เซฟก่อนตอบ
            if (_clanChannelNotifications == null && _context.ClanChannelNotifications is { Count: > 0 })
            {
                var restored = new Dictionary<ChannelType, bool>();
                foreach (var pair in _context.ClanChannelNotifications)
                {
                    restored[(ChannelType)pair.Key] = pair.Value;
                }
                _clanChannelNotifications = restored;
            }
            Send(new ToggleClanNotification
            {
                ChannelNotificationsEnabled = _clanChannelNotifications ?? new Dictionary<ChannelType, bool>()
            }, header.Seq);
        });

        // ResubscribeClanChannel (24) — สมัครช่องแชทเผ่าใหม่หลังเปลี่ยนเผ่า
        // client/SocialSystem.cs:1334-1343 ClanChanged: หน่วง 10 วินาทีแล้วยิง **ไม่รอคำตอบ**
        // เราไม่มีระบบช่องแชทแยกตามเผ่า (SayInExclusiveChannel ของเรากระจายทั้งโลก —
        // server/Core/Player.cs:438-448) ⇒ รับไว้เฉย ๆ กัน log "ไม่มี handler"
        _connection.Recv(delegate(ResubscribeClanChannel msg, PacketHeader header)
        {
        });
    }
}
