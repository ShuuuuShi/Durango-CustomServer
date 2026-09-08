using System;
using System.Collections.Generic;
using System.Text;
using Messages;
using Shared.Region;
using Shared.StatusEffect;
using Shared.Survival;

namespace Durango.Online;

/// <summary>
/// ความเหนื่อยจากสภาพแวดล้อม — ส่ง <c>FatigueVelocities</c>(318) ที่เซิร์ฟไม่เคยส่งมาก่อน
///
/// ═══ ทำไมต้องมี ═══
/// ผล <c>type=2</c> (Fatigue) ใน <c>status_effects.json</c> อ้างถึงหมวดความเหนื่อยด้วยชื่อ
/// snake_case (<c>hot</c> · <c>gale</c> · <c>default</c> …) แต่ถ้าไม่มีหมวดไหนทำงานอยู่เลย
/// ตัวเลขพวกนั้นก็ไม่มีอะไรให้ไปลด ⇒ บัพ 261 จาก 372 ชนิดเป็นไอคอนเปล่า
/// เช่น <c>inside</c> (อยู่ในบ้าน) ที่ควรตัดลม/แดดทิ้งทั้งหมด
///
/// ═══ สายที่ต่อครบแล้ว ═══
/// <code>
/// เซิร์ฟ  ไบโอมที่ยืน → หมวด (region_templates.biome_effects)
///        × default_ratio (fatigue_categories.json)
///        × (1 + Σ ผล type=2 ของบัพที่ติดอยู่)
///        → SurvivalState momenta "env_fatigue"   ⇒ หลอดเดินจริง
///        → FatigueVelocities(318)                ⇒ ฝั่งเกมอธิบายได้ว่าเหนื่อยเพราะอะไร
/// ตัวเกม FatigueSystem.UpdateFatigue รวมเป็น FatigueVelocity โชว์บนหลอด
///        FatigueMomentum.Set แจกแจงเป็นรายการเหตุผล (÷ default_ratio เป็นเปอร์เซ็นต์)
///        LocalMotionUpdater ใช้ FatigueEffect เปลี่ยนท่ายืนเป็นร้อน/หนาว
/// </code>
///
/// ⚠️ <c>Resistances</c> ส่งเป็นตารางว่างไว้ก่อน — ค่าต้านทาน (Derived 310-317) คิดได้จาก
///    <c>SendFullStatistics</c> แต่ **สูตรว่าต้านทานลดความเหนื่อยเท่าไรไม่มีอยู่ในไฟล์ data**
///    ⇒ ใส่ค่ามั่วไม่ได้ ปล่อยว่างแล้วฝั่งเกมจะไม่โชว์บรรทัด "ต้านทาน" เฉย ๆ
///    (client/Durango.UI/FatigueMomentum.cs:158 ข้ามเมื่อค่าเป็น 0)
/// ⚠️ <c>BiomeFatigue</c> (สภาพแวดล้อมไม่เสถียร) ยังไม่ส่ง — ต้องมี unstable_factor ของเกาะก่อน
/// </summary>
public partial class Player
{
    /// <summary>ชื่อแหล่ง momenta ของความเหนื่อยจากสภาพแวดล้อม — ห้ามชนกับ online/moving/rest/weather_se:</summary>
    private const string EnvFatigueSource = "env_fatigue";

    /// <summary>ลายเซ็นของชุดที่ส่งไปล่าสุด — กันส่งซ้ำเมื่อคิดใหม่แล้วได้เลขเดิม</summary>
    private string _lastFatigueSignature;

    // Process() เดิน 120 ครั้ง/วินาที/คน ⇒ ห้ามคิดใหม่ทุกเฟรม
    // อินพุตของสูตรมีแค่ ช่องที่ยืน · เลเวลตัวละคร · ชุดบัพที่ติดอยู่ ⇒ เฝ้าสามอย่างนี้พอ
    private Point2 _fatigueCheckedTile = new(int.MinValue, int.MinValue);
    private int _fatigueCheckedStamp = int.MinValue;

    /// <summary>
    /// คิดความเหนื่อยจากสภาพแวดล้อมใหม่ แล้วส่งให้ฝั่งเกมถ้าเปลี่ยนจริง
    /// เรียกทุกเฟรมได้ — ข้างในเทียบลายเซ็นก่อนส่ง
    /// </summary>
    private void SyncFatigueVelocities()
    {
        Point2 tile = LifeTileOf(this);
        int stamp = StatusEffectStamp();
        if (tile.x == _fatigueCheckedTile.x && tile.y == _fatigueCheckedTile.y &&
            stamp == _fatigueCheckedStamp)
        {
            return;
        }
        _fatigueCheckedTile = tile;
        _fatigueCheckedStamp = stamp;

        RegionCatalog.TemplateInfo template = RegionCatalog.GetTemplate(_world.TerrainInfo?.region_template);
        Role role = template?.Role ?? Role.Rural;
        int regionLevel = Math.Max(1, template?.Level ?? 1);
        int selfLevel = Math.Max(1, _context.AppearPlayer.Level);

        double basis = FatigueTuning.BaseVelocity(role, selfLevel, regionLevel);

        // ── หมวดที่ทำงานอยู่ ───────────────────────────────────────────────────────
        // **จุดที่ข้อมูลไม่ได้บอก — บอกไว้ตรง ๆ** ไฟล์ไม่ระบุว่าหมวด Default ทำงาน "พร้อมกับ"
        // หมวดของไบโอม หรือเป็น "ตัวสำรองเมื่อไบโอมไม่ให้หมวดอะไร"
        //   · แบบบวกกัน  → เกาะเลเวลเท่าเรา 0.16/วิ ⇒ หลอด 100 เต็มใน ~10 นาทีที่แค่เดินเล่น
        //   · แบบสำรอง   → 0.08/วิ ⇒ ~20 นาที
        // เลือกแบบสำรอง เพราะ online_momenta ในไฟล์ตั้งใจหักล้างให้ "อยู่เฉย ๆ ความเหนื่อยนิ่ง"
        // และมี fatigue_cost แยกต่อการกระทำ (collect/craft/build/combat) อยู่แล้ว
        // ⇒ สภาพแวดล้อมไม่ควรเป็นตัวถมหลอดหลัก · สลับเป็นแบบบวกได้ที่บรรทัดเดียวข้างล่าง
        var active = new List<FatigueCategory>();
        Biome biome = WorldStatusRules.UnmaskBiome(_world.BiomeAt(tile));
        if (template != null && template.BiomeEffects.TryGetValue(biome, out FatigueCategory[] cats))
        {
            foreach (FatigueCategory c in cats)
            {
                if (!active.Contains(c)) active.Add(c);
            }
        }
        if (active.Count == 0) active.Add(FatigueCategory.Default);

        Dictionary<string, double> modifiers = CollectFatigueModifiers();
        var velocities = new Dictionary<FatigueCategory, float>();
        float total = 0f;
        string fatigueEffect = null;
        double strongest = 0.0;

        foreach (FatigueCategory category in active)
        {
            FatigueTuning.CategoryInfo info = FatigueTuning.Get(category);
            if (info == null) continue;

            // ผล type=2 เป็น "สัดส่วนที่หายไป" — inside ให้ default -1.0 = ตัดทิ้งหมด · hot -0.75 = เหลือ 25%
            double modifier = modifiers.GetValueOrDefault(info.SnakeCase);
            double v = basis * info.DefaultRatio * Math.Max(0.0, 1.0 + modifier);

            velocities[category] = (float)v;
            total += (float)v;
            if (v > strongest && !string.IsNullOrEmpty(info.Effect))
            {
                strongest = v;
                fatigueEffect = info.Effect;
            }
        }

        string signature = BuildFatigueSignature(velocities, fatigueEffect);
        if (string.Equals(signature, _lastFatigueSignature, StringComparison.Ordinal)) return;
        _lastFatigueSignature = signature;

        // หลอดจริง — ไม่ตั้งตรงนี้ ตัวเลขที่ส่งไปจะเป็นแค่ป้ายที่หลอดไม่เดินตาม
        _survival.SetMomentum(EnvFatigueSource, total > 0f
            ? new Dictionary<string, float> { [SurvivalState.KeyFatigue] = total }
            : null);
        FlushSurvival();

        Send(new FatigueVelocities
        {
            Velocities = velocities,
            FatigueEffect = fatigueEffect,
            Resistances = new Dictionary<FatigueCategory, float>(),
            BiomeFatigue = null
        });
    }

    /// <summary>
    /// รวมผล <c>type=2</c> ของบัพที่ติดอยู่ทั้งหมด → ชื่อหมวด (snake_case) เป็นตัวคูณที่หายไป
    /// เดินรอบเดียวต่อการคิดหนึ่งครั้ง — เรียกแยกต่อหมวดจะ PackDetails ซ้ำหลายรอบโดยเปล่าประโยชน์
    /// (<c>inside</c> ตัวเดียวมี hot/very_hot/cold/very_cold/scorching_sun/gale/default ครบ 7 คีย์)
    /// </summary>
    private Dictionary<string, double> CollectFatigueModifiers()
    {
        var sum = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (TimedStatusEffect effect in _timedStatusEffects.Values)
        {
            Accumulate(sum, StatusEffectCatalog.PackDetails(effect.Id, effect.Level));
        }
        foreach (string id in _toggledStatusEffects.Keys)
        {
            // toggle ที่ id ซ้ำกับ timed ถูก timed ทับตอนส่ง (Player.cs:706) ⇒ ห้ามนับสองรอบ
            if (_timedStatusEffects.ContainsKey(id)) continue;
            Accumulate(sum, StatusEffectCatalog.PackDetails(id, 1));
        }
        return sum;
    }

    private static void Accumulate(Dictionary<string, double> sum, EffectDetail[] details)
    {
        if (details == null) return;
        foreach (EffectDetail d in details)
        {
            if (d.Type != EffectType.Fatigue || string.IsNullOrEmpty(d.Key)) continue;
            sum[d.Key] = sum.GetValueOrDefault(d.Key) + d.Value;
        }
    }

    /// <summary>
    /// ตราประทับของ "อินพุตที่ไม่ใช่ตำแหน่ง" — ชุดบัพ (id + เลเวล) กับเลเวลตัวละคร
    /// ไม่จองหน่วยความจำ ⇒ เรียกทุกเฟรมได้ ต่างจากการคิดสูตรเต็มซึ่งจอง Dictionary หลายก้อน
    /// </summary>
    private int StatusEffectStamp()
    {
        // เกาะต้องอยู่ในตราด้วย — ย้ายเกาะแล้วบังเอิญยืนช่องเดิม สูตร/หมวดจะค้างของเกาะเก่า
        int h = _context.AppearPlayer.Level * 397;
        h = h * 31 + (_world.TerrainId?.GetHashCode() ?? 0);
        foreach (TimedStatusEffect effect in _timedStatusEffects.Values)
        {
            // XOR — ไม่ขึ้นกับลำดับที่ Dictionary คืนมา
            h ^= StringComparer.OrdinalIgnoreCase.GetHashCode(effect.Id) * 31 + effect.Level;
        }
        foreach (string id in _toggledStatusEffects.Keys)
        {
            h ^= StringComparer.OrdinalIgnoreCase.GetHashCode(id) * 17;
        }
        return h;
    }

    /// <summary>ลายเซ็นแบบหยาบ (ทศนิยม 4 ตำแหน่ง) — ละเอียดกว่านี้จะส่งรัวเพราะเลขแกว่งท้าย ๆ</summary>
    private static string BuildFatigueSignature(Dictionary<FatigueCategory, float> velocities, string effect)
    {
        var sb = new StringBuilder(64);
        sb.Append(effect ?? "-");
        foreach (KeyValuePair<FatigueCategory, float> kv in velocities)
        {
            sb.Append('|').Append((int)kv.Key).Append(':').Append(kv.Value.ToString("F4"));
        }
        return sb.ToString();
    }
}
