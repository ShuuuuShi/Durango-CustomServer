using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Network;
using Durango.Utils;
using Messages;
using Newtonsoft.Json;
using Shared.Battle;
using Shared.Teleport;
using Yaml;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
// ระบบต่อสู้ — [5 ก.ย. 2026]
//
// ลำดับ message ที่ตัวเกมคาดหวัง (ยืนยันจากซอร์สจริง):
//
//   เข้าเกม  client → GetActions(314)          (client/CombatSystem.cs:180-183 OnReady)
//            server → Actions(315)
//            ⚠️ **ไม่ตอบ = ไม่มีปุ่มท่าต่อสู้เลยทั้งจอ** เพราะ ActionSlots ถูกสร้างจาก
//              _currentActions ซึ่งมีที่มาเดียวคือ message นี้ (CombatSystem.cs:262-303, 305-367)
//              ท่าที่ FillPlaceholderActions หามาเองเป็นแค่ "ไอคอนจาง ๆ" กดไม่ได้ (:592-642)
//
//   เล็งเป้า client → SelectBattleTarget(3441) (CombatSystem.cs:155-165) — ไม่รอคำตอบ
//   ใช้ท่า   client → UseBattleAction(3440)    (client/Durango.Logic.Combat/UsingAction.cs:218-224)
//            ⚠️ ส่งแบบ "ยิงแล้วลืม" ไม่มี .On ⇒ ห้ามคาดหวังว่าจะตอบที่ seq นี้
//            ผลของท่าเดินทางกลับด้วย push แยก: Damaged(12) · BattleBegun(3278) · BattleEnded(3587)
//            (CombatSystem.cs:100-106 ผูก global handler ไว้ทั้งสามตัว)
//
//   ออกจากสู้ client → ExitBattle(3496)        (CombatSystem.cs:652-655) — ไม่รอคำตอบ
//            server → BattleEnded(3587) ที่ EntityId = ตัวผู้เล่นเอง
//            ⚠️ EntityId ที่ไม่ใช่ตัวเอง client จะตีเป็น "สัตว์เลี้ยง" (CombatSystem.cs:531-545)
//              ⇒ BattleBegun/BattleEnded ต้องส่งเฉพาะเจ้าตัว ห้าม broadcast
//
//   ตาย/เกิด server → EntityDied(119) / EntityRevived(119119)
//            ⚠️ เกมไม่มี message "ตาย" โดยตรง — ObjectManager.cs:153-161 เอาสองตัวนี้ไปเรียก
//              CharacterBehavior.SetAlive ซึ่งเป็นตัวจุด Died/Revived ทั้งเกม
//              (เมนู "ชุบชีวิต" ก็โผล่จากตรงนี้ — client/Durango.UI/ContextActionGroupBase.cs:39-46)
//            client → Revive(2101) / ReviveImmediately(210201) (client/PlayerController.cs:496-511)
//              ทั้งคู่ยิงแล้วลืมเหมือนกัน ⇒ ตอบด้วย push: EntityRevived + Teleported + SurvivalUpdated
//
// [5 ก.ย. 2026] มีระบบสัตว์แล้ว — เป้าหมายที่ตีได้คือผู้เล่นคนอื่นบนเกาะเดียวกัน (TryResolveVictim)
//    หรือสัตว์ป่า (Core/Player.Hunting.cs → TryAttackAnimal ซึ่งอ่านบัญชีสัตว์จาก World.AnimalManager)
//    สัตว์ยังไม่มี AI ไม่เดินและไม่ตีกลับ — งานก้อนถัดไป
// ═══════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// ค่าที่ **เราตั้งเอง** ของระบบต่อสู้ — ตัวเลขสมดุลจริงทั้งหมดอ่านจากไฟล์
/// (data/assets/entity_types/players.json · player/player_battle_actions.json ·
///  performance.json · constants.json) ห้ามย้ายมาไว้ที่นี่
/// </summary>
public static class CombatTuning
{
    /// <summary>**ค่าของเรา** — ความเสียหายขั้นต่ำต่อการโดนหนึ่งครั้ง (กันเลข 0 ที่ดูเหมือนบั๊ก)</summary>
    public const int MinDamage = 1;

    /// <summary>
    /// **ค่าของเรา** — ทิศที่ถือว่าโดนโจมตี
    ///
    /// ของจริงคิดจากมุมระหว่างผู้โจมตีกับหน้าของเป้า แล้วคูณ damage_ratio_table
    /// (players.json → damage_ratio_table: front 1.0 · back 1.2 · left/right 1.0)
    /// เซิร์ฟยังไม่ได้เก็บ "ทิศที่ตัวละครหันหน้า" (AppearPlayer.Move เก็บแค่เส้นทาง)
    /// ⇒ ใช้ Front ซึ่งเป็นตัวคูณ 1.0 = ไม่บวกไม่ลบ ปลอดภัยกว่าเดาทิศผิดแล้วดาเมจเพี้ยน
    /// </summary>
    public const DamageDirection HitDirection = DamageDirection.Front;

    /// <summary>
    /// **ค่าของเรา** — จำนวนครั้งที่ตายสูงสุดที่เอาไปเปิดตาราง death_penalty
    /// ตารางจริงมีคีย์ 0..3 (constants.json → death_penalty.gauge_ratio_by_death_count)
    /// ตายเกินกว่านั้นใช้แถวสุดท้าย
    /// </summary>
    public const int MaxDeathCountRow = 3;
}

// ── ตัวข้อมูลของไฟล์เกม (โหลดเองในไฟล์นี้ เพราะคลาสใน Support/ พอร์ตมาเท่าที่ระบบอื่นใช้) ──

/// <summary>ท่าต่อสู้ของผู้เล่นหนึ่งท่า — data/assets/player/player_battle_actions.json</summary>
public class BattleActionData
{
    public BattleActionMeta meta;
    public BattleAttackInfo[] attack_info;
}

public class BattleActionMeta
{
    public int stamina;
    public float cooltime;
    public float action_length;

    /// <summary>
    /// ระยะที่ใช้ท่าได้ — **ต้องเป็น nullable**: ท่าหลบสามตัว (onehand_dodge/twohand_dodge/
    /// barehand_dodge) เขียนไว้เป็น null ตรง ๆ ในไฟล์ ถ้าประกาศเป็น float เฉย ๆ Newtonsoft
    /// จะโยน exception แล้ว Json.Read กลืนเป็น "ไม่มีท่าเลยสักตัว" (Support/Json.cs:39-46)
    /// </summary>
    public float? use_range;

    public string motion;
}

public class BattleAttackInfo
{
    public float damage_bonus;
    public float armor_penetration;
    public float accuracy_ratio;
    public float groggy;
    public Dictionary<string, float> atk_ratio;     // impact / pierce / cut — รวมกันได้ 1.0 เสมอในไฟล์
    public DamageType damage_type;                  // รูปทรงการโจมตี (Melee/Area/Ranged) ไม่ใช่ชนิดอาวุธ
}

/// <summary>tag ของอุปกรณ์ → ท่าที่ใช้ได้ — data/assets/tag_allow_actions.json</summary>
public class TagAllowActionData
{
    public string[] default_actions;
    public string[] skill_actions;
}

/// <summary>ค่าต่อสู้ของผู้เล่นจาก data/assets/entity_types/players.json → "player"</summary>
public class PlayerBattleStats
{
    public int attack;
    public int defense;
    public int dodge;
    public int accuracy;
    public float battle_retreat_time;
    public Dictionary<string, float> damage_ratio_table;             // front/back/left/right
    public Dictionary<string, PlayerBodyPart> body_parts;            // มีแค่ "body"
    public PlayerBareHands bare_hands;
}

public class PlayerBodyPart
{
    public float dodge_ratio;
    public Dictionary<string, float> defense_ratio;                  // impact / pierce / cut
    public float max_hp;
}

public class PlayerBareHands
{
    public string attack_type;                                       // "bare_hands"
}

/// <summary>constants.json → death_penalty (บทลงโทษตอนตาย)</summary>
public class DeathPenaltyData
{
    public float default_item_drop_ratio;
    public float death_point_remaining_duration;
    public Dictionary<string, float> gauge_ratio_by_death_count;     // "0".."3" → สัดส่วนหลอดตอนฟื้น
    public float[] fatigue_recovery_ratio_by_death_count;
}

/// <summary>constants.json → revive_immediately (ฟื้นทันที)</summary>
public class ReviveImmediatelyData
{
    public Dictionary<string, float> gauge_ratio;                    // stamina/life/health/energy → 0.7
}

/// <summary>เฉพาะสองบล็อกที่ระบบต่อสู้ใช้จาก constants.json (Support/YamlConstants.cs พอร์ตมาไม่ครบ)</summary>
public class CombatConstantsData
{
    public DeathPenaltyData death_penalty;
    public ReviveImmediatelyData revive_immediately;
}

/// <summary>ชนิดอาวุธของแต่ละ prototype — performance.json → weapon → &lt;id&gt; → "[1, 60]" → attack_type</summary>
public class WeaponPerformanceData
{
    public string attack_type;                                       // sword/axe/blunt/spear/arrow/stone/bare_hands

    // [5 ก.ย. 2026] ค่าโจมตีของอาวุธ — เป็น "สูตรตามเลเวล" เช่น "72.02 + (level * 1.3)"
    // เดิมข้ามไปเพราะโปรเจกต์ยังไม่มีตัวประเมินสูตร ตอนนี้มีแล้ว (Support/StatFormula.cs)
    public string attack;
}

public class WeaponPerformanceRoot
{
    [JsonProperty("weapon")]
    public Dictionary<string, Dictionary<string, WeaponPerformanceData>> Weapon;
}

/// <summary>
/// ตารางข้อมูลของระบบต่อสู้ — โหลดครั้งเดียวตอนใช้ครั้งแรก (เหตุผลเดียวกับ CraftRecipeStore)
/// </summary>
public static class BattleDataStore
{
    private static readonly object Lock = new();
    private static bool _loaded;

    private static Dictionary<string, BattleActionData> _actions;
    private static Dictionary<string, TagAllowActionData> _tagActions;
    private static PlayerBattleStats _stats;
    private static CombatConstantsData _constants;
    private static Dictionary<string, string> _weaponAttackTypes;
    private static Dictionary<string, string> _weaponAttackFormulas;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (Lock)
        {
            if (_loaded) return;
            _actions = Json.ReadFromFile<Dictionary<string, BattleActionData>>("player/player_battle_actions")
                       ?? new Dictionary<string, BattleActionData>();
            _tagActions = Json.ReadFromFile<Dictionary<string, TagAllowActionData>>("tag_allow_actions")
                          ?? new Dictionary<string, TagAllowActionData>();
            var players = Json.ReadFromFile<Dictionary<string, PlayerBattleStats>>("entity_types/players");
            _stats = players != null && players.TryGetValue("player", out var s) ? s : new PlayerBattleStats();
            _constants = Json.ReadFromFile<CombatConstantsData>("constants") ?? new CombatConstantsData();

            _weaponAttackTypes = new Dictionary<string, string>();
            _weaponAttackFormulas = new Dictionary<string, string>();
            var performance = Json.ReadFromFile<WeaponPerformanceRoot>("performance");
            if (performance?.Weapon != null)
            {
                foreach (var pair in performance.Weapon)
                {
                    // คีย์ชั้นในเป็นช่วงเลเวล "[1, 60]" — ชนิดอาวุธไม่เคยเปลี่ยนตามเลเวล เอาแถวแรกพอ
                    foreach (var byLevel in pair.Value)
                    {
                        if (!string.IsNullOrEmpty(byLevel.Value?.attack_type))
                        {
                            _weaponAttackTypes[pair.Key] = byLevel.Value.attack_type;
                            if (!string.IsNullOrEmpty(byLevel.Value.attack))
                            {
                                _weaponAttackFormulas[pair.Key] = byLevel.Value.attack;
                            }
                            break;
                        }
                    }
                }
            }
            _loaded = true;
            Console.WriteLine($"[combat] โหลดท่าต่อสู้ {_actions.Count} ท่า · tag→ท่า {_tagActions.Count} กลุ่ม · " +
                              $"ชนิดอาวุธ {_weaponAttackTypes.Count} แบบ");
        }
    }

    public static BattleActionData Action(string id)
    {
        EnsureLoaded();
        return string.IsNullOrEmpty(id) ? null : _actions.GetValueOrDefault(id);
    }

    public static TagAllowActionData ActionsOfTag(string tagId)
    {
        EnsureLoaded();
        return string.IsNullOrEmpty(tagId) ? null : _tagActions.GetValueOrDefault(tagId);
    }

    public static PlayerBattleStats Stats
    {
        get { EnsureLoaded(); return _stats; }
    }

    public static DeathPenaltyData DeathPenalty
    {
        get { EnsureLoaded(); return _constants.death_penalty; }
    }

    public static ReviveImmediatelyData ReviveImmediately
    {
        get { EnsureLoaded(); return _constants.revive_immediately; }
    }

    public static string WeaponAttackType(string prototypeId)
    {
        EnsureLoaded();
        return string.IsNullOrEmpty(prototypeId) ? null : _weaponAttackTypes.GetValueOrDefault(prototypeId);
    }

    /// <summary>
    /// ค่าโจมตีของอาวุธชิ้นนั้นที่เลเวลของมัน — คิดจากสูตรจริงใน performance.json
    /// คืน 0 ถ้าไม่ใช่อาวุธหรืออ่านสูตรไม่ออก (ผู้เรียกจะได้ใช้ค่าฐานของตัวละครอย่างเดียว)
    /// </summary>
    public static float WeaponAttack(string prototypeId, int level)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(prototypeId)) return 0f;
        string formula = _weaponAttackFormulas.GetValueOrDefault(prototypeId);
        if (formula == null) return 0f;
        return StatFormula.TryEval(formula, "level", level, out double value) ? (float)Math.Max(0.0, value) : 0f;
    }
}

public partial class Player
{
    /// <summary>
    /// ทะเบียนผู้เล่นที่ออนไลน์อยู่ — ใช้หา "คนที่โดนตี" จาก TargetEntityId
    ///
    /// ทำไมต้องเก็บเอง: <c>World._players</c> เป็น private และไม่มีเมธอดค้นหาให้
    /// (Core/World.cs:44) ส่วน event PlayerAppeared ยิงเฉพาะ "คนที่เพิ่งเข้ามา" ⇒ คนที่อยู่ก่อน
    /// จะไม่มีวันโผล่ใน event ของคนที่มาทีหลัง · ที่นี่ทุก Player ลงทะเบียนตัวเองตอนถูกสร้าง
    /// (RegisterSystemHandlers ถูกเรียกจาก constructor) จึงครบทุกคนแน่นอน
    /// </summary>
    private static readonly Dictionary<string, Player> LivePlayers = new();

    /// <summary>เป้าหมายล่าสุดที่ client เล็งไว้ (SelectBattleTarget) — ใช้ตอน UseBattleAction ไม่ส่ง target มา</summary>
    private string _battleTargetId;

    /// <summary>กำลังอยู่ในโหมดต่อสู้ไหม — กันส่ง BattleBegun ซ้ำทุกครั้งที่กดโจมตี</summary>
    private bool _inBattle;

    /// <summary>ชุดท่าที่ push ไปแล้วล่าสุด — กันส่ง Actions ซ้ำจนปุ่มกระพริบ</summary>
    private HashSet<string> _sentActionIds;

    /// <summary>
    /// ตายมากี่ครั้ง — ใช้เปิดตาราง death_penalty.gauge_ratio_by_death_count
    ///
    /// ⚠️ **อยู่ในหน่วยความจำต่อ connection** เพราะ PlayerContext ยังไม่มีช่องเก็บ
    /// (Core/PlayerContext.cs — ไฟล์นอกขอบเขตของระบบนี้) ⇒ ต่อใหม่แล้วนับใหม่จาก 0
    /// ผลคือบทลงโทษไม่สะสมข้ามการเข้าเกม (ผู้เล่นได้เปรียบ ไม่ใช่เสียเปรียบ)
    /// </summary>
    private int _deathCount;

    private void RegisterCombatHandlers()
    {
        lock (LivePlayers)
        {
            LivePlayers[EntityId] = this;
        }

        // รายการท่าต่อสู้ — ตัวเดียวที่ทำให้ปุ่มโจมตีโผล่ (ดูหมายเหตุหัวไฟล์)
        _connection.Recv(delegate(GetActions msg, PacketHeader header)
        {
            SendBattleActions(header.Seq);
        });
        // เล็งเป้า — client ไม่รอคำตอบ (CombatSystem.cs:155-165 Send เฉย ๆ)
        // ถือโอกาสส่งรายการท่าชุดใหม่ตามอุปกรณ์ที่ใส่อยู่ตอนนี้ เพราะเซิร์ฟไม่มีจุดรู้ว่า
        // ผู้เล่นเปลี่ยนอาวุธ (handler ของ Equip อยู่ใน Core/Player.cs ซึ่งระบบนี้แตะไม่ได้)
        // client รับ Actions ซ้ำได้ปลอดภัย — ของเดิมถูกใช้ต่อ ไม่รีเซ็ตคูลดาวน์ (CombatSystem.cs:272-284)
        _connection.Recv(delegate(SelectBattleTarget msg, PacketHeader header)
        {
            _battleTargetId = msg.EntityId;
            SendBattleActions();
        });
        _connection.Recv(delegate(UseBattleAction msg, PacketHeader header)
        {
            HandleUseBattleActionMsg(msg);
        });
        _connection.Recv(delegate(ExitBattle msg, PacketHeader header)
        {
            SetBattleMode(false);
        });
        _connection.Recv(delegate(Revive msg, PacketHeader header)
        {
            HandleReviveMsg(normal: true);
        });
        // ฟื้นทันที — ของจริงหักบัตร/เงิน (client/Durango.UI/InteractionGroup.cs:298-345 คิดราคาจาก
        // costs.json → revive_immediately) เซิร์ฟยังไม่มีระบบเงิน จึงให้ฟรี แต่ใช้สัดส่วนหลอดของจริง
        _connection.Recv(delegate(ReviveImmediately msg, PacketHeader header)
        {
            HandleReviveMsg(normal: false);
        });

        _connection.ConnetionClosed += delegate
        {
            lock (LivePlayers)
            {
                if (LivePlayers.TryGetValue(EntityId, out Player current) && ReferenceEquals(current, this))
                {
                    LivePlayers.Remove(EntityId);
                }
            }
        };
    }

    // ── รายการท่าต่อสู้ ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ประกอบ Actions(315) จากอุปกรณ์ที่ใส่อยู่
    ///
    /// วิธีเดียวกับที่เกมหา "ท่าที่ควรมี" ฝั่งตัวเอง (client/CombatSystem.cs:592-642
    /// FillPlaceholderActions): tag ของของที่ใส่ → tag_allow_actions.json → default_actions +
    /// skill_actions ⇒ ใช้ข้อมูลชุดเดียวกัน ผลที่ได้จึงตรงกับที่ UI คาดไว้
    ///
    /// **ค่าของเรา: ให้ท่ามือเปล่าเมื่อไม่มีอาวุธ** — ของจริงท่ามือเปล่ามาจากสกิลพื้นฐาน
    /// (players.json → base_skills: ["combat_actions"]) ซึ่งเซิร์ฟยังไม่มีระบบสกิล
    /// ไม่ใส่ให้ = ผู้เล่นที่ยังไม่มีอาวุธจะไม่มีปุ่มโจมตีเลย
    /// </summary>
    private void SendBattleActions(uint replyOf = 0u)
    {
        var ids = new HashSet<string>();
        foreach (var pair in _context.EquippedItems)
        {
            int index = _context.InventoryItems.FindIndex(item => item.Id == pair.Value);
            if (index < 0) continue;
            Messages.Tag[] tags = _context.InventoryItems[index].Tags;
            if (tags == null) continue;
            foreach (Messages.Tag tag in tags) AddActionsOfTag(ids, tag.Id);
        }
        if (ids.Count == 0) AddActionsOfTag(ids, "bare_hands");

        // push ซ้ำทั้งที่ชุดท่าเหมือนเดิม = UI สร้างปุ่มใหม่ทุกครั้งที่เปลี่ยนเป้า
        // (client/CombatSystem.cs:287 SetCurrentBattleActions → UpdateActionSlots → PlayerActionsUpdated)
        // ⇒ ส่งเฉพาะตอนชุดเปลี่ยนจริง · replyOf > 0 คือถูกถามตรง ๆ ต้องตอบเสมอ
        if (replyOf == 0u && _sentActionIds != null && _sentActionIds.SetEquals(ids))
        {
            return;
        }
        _sentActionIds = ids;

        var statuses = new List<ActionStatus>();
        foreach (string id in ids)
        {
            BattleActionData action = BattleDataStore.Action(id);
            if (action?.meta == null) continue;         // ท่าที่ไม่มีในไฟล์ = ท่าของสัตว์/พาหนะ ข้ามไป
            statuses.Add(new ActionStatus
            {
                Id = id,
                Stamina = action.meta.stamina,
                Cooltime = action.meta.cooltime
            });
        }
        Send(new Actions { BattleActions = statuses.ToArray() }, replyOf);
    }

    private static void AddActionsOfTag(HashSet<string> ids, string tagId)
    {
        TagAllowActionData allowed = BattleDataStore.ActionsOfTag(tagId);
        if (allowed == null) return;
        if (allowed.default_actions != null) ids.UnionWith(allowed.default_actions);
        if (allowed.skill_actions != null) ids.UnionWith(allowed.skill_actions);
    }

    // ── ใช้ท่า ──────────────────────────────────────────────────────────────────────

    private void HandleUseBattleActionMsg(UseBattleAction msg)
    {
        if (!_context.AppearPlayer.IsAlive) return;          // ตายแล้วออกท่าไม่ได้
        BattleActionData action = BattleDataStore.Action(msg.ActionId);
        if (action?.meta == null)
        {
            Console.WriteLine($"[combat] ไม่รู้จักท่า '{msg.ActionId}'");
            return;
        }
        // ค่าความอึดที่ท่าใช้ — ตัวเลขจริงจาก player_battle_actions.json → meta.stamina
        // client กันการกดตอนความอึดไม่พออยู่แล้ว (CombatSystem.cs:453-460) เซิร์ฟหักตามจริง
        if (action.meta.stamina > 0)
        {
            _survival.Add(SurvivalState.KeyStamina, -action.meta.stamina);
            FlushSurvival();
        }
        SetBattleMode(true, msg.TargetEntityId);

        BattleAttackInfo attack = action.attack_info != null && action.attack_info.Length > 0
            ? action.attack_info[0]
            : null;
        if (attack == null) return;                          // ท่าหลบ (onehand_dodge ฯลฯ) ไม่มีดาเมจ

        string targetId = msg.TargetEntityId ?? _battleTargetId;
        Player victim = TryResolveVictim(targetId);
        if (victim != null)
        {
            victim.ReceiveAttack(this, attack, msg.StartAt);
            return;
        }
        // ไม่ใช่ผู้เล่น ⇒ ลองสัตว์ป่า (รอยต่ออยู่ที่ Core/Player.Hunting.cs)
        TryAttackAnimal(targetId, attack, msg.StartAt);
    }

    /// <summary>
    /// หา "ตัวที่โดนตี" จาก entity id
    ///
    /// หาเฉพาะ "ผู้เล่น" — ถ้าไม่ใช่ผู้เล่น ผู้เรียกจะไปลองหาสัตว์ต่อเองที่ TryAttackAnimal
    /// </summary>
    private Player TryResolveVictim(string entityId)
    {
        if (string.IsNullOrEmpty(entityId) || entityId == EntityId) return null;
        Player other;
        lock (LivePlayers)
        {
            LivePlayers.TryGetValue(entityId, out other);
        }
        // คนละเกาะตีกันไม่ได้ (แต่ละเกาะเป็นคนละ World — ดู Core/GameServer.cs:42-43 WorldOf)
        if (other == null || !ReferenceEquals(other._world, _world)) return null;
        return other._context.AppearPlayer.IsAlive ? other : null;
    }

    // ── ความเสียหาย ────────────────────────────────────────────────────────────────

    /// <summary>
    /// รับความเสียหายจากผู้เล่นอีกคน แล้วกระจาย Damaged(12) ให้ทุกคนบนเกาะเห็น
    ///
    /// **สูตรความเสียหายเป็นของเรา** ประกอบจากตัวเลขจริงล้วน ๆ:
    ///   raw     = attack(40)          ← players.json → player.attack
    ///           × damage_bonus        ← player_battle_actions.json → attack_info[0].damage_bonus
    ///   ป้องกัน = defense(0)          ← players.json → player.defense
    ///           × defense_ratio[ชนิด] ← players.json → body_parts.body.defense_ratio (impact/pierce/cut)
    ///           × (1 - armor_penetration) ← attack_info[0].armor_penetration
    ///   ค่าสุดท้าย = (raw - ป้องกัน) × ตัวคูณทิศ(Front = 1.0)
    /// ชนิดความเสียหายเลือกจาก atk_ratio ตัวที่มากที่สุด (ในไฟล์ผลรวมเป็น 1.0 เสมอ = สัดส่วนล้วน ๆ)
    ///
    /// ⚠️ ของจริงยังมีค่าโจมตีของอาวุธ (performance.json → weapon.attack = "72.02 + (level * 1.3)")
    /// ซึ่งเป็นสูตรข้อความและโปรเจกต์นี้ไม่มีตัวประเมินสูตร (หมายเหตุเดียวกับ Core/SurvivalState.cs:56-60)
    /// ⇒ ใช้ค่าฐานของตัวละครอย่างเดียว ดาเมจจึงคงที่ 40 ไม่ว่าถืออะไร (ชนิดอาวุธยังถูกต้อง)
    ///
    /// ตัดสินโดน/พลาด: accuracy(100) เทียบ dodge(0) จากไฟล์จริง ⇒ ค่าปัจจุบันคือ "โดนเสมอ"
    /// </summary>
    private void ReceiveAttack(Player attacker, BattleAttackInfo attack, double startAt)
    {
        PlayerBattleStats stats = BattleDataStore.Stats;
        string damageKind = DominantAtkRatio(attack);
        float defenseRatio = 1f;
        if (stats.body_parts != null && stats.body_parts.TryGetValue("body", out PlayerBodyPart body)
            && body.defense_ratio != null && body.defense_ratio.TryGetValue(damageKind, out float ratio))
        {
            defenseRatio = ratio;
        }
        float directionRatio = 1f;
        if (stats.damage_ratio_table != null
            && stats.damage_ratio_table.TryGetValue(CombatTuning.HitDirection.ToString().ToLowerInvariant(), out float dir))
        {
            directionRatio = dir;
        }

        float bonus = attack.damage_bonus > 0f ? attack.damage_bonus : 1f;
        float raw = attacker.CurrentAttackPower() * bonus;
        float defense = stats.defense * defenseRatio * (1f - Math.Clamp(attack.armor_penetration, 0f, 1f));
        int value = Math.Max(CombatTuning.MinDamage, (int)Math.Round((raw - defense) * directionRatio));

        // โดนหรือหลบ — ด้วยตัวเลขในไฟล์ (accuracy 100 · dodge 0) ผลคือโดนเสมอ แต่เขียนเป็นสูตรไว้
        // เพื่อให้พอมีค่าจากอุปกรณ์/สกิลจริงแล้วใช้ต่อได้เลย
        bool dodged = stats.dodge > 0 && stats.dodge * (attack.accuracy_ratio > 0f ? attack.accuracy_ratio : 1f)
                      > stats.accuracy;

        var damaged = new Damaged
        {
            VictimId = EntityId,
            AttackerId = attacker.EntityId,
            EventAt = startAt > 0.0 ? startAt : Times.UnixTimeNow(),
            Damage = new Damage
            {
                Result = dodged ? DamageResult.Dodged : DamageResult.Hit,
                Value = dodged ? 0 : value,
                Part = BodyPart.Body,                    // players.json → body_parts มีแค่ "body"
                Direction = CombatTuning.HitDirection,
                AttackType = attacker.CurrentAttackType(),
                Effects = DamageEffects.None
            }
        };
        _world.BroadCast(damaged);
        SetBattleMode(true, attacker.EntityId);
        if (dodged) return;

        // เลือดของผู้เล่น = หลอด life (players.json → survival.life ค่าสูงสุดมาจาก life.max_gauge = 300
        // ซึ่งตรงกับ body_parts.body.max_hp) ⇒ หักที่หลอดนี้ ไม่ได้เก็บ HP แยกอีกชุด
        _survival.Add(SurvivalState.KeyLife, -value);
        FlushSurvival();
        if (_survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime) <= 0f)
        {
            Die();
        }
        OnContextChanged();
    }

    /// <summary>ชนิดความเสียหายที่ท่านี้เน้น — atk_ratio ตัวที่สัดส่วนมากที่สุด (impact/pierce/cut)</summary>
    private static string DominantAtkRatio(BattleAttackInfo attack)
    {
        if (attack.atk_ratio == null || attack.atk_ratio.Count == 0) return "impact";
        string best = "impact";
        float bestValue = float.MinValue;
        foreach (var pair in attack.atk_ratio)
        {
            if (pair.Value > bestValue) { bestValue = pair.Value; best = pair.Key; }
        }
        return best;
    }

    /// <summary>
    /// ชนิดอาวุธที่ถืออยู่ — performance.json → weapon → &lt;prototype&gt; → attack_type
    /// ไม่มีอาวุธ ⇒ ใช้ players.json → player.bare_hands.attack_type ("bare_hands")
    /// </summary>
    /// <summary>
    /// ค่าโจมตีรวมของตัวละครตอนนี้ = ค่าฐาน + ค่าของอาวุธที่ถืออยู่
    ///
    /// [5 ก.ย. 2026] เดิมใช้ค่าฐานอย่างเดียว (40) เพราะค่าอาวุธในไฟล์เป็นสูตรข้อความ
    /// (<c>performance.json → weapon.attack = "72.02 + (level * 1.3)"</c>) แล้วยังไม่มีตัวคิดสูตร
    /// ผลคือถืออะไรก็ดาเมจเท่ากัน และตีสัตว์เลเวลกลาง ๆ ไม่เข้าเลย
    /// (สัตว์ lv25 มีเกราะ 125 &gt; ค่าฐาน 40 ⇒ ทุกครั้งได้ดาเมจขั้นต่ำ 1 ⇒ ต้องตี 1,800 ครั้ง)
    /// ตอนนี้คิดสูตรได้แล้วด้วย <see cref="StatFormula"/> ⇒ อาวุธมีความหมายจริง
    /// </summary>
    private float CurrentAttackPower()
    {
        float best = 0f;
        foreach (var pair in _context.EquippedItems)
        {
            int index = _context.InventoryItems.FindIndex(item => item.Id == pair.Value);
            if (index < 0) continue;
            Item item2 = _context.InventoryItems[index];
            float attack = BattleDataStore.WeaponAttack(item2.Prototype, item2.Level);
            if (attack > best) best = attack;      // ถือได้หลายช่อง เอาชิ้นที่แรงสุด
        }
        return BattleDataStore.Stats.attack + best;
    }

    private AttackType CurrentAttackType()
    {
        foreach (var pair in _context.EquippedItems)
        {
            int index = _context.InventoryItems.FindIndex(item => item.Id == pair.Value);
            if (index < 0) continue;
            string type = BattleDataStore.WeaponAttackType(_context.InventoryItems[index].Prototype);
            if (!string.IsNullOrEmpty(type)) return ParseAttackType(type);
        }
        return ParseAttackType(BattleDataStore.Stats.bare_hands?.attack_type);
    }

    /// <summary>ชื่อชนิดอาวุธในไฟล์ (snake_case) → Shared.Battle.AttackType</summary>
    private static AttackType ParseAttackType(string name)
    {
        return name switch
        {
            "bare_hands" => AttackType.BareHands,
            "dagger" => AttackType.Dagger,
            "sword" => AttackType.Sword,
            "axe" => AttackType.Axe,
            "blunt" => AttackType.Blunt,
            "spear" => AttackType.Spear,
            "arrow" => AttackType.Arrow,
            "stone" => AttackType.Stone,
            _ => AttackType.BareHands
        };
    }

    // ── โหมดต่อสู้ ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// เปิด/ปิดโหมดต่อสู้ของผู้เล่นคนนี้
    /// ⚠️ ส่งเฉพาะเจ้าตัว: client ตีความ EntityId ที่ไม่ใช่ของตัวเองเป็น "สัตว์เลี้ยง"
    /// (client/CombatSystem.cs:510-524 · 531-545) ⇒ broadcast แล้วคนอื่นจะสั่งสัตว์ที่ไม่มีอยู่
    /// </summary>
    private void SetBattleMode(bool on, string enemyId = null)
    {
        if (on == _inBattle)
        {
            return;
        }
        _inBattle = on;
        if (on)
        {
            Send(new BattleBegun
            {
                EntityId = EntityId,
                EventAt = Times.UnixTimeNow(),
                EnemyId = enemyId,
                StartDamaged = false
            });
        }
        else
        {
            Send(new BattleEnded { EntityId = EntityId, EventAt = Times.UnixTimeNow() });
        }
    }

    // ── ตาย / เกิดใหม่ ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ตาย — client รู้เรื่องนี้ทางเดียวคือ EntityDied(119)
    /// (client/ObjectManager.cs:158-161 → CharacterBehavior.SetAlive(false) → เมนูชุบชีวิตโผล่)
    ///
    /// บทลงโทษที่ทำแล้ว: หลอดกลับมาแค่บางส่วนตอนฟื้น (constants.json → death_penalty)
    /// **ยังไม่ได้ทำ**: ทำของหล่นที่จุดตาย (death_penalty.default_item_drop_ratio = 0.5 และ
    /// prevent_item_drop_ratio_by_level) เพราะต้องมีระบบ "กองของบนพื้น" ซึ่งเซิร์ฟยังไม่มี
    /// (ไม่มี handler ของ Collect/GetCollectible) — ทำครึ่ง ๆ แล้วของหายเปล่าอันตรายกว่าไม่ทำ
    /// </summary>
    private void Die()
    {
        if (!_context.AppearPlayer.IsAlive) return;
        _context.AppearPlayer.IsAlive = false;
        _deathCount++;
        _survival.Set(SurvivalState.KeyLife, 0f);

        // ความเหนื่อยลดลงตอนตาย ตามตารางจริง fatigue_recovery_ratio_by_death_count [0.3,0.2,0.1,0]
        // (ยิ่งตายบ่อย ยิ่งฟื้นความเหนื่อยน้อยลง) — "ratio" อ่านว่าสัดส่วนของค่าที่มีอยู่
        float[] fatigueRatios = BattleDataStore.DeathPenalty?.fatigue_recovery_ratio_by_death_count;
        if (fatigueRatios != null && fatigueRatios.Length > 0)
        {
            int row = Math.Clamp(_deathCount - 1, 0, fatigueRatios.Length - 1);
            float fatigue = _survival.ValueAt(SurvivalState.KeyFatigue, Gauge.CurrentTime);
            _survival.Set(SurvivalState.KeyFatigue, fatigue * (1f - fatigueRatios[row]));
        }
        FlushSurvival();
        SetBattleMode(false);
        _world.BroadCast(new EntityDied { EntityId = EntityId, At = Times.UnixTimeNow() });
        Console.WriteLine($"[combat] {EntityId[..Math.Min(8, EntityId.Length)]} ตาย (ครั้งที่ {_deathCount})");
        OnContextChanged();
    }

    /// <summary>
    /// ชุบชีวิต — Revive(2101) กลับจุดเกิดของเกาะ · ReviveImmediately(210201) ฟื้นตรงที่ตาย
    ///
    /// สัดส่วนหลอดที่ได้คืนมาจากไฟล์จริง (constants.json):
    ///   ปกติ    death_penalty.gauge_ratio_by_death_count {"0":0.6, "1":0.4, "2":0.2, "3":0.1}
    ///   ทันที   revive_immediately.gauge_ratio {stamina/life/health/energy = 0.7}
    ///
    /// **การตีความเป็นของเรา**: บล็อก death_penalty บอกแค่ "สัดส่วน" ไม่ได้ระบุว่าใช้กับหลอดไหน
    /// ⇒ ใช้ชุดเดียวกับ revive_immediately.gauge_ratio ที่ระบุไว้ชัด (stamina/life/health/energy)
    /// เพราะสองบล็อกนี้เป็นเรื่องเดียวกันคนละทาง
    ///
    /// ⚠️ Revive ปกติของจริงมีตัวเลือกจุดเกิด (กลับแคมป์ / วาร์ปโฮล — msg.WarpholeTile)
    /// เซิร์ฟยังไม่มีระบบแคมป์/วาร์ปโฮล จึงกลับจุดเข้าเกาะเสมอ (World.EntryPoint)
    /// </summary>
    private void HandleReviveMsg(bool normal)
    {
        if (_context.AppearPlayer.IsAlive) return;

        Dictionary<string, float> ratios = normal ? DeathPenaltyRatios() : BattleDataStore.ReviveImmediately?.gauge_ratio;
        if (ratios == null || ratios.Count == 0)
        {
            // ไฟล์ constants.json อ่านไม่ได้ — ไม่ใช่ค่าสมดุลของเกม แค่กันผู้เล่นฟื้นมาโดยเลือดยังเป็น 0
            // (ฟื้นแล้วเลือด 0 = ยืนนิ่งรอตายซ้ำโดยไม่มีอะไรบอก)
            Console.WriteLine("[combat] ⚠️ ไม่พบ death_penalty/revive_immediately ใน constants.json — ฟื้นแบบเต็มหลอด");
            ratios = new Dictionary<string, float>
            {
                { SurvivalState.KeyLife, 1f },
                { SurvivalState.KeyHealth, 1f },
                { SurvivalState.KeyStamina, 1f },
                { SurvivalState.KeyEnergy, 1f }
            };
        }
        RestoreGauges(ratios);

        _context.AppearPlayer.IsAlive = true;
        if (normal)
        {
            // ย้ายตัวกลับจุดเข้าเกาะ — client เอา Teleported.Tile ไปคูณขนาดช่องเอง
            // (client/PlayerManager.cs:346-353 Util.TilePositionToClientPosition)
            Movement[] movements = _context.AppearPlayer.Move.Movements;
            if (movements != null && movements.Length > 0 && movements[0].Path != null && movements[0].Path.Length > 0)
            {
                movements[0].Path[0].Position = GetEntryPosition();
            }
            Send(new Teleported { Tile = _world.EntryPoint, Type = TeleportType.Revive });
        }
        _world.BroadCast(new EntityRevived { EntityId = EntityId, At = Times.UnixTimeNow() });
        OnContextChanged();
    }

    /// <summary>สัดส่วนหลอดตอนฟื้นแบบปกติ — เลือกแถวตามจำนวนครั้งที่ตาย</summary>
    private Dictionary<string, float> DeathPenaltyRatios()
    {
        Dictionary<string, float> byCount = BattleDataStore.DeathPenalty?.gauge_ratio_by_death_count;
        Dictionary<string, float> shape = BattleDataStore.ReviveImmediately?.gauge_ratio;
        if (byCount == null || byCount.Count == 0 || shape == null) return null;
        int row = Math.Clamp(_deathCount - 1, 0, CombatTuning.MaxDeathCountRow);
        float ratio = 0f;
        for (int i = row; i >= 0; i--)
        {
            if (byCount.TryGetValue(i.ToString(), out ratio)) break;
        }
        var result = new Dictionary<string, float>();
        foreach (string key in shape.Keys) result[key] = ratio;
        return result;
    }

    /// <summary>
    /// ตั้งหลอดเป็น "สัดส่วน × ค่าสูงสุด" — ค่าสูงสุดอ่านจากนิยามจริงใน players.json
    /// (life/health ใช้ life.max_gauge = 300 · stamina/energy ใช้ stamina.max_gauge = 100)
    /// ไม่ hardcode เลข เพราะ SurvivalState คลี่ ProxyGauge จากไฟล์เดียวกันอยู่แล้ว
    /// </summary>
    private void RestoreGauges(Dictionary<string, float> ratios)
    {
        if (ratios == null || ratios.Count == 0) return;
        Dictionary<string, SurvivalGaugeDef> survival = PlayerTypes.Player?.Survival;
        foreach (var pair in ratios)
        {
            float max = GaugeMaxOf(survival, pair.Key);
            if (max <= 0f) continue;
            _survival.Set(pair.Key, max * pair.Value);
        }
        FlushSurvival();
    }

    /// <summary>ค่าสูงสุดของหลอดตามไฟล์ — คลี่ ProxyGauge ("energy" = "stamina.max_gauge") ให้ด้วย</summary>
    private static float GaugeMaxOf(Dictionary<string, SurvivalGaugeDef> survival, string key)
    {
        if (survival == null || !survival.TryGetValue(key, out SurvivalGaugeDef def) || def == null) return 0f;
        for (int guard = 0; def != null && !string.IsNullOrEmpty(def.Ref) && guard < 8; guard++)
        {
            string[] parts = def.Ref.Split('.');
            if (!survival.TryGetValue(parts[0], out SurvivalGaugeDef target)) return 0f;
            for (int i = 1; i < parts.Length && target != null; i++)
            {
                target = parts[i] switch
                {
                    "max_gauge" => target.MaxGauge,
                    "min_gauge" => target.MinGauge,
                    _ => null
                };
            }
            def = target;
        }
        if (def == null) return 0f;
        return def.Max ?? def.MaxGauge?.Max ?? def.Value ?? 0f;
    }
}
