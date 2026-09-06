using System;
using Durango.Network;
using Messages;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  เควส: รางวัล / คะแนน / NPC เนื้อเรื่อง / วาร์ปเนื้อเรื่อง / ภารกิจหมู่เกาะ
//
//  ไฟล์นี้รับ "ปลายทางฝั่งการกระทำ" ของระบบเควส ส่วน "ปลายทางฝั่งอ่านข้อมูล"
//  (GetQuests 237918 / GetQuestState 398132) อยู่ที่ Core/Player.Quest.cs แล้ว — ห้ามทับกัน
//
//  ═══ ความจริงของเซิร์ฟตอนนี้ ═══
//  เซิร์ฟ **ยังไม่มีเอนจินเควส** — ไม่มีใครเริ่มเควส วัดความคืบหน้า หรือจ่ายรางวัล
//  (คอมเมนต์เดียวกันที่ Core/Player.Quest.cs:36-42 · QuestStore ยังว่างเปล่าตลอด)
//  และไม่มีระบบ "ภารกิจหมู่เกาะ" ด้วย — เซิร์ฟไม่เคยส่ง CurrentArchipelagoTodos ออกไปเลย
//  (grep ทั้ง server/Core + server/Support แล้วไม่มีจุดส่ง) ⇒ ToDo ของหมู่เกาะจึงไม่เคยขึ้น
//
//  ⇒ กติกาที่ใช้ทั้งไฟล์นี้ (ตาม ROADMAP ข้อ "ห้ามแต่งข้อมูลปลอม"):
//    · คำขอที่เป็น "การอ่านข้อมูล" → ตอบชนิดที่ฝั่งเกมรอ ด้วย **ค่าว่างที่ถูกโครงสร้าง**
//      เพราะบางหน้าจอตั้งธง initialized ได้ที่เดียวคือตอนได้คำตอบ ไม่ตอบ = ค้างหมุนตลอดกาล
//    · คำขอที่เป็น "การกระทำ" ที่ทำจริงไม่ได้ → ตอบ Abort ที่มี Text เสมอ
//      ฝั่งเกมมี global handler รับอยู่ (client/GameManager.cs:269 → :309-312
//      DefaultAbortHandler → UIManager.SystemMsg) ⇒ ผู้เล่นเห็นข้อความ ไม่ใช่กดแล้วเงียบ
//      ⚠️ ห้าม default(Abort) เด็ดขาด — Text=null ทำให้ LimitText(null).Length แครชฝั่งเกม
//    · ห้ามจ่ายรางวัลลม ๆ (QuestRewardResults / QuestScoreReward ที่ State=Taken) เพราะจะทำให้
//      UI ขึ้นว่า "รับรางวัลแล้ว" ทั้งที่ไม่มีของเข้ากระเป๋าจริง
//
//  ═══ ทำไมต้องลงทะเบียนแม้จะตอบไม่ได้ ═══
//  ทุกตัวในไฟล์นี้เดิม "ไม่มี handler" ⇒ log เซิร์ฟขึ้นเตือนทุกครั้งที่ผู้เล่นกด และผู้เล่นไม่ได้
//  อะไรกลับเลย · ที่หนักกว่านั้นคือ RequestQuestScoreReward ที่ล็อกหน้าจอค้างถาวรถ้าเงียบ
//  (ดูคอมเมนต์ตรง handler นั้น)
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    private void RegisterQuestFlowHandlers()
    {
        // ── GetQuestScoreInfos (237920) — แถบคะแนนเควสด้านล่างหน้าเควส ─────────────
        // ยิงจาก QuestGroup.UpdateQuestScores (client/Durango.UI/QuestGroup.cs:121-128)
        // ผ่าน QuestSystem.GetQuestScoreInfos (client/Durango.Logic/QuestSystem.cs:280-289)
        // ซึ่งผูก **.On<QuestScoreInfos> กับ seq** ⇒ ต้องตอบที่ header.Seq เท่านั้น
        // (อีกจุดคือปุ่มโกงตอน debug build — client/Durango.UI/QuestBottomWidget.cs:342-348)
        //
        // ⚠️ ก่อนยิง client เรียก _questBottomWidget.BeginLoading() (QuestGroup.cs:126 →
        //    QuestBottomWidget.cs:84-92) ซึ่งเปิดไอคอนโหลดค้างไว้ · ตัวที่ปิดมันมีตัวเดียวคือ
        //    UpdateScoreInfo → EndLoading (QuestBottomWidget.cs:102-104) ที่จะถูกเรียกก็ต่อเมื่อ
        //    คำตอบ QuestScoreInfos มาถึง (QuestSystem.cs:285 → QuestGroup.cs:196-201)
        //    ⇒ **ไม่ตอบ = แถบล่างหน้าเควสหมุนค้างตลอดกาล**
        _connection.Recv(delegate(GetQuestScoreInfos msg, PacketHeader header)
        {
            SendEmptyQuestScoreInfos(msg.Category, header.Seq);
        });

        // ── RequestQuestReward (237923) — กดปุ่ม "รับรางวัล" ที่การ์ดเควส ───────────
        // ยิงจาก QuestNodeWidget.OnClickReceiveButton (client/Durango.UI/QuestNodeWidget.cs:109-117)
        // ผ่าน QuestSystem.RequestQuestReward (client/Durango.Logic/QuestSystem.cs:291-297)
        // **ไม่มี .On ผูก seq** — ฝั่งเกมรอ QuestRewardResults(237924) ทาง global handler แทน
        // (client/Durango.Logic/QuestSystem.cs:62 Connections.Frontend.On<QuestRewardResults>)
        //
        // ทำไมไม่ตอบ QuestRewardResults: มันคือ "ใบเสร็จการจ่ายรางวัล" — client จะเอาไป
        // SetQuestRewardResults ทำให้เควสกลายเป็นรับรางวัลแล้ว (QuestSystem.cs:136) และเด้ง
        // ป๊อปอัพของรางวัล ทั้งที่เซิร์ฟไม่มีเอนจินเควสจ่ายของจริง = หลอกผู้เล่น ⇒ ตอบ Abort
        //
        // ผลข้างเคียงที่ยอมรับได้: ปุ่มจะค้างวงแหวนโหลด (_isWaitRewardRequest=true) จนกว่า
        // การ์ดจะถูกวาดใหม่ ซึ่ง Set() รีเซ็ตธงให้เอง (QuestNodeWidget.cs:96-98)
        _connection.Recv(delegate(RequestQuestReward msg, PacketHeader header)
        {
            Console.WriteLine($"[เควส] {Short(EntityId)} ขอรับรางวัลเควส '{msg.QuestId}' — เซิร์ฟยังไม่มีเอนจินเควส");
            Send(new Abort { Text = "ยังไม่เปิดใช้งานการรับรางวัลเควส" }, header.Seq);
        });

        // ── RequestQuestScoreReward (237925) — กดรับรางวัลตามคะแนนเควส ─────────────
        // ยิงจาก QuestBottomWidget.QuestRewardRequested (client/Durango.UI/QuestBottomWidget.cs:292-300)
        // ผ่าน QuestSystem.RequestQuestScoreReward (client/Durango.Logic/QuestSystem.cs:299-311)
        // ซึ่งผูก **.On<QuestScoreInfos> กับ seq**
        //
        // ⚠️⚠️ จุดตายที่ต้องระวังที่สุดในไฟล์นี้: ก่อนยิง client เรียก LockInteraction() แล้ว
        //    แปะ LoadingRing (QuestBottomWidget.cs:294-299) · ตัวปลดล็อกมีเส้นเดียวคือ
        //    UpdateScoreInfo → EndLoading + PlayScrollAnim → UnlockInteraction
        //    (QuestBottomWidget.cs:102-118, 193-217) ซึ่งวิ่งได้ก็ต่อเมื่อได้ QuestScoreInfos
        //    ⇒ ตอบแค่ Abort อย่างเดียว = แถบคะแนนล็อกถาวร กดอะไรไม่ได้อีกเลยจนกว่าจะปิดเกม
        //
        // ⇒ ตอบสองก้อนบน seq เดียว: Abort (บอกผู้เล่นว่ายังทำไม่ได้) + QuestScoreInfos
        //   (สถานะจริงปัจจุบัน = คะแนน 0 ไม่มีรางวัล ⇒ ปลดล็อก UI โดยไม่โกหกว่ารับรางวัลแล้ว)
        //   ต้องคร่อมด้วย ReplySequenceMark ไม่งั้นก้อนที่สองไม่ถึงฝั่งเกม
        //   (client/Durango.Network/Connection.cs:839-861 เปิด/ปิดชุดคำตอบต่อเนื่อง —
        //    กลไกเดียวกับที่ Core/Player.Crafting.cs:91-110 อธิบายไว้)
        _connection.Recv(delegate(RequestQuestScoreReward msg, PacketHeader header)
        {
            Console.WriteLine($"[เควส] {Short(EntityId)} ขอรับรางวัลคะแนน {msg.Score} หมวด '{msg.Category}' — เซิร์ฟยังไม่มีตารางรางวัลคะแนน");

            Send(default(ReplySequenceMark), header.Seq);   // เปิดชุดคำตอบต่อเนื่อง
            Send(new Abort { Text = "ยังไม่เปิดใช้งานรางวัลคะแนนเควส" }, header.Seq);
            SendEmptyQuestScoreInfos(msg.Category, header.Seq);
            Send(default(ReplySequenceMark), header.Seq);   // ปิดชุด — ไม่ปิด handler ฝั่งเกมค้าง
        });

        // ── CustomQuestEvent (312798) — สคริปต์ไกด์แจ้ง "เกิดเหตุการณ์คำสำคัญนี้แล้ว" ──
        // ยิงจากคำสั่งในสคริปต์ PlayGuide ชื่อ "CustomQuestEvent"
        // (client/Durango.Logic.PlayGuide/CustomCommand.cs:73 ลงทะเบียน → :617-627 ส่ง)
        // **ไม่ผูกรออะไรกลับ** และไม่ใช่การกระทำที่ผู้เล่นกดเอง (สคริปต์ยิงให้อัตโนมัติ)
        // ⇒ ห้ามตอบ Abort เพราะจะเด้ง toast ขึ้นกลางฉากไกด์ทั้งที่ผู้เล่นไม่ได้ทำอะไรผิด
        // ของจริงเซิร์ฟจะเอา Keyword ไปเดินความคืบหน้าเควสที่รอคำสำคัญนี้ — เรายังไม่มีเอนจิน
        // ⇒ รับไว้เงียบ ๆ + log ไว้ให้ตามรอยได้ว่าสคริปต์ยิงคำไหนมาบ้าง
        _connection.Recv(delegate(CustomQuestEvent msg, PacketHeader header)
        {
            Console.WriteLine($"[เควส] {Short(EntityId)} แจ้งเหตุการณ์เควส '{msg.Keyword}' — ยังไม่มีเอนจินเควสรับไปเดินต่อ");
        });

        // ── InteractWithEpicNPC (3141593) — คุยกับ NPC เนื้อเรื่อง (K / T) ──────────
        // ยิงจาก ClientInteractionQuest.MenuClicked (client/ClientInteractionQuest.cs:77-95)
        // มีสองทาง: ส่งของให้ NPC (ItemIds = ของที่ผู้เล่นเลือก) หรือคุยเฉย ๆ (ItemIds = null)
        // **ไม่ผูกรออะไรกลับ** — ของจริงเซิร์ฟจะกินของแล้วเดินเควสให้ (Shared.Quest/EpicNPCType.cs
        // K=0 / T=1)
        //
        // ⚠️ **ห้ามกินของทิ้ง** — เซิร์ฟไม่มีเควสให้เครดิตกลับ กินไปคือของหายฟรี
        // ⇒ ไม่แตะกระเป๋าเลย แล้วตอบ Abort ให้ผู้เล่นรู้ว่าไม่ได้ส่งของสำเร็จ
        // (ในทางปฏิบัติเส้นนี้ยังไปไม่ถึง เพราะเซิร์ฟไม่เคยส่ง AppearEpicNPC(3141592) ออกไป
        //  ⇒ NPC เนื้อเรื่องไม่เคยโผล่ในเกม — handler นี้จึงเป็นตัวกันเหนียว)
        _connection.Recv(delegate(InteractWithEpicNPC msg, PacketHeader header)
        {
            int itemCount = msg.ItemIds?.Length ?? 0;
            Console.WriteLine($"[เควส] {Short(EntityId)} คุยกับ NPC เนื้อเรื่อง {msg.Npc} (ยื่นของ {itemCount} ชิ้น) — ยังไม่มีเอนจินเควส · ไม่กินของ");
            Send(new Abort { Text = "ยังไม่เปิดใช้งานเนื้อเรื่องกับ NPC นี้" }, header.Seq);
        });

        // ── RequestEpicWarp (77777) — วาร์ปตามเนื้อเรื่องหลังจบหนังบท ──────────────
        // ยิงจาก QuestSystem.OnQuestRewardResults หลังเล่นหนังจบ
        // (client/Durango.Logic/QuestSystem.cs:155-166 chapter.PlayMovie(...) → Send)
        // **ไม่ผูกรออะไรกลับ** — ของจริงเซิร์ฟย้ายผู้เล่นไปเกาะบทถัดไปแล้วส่ง Emigrated
        //
        // เซิร์ฟยังไม่มี "เกาะของบทเนื้อเรื่อง" ให้ย้ายไป (ระบบเดินทางที่มีคือท่าเรือ/กลับบ้าน
        // ที่ Core/Player.Warp.cs) ⇒ ตอบ Abort · ห้ามย้ายมั่วไปเกาะอื่นเพราะผู้เล่นจะหลุด
        // ออกจากเกาะของตัวเองโดยไม่ได้ตั้งใจ
        // (เส้นนี้ยังไปไม่ถึงเช่นกัน เพราะเซิร์ฟไม่เคยส่ง QuestRewardResults ตามที่อธิบายข้างบน)
        _connection.Recv(delegate(RequestEpicWarp msg, PacketHeader header)
        {
            Console.WriteLine($"[เควส] {Short(EntityId)} ขอวาร์ปตามเนื้อเรื่อง — เซิร์ฟยังไม่มีเกาะของบทเนื้อเรื่อง");
            Send(new Abort { Text = "ยังไม่เปิดใช้งานการเดินทางตามเนื้อเรื่อง" }, header.Seq);
        });

        // ── RequestReturnerGuideAction (3450984) — ปุ่มพิเศษของไกด์ "ผู้กลับ" ───────
        // ยิงจาก PlayGuideSystem.NotifyQuizAnswered (client/PlayGuideSystem.cs:965-996)
        // ตามคำตอบในบทสนทนาไกด์ · **ไม่ผูกรออะไรกลับ**
        // ค่าที่เป็นไปได้ (GameCode/Shared.Guide/ReturnerGuideAction.cs):
        //   AdvisorReset=0 (คืนแต้มที่ปรึกษา) · SkillReset=1 (คืนแต้มสกิล) · ReliefGoodsReceive=2 (รับของช่วยเหลือ)
        // ทั้งสามอย่างคือ "แจกของ/คืนแต้มจริง" ที่เซิร์ฟทำไม่ได้ ⇒ ตอบ Abort ไม่แจกลม ๆ
        //
        // ในทางปฏิบัติไม่ถูกเรียก เพราะ Core/Player.Social.cs:78 ตอบ GetReturnerInfo ว่า
        // IsReturner=false ⇒ ไกด์ผู้กลับไม่เริ่มเลย — handler นี้เป็นตัวกันเหนียว
        _connection.Recv(delegate(RequestReturnerGuideAction msg, PacketHeader header)
        {
            Console.WriteLine($"[ผู้กลับ] {Short(EntityId)} ขอทำ {msg.Action} — เซิร์ฟยังไม่มีระบบผู้กลับ");
            Send(new Abort { Text = "ยังไม่เปิดใช้งานสิทธิพิเศษของผู้กลับ" }, header.Seq);
        });

        // ── RequestArchipelagoRegionClear (240002) — กด "รายงานภารกิจบุกเบิก" ───────
        // ยิงจาก ArchipelagoToDoCollection.ReportArchipelagoMission
        // (client/Durango.Logic/ArchipelagoToDoCollection.cs:154-157) →
        // ArchipelagoMissionSystem.RequestRegionClear (client/Durango.Logic/ArchipelagoMissionSystem.cs:125-128)
        // **ไม่ผูกรออะไรกลับ** — ของจริงเซิร์ฟจ่ายรางวัลแล้ว push CurrentArchipelagoTodos(240001)
        // ที่ฝั่งเกมรับด้วย global handler (client/Durango.Logic/ArchipelagoMissionSystem.cs:27)
        //
        // เซิร์ฟไม่มีระบบภารกิจหมู่เกาะ (ไม่เคยส่ง CurrentArchipelagoTodos เลย ⇒ ToDo ไม่เคยขึ้น
        // และปุ่มนี้ไม่เคยโผล่) ⇒ ตอบ Abort ไม่จ่ายรางวัลลม ๆ
        _connection.Recv(delegate(RequestArchipelagoRegionClear msg, PacketHeader header)
        {
            Console.WriteLine($"[หมู่เกาะ] {Short(EntityId)} รายงานภารกิจบุกเบิกจบ — เซิร์ฟยังไม่มีระบบภารกิจหมู่เกาะ");
            Send(new Abort { Text = "ยังไม่เปิดใช้งานภารกิจบุกเบิกหมู่เกาะ" }, header.Seq);
        });

        // ── ReissueArchipelagoTodos (240005) — กด "รับภารกิจบุกเบิกใหม่" ────────────
        // ยิงจาก ArchipelagoToDoCollection.RequestNewArchipelagoMission หลังผู้เล่นกดยืนยัน
        // ในกล่องข้อความ (client/Durango.Logic/ArchipelagoToDoCollection.cs:183-192) →
        // ArchipelagoMissionSystem.RequestReissueArchipelagoTodos (ArchipelagoMissionSystem.cs:130-133)
        // **ไม่ผูกรออะไรกลับ** — ของจริงเซิร์ฟสุ่ม todo ชุดใหม่แล้ว push CurrentArchipelagoTodos
        // เหตุผลเดียวกับตัวบน ⇒ ตอบ Abort
        _connection.Recv(delegate(ReissueArchipelagoTodos msg, PacketHeader header)
        {
            Console.WriteLine($"[หมู่เกาะ] {Short(EntityId)} ขอภารกิจบุกเบิกชุดใหม่ — เซิร์ฟยังไม่มีระบบภารกิจหมู่เกาะ");
            Send(new Abort { Text = "ยังไม่เปิดใช้งานภารกิจบุกเบิกหมู่เกาะ" }, header.Seq);
        });

        // ── RequestFullCountPOIsReward (9031) — รางวัล "สำรวจจุดสำคัญครบทั้งเกาะ" ───
        // ยิงจากปุ่มรางวัลบนแผนที่โลก: ExploreReward.Set(RewardState.Available)
        // (client/Durango.UI/ExploreReward.cs:53-62) → MapSystem.RequestFullCountPOIsReward
        // (client/MapSystem.cs:416-422 ใส่ RegionId ของเกาะที่ยืนอยู่)
        // **ไม่ผูกรออะไรกลับ** — ของจริงเซิร์ฟจ่ายเงินแล้ว push ExploredPOIs(903) ใหม่ที่มี
        // FullCountRewarded=true (client/MapSystem.cs:166 global On<ExploredPOIs>)
        //
        // ปุ่มนี้จะโผล่ก็ต่อเมื่อ ExploredPOIs.RewardCost != null (client/Durango.UI/WorldMapGroup.cs:717-724)
        // แต่ Core/Player.Map.cs:143-148 ส่ง RewardCost=null (ไม่มีตารางรางวัลจริง) ⇒ กดไม่ได้อยู่แล้ว
        // ⇒ handler นี้เป็นตัวกันเหนียว · ตอบ Abort ไม่จ่ายเงินลม ๆ
        _connection.Recv(delegate(RequestFullCountPOIsReward msg, PacketHeader header)
        {
            Console.WriteLine($"[แผนที่] {Short(EntityId)} ขอรางวัลสำรวจครบของเกาะ '{msg.RegionId}' — เซิร์ฟยังไม่มีตารางรางวัลสำรวจ");
            Send(new Abort { Text = "ยังไม่เปิดใช้งานรางวัลสำรวจจุดสำคัญครบ" }, header.Seq);
        });

        // ── RequestDumpedPersonalIsland (381922) — ดัมป์เกาะส่วนตัวออกมาเป็นไฟล์ ────
        // เป็นเครื่องมือ debug ล้วน ๆ สองจุด:
        //   · Commands.ClientCheatDownloadPersonalIsland (client/Durango.Development/Commands.cs:569-581)
        //   · เมนู debug "dump_personal_island" (client/Durango.System.Config/ConfigInstance.cs:542-548)
        // ทั้งคู่ผูก **.On<DumpedPersonalIsland>(381923) กับ seq**
        //
        // ทำไมไม่ตอบ DumpedPersonalIsland: ก้อนนั้นต้องมี TerrainId + AppearPlayer + Artifacts
        // + InventoryItems + Garden (ไบต์ของแปลงปลูกทั้งเกาะ) ครบชุด ฝั่งเกมเอาไปเขียนเป็น
        // WorldContext/PlayerContext ของโหมดออฟไลน์ทันที (ConfigInstance.cs:551-570)
        // ⇒ ตอบไม่ครบ = สร้างไฟล์เซฟออฟไลน์ที่พังไว้ในเครื่องผู้เล่น อันตรายกว่าไม่ตอบ
        // ⇒ ตอบ Abort · **ผลข้างเคียง**: ป๊อปอัพ DumpPersonalIslandPopup จะค้างเปิดอยู่
        //   (มันปิดตัวเองในคอลแบ็กเท่านั้น) — ยอมรับได้เพราะเข้าถึงได้จากเมนู debug เท่านั้น
        _connection.Recv(delegate(RequestDumpedPersonalIsland msg, PacketHeader header)
        {
            Console.WriteLine($"[debug] {Short(EntityId)} ขอดัมป์เกาะส่วนตัวของ {Short(msg.PlayerEntityId)} — ยังไม่รองรับ");
            Send(new Abort { Text = "ยังไม่รองรับการดัมป์เกาะส่วนตัว" }, header.Seq);
        });
    }

    /// <summary>
    /// ตอบ QuestScoreInfos(237921) แบบ "ว่างแต่ถูกโครงสร้าง" — ใช้ร่วมกันสองจุด
    /// (GetQuestScoreInfos และ RequestQuestScoreReward) เพื่อไม่ให้สองเส้นตอบขัดกันเอง
    ///
    /// ⚠️ QuestScoreRewards **ห้ามเป็น null** — ฝั่งเกมทำ
    /// <c>_scoreRewards.AddRange(questScoreInfos.QuestScoreRewards)</c> ตรง ๆ
    /// (client/Durango.UI/QuestBottomWidget.cs:120-126) ⇒ null = ArgumentNullException แครช
    ///
    /// ทำไมส่งอาร์เรย์ว่าง (ไม่ใช่แต่งตารางรางวัล): client ใช้ "มีรางวัลกี่ชิ้น" ตัดสินว่าหมวดนี้
    /// มีระบบคะแนนหรือไม่ — <c>HasQuestScore = GetSize(rewards) &gt; 0</c>
    /// (client/Durango.Logic/QuestSystem.cs:471-481) แล้ว QuestGroup ซ่อนแถบล่างทิ้งไปเลย
    /// ถ้าเป็น false (client/Durango.UI/QuestGroup.cs:133-140) ⇒ ผู้เล่นเห็น "ไม่มีระบบนี้"
    /// ซึ่งเป็นความจริง ดีกว่าเห็นตารางรางวัลปลอมที่กดแล้วไม่ได้ของ
    ///
    /// CurQuestScore=0 คือคะแนนจริงของผู้เล่น — เซิร์ฟไม่เคยให้คะแนนเควสใคร (ไม่มีเอนจินเควส)
    /// </summary>
    private void SendEmptyQuestScoreInfos(string category, uint seq)
    {
        Send(new QuestScoreInfos
        {
            // ⚠️ ต้อง echo หมวดที่ขอมา **ดิบ ๆ ห้ามแทนค่า** — client ทิ้งคำตอบที่หมวดไม่ตรงกับ
            // แท็บที่เปิดอยู่ทั้งก้อน: QuestGroup.QuestScoreInfosUpdated เช็ค
            // `if (IsOpened && !(questScoreInfos.Category != SelectedCategory))` ก่อนเรียก
            // UpdateScoreInfo (client/Durango.UI/QuestGroup.cs:194-200)
            // ⇒ หมวดไม่ตรง = ไม่มีใครเรียก EndLoading/UnlockInteraction = ค้างถาวร
            //   ซึ่งเป็นอาการเดียวกับที่ทั้งไฟล์นี้พยายามกันอยู่
            //
            // เดิมตรงนี้เขียนว่า "ว่าง ⇒ ใช้ EpicCategory" ซึ่งกลับหัวกลับหาง: ถ้า client ขอมา
            // ด้วยหมวดว่างจริง SelectedCategory ฝั่งนั้นก็ว่างด้วย การตอบ "sunset" กลับไปจึง
            // การันตีว่าหมวดไม่ตรง — คือสร้างอาการค้างขึ้นมาเองแทนที่จะกัน
            // (ต่างจาก GetQuests ที่ Player.Quest.cs:81 แทนค่าได้ เพราะเส้นนั้น client ใช้
            //  หมวดของฝั่งตัวเองเดินต่อ ไม่ได้เอา Category ในคำตอบไปเทียบแบบนี้)
            //
            // ในทางปฏิบัติหมวดว่างไปไม่ถึงอยู่แล้ว — QuestGroup.TryOpen คืน false ถ้า
            // SelectedCategory ว่าง (client/Durango.UI/QuestGroup.cs:84-96) ⇒ ไม่มีใครยิงคำขอ
            // แต่ echo ดิบไว้ก็ไม่มีทางผิด ส่วนการแทนค่ามีแต่ทางผิด ⇒ เลือกทางที่ผิดไม่ได้
            // null ก็ปลอดภัย (QuestScoreInfos.Pack แปลง null เป็นสตริงว่างให้เอง) แต่เขียนชัดไว้
            Category = category ?? string.Empty,
            CurQuestScore = 0,
            QuestScoreRewards = Array.Empty<QuestScoreReward>()
        }, seq);
    }
}
