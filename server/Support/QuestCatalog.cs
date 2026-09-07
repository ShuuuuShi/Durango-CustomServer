using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Durango.Utils;
using Newtonsoft.Json;
using Shared.Quest;

namespace Yaml.Util;

/// <summary>
/// Phase 1 quest definitions loaded from <c>quests/quests_for_client.json</c>.
///
/// The client asset has display text + <c>quest_type</c> + <c>category</c> only — no
/// structured objectives or rewards. This loader keeps the raw rows and applies a
/// small curated map from Daily (and Once, when the same count-event pipeline
/// applies) ids onto <see cref="QuestEventType"/> so gameplay can drive progress.
/// </summary>
public static class QuestCatalog
{
    public const string DailyCategory = "daily";

    /// <summary>Canonical event filters written into <see cref="QuestDef.Filter"/>.</summary>
    public static class Filters
    {
        public const string Gather = "gather";
        public const string Carcass = "carcass";
        public const string Weapon = "weapon";
        public const string Tool = "tool";
        public const string Clothing = "clothing";
        public const string Cook = "cook";
        public const string Process = "process";
    }

    static readonly Regex FirstNumber = new(@"(\d+)", RegexOptions.Compiled);
    static readonly Dictionary<string, QuestDef> ById = new(StringComparer.Ordinal);
    static readonly Dictionary<string, List<QuestDef>> ByCategory = new(StringComparer.Ordinal);

    public static bool Loaded { get; private set; }

    public static IReadOnlyDictionary<string, QuestDef> All => ById;

    public static void Load()
    {
        ById.Clear();
        ByCategory.Clear();
        Loaded = false;

        var raw = Json.ReadFromFile<Dictionary<string, QuestAssetRow>>("quests/quests_for_client");
        if (raw == null || raw.Count == 0)
        {
            Console.WriteLine("[เควส] ไม่พบ quests/quests_for_client — แคตตาล็อกว่าง");
            Loaded = true;
            return;
        }

        int daily = 0, once = 0, live = 0;
        foreach (KeyValuePair<string, QuestAssetRow> pair in raw)
        {
            if (string.IsNullOrEmpty(pair.Key) || pair.Value == null) continue;
            QuestDef def = FromAsset(pair.Key, pair.Value);
            ById[def.Id] = def;
            if (!ByCategory.TryGetValue(def.Category, out List<QuestDef> list))
            {
                list = new List<QuestDef>();
                ByCategory[def.Category] = list;
            }
            list.Add(def);
            if (def.Type == QuestType.Daily) daily++;
            else if (def.Type == QuestType.Once) once++;
            if (def.IsLive) live++;
        }

        Loaded = true;
        Console.WriteLine($"[เควส] โหลดแคตตาล็อก {ById.Count} แถว (Daily {daily} · Once {once} · เล่นได้ {live})");
    }

    public static QuestDef Find(string questId)
    {
        if (string.IsNullOrEmpty(questId)) return null;
        return ById.TryGetValue(questId, out QuestDef def) ? def : null;
    }

    /// <summary>Daily (and later Once) categories the server actually lists to the client.</summary>
    public static bool IsPlayableCategory(string category)
        => string.Equals(category, DailyCategory, StringComparison.Ordinal);

    /// <summary>Id is a Daily/Once row we persist and answer with real state (not sunset Finished).</summary>
    public static bool IsTracked(string questId)
    {
        QuestDef def = Find(questId);
        return def != null && IsPlayableCategory(def.Category);
    }

    public static IReadOnlyList<QuestDef> InCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return Array.Empty<QuestDef>();
        return ByCategory.TryGetValue(category, out List<QuestDef> list) ? list : Array.Empty<QuestDef>();
    }

    public static IEnumerable<QuestDef> Live
    {
        get
        {
            foreach (QuestDef def in ById.Values)
            {
                if (def.IsLive) yield return def;
            }
        }
    }

    internal static QuestDef FromAsset(string id, QuestAssetRow row)
    {
        string category = row.Category ?? string.Empty;
        var type = (QuestType)row.QuestType;
        string description = row.Description?.MsgId ?? string.Empty;
        int goal = ParseGoal(description);
        Classify(id, category, type, description, goal, out QuestEventType ev, out string filter,
            out bool live, out string unknown);

        return new QuestDef
        {
            Id = id,
            Category = category,
            Type = type,
            Subject = row.Subject?.MsgId ?? id,
            Description = description,
            GoalCount = Math.Max(1, goal),
            Event = ev,
            Filter = filter ?? string.Empty,
            IsLive = live,
            UnknownReason = unknown ?? string.Empty,
            DisplayOnHud = row.DisplayOnHud,
            Order = row.Order
        };
    }

    public static int ParseGoal(string description)
    {
        if (string.IsNullOrEmpty(description)) return 1;
        Match m = FirstNumber.Match(description);
        if (!m.Success) return 1;
        return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0
            ? n
            : 1;
    }

    static void Classify(string id, string category, QuestType type, string description, int goal,
        out QuestEventType ev, out string filter, out bool live, out string unknown)
    {
        ev = QuestEventType.Invalid;
        filter = string.Empty;
        live = false;
        unknown = string.Empty;

        // Phase 1 only activates the official Daily tab. Once rows are parsed so the
        // next agent can see them, but they are not the same count-event pipeline
        // (advisor courses, level gates, event islands).
        if (!string.Equals(category, DailyCategory, StringComparison.Ordinal) || type != QuestType.Daily)
        {
            unknown = type == QuestType.Once
                ? "Once อยู่นอกหมวด daily — ส่วนใหญ่เป็น advisor/เลเวล/อีเวนต์ ไม่ใช่ตัวนับเหตุการณ์"
                : "นอกหมวด daily (อีเวนต์/คริสต์มาส/เมือง ฯลฯ) — ยังไม่เปิดในเฟส 1";
            return;
        }

        if (id.StartsWith("mission_finish", StringComparison.Ordinal))
        {
            ev = QuestEventType.MissionUpdated;
            unknown = "ต้องการระบบภารกิจฝ่าย (Faction Missions) — นอกขอบเขตเฟส 1";
            return;
        }

        if (id.StartsWith("daily_hunting_a", StringComparison.Ordinal) ||
            id.StartsWith("daily_hunting_b", StringComparison.Ordinal) ||
            id.StartsWith("daily_hunting_c", StringComparison.Ordinal))
        {
            ev = QuestEventType.Hunted;
            live = true;
            return;
        }

        if (id.StartsWith("daily_hunting_", StringComparison.Ordinal))
        {
            ev = QuestEventType.Hunted;
            unknown = "ล่าบนเกาะไบโอมเฉพาะ — เซิร์ฟยังไม่กรองเกาะ/ไบโอมของเควสนี้";
            return;
        }

        if (id.StartsWith("daily_weaponcrafting_a", StringComparison.Ordinal))
        {
            ev = QuestEventType.Crafted;
            filter = Filters.Weapon;
            live = true;
            return;
        }

        if (id.StartsWith("daily_weaponcrafting_b", StringComparison.Ordinal))
        {
            ev = QuestEventType.Crafted;
            filter = Filters.Tool;
            live = true;
            return;
        }

        if (id.StartsWith("daily_armorcrafting", StringComparison.Ordinal))
        {
            ev = QuestEventType.Crafted;
            filter = Filters.Clothing;
            live = true;
            return;
        }

        if (id.StartsWith("daily_cooking_a", StringComparison.Ordinal) ||
            id.StartsWith("daily_cooking_b", StringComparison.Ordinal))
        {
            ev = QuestEventType.Crafted;
            filter = Filters.Cook;
            live = true;
            if (id.StartsWith("daily_cooking_b", StringComparison.Ordinal))
            {
                unknown = "คำอธิบายคือ 'แปรรูปวัตถุดิบ' แต่ไฟล์สูตรไม่มีหมวดแยก — นับเป็น cook ไปด้วย";
            }
            return;
        }

        if (id.StartsWith("daily_constructing", StringComparison.Ordinal))
        {
            ev = QuestEventType.Built;
            live = true;
            return;
        }

        if (id.StartsWith("daily_process", StringComparison.Ordinal))
        {
            ev = QuestEventType.Crafted;
            filter = Filters.Process;
            live = true;
            if (id.StartsWith("daily_process_b", StringComparison.Ordinal))
            {
                unknown = "คำอธิบายคือ '다듬기/แต่งวัสดุ' แต่ใช้หมวด material_process เดียวกับ process_a";
            }
            return;
        }

        if (id.StartsWith("daily_gathering", StringComparison.Ordinal))
        {
            ev = QuestEventType.Collected;
            filter = Filters.Gather;
            live = true;
            return;
        }

        if (id.StartsWith("daily_butchery", StringComparison.Ordinal))
        {
            ev = QuestEventType.Collected;
            filter = Filters.Carcass;
            live = true;
            return;
        }

        if (id.StartsWith("daily_farming", StringComparison.Ordinal))
        {
            ev = QuestEventType.Farmed;
            live = true;
            return;
        }

        unknown = "Daily ในหมวด daily แต่ยังไม่มีตัวจับคู่ QuestEventType";
        _ = description;
        _ = goal;
    }

    /// <summary>
    /// Does this gameplay event advance <paramref name="def"/>?
    /// <paramref name="detail"/> is carcass/gather for Collected, or the recipe
    /// <c>category</c> string for Crafted.
    /// </summary>
    public static bool Matches(QuestDef def, QuestEventType ev, string detail)
    {
        if (def == null || !def.IsLive || def.Event != ev) return false;
        if (string.IsNullOrEmpty(def.Filter)) return true;
        if (ev == QuestEventType.Collected)
        {
            return string.Equals(def.Filter, detail, StringComparison.OrdinalIgnoreCase);
        }
        if (ev == QuestEventType.Crafted)
        {
            return CraftMatches(def.Filter, detail);
        }
        return true;
    }

    public static bool CraftMatches(string filter, string recipeCategory)
    {
        recipeCategory ??= string.Empty;
        if (string.Equals(filter, Filters.Weapon, StringComparison.Ordinal))
            return recipeCategory.StartsWith("weapon", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(filter, Filters.Tool, StringComparison.Ordinal))
            return recipeCategory.StartsWith("tool", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(filter, Filters.Clothing, StringComparison.Ordinal))
            return recipeCategory.StartsWith("clothing", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(filter, Filters.Cook, StringComparison.Ordinal))
            return recipeCategory.StartsWith("cook", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(filter, Filters.Process, StringComparison.Ordinal))
        {
            return recipeCategory.StartsWith("material_process", StringComparison.OrdinalIgnoreCase) ||
                   recipeCategory.StartsWith("process", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>
    /// Apply one matching event to an in-memory row. Returns false when the row
    /// should not move (already claimed / already at the goal / not live).
    /// </summary>
    public static bool TryApply(QuestProgressRow row, QuestDef def, int amount)
    {
        if (row == null || def == null || !def.IsLive || amount <= 0) return false;
        if (row.State == QuestState.Finished || row.State == QuestState.ReachTheGoal) return false;
        int goal = Math.Max(1, def.GoalCount);
        int next = Math.Min(goal, row.Progress + amount);
        if (next == row.Progress) return false;
        row.Progress = next;
        row.GoalCount = goal;
        if (row.Progress >= goal) row.State = QuestState.ReachTheGoal;
        else row.State = QuestState.WorkInProgress;
        return true;
    }

    /// <summary>KST (UTC+9) calendar day used as the Daily reset key.</summary>
    public static string CurrentResetDay(DateTimeOffset? utcNow = null)
    {
        DateTimeOffset now = (utcNow ?? DateTimeOffset.UtcNow).ToOffset(KstOffset);
        return now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Unix seconds of the next KST midnight — written into <c>QuestToDo.EndAt</c>.</summary>
    public static double NextResetUnix(DateTimeOffset? utcNow = null)
    {
        DateTimeOffset now = (utcNow ?? DateTimeOffset.UtcNow).ToOffset(KstOffset);
        DateTimeOffset next = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, KstOffset).AddDays(1);
        return next.ToUnixTimeSeconds();
    }

    public static bool ShouldResetDaily(string savedDay, DateTimeOffset? utcNow = null)
    {
        if (string.IsNullOrEmpty(savedDay)) return false;
        return !string.Equals(savedDay, CurrentResetDay(utcNow), StringComparison.Ordinal);
    }

    static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);
}

public sealed class QuestDef
{
    public string Id;
    public string Category;
    public QuestType Type;
    public string Subject;
    public string Description;
    public int GoalCount;
    public QuestEventType Event;
    public string Filter;
    public bool IsLive;
    public string UnknownReason;
    public bool DisplayOnHud;
    public int Order;
}

/// <summary>Plain progress row used by tests and by <see cref="Durango.Online.Player.QuestStore"/>.</summary>
public sealed class QuestProgressRow
{
    public QuestState State;
    public int Progress;
    public int GoalCount = 1;
}

public sealed class QuestAssetRow
{
    [JsonProperty("quest_type")] public int QuestType;
    [JsonProperty("category")] public string Category;
    [JsonProperty("subject")] public Gettext Subject;
    [JsonProperty("description")] public Gettext Description;
    [JsonProperty("display_on_hud")] public bool DisplayOnHud;
    [JsonProperty("auto_finish")] public bool AutoFinish;
    [JsonProperty("order")] public int Order;
    [JsonProperty("icon")] public string Icon;
}
