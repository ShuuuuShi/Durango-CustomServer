using System;
using System.Collections.Generic;
using Durango.Network;
using Messages;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  ดนตรี (แต่งเพลง/แชร์โน้ต) + คอนเสิร์ต (วงดนตรีบนเวที)
//
//  ═══ ของเดิมที่มีอยู่แล้ว (อย่าซ้ำ) ═══
//  server/Core/Player.cs:407-425 รับไปแล้ว 5 ตัว และเก็บลง _context.Musics
//  (Dictionary<int, Music> — server/Core/PlayerContext.cs:39):
//    GetMusics(47852453) · SaveMusicToSlot · RemoveMusicFromSlot · PlayMusic(3802) · StopMusic(3803)
//  server/Core/Player.cs:477-483 รับ TurnOnMusic/TurnOffMusic (ตู้เพลงของสิ่งปลูกสร้าง) ไปแล้ว
//  ⇒ ไฟล์นี้รับ **เฉพาะ 11 ตัวที่ยังไม่มีใครรับ** เท่านั้น
//
//  ═══ แบ่งเป็น 2 ก้อน ═══
//  ก้อนที่ 1 "โน้ตเพลง" (47852xxx) — ช่องเพลงส่วนตัวเราทำได้จริง (มี _context.Musics)
//    แต่ "โน้ตที่แชร์" (SharedSheet/SharedMusic) เซิร์ฟยังไม่มีคลังเก็บเลย
//    (grep "SharedMusics" ในเซิร์ฟ: HandleGetMusicsMsg — server/Core/Player.cs:1626-1629 —
//     ส่งแต่ _Musics ไม่เคยเติม SharedMusics ⇒ ฝั่งเกมไม่มีทางมี SharedId อยู่ในมือ)
//  ก้อนที่ 2 "คอนเสิร์ต" (63459xxx) — ทุกตัวเป็นคำสั่งยิงทิ้ง ไม่รอคำตอบ
//    สถานะวงเดินทางมาทาง push `Bandstand`(63459083) ตัวเดียว รับด้วย global
//    Connections.Frontend.On (client/ArtifactManager.cs:61-68 → artifactState.Bandstand)
//    เซิร์ฟเราไม่มีอะไรรองรับ Bandstand เลยสักบรรทัด (grep "Bandstand" ทั้ง server/ นอก
//    GameCode/Messages/ = 0 ผลลัพธ์) ⇒ ทำจริงไม่ได้
//
//  ═══ ทำไมต้องตอบ Abort ไม่ใช่เงียบ ═══
//  ข้อความยิงทิ้งที่ถูกตอบกลับมาโดยไม่มีใครจอง seq จะตกไปที่ตัวรับกลาง
//  DefaultAbortHandler (client/GameManager.cs:309-312) → UIManager.SystemMsg(Text) 4 วินาที
//  ⇒ ผู้เล่นได้รู้ว่า "กดแล้วไม่เกิดอะไรเพราะระบบยังไม่เปิด" แทนที่จะกดค้างงง ๆ
//  ⚠️ ห้ามส่ง default(Abort) เด็ดขาด — Text เป็น null แล้วฝั่งเกมแครช
//     (nil → UnpackGettextFromMsgPack คืน null → LimitText(null).Length → NRE)
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    // ⚠️ ยังไม่มีใครเรียกเมธอดนี้ — ตราบใดที่ยังไม่ต่อสาย ทั้ง 11 ตัวข้างล่างไม่ทำงานเลย
    //    คนต่อสายให้เพิ่มบรรทัดนี้ใน server/Core/Player.Systems.cs → RegisterSystemHandlers()
    //    (ต่อท้ายกลุ่ม RegisterSocialHandlers(); ราวบรรทัด 44 — ก่อน ReviveIfDeadOnLogin();)
    //        RegisterMusicHandlers();       // Player.Music.cs     — โน้ตเพลงที่แชร์ + คอนเสิร์ต
    //    ลำดับปลอดภัย: RegisterSystemHandlers() ถูกเรียกที่ Player.cs:550 คือ "หลัง" ชุด Recv
    //    ของ Player.cs (บรรทัด 100-500) แต่ TypeCode ทั้ง 11 ตัวนี้ไม่ชนกับของเดิมสักตัว
    //    (GetMusics 47852453 / SaveMusicToSlot / RemoveMusicFromSlot / PlayMusic 3802 /
    //     StopMusic 3803 / TurnOnMusic / TurnOffMusic เป็นคนละ TypeCode ทั้งหมด) ⇒ ไม่ทับใคร
    private void RegisterMusicHandlers()
    {
        // ───────────────────────── ก้อนที่ 1 : โน้ตเพลง ─────────────────────────

        // GetMusic (47852452) — ขอโน้ตของ "ช่องเดียว" (ตอนเปิดแก้ไขเพลง/ดูตัวอย่าง)
        // จุดยิง: client/MusicManager.cs:558-570 GetMusic(int id, callback)
        //   → Send(new GetMusic { Slot = id })  ← สังเกตว่าเซ็ตแต่ Slot ฟิลด์ EntityId ปล่อยเป็น null
        //     (GetMusic.Pack: EntityId==null → PackNull ⇒ ฝั่งเราถอดออกมาได้ null กลับมาเป๊ะ —
        //      server/GameCode/Messages/GetMusic.cs:39-54 Unpack เช็ค IsNil ก่อน AsString)
        //   → .On(delegate(Messages.Music music …) callback(music))
        //   → .Rest(callback(null))  ⇒ ตอบชนิดอื่น = ถือว่า "ไม่มีโน้ตนี้"
        // ⇒ ตัวนี้ทำ **ของจริง** ได้ เพราะ _context.Musics มีอยู่แล้ว (Player.cs:1631-1642
        //   SaveMusicToSlot เขียนลงไป) — ไม่ต้องแต่งอะไรเลย
        _connection.Recv(delegate(GetMusic msg, PacketHeader header)
        {
            // **การตีความของเรา**: EntityId ที่ว่าง = "ของตัวเอง" (ฝั่งเกมส่งมาแบบนี้เสมอ)
            // ถ้าเป็น id คนอื่น เราไม่มีทางอ่านช่องเพลงของผู้เล่นคนอื่น ⇒ ปฏิเสธตรง ๆ
            if (!string.IsNullOrEmpty(msg.EntityId) && msg.EntityId != EntityId)
            {
                Send(new Abort { Text = "อ่านโน้ตเพลงของผู้เล่นคนอื่นไม่ได้" }, header.Seq);
                return;
            }

            Dictionary<int, Music> musics = _context.Musics;
            if (musics != null && musics.TryGetValue(msg.Slot, out var music))
            {
                Send(music, header.Seq);
                return;
            }

            // ไม่มีโน้ตในช่องนี้ — ฝั่งเกมจะเข้า .Rest แล้วเรียก callback(null) เอง
            Send(new Abort { Text = "ไม่พบโน้ตเพลงในช่องนี้" }, header.Seq);
        });

        // GetSharedMusic (47852457) — ขอ "โน้ตที่ถูกแชร์" ตาม SheetId พร้อมจำนวนคนที่เอาไปใช้
        // จุดยิง: client/MusicManager.cs:357-368 (ใน AsyncCachedDictionary อายุแคช 60 วิ)
        //   → .On(delegate(SharedMusic music …) result(key, music))
        //   → .Rest(result(key, default(SharedMusic)))
        // ผู้ใช้ผล: client/Durango.UI/MusicNodeWidget.cs:82-86 ใช้แค่ m.RefCount ไปต่อท้ายชื่อ
        // ⇒ เป็น "คำขอข้อมูล" ที่ยิงซ้ำเองทุก 60 วิ **ห้ามตอบ Abort** ไม่งั้นเด้ง toast รัว
        //   ตอบโครงว่างที่ถูกชนิด: RefCount=0 + Music เปล่า ซึ่งได้ผลปลายทางเท่ากับ .Rest เป๊ะ
        //   (Music.Pack: Name==null → PackString("") จึงไม่ทำให้ฝั่งเกมพัง — Messages/Music.cs:24-31)
        _connection.Recv(delegate(GetSharedMusic msg, PacketHeader header)
        {
            Send(new SharedMusic
            {
                RefCount = 0,
                Music = new Music
                {
                    Name = string.Empty,
                    Data = Array.Empty<byte>(),
                    Duration = 0f,
                    Publisher = null
                }
            }, header.Seq);
        });

        // PublishMusic (47852557) — "แชร์โน้ต" เอาเพลงในช่อง Slot ออกเป็นโน้ตสาธารณะ
        // จุดยิง: client/MusicManager.cs:673-691 → .On<SharedSheet>(ได้ SheetId) / .Rest(result(null))
        // ผู้ใช้ผล: client/Durango.UI/MusicEditorGroup.cs:393-399 — ถ้าไม่มีค่าก็ไม่ทำอะไรต่อ
        // ⇒ เซิร์ฟไม่มีคลังโน้ตสาธารณะ (ไม่มีที่เก็บ SheetId → Music) จะปั้น SheetId ปลอมส่งไป
        //   ก็ได้แต่ผู้เล่นจะโดนหลอกว่า "แชร์สำเร็จ" ทั้งที่ไม่มีใครโหลดได้ ⇒ ปฏิเสธตรงไปตรงมา
        _connection.Recv(delegate(PublishMusic msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานการแชร์โน้ตเพลง" }, header.Seq);
        });

        // ChangeFollowMusic (47852459) — กด "นำเข้า/ลบ" โน้ตที่คนอื่นแชร์ (WantFollow true/false)
        // จุดยิง: client/MusicManager.cs:658-671 → .All(result(Packet.IsSuccess(p)))
        //   Packet.IsSuccess คืน false เมื่อ TypeCode 1022/1024/3650 (client/Durango.Network/Packet.cs:90-101)
        //   Abort คือ 1024 ⇒ ฝั่งเกมได้ ok=false ถูกต้อง
        // ผู้ใช้ผล: MusicEditorGroup.cs:317-323 (ลบไม่สำเร็จ → Refresh คืนรายการเดิม)
        //           MusicEditorGroup.cs:530-536 (นำเข้าไม่สำเร็จ → ไม่ขึ้น "บันทึกแล้ว")
        // ⇒ ไม่มีคลังโน้ตสาธารณะให้ติดตาม ⇒ Abort พร้อมข้อความ
        _connection.Recv(delegate(ChangeFollowMusic msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานการแชร์โน้ตเพลง" }, header.Seq);
        });

        // PlaySharedMusic (47852451) — เล่นโน้ตที่แชร์ด้วยเครื่องดนตรีชิ้นหนึ่ง
        // จุดยิง: client/MusicManager.cs:544-550 (ยิงทิ้ง ไม่มี .On) — คู่แฝดของ PlayMusic(3802)
        // ทางที่ถูกคือเซิร์ฟกระจาย Musician(815) ให้คนรอบข้าง (เทียบ server/Core/Player.cs:1659-1686
        // HandlePlayMusicMsg ที่ทำแบบนั้นกับ Slot ของตัวเอง) แต่ต้องเปิดโน้ตจาก SharedSheetId ให้ได้ก่อน
        // ⇒ ไม่มีคลังโน้ตสาธารณะ = แปลง SharedSheetId เป็น Music ไม่ได้ ⇒ Abort พร้อมข้อความ
        //   (ห้ามกระจาย Musician ที่ Music ว่าง เพราะคนรอบข้างจะเห็นตัวละครเล่นดนตรีแบบไม่มีเสียง)
        _connection.Recv(delegate(PlaySharedMusic msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานการเล่นโน้ตที่แชร์" }, header.Seq);
        });

        // ───────────────────────── ก้อนที่ 2 : คอนเสิร์ต ─────────────────────────
        //  ทั้ง 6 ตัวข้างล่างยิงทิ้งหมด ไม่มี .On สักตัว — ฝั่งเกมรอ push `Bandstand` อย่างเดียว
        //  (client/ArtifactManager.cs:61-68) เซิร์ฟเรายังไม่มีสถานะเวที/วง ⇒ ตอบ Abort ทุกตัว
        //  ผลคือผู้เล่นเห็น toast อธิบาย แทนที่จะกดแล้วหน้าต่างค้างว่าง (ConcertPopup ไม่ยอมโผล่
        //  ถ้า _concert เป็น null — client/Durango.UI.Popup/ConcertPopup.cs:115-118 IsShowable)

        // HostConcert (63459079) — "ตั้งวง" ที่เวที (ประตูทางเข้าของทั้งระบบ)
        // จุดยิง: client/Durango.UI/MusicEditorGroup.cs:116-125 (Interaction.HostConcert = 552)
        //         → client/MusicManager.cs:711-718
        _connection.Recv(delegate(HostConcert msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบคอนเสิร์ต" }, header.Seq);
        });

        // RegisterConcert (63459082) — เข้าร่วม/ถอนตัวจากวง (Order+InstrumentItemId มีค่า = เข้า,
        // เป็น null ทั้งคู่ = ถอนตัว — client/MusicManager.cs:720-738 RegisterConcert/UnregisterConcert)
        // จุดยิง: client/Durango.UI.Popup/ConcertPopup.cs:276, 292, 324
        _connection.Recv(delegate(RegisterConcert msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบคอนเสิร์ต" }, header.Seq);
        });

        // SetConcertMusic (63459080) — หัวหน้าวงเลือกโน้ตของตัวเอง (Slot) ให้ราง Order
        // (Slot เป็น null = ล้างโน้ตออกจากราง — client/MusicManager.cs:766-775 ClearConcertMusic)
        // จุดยิง: client/Durango.UI.Popup/ConcertPopup.cs:375 → client/MusicManager.cs:740-753
        _connection.Recv(delegate(SetConcertMusic msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบคอนเสิร์ต" }, header.Seq);
        });

        // SetSharedConcertMusic (63459180) — เหมือนตัวบน แต่เลือกโน้ตที่ "แชร์" (SharedSheetId)
        // จุดยิง: client/MusicManager.cs:753-765 (สาขา else ของ SetConcertMusic)
        _connection.Recv(delegate(SetSharedConcertMusic msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบคอนเสิร์ต" }, header.Seq);
        });

        // PlayConcert (63459081) — หัวหน้าวงกด "เริ่มบรรเลง"
        // จุดยิง: client/Durango.UI.Popup/ConcertPopup.cs:110 → client/MusicManager.cs:693-700
        _connection.Recv(delegate(PlayConcert msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบคอนเสิร์ต" }, header.Seq);
        });

        // FinishConcert (63459101) — หัวหน้าวงกด "เลิกรวบรวมวง"
        // จุดยิง: client/Durango.UI.Popup/ConcertPopup.cs:80 → client/MusicManager.cs:702-709
        _connection.Recv(delegate(FinishConcert msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบคอนเสิร์ต" }, header.Seq);
        });
    }
}
