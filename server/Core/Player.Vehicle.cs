using Durango.Network;
using Messages;
using Shared.Display;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  พาหนะสิ่งปลูกสร้าง (เครื่องยิงหิน) · บอลลูน · เครื่องเร่งวาร์ป — [6 ก.ย. 2026]
//
//  สามระบบนี้คนละเรื่องกันแต่ถูกจับรวมไว้ไฟล์เดียว เพราะทั้งหมด "ขี่อยู่บนของที่เซิร์ฟ
//  ยังไม่ได้สร้างสถานะให้" ⇒ เหตุผลที่ตอบแบบเดียวกันหมดจึงเป็นเหตุผลเดียวกัน
//
//  ═══ ทำไมทุกตัวในไฟล์นี้ตอบ Abort แทนที่จะทำจริง ═══
//
//  ①  **เครื่องยิงหิน (catapult)** — ฝั่งเกมอ่านค่าทุกอย่างจาก
//      <c>artifact.ArtifactState.Catapult</c> (server/GameCode/Messages/ArtifactState.cs:52
//      — ชนิด <c>CatapultState?</c> มี Atk / AtkRangeMin / AtkRangeMax / DmgRadius /
//      Cooltime / RemainedProjectilesSize / MaxProjectilesSize)
//      แต่ทั้ง Core/ArtifactManager.cs และ Core/WorldContext.cs **ไม่มีที่ไหนเขียนค่านี้เลย**
//      (grep "Catapult" ใน server/Core/ ได้ 0 บรรทัด) ⇒ ค่าเป็น null ตลอด
//      ผลถ้าเราปล่อยให้ขึ้นขี่จริง:
//        · client/VehicleCatapult.cs:198-203 GetCatapultState() → Debug.LogError แล้วคืน
//          default(CatapultState) ⇒ ระยะยิง 0 · กระสุน 0 · พลังโจมตี 0
//        · client/Durango.UI/CatapultStateWidget.cs:63-66 โชว์แผงสถานะที่เป็นศูนย์ทั้งแผง
//        · client/Durango.UI/CombatGroup.cs:378 กดยิงแล้วเงียบ (else-if ไม่เข้า)
//      ⇒ ผู้เล่นนั่งอยู่บนเครื่องยิงหินที่ยิงไม่ออกและอ่านค่าไม่ได้ = แย่กว่าไม่ให้ขึ้น
//
//  ②  **บอลลูน** — เป็นระบบ "ออกจากเกาะ" ไม่ใช่แค่ท่าขี่ ต้องมีครบสามอย่างที่เซิร์ฟยังไม่มี:
//      หักค่าตั๋ว (costs.json → balloon_ticket · ฝั่งเกมเช็คแค่ว่า "จ่ายไหว" ไม่ได้หักเอง —
//      client/PetManager.cs:515-544 Interaction.RideBalloon), ปลายทางเกาะใหม่, และตัวจับเวลา
//      บังคับลงเมื่อครบ 15 นาที (ข้อความเตือนในกล่องยืนยันบอกไว้เอง)
//      ทั้งสามอย่างไม่มีในเซิร์ฟ (ไม่มี handler RecommendPersonalRegion / DepartTutorial เลย)
//
//  ③  **เครื่องเร่งวาร์ป (warp accelerator)** — สถานะอยู่ที่
//      <c>ArtifactState.Warpaccelerator</c> (ArtifactState.cs:64) ซึ่งเซิร์ฟก็ไม่เคยเขียน
//      และเซิร์ฟยังไม่มี handler <c>GetWarpAcceleratorCost</c> ด้วย ⇒ กล่องยืนยันค่าเข้าร่วม
//      (client/Durango.Logic.Interactions/ArtifactInteractions.cs:1262-1268 DoWarpAccelerate
//       ยิง GetWarpAcceleratorCost แล้วรอ .On<Cost> ก่อนถึงจะเปิดกล่อง) ไม่มีวันเปิด
//      ⇒ รางวัลก็ไม่มีของจริง และกฎโปรเจกต์ห้ามแต่งรางวัลปลอม
//
//  ═══ แล้วทำไมยังต้องลงทะเบียนทั้งที่ฝั่งเกมแทบยิงไม่ถึง ═══
//
//  interaction สามตัว (MountVehicle=910 · ParticipateAcceleration=672 ·
//  ReceiveAccelerationRewards=671 — server/GameCode/Shared.System/Interaction.cs:111,112,124)
//  เซิร์ฟเป็นคนแจกให้ผ่าน <c>Touched.Interactions</c> ซึ่ง Core/Player.cs HandleTouchMsg
//  ยังไม่แจก ⇒ วันนี้เมนูไม่โผล่ ยิงไม่ถึงเรา
//  แต่ <c>MountAirBalloon</c> (10262) กับ <c>DismountAirBalloon</c> เป็น interaction
//  **ฝั่งเกมสร้างเอง** จาก client/VehicleAirBalloon.cs:308-322 ContextActionFinder
//  (ไม่ผ่านเซิร์ฟเลย) ⇒ ถ้ามีบอลลูนโผล่ในฉากเมื่อไหร่ ข้อความ 123987/135867 มาถึงเราแน่
//  ⇒ ต้องมี handler ไว้ ไม่งั้นได้ log "ไม่มี handler" แล้วผู้เล่นกดค้างโดยไม่รู้เหตุ
//
//  ═══ ทางปลดล็อกเมื่อแก้ไฟล์นอกขอบเขตได้แล้ว ═══
//    (1) ArtifactManager: เพิ่ม API เขียน ArtifactState.Catapult / .Warpaccelerator
//        + ช่องเซฟใน WorldContext
//    (2) Player.cs HandleTouchMsg: แจก Interaction.MountVehicle เมื่อ blueprint มี component
//        "Catapult" (data/assets/entity_types/artifact.json id 7061 → components ["Collidable",
//        "Catapult"]) และแจก ParticipateAcceleration/ReceiveAccelerationRewards เมื่อมี
//        component "WarpAccelerator" (artifact.json id 6282 → components ["WarpAccelerator"])
//    (3) ค่อยแทน handler ในไฟล์นี้ทีละตัวด้วยของจริง
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    private void RegisterVehicleHandlers()
    {
        // ข้อความ Abort ต้องมี Text เสมอ — default(Abort) ทำให้ Text เป็น null แล้วฝั่งเกมแครช
        // ที่ client/GameManager.cs:309-312 DefaultAbortHandler → LimitText(null).Length
        const string catapultMsg = "ยังไม่เปิดใช้งานเครื่องยิงหิน";
        const string balloonMsg = "ยังไม่เปิดใช้งานบอลลูน";
        const string acceleratorMsg = "ยังไม่เปิดใช้งานเครื่องเร่งวาร์ป";

        // ── MountVehicle (327918) — ขึ้นขี่พาหนะที่เป็นสิ่งปลูกสร้าง (ตอนนี้มีแค่เครื่องยิงหิน) ──
        // จุดยิง: client/PetManager.cs:579-591 Interaction.MountVehicle → ส่ง {EntityId, Tile}
        //         ของ artifact แล้ว **ไม่ผูกรอคำตอบ** (ไม่มี .On)
        // ทางที่ถูกต้องคือเซิร์ฟ broadcast PlayerDisplay(2431) ที่ BoardingOn=Vehicle +
        // VehicleEntityId=artifact ⇒ client/PlayerManager.cs:377 SetDisplay(handleBoarding:true)
        // → client/PlayerBehavior.cs:1760-1763 ตั้ง _reservedMountTargetId แล้ววนหา vehicle
        // ทุกครึ่งวินาทีจนเจอ (CoReservedMountTarget :1809-1824 — **ไม่มีเงื่อนไขเลิกวน**)
        // ⇒ ตอบ Abort ตามเหตุผล ① ที่หัวไฟล์ ดีกว่าปล่อยให้ขึ้นไปนั่งบนของที่ค่าเป็นศูนย์หมด
        _connection.Recv(delegate(MountVehicle msg, PacketHeader header)
        {
            Send(new Abort { Text = catapultMsg }, header.Seq);
        });

        // ── UnmountVehicle (192834) — ลงจากพาหนะสิ่งปลูกสร้าง ──────────────────────────
        // จุดยิง: client/PetManager.cs:976-991 UnmountVehicle() เรียกจากปุ่มย้อนกลับตอนอยู่โหมด
        //         ขี่ (client/Durango.UI/CombatGroup.cs:486-488 BattleViewMode.Mount)
        // ⚠️ ฝั่งเกม **ไม่ได้ลงเองทันที** มันรอเซิร์ฟ push PlayerDisplay ที่ BoardingOn=None
        //    ⇒ ถ้าตอบ Abort ที่นี่ = ผู้เล่นติดอยู่บนพาหนะถาวร ห้ามตอบ Abort เด็ดขาด
        // วันนี้เราไม่เคยตั้ง BoardingOn=Vehicle (MountVehicle ข้างบนตอบ Abort) ⇒ ไม่มีอะไรให้ล้าง
        // แต่ยังเช็คแล้วล้างให้จริง เผื่อมีคนต่อ MountVehicle ของจริงทีหลัง — และ **ต้องเช็คก่อน**
        // เพราะถ้า broadcast None พร่ำเพรื่อตอนผู้เล่นขี่สัตว์อยู่ ฝั่งเกมจะ Driver.Unmount()
        // ทิ้ง (client/PlayerBehavior.cs:1765-1770) = เด้งลงจากหลังสัตว์เอง
        _connection.Recv(delegate(UnmountVehicle msg, PacketHeader header)
        {
            if (_context.AppearPlayer.Display.BoardingOn != BoardingOn.Vehicle) return;
            _context.AppearPlayer.Display.BoardingOn = BoardingOn.None;
            _context.AppearPlayer.Display.VehicleEntityId = string.Empty;
            _world.BroadCast(_context.AppearPlayer.Display);
        });

        // ── MountAirBalloon (123987) — ขึ้นบอลลูน ──────────────────────────────────────
        // ฟิลด์เดียว: WithVoucher (bool?) = จ่ายด้วยคูปองหรือเงิน
        // จุดยิงสามทาง ทั้งหมด **ไม่ผูกรอคำตอบ**:
        //   ก) client/PetManager.cs:515-544 Interaction.RideBalloon — เช็คว่าจ่ายไหวแล้วส่ง
        //      {WithVoucher=byVoucher} · รอเซิร์ฟหักค่าตั๋วแล้ว push BoardingOn=AirBalloon
        //      (client/PlayerBehavior.cs:1754-1758 → MountAirBalloon() เสกบอลลูนให้เอง)
        //   ข) client/PetManager.cs:546-556 Interaction.MountAirBalloon — ฝั่งเกมขึ้นเองก่อน
        //      แล้วค่อยส่ง default(MountAirBalloon) มาบอก (WithVoucher=null)
        //   ค) client/Durango.UI/EstateGroup.cs:124-138 AirBalloonLeaving — ขึ้นเองแล้วบอก
        //      เหมือนกัน แล้ววินาทีที่ 5 ยิง SendDepartTutorial ต่อ
        // ⇒ ตอบ Abort ตามเหตุผล ② ที่หัวไฟล์ (ทาง ข/ค ผู้เล่นจะเห็นข้อความขณะลอยอยู่บนบอลลูน
        //   ที่ฝั่งเกมเสกเอง — ซึ่งตรงความจริงว่ามันไปไหนไม่ได้ ดีกว่าเงียบให้รอเก้อ)
        _connection.Recv(delegate(MountAirBalloon msg, PacketHeader header)
        {
            Send(new Abort { Text = balloonMsg }, header.Seq);
        });

        // ── UnmountAirBalloon (135867) — ลงจากบอลลูน (struct ว่าง ไม่มีฟิลด์) ───────────
        // ข้อความนี้เดินสองทาง:
        //   เกม→เซิร์ฟ  client/PetManager.cs:993-1002 UnmountAirbaloon() — ฝั่งเกมลงเองแล้ว
        //               (ReserveUnmount) ค่อยส่งมาบอก ⇒ เราแค่จดสถานะ **ห้ามตอบ Abort**
        //   เซิร์ฟ→เกม  client/PetManager.cs:79-86 On<UnmountAirBalloon> global — ใช้บังคับลง
        //               (เช่นครบ 15 นาที) เราไม่มีตัวจับเวลานั้น จึงไม่ push ตัวนี้ออกไป
        // ห้าม echo ข้อความเดิมกลับ: จะไปกระตุ้น global handler ข้างบนซ้ำโดยไม่จำเป็น
        // เช็ค BoardingOn ก่อนล้างด้วยเหตุผลเดียวกับ UnmountVehicle
        _connection.Recv(delegate(UnmountAirBalloon msg, PacketHeader header)
        {
            if (_context.AppearPlayer.Display.BoardingOn != BoardingOn.AirBalloon) return;
            _context.AppearPlayer.Display.BoardingOn = BoardingOn.None;
            _context.AppearPlayer.Display.VehicleEntityId = string.Empty;
            _world.BroadCast(_context.AppearPlayer.Display);
        });

        // ── FireProjectileFromVehicle (203493) — ยิงหินจากเครื่องยิงหิน ────────────────
        // จุดยิง: client/Durango.UI/CombatGroup.cs:355-403 TouchScreenInMount()
        //   ก่อนส่ง ฝั่งเกมกรองเองสามชั้น: มี Catapult component (:368) · พ้นคูลดาวน์จาก
        //   VehicleProjectileFired ตัวก่อน (:374) · และ **ArtifactState.Catapult ต้องมีค่า** (:378)
        //   ⇒ วันนี้ชั้นที่สามไม่ผ่านตลอด ข้อความนี้จึงยังมาไม่ถึงเรา (แต่ลงทะเบียนกันไว้)
        // ของจริงต้องตอบ VehicleProjectileFired(243178) แบบ push พร้อมตารางเวลา 6 จังหวะ
        // (PrepareAnimAt/FireAnimAt/ShootAt/WarnSince/WarnUntil/DmgAt) + หักกระสุน + คิดดาเมจ
        // ซึ่งไม่มีข้อมูล CatapultState ให้คิดเลย ⇒ ห้ามเดาเวลา/ดาเมจ จึงตอบ Abort
        _connection.Recv(delegate(FireProjectileFromVehicle msg, PacketHeader header)
        {
            Send(new Abort { Text = catapultMsg }, header.Seq);
        });

        // ── ParticipateAcceleration (21112513) — เข้าร่วมกิจกรรมเครื่องเร่งวาร์ป ────────
        // จุดยิง: client/Durango.Logic.Interactions/ArtifactInteractions.cs:98 (ผูก
        //   Interaction.ParticipateAcceleration) → :1342-1352 ส่ง {EntityId, Tile} ไม่ผูกรอ
        //   ก่อนหน้านั้นต้องผ่านกล่องยืนยัน DoWarpAccelerate (:1241-1268) ที่หัก **สสารวาร์ป**
        //   ตามราคาที่ได้จาก GetWarpAcceleratorCost — ซึ่งเซิร์ฟยังไม่มี handler
        // ⇒ เป็นการกระทำที่ทำจริงไม่ได้ (ไม่มีคลื่นสัตว์ ไม่มีเฟส ไม่มีรายชื่อผู้เข้าร่วม)
        //   และถ้าตอบ OK ลอย ๆ ผู้เล่นจะเสียสสารวาร์ปฟรี ⇒ ตอบ Abort
        _connection.Recv(delegate(ParticipateAcceleration msg, PacketHeader header)
        {
            Send(new Abort { Text = acceleratorMsg }, header.Seq);
        });

        // ── ReceiveAcceleratorRewards (21112514) — รับรางวัลจากเครื่องเร่งวาร์ป ─────────
        // จุดยิง: ArtifactInteractions.cs:99 (Interaction.ReceiveAccelerationRewards = 671)
        //   → :1354-1360 ส่ง {EntityId, Tile} ไม่ผูกรอ
        // ของจริงต้องตอบ Rewarded ที่แนบ WarpAccelerationRewardsEffect (ฝั่งเกมดักที่
        // client/WarpAcceleratorSystem.cs:26-31 StatisticsSystem.Rewarded แล้วเก็บ
        // WarpAcceleratorAcquisition ไปโชว์โควตารายสัปดาห์)
        // ⇒ เราไม่มีบันทึกว่าใครเข้าร่วมเฟสไหนสำเร็จ การแต่งรางวัลขึ้นมาเป็นการโกหกผู้เล่น
        //   (กฎโปรเจกต์: ว่างดีกว่าปลอม) ⇒ ตอบ Abort
        _connection.Recv(delegate(ReceiveAcceleratorRewards msg, PacketHeader header)
        {
            Send(new Abort { Text = acceleratorMsg }, header.Seq);
        });
    }
}
