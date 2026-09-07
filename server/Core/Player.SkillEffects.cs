using System;
using System.Collections.Generic;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  [7 ก.ย. 2026] สกิลมีผลกับการเล่นจริง
//
//  ⚠️ ก่อนมีไฟล์นี้ สกิลของเราเป็นแค่ "กุญแจปลดสูตร" อย่างเดียว
//     ค้นทั้ง server/Core และ server/Support แล้วไม่มีที่ไหนเลยที่เอาเลเวลสกิลไปคูณ
//     เวลาเก็บของ · เวลาคราฟต์ · ค่าพลังงาน · จำนวนของที่ได้ · ดาเมจ
//     ⇒ เรียนสกิลไปเท่าไรก็เก็บของช้าเท่าเดิม ได้ของเท่าเดิม สกิลไม่ใช่ "ความเก่ง"
//
//  ═══ ทำไมคิดจาก "ผลรวมเลเวลในหมวด" ไม่ใช่ผลของสกิลรายตัว ═══
//
//  ข้อมูลจริงบอกผลของสกิลรายตัวผ่าน rewards → modifiers ซึ่งเรามีครบและต่อสายไว้แล้ว
//  ที่ Player.Skills.CollectModifiers() **แต่** แปลงเป็น Derived ได้แค่ 40 จาก 138 ตัว
//  (SkillDataStore.BuildDerivedMap เดาจากชื่อ) ที่เหลือเป็นชื่ออย่าง gathering_speed
//  ซึ่งไม่มีคู่ใน enum Derived ⇒ ส่งไป client เฉย ๆ ไม่มีผลฝั่งเซิร์ฟ
//
//  ⇒ ไฟล์นี้เติมช่องว่างนั้นด้วยสัดส่วนระดับ "หมวด" แทน: ยิ่งลงแรงในหมวดไหนมาก
//    ยิ่งเก่งด้านนั้น ซึ่งตรงกับที่เกมออกแบบไว้ และไม่ทับกับสาย modifiers ที่มีอยู่
//    (สายนั้นคุม Attack/Defense/Dodge/Accuracy · ไฟล์นี้คุมความเร็ว/โบนัส/ค่าใช้จ่าย)
//
//  **ค่าเพดานทั้งหมดในไฟล์นี้เป็นค่าของเรา** — ข้อมูลจริงของ NEXON ไม่เคยหลุดมากับ client
//  ยกช่วงมาจากโปรเจกต์ Opencode (ServerPlayer.SkillEffects.cs) ที่เทสกับผู้เล่นจริงมาแล้ว
// ═══════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// **ค่าของเรา** — เพดานโบนัสของแต่ละหมวดตอนสกิลเต็ม
/// รวมไว้ที่เดียวเพื่อให้ปรับสมดุลได้โดยไม่ต้องไล่หาในโค้ด
/// </summary>
public static class SkillEffectTuning
{
    /// <summary>
    /// ผลรวม "เลเวลสกิลในหมวด + เลเวลหมวด×3" ที่ถือว่าเก่งเต็มขั้น
    ///
    /// นับเลเวลหมวดเข้าด้วยเพราะของเรามีระบบวิจัยหมวดจริง (categories.json)
    /// ซึ่งเป็นตัวชี้วัดความชำนาญที่ตรงกว่าจำนวนโหนดที่กดไป
    /// 60 = เลเวลสูงสุดของตัวละคร ใช้เป็นหลักไมล์เดียวกัน
    /// </summary>
    public const int FullAt = 60;

    /// <summary>เลเวลหมวด 1 ระดับ คิดเท่ากับสกิลกี่เลเวล</summary>
    public const int CategoryLevelWeight = 3;

    public const float GatherSpeed = 0.40f;      // เก็บของเร็วขึ้นสูงสุด 40%
    public const float GatherBonus = 0.30f;      // โอกาสได้ของเพิ่มอีกชิ้น สูงสุด 30%
    public const float ButcherySpeed = 0.40f;
    public const float ButcheryBonus = 0.30f;
    public const float CraftSpeed = 0.40f;
    public const float MeleeDamage = 0.50f;      // ดาเมจที่ตีออก สูงสุด +50%
    public const float DefenseReduce = 0.30f;    // ดาเมจที่รับ ลดสูงสุด 30%
    public const float EnergySave = 0.30f;       // ค่าพลังงานที่ใช้ ลดสูงสุด 30%

    /// <summary>ตัวคูณค่าใช้จ่ายต่ำสุด — ต่ำกว่านี้เท่ากับทำงานฟรี</summary>
    public const float MinCostScale = 0.1f;
}

public partial class Player
{
    private static readonly Random SkillBonusRng = new();

    /// <summary>
    /// ความเก่งของหมวดนี้เป็นสัดส่วน 0-1
    ///
    /// = (ผลรวมเลเวลสกิลที่เรียนในหมวด + เลเวลหมวด×3) ÷ FullAt
    /// </summary>
    private float SkillRatio(Shared.Skill.Category category)
    {
        int cat = (int)category;
        int total = CategoryState(cat).Level * SkillEffectTuning.CategoryLevelWeight;

        if (_skills?.Learned != null)
        {
            foreach (var (skillId, subs) in _skills.Learned)
            {
                if (!SkillDataStore.CategoryOfBundle.TryGetValue(skillId, out int owner)) continue;
                if (owner != cat || subs == null) continue;
                foreach (int level in subs.Values) total += level;
            }
        }

        return Math.Min(1f, total / (float)Math.Max(1, SkillEffectTuning.FullAt));
    }

    /// <summary>ความเก่งด้านคราฟต์ — ใช้หมวดที่เก่งที่สุด (สูตรไหนก็ได้)</summary>
    private float CraftRatio()
    {
        float best = 0f;
        foreach (Shared.Skill.Category cat in CraftCategories)
        {
            best = Math.Max(best, SkillRatio(cat));
        }
        return best;
    }

    private static readonly Shared.Skill.Category[] CraftCategories =
    {
        Shared.Skill.Category.Weaponcrafting, Shared.Skill.Category.Armorcrafting,
        Shared.Skill.Category.Constructing, Shared.Skill.Category.Cooking,
        Shared.Skill.Category.Process
    };

    // ── ตัวคูณที่เอาไปใช้จริง ────────────────────────────────────────────────────

    /// <summary>เวลาเก็บของ × ค่านี้</summary>
    public float GatherDurationScale() =>
        1f - SkillRatio(Shared.Skill.Category.Gathering) * SkillEffectTuning.GatherSpeed;

    /// <summary>เวลาแล่ซาก × ค่านี้</summary>
    public float ButcheryDurationScale() =>
        1f - SkillRatio(Shared.Skill.Category.Butchery) * SkillEffectTuning.ButcherySpeed;

    /// <summary>เวลาคราฟต์ × ค่านี้</summary>
    public float CraftDurationScale() => 1f - CraftRatio() * SkillEffectTuning.CraftSpeed;

    /// <summary>ดาเมจที่ตีออก × ค่านี้ (แยกระยะประชิด/ระยะไกลตามหมวดที่ใช้จริง)</summary>
    public float MeleeDamageScale() =>
        1f + SkillRatio(Shared.Skill.Category.MeleeCombat) * SkillEffectTuning.MeleeDamage;

    public float RangedDamageScale() =>
        1f + SkillRatio(Shared.Skill.Category.RangedCombat) * SkillEffectTuning.MeleeDamage;

    /// <summary>
    /// ตัวคูณดาเมจตามอาวุธที่ถืออยู่จริง — ธนู/หินใช้หมวดระยะไกล ที่เหลือใช้ประชิด
    /// (เกณฑ์เดียวกับที่ Player.Hunting ใช้เลือกหมวดตอนแจก exp ⇒ เรียนหมวดไหน หมวดนั้นมีผล)
    /// </summary>
    public float OutgoingDamageScale() =>
        CurrentAttackType() is Shared.Battle.AttackType.Arrow or Shared.Battle.AttackType.Stone
            ? RangedDamageScale()
            : MeleeDamageScale();

    /// <summary>ดาเมจที่รับ × ค่านี้</summary>
    public float DamageTakenScale() =>
        Math.Max(SkillEffectTuning.MinCostScale,
                 1f - SkillRatio(Shared.Skill.Category.Defense) * SkillEffectTuning.DefenseReduce);

    /// <summary>
    /// ค่าพลังงาน/ความเหนื่อยที่เสีย × ค่านี้
    ///
    /// ใช้หมวด Survival — ของเราไม่มีหลอดสตามินาแยกแบบ Opencode
    /// ค่าที่เทียบเคียงคือ energy กับ fatigue ซึ่งเป็นตัวจำกัดว่าทำงานได้นานแค่ไหน
    /// </summary>
    public float EnergyCostScale() =>
        Math.Max(SkillEffectTuning.MinCostScale,
                 1f - SkillRatio(Shared.Skill.Category.Survival) * SkillEffectTuning.EnergySave);

    /// <summary>เก็บของรอบนี้ได้ของเพิ่มอีกชิ้นไหม</summary>
    public bool RollGatherBonus() =>
        RollChance(SkillRatio(Shared.Skill.Category.Gathering) * SkillEffectTuning.GatherBonus);

    /// <summary>แล่รอบนี้ได้ชิ้นส่วนเพิ่มไหม</summary>
    public bool RollButcheryBonus() =>
        RollChance(SkillRatio(Shared.Skill.Category.Butchery) * SkillEffectTuning.ButcheryBonus);

    private static bool RollChance(float chance)
    {
        if (chance <= 0f) return false;
        lock (SkillBonusRng)
        {
            return SkillBonusRng.NextDouble() < chance;
        }
    }

    // ── ค่าสถานะพื้นฐาน 8 ตัว ────────────────────────────────────────────────────

    /// <summary>
    /// **ค่าของเรา** — ค่าสถานะไหนโตจากหมวดสกิลอะไร
    ///
    /// เลือกจากความหมายของ ability ในเกมต้นฉบับ:
    /// พลัง = ฟัน/แบกของหนัก · อดทน = โดนตีแล้วยังยืนอยู่ · คล่องแคล่ว = ขยับตัวตอนสู้ ·
    /// คล่องมือ = งานฝีมือ · รับรู้ = หาของเจอ/เล็งแม่น · สติปัญญา = รู้ว่าอะไรผสมกับอะไรได้ ·
    /// มุ่งมั่น = งานที่ต้องอดทนทำซ้ำ · เสน่ห์ = เลี้ยงคนเป็นและแต่งตัวเป็น
    ///
    /// (เสน่ห์ยังไม่มีผลกับอะไรจนกว่าจะมีระบบ NPC/ตลาด — ใส่ไว้ไม่ให้หน้าตัวละครมีช่องตาย)
    /// </summary>
    private static readonly Dictionary<Shared.Ability.Basic, Shared.Skill.Category[]> AbilitySources = new()
    {
        [Shared.Ability.Basic.Strength] = new[]
            { Shared.Skill.Category.MeleeCombat, Shared.Skill.Category.Constructing },
        [Shared.Ability.Basic.Endurance] = new[]
            { Shared.Skill.Category.Defense, Shared.Skill.Category.Survival },
        [Shared.Ability.Basic.Agility] = new[]
            { Shared.Skill.Category.MeleeCombat, Shared.Skill.Category.RangedCombat },
        [Shared.Ability.Basic.Dexterity] = new[]
            { Shared.Skill.Category.Weaponcrafting, Shared.Skill.Category.Armorcrafting, Shared.Skill.Category.Process },
        [Shared.Ability.Basic.Perception] = new[]
            { Shared.Skill.Category.Gathering, Shared.Skill.Category.RangedCombat },
        [Shared.Ability.Basic.Intelligence] = new[]
            { Shared.Skill.Category.Cooking, Shared.Skill.Category.Process },
        [Shared.Ability.Basic.Will] = new[]
            { Shared.Skill.Category.Survival, Shared.Skill.Category.Butchery },
        [Shared.Ability.Basic.Charisma] = new[]
            { Shared.Skill.Category.Cooking, Shared.Skill.Category.Armorcrafting }
    };

    /// <summary>
    /// ค่าสถานะพื้นฐานก่อนรวม modifier จากสกิล
    /// = ฐาน + (เลเวล−1)×AbilityPerLevel + Σ(เลเวลหมวด−1)×AbilityPerCategory
    ///
    /// ⚠️ ต้องหักค่าเริ่มต้น 1 ออกทั้งเลเวลและเลเวลหมวด ไม่งั้นตัวละครใหม่ได้โบนัสตั้งแต่เกิด
    /// และค่าที่ผูกหลายหมวดจะสูงกว่าตัวอื่นทั้งที่ยังไม่เคยเล่น
    /// </summary>
    private float BaseAbilityValue(Shared.Ability.Basic ability)
    {
        float value = SkillTuning.BasicAbilityBase
                    + Math.Max(0, _skillLevel - 1) * SkillTuning.AbilityPerLevel;

        if (AbilitySources.TryGetValue(ability, out Shared.Skill.Category[] cats))
        {
            int progress = 0;
            foreach (Shared.Skill.Category cat in cats)
            {
                progress += Math.Max(0, CategoryState((int)cat).Level - 1);
            }
            value += progress * SkillTuning.AbilityPerCategory;
        }

        return Math.Max(1f, Math.Min(SkillTuning.AbilityMax, value));
    }

    // ── หลอดที่โตตามตัวละคร ──────────────────────────────────────────────────────

    /// <summary>**ค่าของเรา** — เลือดสูงสุดที่เพิ่มต่อ 1 เลเวล และต่อ 1 หน่วยความอดทน</summary>
    private const float LifePerLevel = 4f;
    private const float LifePerEndurance = 2f;

    /// <summary>**ค่าของเรา** — พลังงานสูงสุดที่เพิ่มต่อ 1 เลเวล และต่อ 1 หน่วยความมุ่งมั่น</summary>
    private const float EnergyPerLevel = 1.5f;
    private const float EnergyPerWill = 0.8f;

    /// <summary>
    /// อัปเดตเพดานหลอดตามเลเวล/ค่าสถานะปัจจุบัน
    ///
    /// health เป็นเพดานของ life · energy เป็นเพดานของ stamina
    /// (entity_types/players.json → survival — ดู SurvivalState.Resolve)
    /// ⇒ บวกที่สองตัวนี้ตัวเดียว หลอดที่อ้างถึงมันก็ขยายตาม
    ///
    /// ⚠️ ตัวละครใหม่ (เลเวล 1 · ทุกหมวดเลเวล 1) ต้องได้ 0 พอดี ไม่งั้นค่าเริ่มต้นเพี้ยนจากที่ข้อมูลบอก
    /// </summary>
    private void RefreshSurvivalMax()
    {
        if (_survival == null) return;
        double now = Gauge.CurrentTime;

        float endurance = BaseAbilityValue(Shared.Ability.Basic.Endurance) - SkillTuning.BasicAbilityBase;
        float will = BaseAbilityValue(Shared.Ability.Basic.Will) - SkillTuning.BasicAbilityBase;
        int levels = Math.Max(0, _skillLevel - 1);

        bool changed = _survival.SetMaxBonus(SurvivalState.KeyHealth,
                           levels * LifePerLevel + Math.Max(0f, endurance) * LifePerEndurance, now);
        changed |= _survival.SetMaxBonus(SurvivalState.KeyEnergy,
                       levels * EnergyPerLevel + Math.Max(0f, will) * EnergyPerWill, now);

        // ⚠️ Rebuild เปลี่ยนแค่หลอดฝั่งเซิร์ฟ — ต้อง broadcast ด้วย ไม่งั้นบนจอยังเป็นเพดานเก่า
        // จนกว่าจะมีอะไรอื่นไปสั่ง flush (client วาดจากชุด Gauge ที่ได้รับล่าสุดล้วน ๆ)
        if (changed) FlushSurvival();
    }

    /// <summary>
    /// เอาเพดานหลอดที่ขยายแล้วไปทับค่าใน Statistics ให้ตัวเลขบนจอตรงกับหลอดจริง
    ///
    /// ⚠️ SurvivalState.FillDeriveds อ่าน max จาก players.json ตรง ๆ ⇒ ไม่รู้จักส่วนที่บวกเพิ่ม
    /// ถ้าไม่ทับ หน้าจอจะโชว์เลือดสูงสุด 300 ตลอดทั้งที่หลอดจริงขยายไปแล้ว
    ///
    /// max_effected_by ในไฟล์จริง: energy→3 (MaxEnergy) · health→0 (MaxHealth)
    /// ส่วน LifeMax(50) ไม่ได้อยู่ในตารางนั้น ต้องตั้งเอง เพราะ health คือเพดานของ life
    /// </summary>
    private void ApplySurvivalMaxToDeriveds(IDictionary<Shared.Ability.Derived, float> deriveds)
    {
        if (deriveds == null || _survival == null) return;
        double now = Gauge.CurrentTime;

        float health = _survival.MaxOf(SurvivalState.KeyHealth, now);
        if (health > 0f)
        {
            deriveds[Shared.Ability.Derived.MaxHealth] = health;
            deriveds[Shared.Ability.Derived.LifeMax] = health;
        }

        float energy = _survival.MaxOf(SurvivalState.KeyEnergy, now);
        if (energy > 0f) deriveds[Shared.Ability.Derived.MaxEnergy] = energy;
    }

    /// <summary>สรุปโบนัสของตัวเอง — ใช้ตอบ cheat "skills" ให้ตรวจได้ว่าที่เรียนมีผลจริง</summary>
    private string DescribeSkillBonuses()
    {
        return $"เก็บของ เร็วขึ้น {1f - GatherDurationScale():P0} · โบนัส " +
               $"{SkillRatio(Shared.Skill.Category.Gathering) * SkillEffectTuning.GatherBonus:P0} | " +
               $"แล่เนื้อ เร็วขึ้น {1f - ButcheryDurationScale():P0} | " +
               $"คราฟต์ เร็วขึ้น {1f - CraftDurationScale():P0} | " +
               $"ตี +{MeleeDamageScale() - 1f:P0} · รับ -{1f - DamageTakenScale():P0} | " +
               $"พลังงาน -{1f - EnergyCostScale():P0}";
    }
}
