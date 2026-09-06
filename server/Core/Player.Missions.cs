using System;
using System.Collections.Generic;
using Durango.Network;
using Messages;
using Shared.Faction;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  ภารกิจประจำวัน/สารานุกรมฝั่งกลุ่ม (Faction missions) — GetMissions
//
//  ทำไมต้องมี handler ตัวนี้: เกมยิง GetMissions มา "ทุกครั้งที่เข้าเกม" ตอนระบบพร้อม
//  (client/FactionSystem.cs:139-148 OnReady → Send(default(GetMissions)) แบบไม่ผูก .On)
//  และยิงซ้ำหลังยกเลิกภารกิจ (client/FactionSystem.cs:339-348) — คำตอบถูกดึงด้วย
//  global handler ที่ลงทะเบียนไว้แล้ว: FactionSystem.Start → On<MissionInfos>
//  (client/FactionSystem.cs:105-108 → OnMissionInfos)
//
//  ⚠️ ไม่ตอบ = ระบบภารกิจ "ไม่เคยเริ่ม" แบบเงียบ ๆ:
//    OnMissionInfos บรรทัด 248 คือที่เดียวที่ตั้ง IsMissionInitialized = true และ
//    UpdateMissionState (client/FactionSystem.cs:586-591) return ทิ้งทันทีถ้ายังไม่ถูกตั้ง
//    ⇒ สถานะภารกิจติด Disabled ตลอดกาล ไม่มี error ให้เห็น (เดิมเซิร์ฟแท้ก็ไม่มี handler
//    ตัวนี้เหมือนกัน — client/Durango.Online/Player.cs ไม่มี GetMissions ⇒ เป็นของตาย
//    ที่ต้นฉบับทิ้งไว้ ไม่ใช่ของที่เราทำพัง)
//
//  ทำไมตอบ "ชุดว่างครบโครง" ถึงถูกต้อง:
//    - OnMissionInfos วนข้อมูลที่ส่งไปเป็นลำดับ: Missions ว่าง = ไม่มีภารกิจค้าง
//      (client/FactionSystem.cs:268-277) · MissionToDoUpdater.Update(ว่าง) เคลียร์ todo เก่า
//      แล้วจบอย่างปลอดภัย (client/Durango.Logic.Faction/MissionToDoUpdater.cs:27-85 —
//      ลูปไม่ทำงาน, RemoveUnused(0) ลบ collection เก่า)
//    - ShuffleCondition.Set(0, 0.0) ปลอดภัย (client/Durango.Logic.Faction/ShuffleCondition.cs:44-48)
//    - สถานะภารกิจที่ได้ = Idle/Disabled ไม่ใช่ Ready ⇒ ป้ายเตือนที่วิทยุไม่โผล่
//      (client/Durango.UI/MissionAlertTargetController.cs:69-72) — ตรงกับ "ยังไม่มีภารกิจ"
//      เพราะฝั่งเกมยังไม่เคยได้ Factions (เซิร์ฟไม่มี handler GetFactions ⇒ เลเวลกลุ่มทุกกลุ่ม
//      เป็น 0 ⇒ IsMissionAvailable เป็นเท็จเสมอ — client/Durango.Logic.Faction/Faction.cs:200-209)
//      ⇒ การเสกภารกิจขึ้นมาเองโดยไม่มีระบบกลุ่มรองรับ = เดาเงื่อนไขคนอื่น ⇒ ห้าม
//
//  ⚠️ ตอบด้วย ReplyOf = header.Seq: client ส่งคำขอแบบไม่ผูก .On ⇒ คำตอบที่ ReplyOf ตรง
//  seq จะตกไป global On<MissionInfos> เอง (client/Durango.Network/Connection.cs:883-886
//  fallback จาก seq-bound ไป global เมื่อไม่มีตัวจับ seq นั้น) — และถ้าอนาคตมีใครเริ่มผูก
//  .On ก็ยังรับได้ทันทีโดยไม่ต้องแก้เซิร์ฟ
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    private void RegisterMissionHandlers()
    {
        _connection.Recv(delegate(GetMissions msg, PacketHeader header)
        {
            // คำขอไม่พกข้อมูลอะไรเลย (struct ขนาด 1 ไบต์ — GameCode/Messages/GetMissions.cs)
            // ⇒ คำตอบเดียวที่ถูกคือสรุปสถานะภารกิจทั้งหมดของผู้เล่นคนนี้
            Send(BuildMissionInfosReply(), header.Seq);
        });
    }

    /// <summary>
    /// สรุปสถานะภารกิจชุดว่าง — โครงครบทุกฟิลด์ตามที่ MissionInfos.Unpack ฝั่งเกมอ่าน
    /// (client/Messages/MissionInfos.cs:85-142) · array/dict ว่าง ไม่ใช่ null
    /// ยกเว้น RecommendFailReasons ที่ null ได้ตามโปรโตคอล (Pack ส่ง PackNull —
    /// GameCode/Messages/MissionInfos.cs:57-60 · client เช็ค IsNil ก่อนใช้: FactionSystem.cs:263)
    ///
    /// ShuffleCount = 0 และ ShuffleAt = null ⇒ จำนวนสุ่มภารกิจคงเหลือ = 0
    /// (client/Durango.Logic.Faction/ShuffleCondition.cs:14-27 คืน _baseRemainCount ตรง ๆ
    /// เมื่อ _shuffleAt = 0) — ปุ่มสุ่มภารกิจจึงกดไม่ได้ ซึ่งถูกต้องเพราะยังไม่มีภารกิจให้สุ่ม
    /// </summary>
    private static MissionInfos BuildMissionInfosReply() => new()
    {
        Missions = Array.Empty<Mission>(),
        MissionActivatesAt = new Dictionary<FactionType, double>(),
        RecommendFailReasons = null,
        ShuffleCount = 0,
        ShuffleAt = null
    };
}
