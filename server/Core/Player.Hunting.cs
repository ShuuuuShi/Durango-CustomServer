using System;
using System.Collections.Generic;
using Durango.Network;
using Durango.Utils;
using Messages;
using Shared.Animal;   // AnimalStatus — สถานะที่ส่งไปกับ CombatInteraction
using Shared.Battle;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  ล่าสัตว์ — ต่อสายจากระบบต่อสู้ (Player.Combat.cs) เข้าหาสัตว์ป่า (Core/AnimalManager.cs)
//
//  ระบบต่อสู้เขียนไว้ครบแล้วแต่หาเป้าเจอแค่ "ผู้เล่นคนอื่นบนเกาะเดียวกัน" เพราะตอนนั้น
//  เซิร์ฟยังไม่มีสัตว์เลยสักตัว ⇒ ไฟล์นี้คือชิ้นส่วนที่ขาด: เอา entity id ที่ผู้เล่นเล็ง
//  ไปหาในบัญชีสัตว์ของเกาะ แล้วเดินสายความเสียหายชุดเดียวกัน
//
//  ทำไมแยกไฟล์: Player.Combat.cs กับ Player.Animals.cs มีเจ้าของอยู่แล้ว การแตะไฟล์
//  ของคนอื่นระหว่างทำงานขนานกันคือทางลัดสู่การแก้ทับกัน — ไฟล์นี้จึงถือเฉพาะ "รอยต่อ"
//
//  ลำดับข้อความฝั่งเกม (เหมือนตีผู้เล่นทุกประการ — client ไม่ได้แยกว่าเป้าเป็นอะไร):
//    UseBattleAction(3440) → server หักความอึด → Damaged(12) broadcast
//                          → เลือดสัตว์หมด → EntityDied(119) broadcast
//  client/ObjectManager.cs:153-161 เอา EntityDied ไปเรียก SetAlive(false) ให้ทุก entity
//  ไม่ว่าจะเป็นผู้เล่นหรือสัตว์ ⇒ ใช้ทางเดียวกันได้เลย ไม่ต้องมี message เฉพาะของสัตว์
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    /// <summary>
    /// ตายอยู่ตอนเข้าเกม ⇒ ให้ฟื้นทันที — กันตัวละครค้างตายถาวร
    ///
    /// สถานะตายถูกเซฟลงไฟล์ (<c>appear_player.IsAlive = false</c>) ⇒ ตายแล้วปิดเกม
    /// เปิดใหม่ก็ยังตายอยู่ และทางเดียวที่จะฟื้นคือกดปุ่มบนหน้าจอตาย ซึ่งถ้าด้วยเหตุใดก็ตาม
    /// หน้าจอนั้นไม่ขึ้น (เข้าเกมมาแล้วเป็นศพเดินได้) ตัวละครจะติดถาวรโดยไม่มีทางแก้ในเกม
    /// ⇒ เข้ามาเมื่อไรก็ฟื้นให้เลย ตามกติกาบทลงโทษเดิมทุกอย่าง (หลอดลดตามจำนวนครั้งที่ตาย)
    ///
    /// ใช้ <c>HandleReviveMsg</c> ตัวเดียวกับตอนผู้เล่นกดฟื้นเอง จะได้ไม่มีตรรกะสองชุด
    /// เรียกจาก <c>RegisterSystemHandlers</c> (constructor) — ตอนนั้น client ยังไม่ subscribe
    /// แต่ไม่เป็นไร เพราะสิ่งที่สำคัญคือค่าใน <c>AppearPlayer</c> ซึ่งถูกส่งไปกับตัวละครอยู่แล้ว
    /// </summary>
    private void ReviveIfDeadOnLogin()
    {
        if (_context.AppearPlayer.IsAlive) return;
        Console.WriteLine($"[combat] {EntityId[..Math.Min(8, EntityId.Length)]} เข้าเกมมาในสภาพตาย — ฟื้นให้ที่จุดเข้าเกาะ");
        HandleReviveMsg(normal: true);
    }

    /// <summary>สัตว์ที่ผู้เล่นคนนี้ "เห็นอยู่ตอนนี้" — กันส่ง AppearAnimal ซ้ำทุกรอบ</summary>
    private readonly HashSet<string> _animalSet = new(StringComparer.Ordinal);

    private double _nextAnimalSyncAt;

    /// <summary>
    /// ส่งสัตว์ที่อยู่ในระยะมองให้ผู้เล่น และลืมตัวที่ออกนอกระยะไปแล้ว
    ///
    /// ทำไมต้องคอยส่งซ้ำ ไม่ใช่ส่งครั้งเดียวตอนเข้าเกม: **ตัวเกมทำลายอ็อบเจกต์สัตว์ที่อยู่ไกลทิ้งเอง**
    /// (client/AnimalManager.cs:171-175 Animal_Destroyed ลบออกจาก _animals) ⇒ เดินออกไปแล้ว
    /// เดินกลับมา สัตว์หายถาวรถ้าเซิร์ฟไม่ส่งใหม่ — ยืนยันในเกมจริงแล้ว (เดินไกลแล้ว สัตว์เหลือ 0 ตัว)
    ///
    /// ส่งซ้ำได้ปลอดภัย: ฝั่งเกมเช็คก่อนว่ามีตัวนี้อยู่แล้วไหม ถ้ามีก็แค่เรียก Appear() ไม่ได้สร้างซ้ำ
    /// (client/AnimalManager.cs:23-37) ⇒ ไม่ต้องมีข้อความ "สัตว์หายไป" แยก
    ///
    /// ระยะที่ใช้คือกรอบ 3×3 chunk รอบตัว — กรอบเดียวกับที่สิ่งปลูกสร้างใช้ (Player.IsOverlapped)
    /// เรียกจาก World.Process() ไม่ใช่ Player.Process() เพราะไฟล์ Player.cs มีเจ้าของอยู่
    /// </summary>
    /// <summary>
    /// [7 ก.ย. 2026] ลืมว่าเคยส่งสัตว์ตัวนี้ให้ผู้เล่นคนนี้แล้ว
    /// ⇒ รอบถัดไปของ <see cref="SyncAnimalVisibility"/> จะส่ง AppearAnimal ตัวใหม่ให้
    /// (ใช้ตอนซากหายแล้วสัตว์เกิดใหม่ — ดู World.OnCorpseDisposed)
    /// </summary>
    public void ForgetAnimal(string entityId) => _animalSet.Remove(entityId);

    public void SyncAnimalVisibility()
    {
        AnimalManager manager = _world.AnimalManager;
        if (manager == null || manager.Count == 0) return;

        // ไม่ต้องทำทุกเฟรม — ตำแหน่งเปลี่ยนช้ากว่านั้นมาก (เซิร์ฟเดิน ~62 รอบ/วินาที)
        double now = Gauge.CurrentTime;
        if (now < _nextAnimalSyncAt) return;
        _nextAnimalSyncAt = now + AnimalSyncIntervalSeconds;

        int minX = (_centerX - 1) * 16;
        int maxX = (_centerX + 2) * 16;
        int minY = (_centerY - 1) * 16;
        int maxY = (_centerY + 2) * 16;

        foreach (AnimalManager.Animal animal in manager.All)
        {
            bool inRange = animal.Tile.x >= minX && animal.Tile.x < maxX
                        && animal.Tile.y >= minY && animal.Tile.y < maxY;

            if (inRange)
            {
                if (_animalSet.Add(animal.EntityId)) Send(animal.ToMessage());
            }
            else
            {
                // ออกนอกระยะ ⇒ ลืมไว้ก่อน เดี๋ยวกลับเข้ามาค่อยส่งใหม่ (ตัวเกมทำลายทิ้งเองอยู่แล้ว)
                _animalSet.Remove(animal.EntityId);
                continue;
            }

            if (animal.IsAlive) AnimalTurn(animal, now);
        }
    }

    /// <summary>
    /// ตาของสัตว์ตัวหนึ่ง — ตัดสินใจว่าจะกัดผู้เล่นคนนี้ไหม
    ///
    /// ═══ ทำไมต้องอยู่ฝั่งเซิร์ฟ ═══
    /// **การเดินเล่นของสัตว์เป็นของฝั่งเกม** — <c>client/ClientAnimalActor.cs</c> เป็น component
    /// บนตัวโมเดล ที่เดินสุ่มรอบจุดเกิดเองทุกเฟรมโดยไม่ต้องถามเซิร์ฟ (มี <c>_wanderRadius</c>
    /// กับตารางท่าทางของมันเอง) ⇒ **เซิร์ฟไม่ต้องส่ง Move ให้สัตว์ ห้ามส่งด้วย จะไปสู้กับมัน**
    ///
    /// แต่ **การต่อสู้เป็นของฝั่งเซิร์ฟ** — ฝั่งเกมไม่เคยอ่าน <c>ai_factor_id</c> ใน animal.json เลย
    /// สักที่เดียว (เช็คแล้วทั้งซอร์ส) และรับผลเป็น <c>Damaged</c> อย่างเดียว
    /// ⇒ ตรรกะไล่กัดอยู่บนเซิร์ฟจริงของ NEXON ซึ่งไม่มีซอร์ส ต้องเขียนเอง
    ///
    /// กติกาที่ใช้ (อิงฟิลด์จริงในข้อมูล ไม่ได้ตั้งลอย ๆ):
    /// • <c>type</c> = Carnivore/Scavenger → ไล่กัดคนที่เข้ามาใกล้เอง
    /// • <c>type</c> = Herbivore → กัดเฉพาะคนที่ตีมันก่อน (ตั้ง AggroTargetId ตอนโดนตี)
    /// • จังหวะการตีจาก <c>attack_cooltime</c> · ความแรงจากสูตร <c>attack</c> ของชนิดนั้น
    /// </summary>
    private void AnimalTurn(AnimalManager.Animal animal, double now)
    {
        AnimalTypes.Info info = AnimalTypes.Get(animal.EntityType);
        if (info == null) return;
        if (!_context.AppearPlayer.IsAlive) return;             // ตายแล้วไม่ต้องรุมซ้ำ

        bool hunting = animal.AggroTargetId == EntityId;
        if (!hunting && !info.IsAggressive) return;             // สัตว์กินพืชไม่แตะคนก่อน

        // นอกระยะเห็นเหยื่อ = ไม่สนใจ
        if (!IsWithinTiles(animal.Tile, AnimalAggroTiles))
        {
            // [7 ก.ย. 2026] หนีไปไกลนานพอแล้ว ⇒ เลิกโกรธ
            //
            // ⚠️ เดิม AggroTargetId ถูกตั้งแล้ว **ไม่มีจุดไหนเคลียร์เลยสักที่** (grep ทั้งเซิร์ฟ)
            // และ AnimalManager.Process ข้ามการเดินเล่นตลอดถ้ายังมี aggro
            // ⇒ ปล่อยไว้นาน ๆ สัตว์ทั้งเกาะค้างท่าเตรียมสู้แล้วยืนนิ่งหมด
            if (hunting && animal.AggroSeenAt > 0.0 && now - animal.AggroSeenAt > AggroForgetSeconds)
            {
                animal.AggroTargetId = null;
                animal.AggroSeenAt = 0.0;
                _world.BroadCast(animal.ToMotionMessage());                      // กลับท่ายืนปกติ
                _world.BroadCast(CombatStatus(animal, AnimalStatus.Peace, lookAt: false));
                LeaveBattleWith(animal.EntityId);   // มันเลิกไล่แล้ว — ผู้เล่นออกจากโหมดสู้ด้วย
            }
            return;
        }
        animal.AggroSeenAt = now;

        if (string.IsNullOrEmpty(animal.AggroTargetId))
        {
            animal.AggroTargetId = EntityId;
            _world.BroadCast(animal.ToMotionMessage());   // เข้าโหมดสู้ — เปลี่ยนท่ายืน
            // บอกฝั่งเกมว่าเข้าโหมดสู้แล้ว — ไอคอนสถานะบนหัว + หันหน้ามองเป้า
            _world.BroadCast(CombatStatus(animal, AnimalStatus.Battle, lookAt: true));
        }
        animal.AggroTargetId = EntityId;

        // [7 ก.ย. 2026] **ตัวละครต้องเข้าโหมดต่อสู้ด้วย**
        //
        // ⚠️ เดิมสัตว์ไล่กัดยังไงผู้เล่นก็ไม่เข้าโหมดสู้เลย เพราะ SetBattleMode ถูกเรียก
        // แค่ตอน "ผู้เล่นกดโจมตีเอง" (UseBattleAction) กับ "โดนผู้เล่นอื่นตี" (ReceiveAttack)
        // — สัตว์ป่าไม่เคยผ่านสองทางนั้น
        // ⇒ ฝั่งเกมไม่เคยได้ BattleBegun ที่ EntityId เป็นของตัวเอง (client/CombatSystem.cs:510-522)
        //   ผลคือไม่ชักอาวุธ · ไม่มีปุ่มท่าต่อสู้ · ความเร็วเดินยังเป็นโหมดปกติ
        EnterBattleWith(animal.EntityId);

        // [7 ก.ย. 2026] เห็นเหยื่อแล้วต้อง "เดินเข้าไปหา" ก่อนกัด
        //
        // ⚠️ เดิมกัดได้ทันทีตั้งแต่ระยะ 4 ช่อง โดยไม่ขยับเลย ⇒ บนจอเห็นไดโนยืนนิ่งอยู่ไกล ๆ
        // แล้วเลือดผู้เล่นลดเอง (อาการที่ผู้เล่นแจ้ง) — ของจริงสัตว์ต้องวิ่งเข้ามาประชิดก่อน
        if (!IsWithinTiles(animal.Tile, AnimalAttackTiles))
        {
            // กำลังเดินอยู่ = ปล่อยให้เดินให้จบก่อน ไม่สั่งเส้นทางใหม่ทับ (ตัวจะกระตุก)
            if (animal.StopWalkingAt > now) return;

            WorldPosition target = PlayerWorldPosition();
            if (target.x == 0f && target.y == 0f) return;      // ยังไม่รู้ตำแหน่งผู้เล่น

            Move chase = _world.AnimalManager?.BuildChase(animal, target, AttackStopDistance, now)
                         ?? default;
            if (chase.Movements != null)
            {
                _world.BroadCast(chase);
                Console.WriteLine($"[ล่าสัตว์] {info.Name} วิ่งเข้าหา {Short(EntityId)} " +
                                  $"→ [{animal.Tile.x},{animal.Tile.y}]");
            }
            return;                                       // รอบนี้เดิน รอบหน้าค่อยกัด
        }

        if (now < animal.NextAttackAt) return;
        animal.NextAttackAt = now + Math.Max(0.5f, info.AttackCooltime);

        // [7 ก.ย. 2026] ส่งท่าโจมตีให้เห็นบนจอ — เดิมส่งแต่ Damaged ⇒ สัตว์ยืนนิ่งแต่เลือดลด
        //
        // ⚠️ ต้องหันหน้าเข้าหาเหยื่อด้วย ไม่งั้นตัวค้างหันไปทางที่เดินมาล่าสุด = ดูเหมือนกัดลม
        // และต้องนัดกลับท่ายืน เพราะคลิปโจมตีลาก root bone ไปข้างหน้า ถ้าไม่ดึงกลับ
        // ตัวจะค้างหน้าตำแหน่งจริงแล้ว packet ถัดไปกระชากกลับ = เห็นเป็นวาร์ป
        Move attackMotion = animal.ToAttackMotionMessage(PlayerWorldPosition(), now, AttackMotionRng);
        if (attackMotion.Movements != null)
        {
            _world.BroadCast(attackMotion);
            animal.StandAt = now + AnimalManager.Animal.AttackClipSeconds;
        }

        // วงแหวนเตือน "กำลังจะฟาด" ก่อนดาเมจเข้า
        // client/ObjectManager.cs:138 หารด้วย 1000 เอง ⇒ ต้องส่งเป็น **มิลลิวินาที**
        _world.BroadCast(CombatStatus(animal, AnimalStatus.Battle, lookAt: true, noticeAttack: true));

        // ป้องกันของผู้เล่น — ใช้ค่า Derived หลังรวมสกิล (players.json → player.defense ฐานเป็น 0)
        // [7 ก.ย. 2026] แล้วคูณตัวลดดาเมจจากสกิลหมวดป้องกัน (ดู Player.SkillEffects.cs)
        // ⚠️ เดิมใช้ค่าฐานดิบอย่างเดียว ⇒ เรียนสกิลป้องกันไปก็โดนสัตว์กัดเจ็บเท่าเดิม
        float value = Math.Max(CombatTuning.MinDamage,
                               (float)Math.Round((animal.Attack - CurrentDerivedDefense())
                                                 * DamageTakenScale()));

        _world.BroadCast(new Damaged
        {
            VictimId = EntityId,
            AttackerId = animal.EntityId,
            EventAt = Times.UnixTimeNow(),
            Damage = new Damage
            {
                Result = DamageResult.Hit,
                Value = (int)value,
                Part = BodyPart.Body,
                Direction = CombatTuning.HitDirection,
                AttackType = BodyAttackTypeOf(info),
                // [7 ก.ย. 2026] เดิมเป็น None เสมอ ⇒ ฝั่งเกมไม่เล่นท่าเจ็บ/กระเด็นให้เลย
                // (client/Durango.Logic.Combat/DamagedProcesser.cs อ่าน flag ชุดนี้)
                Effects = HitEffectOf(info)
            }
        });

        _survival.Add(SurvivalState.KeyLife, -value);
        FlushSurvival();

        // ⚠️ ต้องเทียบกับค่ามากกว่า 0 นิดหนึ่ง: หลอดเลือดมีความชันบวก (ฟื้นเอง) ⇒ พออ่านค่า
        // อีกเสี้ยววินาทีถัดมามันไต่ขึ้นพ้น 0 แล้ว ทำให้เช็ค "<= 0" ไม่เคยจริงเลยแม้เลือดจะหมด
        // (เจอของจริง: หมาป่าตีจนเลือดเหลือ 0 บนจอ แต่เซิร์ฟไม่เคยเรียก Die)
        if (_survival.ValueAt(SurvivalState.KeyLife, now) <= DeadLifeThreshold)
        {
            Console.WriteLine($"[ล่าสัตว์] {info.Name} lv{animal.CombatLevel} ฆ่า " +
                              $"{EntityId[..Math.Min(8, EntityId.Length)]}");
            Die();
        }
        OnContextChanged();
    }

    /// <summary>
    /// **ค่าของเรา** — สัตว์กินเนื้อเริ่มไล่กัดเมื่อผู้เล่นเข้ามาใกล้กี่ช่อง
    ///
    /// ข้อมูลเกมมี <c>bound_radius</c> (200-400) กับ <c>herd_collide_distance</c> แต่ทั้งคู่เป็น
    /// ขนาดตัว/ระยะเบียดกันของฝูง ไม่ใช่ระยะเห็นเหยื่อ — ระยะไล่ล่าอยู่ใน ai_factor ซึ่งไม่มีในข้อมูล
    /// 4 ช่อง ≈ ระยะที่ผู้เล่นเห็นตัวสัตว์เต็ม ๆ บนจอ และใกล้เคียงระยะเก็บของ (5 ช่อง)
    /// </summary>
    private const int AnimalAggroTiles = 4;

    /// <summary>
    /// **ค่าของเรา** — ต้องเข้ามาใกล้กี่ช่องถึงจะกัดได้จริง
    ///
    /// 4 ช่องของ <see cref="AnimalAggroTiles"/> คือ "ระยะเห็นเหยื่อแล้วเริ่มไล่"
    /// ส่วนระยะกัดต้องประชิดกว่านั้นมาก ไม่งั้นเห็นสัตว์ตีข้ามจอ
    /// 1 ช่อง = 200 หน่วย ≈ ระยะที่ตัวสัตว์กับผู้เล่นเกือบชนกันบนจอ
    /// </summary>
    private const int AnimalAttackTiles = 1;

    /// <summary>**ค่าของเรา** — สัตว์หยุดห่างจากผู้เล่นกี่หน่วยตอนวิ่งเข้าหา (ไม่ให้เดินทับตัว)</summary>
    private const float AttackStopDistance = 150f;

    /// <summary>ตัวสุ่มท่าโจมตี — main loop เส้นเดียว ไม่ต้องล็อก</summary>
    private static readonly Random AttackMotionRng = new();

    /// <summary>ตำแหน่งผู้เล่นในพิกัดโลก — ใช้เป็นปลายทางตอนสัตว์วิ่งเข้าหา</summary>
    private WorldPosition PlayerWorldPosition()
    {
        Movement[] movements = _context.AppearPlayer.Move.Movements;
        if (movements == null || movements.Length == 0 ||
            movements[0].Path == null || movements[0].Path.Length == 0)
        {
            return default;
        }
        return movements[0].Path[0].Position;
    }

    /// <summary>เลือดต่ำกว่านี้ถือว่าตาย — เผื่อความชันของหลอดที่ไต่ขึ้นระหว่างอ่านค่า</summary>
    private const float DeadLifeThreshold = 1f;

    /// <summary>
    /// เอฟเฟกต์ตอนสัตว์กัดโดน — เลือกจากขนาดตัว
    ///
    /// ฝั่งเกมเลือกเอฟเฟกต์จาก <c>Damage.AttackType</c> ตรง ๆ
    /// (client/Durango.Render.Effect/DamageEffectManager.cs:138 <c>AttackedEffects[(int)type]</c>)
    /// ⇒ ส่ง BareHands จะได้เอฟเฟกต์ "หมัดคน" ตอนไดโนเสาร์กัด ซึ่งผิดแน่
    ///
    /// **การตีความของเรา**: ในชนิดโจมตีทั้งหมดมีแค่ SmallBody/LargeBody ที่เข้ากับสัตว์
    /// แต่ซอร์สฝั่งเกมไม่มีที่ไหนบอกว่าสัตว์ตัวไหนใช้ตัวไหน (grep แล้วเจอแค่ในนิยาม enum)
    /// ⇒ แบ่งด้วย <c>size_level</c> ซึ่งเป็นฟิลด์ขนาดเดียวที่มีในข้อมูล (ค่าจริง 1-7)
    /// ที่ 4 ขึ้นไปนับว่าตัวใหญ่ — เป็นจุดกึ่งกลางของช่วง ไม่ได้มาจากไฟล์
    /// </summary>
    private static AttackType BodyAttackTypeOf(AnimalTypes.Info info) =>
        (info?.SizeLevel ?? 1) >= 4 ? AttackType.LargeBody : AttackType.SmallBody;

    /// <summary>
    /// เอฟเฟกต์ตอนโดนสัตว์กัด — **ค่าของเรา**
    /// ข้อมูลเกมไม่ได้บอกว่าตัวไหนควรทำให้กระเด็น ⇒ ผูกกับขนาดตัวซึ่งเป็นค่าจริงจาก
    /// animal.json (size_level) เกณฑ์เดียวกับ <see cref="BodyAttackTypeOf"/>
    /// </summary>
    private static DamageEffects HitEffectOf(AnimalTypes.Info info) =>
        (info?.SizeLevel ?? 1) >= 4 ? DamageEffects.Blow : DamageEffects.KnockBack;

    /// <summary>
    /// **ค่าของเรา** — เป้าออกนอกระยะนานเกินกี่วินาทีถึงเลิกโกรธ
    /// ตั้งให้พอไล่ต่อได้ถ้าผู้เล่นแค่ถอยหลบ แต่ไม่ค้างโกรธตลอดกาล
    /// </summary>
    private const double AggroForgetSeconds = 12.0;

    /// <summary>
    /// สถานะการต่อสู้ของสัตว์ (CombatInteraction 23)
    ///
    /// ฝั่งเกมรับที่ client/ObjectManager.cs —
    ///   <c>Details["status"]</c>        → AnimalBehavior.Status (ไอคอนบนหัว + โหมดสู้)
    ///   <c>Details["notice_attack"]</c> → AnimalBehavior.AttackNotice (วงแหวนเตือนก่อนฟาด · มิลลิวินาที)
    ///   <c>Details["look_at"]</c>       → หันหัวมองเป้า (ใช้คู่กับ TargetId)
    /// และมันข้ามเองถ้า TargetId ไม่ใช่ผู้เล่นในเครื่องนั้น ⇒ broadcast ได้ปลอดภัย
    ///
    /// ⚠️ ไม่ส่ง = สัตว์ไม่มีไอคอนสถานะ ไม่หันหน้ามอง ไม่มีวงแหวนเตือน
    /// ผู้เล่นเห็นแค่ "ยืนนิ่งแล้วเลือดลด"
    /// </summary>
    private CombatInteraction CombatStatus(AnimalManager.Animal animal, AnimalStatus status,
                                           bool lookAt, bool noticeAttack = false)
    {
        var details = new Dictionary<string, long>
        {
            ["status"] = (long)status,
            ["look_at"] = lookAt ? 1L : 0L
        };
        // notice_attack เป็นเวลาแบบจำนวนเต็ม (มิลลิวินาที) — Times.UnixTimeNow() คืน double
        if (noticeAttack) details["notice_attack"] = (long)(Times.UnixTimeNow() * 1000.0);
        return new CombatInteraction
        {
            EntityId = animal.EntityId,
            TargetId = EntityId,
            Details = details
        };
    }

    /// <summary>ผู้เล่นอยู่ในระยะกี่ช่องจากจุดนี้ไหม (1 ช่อง = 200 หน่วยพิกัดโลก)</summary>
    private bool IsWithinTiles(Point2 tile, int tiles)
    {
        Movement[] movements = _context.AppearPlayer.Move.Movements;
        if (movements == null || movements.Length == 0 ||
            movements[0].Path == null || movements[0].Path.Length == 0)
        {
            return false;   // ยังไม่รู้ตำแหน่ง — อย่าเพิ่งกัด (ตรงข้ามกับตอนเก็บของที่ไม่บล็อก)
        }
        WorldPosition pos = movements[0].Path[0].Position;
        float dx = pos.x / 200f - tile.x;
        float dy = pos.y / 200f - tile.y;
        return dx * dx + dy * dy <= tiles * tiles;
    }

    /// <summary>**ค่าของเรา** — ทุกกี่วินาทีถึงตรวจระยะสัตว์รอบตัวหนึ่งครั้ง</summary>
    private const double AnimalSyncIntervalSeconds = 0.5;

    /// <summary>
    /// เติมเมนูให้ตอนผู้เล่นแตะสัตว์ — คืน true ถ้า entity ที่แตะเป็นสัตว์ (ผู้เรียกจะได้หยุดตรงนั้น)
    ///
    /// ฝั่งเกมไม่ได้ตัดสินใจเองว่าแตะอะไรแล้วทำอะไรได้ — มันเชื่อรายการใน <c>Touched.Interactions</c>
    /// ที่เซิร์ฟส่งมาล้วน ๆ (เหมือนกรณีท่าเรือกับของธรรมชาติ) ⇒ ไม่ส่ง = ไม่มีปุ่มให้กด
    ///
    /// ยังไม่ใส่เมนู "ชำแหละ" ให้ซากสัตว์ เพราะระบบของที่ได้จากซากยังไม่มี
    /// (<c>animal.json → drop_item</c> ชี้ไปที่ชุดของที่ยังไม่มีตารางบอกว่าได้อะไรกี่ชิ้น)
    /// ⇒ ใส่ปุ่มไปก่อนแล้วกดไม่ได้อะไร แย่กว่าไม่มีปุ่ม
    /// </summary>
    private bool TryTouchAnimal(Messages.Touch touch, ref Touched msg)
    {
        AnimalManager.Animal animal = _world.AnimalManager?.Get(touch.EntityId);
        if (animal == null) return false;

        AnimalTypes.Info info = AnimalTypes.Get(animal.EntityType);
        string label = info?.DisplayName ?? info?.Name;

        if (animal.IsAlive)
        {
            if (label != null) msg.EntityName = new Gettext(label);
            msg.Interactions = new[] { (int)Shared.System.Interaction.Attack };
            return true;
        }

        // [7 ก.ย. 2026] ซาก — ต่อเวลาถอยหลังไว้ท้ายชื่อ ผู้เล่นจะได้รู้ว่าเหลือเวลาแล่อีกเท่าไร
        // ⚠️ ไม่บอก = แล่ค้างไว้แล้วซากหายไปกลางคันโดยไม่มีสัญญาณอะไรเลย
        // EntityName เป็นช่องข้อความอิสระที่ฝั่งเกมเอาไปโชว์เป็นหัวเรื่องตอนแตะ (Touched.EntityName)
        double leftSeconds = animal.DiedAt > 0.0
            ? Math.Max(0.0, animal.DiedAt + AnimalManager.CorpseDisposeDelay - Gauge.CurrentTime)
            : 0.0;
        string countdown = leftSeconds > 0.0
            ? $" (ซากหายในอีก {(int)leftSeconds / 60}:{(int)leftSeconds % 60:00} นาที)"
            : string.Empty;
        if (label != null) msg.EntityName = new Gettext(label + countdown);

        // ซากสัตว์ — ชำแหละด้วยทางเดียวกับเก็บของธรรมชาติทุกประการ
        // (Collect 506 + Touched.Collectible) ต่างแค่ collectible id มาจาก animal.json → drop_item
        // ดู Player.Gathering.CollectibleIdOf ที่เป็นจุดเชื่อม
        msg.Collectible = BuildCollectibleFor(animal.EntityId, animal.EntityType, animal.Tile);
        msg.Interactions = new[] { (int)Shared.System.Interaction.Collect };
        return true;
    }

    /// <summary>
    /// ตีสัตว์ป่า — คืน true ถ้า entity ที่เล็งเป็นสัตว์ที่ยังไม่ตายบนเกาะนี้
    ///
    /// สูตรความเสียหายใช้ชุดเดียวกับตีผู้เล่น (ดูคำอธิบายเต็มที่ Player.Combat.ReceiveAttack)
    /// ต่างกันแค่ค่าป้องกันมาจากสูตรของสัตว์ตัวนั้นเอง
    /// (<c>entity_types/animal.json → defense = "(0 + combat_level * 5) * unstable_factor"</c>)
    /// ⇒ สัตว์เลเวลสูงกินดาเมจน้อยลงจริงตามข้อมูลเกม ไม่ใช่เลขที่เราตั้ง
    /// </summary>
    private bool TryAttackAnimal(string entityId, BattleAttackInfo attack, double startAt)
    {
        AnimalManager.Animal animal = _world.AnimalManager?.Get(entityId);
        if (animal == null || !animal.IsAlive) return false;

        float bonus = attack.damage_bonus > 0f ? attack.damage_bonus : 1f;
        // [7 ก.ย. 2026] สกิลหมวดต่อสู้เพิ่มดาเมจ (ดู Player.SkillEffects.cs) — ทางเดียวกับตีผู้เล่น
        float raw = CurrentAttackPower() * bonus * OutgoingDamageScale();

        // เจาะเกราะจากท่า — ฟิลด์เดียวกับที่ใช้ตอนตีผู้เล่น (attack_info[0].armor_penetration)
        float defense = animal.Defense * (1f - Math.Clamp(attack.armor_penetration, 0f, 1f));
        int value = Math.Max(CombatTuning.MinDamage, (int)Math.Round(raw - defense));

        animal.Life = Math.Max(0f, animal.Life - value);
        bool justAngered = string.IsNullOrEmpty(animal.AggroTargetId);
        animal.AggroTargetId = EntityId;      // ตีมันแล้วมันสู้กลับ แม้เป็นสัตว์กินพืช
        // เพิ่งโกรธ ⇒ เปลี่ยนเป็นท่ายืนแบบเตรียมสู้ ให้เห็นบนจอว่ามันตอบสนอง
        if (justAngered && animal.IsAlive) _world.BroadCast(animal.ToMotionMessage());

        // ⚠️ ต้องส่งหลอดเลือดชุดใหม่ตามไปด้วย ไม่งั้น**หลอดเลือดของเป้าไม่ขยับเลย**
        // ข้อความ Damaged(12) ทำแค่เอฟเฟกต์ตอนโดน (client/Durango.Logic.Combat/DamagedProcesser.cs:155
        // → OnTakeDamage → เล่นอนุภาค) มันไม่ได้ไปแตะหลอดเลือด
        // หลอดของเป้าอ่านจาก DamageableEntity.GetLife() (client/Durango.UI/CombatTargetWidget.cs:62)
        // ซึ่งอัปเดตจาก Survival(182) เท่านั้น (client/ObjectManager.cs:47-67)
        // ⇒ ไม่ส่ง = ตีเท่าไรหลอดก็เต็มอยู่อย่างนั้น ดูเหมือนดาเมจไม่เข้า
        BroadcastAnimalSurvival(animal);

        AnimalTypes.Info hit = AnimalTypes.Get(animal.EntityType);
        Console.WriteLine($"[ล่าสัตว์] ตี {hit?.Name ?? animal.EntityType.ToString()} lv{animal.CombatLevel} " +
                          $"−{value} เลือดเหลือ {animal.Life:F0}/{animal.LifeMax:F0}");

        _world.BroadCast(new Damaged
        {
            VictimId = animal.EntityId,
            AttackerId = EntityId,
            EventAt = startAt > 0.0 ? startAt : Times.UnixTimeNow(),
            Damage = new Damage
            {
                Result = DamageResult.Hit,
                Value = value,
                Part = BodyPart.Body,
                Direction = CombatTuning.HitDirection,
                AttackType = CurrentAttackType(),
                // เหตุผลเดียวกับตอนสัตว์กัดเรา — ไม่ใส่ flag = สัตว์ไม่มีท่าเจ็บให้เห็นตอนโดนตี
                Effects = DamageEffects.KnockBack
            }
        });

        if (animal.Life <= 0f)
        {
            animal.IsAlive = false;
            animal.DiedAt = Times.UnixTimeNow();
            _world.BroadCast(new EntityDied { EntityId = animal.EntityId, At = animal.DiedAt });
            // ⚠️ EntityDied อย่างเดียวไม่พอ — client/AnimalBehavior.cs:830 OnDie ไม่เล่นท่าตายให้
            // (แค่เปลี่ยน layer กับไล่สีจาง) ⇒ ไม่ส่งท่ามา สัตว์ตายแล้วยังยืนท่าเดิม
            _world.BroadCast(animal.ToMotionMessage());

            // [7 ก.ย. 2026] ให้ exp ตอนฆ่าเท่านั้น — ห้ามให้ทุกครั้งที่ตีโดน (ฟาร์มเลเวลพัง)
            // Arrow/Stone = ระยะ · อื่น ๆ = ประชิด (ตาม AttackType ของอาวุธที่ถือ)
            AttackType atk = CurrentAttackType();
            Shared.Skill.Category combatCat =
                atk is AttackType.Arrow or AttackType.Stone
                    ? Shared.Skill.Category.RangedCombat
                    : Shared.Skill.Category.MeleeCombat;
            AddExpForAction(SkillTuning.KillWeight, combatCat, "ล่าสัตว์");

            // [7 ก.ย. 2026] เป้าตายแล้วต้องออกจากโหมดต่อสู้ — เดิมไม่เคยส่ง BattleEnded
            // ⇒ ตัวละครค้างท่าถืออาวุธ และ client/InteractionSystem กรองเมนูเหลือแต่ของโหมดสู้
            //   ทำให้แตะซากแล้วไม่มีปุ่ม "ชำแหละ" (เมนู Collect ถูกซ่อนระหว่างอยู่ในโหมดสู้)
            if (string.Equals(_battleTargetId, animal.EntityId, StringComparison.Ordinal) ||
                string.IsNullOrEmpty(_battleTargetId))
            {
                _battleTargetId = null;
                SetBattleMode(false);
            }
            // ตัวที่ไล่กัดเราตายแล้วด้วย — เคลียร์สถานะ "โดนสัตว์ไล่" ไม่งั้นค้างโหมดสู้ต่อ
            LeaveBattleWith(animal.EntityId);

            Console.WriteLine($"[ล่าสัตว์] {EntityId[..Math.Min(8, EntityId.Length)]} ล้ม " +
                              $"{hit?.Name ?? animal.EntityType.ToString()} lv{animal.CombatLevel} " +
                              $"ที่ [{animal.Tile.x},{animal.Tile.y}]");
        }

        return true;
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  จับสัตว์ป่าเป็น "บังเหียน" — ขั้นแรกสุดของการมีสัตว์เลี้ยง
    //
    //  ═══ ทำไมขั้นนี้สำคัญ ═══
    //  ตรวจข้อมูลแล้วพบว่า **บังเหียนคราฟต์ไม่ได้เลยสักสูตร** มีขายแต่ในร้านเงินจริง 16 ชนิด
    //  (data/assets/purchaser/commodities.json) ⇒ ถ้าไม่มีขั้นตอนจับสัตว์ ระบบสัตว์เลี้ยงทั้งชุด
    //  จะถูกล็อกอยู่หลังร้านค้าที่เซิร์ฟนี้ยังไม่มี
    //  แต่ข้อมูลบอกทางไว้ครบแล้ว: animal.json → taming_result ชี้จากสัตว์ป่าไปหาบังเหียน
    //  67 ชนิด (ตรงกับบังเหียนจริงใน performance.json 66 ชนิด) และ **43 ชนิดในนั้นเกิดบนเกาะอยู่แล้ว**
    //  ส่วนเครื่องมือจับ (ไอเทมที่มีแท็ก "capturable") มี 3 ระดับและ **คราฟต์ได้ทั้งหมด**
    //  ⇒ วงจรครบโดยไม่ต้องพึ่งร้านเงินจริง: คราฟต์เครื่องมือ → ตีสัตว์ให้เลือดต่ำ → จับ → ได้บังเหียน
    //
    //  ═══ ลำดับที่ฝั่งเกมคาดหวัง (client/Durango.Logic.Combat/UsingAction.cs:246-256) ═══
    //    client → UseTamingAction(1900) { EntityId, ToolItemId }
    //    server → Timer(1134) ที่ seq เดิม   ⇒ หลอดความคืบหน้าเริ่มเดิน
    //             ไม่ตอบ = .Rest() ทำงาน หลอดหยุดทันที กดแล้วเหมือนไม่มีอะไรเกิดขึ้น
    //    server → Rewarded(2065) { Effect = TamingCompletedEffect } เมื่อจับติด
    //             เป็น push ทั่วไป ไม่ผูก seq (client/Durango.UI/AlarmGroup.cs:293)
    //
    //  ═══ เงื่อนไขที่ฝั่งเกมเช็คก่อนโชว์ปุ่ม (client/Durango.UI/BattleActionButtons.cs:560-585) ═══
    //    1. ชนิดสัตว์ต้อง tamable        2. ในกระเป๋าต้องมีของแท็ก "capturable"
    //    3. เลือดสัตว์ ≤ tamable_hp_rate  4. เว้นระยะตาม taming_cooltime
    //  เซิร์ฟตรวจซ้ำทั้งหมด เพราะ client กันได้แค่ปุ่ม ไม่ได้กันแพ็กเก็ตที่ปลอมมา
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>เวลาที่เริ่มจับครั้งล่าสุด — ใช้กับ taming_cooltime</summary>
    private double _lastTamingAt;

    /// <summary>ตัวสุ่มของระบบจับสัตว์ — main loop เส้นเดียว ไม่ต้องล็อก</summary>
    private static readonly Random TamingRng = new();

    private void RegisterHuntingHandlers()
    {
        _connection.Recv(delegate(UseTamingAction msg, PacketHeader header)
        {
            HandleUseTamingActionMsg(msg, header.Seq);
        });
    }

    private void HandleUseTamingActionMsg(UseTamingAction msg, uint seq)
    {
        AnimalManager.Animal animal = _world.AnimalManager?.Get(msg.EntityId);
        if (animal == null || !animal.IsAlive)
        {
            RejectTaming(seq, "ไม่เจอสัตว์ตัวนี้ หรือมันตายไปแล้ว");
            return;
        }

        AnimalTypes.Info info = AnimalTypes.Get(animal.EntityType);
        if (info == null || !info.Tamable || string.IsNullOrEmpty(info.TamingResult))
        {
            RejectTaming(seq, $"สัตว์ชนิด {info?.Name ?? animal.EntityType.ToString()} ทำให้เชื่องไม่ได้");
            return;
        }

        // เครื่องมือต้องอยู่ในกระเป๋าจริงและมีแท็ก capturable (client/Durango.Logic.Item/Util.cs:92)
        int toolIndex = _context.InventoryItems.FindIndex(item => item.Id == msg.ToolItemId);
        if (toolIndex < 0 || !HasItemTag(_context.InventoryItems[toolIndex], "capturable"))
        {
            RejectTaming(seq, "ไม่มีเครื่องมือจับสัตว์ในกระเป๋า");
            return;
        }

        double now = Gauge.CurrentTime;
        if (now - _lastTamingAt < TamingTuning.Cooltime)
        {
            RejectTaming(seq, "เพิ่งจับไปเมื่อกี้ ยังไม่พ้นเวลารอ");
            return;
        }

        // เลือดต้องต่ำพอ — ค่าจริงจาก constants.json → taming → tamable_hp_rate
        float lifeRatio = animal.LifeMax > 0f ? animal.Life / animal.LifeMax : 1f;
        if (lifeRatio > TamingTuning.TamableHpRate)
        {
            RejectTaming(seq, $"เลือดสัตว์ยังสูงไป ({lifeRatio:P0} ต้องไม่เกิน {TamingTuning.TamableHpRate:P0})");
            return;
        }

        _lastTamingAt = now;
        Send(new Messages.Timer { Duration = TamingTuning.TamingTime }, seq);

        float chance = TamingChance(animal, lifeRatio);
        bool success = TamingRng.NextDouble() < chance;
        Console.WriteLine($"[จับสัตว์] {EntityId[..Math.Min(8, EntityId.Length)]} จับ {info.Name} " +
                          $"lv{animal.CombatLevel} เลือด {lifeRatio:P0} โอกาส {chance:P0} → " +
                          (success ? "สำเร็จ" : "หลุด"));

        if (!success)
        {
            // ล้มเหลว: สัตว์ยังอยู่ เครื่องมือยังอยู่ ลองใหม่ได้เมื่อพ้น cooltime
            // **การตีความของเรา** — ข้อมูลไม่ได้บอกว่าจับพลาดแล้วเสียอะไรไหม
            // เลือกทางที่ผู้เล่นไม่เสียของ เพราะถ้าเราตีความผิดแล้วของหาย กู้คืนไม่ได้
            return;
        }

        Item? rein = Cheats.MakeItem(info.TamingResult, Math.Max(1, animal.CombatLevel));
        if (rein == null)
        {
            Console.WriteLine($"[จับสัตว์] ⚠️ ไม่พบ prototype ของบังเหียน '{info.TamingResult}' — ยกเลิก");
            return;
        }

        // สัตว์หายจากโลก แล้วบังเหียนเข้ากระเป๋า
        animal.IsAlive = false;
        animal.DiedAt = now;
        _world.BroadCast(new EntityDied { EntityId = animal.EntityId, At = now });
        _world.BroadCast(animal.ToMotionMessage());

        var items = new List<Item> { rein.Value };
        AddItems(items);
        Send(new InventoryUpdated { EntityId = EntityId, Items = items.ToArray() });
        Send(new Rewarded
        {
            Effect = new TamingCompletedEffect
            {
                Type = Shared.System.RewardEffect.AnimalTamed,
                AnimalEntityId = animal.EntityId,
                AnimalEntityType = animal.EntityType,
                ReinsId = rein.Value.Id
            }
        });
        OnContextChanged();
    }

    /// <summary>
    /// โอกาสจับติด — สูตรจริงทั้งสองตัวจาก constants.json → taming
    ///
    ///   success_ratio        = "2 * (1 / (1 + exp(-(2.2 / 6 * d_l + 2.2))) - 0.5)"   d_l = ส่วนต่างเลเวล
    ///   adjust_by_life_ratio = "1 - 0.8 * pow(r / R, 2)"                             r/R = เลือดที่เหลือ
    ///
    /// **การตีความของเราคือเอาสองค่ามาคูณกัน** — ไฟล์ข้อมูลไม่ได้บอกว่าประกอบกันยังไง
    /// แต่ชื่อ adjust_by_life_ratio อ่านว่าเป็น "ตัวปรับ" ของค่าหลัก และช่วงค่าของมัน
    /// (เลือดเต็ม = 0.2 · เลือดหมด = 1.0) เข้ากับการเป็นตัวคูณพอดี
    ///
    /// d_l เป็นบวกเมื่อผู้เล่นเลเวลสูงกว่าสัตว์ — ทิศนี้ทำให้สัตว์ที่แรงกว่าจับยากกว่า
    /// ซึ่งเป็นทิศเดียวที่สมเหตุสมผล (ที่ d_l = 0 ได้ราว 60%)
    /// </summary>
    private float TamingChance(AnimalManager.Animal animal, float lifeRatio)
    {
        int playerLevel = Math.Max(1, _context.AppearPlayer.Level);
        var vars = new Dictionary<string, double>
        {
            ["d_l"] = playerLevel - animal.CombatLevel,
            ["r"] = animal.Life,
            ["R"] = Math.Max(1f, animal.LifeMax)
        };

        double baseChance = StatFormula.EvalOr(TamingTuning.SuccessRatio, vars, 0.5);
        double adjust = StatFormula.EvalOr(TamingTuning.AdjustByLifeRatio, vars, 1.0);
        return (float)Math.Clamp(baseChance * adjust, 0.0, 1.0);
    }

    /// <summary>
    /// ส่งหลอดเลือดชุดใหม่ของสัตว์ให้ทุกคนบนเกาะ
    ///
    /// รูปแบบเดียวกับที่แนบไปกับ AppearAnimal ตอนสัตว์โผล่ (ดู AnimalManager.Animal.ToMessage)
    /// — ใช้ Gauge ที่มีค่าสูงสุด/ต่ำสุดครบ ไม่ใช่แค่ค่าปัจจุบัน ไม่งั้นฝั่งเกมคำนวณสัดส่วนหลอดไม่ได้
    /// </summary>
    private void BroadcastAnimalSurvival(AnimalManager.Animal animal)
    {
        _world.BroadCast(new Survival
        {
            EntityId = animal.EntityId,
            Life = new Gauge(animal.LifeMax, 0f, new[] { new GaugeNode(Gauge.CurrentTime, animal.Life) }),
            Gauges = new Dictionary<string, Gauge>()
        });
    }

    /// <summary>ปฏิเสธคำขอจับ + เขียนเหตุผลลง log (ฝั่งเกมแค่หยุดหลอดเงียบ ๆ ไม่โชว์อะไรเลย)</summary>
    private void RejectTaming(uint seq, string reason)
    {
        Console.WriteLine($"[จับสัตว์] ปฏิเสธ: {reason}");
        Send(new Abort { Text = reason }, seq);
    }

    private static bool HasItemTag(Item item, string tagId)
    {
        if (item.Tags == null) return false;
        foreach (Messages.Tag tag in item.Tags)
        {
            if (tag.Id == tagId) return true;
        }
        return false;
    }
}
