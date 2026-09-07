using System;
using System.Collections.Generic;
using System.IO;
using Shared.Quest;
using Yaml.Util;

namespace DurangoServerNx;

/// <summary>
/// Offline assertions for Phase 1 Daily quests — no TCP, no GameCode edits.
/// Run: <c>DurangoServer --quest-check [--data &lt;dataDir&gt;]</c>
/// </summary>
internal static class QuestCatalogCheck
{
    static int _failed;
    static int _passed;

    public static int Run(string dataDir)
    {
        _failed = 0;
        _passed = 0;
        Durango.Utils.Json.DataDir = dataDir;
        if (!Directory.Exists(Path.Combine(dataDir, "assets", "quests")))
        {
            Console.WriteLine($"[quest-check] ❌ ไม่พบ {dataDir}/assets/quests");
            return 1;
        }

        QuestCatalog.Load();
        CheckLoadCounts();
        CheckOriginalThreeStillLive();
        CheckBeyondOriginalThree();
        CheckUnknownGaps();
        CheckEventMatching();
        CheckProgressAndClaim();
        CheckDailyResetClock();
        CheckTrackedNotSunset();

        Console.WriteLine($"[quest-check] ผ่าน {_passed} · ตก {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    static void CheckLoadCounts()
    {
        Expect(QuestCatalog.Loaded, "โหลดแคตตาล็อกแล้ว");
        Expect(QuestCatalog.All.Count >= 1300, $"มีแถวจาก assets (ได้ {QuestCatalog.All.Count})");
        int daily = 0, live = 0;
        foreach (QuestDef def in QuestCatalog.InCategory(QuestCatalog.DailyCategory))
        {
            daily++;
            if (def.IsLive) live++;
        }
        Expect(daily == 30, $"หมวด daily จาก assets = 30 (ได้ {daily})");
        Expect(live >= 14, $"Daily ที่เล่นได้ ≥ 14 (ได้ {live})");
    }

    static void CheckOriginalThreeStillLive()
    {
        ExpectLive("daily_gathering_a_01", QuestEventType.Collected, QuestCatalog.Filters.Gather, 20);
        ExpectLive("daily_weaponcrafting_b_01", QuestEventType.Crafted, QuestCatalog.Filters.Tool, 5);
        ExpectLive("daily_constructing_a_01", QuestEventType.Built, "", 2);
    }

    static void CheckBeyondOriginalThree()
    {
        ExpectLive("daily_hunting_a_01", QuestEventType.Hunted, "", 10);
        ExpectLive("daily_hunting_b_01", QuestEventType.Hunted, "", 10);
        ExpectLive("daily_hunting_c_01", QuestEventType.Hunted, "", 10);
        ExpectLive("daily_weaponcrafting_a_01", QuestEventType.Crafted, QuestCatalog.Filters.Weapon, 5);
        ExpectLive("daily_armorcrafting_a_01", QuestEventType.Crafted, QuestCatalog.Filters.Clothing, 3);
        ExpectLive("daily_cooking_a_01", QuestEventType.Crafted, QuestCatalog.Filters.Cook, 10);
        ExpectLive("daily_cooking_b_02", QuestEventType.Crafted, QuestCatalog.Filters.Cook, 5);
        ExpectLive("daily_process_a_01", QuestEventType.Crafted, QuestCatalog.Filters.Process, 10);
        ExpectLive("daily_process_b_01", QuestEventType.Crafted, QuestCatalog.Filters.Process, 4);
        ExpectLive("daily_butchery_a_01", QuestEventType.Collected, QuestCatalog.Filters.Carcass, 10);
        ExpectLive("daily_farming_a_01", QuestEventType.Farmed, "", 10);
    }

    static void CheckUnknownGaps()
    {
        QuestDef mission = QuestCatalog.Find("mission_finish_1");
        Expect(mission != null && !mission.IsLive, "mission_finish_1 ไม่เล่นในเฟส 1");
        Expect(mission != null && mission.Event == QuestEventType.MissionUpdated, "mission_finish_* = MissionUpdated");

        QuestDef biome = QuestCatalog.Find("daily_hunting_d_01");
        Expect(biome != null && !biome.IsLive, "daily_hunting_d_01 (ไบโอม) ยัง UNKNOWN");

        QuestDef once = QuestCatalog.Find("advisor_combat_onehand_master");
        Expect(once != null && once.Type == QuestType.Once && !once.IsLive,
            "Once/permanent โหลดได้แต่ยังไม่เปิด");

        QuestDef story = QuestCatalog.Find("web_daily_gathering");
        Expect(story != null && !story.IsLive, "web_daily_* หมวด christmas ยังไม่เปิด");
    }

    static void CheckEventMatching()
    {
        QuestDef gather = QuestCatalog.Find("daily_gathering_a_01");
        Expect(QuestCatalog.Matches(gather, QuestEventType.Collected, QuestCatalog.Filters.Gather),
            "เก็บของเดิน daily_gathering_a_01");
        Expect(!QuestCatalog.Matches(gather, QuestEventType.Collected, QuestCatalog.Filters.Carcass),
            "ชำแหละไม่เดิน daily_gathering_a_01");
        Expect(!QuestCatalog.Matches(gather, QuestEventType.Crafted, "cook"),
            "คราฟต์ไม่เดิน daily_gathering_a_01");

        QuestDef butcher = QuestCatalog.Find("daily_butchery_a_01");
        Expect(QuestCatalog.Matches(butcher, QuestEventType.Collected, QuestCatalog.Filters.Carcass),
            "ชำแหละเดิน daily_butchery_a_01");
        Expect(!QuestCatalog.Matches(butcher, QuestEventType.Collected, QuestCatalog.Filters.Gather),
            "เก็บของไม่เดิน daily_butchery_a_01");

        QuestDef weapon = QuestCatalog.Find("daily_weaponcrafting_a_01");
        Expect(QuestCatalog.Matches(weapon, QuestEventType.Crafted, "weapon_and_tool"),
            "weapon_and_tool เดิน daily_weaponcrafting_a_01");
        Expect(!QuestCatalog.Matches(weapon, QuestEventType.Crafted, "tool"),
            "tool ไม่เดิน daily_weaponcrafting_a_01");

        QuestDef tool = QuestCatalog.Find("daily_weaponcrafting_b_01");
        Expect(QuestCatalog.Matches(tool, QuestEventType.Crafted, "tool"),
            "tool เดิน daily_weaponcrafting_b_01");
        Expect(QuestCatalog.Matches(tool, QuestEventType.Crafted, "tool_season2"),
            "tool_season2 เดิน daily_weaponcrafting_b_01");
        Expect(!QuestCatalog.Matches(tool, QuestEventType.Crafted, "weapon_and_tool"),
            "weapon_and_tool ไม่เดิน daily_weaponcrafting_b_01");

        QuestDef cook = QuestCatalog.Find("daily_cooking_a_01");
        Expect(QuestCatalog.Matches(cook, QuestEventType.Crafted, "cook"), "cook เดิน daily_cooking_a_01");
        Expect(QuestCatalog.Matches(cook, QuestEventType.Crafted, "cook_season2"),
            "cook_season2 เดิน daily_cooking_a_01");

        QuestDef process = QuestCatalog.Find("daily_process_a_01");
        Expect(QuestCatalog.Matches(process, QuestEventType.Crafted, "material_process"),
            "material_process เดิน daily_process_a_01");
        Expect(QuestCatalog.Matches(process, QuestEventType.Crafted, "process_season2"),
            "process_season2 เดิน daily_process_a_01");

        QuestDef hunt = QuestCatalog.Find("daily_hunting_a_01");
        Expect(QuestCatalog.Matches(hunt, QuestEventType.Hunted, null), "ล่าเดิน daily_hunting_a_01");

        QuestDef biome = QuestCatalog.Find("daily_hunting_d_01");
        Expect(!QuestCatalog.Matches(biome, QuestEventType.Hunted, null),
            "ไบโอมล่ายังไม่เดินความคืบหน้า");
    }

    static void CheckProgressAndClaim()
    {
        QuestDef def = QuestCatalog.Find("daily_gathering_a_01");
        var row = new QuestProgressRow { State = QuestState.WorkInProgress, Progress = 0, GoalCount = def.GoalCount };
        Expect(QuestCatalog.TryApply(row, def, 1) && row.Progress == 1 && row.State == QuestState.WorkInProgress,
            "เก็บครั้งแรก = WIP 1/20");
        Expect(QuestCatalog.TryApply(row, def, 19) && row.Progress == 20 && row.State == QuestState.ReachTheGoal,
            "ครบเป้า = ReachTheGoal");
        Expect(!QuestCatalog.TryApply(row, def, 1) && row.Progress == 20,
            "หลังถึงเป้าแล้วไม่บวกต่อ");
        row.State = QuestState.Finished;
        Expect(!QuestCatalog.TryApply(row, def, 1), "หลังรับรางวัลแล้วไม่บวกต่อ");
    }

    static void CheckDailyResetClock()
    {
        var kstNoon = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.FromHours(9));
        Expect(QuestCatalog.CurrentResetDay(kstNoon.ToUniversalTime()) == "2026-09-07",
            "วันรีเซ็ต KST = 2026-09-07");
        double endAt = QuestCatalog.NextResetUnix(kstNoon.ToUniversalTime());
        var next = DateTimeOffset.FromUnixTimeSeconds((long)endAt);
        Expect(next.ToOffset(TimeSpan.FromHours(9)).Hour == 0 &&
               next.ToOffset(TimeSpan.FromHours(9)).Day == 8,
            "EndAt คือเที่ยงคืน KST วันถัดไป");
        Expect(QuestCatalog.ShouldResetDaily("2026-09-06", kstNoon.ToUniversalTime()),
            "วันเก่าต้องรีเซ็ต");
        Expect(!QuestCatalog.ShouldResetDaily("2026-09-07", kstNoon.ToUniversalTime()),
            "วันเดียวกันไม่รีเซ็ต");
        Expect(!QuestCatalog.ShouldResetDaily("", kstNoon.ToUniversalTime()),
            "วันว่าง (ผู้เล่นใหม่) ไม่รีเซ็ต");
        Expect(!QuestCatalog.ShouldResetDaily(null, kstNoon.ToUniversalTime()),
            "วัน null ไม่รีเซ็ต");
        // ถ้า FlushQuestSave ประทับวันนี้ก่อนรีเซ็ต แถวเก่าจะค้าง — ShouldResetDaily ต้องยังเห็นวันเก่า
        Expect(QuestCatalog.ShouldResetDaily("2026-09-06", kstNoon.ToUniversalTime()) &&
               QuestCatalog.CurrentResetDay(kstNoon.ToUniversalTime()) == "2026-09-07",
            "วันเซฟกับวันปัจจุบันคนละค่าจนกว่าจะรีเซ็ตจริง");
    }

    static void CheckTrackedNotSunset()
    {
        Expect(QuestCatalog.IsTracked("daily_gathering_a_01"), "daily_gathering_a_01 ถูกติดตาม");
        Expect(QuestCatalog.IsTracked("mission_finish_1"), "mission_finish_1 อยู่ในหมวด daily จึงติดตามสถานะ");
        Expect(!QuestCatalog.IsTracked("advisor_combat_onehand_master"), "Once/permanent ไม่ติดตามในเฟส 1");
        Expect(QuestCatalog.IsPlayableCategory("daily"), "หมวด daily เปิดใน UI");
        Expect(!QuestCatalog.IsPlayableCategory("sunset"), "sunset ไม่ใช่หมวดที่เฟส 1 เปิดเอง");
        Expect(!QuestCatalog.IsPlayableCategory("permanent"), "permanent ยังไม่เปิดแท็บ");
    }

    static void ExpectLive(string id, QuestEventType ev, string filter, int goal)
    {
        QuestDef def = QuestCatalog.Find(id);
        Expect(def != null, $"มี {id} ในแคตตาล็อก");
        if (def == null) return;
        Expect(def.IsLive, $"{id} เล่นได้");
        Expect(def.Event == ev, $"{id} event={ev} (ได้ {def.Event})");
        Expect(string.Equals(def.Filter ?? "", filter ?? "", StringComparison.Ordinal),
            $"{id} filter={filter} (ได้ {def.Filter})");
        Expect(def.GoalCount == goal, $"{id} เป้า={goal} (ได้ {def.GoalCount})");
        Expect(def.Type == QuestType.Daily, $"{id} เป็น Daily");
    }

    static void Expect(bool cond, string title)
    {
        if (cond)
        {
            _passed++;
            Console.WriteLine($"[quest-check] ✓ {title}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"[quest-check] ❌ {title}");
        }
    }
}
