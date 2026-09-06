using System;
using Durango.Network;
using Messages;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  พันธมิตรของแคลน (Ally) — ข้อตกลงระหว่างแคลนสองแคลนให้นับกันเป็นพวกเดียวกัน
//
//  ═══ ระบบนี้ทำงานยังไงในเกมจริง ═══
//  แคลนหนึ่งมี "ช่องพันธมิตร" (AllySlot) หลายช่อง เปิดเพิ่มตามเลเวลแคลน
//  (client/Durango.UI/ClanAllyPage.cs:139-227 Set() อ่าน Constants.Ally.SlotOpensAt/DefaultSlotCount
//   /MaxSlotCount มาคำนวณจำนวนช่องที่เปิดแล้ว แล้ววาดวิดเจ็ตตามสถานะของแต่ละช่อง)
//  แต่ละช่องมีสองแกน: IsAlly (เป็นพันธมิตรกันอยู่หรือยัง) กับ State
//  (server/GameCode/Shared.Clan/AllySlotState.cs — Solid=0 · Suggested=1 · BeenSuggested=2 · Locked=3)
//  ผสมกันได้ 6 หน้าตาตาม ClanAllyPage.cs:171 (Locked) + :178-202 (IsAlly × State) เช่น
//    IsAlly=false + Suggested     = เราเสนอไปแล้ว รอเขาตอบ
//    IsAlly=false + BeenSuggested = เขาเสนอมา รอเราตอบ (รับ/ปฏิเสธ)
//    IsAlly=true  + Solid         = เป็นพันธมิตรกันอยู่
//    IsAlly=true  + Suggested     = เราเสนอเลิกไปแล้ว รอเขาตอบ
//    State=Locked                 = ช่องถูกล็อกชั่วคราว (เพราะเคยฉีกสัญญาแบบไม่รอตกลง)
//
//  ═══ ทำไมต้องมี handler กลุ่มนี้ ═══
//  ผลของการเป็นพันธมิตรไม่ได้อยู่แค่หน้า UI — client/ClanSystem.cs:350-362 IsAlliedClan()
//  วนดู Allies เพื่อตัดสินว่าผู้เล่นอีกคน "เป็นพวกเรา" หรือไม่ (ใช้ตัดสินการตีกัน/สิทธิ์ต่าง ๆ)
//  ถ้า Allies ยังเป็น null เพราะเซิร์ฟไม่เคยตอบ ตัวนับ KUtility.GetSize คืน 0 ⇒ ไม่พังก็จริง
//  แต่หน้า "สถานะพันธมิตร" จะค้างเปล่า ๆ และ log ฝั่งเซิร์ฟจะขึ้น "ไม่มี handler" ทุกครั้งที่เข้าเกม
//
//  ═══ ขอบเขตที่เราทำได้ตอนนี้ ═══
//  เซิร์ฟตัวนี้ **ยังไม่มีระบบแคลนจริง** — server/Core/PlayerContext.cs:180-181 ยัด
//  AppearPlayer.Member.ClanId/ClanName เป็นค่าว่างตายตัว (ที่เดียวในเซิร์ฟที่แตะ Member.ClanId)
//  และ server/Core/Player.Clan.cs ตอบ MakeClan/JoinClan ด้วย Abort ⇒ ผู้เล่นไม่มีวันมีแคลน
//  (และ client/ClanSystem.cs:89 ยิง GetAllySlots เฉพาะตอน HasClan เท่านั้น — PlayerBehavior.cs:261
//   HasClan = ClanId ไม่ว่าง && RoleId != -1) ⇒ ปกติคำขอนี้จะไม่ถูกยิงด้วยซ้ำ
//  เราจึงลงทะเบียนไว้แบบ "โครงว่างที่ถูกชนิด" ตามแนวเดียวกับ Player.Social.cs:
//  ตอบรายการช่องพันธมิตร **ว่างเปล่า** (ไม่แต่งช่องปลอมให้ผู้เล่นเข้าใจว่ากดเพิ่มพันธมิตรได้)
//  และคำสั่งที่เป็น "การกระทำ" ตอบ Abort พร้อมข้อความไทย แทนการเงียบหาย
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    // ต่อสายแล้ว: server/Core/Player.Systems.cs:52 เรียก RegisterAllyHandlers() ต่อจาก
    // RegisterClanHandlers() (:51) — เป็นกลุ่มแคลนเหมือนกัน
    // ⚠️ ห้ามเพิ่มจุดเรียกที่สอง — server/GameCode/Durango.Online/Connection.cs
    //    RegisterMessageHandlerToRegistry เตือนแล้วทับ handler เดิมของ TypeCode เดียวกัน
    private void RegisterAllyHandlers()
    {
        // ข้อความกลางเวลาผู้เล่นสั่งอะไรที่เซิร์ฟยังทำจริงไม่ได้
        // ⚠️ ห้ามส่ง default(Abort) เด็ดขาด — Text=null ⇒ Abort.Pack แพ็ก null ลงสาย
        //    (server/GameCode/Messages/Abort.cs:20 PackString(val.Text)) → ฝั่งเกม
        //    client/GameManager.cs:308 ผูก On<Abort>(DefaultAbortHandler) → :348-350
        //    เรียก LimitText(msg.Text) แล้ว .Length บน null ⇒ เกมแครช
        // ประกาศเป็น const "ในเมธอด" ไม่ใช่สมาชิกของคลาส เพราะ Player เป็น partial class ที่ถูกเขียน
        // พร้อมกันหลายไฟล์ — ตั้งชื่อสมาชิกซ้ำข้ามไฟล์เมื่อไหร่คอมไพล์พังทันที ส่วน const ในเมธอด
        // เป็นของเฉพาะเมธอดนี้ ชนกับไฟล์อื่นไม่ได้ (และถูก inline ตอนคอมไพล์ ไม่ต้องแคปเจอร์เข้า closure)
        const string AllyNotAvailableText = "ยังไม่เปิดใช้งานระบบพันธมิตรของแคลน";

        // ── GetAllySlots (9138745) — ขอสถานะช่องพันธมิตรทั้งหมดของแคลนตัวเอง ──────────────
        // จุดยิง: client/ClanSystem.cs:83 (OnReady ตอนเข้าเกม) และ :202 (ตอนย้าย/เข้าแคลนใหม่)
        //         ผ่าน ClanSystem.GetAllySlots() ที่ :86-93 → :91 Send(default(GetAllySlots))
        // ⚠️ ฝั่งเกม **ไม่ได้ผูก .On<> กับคำขอ** — รับคำตอบด้วย global handler ตัวเดียว
        //    client/ClanSystem.cs:57 `Connections.Frontend.On<AllySlots>(OnAllySlots)`
        //    → :95-102 เก็บลง Allies แล้วยิง event AlliesUpdated (ClanAllyPage.cs:121 ฟังอยู่)
        // ตอบด้วย header.Seq ได้ปลอดภัย: client/Durango.Network/Connection.cs:868-886 HandleMsg
        // หา handler เฉพาะ seq ก่อน ไม่เจอค่อยตกไป global ⇒ ทางนี้ถึงมือ OnAllySlots เหมือนกัน
        // แต่ครอบคลุมกว่าการ push ReplyOf=0 (เผื่อวันหลังฝั่งเกมผูก .On<AllySlots> กับคำขอ)
        //
        // **การตีความของเรา**: ยังไม่มีระบบแคลน ⇒ ไม่มีช่องพันธมิตรจริงสักช่อง จึงตอบอาเรย์ว่าง
        // จงใจไม่แต่งช่องเปล่า (IsAlly=false + State=Solid) ทั้งที่มันจะทำให้ปุ่ม "+ เพิ่มพันธมิตร"
        // โผล่ (ClanAllyPage.cs:211-220) เพราะกดแล้วปลายทางคือ SuggestAlly ที่เราทำจริงไม่ได้
        // — หลอกให้ผู้เล่นกดของที่ใช้ไม่ได้ แย่กว่าปล่อยว่าง (กฎ: ห้ามแต่งข้อมูลปลอม)
        _connection.Recv(delegate(GetAllySlots msg, PacketHeader header)
        {
            Send(new AllySlots { Slots = Array.Empty<AllySlot>() }, header.Seq);
        });

        // ── SuggestAlly (9138747) — เสนอเป็นพันธมิตรกับแคลนอื่น ──────────────────────────
        // จุดยิง: client/ClanSystem.cs:695-701 (static SuggestAlly) ถูกเรียกจาก
        //   · client/Durango.UI/ClanGroup.cs:205-230 หลังผู้เล่นกดยืนยันในกล่องข้อความ
        //     ต้นทางอีกที: client/Durango.UI.Popup/ClanInfoPopup.cs:182-184 ปุ่ม "동맹 맺기"
        //     และ client/Durango.UI/ClanListPage.cs:166 (ค้นหาแคลนเพื่อผูกพันธมิตร)
        // ฝั่งเกมส่งแบบ **ไม่ผูก .On<>** ⇒ ปกติรอ AllySlots ชุดใหม่ถูก push กลับมาแทน
        // เราสร้างข้อเสนอจริงไม่ได้ (ไม่มีที่เก็บ ไม่มีแคลนปลายทาง) ⇒ ตอบ Abort
        // ซึ่งจะไปโผล่เป็นข้อความระบบผ่าน client/GameManager.cs:308 → :348 DefaultAbortHandler
        // (→ UIManager.SystemMsg แสดง 4 วินาที)
        _connection.Recv(delegate(SuggestAlly msg, PacketHeader header)
        {
            Send(new Abort { Text = AllyNotAvailableText + " — ยังเสนอเป็นพันธมิตรไม่ได้" }, header.Seq);
        });

        // ── SuggestBreak (9138748) — เสนอ "เลิกเป็นพันธมิตร" แบบตกลงกันสองฝ่าย ──────────
        // จุดยิง: client/ClanSystem.cs:703-709 ← client/Durango.UI/ClanGroup.cs:237-250 (:247)
        //         (ปุ่มบนวิดเจ็ตช่องที่ IsAlly=true + Solid — ClanAllyPage.cs:71)
        // หมายเหตุ: เพราะเราตอบรายการช่องว่างเปล่า วิดเจ็ตนี้จะไม่ถูกวาด ⇒ ปกติยิงไม่ถึง
        //           ลงทะเบียนไว้กันเหนียว (และกัน log "ไม่มี handler" ถ้ามีทางยิงอื่น)
        _connection.Recv(delegate(SuggestBreak msg, PacketHeader header)
        {
            Send(new Abort { Text = AllyNotAvailableText + " — ยังเสนอเลิกเป็นพันธมิตรไม่ได้" }, header.Seq);
        });

        // ── AcceptSuggestion (9138749) — รับข้อเสนอที่อีกฝ่ายส่งมา ─────────────────────
        // จุดยิง: client/ClanSystem.cs:711-717 ← client/Durango.UI/ClanGroup.cs:278 (BeenSuggestedAlly
        //         — รับข้อเสนอเป็นพันธมิตร) และ :298 (BeenBreakSuggestedAlly — รับข้อเสนอเลิกเป็น
        //         พันธมิตร) ปุ่ม "수락" ในกล่องข้อความ
        // ใช้ message ตัวเดียวกันทั้งสองกรณี ฝั่งเซิร์ฟจริงแยกจากสถานะช่องเอง
        // เราไม่มีข้อเสนอค้างอยู่จริงให้รับ ⇒ ตอบ Abort
        _connection.Recv(delegate(AcceptSuggestion msg, PacketHeader header)
        {
            Send(new Abort { Text = AllyNotAvailableText + " — ยังรับข้อเสนอไม่ได้" }, header.Seq);
        });

        // ── RefuseSuggestion (9138750) — ปฏิเสธข้อเสนอที่อีกฝ่ายส่งมา ──────────────────
        // จุดยิง: client/ClanSystem.cs:719-725 ← client/Durango.UI/ClanGroup.cs:281 และ :301
        //         (ปุ่ม "거절" คู่กับ AcceptSuggestion ในกล่องเดียวกัน)
        _connection.Recv(delegate(RefuseSuggestion msg, PacketHeader header)
        {
            Send(new Abort { Text = AllyNotAvailableText + " — ยังปฏิเสธข้อเสนอไม่ได้" }, header.Seq);
        });

        // ── BreakAlly (9138751) — ฉีกสัญญาพันธมิตรฝ่ายเดียว (ไม่รออีกฝ่ายตกลง) ──────────
        // จุดยิง: client/ClanSystem.cs:727-733 ← client/Durango.UI/ClanGroup.cs:252-265 (:262)
        //         กล่องเตือนบอกว่าทำแล้วช่องนั้นจะถูกล็อก (State=Locked) ไปพักหนึ่ง
        // เราไม่มีสัญญาจริงให้ฉีก และไม่มีที่เก็บสถานะล็อกช่อง ⇒ ตอบ Abort
        _connection.Recv(delegate(BreakAlly msg, PacketHeader header)
        {
            Send(new Abort { Text = AllyNotAvailableText + " — ยังเลิกเป็นพันธมิตรไม่ได้" }, header.Seq);
        });
    }
}
