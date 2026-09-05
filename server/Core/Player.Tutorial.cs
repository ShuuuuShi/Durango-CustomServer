using System;
using Durango.Network;
using Durango.Utils;
using Messages;
using Yaml;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  บทเรียนเริ่มเกม (PlayGuide) — คำสั่งที่บทไกด์สั่งให้เซิร์ฟจัดฉากให้
//
//  ═══ ทำไมต้องมี ═══
//  บทไกด์ฝั่งเกมเป็นสคริปต์ที่สั่งงานเป็นขั้น ๆ และบางขั้น **สั่งให้เซิร์ฟเปลี่ยนสถานะผู้เล่น**
//  เพื่อให้ฉากถัดไปเล่นต่อได้ (client/Durango.Logic.PlayGuide/CustomCommand.cs:63-66
//  ลงทะเบียนคำสั่ง Ancora_Event_* ซึ่งยิง TutorialEvent(701) ขึ้นมา)
//
//  ⚠️ เซิร์ฟไม่รับ message นี้ ⇒ สถานะไม่เคยเปลี่ยนตามที่บทไกด์รอ ⇒ **ขั้นนั้นไม่จบ**
//  บทสนทนาเลยค้าง/วนซ้ำบังจออยู่ทุกครั้งที่เข้าเกม (เจอกับตาตอนเทส กล่องคุยของ "เคย์"
//  โผล่ทับจอทุกครั้งที่ต่อใหม่ ต้องกด "ถัดไป" รัวถึงจะหาย)
//
//  ═══ ทำไมไม่ตอบอะไรกลับ ═══
//  ฝั่งเกมยิงแล้วลืม ไม่มี .On() ตามหลัง (CustomCommand.cs:543-570) ⇒ ตอบไปก็ไม่มีใครรับ
//  สิ่งที่มันรอคือ **ผลของการเปลี่ยนหลอด** ซึ่งเดินทางกลับด้วย SurvivalUpdated ตามปกติอยู่แล้ว
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    private void RegisterTutorialHandlers()
    {
        _connection.Recv(delegate(TutorialEvent msg, PacketHeader header)
        {
            HandleTutorialEventMsg(msg);
        });
    }

    /// <summary>
    /// ชื่อเหตุการณ์ทั้งหมดที่บทไกด์ส่งมา — มีแค่ 4 ตัว ไล่จาก
    /// <c>grep 'Event = "' client/Durango.Logic.PlayGuide/CustomCommand.cs</c>
    /// </summary>
    private void HandleTutorialEventMsg(TutorialEvent msg)
    {
        switch (msg.Event)
        {
            // เริ่มบท: ตั้งหลอดให้เต็มก่อน จะได้เล่นฉากต่อไปได้จากสภาพสมบูรณ์
            case "init_health":
                RestoreAllGaugesFull();
                break;

            // ฉาก "ฟื้นขึ้นมา" — ต้องมีชีวิตจริง ๆ ไม่งั้นบทถัดไปเล่นไม่ได้
            case "resurrect":
                if (!_context.AppearPlayer.IsAlive) HandleReviveMsg(normal: false);
                else RestoreAllGaugesFull();
                break;

            // ฉาก "ได้กินอาหาร" — เติมหลอดอิ่ม/แรงให้เต็ม
            case "restore_food_k":
                SetGaugeFull(SurvivalState.KeyEnergy);
                SetGaugeFull(SurvivalState.KeyStamina);
                break;

            // ฉาก "เหนื่อยล้า" — ดันความเหนื่อยขึ้นถึงขั้นเตือน ให้ผู้เล่นเห็นว่าหลอดนี้มีผลจริง
            case "set_tired":
                _survival.Set(SurvivalState.KeyFatigue, SurvivalTuning.FatigueCaution);
                FlushSurvival();
                break;

            default:
                Console.WriteLine($"[บทเรียน] ไม่รู้จักเหตุการณ์ '{msg.Event}' — ไม่ทำอะไร");
                return;
        }

        Console.WriteLine($"[บทเรียน] {EntityId[..Math.Min(8, EntityId.Length)]} → {msg.Event}");
        OnContextChanged();
    }

    /// <summary>เติมหลอดที่บทไกด์สนใจให้เต็ม (ใช้ค่าสูงสุดจริงจาก entity_types/players.json)</summary>
    private void RestoreAllGaugesFull()
    {
        SetGaugeFull(SurvivalState.KeyHealth);   // ต้องก่อน life เพราะ health เป็นเพดานของ life
        SetGaugeFull(SurvivalState.KeyLife);
        SetGaugeFull(SurvivalState.KeyStamina);
        SetGaugeFull(SurvivalState.KeyEnergy);
        _survival.Set(SurvivalState.KeyFatigue, 0f);
        FlushSurvival();
    }

    private void SetGaugeFull(string key)
    {
        float max = GaugeMaxOf(PlayerTypes.Player?.Survival, key);
        if (max > 0f) _survival.Set(key, max);
    }
}
