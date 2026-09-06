using System;
using Durango.Network;
using Messages;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  งานวิจัย (Research) — ห้องแล็บส่วนตัว + งานวิจัยของเผ่า
//
//  ⚠️ อย่าสับสนกับ "วิจัยหมวดทักษะ" (ResearchSkillCategory / SkipSkillCategoryResearch)
//     ตัวนั้นทำจริงไปแล้วที่ server/Core/Player.Skills.cs:474-476 — คนละระบบกัน
//
//  ═══ ระบบนี้ทำงานยังไงในเกมจริง ═══
//  1) วิจัยส่วนตัว: แตะห้องแล็บ → เมนู Interaction.PersonalResearch → เปิดหน้าต่าง
//     client/Durango.UI/ResearchGroup.cs:58-85 → ยิง GetAvailablePersonalResearch
//     ได้ลิสต์มาแล้วเลือก → จ่ายค่าวิจัย → StartPersonalResearch → ได้ StatusEffect ชั่วคราว
//  2) วิจัยของเผ่า: แตะห้องแล็บของเผ่า → client/Durango.Logic/ResearchSystem.cs:67-191
//     เอา GetAvailableClanResearch มาสร้างเมนู แล้วเอา GetClanResearch มาใส่ตัวจับเวลา
//     (Until = วิจัยอยู่ · CooltimeUntil = คูลดาวน์หลังวิจัยเสร็จ)
//
//  ═══ ทำไมตอบเป็น "ลิสต์ว่าง" ไม่ใช่ลิสต์จริง ═══
//  ข้อมูลจริงมีครบ (server/data/assets/personal_research.json · clan_research.json —
//  ของ NEXON แท้ ทั้ง amount/currency/duration/status_effect_id) **แต่เซิร์ฟยังต่อไม่ได้**:
//    · เซิร์ฟไม่มีตัวโหลด json สองไฟล์นี้เลย (grep "personal_research" ใน server/*.cs = ไม่เจอ)
//    · StatusEffect ฝั่งเซิร์ฟออกได้ทางเดียวคือ _toggledStatusEffects (server/Core/Player.cs:52-55,
//      681-690) ซึ่งผู้เล่นเป็นคนสั่งเปิด/ปิดเอง — ไม่มีช่องให้ "มอบ" ผลวิจัยแบบมีอายุ
//    · ไม่มีทั้งระบบเผ่า ห้องแล็บของเผ่า และการหักค่าวิจัย
//  ⇒ ถ้าส่งลิสต์จริงออกไป ผู้เล่นจะกดยืนยัน "จ่าย {amount}" (ResearchGroup.cs:134-141)
//     แล้วไม่ได้อะไรกลับ = หลอกผู้เล่น ⇒ ตอบลิสต์ว่างที่ **ถูกโครงสร้าง** ดีกว่า
//     ฝั่งเกมรองรับลิสต์ว่างอย่างสวยงามอยู่แล้ว: ResearchTiersWidget.Set คืน false
//     (client/Durango.UI/ResearchTiersWidget.cs:132-134 `_availableTiers.Count == 0`)
//     → ResearchPageWidget.SetResearchList เรียก SetEmpty ต่อ
//     (client/Durango.UI/ResearchPageWidget.cs:69-75 เรียก · :82-87 ตัวเมธอด)
//     ขึ้นป้าย "ไม่มีรายการให้วิจัย" — ไม่แครช ไม่ค้าง
//
//  ⚠️ ห้ามเงียบเด็ดขาด — ResearchGroup.cs:72 ผูก "วงแหวนโหลด" ไว้ก่อนยิง แล้วถอดออกได้
//     ที่เดียวคือใน callback ของคำตอบ (:79) ไม่ตอบ = วงโหลดหมุนค้างทับหน้าต่างตลอดไป
//     ส่วนวิจัยเผ่าใช้ AsyncCachedDictionary/AsyncCachedData (ResearchSystem.cs:25-26)
//     ซึ่งจำสถานะ "กำลังขอ" ไว้ ไม่ตอบ = ค้างสถานะนั้นถาวร ขอใหม่ก็ไม่ยิงซ้ำ
//     (client/Durango.Utils/AsyncCachedData.cs:38-43 — มี _callback ค้างอยู่แล้วไม่เรียก
//      _request ซ้ำ · AsyncCachedDictionary.cs:87-99 — key อยู่ใน _requestedDict แล้ว
//      แค่พ่วง callback เพิ่ม ไม่ยิงใหม่) ทั้งสองตัวไม่มี timeout
//
//  ✅ ต่อสายแล้ว: RegisterResearchHandlers() ถูกเรียกที่ server/Core/Player.Systems.cs:61
//     (ตรวจ 7 ก.ย. 2026 — ไฟล์นั้นมีเจ้าของอยู่ ห้ามแก้จากที่นี่)
//     ทั้ง 5 TypeCode ในไฟล์นี้ไม่ซ้ำกับ handler อื่นในเซิร์ฟเลย (grep ทั้ง server/ แล้ว
//     เจอชื่อ ClanResearch/PersonalResearch นอกโฟลเดอร์ Messages แค่ที่ enum
//     server/GameCode/Shared.System/Interaction.cs:43,50) ⇒ ไม่มีใครลงทะเบียนทับกัน
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    private void RegisterResearchHandlers()
    {
        // ── GetClanResearch (5987341) ─────────────────────────────────────────────
        // ไม่มีฟิลด์ (server/GameCode/Messages/GetClanResearch.cs:6-9 — [StructLayout Size=1]
        // struct เปล่า มีแต่ const TypeCode) ⇒ Unpack ไม่อ่าน payload เลย ปลอดภัย
        // ยิงจาก client/Durango.Logic/ResearchSystem.cs:49-52 และ **รอ .On<ClanResearchList>**
        // (แคชไว้ 60 วิ — ResearchSystem.cs:26) เอาไปเทียบ LabEntityId/Until/CooltimeUntil
        // เพื่อใส่ตัวจับเวลาให้เมนู (ResearchSystem.cs:117-172) และกันกดซ้ำ
        // (client/Durango.Logic.Interactions/ArtifactInteractions.cs:1139-1153)
        // ⇒ ตอบชุดว่าง = "ยังไม่มีงานวิจัยของเผ่าที่กำลังเดินอยู่" ซึ่งเป็นความจริง
        _connection.Recv(delegate(GetClanResearch msg, PacketHeader header)
        {
            Send(new ClanResearchList
            {
                // ระบุชนิดเต็ม ๆ ตามสไตล์ฝั่งเกม (ที่นั่น Messages.ClanResearch ชนกับ
                // Yaml.ClanResearch จริง — client/Durango.Logic/ResearchSystem.cs:92,119)
                // ฝั่งเซิร์ฟยังไม่มี Yaml.ClanResearch จึงไม่ชน แต่เขียนเต็มไว้กันชนภายหลัง
                ResearchList = Array.Empty<Messages.ClanResearch>()
            }, header.Seq);
        });

        // ── GetAvailableClanResearch (5987333) ────────────────────────────────────
        // ฟิลด์: EntityId + Tile ของห้องแล็บที่แตะ (GetAvailableClanResearch.cs:9-11)
        // ยิงจาก client/Durango.Logic/ResearchSystem.cs:37-44 รอ .On<AvailableClanResearch>
        // แล้วเอา id ที่ได้ไปเปิดดู Yaml.ClanResearch สร้างปุ่มในเมนูแตะ (ResearchSystem.cs:82-107)
        // ⇒ ตอบลิสต์ว่าง: เมนู "วิจัยของเผ่า" ถูกถอดทิ้งไปแล้วตั้งแต่ ResearchSystem.cs:74
        //   (RemoveAt) และไม่มีอะไรมาแทน = ผู้เล่นไม่เห็นตัวเลือกที่กดแล้วพัง
        _connection.Recv(delegate(GetAvailableClanResearch msg, PacketHeader header)
        {
            Send(new AvailableClanResearch
            {
                AvailableResearchIds = Array.Empty<string>()
            }, header.Seq);
        });

        // ── StartClanResearch (3702) ──────────────────────────────────────────────
        // ฟิลด์: EntityId + Tile + Id (StartClanResearch.cs:9-13)
        // ยิงจาก client/Durango.Logic/ResearchSystem.cs:196-201 แบบ **ไม่ผูก .On** — กดมาจาก
        // ปุ่มยืนยันใน client/Durango.UI.Popup/ClanResearchPopup.cs:94-98 (ยิงที่ :96
        // และมีทางลัดเรียกซ้ำที่ :114)
        // ในทางปฏิบัติจะไม่มีวันมาถึง เพราะลิสต์ด้านบนว่าง (ป๊อปอัปเปิดจากเมนูที่ลิสต์นั้นสร้าง)
        // แต่ลงทะเบียนไว้กัน log "ไม่มี handler" และกันผู้เล่นยิงมาเอง
        // ไม่มี handler ผูก seq นี้ ⇒ client/Durango.Network/Connection.cs:883-886 ตกไปหยิบ
        // ตัวรับกลางแทน = Connections.Frontend.On<Abort> ที่ client/GameManager.cs:308
        // → DefaultAbortHandler (:348-351) ขึ้นข้อความบนจอ
        // ⚠️ ต้องมี Text เสมอ — LimitText (GameManager.cs:329-335) เรียก text.Length ตรง ๆ
        //    ไม่เช็ค null ⇒ default(Abort) ทำเกมแครชทันที
        _connection.Recv(delegate(StartClanResearch msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานงานวิจัยของเผ่า" }, header.Seq);
        });

        // ── GetAvailablePersonalResearch (5987336) ────────────────────────────────
        // ฟิลด์: EntityId + Tile ของห้องแล็บ (GetAvailablePersonalResearch.cs:9-11)
        // ยิงจาก client/Durango.Logic/ResearchSystem.cs:206-217 รอ .On<AvailablePersonalResearch>
        // มี .Rest → onResult(null) เป็นทางหนี แต่ถ้าไม่ตอบอะไรเลยทั้งสองทางไม่ทำงาน
        // (ตอบชนิดนี้แล้ว .Rest จะไม่ยิงซ้ำ เพราะลงทะเบียนแบบ allowReplied:false —
        //  ReplyMessageHandlerRegistrar.cs:37-44 คู่กับเงื่อนไข Connection.cs:893)
        // ⇒ วงแหวนโหลดค้าง (ResearchGroup.cs:72 ผูกไว้ก่อนยิง / :79 ถอดใน callback เท่านั้น)
        //
        // รูปคำตอบ (AvailablePersonalResearch.cs:9-15):
        //   AvailableResearchIds   — วิจัยได้เลย
        //   UnavailableResearchIds — คู่ (id, ระดับการบุกเบิกที่ต้องถึงก่อน) ⇒ ปุ่มเป็นสีเทา
        //   ResearchingId          — id ที่กำลังวิจัยอยู่ (string, nullable)
        //   AvailableResearchAt    — เวลาที่จะวิจัยครั้งถัดไปได้ (double?, nullable)
        // ส่ง null ได้จริงทั้งสองช่อง: Pack แปลงเป็น nil (AvailablePersonalResearch.cs:68-70
        // ResearchingId · :80-83 AvailableResearchAt) และ Unpack ฝั่งเกมเช็ค IsNil ก่อนเสมอ
        // (:114-133) — ไฟล์ฝั่งเกม client/Messages/AvailablePersonalResearch.cs เหมือนกันบิตต่อบิต
        // ยิ่งกว่านั้น grep ทั้ง client/ แล้ว **ไม่มีโค้ดไหนอ่านสองฟิลด์นี้เลย** (อ่านแค่สองอาร์เรย์
        // ผ่าน AvailablePersonalResearchExtension.ResearchableIds ซึ่งเช็ค null ให้ที่ :25,:33)
        // ⇒ null ไม่มีทางทำให้เกมพัง
        // **การตีความของเรา**: ว่างทุกช่อง + null = "ห้องแล็บนี้ยังไม่มีอะไรให้วิจัย"
        _connection.Recv(delegate(GetAvailablePersonalResearch msg, PacketHeader header)
        {
            Send(new AvailablePersonalResearch
            {
                AvailableResearchIds = Array.Empty<string>(),
                UnavailableResearchIds = Array.Empty<Pair<string, int>>(),
                ResearchingId = null,          // ไม่มีงานวิจัยค้างอยู่
                AvailableResearchAt = null     // ไม่มีคูลดาวน์
            }, header.Seq);
        });

        // ── StartPersonalResearch (5987338) ───────────────────────────────────────
        // ฟิลด์: EntityId + Tile + ResearchId (StartPersonalResearch.cs:9-13)
        // ยิงจาก client/Durango.Logic/ResearchSystem.cs:221-232 ด้วย **.All(...)** ⇒ รับ packet
        // อะไรก็ได้แล้วส่งผ่าน Packet.IsSuccess()
        // Abort TypeCode 1024 อยู่ในชุด "ไม่สำเร็จ" (client/Durango.Network/Packet.cs:90-99)
        // ⇒ success = false ⇒ ResearchGroup.cs:115-121 **ไม่ขึ้นป้าย "ได้รับผลวิจัยแล้ว" หลอก ๆ**
        // และเพราะ .All ลงทะเบียนแบบ allowReplied:true
        // (client/Durango.Network/ReplyMessageHandlerRegistrar.cs:28-35) ตัวรับกลาง On<Abort>
        // ก็ยังทำงานคู่กัน (client/Durango.Network/Connection.cs:883-894 — หยิบ _packetHandlers
        // มาเรียกก่อน แล้วค่อยเรียก Handler ของ seq) ⇒ ผู้เล่นเห็นเหตุผลเป็นข้อความด้วย
        _connection.Recv(delegate(StartPersonalResearch msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานงานวิจัยส่วนตัว" }, header.Seq);
        });
    }
}
