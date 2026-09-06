using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Network;
using Messages;
using Shared.Estate;
using Shared.Teleport;
using Yaml;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  แผนที่ / วาร์ป / การเดินทางข้ามเกาะ — [6 ก.ย. 2026]
//
//  ไฟล์นี้ต่อจากของเดิมสองไฟล์ **ห้ามซ้ำกับมัน** (อ่านก่อนแก้ทุกครั้ง):
//    • server/Core/Player.Warp.cs — จุดกลับ/กลับบ้าน/ไปท่าเรือ + Points(2033)
//      มี BeginWarp/FinishWarp/_warpTimers ที่ไฟล์นี้ยืมแนวมาใช้ต่อ (บรรทัด 252-303)
//    • server/Core/Player.Map.cs  — หมุดบนแผนที่ (POI) + LoadPois/RegionKey ที่ไฟล์นี้เรียกใช้
//
//  ═══ เซิร์ฟนี้ "ข้ามเกาะ" ได้แค่ไหน ═══
//  ตรงข้ามกับที่เคยเข้าใจกัน: เซิร์ฟนี้ **มีเกาะจริง 18 ลูก** (server/data/terrains/*.zip)
//  และการเดินทางด้วยเรือทำงานอยู่แล้วผ่าน Player.cs:1993 HandleTravelMsg
//  (เซิร์ฟจำชื่อเกาะปลายทางลงไฟล์เซฟ → ส่ง Emigrated(2099) → ตัวเกมตัดการเชื่อมต่อแล้วต่อใหม่
//   เข้าโลกนั้น — client/GameManager.cs:322-336)
//  ⇒ คำสั่งที่ "ย้ายเกาะ" ในไฟล์นี้จึงต่อสายเข้า HandleTravelMsg ตัวเดิม ไม่ทำทางใหม่ซ้อน
//
//  แต่สิ่งที่ **ยังทำจริงไม่ได้** คือกลุ่มที่ต้องมีระบบอื่นรองรับก่อน:
//    • เกาะส่วนตัว / ที่ดิน (estate) — server/Core/Player.Estate.cs อธิบายไว้แล้วว่าเซิร์ฟ
//      ไม่เคยส่ง EstateGrids/EstateLicense เลย ⇒ ไม่มีที่ดินให้วาร์ปกลับไปหา
//    • ภารกิจหมู่เกาะ (archipelago mission) · แคมป์ · แท่งเร่งวาร์ป (warp rush)
//    • ระบบเงิน t_stone — ค่าวาร์ปจริงมีสูตรอยู่ใน constants.json → warp → warp_cost
//      แต่ยังไม่มีกระเป๋าเงิน ⇒ **วาร์ปฟรี (Cost = 0)** ตามแนวเดียวกับ Player.Warp.cs:55
//  กลุ่มนี้ตอบ Abort ที่มีข้อความไทยเสมอ ไม่ปล่อยเงียบ
//
//  ═══ กับดักที่ยืนยันจากซอร์สฝั่งเกมแล้ว ═══
//
//  ① คำสั่งวาร์ปทุกตัวผ่าน MapSystem.TryWarp → DoWarp (client/MapSystem.cs:589-612)
//     ซึ่งผูก .On<Timer>(…).Rest(… WarpTimer.Stop())
//     ⇒ ตอบ Timer = หลอดวาร์ปเดินต่อ · ตอบอย่างอื่น (เช่น Abort) = หลอดหยุด + ข้อความเด้ง
//     ⇒ **ห้ามเงียบ** ไม่งั้นตัวละครยืนเล่นท่าวาร์ปค้างแล้วจบไปเฉย ๆ
//
//  ② Abort ต้องมี Text เสมอ — client/GameManager.cs:309-312 DefaultAbortHandler เรียก
//     LimitText(msg.Text) ⇒ default(Abort) ทำให้ฝั่งเกมแครช (Text เป็น null)
//
//  ③ **ห้ามตอบ WarpCosts ที่มี Costs ว่างให้ GetWarpBackCost** — client/MapSystem.cs:426-429
//     เขียน msg.Costs[0].Cost ตรง ๆ ไม่เช็คขนาด ⇒ IndexOutOfRange ฝั่งเกม
//     (ตัวพี่น้องอย่าง GetWarpCosts/GetWarpCostToNextRegion เช็คขนาดก่อน จึงตอบว่างได้)
//     ทางออกของเราคือตอบ Abort แทน — ไม่แครช ไม่ต้องแต่งราคาปลอม และผู้เล่นเห็นเหตุผล
//
//  ④ TeleportType ต้องตรงกับสิ่งที่ทำจริง — client/Durango.Logic.PlayGuide/WarpToDo.cs:20
//     นับภารกิจ "วาร์ป" สำเร็จเมื่อ Type เป็น Warp หรือ WarpBack เท่านั้น
//     ⇒ วาร์ปผ่านรูวาร์ปต้องส่ง TeleportType.Warp (ต่างจาก "กลับบ้าน" ที่ต้องเป็น Returning
//       ตามที่ Player.Warp.cs:51-53 เตือนไว้) — เป็นเหตุผลเดียวที่ไฟล์นี้มีตัวเดินเวลาของตัวเอง
//       แทนการเรียก BeginWarp ตรง ๆ เพราะ FinishWarp ตัวเดิมตรึง Type ไว้ที่ Returning
//
//  ⑤ ข้อความที่ฝั่งเกมยิงแบบ **ไม่รอคำตอบ** ห้ามตอบ Abort ถ้ามันยิงเองอัตโนมัติ
//     (เหตุผลเต็มที่ Player.Estate.cs:38-41 — toast จะเด้งใส่หน้าโดยผู้เล่นไม่ได้สั่งอะไร)
//     ในไฟล์นี้มีตัวไม่รอคำตอบ 4 ตัว แยกเป็นสองแบบ:
//       • ActivePersonalRegionWarphole · TravelToRandomPersonalRegion · Withdraw
//         — มาจากปุ่ม/หน้าต่างยืนยันที่ผู้เล่นกดเอง ⇒ ตอบได้ และต้องตอบ ไม่งั้นกดแล้วเงียบสนิท
//       • GetRouteOfArchipelago — หน้าต่างเปิดแล้วยิงเอง ⇒ ห้าม Abort · ตอบด้วย push ที่ ReplyOf = 0
//
//  ═══ ตัวที่จงใจไม่ลงทะเบียนในไฟล์นี้ ═══
//    • RemoveSection (3687) — ชื่อชวนเข้าใจผิด แต่มันคือ "ลบช่องหมวดในตู้เก็บของ"
//      (client/InventorySystem.cs:740-746 ส่ง EntityId/Tile/SectionName ของตู้ที่เปิดอยู่)
//      คู่ของมันคือ MakeSection ซึ่งอยู่ใน server/Core/Player.Inventory.cs:160 แล้ว
//      ⇒ เป็นงานของระบบคลังของ ไม่ใช่การเดินทาง — ส่งต่อให้ Life ตามที่ตกลงกัน
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    /// <summary>
    /// แบบแปลนที่นับเป็น "รูวาร์ป" ที่กดวาร์ปได้
    ///
    /// สองตัวนี้ไม่ได้เลือกเอง — client/Durango.Logic.Map/POIUpdater.cs:147-152 แปลง
    /// <c>BlueprintId</c> เป็นหมุดชนิด <c>CargoWarphole</c> เฉพาะสองชื่อนี้เท่านั้น
    /// แล้ว client/MapSystem.cs:270-273,300-307 เอาหมุดนั้นไปทำ <c>IndicatorType.Warphole</c>
    /// ซึ่งเป็นชนิดเดียวที่กดเพื่อวาร์ปได้ (WorldMapGroup.cs:1029-1035)
    /// ⇒ ถ้ารับชนิดอื่น เซิร์ฟจะยอมวาร์ปไปที่ที่ฝั่งเกมไม่มีปุ่มให้กดตั้งแต่แรก
    ///
    /// เกาะของเราวาง <c>neutral_warphole</c> (entity type 9450) จาก pois.yml
    /// (server/Core/World.cs PlaceTerrainPois) ส่วน <c>cargo_warphole_in</c> (9440)
    /// เป็นของที่ผู้เล่นสร้างเอง — เผื่อไว้ให้ครบตามที่ฝั่งเกมรู้จัก
    /// </summary>
    private static readonly HashSet<string> WarpholeBlueprints =
        new(StringComparer.Ordinal) { "neutral_warphole", "cargo_warphole_in" };

    private void RegisterTravelHandlers()
    {
        // ── กลุ่มที่ 1: วาร์ปในเกาะเดียวกัน — **ทำของจริงได้** ──────────────────────────────

        // GetWarpCosts (2106) — ราคาวาร์ปของรูวาร์ปแต่ละแห่งบนเกาะนี้
        // จุดยิง: client/Durango.UI/WorldMapGroup.cs:961-969 SetMapForWarp (เปิดแผนที่โหมดวาร์ป)
        //         และ client/Durango.UI/InteractionGroup.cs:288-297 (ฟื้นคืนชีพที่รูวาร์ป)
        // รอ .On<WarpCosts> ที่ seq เดิม — ไม่ตอบ = แผนที่โหมดวาร์ปไม่มีป้ายราคาและกดไม่ได้เลย
        _connection.Recv(delegate(GetWarpCosts msg, PacketHeader header)
        {
            HandleGetWarpCostsMsg(header.Seq);
        });

        // Warp (2108) {Tile} — วาร์ปไปรูวาร์ปที่กดบนแผนที่
        // จุดยิง: client/MapSystem.cs:432-438 → TryWarp → DoWarp (รอ Timer, มี .Rest)
        // ⚠️ ฝั่งเกมยอมยิงเฉพาะช่องที่อยู่ใน _latestWarpholeCosts เท่านั้น
        //    (client/Durango.UI/WorldMapGroup.cs:1061-1066) ⇒ เซิร์ฟตรวจซ้ำด้วยชุดเดียวกัน
        _connection.Recv(delegate(Warp msg, PacketHeader header)
        {
            HandleWarpMsg(msg, header.Seq);
        });

        // IsWarpholeAvailable (3021) {EntityId, Tile} — "แตะรูวาร์ปนี้แล้วเปิดแผนที่วาร์ปได้ไหม"
        // จุดยิง: client/Durango.UI/WorldMapGroup.cs:1213-1226 — รอ **.On<OK>** อย่างเดียว
        // ได้ OK แล้วถึงจะเรียก OpenForWarp(null) ⇒ ไม่ตอบ = แตะรูวาร์ปแล้วไม่มีอะไรเกิดขึ้น
        _connection.Recv(delegate(IsWarpholeAvailable msg, PacketHeader header)
        {
            HandleIsWarpholeAvailableMsg(msg, header.Seq);
        });

        // ── กลุ่มที่ 2: ข้อมูลแผนที่/เส้นทาง — **ทำของจริงได้** ────────────────────────────

        // GetRegionMapInfo (205) {RegionId} — ขนาดแผนที่ + หมอกของเกาะที่ขอ
        // จุดยิง: client/Durango.UI/SharedMapContext.cs:119-160 Load() รอ .On<RegionMapInfo>
        // แล้วเอา TileCount.x ไปตั้ง MapSize ก่อนสร้างพื้นผิวแผนที่ทั้งผืน
        // ⚠️ ไม่ตอบ = หน้าต่างแผนที่ที่แชร์กัน (ปักหมุดบอกตำแหน่ง) ค้างจอดำถาวร
        // ⚠️ ต้องสะท้อน RegionId กลับให้ตรงตัวอักษรที่ขอมา — SharedMapContext.cs:134
        //    เทียบ _regionId != msg.RegionId แล้วทิ้งคำตอบที่ไม่ตรงทันที
        _connection.Recv(delegate(GetRegionMapInfo msg, PacketHeader header)
        {
            HandleGetRegionMapInfoMsg(msg, header.Seq);
        });

        // GetRouteOfArchipelago (20301) {EntityId, Tile} — เส้นทางไป "เกาะข้างเคียงในหมู่เกาะเดียวกัน"
        // จุดยิง: client/Durango.UI/ExploreGroup.cs:249-251 (เปิดหน้าท่าเรือโหมด Neighbor)
        //         ผ่าน client/ExploreSystem.cs:389-396 ซึ่ง **ไม่ผูก .On รอคำตอบ**
        // ⇒ ต้องตอบแบบ ReplyOf = 0 เพราะฝั่งเกมรับด้วย global On<RoutesOfArchipelago>
        //   (client/ExploreSystem.cs:45 → :273-281 แปลงเป็น Routes แล้วยิง RoutesUpdated)
        _connection.Recv(delegate(GetRouteOfArchipelago msg, PacketHeader header)
        {
            SendRoutesOfCurrentArchipelago();
        });

        // RecommendRegion (3001) {EntityId, Tile, Role?, TemplateId} — "หาเกาะแบบนี้ให้หน่อย"
        // จุดยิง: client/Durango.UI/ExploreGroup.cs:158-184 (กดที่โซนที่ยังไม่รู้จักบนหน้าเดินเรือ)
        //         ผ่าน client/ExploreSystem.cs:221-243 — รอ .On<Region> หรือ .On<Error>
        // ได้คำตอบแล้วฝั่งเกมแค่ขอ Routes ใหม่ (ExploreGroup.cs:357-364 OnFoundRegion)
        _connection.Recv(delegate(RecommendRegion msg, PacketHeader header)
        {
            HandleRecommendRegionMsg(msg, header.Seq);
        });

        // RecommendArchipelago (3012) {Level, Biome, UnstableFactor?} — "หาหมู่เกาะระดับนี้ให้หน่อย"
        // จุดยิง: client/Durango.UI/ExploreGroup.cs:124-146 ผ่าน client/ExploreSystem.cs:205-219
        //         รอ .On<Archipelago> — ExploreGroup.cs:348-355 ปิดวงกลมโหลดแล้วขอ Routes ใหม่
        // ⚠️ ไม่ตอบ = วงกลมโหลดกลางจอหมุนค้างไปตลอด (ShowLoadingIcon(true) ไม่มีใครปิด)
        _connection.Recv(delegate(RecommendArchipelago msg, PacketHeader header)
        {
            HandleRecommendArchipelagoMsg(msg, header.Seq);
        });

        // RecommendStableRegions (5792841) — รายชื่อเกาะ "ที่มั่นคง" ให้เลือกย้ายไปตั้งหลัก
        // จุดยิง: client/Durango.UI/RecommendRegionPage.cs:110-117 (หน้า "แนะนำเกาะ" ในเมนูที่ดิน)
        //         ผ่าน client/MapSystem.cs:694-718 — รอ .On<RecommendedStableRegions> และมี .Rest
        _connection.Recv(delegate(RecommendStableRegions msg, PacketHeader header)
        {
            HandleRecommendStableRegionsMsg(header.Seq);
        });

        // ── กลุ่มที่ 3: ย้ายเกาะจริง — ต่อสายเข้า HandleTravelMsg ตัวเดิม ────────────────────

        // TravelToStableRegion (20321235) {RegionId} — เลือกเกาะจากหน้าแนะนำแล้วย้ายไปเลย
        // จุดยิง: client/Durango.UI/RecommendRegionPage.cs:185-188 → client/MapSystem.cs:721-746
        //         ซึ่งใช้ .All(Packet.IsSuccess) ⇒ รับ OK ที่ HandleTravelMsg ส่งอยู่แล้วได้พอดี
        _connection.Recv(delegate(TravelToStableRegion msg, PacketHeader header)
        {
            Console.WriteLine($"[เดินทาง] {Short(EntityId)} ย้ายไปเกาะที่มั่นคง '{msg.RegionId}'");
            HandleTravelMsg(msg.RegionId, header.Seq);
        });

        // TravelToRandomPersonalRegion (20314) — "ออกเรือสุ่มไปเกาะส่วนตัวของใครสักคน"
        // จุดยิง: client/Durango.UI/ExploreGroup.cs:327-338 (หน้าต่างยืนยัน → ยิงแบบไม่รอคำตอบ)
        // เซิร์ฟยังไม่มีระบบ "เจ้าของเกาะ" แต่เกาะบทบาท Personal มีอยู่จริงในสารบัญและ
        // ล่องเรือไปได้อยู่แล้วผ่านหน้าเส้นทางปกติ ⇒ สุ่มหนึ่งลูกแล้วพาไปจริง ไม่ใช่ของปลอม
        _connection.Recv(delegate(TravelToRandomPersonalRegion msg, PacketHeader header)
        {
            HandleTravelToRandomPersonalRegionMsg(header.Seq);
        });

        // Withdraw (2028) {EntityId, Tile} — "กลับท่าเรือเกาะที่มั่นคงที่ออกมา"
        // จุดยิง: client/Durango.UI/ExploreGroup.cs:303-326 (เมนู SailingWithdraw ที่ท่าเรือ
        //         บนเกาะไม่เสถียร → หน้าต่างยืนยัน) ผ่าน client/ExploreSystem.cs:140-147 ไม่รอคำตอบ
        // **การตีความของเรา**: เซิร์ฟยังไม่เก็บประวัติว่าออกเรือมาจากเกาะไหน ⇒ ใช้ความหมาย
        // เดียวกับ SailingBack ที่ Player.cs:269-272 ใช้อยู่แล้ว คือกลับ "เกาะตั้งต้น" (RegionId = null)
        _connection.Recv(delegate(Withdraw msg, PacketHeader header)
        {
            Console.WriteLine($"[เดินทาง] {Short(EntityId)} ถอนตัวจาก {_world.TerrainId} กลับเกาะตั้งต้น");
            HandleTravelMsg(null, header.Seq);
        });

        // ── กลุ่มที่ 4: ยังทำจริงไม่ได้ — ตอบ Abort ที่มีข้อความ ห้ามเงียบ ──────────────────

        // GetWarpBackCost (2109) — ราคาวาร์ปกลับเกาะที่เพิ่งสำรวจอยู่
        // จุดยิง: client/Durango.UI/WorldMapGroup.cs:1161-1179 (ปุ่มบนแผนที่) ผ่าน
        //         client/MapSystem.cs:424-430 ซึ่งอ่าน **msg.Costs[0].Cost โดยไม่เช็คขนาด**
        // ⇒ ตอบ WarpCosts ว่างไม่ได้ (กับดัก ③) และแต่งราคาก็ไม่ได้เพราะวาร์ปกลับทำไม่ได้อยู่ดี
        //   (Points.LastReturnPoint เซิร์ฟส่ง null อยู่แล้ว — Player.Warp.cs:355)
        _connection.Recv(delegate(GetWarpBackCost msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานการวาร์ปกลับเกาะเดิม" }, header.Seq);
        });

        // WarpBack (2110) — วาร์ปกลับเกาะที่สำรวจค้างไว้
        // จุดยิง: client/MapSystem.cs:441-444 → TryWarp (รอ Timer, มี .Rest)
        // เซิร์ฟไม่ได้เก็บ "เกาะก่อนหน้า" และไม่เคยส่ง Points.LastReturnPoint ⇒ ไม่มีปลายทาง
        _connection.Recv(delegate(WarpBack msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่มีเกาะที่วาร์ปกลับได้ — ใช้ท่าเรือเดินทางแทน" }, header.Seq);
        });

        // OpenMap (915) {VoucherId} — "ซื้อแผนที่" เปิดหมุดทั้งเกาะรวดเดียว
        // จุดยิง: client/MapSystem.cs:139-152 PurchaseMap ← client/Durango.UI/WorldMapGroup.cs:1103
        //         รอ .On<ExploredPOIs> แล้วถือว่าซื้อสำเร็จ
        // ⚠️ ตอบหมุดครบทั้งเกาะ = แจกฟรีทั้งที่เป็นของต้องซื้อ และล้มระบบสำรวจของ Player.Map.cs
        //    ตอบหมุดเดิม = ผู้เล่นนึกว่าจ่ายแล้วแต่ไม่ได้อะไร ⇒ ปฏิเสธตรง ๆ ชัดเจนกว่า
        _connection.Recv(delegate(OpenMap msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานการซื้อแผนที่" }, header.Seq);
        });

        // ActivePersonalRegionWarphole (3022) {EntityId, Tile} — เปิดใช้รูวาร์ปส่วนตัว
        // จุดยิง: client/Durango.Logic.Interactions/ArtifactInteractions.cs:117-124
        //         (เมนูบนสิ่งปลูกสร้าง — ยิงแบบไม่รอคำตอบ)
        // ผู้เล่นเป็นคนกดเมนูนี้เอง ⇒ ตอบ Abort ได้ตามข้อ ⑤ (ไม่ใช่ข้อความที่เกมยิงเอง)
        _connection.Recv(delegate(ActivePersonalRegionWarphole msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบเกาะส่วนตัว" }, header.Seq);
        });

        // WarpToPersonalRegion (3023) — วาร์ปไปที่ดินบนเกาะส่วนตัวของตัวเอง
        // จุดยิง: client/Durango.Logic.Interactions/ArtifactInteractions.cs:125-132 → TryWarp
        // ต้องมีระบบที่ดินก่อน (server/Core/Player.Estate.cs อธิบายไว้ว่ายังไม่มี)
        _connection.Recv(delegate(WarpToPersonalRegion msg, PacketHeader header)
        {
            // ใช้เส้นเดียวกับ ReturnToEstate(PersonalPlayer)
            HandleReturnToEstate(new ReturnToEstate { OwnerType = OwnerType.PersonalPlayer }, header.Seq);
        });

        // WarpToUrbanRegion (3024) — วาร์ปไปที่ดินบนเกาะเมือง
        // จุดยิง: client/Durango.Logic.Interactions/ArtifactInteractions.cs:133-140 → TryWarp
        _connection.Recv(delegate(WarpToUrbanRegion msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบที่ดินบนเกาะเมือง" }, header.Seq);
        });

        // WarpToNextArchipelagoRegion (2035) — วาร์ปข้ามไปเกาะถัดไปของภารกิจหมู่เกาะ
        // จุดยิง: client/Durango.Logic/ArchipelagoToDoCollection.cs:159-180 (ปุ่มในรายการภารกิจ)
        //         ผ่าน client/ExploreSystem.cs:154-157 → TryWarp
        // ลำดับเกาะมาจาก ArchipelagoMission ซึ่งเซิร์ฟยังไม่ได้ทำ (Player.cs:1943 Progess = 100
        // ตายตัวอยู่) ⇒ ไม่รู้ว่า "เกาะถัดไป" คือลูกไหนจริง ๆ — บอกทางที่ใช้ได้จริงแทน
        _connection.Recv(delegate(WarpToNextArchipelagoRegion msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานภารกิจหมู่เกาะ — ใช้ท่าเรือเดินทางแทน" }, header.Seq);
        });

        // GetWarpCostToNextRegion (12033) — ราคาวาร์ปไปเกาะถัดไปของภารกิจหมู่เกาะ
        // จุดยิง: client/Durango.Logic/ArchipelagoMissionSystem.cs:135-145 รอ .On<WarpCosts>
        //         (ตัวนี้เช็ค GetSize(msg.Costs) > 0 ก่อน จึงตอบว่างได้โดยไม่แครช — ต่างจากกับดัก ③)
        // แต่ตอบว่าง = ปุ่มกดแล้วเงียบสนิท ⇒ ตอบ Abort ให้ผู้เล่นรู้เหตุผลตั้งแต่ขั้นถามราคา
        _connection.Recv(delegate(GetWarpCostToNextRegion msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานภารกิจหมู่เกาะ" }, header.Seq);
        });

        // GetWarpAcceleratorCost (21112519) — ค่าเข้าร่วมกิจกรรม "เร่งวาร์ป"
        // จุดยิง: client/Durango.Logic.Interactions/ArtifactInteractions.cs:1262-1268
        //         รอ .On<Cost> แล้วเปิดหน้าต่างจ่ายเงิน (DoWarpAccelerate ที่ :1240-1259)
        // เซิร์ฟยังไม่มีระบบ WarpAccelerator (ไม่เคยส่ง ArtifactState.Warpaccelerator เลย)
        // ⇒ ตอบราคา 0 จะพาไปหน้าต่างที่กดยืนยันแล้วไม่มีอะไรรับต่อ — ปฏิเสธตรงนี้ชัดกว่า
        _connection.Recv(delegate(GetWarpAcceleratorCost msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานกิจกรรมเร่งวาร์ป" }, header.Seq);
        });

        // RecommendPersonalRegion (3002) {TemplateId} — สร้าง/เลือกภูมิประเทศเกาะส่วนตัวของตัวเอง
        // จุดยิง: client/Durango.UI/EstateGroup.cs:93-117 รอ .On<PersonalRegion>
        //         ได้แล้วจะพาไปเกาะส่วนตัวทันที (EstateSystem.ReturnToEstate)
        // ⚠️ ตอบ PersonalRegion ปลอม = ฝั่งเกมพาไปเกาะที่ไม่มีอยู่ ⇒ ปฏิเสธ
        _connection.Recv(delegate(RecommendPersonalRegion msg, PacketHeader header)
        {
            HandleRecommendPersonalRegion(msg, header.Seq);
        });

        // ReturnToCamp (3462987) — กลับ "แคมป์" (จุดตั้งค่ายบนเกาะไม่เสถียร)
        // จุดยิง: client/Durango.UI/WorldMapGroup.cs:761-765 → client/MapSystem.cs:460-470 TryWarp
        // ปุ่มนี้เปิดได้ก็ต่อเมื่อ Points.CampPoint มีค่า (WorldMapGroup.cs:200-210 ValidFunc)
        // ซึ่งเซิร์ฟส่ง null อยู่ (Player.Warp.cs:356) — แต่บทไกด์เรียกตรงได้ จึงต้องรับไว้
        _connection.Recv(delegate(ReturnToCamp msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบแคมป์" }, header.Seq);
        });
    }

    // ── รูวาร์ปบนเกาะนี้ ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ช่องของรูวาร์ปที่ผู้เล่นคนนี้ "เจอแล้ว" บนเกาะปัจจุบัน
    ///
    /// ทำไมต้องกรองสองชั้น:
    ///   ① เอาเฉพาะที่เจอแล้ว — หมุดบนแผนที่ฝั่งเกมมาจาก ExploredPOIs เท่านั้น
    ///      (server/Core/Player.Map.cs) ⇒ ถ้าส่งครบทั้งเกาะ ตัวนับ "ต้องเจอรูวาร์ปอย่างน้อย
    ///      2 แห่งถึงวาร์ปได้" ของ client/Durango.UI/WorldMapGroup.cs:963-966 จะโกหกผู้เล่น
    ///   ② เทียบกับ pois.yml อีกชั้น — ExplorePOI เป็นข้อมูลที่ **ฝั่งเกมเป็นคนบอกมา**
    ///      (client/Durango.Logic.Map/POIUpdater.cs) ถ้าเชื่อดิบ ๆ ตัวเกมที่ถูกดัดแปลงจะ
    ///      ประกาศช่องไหนก็ได้ว่าเป็นรูวาร์ป แล้ววาร์ปไปช่องไหนบนเกาะก็ได้ทันที
    /// </summary>
    private List<Point2> ExploredWarpholeTiles()
    {
        var found = new List<Point2>();
        if (_context.ExploredPOIs == null)
        {
            return found;
        }

        TerrainPois pois = LoadPois(null);
        var real = new HashSet<long>();
        if (pois?.Warpholes != null)
        {
            foreach (Point2 tile in pois.Warpholes)
            {
                real.Add(TileKey(tile.x, tile.y));
            }
        }

        foreach (ExploredPoint point in _context.ExploredPOIs.Values)
        {
            if (point.RegionId != _world.TerrainId)
            {
                continue;
            }
            var type = (Shared.System.PointOfInterest)point.Type;
            if (type is not (Shared.System.PointOfInterest.Warphole or
                             Shared.System.PointOfInterest.CargoWarphole))
            {
                continue;
            }
            if (!real.Contains(TileKey(point.X, point.Y)))
            {
                continue;
            }
            found.Add(new Point2(point.X, point.Y));
        }
        return found;
    }

    /// <summary>รวมพิกัดช่องเป็นคีย์เดียวสำหรับ HashSet — เกาะกว้างสุดหลักพัน ไม่ล้น</summary>
    private static long TileKey(int x, int y) => ((long)x << 32) | (uint)y;

    private void HandleGetWarpCostsMsg(uint seq)
    {
        List<Point2> tiles = ExploredWarpholeTiles();
        var costs = new WarpCost[tiles.Count];
        for (int i = 0; i < tiles.Count; i++)
        {
            costs[i] = new WarpCost
            {
                // Cost = 0 (ฟรี) — สูตรจริง constants.json → warp → warp_cost ต้องมีระบบเงิน
                // t_stone ก่อน ซึ่งยังไม่มี (เหตุผลเดียวกับ Support/WarpTuning.cs และค่าเดินเรือ)
                Tile = tiles[i],
                Cost = 0L,
                // Prohibited = true จะขึ้นป้าย "ใช้ไม่ได้" — สงวนไว้ให้ระบบสงคราม/ยึดครอง
                // ที่ยังไม่มี (client/Durango.UI/WorldMapGroup.cs:1067-1071)
                Prohibited = false
            };
        }
        Send(new WarpCosts { Costs = costs }, seq);
    }

    private void HandleWarpMsg(Warp msg, uint seq)
    {
        List<Point2> tiles = ExploredWarpholeTiles();
        bool known = tiles.Any(t => t.x == msg.Tile.x && t.y == msg.Tile.y);
        if (!known)
        {
            Console.WriteLine($"[เดินทาง] ปฏิเสธวาร์ป {Short(EntityId)} → " +
                              $"[{msg.Tile.x},{msg.Tile.y}] — ไม่ใช่รูวาร์ปที่เจอแล้วบนเกาะนี้");
            Send(new Abort { Text = "ที่นั่นไม่ใช่รูวาร์ปที่เคยพบ" }, seq);
            return;
        }

        // ลงตรงช่องของรูวาร์ปเลย เหมือนที่ Player.Warp.cs:179 ทำกับท่าเรือ — ไม่เลี่ยง footprint
        // เพราะฝั่งเกมดันตัวละครออกจากสิ่งกีดขวางเองอยู่แล้ว และการเลี่ยงเองอาจไปโผล่ในน้ำ
        BeginTravelWarp(msg.Tile, seq, "วาร์ปผ่านรูวาร์ป", TeleportType.Warp);
    }

    private void HandleIsWarpholeAvailableMsg(IsWarpholeAvailable msg, uint seq)
    {
        if (_world.ArtifactManager.Get(msg.EntityId) is not { } artifact)
        {
            Send(new Abort { Text = "ไม่พบรูวาร์ปนี้" }, seq);
            return;
        }

        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(artifact.EntityType);
        if (blueprint?.Id == null || !WarpholeBlueprints.Contains(blueprint.Id))
        {
            Send(new Abort { Text = "ที่นี่ใช้วาร์ปไม่ได้" }, seq);
            return;
        }

        // ด่านระยะเดียวกับ MayTouchArtifact (Player.cs:1327-1335) — แต่ **ไม่เช็คเจ้าของ**
        // เพราะรูวาร์ปกลางเกาะเป็นของที่เซิร์ฟวางเอง ไม่มีเจ้าของ (World.PlaceTerrainPois)
        // ⇒ ถ้าเรียก MayTouchArtifact ตรง ๆ จะปฏิเสธทุกคนตลอดกาล
        int reach = ArtifactReachTiles + Math.Max(artifact.Size.x, artifact.Size.y);
        if (!IsWithinTiles(artifact.Tile, reach))
        {
            Send(new Abort { Text = "อยู่ไกลรูวาร์ปเกินไป" }, seq);
            return;
        }

        Send(default(OK), seq);
    }

    // ── ตัวเดินเวลาวาร์ปของไฟล์นี้ ───────────────────────────────────────────────────

    /// <summary>
    /// เริ่มวาร์ปแบบระบุ <see cref="TeleportType"/> ได้
    ///
    /// ทำไมไม่เรียก <c>BeginWarp</c> ของ Player.Warp.cs: ตัวนั้นจบด้วย <c>FinishWarp</c>
    /// ซึ่งตรึง <c>TeleportType.Returning</c> ไว้ตายตัว (ถูกแล้วสำหรับ "กลับบ้าน") แต่การวาร์ป
    /// ผ่านรูวาร์ปต้องเป็น <c>Warp</c> ไม่งั้นบทไกด์นับภารกิจวาร์ปไม่ขึ้น (กับดัก ④ หัวไฟล์)
    ///
    /// นาฬิกาที่สร้างที่นี่ฝากไว้ในลิสต์ <c>_warpTimers</c> ตัวเดียวกับ Player.Warp.cs
    /// เพื่อให้ <c>ClearWarpTimers</c> ที่ผูกกับ ConnetionClosed ไว้แล้วเก็บกวาดให้ครบตอนหลุด
    /// </summary>
    private void BeginTravelWarp(Point2 tile, uint seq, string what, TeleportType type)
    {
        float duration = Math.Max(0f, WarpTuning.WarpTime);

        // ⚠️ Timer ต้องเป็นคำตอบแรกและตัวเดียวที่ seq นี้ (กับดัก ① ที่ Player.Warp.cs:38-42)
        Send(new Messages.Timer { Duration = duration }, seq);
        Console.WriteLine($"[เดินทาง] {Short(EntityId)} {what} → [{tile.x},{tile.y}] (รอ {duration:0.#} วิ)");

        if (duration <= 0f)
        {
            FinishTravelWarp(tile, type);
            return;
        }

        System.Threading.Timer timer = null;
        timer = new System.Threading.Timer(delegate
        {
            try
            {
                FinishTravelWarp(tile, type);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[เดินทาง] ย้ายตัวไม่สำเร็จ: {e.Message}");
            }
            finally
            {
                lock (_warpTimers) { _warpTimers.Remove(timer); }
                timer?.Dispose();
            }
        }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

        lock (_warpTimers) { _warpTimers.Add(timer); }
        timer.Change((int)(duration * 1000f), System.Threading.Timeout.Infinite);
    }

    /// <summary>
    /// ย้ายตัวจริง — ลอกลำดับจาก <c>Player.Warp.cs:291-303 FinishWarp</c> ทั้งดุ้น ต่างแค่ Type
    ///
    /// ⚠️ สองหน่วยคนละแบบ: ฝั่งเซิร์ฟเก็บเป็น world unit (ช่อง × 200) ส่วน <c>Teleported.Tile</c>
    /// เป็นหน่วยช่อง (ฝั่งเกมคูณเอง) — ไม่อัปเดตฝั่งเซิร์ฟ = ด่านระยะทุกตัวยังคิดจากที่เก่าเงียบ ๆ
    /// ⚠️ <c>Teleported</c> ต้อง ReplyOf = 0 เพราะฝั่งเกมรับด้วย global handler
    /// (client/PlayerManager.cs:346-353) — ส่งที่ seq จะไปโดน .Rest แล้วตัวละครไม่ขยับ
    /// </summary>
    private void FinishTravelWarp(Point2 tile, TeleportType type)
    {
        Movement[] movements = _context.AppearPlayer.Move.Movements;
        if (movements != null && movements.Length > 0 &&
            movements[0].Path != null && movements[0].Path.Length > 0)
        {
            movements[0].Path[0].Position = new WorldPosition(tile.x * 200, tile.y * 200);
        }

        Send(new Teleported { Tile = tile, Type = type });
        OnContextChanged();
    }

    // ── ข้อมูลแผนที่ของเกาะ ──────────────────────────────────────────────────────────

    private void HandleGetRegionMapInfoMsg(GetRegionMapInfo msg, uint seq)
    {
        string regionId = RegionKey(msg.RegionId);
        int tilesX;
        int tilesY;
        DefoggedChunks chunks;

        if (string.Equals(regionId, _world.TerrainId, StringComparison.OrdinalIgnoreCase))
        {
            tilesX = _world.NumTilesX;
            tilesY = _world.NumTilesY;
            // ชุดเดียวกับที่ส่งตอนเข้าเกม (Player.cs:860 SendDefoggedChunks) — เปิดหมอกทั้งผืน
            chunks = _world.CreateDefoggedChunks();
        }
        else
        {
            TerrainData data;
            try
            {
                data = TerrainLoader.Load(regionId);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[แผนที่] อ่านขนาดเกาะ {regionId} ไม่ได้: {e.Message}");
                data = null;
            }
            if (data == null)
            {
                Send(new Abort { Text = "ไม่พบข้อมูลแผนที่ของเกาะนี้" }, seq);
                return;
            }
            tilesX = data.Width;
            tilesY = data.Height;
            // เกาะที่ยังไม่เคยไป = ยังไม่เปิดหมอกสักชังก์ (ต้องเป็นอาร์เรย์ว่าง ไม่ใช่ null —
            // client/Durango.UI/SharedMapContext.cs:150 วน .Chunks.Length ทันทีโดยไม่เช็ค null)
            chunks = new DefoggedChunks { Chunks = Array.Empty<Point2>() };
        }

        Send(new RegionMapInfo
        {
            RegionId = msg.RegionId,   // ต้องสะท้อนตัวอักษรเดิม ไม่งั้นฝั่งเกมทิ้งคำตอบ
            TerrainId = regionId,
            TileCount = new Point2(tilesX, tilesY),
            DefoggedChunks = chunks
        }, seq);
    }

    // ── เส้นทาง / การแนะนำเกาะ ───────────────────────────────────────────────────────

    /// <summary>
    /// เกาะที่ "มั่นคง" — บทบาท Rural / Outpost / Urban
    ///
    /// สามตัวนี้คือกลุ่มที่ฝั่งเกมถือว่าไม่ใช่เกาะไม่เสถียร: client/Durango.UI/WorldMapGroup.cs
    /// ContextActionFinder จัดสามบทบาทนี้เป็นพวกเดียวกัน (มีท่าเรือให้วาร์ปกลับ) และหน้าที่
    /// เรียกใช้คำสั่งนี้ก็เปิดเฉพาะตอนยืนอยู่บน Rural/Urban (RecommendRegionPage.cs:57)
    /// </summary>
    private static bool IsStableRole(Shared.Region.Role role) =>
        role is Shared.Region.Role.Rural or Shared.Region.Role.Outpost or Shared.Region.Role.Urban;

    private void HandleRecommendStableRegionsMsg(uint seq)
    {
        var routes = new List<Route>();
        foreach (Messages.Region region in RegionCatalog.Others(_world.TerrainId))
        {
            RegionCatalog.TemplateInfo template = RegionCatalog.GetTemplate(region.TemplateId);
            if (template == null || !IsStableRole(template.Role))
            {
                continue;
            }
            // Price = null = ฟรี — ตีความเดียวกับ Player.cs:1876 (client/Durango.UI/ExploreGroup.cs:222
            // อ่าน null เป็น Money.ForFree แล้วข้ามหน้าจ่ายเงิน)
            routes.Add(new Route { RegionId = region.Id, Price = null });
        }

        // สลับลำดับทุกครั้ง — ปุ่ม "แนะนำใหม่" (RecommendRegionPage.cs:110) ยิงคำสั่งเดิมซ้ำ
        // ถ้าลำดับตายตัวผู้เล่นจะเห็นชุดเดิมตลอดและนึกว่าปุ่มเสีย
        // ฝั่งเกมตัดเองตามจำนวนช่องที่วางไว้บนหน้าจอ (RecommendRegionPage.cs:127-149)
        Route[] shuffled = routes.OrderBy(_ => System.Random.Shared.Next()).ToArray();

        Console.WriteLine($"[เดินทาง] {Short(EntityId)} ขอรายชื่อเกาะที่มั่นคง — ส่งไป {shuffled.Length} ลูก");
        Send(new RecommendedStableRegions { Routes = shuffled }, seq);
    }

    private void HandleRecommendRegionMsg(RecommendRegion msg, uint seq)
    {
        var candidates = new List<Messages.Region>();
        foreach (Messages.Region region in RegionCatalog.Others(_world.TerrainId))
        {
            RegionCatalog.TemplateInfo template = RegionCatalog.GetTemplate(region.TemplateId);
            if (template == null)
            {
                continue;
            }
            if (!string.IsNullOrEmpty(msg.TemplateId) &&
                !string.Equals(region.TemplateId, msg.TemplateId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (msg.Role.HasValue && template.Role != msg.Role.Value)
            {
                continue;
            }
            candidates.Add(region);
        }

        if (candidates.Count == 0)
        {
            // ฝั่งเกมมี .On<Error> รออยู่ (client/ExploreSystem.cs:238-242) แล้วปิดวงกลมโหลดให้
            // ⚠️ Error.Text ก็ต้องไม่เป็น null ด้วยเหตุผลเดียวกับ Abort (Player.cs:1985-1987)
            Console.WriteLine($"[เดินทาง] ไม่มีเกาะที่ตรงคำขอ role={msg.Role} template='{msg.TemplateId}'");
            Send(new Error { Text = "ยังไม่มีเกาะแบบนี้ในเซิร์ฟนี้" }, seq);
            return;
        }

        // "แนะนำ" ในเกมจริงคือเซิร์ฟสร้างเกาะใหม่ให้ — ของเราเกาะมีอยู่ครบแล้วและอยู่ในหน้า
        // เส้นทางอยู่แล้ว ⇒ สุ่มคืนหนึ่งลูกที่มีจริง ฝั่งเกมจะขอ Routes ใหม่แล้วเห็นมันในลิสต์
        Messages.Region picked = candidates[System.Random.Shared.Next(candidates.Count)];
        Console.WriteLine($"[เดินทาง] {Short(EntityId)} แนะนำเกาะ {picked.Id} ({picked.TemplateId})");
        Send(picked, seq);
    }

    private void HandleRecommendArchipelagoMsg(RecommendArchipelago msg, uint seq)
    {
        // คำขอมีแค่ Level/Biome (ไม่มี id) ⇒ ไล่หา template ที่ตรงแล้วประกอบ id ด้วยสูตรเดียว
        // กับตอนส่ง Routes (RegionCatalog.ArchipelagoIdOf) ไม่งั้นจะได้คนละหมู่เกาะกัน
        // UnstableFactor ในคำขอไม่ได้ใช้ — เราส่ง 1 คงที่ทุกหมู่เกาะอยู่แล้ว (Player.cs:1913-1916)
        string archipelagoId = null;
        foreach (Messages.Region region in RegionCatalog.All)
        {
            RegionCatalog.TemplateInfo template = RegionCatalog.GetTemplate(region.TemplateId);
            if (template == null || template.Level != msg.Level || template.Biome != msg.Biome)
            {
                continue;
            }
            archipelagoId = RegionCatalog.ArchipelagoIdOf(template);
            break;
        }

        if (archipelagoId == null)
        {
            Console.WriteLine($"[เดินทาง] ไม่มีหมู่เกาะระดับ {msg.Level} ไบโอม {msg.Biome}");
            Send(new Abort { Text = "ยังไม่มีหมู่เกาะแบบนี้ในเซิร์ฟนี้" }, seq);
            return;
        }

        // ใช้ตัวประกอบคำตอบเดิม (Player.cs:1935 HandleGetArchipelagoMsg) — มันส่ง Archipelago(2053)
        // ที่ seq ให้เรียบร้อย และเป็นชุดข้อมูลเดียวกับที่ GetArchipelago ตอบ จึงไม่ขัดกันเอง
        HandleGetArchipelagoMsg(new GetArchipelago { ArchipelagoId = archipelagoId }, seq);
    }

    /// <summary>
    /// เกาะข้างเคียงในหมู่เกาะเดียวกับที่ยืนอยู่ — <c>RoutesOfArchipelago</c>(20320) แบบ ReplyOf = 0
    ///
    /// ประกอบด้วยสูตรเดียวกับ <c>Player.cs:1861 HandleGetRoutesMsg</c> ทุกฟิลด์
    /// (UnstableFactor = 1 · Price = null = ฟรี · ไม่มีเควสบังคับ) เพื่อให้สองหน้าจอไม่ขัดกัน
    /// </summary>
    private void SendRoutesOfCurrentArchipelago()
    {
        var archipelagoRoutes = new List<ArchipelagoRoute>();

        RegionCatalog.TemplateInfo current = null;
        if (RegionCatalog.TryGet(_world.TerrainId, out Messages.Region here))
        {
            current = RegionCatalog.GetTemplate(here.TemplateId);
        }

        if (current != null)
        {
            string archipelagoId = RegionCatalog.ArchipelagoIdOf(current);
            var included = new List<Route>();
            foreach (Messages.Region region in RegionCatalog.Others(_world.TerrainId))
            {
                RegionCatalog.TemplateInfo template = RegionCatalog.GetTemplate(region.TemplateId);
                if (template == null || RegionCatalog.ArchipelagoIdOf(template) != archipelagoId)
                {
                    continue;
                }
                included.Add(new Route { RegionId = region.Id, Price = null });
            }

            archipelagoRoutes.Add(new ArchipelagoRoute
            {
                ArchipelagoId = archipelagoId,
                Level = current.Level,
                Biome = current.Biome,
                UnstableFactor = 1,
                IncludedRoutes = included.ToArray(),
                PrerequisiteQuest = null,
                IsEpic = false
            });
        }
        else
        {
            Console.WriteLine($"[เดินทาง] เกาะ {_world.TerrainId} ไม่มี template — ส่งเส้นทางข้างเคียงเปล่า");
        }

        Send(new RoutesOfArchipelago { ArchipelagoRoutes = archipelagoRoutes.ToArray() });
    }

    private void HandleTravelToRandomPersonalRegionMsg(uint seq)
    {
        var candidates = new List<Messages.Region>();
        foreach (Messages.Region region in RegionCatalog.Others(_world.TerrainId))
        {
            RegionCatalog.TemplateInfo template = RegionCatalog.GetTemplate(region.TemplateId);
            if (template != null && template.Role == Shared.Region.Role.Personal)
            {
                candidates.Add(region);
            }
        }

        if (candidates.Count == 0)
        {
            Send(new Abort { Text = "ยังไม่มีเกาะส่วนตัวให้ไปเยี่ยม" }, seq);
            return;
        }

        Messages.Region picked = candidates[System.Random.Shared.Next(candidates.Count)];
        Console.WriteLine($"[เดินทาง] {Short(EntityId)} ออกเรือสุ่มไปเกาะส่วนตัว {picked.Id}");
        HandleTravelMsg(picked.Id, seq);
    }
}
