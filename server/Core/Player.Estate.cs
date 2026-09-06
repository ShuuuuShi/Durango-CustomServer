using System;
using Durango.Network;
using Messages;
using Shared.Estate;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  ที่ดิน / สิทธิ์เข้าถึง (Estate) — การประกาศจับจองพื้นที่ + ใครเข้ามาทำอะไรได้บ้าง
//
//  ═══ ระบบนี้คืออะไร ═══
//  ในเกมจริง ผู้เล่นเดินไปยืนบนที่ว่างแล้ว "ประกาศสิทธิ์" เป็นเจ้าของช่องขนาด 4x4 tile
//  (client/EstateSystem.cs:20 EstateGridSize = 4 — ตรงกับ server/Core/World.cs:735)
//  จากนั้นจึงขยาย/หด/ต่ออายุ/รื้อทิ้งได้
//  OwnerType (server/GameCode/Shared.Estate/OwnerType.cs) มี 6 ค่า คือ Invalid(-1) · Player ·
//  System · ClanEstate · ClanWarphole · PersonalPlayer — ที่ผู้เล่นจับจองเองได้มี 4 ค่า:
//    Player = ที่ดินในเมือง · PersonalPlayer = เกาะส่วนตัว · ClanEstate/ClanWarphole = ของแคลน
//    (System = ของเซิร์ฟ ไม่ใช่ของใคร ผู้เล่นประกาศเองไม่ได้)
//  ประโยชน์คือกันคนอื่นมารื้อ/เก็บของในเขตเรา และเปิดปุ่มวาร์ปกลับที่ดินตัวเองได้ทันที
//
//  ═══ ไฟล์นี้ทำหน้าที่อะไร ═══
//  ไฟล์นี้เป็น **ชั้นลงทะเบียน/เดินสายอย่างเดียว** ไม่มีตรรกะของระบบอยู่ในนี้
//  ตัวระบบจริงถูกเขียนไว้ที่อื่นแล้ว แบ่งเป็นสองชั้น:
//    • คลังข้อมูลระดับโลก — server/Core/World.cs:735-920
//      เก็บ Estates/EstateCells ลงไฟล์ .world แล้วมี DeclareEstate(:791) · ExpandEstate ·
//      ShrinkEstate · RemoveEstate(:871) · GetEstate(:741) · ToLicense(:772) ·
//      BuildEstateGridsForChunks(:884)
//    • ตรรกะต่อผู้เล่นหนึ่งคน — server/Core/Player.PersonalRegion.cs
//      HandleDeclareEstate(:293) · HandleExpandEstate(:325) · HandleShrinkEstate(:338) ·
//      HandleRemoveEstate(:351) · HandleSetEstateLicense(:368) · HandleExtendEstate(:387) ·
//      HandleReturnToEstate(:138) · HandleVisitEstate(:179) · HandleSetPersonalRegionAdmission(:219)
//  ⇒ เวลาจะแก้พฤติกรรมของคำสั่งไหน ให้ไปแก้ที่สองไฟล์นั้น ไม่ใช่ไฟล์นี้
//
//  ═══ สถานะจริงของระบบตอนนี้ ═══
//  ที่ดินเป็น "ของจริง" แล้ว ไม่ใช่โครงว่างอีกต่อไป:
//    • ประกาศ/ขยาย/หด/ต่ออายุ แล้วเซิร์ฟตอบ EstateLicense(2430) ของจริงที่บันทึกลงดิสก์
//    • หลังทุกคำสั่งที่เปลี่ยนรูปที่ดิน จะกระจาย EstateGrids ให้ทุกคนในชังก์รอบ ๆ
//      (Player.PersonalRegion.cs:278-291 BroadcastEstateGridsAround → World.BroadCast)
//      ฝั่งเกมรับที่ global On<EstateGrids> (client/EstateSystem.cs:82) แล้ววาดเส้นเขตใหม่
//    • GetEstateLicenses(3821) ตอบด้วย BuildEstateLicenses() ของจริง — server/Core/Player.cs:453-455
//  สิ่งที่ **ยังไม่มี** คือ ระบบเงิน (ต่ออายุจึงยังฟรี ดู Player.PersonalRegion.cs:395 ที่กำกับ
//  **ค่าของเรา** ไว้) · มัดจำ/Deposit · CycleStartsAt/CycleEndsAt · สิทธิ์ระดับ artifact รายชิ้น
//
//  ═══ วิธีตอบที่เลือกใช้ และเหตุผล ═══
//  ฝั่งเกมมีตัวรับ Abort แบบ global อยู่แล้ว (client/GameManager.cs:269 → :309-312
//  DefaultAbortHandler → UIManager.SystemMsg(...) เด้ง toast 4 วินาที ไม่ใช่หน้าต่างโมดัล)
//  และ client/Durango.Network/Connection.cs:868-895 HandleMsg จะตกไปหา handler ตัว global
//  เมื่อชนิดที่ตอบกลับไม่ตรงกับที่ .On<T> ผูกไว้ ⇒ ตอบ Abort ที่ seq เดิมได้ผลสองอย่างพร้อมกัน:
//    ① ปลดล็อกฝั่งเกมจากสถานะ "รอคำตอบ"  ② ผู้เล่นเห็นข้อความว่าทำไมกดแล้วไม่เกิดอะไร
//  ⇒ Abort ทุกตัวต้องมี Text เสมอ ห้าม default(Abort) เพราะ Text จะเป็น null แล้วฝั่งเกมแครช
//    (Abort.Unpack → LocalizeSystem.UnpackGettextFromMsgPack คืน null → LimitText(null).Length)
//
//  ⚠️ แต่ **ไม่ใช่ทุกตัวควรตอบ Abort** — ข้อความที่ฝั่งเกมยิงแบบไม่รอคำตอบ (ไม่ต่อ .On/.Rest)
//     ถ้าตอบ Abort ไป toast จะเด้งใส่หน้าผู้เล่นทั้งที่เขาไม่ได้สั่งอะไรที่เกี่ยวกับที่ดิน
//     (เช่น SetPersonalRegionAdmission ยิงตอน "ปิดหน้าต่าง" · GetEstateLicenseById ยิงเองเป็นรอบ)
//     ⇒ กลุ่มนี้ห้ามตอบ Abort เด็ดขาด
//
//  ═══ ตัวที่จงใจไม่ทำ ═══
//  • EstateLicenses (3821) — **ทิศทางเซิร์ฟ→เกม** ไม่ใช่คำขอ เป็นคำตอบของ GetEstateLicenses
//    (client/EstateSystem.cs:446-453 Send(default(GetEstateLicenses)).On(EstateLicenses))
//    และ server/Core/Player.cs:453-455 ตอบให้อยู่แล้ว ⇒ ห้ามลงทะเบียน Recv ให้มัน
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    // ต่อสายแล้วที่ server/Core/Player.Systems.cs:50 (`RegisterEstateHandlers();`)
    //
    // ตรวจแล้วว่าไม่ชนกับใคร: TypeCode ทั้ง 12 ตัวในไฟล์นี้ (2420 · 2421 · 2422 · 2426 · 2104 ·
    // 3822 · 20423 · 20424 · 879534 · 9518234 · 10190234 · 987123450) ไม่ซ้ำกันเอง และไม่มี
    // handler ตัวอื่นใน server/ จองไว้ก่อน — สำคัญเพราะ Connection.Recv ตัวที่ลงทะเบียน
    // ทีหลังจะ **ทับ** ตัวก่อนหน้าเงียบ ๆ (server/GameCode/Durango.Online/Connection.cs:189-210
    // RegisterMessageHandlerToRegistry — ถ้า key ซ้ำจะ Remove ของเดิมแล้ว Add ตัวใหม่)
    private void RegisterEstateHandlers()
    {
        // ── กลุ่มที่ 1: คำสั่งจัดการที่ดิน — ฝั่งเกม "รอ EstateLicense กลับ" ทุกตัว ──────────
        //
        // ทั้งสี่ตัวล่างนี้ผูก .On(delegate(EstateLicense msg, ...)) ตัวเดียว ไม่มี .Rest
        // ⇒ ไม่ตอบ = ปุ่มค้างเป็น "กำลังส่ง" ไปตลอด
        // ตัว Handle* ใน Player.PersonalRegion.cs ตอบ EstateLicense ของจริงเมื่อทำสำเร็จ
        // และตอบ Abort (มีข้อความเสมอ) เมื่อทำไม่ได้ — ครบทั้งสองทางแล้ว

        // DeclareEstate (2422) — ประกาศจับจองช่องที่ยืนอยู่
        // จุดยิง: client/Durango.UI/EstateGridGroup.cs:581 OnDeclareEstateClick (แตะช่องบนตาราง)
        //         → client/EstateSystem.cs:547-560 DeclareEstate(...).On(EstateLicense)
        // ฟิลด์จริง (server/GameCode/Messages/DeclareEstate.cs:10,12): OwnerType · Cell(Point2)
        // ตรรกะ: Player.PersonalRegion.cs:293 — รองรับ Player/PersonalPlayer, ชนิดอื่นตอบ Abort
        _connection.Recv(delegate(DeclareEstate msg, PacketHeader header)
        {
            HandleDeclareEstate(msg, header.Seq);
        });

        // ExpandEstate (2421) — ขยายที่ดินเดิมออกไปอีกหนึ่งช่อง
        // จุดยิง: client/EstateSystem.cs:494-507 ExpandEstate(...).On(EstateLicense)
        // ฟิลด์จริง (server/GameCode/Messages/ExpandEstate.cs:9,11): EstateId(string) · Cell(Point2)
        // ตรรกะ: Player.PersonalRegion.cs:325 — เพดานขนาดที่ PersonalEstateMaxSize (:240)
        _connection.Recv(delegate(ExpandEstate msg, PacketHeader header)
        {
            HandleExpandEstate(msg, header.Seq);
        });

        // ShrinkEstate (2426) — คืนช่องที่ขยายไว้
        // จุดยิง: client/EstateSystem.cs:509-522 ShrinkEstate(...).On(EstateLicense)
        // ฟิลด์จริง (server/GameCode/Messages/ShrinkEstate.cs:9,11): EstateId · Cell
        // ตรรกะ: Player.PersonalRegion.cs:338
        _connection.Recv(delegate(ShrinkEstate msg, PacketHeader header)
        {
            HandleShrinkEstate(msg, header.Seq);
        });

        // ExtendEstateActivation (3822) — ต่ออายุใบอนุญาตออกไปอีก 7 วัน
        // (client/EstateSystem.cs:18 ExtendDays = 7)
        // จุดยิง: client/EstateSystem.cs:532-545 ExtendEstate(...).On(EstateLicense)
        // ฟิลด์จริง (server/GameCode/Messages/ExtendEstateActivation.cs:9,11): EstateId · Cost(long)
        // ตรรกะ: Player.PersonalRegion.cs:387
        // ⚠️ msg.Cost ยัง **ไม่ถูกหักจริง** เพราะเซิร์ฟยังไม่มีระบบเงิน — ต่ออายุจึงฟรีไปก่อน
        //    (กำกับ **ค่าของเรา** ไว้แล้วที่ Player.PersonalRegion.cs:395)
        _connection.Recv(delegate(ExtendEstateActivation msg, PacketHeader header)
        {
            HandleExtendEstate(msg, header.Seq);
        });

        // ── กลุ่มที่ 2: สิทธิ์เข้าถึง — ฝั่งเกมรอชนิดอื่นที่ไม่ใช่ EstateLicense ──────────────

        // SetEstateLicense (2420) — ตั้งว่าคนนอก/เพื่อน/สมาชิกแคลน ทำอะไรในเขตเราได้บ้าง
        // จุดยิง: client/EstateSystem.cs:479-492 SetEstateLicense(...) **รอ .On<OK> เท่านั้น**
        // ฟิลด์จริง (server/GameCode/Messages/SetEstateLicense.cs:9,11): EstateId · AccessRights
        //   โดย Messages.AccessRights (server/GameCode/Messages/AccessRights.cs:10,12,14) แยกเป็น
        //   ForOthers / ForFriends(map ตาม FriendType) / ForClanMembers(map ตาม role id)
        // ตรรกะ: Player.PersonalRegion.cs:368 — เก็บเฉพาะ ForOthers ลง EstateRecord.AccessForOthers
        //   แล้วตอบ default(OK) (OK ไม่มีฟิลด์ ใช้ default ได้ ต่างจาก Abort ที่ห้าม)
        //   ⚠️ ForFriends/ForClanMembers ยังไม่ถูกเก็บ — ตั้งค่าสองอันนั้นแล้วค่าจะหายรอบหน้า
        _connection.Recv(delegate(SetEstateLicense msg, PacketHeader header)
        {
            HandleSetEstateLicense(msg, header.Seq);
        });

        // SetArtifactAccess (987123450) — ตั้งสิทธิ์รายชิ้น ว่าใครเปิดหีบ/ใช้เตาของเราได้
        // จุดยิง: client/Durango.UI/ArtifactInfoGroup.cs:238-246 → client/EstateSystem.cs:663-676
        //   ผูกด้วย .All(packet => onResult(Packet.IsSuccess(packet)))
        //   Packet.IsSuccess (client/Durango.Network/Packet.cs:90-101) นับ 1022/1024/3650 = ล้มเหลว
        //   ⇒ Abort(1024) ทำให้ result เป็น false ⇒ ฝั่งเกม **ไม่อัปเดตหน้าจอ** ตรงตามความจริง
        // ฟิลด์จริง (server/GameCode/Messages/SetArtifactAccess.cs:9,11,13): EntityId · Tile · Access
        //
        // ตัวนี้ยังเป็นโครงว่างตัวเดียวที่เหลือในไฟล์: สิทธิ์ระดับ artifact รายชิ้นเซิร์ฟยังไม่มี
        // ที่เก็บเลย (grep "ArtifactAccess" ทั้ง server/Core/ ไม่เจอนอกจากไฟล์นี้) และแท็บนี้
        // ฝั่งเกมเปิดได้ก็ต่อเมื่อ artifact.ArtifactState.Access มีค่า
        // (client/Durango.UI/ArtifactInfoGroup.cs:253) ซึ่งเซิร์ฟเราไม่เคยตั้ง ⇒ ปกติจะกดไม่ถึง
        // แต่ยังต้องรับไว้ กันกรณีฝั่งเกมยิงมาจากทางอื่นแล้วค้างรอคำตอบ
        //
        // 💡 วันที่ทำ ArtifactState.Access จริง ต้องกลับมาแก้ตัวนี้ให้ตอบ OK ไม่งั้นระบบใหม่
        //    จะถูก Abort ตัวนี้บล็อกไว้เงียบ ๆ
        _connection.Recv(delegate(SetArtifactAccess msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานการตั้งสิทธิ์ใช้สิ่งปลูกสร้าง" }, header.Seq);
        });

        // ── กลุ่มที่ 3: วาร์ปไปที่ดิน — ต้องตอบตามกติกาหลอดวาร์ป ─────────────────────────────
        //
        // ทั้งสองตัวถูกยิงผ่าน MapSystem.TryWarp (client/MapSystem.cs:477-503) ซึ่งลงเอยที่
        // DoWarp (client/MapSystem.cs:587-613): เริ่มเล่นท่าวาร์ป + หลอดเวลา แล้วผูก
        //     .On<Messages.Timer>(...)   ← ได้ Timer = ตั้งเวลาหลอดใหม่ตาม Duration
        //     .Rest(() => WarpTimer.Stop())  ← ได้ชนิดอื่น = หยุดหลอด
        // ⇒ **ไม่ตอบเลย = ตัวละครยืนเล่นท่าวาร์ปค้างจนหลอด Ping+5 วิ หมดเอง** แบบไม่มีคำอธิบาย
        // ⇒ สำเร็จ: ตอบ Timer ที่ seq แล้วค่อยส่ง Emigrated ตามหลังเมื่อครบเวลา
        //   ล้มเหลว: ตอบ Abort ที่ seq เดิม — .Rest หยุดหลอดทันที + toast บอกเหตุผล
        //   (กติกาเดียวกับที่ server/Core/Player.Warp.cs อธิบายไว้ที่หัวไฟล์ ข้อ ①)

        // VisitEstate (2104) — ไปเยี่ยมที่ดิน/เกาะส่วนตัวของผู้เล่นคนอื่นหรือของแคลน
        // จุดยิง: client/EstateSystem.cs:571-595 VisitEstate(...) ผ่าน TryWarp
        // ฟิลด์จริง (server/GameCode/Messages/VisitEstate.cs:11,13,15,17):
        //   OwnerId · RegionId(nullable) · OwnerType · Cost(Money? — ค่าเดินทาง)
        // ตรรกะ: Player.PersonalRegion.cs:179 — รองรับเฉพาะ OwnerType.PersonalPlayer ที่มี
        //   RegionId ขึ้นต้น "personal_" ชนิดอื่น (รวม ClanWarphole) ตอบ Abort
        // ⚠️ msg.Cost ยังไม่ถูกหัก — เหตุผลเดียวกับ ExtendEstateActivation
        _connection.Recv(delegate(VisitEstate msg, PacketHeader header)
        {
            HandleVisitEstate(msg, header.Seq);
        });

        // ReturnToEstate (10190234) — วาร์ปกลับที่ดินของตัวเอง (ปุ่มบนแผนที่/หน้าที่ดิน)
        // จุดยิง: client/EstateSystem.cs:597-604 ReturnToEstate(...) ผ่าน TryWarp เช่นกัน
        // ฟิลด์จริง (server/GameCode/Messages/ReturnToEstate.cs:10): OwnerType ตัวเดียว
        //   — ปลายทางไม่ได้มากับข้อความ เซิร์ฟต้องรู้เองว่าที่ดินชนิดนั้นของผู้เล่นอยู่ไหน
        // ตรรกะ: Player.PersonalRegion.cs:138 — รองรับเฉพาะ PersonalPlayer (กลับเกาะส่วนตัว
        //   ตาม _context.PersonalRegionId) ชนิดอื่นตอบ Abort
        //
        // หมายเหตุ: server/Core/Player.Travel.cs:243 เรียก HandleReturnToEstate ตัวเดียวกันนี้
        //   ซ้ำอีกทางหนึ่ง (คนละ TypeCode จึงไม่ทับกัน) — แก้ตรรกะที่เดียวมีผลทั้งสองทาง
        _connection.Recv(delegate(ReturnToEstate msg, PacketHeader header)
        {
            HandleReturnToEstate(msg, header.Seq);
        });

        // ── กลุ่มที่ 4: ยิงแล้วไม่รอคำตอบ — ห้ามตอบ Abort ─────────────────────────────────────
        //
        // สี่ตัวล่างนี้ฝั่งเกม Send() เปล่า ๆ ไม่ต่อ .On/.Rest/.All เลยแม้แต่ตัวเดียว
        // ⇒ ตอบอะไรกลับไปจะตกไปที่ handler ตัว global แทน (client Connection.cs:883-886)
        //   ซึ่งกรณี Abort แปลว่า toast เด้งใส่หน้าผู้เล่นทั้งที่เขาไม่ได้กดอะไรผิด

        // RemoveEstate (9518234) — รื้อที่ดินทิ้ง (คืนพื้นที่)
        // จุดยิง: client/Durango.UI/EstatePage.cs:298 และ client/Durango.UI/EstateGridGroup.cs:129,498
        //         → client/EstateSystem.cs:524-530 Send(new RemoveEstate{...}) **ไม่มี .On ต่อท้าย**
        // ฟิลด์จริง (server/GameCode/Messages/RemoveEstate.cs:9): EstateId ตัวเดียว
        // ตรรกะ: Player.PersonalRegion.cs:351 — ลบออกจากคลังแล้วกระจาย EstateGrids รอบช่องนั้น
        //   ฝั่งเกมรีเฟรชหน้าจอจาก EstateGrids ที่ตามมา (client/EstateSystem.cs:82 OnEstateGrid)
        //   จึงไม่ต้องตอบอะไรที่ seq นี้ — ถูกต้องตามที่ฝั่งเกมออกแบบไว้
        _connection.Recv(delegate(RemoveEstate msg, PacketHeader header)
        {
            HandleRemoveEstate(msg);
        });

        // GetEstateLicenseById (879534) — ขอใบอนุญาตใบเดียวตาม id
        // จุดยิง: client/EstateSystem.cs:279-296 RefershInvalidateEstateInfo — **ฝั่งเกมยิงเอง
        //   เป็นรอบ ๆ** เมื่อใบอนุญาตเดินถึงเวลา CycleEndsAt ไม่ใช่ผู้เล่นกดปุ่ม
        //   และไม่ต่อ .On ⇒ คำตอบวิ่งเข้า global On<EstateLicense> (client/EstateSystem.cs:83
        //   → OnEstateLicenseChanged:383-386 → SetLicense:388-395 ทับค่าใน EstateInfo ใบนั้น)
        // ฟิลด์จริง (server/GameCode/Messages/GetEstateLicenseById.cs:9): EstateId
        //
        // ตอบด้วยใบอนุญาต **ของจริง** จากคลัง (World.GetEstate:741 → World.ToLicense:772)
        // ไม่ใช่ของแต่ง จึงไม่ผิดข้อห้าม "ห้ามแต่งข้อมูลปลอม"
        // ⚠️ หาไม่เจอให้ **เงียบ** ห้ามตอบ Abort — ตัวนี้ยิงซ้ำเป็นรอบ toast จาก
        //    DefaultAbortHandler (client/GameManager.cs:309-312) จะเด้งรัวไม่หยุด
        // หมายเหตุ: ตอนนี้ World.ToLicense ยังไม่ใส่ CycleEndsAt (ยังไม่มีระบบรอบบิล)
        //   ⇒ เงื่อนไข CycleEndsAt.HasValue ที่ EstateSystem.cs:286 ยังไม่เป็นจริง ฝั่งเกมจึง
        //   ยังไม่ยิงตัวนี้ออกมาในทางปฏิบัติ แต่รับให้ถูกต้องไว้ก่อนเผื่อวันที่ใส่ค่านั้น
        _connection.Recv(delegate(GetEstateLicenseById msg, PacketHeader header)
        {
            EstateRecord rec = _world.GetEstate(msg.EstateId);
            if (rec == null)
            {
                return;
            }
            Send(_world.ToLicense(msg.EstateId, rec), header.Seq);
        });

        // KickVisitor (20424) — ไล่คนที่เข้ามาในเขตเราออกไป
        // จุดยิง: client/SocialSystem.cs:1074-1085 Block() — ยิงพ่วงไปกับการ "บล็อกผู้เล่น"
        //   โดย Send(new KickVisitor{...}) **ไม่มี .On** ส่วนคำตอบที่ฝั่งเกมรอจริงคือของ
        //   Block(...) ที่ยิงไปทาง Connections.Radiotower คนละสายกัน
        // ฟิลด์จริง (server/GameCode/Messages/KickVisitor.cs:9,11): EntityId · Silent(bool)
        //
        // ⚠️ ห้ามตอบ Abort — ผู้เล่นแค่กด "บล็อก" ถ้ามี toast เรื่องที่ดินเด้งขึ้นมาจะงงหนัก
        // เซิร์ฟนี้ยังไม่มีการติดตามว่าใครยืนอยู่ในเขตใคร ⇒ ยังไม่มีใครให้ไล่ = ไม่ต้องทำอะไร
        _connection.Recv(delegate(KickVisitor msg, PacketHeader header)
        {
        });

        // SetPersonalRegionAdmission (20423) — ตั้งว่ากลุ่มไหนเข้าเกาะส่วนตัวเราได้บ้าง
        // จุดยิง: client/Durango.UI.Popup/PersonalRegionAdmissionPopup.cs:133-146 OnHide()
        //   คือยิงตอน **ปิดหน้าต่าง** ถ้ามีการเปลี่ยนค่า — ไม่มี .On รอคำตอบ
        // ฟิลด์จริง (server/GameCode/Messages/SetPersonalRegionAdmission.cs:10):
        //   AdmissionCategories (LicenseCategory[] — ตาม server/GameCode/Shared.Estate/
        //    LicenseCategory.cs; null = ปิดรับทุกคน ตาม PersonalRegionAdmissionPopup.cs:140
        //    ที่ส่ง null เมื่อสวิตช์หลักถูกปิด)
        // หมายเหตุ: ฝั่ง Pack แปลง null เป็น array ว่าง (SetPersonalRegionAdmission.cs:23-26)
        //   และ Unpack ฝั่งเราจองอาร์เรย์เสมอ ⇒ msg.AdmissionCategories ไม่เคยเป็น null จริง
        //   แต่ตัวรับที่ Player.PersonalRegion.cs:225 ยังกัน null ไว้เผื่อ push ทางอื่น
        // ตรรกะ: Player.PersonalRegion.cs:219 — เก็บลง _context.PersonalRegionAdmission
        //   แล้ว Save() ลงเซฟผู้เล่นจริง (ค่าไม่หายตอนเข้าเกมใหม่)
        //
        // ⚠️ ห้ามตอบ Abort — จะเด้ง toast ทุกครั้งที่ผู้เล่นปิดหน้าต่างนี้
        _connection.Recv(delegate(SetPersonalRegionAdmission msg, PacketHeader header)
        {
            HandleSetPersonalRegionAdmission(msg);
        });
    }
}
