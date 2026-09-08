using System;
using System.Collections.Generic;
using Durango.Online;
using Messages;
using Shared.Etc;
using Shared.Region;
using Shared.Survival;
using Shared.Skill;
using Shared.StatusEffect;

namespace DurangoServerNx;

/// <summary>
/// Offline assertions for character / category level-up RewardEffect packets.
/// Run: <c>DurangoServer --fx-check [--data &lt;dataDir&gt;]</c>
/// </summary>
internal static class LevelUpFxCheck
{
    static int _failed;
    static int _passed;

    public static int Run(string dataDir)
    {
        _failed = 0;
        _passed = 0;
        Durango.Utils.Json.DataDir = dataDir;

        CheckCharacterLevelUpPacket();
        CheckCategoryLevelUpPacket();
        CheckClearOnLevelUpTagExists();
        CheckStatusEffectDetailsPacked();
        CheckInsideHouseBuff();
        CheckFatigueTuning(dataDir);
        CheckActionFatigue();
        CheckDestructCost();

        Console.WriteLine($"[fx-check] ผ่าน {_passed} · ตก {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    static void CheckCharacterLevelUpPacket()
    {
        Rewarded rewarded = Player.BuildCharacterLevelUpRewarded(12);
        Expect(rewarded.Effect is LevelUpEffect, "character Rewarded.Effect เป็น LevelUpEffect");
        if (rewarded.Effect is LevelUpEffect fx)
        {
            Expect(fx.Type == Shared.System.RewardEffect.LevelUp, $"LevelUpEffect.Type=LevelUp (ได้ {fx.Type})");
            Expect(fx.Level == 12, $"LevelUpEffect.Level=12 (ได้ {fx.Level})");
        }
    }

    static void CheckCategoryLevelUpPacket()
    {
        Rewarded rewarded = Player.BuildCategoryLevelUpRewarded(Category.Gathering, 5);
        Expect(rewarded.Effect is CategoryLevelUpRewardEffect, "category Rewarded.Effect เป็น CategoryLevelUpRewardEffect");
        if (rewarded.Effect is CategoryLevelUpRewardEffect fx)
        {
            Expect(fx.Type == Shared.System.RewardEffect.CategoryLevelUp, $"CategoryLevelUpRewardEffect.Type=CategoryLevelUp (ได้ {fx.Type})");
            Expect(fx.ChangedLevels != null && fx.ChangedLevels.Count == 1,
                "ChangedLevels มีหมวดเดียว");
            int level = 0;
            bool hasGathering = fx.ChangedLevels != null && fx.ChangedLevels.TryGetValue(Category.Gathering, out level);
            Expect(hasGathering && level == 5, $"ChangedLevels[Gathering]=5 (ได้ {level})");
        }
    }

    static void CheckClearOnLevelUpTagExists()
    {
        StatusEffectCatalog.Template dirty = StatusEffectCatalog.Get("dirty", 1);
        Expect(dirty != null, "โหลด template dirty จาก status_effects.json");
        Expect(Player.HasClearOnLevelUpTag(dirty), "dirty มีแท็ก clear_on_levelup (เคลียร์ตอนเลเวลขึ้น)");
        Expect(!Player.HasClearOnLevelUpTag(null), "template ว่างไม่มีแท็ก clear_on_levelup");
    }

    static void CheckStatusEffectDetailsPacked()
    {
        EffectDetail[] taste = StatusEffectCatalog.PackDetails("taste_good", 1);
        Expect(taste != null && taste.Length == 1, "taste_good แพ็ก effects 1 รายการ");
        if (taste != null && taste.Length == 1)
        {
            Expect(taste[0].Type == EffectType.Modifier, "taste_good type=Modifier");
            Expect(taste[0].Key == "exp_multiplier", "taste_good key=exp_multiplier");
            Expect(Math.Abs(taste[0].Value - 0.03f) < 0.0001f, "taste_good value=0.03 จาก JSON");
        }

        EffectDetail[] rest = StatusEffectCatalog.PackDetails("rest", 1);
        Expect(rest != null && rest.Length >= 1, "rest แพ็ก effects จาก JSON");
        bool fatigue = false;
        if (rest != null)
        {
            foreach (EffectDetail d in rest)
            {
                if (d.Type == EffectType.Survival && d.Key == "fatigue" && d.Value < 0f)
                {
                    fatigue = true;
                    break;
                }
            }
        }
        Expect(fatigue, "rest มี Survival/fatigue ติดลบ (พักแล้วล้าลด)");

        EffectDetail[] dirty = StatusEffectCatalog.PackDetails("dirty", 1);
        Expect(dirty != null && dirty.Length == 1 && dirty[0].Type == EffectType.Fatigue,
            "dirty แพ็ก Fatigue type=2 ตาม JSON");
    }

    static void CheckInsideHouseBuff()
    {
        StatusEffectCatalog.Template inside = StatusEffectCatalog.Get("inside", 1);
        Expect(inside != null, "โหลด template inside จาก status_effects.json");
        EffectDetail[] details = StatusEffectCatalog.PackDetails("inside", 1);
        Expect(details != null && details.Length > 0, "inside แพ็ก effects จาก JSON");
        bool fatigue = false;
        if (details != null)
        {
            foreach (EffectDetail d in details)
            {
                if (d.Type == EffectType.Fatigue) { fatigue = true; break; }
            }
        }
        Expect(fatigue, "inside มี Fatigue (ลดผลอากาศตอนอยู่ในบ้าน)");

        Expect(HouseEnterability.IsEnterable(new[] { "Modular" }), "Modular เข้าบ้านได้");
        Expect(HouseEnterability.IsEnterable(new[] { "FourSideEnterable" }), "FourSideEnterable เข้าบ้านได้");
        Expect(HouseEnterability.IsEnterable(new[] { "Landmark" }), "Landmark เข้าบ้านได้");
        Expect(!HouseEnterability.IsEnterable(new[] { "Fence" }), "รั้วเข้าบ้านไม่ได้");
        Expect(HouseEnterability.ContainsTile(new Point2(10, 20), new Point2(4, 3), new Point2(13, 22)),
            "ช่องมุมขวาล่างของบ้านอยู่ในเขต");
        Expect(!HouseEnterability.ContainsTile(new Point2(10, 20), new Point2(4, 3), new Point2(14, 22)),
            "ช่องนอกขอบบ้านไม่อยู่ในเขต");

        StatusEffectCatalog.Template camp = StatusEffectCatalog.Get("camp_fire", 1);
        Expect(camp != null, "โหลด template camp_fire จาก status_effects.json");
        EffectDetail[] campDetails = StatusEffectCatalog.PackDetails("camp_fire", 1);
        Expect(campDetails != null && campDetails.Length > 0, "camp_fire แพ็ก effects จาก JSON");
    }

    /// <summary>ความเหนื่อยจากสภาพแวดล้อม — ค่าทุกตัวต้องมาจากไฟล์ data ไม่ใช่ค่าที่แต่งเอง</summary>
    static void CheckFatigueTuning(string dataDir)
    {
        // ── หมวด: default_ratio / effect / ชื่อ snake_case ที่ผล type=2 ใช้อ้าง ──
        FatigueTuning.CategoryInfo hot = FatigueTuning.Get(FatigueCategory.Hot);
        FatigueTuning.CategoryInfo veryHot = FatigueTuning.Get(FatigueCategory.VeryHot);
        Expect(hot != null && veryHot != null, "โหลด fatigue_categories.json");
        if (hot != null)
        {
            Expect(hot.SnakeCase == "hot", "Hot -> snake_case \"hot\"");
            Expect(hot.Effect == "hot", "Hot -> ท่ายืน hot");
            Expect(Math.Abs(hot.DefaultRatio - 1f) < 0.001f, "Hot default_ratio = 1.0");
        }
        if (veryHot != null)
        {
            Expect(veryHot.SnakeCase == "very_hot", "VeryHot -> snake_case \"very_hot\"");
            Expect(Math.Abs(veryHot.DefaultRatio - 2f) < 0.001f, "VeryHot default_ratio = 2.0 (เหนื่อยเป็นสองเท่า)");
        }
        Expect(FatigueTuning.Get(FatigueCategory.ScorchingSun)?.SnakeCase == "scorching_sun",
            "ScorchingSun -> snake_case \"scorching_sun\"");
        Expect(FatigueTuning.Get(FatigueCategory.Cold)?.Effect == "cold", "Cold -> ท่ายืน cold");
        Expect(FatigueTuning.ParseCategory("volcanic_heat") == FatigueCategory.VolcanicHeat,
            "อ่าน \"volcanic_heat\" จาก biome_effects ได้");

        // ── ความเร็วฐานแยกตาม Role (constants.json -> fatigue_velocity) ──
        // สูตรหลักที่ sl=rl=20: (0.04 + 0.001*20) * max(0.5, 1 - 0) = 0.06
        double risky = FatigueTuning.BaseVelocity(Role.Risky, 20, 20);
        Expect(Math.Abs(risky - 0.06) < 0.0001, "Risky ใช้สูตร default = 0.06 ที่ sl=rl=20");
        Expect(FatigueTuning.BaseVelocity(Role.Tutorial, 20, 20) == 0.0, "Tutorial ไม่เหนื่อย");
        Expect(FatigueTuning.BaseVelocity(Role.Safehouse, 20, 20) == 0.0, "Safehouse ไม่เหนื่อย");
        Expect(Math.Abs(FatigueTuning.BaseVelocity(Role.Rural, 20, 20) - risky * 0.5) < 0.0001,
            "Rural เหนื่อยครึ่งเดียวของ default");
        Expect(Math.Abs(FatigueTuning.BaseVelocity(Role.Urban, 20, 20) - risky * 0.5) < 0.0001,
            "Urban เหนื่อยครึ่งเดียวของ default");
        // เลเวลเราสูงกว่าเกาะ -> เหนื่อยช้าลง แต่ไม่ต่ำกว่าครึ่ง
        Expect(FatigueTuning.BaseVelocity(Role.Risky, 60, 5) < FatigueTuning.BaseVelocity(Role.Risky, 60, 60),
            "เลเวลสูงกว่าเกาะมากแล้วเหนื่อยช้าลง");

        // ── biome_effects ของแม่แบบจริง (id ยกมาจาก region_templates.json ตรง ๆ) ──
        RegionCatalog.Load(System.IO.Path.Combine(dataDir, "assets"));
        RegionCatalog.TemplateInfo volcano = RegionCatalog.GetTemplate("ri60vo180404");
        Expect(volcano != null, "โหลดแม่แบบ ri60vo180404");
        Expect(volcano != null && volcano.Role == Role.Risky, "ri60vo180404 เป็นเกาะ Risky (ใช้สูตร default)");
        Expect(volcano != null && volcano.BiomeEffects.TryGetValue(Biome.Volcanic, out FatigueCategory[] vc)
               && Array.IndexOf(vc, FatigueCategory.VolcanicHeat) >= 0,
            "ri60vo180404: ไบโอมภูเขาไฟ -> หมวด volcanic_heat");
        RegionCatalog.TemplateInfo tundra = RegionCatalog.GetTemplate("ri30tu171228");
        Expect(tundra != null && tundra.BiomeEffects.TryGetValue(Biome.Tundra, out FatigueCategory[] tc)
               && Array.IndexOf(tc, FatigueCategory.Cold) >= 0,
            "ri30tu171228: ไบโอมทุนดรา -> หมวด cold");

        // ── บัพ inside ต้องหักความเหนื่อยได้จริง (ค่าจาก status_effects.json) ──
        double insideDefault = 0.0, insideGale = 0.0, insideHot = 0.0;
        foreach (EffectDetail d2 in StatusEffectCatalog.PackDetails("inside", 1))
        {
            if (d2.Type != EffectType.Fatigue) continue;
            if (d2.Key == "default") insideDefault = d2.Value;
            if (d2.Key == "gale") insideGale = d2.Value;
            if (d2.Key == "hot") insideHot = d2.Value;
        }
        Expect(Math.Abs(insideDefault + 1.0) < 0.001, "inside: default -1.0 (ในบ้านตัดหมวดพื้นฐานทิ้ง)");
        Expect(Math.Abs(insideGale + 1.0) < 0.001, "inside: gale -1.0 (ในบ้านไม่โดนลม)");
        Expect(Math.Abs(insideHot + 0.75) < 0.001, "inside: hot -0.75 (ในบ้านยังร้อนอยู่บ้าง)");

        // ── เลขปลายทางจริง: ผู้เล่น lv20 บนเกาะ Risky lv40 ที่ไบโอมป่าเขตร้อน (หมวด hot) ──
        // basis = (0.04 + 0.001*20) * max(0.5, 1 - 0.05*(20-40)) = 0.06 * 2 = 0.12
        // เลเวลต่ำกว่าเกาะ 20 -> เหนื่อยเป็นสองเท่า ตรงกับคำอธิบายหมวด Default ในไฟล์
        double basis = FatigueTuning.BaseVelocity(Role.Risky, 20, 40);
        Expect(Math.Abs(basis - 0.12) < 0.0001, "lv20 บนเกาะ lv40 -> ความเร็วฐาน 0.12/วิ (สองเท่า)");
        Expect(FatigueTuning.BaseVelocity(Role.Risky, 60, 5) <= basis * 0.5 + 0.0001,
            "เลเวลสูงกว่าเกาะมาก -> ไม่ต่ำกว่าครึ่งของสูตร (max 0.5)");

        float hotRatio = FatigueTuning.Get(FatigueCategory.Hot)?.DefaultRatio ?? 0f;
        double outside = basis * hotRatio;
        double insideHouse = basis * hotRatio * Math.Max(0.0, 1.0 + insideHot);
        Expect(Math.Abs(outside - 0.12) < 0.0001, "กลางแจ้งหมวด hot = 0.12/วิ");
        Expect(Math.Abs(insideHouse - 0.03) < 0.0001, "เข้าบ้านแล้วเหลือ 0.03/วิ (บัพ inside หัก 75%)");
        Expect(insideHouse < outside, "บัพ inside ทำให้เหนื่อยช้าลงจริง ไม่ใช่แค่ไอคอน");
    }
    /// <summary>ความเหนื่อยต่อการกระทำ — fatigue_cost จาก constants.json (2√e คราฟต์, 4√e สร้าง, 0.4√e เก็บ)</summary>
    static void CheckActionFatigue()
    {
        float craft9 = ActionFatigue.Of("craft", 9f);   // 2 * sqrt(9) = 6
        Expect(Math.Abs(craft9 - 6f) < 0.001f, "craft fatigue = 2*sqrt(9) = 6 (ได้ " + craft9 + ")");
        float build4 = ActionFatigue.Of("build", 4f);   // 4 * sqrt(4) = 8
        Expect(Math.Abs(build4 - 8f) < 0.001f, "build fatigue = 4*sqrt(4) = 8 (ได้ " + build4 + ")");
        float collect25 = ActionFatigue.Of("collect", 25f); // 0.4 * sqrt(25) = 2
        Expect(Math.Abs(collect25 - 2f) < 0.001f, "collect fatigue = 0.4*sqrt(25) = 2 (ได้ " + collect25 + ")");
        Expect(ActionFatigue.Of("craft", 0f) == 0f, "พลังงาน 0 → เหนื่อย 0 (ไม่หัก)");
    }
    static void CheckDestructCost()
    {
        // constants.json → build → destruct : energy "10 + durability / 2."  time "5 + durability / 10."
        // durability = time_limited ? 60 : 7 — พิสูจน์ว่าสูตร (มีจุดท้าย) พาร์สได้ ไม่คืน fallback 0
        Expect(Math.Abs(BuildTuning.DefaultTimeLimitedDurability - 60f) < 0.001f,
            "default_time_limited_durability = 60 (ได้ " + BuildTuning.DefaultTimeLimitedDurability + ")");
        double e7 = BuildTuning.DestructEnergy(7f), t7 = BuildTuning.DestructTime(7f);
        Expect(Math.Abs(e7 - 13.5) < 0.001, "รื้อ durability 7: energy = 10+7/2 = 13.5 (ได้ " + e7 + ")");
        Expect(Math.Abs(t7 - 5.7) < 0.001, "รื้อ durability 7: time = 5+7/10 = 5.7 (ได้ " + t7 + ")");
        double e60 = BuildTuning.DestructEnergy(60f), t60 = BuildTuning.DestructTime(60f);
        Expect(Math.Abs(e60 - 40.0) < 0.001, "รื้อ durability 60: energy = 10+60/2 = 40 (ได้ " + e60 + ")");
        Expect(Math.Abs(t60 - 11.0) < 0.001, "รื้อ durability 60: time = 5+60/10 = 11 (ได้ " + t60 + ")");
    }
    static void Expect(bool cond, string title)
    {
        if (cond)
        {
            _passed++;
            Console.WriteLine($"[fx-check] ✓ {title}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"[fx-check] ❌ {title}");
        }
    }
}
