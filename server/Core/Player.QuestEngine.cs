using System;
using System.Collections.Generic;
using Messages;
using Shared.Quest;
using Yaml.Util;
using SkillCat = Shared.Skill.Category;
using QuestStateEnum = Shared.Quest.QuestState;

namespace Durango.Online;

/// <summary>
/// Phase 1 quest engine — Daily (and Once when the same count-event pipeline applies).
/// Progress lives in <see cref="QuestStore"/> and is flushed to <c>_context.Quests</c>.
/// </summary>
public partial class Player
{
    bool _questsHydrated;

    /// <summary>
    /// โหลดความคืบหน้าจากไฟล์เซฟ แล้วเติมแถว Daily ที่ยังไม่มี
    /// เรียกจาก <see cref="RegisterQuestHandlers"/> ก่อนส่ง QuestCategories
    /// </summary>
    void HydrateQuests()
    {
        if (_questsHydrated) return;
        _questsHydrated = true;

        ContextChanged += FlushQuestSave;

        var loaded = new Dictionary<string, QuestStore.Entry>(StringComparer.Ordinal);
        if (_context.Quests != null)
        {
            foreach (KeyValuePair<string, QuestSaveData> kv in _context.Quests)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                loaded[kv.Key] = new QuestStore.Entry
                {
                    State = kv.Value.State,
                    Progress = Math.Max(0, kv.Value.Progress),
                    GoalCount = Math.Max(1, kv.Value.GoalCount)
                };
            }
        }
        QuestStore.ReplaceAll(EntityId, loaded);

        string today = QuestCatalog.CurrentResetDay();
        if (QuestCatalog.ShouldResetDaily(_context.QuestDailyResetDay))
        {
            ResetDailyQuests();
            Console.WriteLine($"[เควส] {Short(EntityId)} รีเซ็ต Daily (วัน {_context.QuestDailyResetDay} → {today})");
        }
        _context.QuestDailyResetDay = today;

        foreach (QuestDef def in QuestCatalog.InCategory(QuestCatalog.DailyCategory))
        {
            QuestStore.Ensure(EntityId, def);
        }

        FlushQuestSave();
    }

    void ResetDailyQuests()
    {
        foreach (QuestDef def in QuestCatalog.InCategory(QuestCatalog.DailyCategory))
        {
            QuestStore.Set(EntityId, def.Id, QuestStateEnum.WorkInProgress, 0, Math.Max(1, def.GoalCount));
        }
    }

    void FlushQuestSave()
    {
        Dictionary<string, QuestStore.Entry> snap = QuestStore.Snapshot(EntityId);
        var save = new Dictionary<string, QuestSaveData>(snap.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, QuestStore.Entry> kv in snap)
        {
            save[kv.Key] = new QuestSaveData
            {
                State = kv.Value.State,
                Progress = kv.Value.Progress,
                GoalCount = kv.Value.GoalCount
            };
        }
        _context.Quests = save;
        _context.QuestDailyResetDay = QuestCatalog.CurrentResetDay();
    }

    /// <summary>โฆษณาแท็บ Daily หลัง QuestCategories ถึงฝั่งเกมแล้ว</summary>
    void AnnouncePlayableQuests()
    {
        QuestToDo[] todos = DailyTodos();
        if (todos.Length == 0) return;
        Send(new QuestStarted
        {
            Category = QuestCatalog.DailyCategory,
            Quests = todos
        });
    }

    QuestToDo[] DailyTodos()
    {
        IReadOnlyList<QuestDef> defs = QuestCatalog.InCategory(QuestCatalog.DailyCategory);
        var todos = new QuestToDo[defs.Count];
        for (int i = 0; i < defs.Count; i++)
        {
            QuestStore.Ensure(EntityId, defs[i]);
            todos[i] = QuestStore.ToQuestToDo(EntityId, defs[i].Id);
        }
        return todos;
    }

    int CountClaimableDaily()
    {
        int n = 0;
        foreach (QuestDef def in QuestCatalog.InCategory(QuestCatalog.DailyCategory))
        {
            if (QuestStore.StateOf(EntityId, def.Id) == QuestStateEnum.ReachTheGoal) n++;
        }
        return n;
    }

    /// <summary>
    /// จุดรวมจากระบบเล่นจริง — Gathering/Crafting/Building/Hunting/Farm เรียกตรงนี้
    /// <paramref name="detail"/> = gather/carcass หรือ recipe.category
    /// </summary>
    public void NoteQuestEvent(QuestEventType ev, string detail = null, int amount = 1)
    {
        if (amount <= 0) return;
        if (QuestCatalog.ShouldResetDaily(_context.QuestDailyResetDay))
        {
            ResetDailyQuests();
            _context.QuestDailyResetDay = QuestCatalog.CurrentResetDay();
        }

        bool any = false;
        foreach (QuestDef def in QuestCatalog.InCategory(QuestCatalog.DailyCategory))
        {
            if (!QuestCatalog.Matches(def, ev, detail)) continue;
            QuestStore.Entry entry = QuestStore.Ensure(EntityId, def);
            if (entry == null) continue;
            var row = new QuestProgressRow
            {
                State = entry.State,
                Progress = entry.Progress,
                GoalCount = entry.GoalCount
            };
            if (!QuestCatalog.TryApply(row, def, amount)) continue;
            QuestStore.Set(EntityId, def.Id, row.State, row.Progress, row.GoalCount);
            any = true;
            bool finished = row.State == QuestStateEnum.Finished;
            Send(new NotifyQuestProceed
            {
                QuestId = def.Id,
                Progress = row.Progress,
                GoalCount = row.GoalCount,
                Finished = finished
            });
            if (row.State == QuestStateEnum.ReachTheGoal)
            {
                Console.WriteLine($"[เควส] {Short(EntityId)} ถึงเป้า '{def.Id}' ({row.Progress}/{row.GoalCount})");
            }
        }
        if (any) OnContextChanged();
    }

    /// <summary>
    /// กดรับรางวัล Daily ที่ ReachTheGoal — จ่าย skill exp (assets ไม่มีตารางไอเทม)
    /// คืน true เมื่อเคลมสำเร็จ (caller ต้องไม่ Abort)
    /// </summary>
    bool TryClaimPlayableQuestReward(string questId, uint seq)
    {
        QuestDef def = QuestCatalog.Find(questId);
        if (def == null || !QuestCatalog.IsTracked(questId)) return false;

        QuestStore.Entry entry = QuestStore.Find(EntityId, questId);
        if (entry == null || entry.State != QuestStateEnum.ReachTheGoal)
        {
            Console.WriteLine($"[เควส] {Short(EntityId)} ขอรับ '{questId}' แต่ยังไม่ถึงเป้า");
            Send(new Abort { Text = "เควสนี้ยังรับรางวัลไม่ได้" }, seq);
            return true;
        }

        int exp = PreviewActionExp(SkillTuning.QuestClaimWeight);
        SkillCat? skill = SkillForQuest(def);
        AddExpForAction(SkillTuning.QuestClaimWeight, skill, $"เควส {questId}");

        QuestStore.Set(EntityId, questId, QuestStateEnum.Finished, Math.Max(entry.GoalCount, entry.Progress),
            Math.Max(1, entry.GoalCount));
        OnContextChanged();

        var reward = new RewardInfo
        {
            Exp = exp,
            QuestScore = 10
        };

        Send(new NotifyQuestProceed
        {
            QuestId = questId,
            Progress = entry.GoalCount,
            GoalCount = entry.GoalCount,
            Finished = true
        });

        Send(new QuestRewardResults
        {
            Category = def.Category,
            QuestId = questId,
            Reward = reward,
            QuestScoreInfos = new QuestScoreInfos
            {
                Category = def.Category,
                CurQuestScore = 0,
                QuestScoreRewards = Array.Empty<QuestScoreReward>()
            }
        });

        Console.WriteLine($"[เควส] {Short(EntityId)} รับรางวัล '{questId}' (exp {exp})");
        return true;
    }

    static SkillCat? SkillForQuest(QuestDef def)
    {
        if (def == null) return null;
        if (def.Event == QuestEventType.Hunted) return SkillCat.MeleeCombat;
        if (def.Event == QuestEventType.Built) return SkillCat.Constructing;
        if (def.Event == QuestEventType.Farmed) return SkillCat.Farming;
        if (def.Event == QuestEventType.Collected)
        {
            return def.Filter == QuestCatalog.Filters.Carcass ? SkillCat.Butchery : SkillCat.Gathering;
        }
        if (def.Event == QuestEventType.Crafted)
        {
            if (def.Filter == QuestCatalog.Filters.Cook) return SkillCat.Cooking;
            if (def.Filter == QuestCatalog.Filters.Clothing) return SkillCat.Armorcrafting;
            if (def.Filter == QuestCatalog.Filters.Process) return SkillCat.Process;
            if (def.Filter == QuestCatalog.Filters.Weapon || def.Filter == QuestCatalog.Filters.Tool)
                return SkillCat.Weaponcrafting;
        }
        return null;
    }
}
