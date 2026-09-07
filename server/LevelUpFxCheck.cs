using System;
using System.Collections.Generic;
using Durango.Online;
using Messages;
using Shared.Skill;

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
