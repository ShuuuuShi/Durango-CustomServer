using System;
using System.Collections.Generic;
using Messages;
using Shared.Ability;
using UnityEngine;
using Yaml;

namespace Durango.Online;

/// <summary>
/// [5 ก.ย. 2026] ค่าที่ "เราตั้งเอง" ของระบบหลอดสถานะ — **ไม่มีในข้อมูลต้นฉบับ** รวมไว้ที่เดียวตรงนี้
///
/// ทุกตัวเลขที่มาจากไฟล์เกมจริงอยู่ที่ data/assets/entity_types/players.json,
/// data/assets/constants.json และ data/assets/survival/status_effects.json — ห้ามย้ายมาไว้ที่นี่
/// </summary>
public static class SurvivalTuning
{
    /// <summary>
    /// ความยาวเส้นแนวโน้มที่ส่งให้ client ต่อหนึ่งครั้ง (วินาที)
    ///
    /// **ค่าของเรา** — Gauge เป็น graph sync: เซิร์ฟส่ง 2-3 node แล้ว client interpolate เอง
    /// (GameCode/Gauge.cs:116-145) ⇒ ยาวไปก็ไม่เปลืองอะไร แค่ต้องส่งใหม่ก่อนเส้นหมด
    /// 600 วิ = พอให้ส่งใหม่แค่ทุก 5 นาที แต่ยังสั้นพอให้ค่าที่ประมาณไว้ไม่เพี้ยนไกล
    /// </summary>
    public const double DeterminationHorizon = 600.0;

    /// <summary>**ค่าของเรา** — ส่งเส้นชุดใหม่เมื่อเส้นเดิมเดินไปแล้วเกินสัดส่วนนี้ของ Horizon</summary>
    public const double RefreshRatio = 0.5;

    /// <summary>**ค่าของเรา** — ความถี่ที่ยอมให้เช็คสถานะ (Player.Process ถูกเรียก 120 ครั้ง/วินาที)</summary>
    public const double TickInterval = 1.0;

    /// <summary>
    /// **ค่าของเรา** — ถือว่า "หยุดเดิน" เมื่อไม่มี Move ที่ตำแหน่งเปลี่ยนจริงนานเท่านี้ (วินาที)
    ///
    /// เกมไม่มี message "หยุดเดิน" (มีแต่ Depart ตอนเริ่ม — client/MoveMsgGenerator.cs:92)
    /// จึงต้องดูจากเวลา · client ส่ง Move ถี่สุดทุก client_normal_path_delay = 0.5 วิ
    /// (client/MoveMsgGenerator.cs:67) ⇒ ตั้ง 2 วิ = 4 เท่าของช่วงส่ง กันพลาดตอนเน็ตหน่วง
    /// </summary>
    public const double MoveIdleTimeout = 2.0;

    /// <summary>
    /// **ค่าของเรา** — เกณฑ์ "เหนื่อย" (Derived.FatigueCaution = 4) บนหลอด fatigue ที่ max 100
    ///
    /// ⚠️ ตัวเลขนี้ไม่มีในไฟล์ data ไหนเลย (เช็คแล้วทั้ง statistics/player.json,
    /// survival/fatigue_categories.json, survival/status_effects.json, constants.json)
    /// ของจริงมาจากตารางความสามารถของตัวละครที่เซิร์ฟแท้คำนวณเอง ซึ่งเรายังไม่มี
    /// ไม่ส่ง = client อ่านได้ -1 แล้ว fallback เป็น Max (client/Durango.Logic/Fatigue.cs SetGauge)
    /// ⇒ หลอดจะไม่มีสถานะ "เหนื่อย/หมดแรง" เลย
    /// </summary>
    public const float FatigueCaution = 60f;

    /// <summary>**ค่าของเรา** — เกณฑ์ "หมดแรง" (Derived.FatigueDanger = 5) ดูหมายเหตุที่ FatigueCaution</summary>
    public const float FatigueDanger = 90f;

    // ── ผลของการพัก (RestOn) ────────────────────────────────────────────────────────
    // ตัวเลขต่อไปนี้ **คำนวณจากสูตรจริง** ใน data/assets/survival/status_effects.json → "rest"
    // ตัวแรก (min_level 1, max_level 9) ที่ level = 1 — เซิร์ฟยังไม่มีตัวคำนวณสูตรข้อความ
    // (constants.json เต็มไปด้วยสูตรแบบ python แต่ไม่มี evaluator ในโปรเจกต์นี้) จึงกางไว้ตรง ๆ
    // **ที่เป็นของเราคือการเลือก level = 1** (ที่พักพื้นฐาน) เพราะยังไม่ได้ต่อระดับของสิ่งปลูกสร้าง

    /// <summary>rest → fatigue = -(0.15 + 0.0015 * level) ที่ level 1</summary>
    public const float RestFatigueVelocity = -(0.15f + 0.0015f * 1f);

    /// <summary>rest → life = 0.45 + 0.05 * level ที่ level 1</summary>
    public const float RestLifeVelocity = 0.45f + 0.05f * 1f;

    /// <summary>rest → health = 0.18 + 0.02 * level ที่ level 1</summary>
    public const float RestHealthVelocity = 0.18f + 0.02f * 1f;
}

/// <summary>
/// [5 ก.ย. 2026] หลอดสถานะของผู้เล่นหนึ่งคน — สร้างจากข้อมูลจริงและ "เดิน" ตามเวลาจริง
///
/// ทำไมไม่ tick ทุกเฟรม: <see cref="Gauge"/> คือ graph synchronization ไม่ใช่ค่าตัวเลขเดี่ยว
/// (GameCode/Gauge.cs:25 Determination = GaugeNode[]) เซิร์ฟส่ง "เส้นแนวโน้ม" ไปครั้งเดียว
/// แล้ว client interpolate ตามเวลาของมันเอง (Gauge.CurrentValueAndVelocity ที่ :116-145)
/// ⇒ หน้าที่ของเซิร์ฟคือส่งเส้นใหม่ **เมื่อความชันเปลี่ยน** เท่านั้น ไม่ใช่ส่งค่าทุกวินาที
///
/// ค่าและความชันทั้งหมดอ่านจาก data/assets/entity_types/players.json → "player" → "survival"
/// (ดูค่าที่เราเติมเองที่ <see cref="SurvivalTuning"/> — ที่นั่นที่เดียว)
/// </summary>
public sealed class SurvivalState
{
    // คีย์หลอดที่ client รู้จัก (client/CharacterBehavior.cs:436 GetGauge · PlayerHudGroupBase.cs:82-84)
    public const string KeyLife = "life";
    public const string KeyHealth = "health";
    public const string KeyStamina = "stamina";
    public const string KeyEnergy = "energy";
    public const string KeyFatigue = "fatigue";
    public const string KeyGroggy = "groggy";

    // ชื่อแหล่งของ momentum (แรงที่บวกเข้ากับ velocity ฐาน)
    private const string SourceOnline = "online";   // online_momenta จาก players.json
    private const string SourceMoving = "moving";   // เดินอยู่ — constants.json energy.speeds.moving
    private const string SourceResting = "rest";    // พักอยู่ — status_effects.json "rest"

    private readonly PlayerContext _context;

    /// <summary>ค่าปัจจุบันของแต่ละหลอด ณ เวลาที่สร้างเส้นล่าสุด</summary>
    private readonly Dictionary<string, float> _values = new();

    /// <summary>แหล่ง → (คีย์หลอด → ความชันที่บวกเพิ่ม)</summary>
    private readonly Dictionary<string, Dictionary<string, float>> _momenta = new();

    /// <summary>
    /// คีย์ที่เพิ่งถูกตั้งค่าตรง ๆ และยังไม่ได้เอาไปสร้างเส้น
    ///
    /// จำเป็นเพราะ <see cref="Rebuild"/> เริ่มด้วย <see cref="Harvest"/> ที่อ่านค่ากลับจาก
    /// หลอดชุดเดิม — ถ้าไม่กันไว้ ค่าที่ Set มาก่อนหน้าจะถูกค่าเก่าทับทันที
    /// </summary>
    private readonly HashSet<string> _pendingSets = new();

    private double _builtAt;
    private double _nextTickAt;
    private bool _dirty;

    /// <summary>
    /// มีผู้เล่นต่ออยู่จริงไหม — ถ้าไม่ velocity ทุกหลอดถูกบังคับเป็น 0 (เส้นแบน ค่าค้างไว้)
    ///
    /// ทำไมต้องมี: context ถูกสร้าง/โหลดตอนบูต (Host.Load) ซึ่งอาจก่อนคนต่อเข้ามาเป็นชั่วโมง
    /// ถ้าปล่อยให้เส้นเดินตั้งแต่ตอนนั้น รอบเซฟอัตโนมัติ (Program.cs:185 ทุก 60 วิ) จะเขียนค่าที่
    /// "เดินไปแล้ว" ลงไฟล์ ⇒ กลายเป็น offline progression แบบครึ่ง ๆ กลาง ๆ โดยไม่ได้ตั้งใจ
    /// (เช่น life velocity 1/วิ = เลือดเต็มเองภายใน 5 นาทีทั้งที่ผู้เล่นไม่ได้ออนไลน์)
    /// ⇒ หลอดเดินเฉพาะตอนออนไลน์ ซึ่งตรงกับความหมายของ online_momenta ในไฟล์ data อยู่แล้ว
    /// </summary>
    private bool _live;

    public SurvivalState(PlayerContext context, bool live)
    {
        _context = context;
        _live = live;
        // online_momenta = แรงที่ติดตัวตลอดเวลาที่ออนไลน์ — ของ player มีแค่ fatigue +0.083333
        // ซึ่งหักล้างกับ velocity ฐาน -0.083333 พอดี ⇒ อยู่เฉย ๆ ความเหนื่อยนิ่ง (ตามที่ข้อมูลออกแบบ)
        PlayerType type = PlayerTypes.Player;
        if (type?.OnlineMomenta != null && type.OnlineMomenta.Count > 0)
        {
            _momenta[SourceOnline] = new Dictionary<string, float>(type.OnlineMomenta);
        }
        Rebuild(Gauge.CurrentTime);
    }

    /// <summary>
    /// สร้างหลอดชุดใหม่ให้ context หนึ่งครั้งแล้วทิ้ง — ใช้ตอนโหลด/สร้าง PlayerContext
    /// ตอนนั้นยังไม่มีใครต่ออยู่ จึงสร้างแบบแช่ไว้ (ดู <see cref="_live"/>)
    /// </summary>
    public static void Reset(PlayerContext context)
    {
        if (context != null) _ = new SurvivalState(context, live: false);
    }

    /// <summary>แช่หลอดไว้ที่ค่าปัจจุบัน — เรียกตอนผู้เล่นหลุดการเชื่อมต่อ</summary>
    public void Freeze(double now)
    {
        _live = false;
        Rebuild(now);
    }

    // ── การเปลี่ยนความชัน (velocity) ────────────────────────────────────────────────

    /// <summary>เดินอยู่ไหม — เดินแล้ว energy ลดตาม constants.json → energy.speeds.moving</summary>
    public void SetMoving(bool moving)
    {
        float speed = MovingEnergySpeed();
        if (!moving || speed <= 0f)
        {
            SetMomentum(SourceMoving, null);
            return;
        }
        // ไฟล์เก็บเป็น "อัตราสิ้นเปลือง" (บวก) — หลอดต้องลด จึงกลับเครื่องหมาย
        SetMomentum(SourceMoving, new Dictionary<string, float> { { KeyEnergy, -speed } });
    }

    /// <summary>พักอยู่ไหม (RestOn) — ค่าจาก status_effects.json → "rest" ดู SurvivalTuning</summary>
    public void SetResting(bool resting)
    {
        if (!resting)
        {
            SetMomentum(SourceResting, null);
            return;
        }
        SetMomentum(SourceResting, new Dictionary<string, float>
        {
            { KeyFatigue, SurvivalTuning.RestFatigueVelocity },
            { KeyLife, SurvivalTuning.RestLifeVelocity },
            { KeyHealth, SurvivalTuning.RestHealthVelocity }
        });
    }

    /// <summary>ตั้ง/ลบแรงของแหล่งหนึ่ง — คืน true ถ้าค่าเปลี่ยนจริง (ต้องส่งเส้นใหม่)</summary>
    public bool SetMomentum(string source, Dictionary<string, float> velocities)
    {
        bool had = _momenta.TryGetValue(source, out var current);
        bool want = velocities != null && velocities.Count > 0;
        if (!want)
        {
            if (!had) return false;
            _momenta.Remove(source);
            _dirty = true;
            return true;
        }
        if (had && SameVelocities(current, velocities)) return false;
        _momenta[source] = new Dictionary<string, float>(velocities);
        _dirty = true;
        return true;
    }

    // ── การเปลี่ยนค่าแบบไม่ต่อเนื่อง (กิน/โดนตี/ชุบชีวิต) ─────────────────────────────

    /// <summary>ตั้งค่าหลอดตรง ๆ — ใช้ตอนค่ากระโดด ไม่ใช่ค่อย ๆ ไหลตามเวลา</summary>
    public bool Set(string key, float value)
    {
        if (string.IsNullOrEmpty(key)) return false;
        _values[key] = value;
        _pendingSets.Add(key);
        _dirty = true;
        return true;
    }

    /// <summary>บวก/ลบค่าหลอดทันที (บวก = ฟื้น, ลบ = เสีย)</summary>
    public bool Add(string key, float delta)
    {
        if (string.IsNullOrEmpty(key) || Mathf.Approximately(delta, 0f)) return false;
        double now = Gauge.CurrentTime;
        return Set(key, ValueAt(key, now) + delta);
    }

    /// <summary>ค่าปัจจุบันของหลอด (อ่านจากเส้นที่ส่งไปแล้ว) — 0 ถ้าไม่มีหลอดนั้น</summary>
    public float ValueAt(string key, double at)
    {
        Gauge g = GaugeOf(key);
        if (g?.Determination != null && g.Determination.Length > 0) return g.Get(at);
        return _values.TryGetValue(key, out float v) ? v : 0f;
    }

    // ── รอบตรวจ ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// เรียกทุกเฟรมจาก Player.Process — คืน true พร้อม <see cref="SurvivalUpdated"/> (183)
    /// เมื่อต้องส่งเส้นชุดใหม่ (ความชันเปลี่ยน, ค่ากระโดด, หรือเส้นเดิมใกล้หมด)
    /// </summary>
    public bool Tick(double now, out SurvivalUpdated msg)
    {
        msg = default;
        if (!_dirty)
        {
            // ไม่มีอะไรเปลี่ยน — เช็คแค่ว่าเส้นเดิมใกล้หมดหรือยัง และเช็คไม่ถี่
            if (now < _nextTickAt) return false;
            _nextTickAt = now + SurvivalTuning.TickInterval;
            if (now - _builtAt < SurvivalTuning.DeterminationHorizon * SurvivalTuning.RefreshRatio) return false;
        }
        Rebuild(now);
        msg = BuildUpdatedMessage();
        return true;
    }

    /// <summary>บังคับส่งเส้นชุดใหม่เดี๋ยวนี้ (ใช้ตอนค่ากระโดด) — ผู้เรียกส่ง msg เอง</summary>
    public SurvivalUpdated Flush(double now)
    {
        Rebuild(now);
        return BuildUpdatedMessage();
    }

    private SurvivalUpdated BuildUpdatedMessage()
    {
        Survival survival = _context.AppearPlayer.Survival;
        var updated = new Dictionary<string, Gauge>();
        // client รับคีย์ "life" เป็นกรณีพิเศษแล้วเอาไปตั้ง Life ให้เอง
        // (client/CharacterBehavior.cs:371-379 UpdateSurvivalGauges)
        if (survival.Life != null) updated[KeyLife] = survival.Life;
        if (survival.Gauges != null)
        {
            foreach (var pair in survival.Gauges) updated[pair.Key] = pair.Value;
        }
        return new SurvivalUpdated
        {
            EntityId = _context.EntityId,
            Updated = updated,
            Removed = Array.Empty<string>()
        };
    }

    // ── การสร้างเส้น ────────────────────────────────────────────────────────────────

    /// <summary>
    /// สร้างหลอดทั้งชุดใหม่จากข้อมูลจริง แล้วเขียนลง AppearPlayer.Survival
    ///
    /// ต้องสร้าง Gauge ก้อนใหม่ทุกครั้งเพราะ Determination เป็น private set (GameCode/Gauge.cs:25)
    /// — เปลี่ยนในที่ไม่ได้ ซึ่งก็ตรงกับที่เกมคาดหวังอยู่แล้ว เพราะ SurvivalUpdated ส่งทั้งก้อน
    /// </summary>
    public void Rebuild(double now)
    {
        Dictionary<string, SurvivalGaugeDef> defs = ResolvedDefs();
        Harvest(now);

        var built = new Dictionary<string, Gauge>();
        if (defs.Count == 0)
        {
            // ไฟล์ data หาย — ไม่ใช่ค่าสมดุลของเกม แค่กันเซิร์ฟล้มและกันหลอดว่างเปล่าบนจอ
            Console.WriteLine("[survival] ⚠️ ไม่พบ entity_types/players.json → 'player' → 'survival' — ใช้หลอดเปล่า");
            float life = _values.TryGetValue(KeyLife, out float lv) ? lv : 1f;
            built[KeyLife] = new Gauge(1f, 0f, new[] { new GaugeNode(now, life) });
        }
        else
        {
            // หลอดที่เป็น "ค่าสูงสุด" ของหลอดอื่น (health/energy) ต้องเกิดก่อน เพราะอีกฝั่งอ้างถึง
            Dictionary<string, string> maxOwner = MaxOwners(defs);
            foreach (var pair in defs)
            {
                if (!maxOwner.ContainsValue(pair.Key)) continue;
                built[pair.Key] = BuildGauge(pair.Key, pair.Value, null, now);
            }
            foreach (var pair in defs)
            {
                if (built.ContainsKey(pair.Key)) continue;
                Gauge max = maxOwner.TryGetValue(pair.Key, out string owner) ? built.GetValueOrDefault(owner) : null;
                built[pair.Key] = BuildGauge(pair.Key, pair.Value, max, now);
            }
        }

        var gauges = new Dictionary<string, Gauge>();
        foreach (var pair in built)
        {
            if (pair.Key == KeyLife) continue;   // life ไปอยู่ช่อง Survival.Life ไม่ใช่ใน Gauges
            gauges[pair.Key] = pair.Value;
        }
        _context.AppearPlayer.Survival.EntityId = _context.EntityId;
        _context.AppearPlayer.Survival.Life = built.GetValueOrDefault(KeyLife);
        _context.AppearPlayer.Survival.Gauges = gauges;

        _builtAt = now;
        _nextTickAt = now + SurvivalTuning.TickInterval;
        _dirty = false;
        _pendingSets.Clear();
    }

    /// <summary>อ่านค่าปัจจุบันจากหลอดชุดเดิมก่อนทิ้ง (รวมถึงชุดที่เพิ่งโหลดจากไฟล์เซฟ)</summary>
    private void Harvest(double now)
    {
        Survival survival = _context.AppearPlayer.Survival;
        Take(KeyLife, survival.Life);
        if (survival.Gauges != null)
        {
            foreach (var pair in survival.Gauges) Take(pair.Key, pair.Value);
        }
        return;

        void Take(string key, Gauge g)
        {
            // ⚠️ หลอดที่โหลดจากไฟล์เซฟเหลือ node เดียวที่ Time = 0 เสมอ เพราะ GaugeConverter
            // ย่อ Gauge เป็น {min,max,cur} ตอนเขียน JSON (Support/GaugeConverter.cs:13-20)
            // ⇒ Get(now) คืน cur ที่เซฟไว้ ซึ่งคือสิ่งที่เราต้องการพอดี (ยังไม่ทำ offline progression)
            if (g?.Determination == null || g.Determination.Length == 0) return;
            if (_pendingSets.Contains(key)) return;   // ค่าที่ Set มาใหม่ชนะค่าบนเส้นเดิมเสมอ
            _values[key] = g.Get(now);
        }
    }

    private Gauge BuildGauge(string key, SurvivalGaugeDef def, Gauge maxGauge, double now)
    {
        float min = def.Min ?? 0f;
        float maxNow, maxEnd;
        if (maxGauge != null)
        {
            // ค่าสูงสุดเป็นหลอดที่ขยับได้เอง (life ถูกจำกัดด้วย health · stamina ด้วย energy)
            maxNow = maxGauge.Get(now);
            maxEnd = maxGauge.Get(now + SurvivalTuning.DeterminationHorizon);
        }
        else
        {
            maxNow = maxEnd = def.Max ?? def.Value ?? 0f;
        }
        maxNow = Mathf.Max(maxNow, min);
        maxEnd = Mathf.Max(maxEnd, min);

        float cur = _values.TryGetValue(key, out float v) ? v : def.Value ?? maxNow;
        // ไม่มีใครต่ออยู่ = เส้นแบน ค่าค้างไว้เฉย ๆ (ยังไม่ทำ offline progression — ดู _live)
        float velocity = _live ? def.Velocity + MomentumOf(key) : 0f;

        GaugeNode[] nodes = MakeLine(now, cur, velocity, min, maxNow, maxEnd, SurvivalTuning.DeterminationHorizon);
        _values[key] = nodes[0].Value;
        return maxGauge != null ? new Gauge(maxGauge, min, nodes) : new Gauge(maxNow, min, nodes);
    }

    /// <summary>
    /// เส้นแนวโน้ม 2-3 node: (ตอนนี้) → (ตอนชนขอบ หรือ ปลาย horizon)
    ///
    /// Gauge ไม่ clamp ให้เอง (GameCode/Gauge.cs:116-145 แค่ interpolate เชิงเส้นแล้วค้างที่ node
    /// สุดท้าย) ⇒ ถ้าปล่อยเส้นทะลุเพดาน client จะวาดหลอดเกิน 100% ⇒ ต้องตัดที่ขอบเอง
    /// </summary>
    private static GaugeNode[] MakeLine(double now, float cur, float velocity, float min,
                                        float maxNow, float maxEnd, double horizon)
    {
        cur = Mathf.Clamp(cur, min, maxNow);
        float maxVelocity = (float)((maxEnd - maxNow) / horizon);

        double span = horizon;
        int hit = 0;                                   // 0 = ไม่ชนขอบ · 1 = ชนเพดาน · 2 = ชนพื้น
        float relative = velocity - maxVelocity;       // ไล่ตามเพดานที่ขยับอยู่
        if (relative > 0f)
        {
            double t = (maxNow - cur) / relative;
            if (t < span) { span = t; hit = 1; }
        }
        if (velocity < 0f)
        {
            double t = (cur - min) / -velocity;
            if (t < span) { span = t; hit = 2; }
        }
        if (span < 0.0 || double.IsNaN(span)) span = 0.0;

        if (span <= 0.0)
        {
            // ติดขอบอยู่แล้วและยังดันออกนอกขอบ — ถ้าเป็นเพดานที่กำลังลด ให้ไหลตามเพดาน
            float end = hit == 1 ? maxEnd : cur;
            return new[] { new GaugeNode(now, cur), new GaugeNode(now + horizon, end) };
        }

        float goal = hit switch
        {
            1 => maxNow + maxVelocity * (float)span,
            2 => min,
            _ => cur + velocity * (float)span
        };

        if (hit == 1 && span < horizon && maxVelocity < 0f)
        {
            // ชนเพดานกลางทางแล้วเพดานยังลดต่อ — node ที่ 3 ให้ไหลลงตามเพดานแทนที่จะค้าง
            return new[]
            {
                new GaugeNode(now, cur),
                new GaugeNode(now + span, goal),
                new GaugeNode(now + horizon, maxEnd)
            };
        }
        return new[] { new GaugeNode(now, cur), new GaugeNode(now + span, goal) };
    }

    private float MomentumOf(string key)
    {
        float sum = 0f;
        foreach (var source in _momenta)
        {
            if (source.Value.TryGetValue(key, out float v)) sum += v;
        }
        return sum;
    }

    private Gauge GaugeOf(string key)
    {
        if (key == KeyLife) return _context.AppearPlayer.Survival.Life;
        Dictionary<string, Gauge> gauges = _context.AppearPlayer.Survival.Gauges;
        return gauges != null && gauges.TryGetValue(key, out Gauge g) ? g : null;
    }

    private static bool SameVelocities(Dictionary<string, float> a, Dictionary<string, float> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var pair in a)
        {
            if (!b.TryGetValue(pair.Key, out float v) || !Mathf.Approximately(v, pair.Value)) return false;
        }
        return true;
    }

    // ── การอ่านนิยามจากไฟล์ ─────────────────────────────────────────────────────────

    /// <summary>นิยามหลอดทุกตัวหลังคลี่ ProxyGauge ให้ชี้ไปยังนิยามจริงแล้ว</summary>
    private static Dictionary<string, SurvivalGaugeDef> ResolvedDefs()
    {
        var result = new Dictionary<string, SurvivalGaugeDef>();
        Dictionary<string, SurvivalGaugeDef> survival = PlayerTypes.Player?.Survival;
        if (survival == null) return result;
        foreach (var pair in survival)
        {
            SurvivalGaugeDef def = Resolve(survival, pair.Value);
            if (def != null) result[pair.Key] = def;
        }
        return result;
    }

    /// <summary>
    /// ProxyGauge = ชื่อเรียกอีกชื่อของหลอดอื่น — "energy" คือ "stamina.max_gauge"
    /// และ "health" คือ "life.max_gauge" (entity_types/players.json → player → survival)
    /// ⇒ คลี่ ref ตามไฟล์ ไม่ hardcode ว่าหลอดไหนคู่กับหลอดไหน
    /// </summary>
    private static SurvivalGaugeDef Resolve(Dictionary<string, SurvivalGaugeDef> survival, SurvivalGaugeDef def)
    {
        for (int guard = 0; def != null && !string.IsNullOrEmpty(def.Ref) && guard < 8; guard++)
        {
            string[] parts = def.Ref.Split('.');
            if (!survival.TryGetValue(parts[0], out SurvivalGaugeDef target)) return null;
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
        return def;
    }

    /// <summary>คีย์หลอด → คีย์ของหลอดที่ทำหน้าที่เป็นค่าสูงสุดของมัน (life→health, stamina→energy)</summary>
    private static Dictionary<string, string> MaxOwners(Dictionary<string, SurvivalGaugeDef> defs)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in defs)
        {
            SurvivalGaugeDef max = pair.Value.MaxGauge;
            if (max == null) continue;
            foreach (var other in defs)
            {
                // เทียบด้วย ReferenceEquals เพราะ ProxyGauge คลี่แล้วได้ "อ็อบเจกต์เดียวกัน"
                if (other.Key != pair.Key && ReferenceEquals(other.Value, max))
                {
                    result[pair.Key] = other.Key;
                    break;
                }
            }
        }
        return result;
    }

    private static float MovingEnergySpeed()
    {
        Dictionary<string, float> speeds = Yaml.Util.Singleton<Constants>.Instance?.Energy?.Speeds;
        return speeds != null && speeds.TryGetValue("moving", out float v) ? v : 0f;
    }

    // ── ความสามารถที่ผูกกับหลอด (ส่งไปกับ Statistics 2040) ────────────────────────────

    /// <summary>
    /// เติมค่าความสามารถที่เกี่ยวกับหลอดลงใน Statistics.DerivedsAbilities
    ///
    /// ⚠️ ขาด FatigueCaution(4)/FatigueDanger(5) แล้ว client จะไม่มีสถานะ "เหนื่อย/หมดแรง" เลย
    /// เพราะ client/Durango.Logic/FatigueSystem.cs:124-125 อ่านได้ -1 แล้ว Fatigue.SetGauge
    /// fallback ทั้งคู่เป็น Max ⇒ หลอดเต็มถึงจะนับว่าเหนื่อย
    /// </summary>
    public static void FillDeriveds(IDictionary<Derived, float> deriveds)
    {
        if (deriveds == null) return;
        PlayerType type = PlayerTypes.Player;
        Dictionary<string, SurvivalGaugeDef> survival = type?.Survival;
        if (type?.MaxEffectedBy != null && survival != null)
        {
            foreach (var pair in type.MaxEffectedBy)
            {
                // ⚠️ ไฟล์จริงมีสองหลอดชี้ Derived เดียวกัน (groggy:0 กับ health:0) โดยค่า max
                // ต่างกัน (600 กับ 300) ⇒ ไม่มีทางรู้ว่าอันไหนถูกโดยไม่เดา จึงข้ามทั้งคู่
                if (CountMappedTo(type.MaxEffectedBy, pair.Value) != 1) continue;
                SurvivalGaugeDef def = Resolve(survival, survival.TryGetValue(pair.Key, out var d) ? d : null);
                if (def?.Max == null) continue;
                deriveds[(Derived)pair.Value] = def.Max.Value;
            }
        }
        deriveds[Derived.FatigueCaution] = SurvivalTuning.FatigueCaution;
        deriveds[Derived.FatigueDanger] = SurvivalTuning.FatigueDanger;
    }

    private static int CountMappedTo(Dictionary<string, int> map, int derived)
    {
        int n = 0;
        foreach (var pair in map)
        {
            if (pair.Value == derived) n++;
        }
        return n;
    }
}
