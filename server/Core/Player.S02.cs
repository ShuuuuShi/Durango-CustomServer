using System;
using Durango.Network;
using Messages;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  เกาะบทเรียน (แพหนีตาย) + เกาะ PvP / Warp Rush — ซีซัน 2 (S02)
//
//  ═══ ระบบนี้คืออะไร ═══
//  1) เกาะบทเรียน: ผู้เล่นใหม่ช่วยกันลงแรงต่อ "แพหนีตาย" (tutorial boat) แล้วออกเรือจากเกาะ
//     ตัวแพเป็น Artifact ที่ **เซิร์ฟเป็นฝ่ายวางลงฉากให้** ด้วย AppearTutorialBoat
//     พร้อมสถานะกลุ่ม TutorialBoatSessions — ฝั่งเกมรับสี่ตัวนี้แบบ global
//     (client/TutorialIslandSystem.cs:67-70)
//  2) เกาะ PvP: ลงคิวจับคู่ (entree queue) → ครบคนแล้วยิงเข้าสนามรบแบบเหลือรอดคนสุดท้าย
//     เมนูเปิดให้เมื่อเลเวล ≥ season2.entree_level_limit = 50
//     (client/Durango.Logic/WarpRushSystem.cs:203-212 อ่านค่าจาก Yaml Season2.EntreeLevelLimit —
//      client/Yaml/Season2.cs:31-32 · ค่าจริง server/data/assets/constants.json:1244)
//
//  ═══ ทำไมเซิร์ฟนี้ทำของจริงไม่ได้ ═══
//  · ไม่มีแพบทเรียนอยู่ในโลกเลย: `grep -rln "AppearTutorialBoat" server/` เจอเฉพาะ
//    server/GameCode/Messages/AppearTutorialBoat.cs (ตัวโปรโตคอล) — ไม่มีจุดไหนในเซิร์ฟส่งมันออกไป
//    เช่นเดียวกับ TutorialBoatSessions ⇒ ฝั่งเกมไม่เคยมี _tutorialBoat และไม่เคยมี session
//  · ไม่มีคิวจับคู่ / สนาม PvP / ตารางอันดับซีซัน 2 อยู่ในเซิร์ฟเลย
//
//  ═══ ทำไมยังต้องลงทะเบียนให้ครบ ═══
//  ปล่อยเงียบไม่ได้ เพราะฝั่งเกมล็อกปุ่มไว้ระหว่างรอคำตอบ:
//    · WarpRushSystem.cs:245-262 ตั้ง `_isRequesting = true` แล้วปลดใน `.All(...)` เท่านั้น
//      ไม่ตอบอะไรเลย = ปุ่ม "ลงทะเบียนเข้าเกาะ" ตายถาวรจนกว่าจะออกเกม
//    · PvpIslandResultGroup.cs:74-84 / WarpRushResultGroup.cs:54-64 ปิดปุ่ม "ออก" + ใส่วงโหลด
//      แล้วกู้คืนใน `.On<Error>` เท่านั้น
//  ⇒ ตอบ "ปฏิเสธพร้อมข้อความ" ทุกตัวที่เป็นการกระทำ · ตอบ "โครงว่างถูกชนิด" ตัวที่เป็นการถาม
//  ⇒ ห้ามตอบ OK / ห้ามแต่งอันดับ-จำนวนคนในคิว เพราะจะทำให้ผู้เล่นรอสิ่งที่ไม่มีวันมา
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    /// <summary>
    /// ข้อความปฏิเสธร่วมของกลุ่ม S02
    /// ⚠️ ทุก Abort/Error ต้องมี Text เสมอ — `default(Abort)` ทำให้ Text เป็น null แล้วฝั่งเกมแครช
    /// ที่ client/GameManager.cs:348-351 DefaultAbortHandler → LimitText(null) (:329-336) → NRE
    /// </summary>
    private const string S02NotAvailableText = "เซิร์ฟนี้ยังไม่เปิดใช้งานระบบเกาะ PvP / แพบทเรียน";

    private void RegisterS02Handlers()
    {
        // ── เกาะบทเรียน: แพหนีตาย ────────────────────────────────────────────────

        // ParticipateTutorialBoat (2303) — "ขอเข้าร่วมกลุ่มต่อแพ" · ฟิลด์ {EntityId, Tile}
        // (server/GameCode/Messages/ParticipateTutorialBoat.cs:7-11)
        // ยิงจาก client/TutorialIslandSystem.cs:256-273 (InteractionSystem_PreTouchTarget →
        // SendParticipateTutorialBoat) แบบ **ยิงแล้วไม่ผูกรอ** (Send(...) เปล่า ๆ ไม่มี .On)
        // คำตอบจริงเดินทางกลับเป็น push TutorialBoatSessions ที่ฝั่งเกมรับด้วย global On
        // (TutorialIslandSystem.cs:68) — เซิร์ฟเราไม่มีตัวแพและไม่มีตารางกลุ่ม (session)
        //
        // ในทางปฏิบัติยิงมาไม่ถึงอยู่แล้ว: PreTouchTarget จะถูกต่อสายก็ต่อเมื่อ _tutorialBoat != null
        // หรือ _hasSession (TutorialIslandSystem.cs:247-254) ซึ่งเกิดได้จาก AppearTutorialBoat /
        // TutorialBoatSessions เท่านั้น — เราไม่เคยส่งทั้งคู่ ⇒ ลงทะเบียนไว้กัน log "ไม่มี handler"
        // และเผื่อวันหน้ามีคนต่อแพของจริง
        // Abort ไม่มี .On ผูกไว้ ⇒ ตกไปที่ตัวรับกลาง GameManager.cs:308 → DefaultAbortHandler
        // (:348-351) แสดง SystemMsg ให้ผู้เล่นเห็นเหตุผล
        _connection.Recv(delegate(ParticipateTutorialBoat msg, PacketHeader header)
        {
            Send(new Abort { Text = S02NotAvailableText + " — ยังเข้าร่วมต่อแพไม่ได้" }, header.Seq);
        });

        // PutMaterialsIntoTutorialBoat (2304) — "ใส่วัสดุลงแพ"
        // ฟิลด์ {EntityId, Tile, Materials: Dictionary<string,string[]>}
        // (server/GameCode/Messages/PutMaterialsIntoTutorialBoat.cs:8-14)
        // ยิงจาก client/TutorialIslandSystem.cs:275-291 (SendPutTutorialBoatMaterials — ปุ่มยืนยัน
        // ในหน้าคราฟต์ที่เปิดจาก Interaction.BuildTutorialBoat = 10101,
        // client/InteractionData/Interaction.cs:364) แบบไม่ผูกรอเช่นกัน
        //
        // ของจริงเซิร์ฟต้องหักของในกระเป๋าแล้ว push TutorialBoatMaterialUpdated +
        // TutorialBoatSessions กลับไป (ตัวรับที่ TutorialIslandSystem.cs:208-230)
        // เราไม่หักของอะไรเลย ⇒ กระเป๋าไม่เพี้ยน: ฝั่งเกมไม่ได้ลบของล่วงหน้า —
        // CreateFirstMaterialsDictionary แค่ประกอบรายการส่ง (TutorialIslandSystem.cs:281)
        // ⇒ ปฏิเสธอย่างเดียวพอ ไม่ต้องคืนของ
        _connection.Recv(delegate(PutMaterialsIntoTutorialBoat msg, PacketHeader header)
        {
            Send(new Abort { Text = S02NotAvailableText + " — ยังใส่วัสดุลงแพไม่ได้" }, header.Seq);
        });

        // DepartTutorial (2306) — "ออกเรือจากเกาะบทเรียน" · ฟิลด์ {EntityId, Tile}
        // (server/GameCode/Messages/DepartTutorial.cs:7-11)
        // ยิงจาก 2 ที่ (ทั้งคู่ผ่าน TutorialIslandSystem.cs:293-300 SendDepartTutorial ไม่ผูกรอ):
        //   ก) Interaction.DepartTutorial = 10103 (client/InteractionData/Interaction.cs:370)
        //      → TutorialIslandSystem.cs:82-89 — เซิร์ฟต้องแจก interaction นี้เอง วันนี้ไม่แจก
        //   ข) client/Durango.UI/EstateGroup.cs:124-139 AirBalloonLeaving — ขึ้นบอลลูนแล้ว
        //      **หน่วง 5 วินาทีด้วย KUtility.DelayedCall แล้วยิงเสมอ** (เข้าจาก :84 หรือ :108)
        //      ⚠️ ทางนี้ยิงถึงเราจริง เพราะไม่ได้รอคำตอบของ MountAirBalloon
        //      (server/Core/Player.Vehicle.cs ตอบ Abort ให้ MountAirBalloon แต่ตัวจับเวลายังเดินต่อ)
        //
        // ⚠️ ห้ามตอบ DepartTutorialReady (2305) เด็ดขาด — ฝั่งเกมจะเล่นฉาก "depart_tutorial_flow"
        //    ปิดจอด้วยม่านดำแล้วรอจนทึบ (TutorialIslandSystem.cs:189-206
        //    OnDepartTutorialReady → CoFadeAndSendDepartTutorialFor) แล้วยิง DepartTutorialFor
        //    (2307) ต่อ ซึ่งเซิร์ฟนี้ **ไม่มี handler รับ**
        //    (`grep -rln "DepartTutorialFor" server/` เจอเฉพาะไฟล์โปรโตคอล) ⇒ ผู้เล่นค้างหลังม่านดำถาวร
        // ⇒ ปฏิเสธพร้อมข้อความ ปลอดภัยกว่ามาก
        _connection.Recv(delegate(DepartTutorial msg, PacketHeader header)
        {
            Console.WriteLine($"[S02] {EntityId[..Math.Min(8, EntityId.Length)]} ขอออกเรือบทเรียน — ปฏิเสธ (ไม่มีระบบ)");
            Send(new Abort { Text = S02NotAvailableText + " — ยังออกเรือจากเกาะบทเรียนไม่ได้" }, header.Seq);
        });

        // ── เกาะ PvP / Warp Rush ─────────────────────────────────────────────────

        // S02GetLobbyInfo (222214) — ถามสถิติของตัวเองในล็อบบี้เกาะ PvP (struct ว่าง ไม่มีฟิลด์)
        // (server/GameCode/Messages/S02GetLobbyInfo.cs:9)
        // ยิงจาก client/Durango.Logic/WarpRushSystem.cs:279-282 (RequestLobbyInfo) แบบไม่ผูกรอ
        // เรียกทุกครั้งที่เปิดแผงเกาะ PvP (client/Durango.UI/PvpIslandGroup.cs:68-72 OnOpenSucceed)
        // ฝั่งเกมรับด้วย **global On<S02LobbyInfo> (222215)** (WarpRushSystem.cs:147-153) → ยิง event
        // LobbyInfoUpdated ไปวาดแผงคะแนนที่ PvpIslandGroup.cs:54-66
        // ⇒ ตอบแบบ ReplyOf=0 (Send ตัวไม่ผูก seq) เหมือน GetSupportRequests ใน Player.Social.cs:60-63
        //
        // S02LobbyInfo มี 4 ฟิลด์เป็น Pair<int,float>? ทั้งหมด (อันดับ, ค่าสถิติ)
        // — server/GameCode/Messages/S02LobbyInfo.cs:9-15
        // ปล่อย null ทุกช่อง = ฝั่งเกมโชว์ " - " ตามที่มันเตรียมไว้เอง
        // (PvpIslandGroup.SetScore :76-86 — บรรทัด :78-79 `(!info.HasValue) ? " - "`)
        // ⇒ แผงเปิดได้ ไม่ค้าง และไม่ได้แต่งอันดับปลอมให้ใคร
        _connection.Recv(delegate(S02GetLobbyInfo msg, PacketHeader header)
        {
            Send(new S02LobbyInfo
            {
                WinRank = null,
                PlayRank = null,
                KillRank = null,
                AverageKillRank = null
            });
        });

        // S02EnqueueEntree (222201) — "ลงคิวเข้าเกาะ PvP" (struct ว่าง)
        // (server/GameCode/Messages/S02EnqueueEntree.cs:9)
        // client/Durango.Logic/WarpRushSystem.cs:245-262 (EnqueueWarpRushEntry):
        //   `.On<OK>` → ตั้ง IsInEntreeQueue = true  ·  `.All(...)` → ปลด _isRequesting
        //
        // ⚠️ ห้ามตอบ OK — IsInEntreeQueue = true จะไปเพิ่ม EntryTodoCollection เข้ารายการสิ่งที่ต้องทำ
        //    (WarpRushSystem.cs:213-227) แล้วผู้เล่นเฝ้ารอคิวที่ไม่มีวันออกเดินทาง
        //    (แถบเวลากินค่าจาก push S02EntreeInfo ซึ่งเราไม่มีให้ ⇒ ค้างตลอด)
        // ⚠️ ห้ามเงียบ — _isRequesting จะค้าง true (WarpRushSystem.cs:247-250 กันซ้ำ)
        //    แล้วปุ่มลงคิวกดไม่ได้อีกเลยทั้งเซสชัน
        // ⇒ Abort: `.All(...)` ยังทำงาน เพราะมันลงทะเบียนด้วย allowReplied = true
        //   (client/Durango.Network/ReplyMessageHandlerRegistrar.cs:28-35 → Connection.cs:433-439)
        //   แล้ว Connection.cs:893 ปล่อยผ่านเมื่อ AllowReplied ⇒ ปุ่มกลับมากดได้
        //   และตัวรับ Abort กลาง (GameManager.cs:308 → :348-351) แสดงเหตุผลให้ผู้เล่นเห็น
        _connection.Recv(delegate(S02EnqueueEntree msg, PacketHeader header)
        {
            Send(new Abort { Text = S02NotAvailableText + " — ยังลงคิวเข้าเกาะ PvP ไม่ได้" }, header.Seq);
        });

        // S02DequeueEntree (222202) — "ยกเลิกคิวเข้าเกาะ PvP" (struct ว่าง)
        // (server/GameCode/Messages/S02DequeueEntree.cs:9)
        // client/Durango.Logic/WarpRushSystem.cs:264-277 (DequeueWarpRushEntry) — โครงเดียวกับ
        // ตัวลงคิว (`.On<OK>` → IsInEntreeQueue = false · `.All` → ปลด _isRequesting)
        // ในทางปฏิบัติกดไม่ถึงอยู่แล้ว เพราะปุ่มจะสลับเป็น "ยกเลิก" ต่อเมื่อ IsInEntreeQueue = true
        // ซึ่งเกิดได้จาก OK ของตัวลงคิว หรือ push S02EntreeInfo (WarpRushSystem.cs:134-142) เท่านั้น
        // — เราไม่ส่งทั้งคู่ ⇒ ปฏิเสธพร้อมข้อความเหมือนกัน (ยังไงก็ปลด _isRequesting ให้ปุ่มไม่ตาย)
        _connection.Recv(delegate(S02DequeueEntree msg, PacketHeader header)
        {
            Send(new Abort { Text = S02NotAvailableText + " — ยังยกเลิกคิวเข้าเกาะ PvP ไม่ได้" }, header.Seq);
        });

        // S02PVPRefresh (222207) — "ขอสถานะสนามใหม่" (เหลือผู้รอดกี่คน) (struct ว่าง)
        // (server/GameCode/Messages/S02PVPRefresh.cs:9)
        // ยิงจาก client/Durango.Logic/PvpIslandSystem.cs:49-52 แบบไม่ผูกรอ และจะยิงต่อเมื่อได้
        // push S02PVPAnnounceLeave มาทาง **Connections.Radiotower** เท่านั้น — เซิร์ฟนี้ไม่เคยส่ง
        // ⇒ ในทางปฏิบัติไม่มีวันมาถึง ลงทะเบียนไว้กัน log "ไม่มี handler" เฉย ๆ
        //
        // ⚠️ ห้ามตอบ S02PVPStatus (222206) — RemainSurvivorCount ตัวแรกที่ได้จะถูกล็อกเป็น
        //    TotalPlayerCount ถาวร (PvpIslandSystem.cs:53-59: TotalPlayerCount ตั้งครั้งเดียวเมื่อ < 0)
        //    = แต่งจำนวนผู้เล่นปลอมค้างไว้ในเกมทั้งเซสชัน ⇒ ว่างไว้ดีกว่า ตามกฎ "ห้ามแต่งข้อมูลปลอม"
        // ⚠️ ห้ามตอบ Abort ด้วย — ไม่มีใครรอ แต่จะไปเด้ง SystemMsg ใส่หน้าผู้เล่นโดยไม่มีเหตุ
        _connection.Recv(delegate(S02PVPRefresh msg, PacketHeader header)
        {
        });

        // S02Leave (222221) — "ออกจากเกาะ PvP / กลับเกาะตัวเอง" (struct ว่าง)
        // (server/GameCode/Messages/S02Leave.cs:9)
        // ยิงจาก 3 ที่:
        //   · client/Durango.Logic/PvpIslandSystem.cs:92-98 (ExitWithDelay — หน่วง 25 วิหลังตาย/ชนะ)
        //     ทางนี้ **ไม่ผูกรอ** — และมาถึงได้เฉพาะเมื่อเราส่ง S02PVPDead/S02PVPFinish ซึ่งไม่เคยส่ง
        //   · client/Durango.UI/PvpIslandResultGroup.cs:74-84  (ปุ่ม "ออก" ในหน้าสรุปผล)
        //   · client/Durango.UI/WarpRushResultGroup.cs:54-64   (ปุ่ม "ออก" ในหน้าสรุปผล Warp Rush)
        //
        // ⚠️ สองหน้าสรุปผลต้องตอบ **Error (1022)** ไม่ใช่ Abort (1024) — ทั้งคู่ปิดปุ่ม + ติดวงโหลด
        //    ทันทีหลังส่ง แล้วกู้คืนใน `.On<Error>` ที่เดียว (ไม่มี .All / .Rest)
        //    ตอบ Abort = ตัวรับต่อ-seq ไม่มีของ typeCode 1024 ⇒ วงโหลดหมุนค้างตลอดไป กดออกไม่ได้
        //    ส่วน Error จะเข้า `.On<Error>` แล้ว **ยังวิ่งเข้า global handler ซ้ำอีกรอบ** ด้วย เพราะ
        //    client/Durango.Network/Connection.cs:898-901 เขียนเคสพิเศษให้ typeCode 1022 โดยเฉพาะ
        //    ⇒ ผู้เล่นได้เห็นเหตุผลผ่าน GameManager.cs:306 → DefaultErrorHandler (:338-346) ด้วย
        // TypeName ปล่อยไว้ null ได้ — Error.Pack แปลง null เป็นสตริงว่างให้ (Error.cs:24-31)
        // แต่ Text ไม่มีตัวกัน (Error.cs:32 PackString(val.Text) ตรง ๆ) ⇒ ต้องใส่เสมอ
        _connection.Recv(delegate(S02Leave msg, PacketHeader header)
        {
            Send(new Error { Text = S02NotAvailableText + " — ไม่ได้อยู่ในเกาะ PvP" }, header.Seq);
        });
    }
}
