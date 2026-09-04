using System;
using System.Collections.Generic;
using Durango.Utils;
using Messages;
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
            }
        }
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
        if (label != null) msg.EntityName = new Gettext(label);

        if (animal.IsAlive)
        {
            msg.Interactions = new[] { (int)Shared.System.Interaction.Attack };
            return true;
        }

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
        float raw = CurrentAttackPower() * bonus;

        // เจาะเกราะจากท่า — ฟิลด์เดียวกับที่ใช้ตอนตีผู้เล่น (attack_info[0].armor_penetration)
        float defense = animal.Defense * (1f - Math.Clamp(attack.armor_penetration, 0f, 1f));
        int value = Math.Max(CombatTuning.MinDamage, (int)Math.Round(raw - defense));

        animal.Life = Math.Max(0f, animal.Life - value);

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
                Effects = DamageEffects.None
            }
        });

        if (animal.Life <= 0f)
        {
            animal.IsAlive = false;
            animal.DiedAt = Times.UnixTimeNow();
            _world.BroadCast(new EntityDied { EntityId = animal.EntityId, At = animal.DiedAt });

            Console.WriteLine($"[ล่าสัตว์] {EntityId[..Math.Min(8, EntityId.Length)]} ล้ม " +
                              $"{hit?.Name ?? animal.EntityType.ToString()} lv{animal.CombatLevel} " +
                              $"ที่ [{animal.Tile.x},{animal.Tile.y}]");
        }

        return true;
    }
}
