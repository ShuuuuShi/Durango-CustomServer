using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Messages;

namespace Durango.Online;

// บัพ/ดีบัพแบบมีเวลา — รวมกับ _toggledStatusEffects ตอนส่ง StatusEffects ทั้งชุด
public partial class Player
{
    private sealed class TimedStatusEffect
    {
        public string Id;
        public int Level = 1;
        public double Since;
        public double Until; // 0 = ไม่มีอายุ (เช่น rest ระหว่างนั่ง / inside ในบ้าน)
        public string NameGettext;
    }

    private const string InsideStatusEffectId = "inside";
    private Point2 _insideCheckedTile = new(int.MinValue, int.MinValue);

    /// <summary>สถานะที่มีเวลาหมดอายุ — คีย์เป็น effect id</summary>
    private readonly Dictionary<string, TimedStatusEffect> _timedStatusEffects =
        new(StringComparer.OrdinalIgnoreCase);

    private const string WeatherSeSourcePrefix = "weather_se:";

    /// <summary>
    /// ใส่/ต่ออายุสถานะจากข้อมูลจริงใน status_effects.json
    /// durationOverride วินาที — ถ้ามีใช้ค่านั้น ไม่งั้นใช้ duration ใน JSON · 0 = ไม่มีอายุ
    /// </summary>
    private bool ApplyTimedStatusEffect(string effectId, int level = 1, double? durationOverride = null)
    {
        if (string.IsNullOrEmpty(effectId)) return false;
        StatusEffectCatalog.Template template = StatusEffectCatalog.Get(effectId, level);
        if (template == null)
        {
            Console.WriteLine($"[สถานะ] ไม่รู้จัก effect '{effectId}' — ไม่ใส่");
            return false;
        }
        level = Math.Clamp(level, template.MinLevel, Math.Max(template.MinLevel, template.MaxLevel));

        // เคารพ deactivates จาก JSON
        if (template.Deactivates != null)
        {
            foreach (string other in template.Deactivates)
            {
                _timedStatusEffects.Remove(other);
                _toggledStatusEffects.Remove(other);
            }
        }

        double now = Times.UnixTimeNow();
        double duration = durationOverride ?? template.DurationSeconds ?? 0;
        var entry = new TimedStatusEffect
        {
            Id = effectId,
            Level = level,
            Since = now,
            Until = duration > 0 ? now + duration : 0
        };
        _timedStatusEffects[effectId] = entry;
        ApplyType1MomentaFromActiveEffects();
        Console.WriteLine($"[สถานะ] {Short(EntityId)} ติด {effectId} lv{level} " +
                          (duration > 0 ? $"{duration:0}วิ" : "ไม่มีอายุ"));
        return true;
    }

    private bool ClearTimedStatusEffect(string effectId)
    {
        if (string.IsNullOrEmpty(effectId)) return false;
        bool removed = _timedStatusEffects.Remove(effectId);
        if (removed) ApplyType1MomentaFromActiveEffects();
        return removed;
    }

    /// <summary>ลบสถานะที่หมดอายุ — คืน true ถ้ามีการเปลี่ยน</summary>
    private bool ExpireTimedStatusEffects()
    {
        if (_timedStatusEffects.Count == 0) return false;
        double now = Times.UnixTimeNow();
        var expired = _timedStatusEffects.Values
            .Where(e => e.Until > 0 && e.Until <= now)
            .Select(e => e.Id)
            .ToList();
        if (expired.Count == 0) return false;
        foreach (string id in expired) _timedStatusEffects.Remove(id);
        ApplyType1MomentaFromActiveEffects();
        return true;
    }

    /// <summary>
    /// รวมผล type=1 จาก SE ที่ยังอยู่ → SurvivalState momenta
    /// volcanic_storm หัก health · volcanic_storm_sign หัก stamina ตาม JSON
    /// </summary>
    private void ApplyType1MomentaFromActiveEffects()
    {
        // เคลียร์ momenta ของ SE เก่าทั้งหมดก่อน แล้วใส่ใหม่จากที่ยังอยู่
        // (ใช้ prefix กันชนกับ online/moving/rest)
        var toClear = _survival.MomentumSources()
            .Where(s => s.StartsWith(WeatherSeSourcePrefix, StringComparison.Ordinal))
            .ToList();
        foreach (string src in toClear)
        {
            _survival.SetMomentum(src, null);
        }

        foreach (TimedStatusEffect se in _timedStatusEffects.Values)
        {
            StatusEffectCatalog.Template t = StatusEffectCatalog.Get(se.Id, se.Level);
            if (t?.Type1Velocities == null || t.Type1Velocities.Count == 0) continue;
            // แปลงชื่อหลอดจาก JSON → คีย์ SurvivalState
            var velocities = new Dictionary<string, float>();
            foreach (KeyValuePair<string, float> kv in t.Type1Velocities)
            {
                string key = kv.Key switch
                {
                    "life" => SurvivalState.KeyLife,
                    "health" => SurvivalState.KeyHealth,
                    "stamina" => SurvivalState.KeyStamina,
                    "energy" => SurvivalState.KeyEnergy,
                    "fatigue" => SurvivalState.KeyFatigue,
                    _ => null
                };
                if (key == null) continue;
                velocities[key] = kv.Value;
            }
            if (velocities.Count > 0)
            {
                _survival.SetMomentum(WeatherSeSourcePrefix + se.Id, velocities);
            }
        }
        FlushSurvival();
    }

    /// <summary>แท็กใน status_effects.json ที่บอกให้เคลียร์สถานะตอนเลเวลตัวละครขึ้น</summary>
    internal const string ClearOnLevelUpTag = "clear_on_levelup";

    internal static bool HasClearOnLevelUpTag(StatusEffectCatalog.Template template)
    {
        if (template?.Tags == null) return false;
        foreach (string tag in template.Tags)
        {
            if (string.Equals(tag, ClearOnLevelUpTag, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// เคลียร์บัพ/ดีบัพที่ติดแท็ก <c>clear_on_levelup</c> ตอนตัวละครขึ้นเลเวล
    /// คืน true ถ้ามีสถานะหายจริง — ผู้เรียกต้อง <see cref="SendStatusEffects"/> ตาม
    /// </summary>
    private bool ClearStatusEffectsOnLevelUp()
    {
        bool changed = false;

        if (_timedStatusEffects.Count > 0)
        {
            var timedIds = new List<string>(_timedStatusEffects.Keys);
            foreach (string id in timedIds)
            {
                TimedStatusEffect entry = _timedStatusEffects[id];
                if (!HasClearOnLevelUpTag(StatusEffectCatalog.Get(id, entry.Level))) continue;
                changed |= ClearTimedStatusEffect(id);
            }
        }

        if (_toggledStatusEffects.Count > 0)
        {
            var toggledIds = new List<string>(_toggledStatusEffects.Keys);
            foreach (string id in toggledIds)
            {
                if (!HasClearOnLevelUpTag(StatusEffectCatalog.Get(id))) continue;
                _toggledStatusEffects.Remove(id);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// ใส่สถานะถ้ายังไม่มีหรือหมดแล้ว — ไม่ต่ออายุของที่ยังอยู่
    /// ใช้ตอนวนทุกเฟรม (ฝนยังตก / ยังยืนในน้ำ) จะได้ไม่ยิง StatusEffects ซ้ำ
    /// </summary>
    private bool EnsureTimedStatusEffect(string effectId, int level = 1)
    {
        if (_timedStatusEffects.TryGetValue(effectId, out TimedStatusEffect existing) &&
            WorldStatusRules.IsActiveTimed(existing.Until, Times.UnixTimeNow()))
        {
            return false;
        }
        return ApplyTimedStatusEffect(effectId, level);
    }

    /// <summary>ซิงก์ SE จากสภาพอากาศปัจจุบันของเกาะ</summary>
    public void SyncWeatherStatusEffects(string weather)
    {
        if (SyncWeatherStatusEffectsCore(weather, refresh: true))
        {
            SendStatusEffects();
        }
    }

    /// <summary>
    /// refresh=true ตอนอากาศเปลี่ยน/เพิ่งเข้าเกาะ — ต่ออายุทันที
    /// refresh=false ตอนวนทุกเฟรม — ใส่คืนเฉพาะตอนหมดแล้ว (wet ใน JSON ยาว 120 วิ แต่ฝนเฟสถัดไป 300 วิ)
    /// </summary>
    private bool SyncWeatherStatusEffectsCore(string weather, bool refresh)
    {
        bool changed = false;
        if (WorldStatusRules.IsRainyWeather(weather))
        {
            changed |= refresh
                ? ApplyTimedStatusEffect("wet", 1)
                : EnsureTimedStatusEffect("wet", 1);
        }

        if (WorldStatusRules.IsVolcanicStormWeather(weather))
        {
            changed |= ClearTimedStatusEffect("volcanic_storm_sign");
            changed |= refresh
                ? ApplyTimedStatusEffect("volcanic_storm", 1)
                : EnsureTimedStatusEffect("volcanic_storm", 1);
        }
        else if (WorldStatusRules.IsVolcanicSignWeather(weather))
        {
            changed |= refresh
                ? ApplyTimedStatusEffect("volcanic_storm_sign", 1)
                : EnsureTimedStatusEffect("volcanic_storm_sign", 1);
        }

        return changed;
    }

    /// <summary>ยืนในน้ำ (มหาสมุทร/แม่น้ำ/ทะเลสาบ) → wet ตาม <c>Durango.Terrain.Util.IsWater</c></summary>
    private bool SyncStandingWaterStatusEffect()
    {
        Movement[] movements = _context.AppearPlayer.Move.Movements;
        if (movements == null || movements.Length == 0 ||
            movements[0].Path == null || movements[0].Path.Length == 0)
        {
            return false;
        }
        WorldPosition pos = movements[0].Path[0].Position;
        Point2 tile = WorldStatusRules.TileFromWorldPosition(pos.x, pos.y);
        Shared.Region.Biome biome = WorldStatusRules.UnmaskBiome(_world.BiomeAt(tile));
        if (!WorldStatusRules.IsWaterBiome(biome)) return false;
        return EnsureTimedStatusEffect("wet", 1);
    }

    /// <summary>
    /// ต่ออายุบัพโลกที่แหล่งยังอยู่ — เรียกทุกเฟรมหลังหมดอายุแล้ว
    /// คืน true ถ้ามีสถานะเปลี่ยน (ผู้เรียกรวมกับ Expire แล้วค่อย Send ครั้งเดียว)
    /// </summary>
    private bool SyncWorldDrivenStatusEffects()
    {
        bool changed = SyncWeatherStatusEffectsCore(_world.Weather, refresh: false);
        changed |= SyncStandingWaterStatusEffect();
        changed |= SyncInsideStatusEffect();
        changed |= SyncCampFireStatusEffect();
        return changed;
    }

    /// <summary>
    /// เดินเข้าเขตบ้านที่เข้าได้ → บัพ <c>inside</c> จาก status_effects.json (ชื่อ "집")
    /// โชว์ชื่อเจ้าของผ่าน NameGettext ถ้ามี
    /// </summary>
    private bool SyncInsideStatusEffect()
    {
        Point2 tile = LifeTileOf(this);
        if (tile.x == _insideCheckedTile.x && tile.y == _insideCheckedTile.y)
        {
            return false;
        }
        _insideCheckedTile = tile;

        AppearArtifact? house = FindEnterableHouseAt(tile);
        bool had = _timedStatusEffects.TryGetValue(InsideStatusEffectId, out TimedStatusEffect existing);
        if (!house.HasValue)
        {
            return had && ClearTimedStatusEffect(InsideStatusEffectId);
        }

        string name = HouseOwnerBuffName(house.Value);
        if (had)
        {
            if (string.Equals(existing.NameGettext, name, StringComparison.Ordinal)) return false;
            existing.NameGettext = name;
            return true;
        }

        if (!ApplyTimedStatusEffect(InsideStatusEffectId, 1, durationOverride: 0)) return false;
        _timedStatusEffects[InsideStatusEffectId].NameGettext = name;
        return true;
    }

    private AppearArtifact? FindEnterableHouseAt(Point2 tile)
    {
        if (tile.x < -500) return null;
        AppearArtifact? best = null;
        foreach (AppearArtifact artifact in _world.ArtifactManager.Enumerable(a => a.IsAlive))
        {
            if (!HouseEnterability.IsEnterable(artifact.EntityType)) continue;
            if (!HouseEnterability.ContainsTile(artifact.Tile, artifact.Size, tile)) continue;
            if (!best.HasValue || artifact.Size.x * artifact.Size.y < best.Value.Size.x * best.Value.Size.y)
            {
                best = artifact;
            }
        }
        return best;
    }

    private string HouseOwnerBuffName(AppearArtifact house)
    {
        string ownerId = _world.ArtifactManager.OwnerOf(house.EntityId);
        if (string.IsNullOrEmpty(ownerId)) ownerId = house.FounderEntityId;
        string ownerName = ResolvePlayerName(ownerId);
        if (string.IsNullOrEmpty(ownerName)) return null;
        string fmt = MoCatalog.Translate("{0} 님의 집");
        return string.Format(fmt, ownerName);
    }

    private string ResolvePlayerName(string entityId)
    {
        if (string.IsNullOrEmpty(entityId)) return null;
        if (string.Equals(entityId, EntityId, StringComparison.Ordinal)) return Name;
        foreach (Player other in _world.PlayersSnapshot())
        {
            if (string.Equals(other.EntityId, entityId, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(other.Name))
            {
                return other.Name;
            }
        }
        return null;
    }

    /// <summary>
    /// ยืนบนที่ดินที่ประกาศแล้ว → บัพ <c>camp_fire</c> (아늑함) จาก status_effects.json
    /// ถอดเมื่อเดินออกจากเขต
    /// </summary>
    private bool SyncCampFireStatusEffect()
    {
        Point2 tile = LifeTileOf(this);
        if (tile.x < -500)
        {
            return _timedStatusEffects.ContainsKey("camp_fire") && ClearTimedStatusEffect("camp_fire");
        }
        Point2 cell = World.CellFromTile(tile);
        bool onEstate = _world.TryGetEstateIdAtCell(cell, out _);
        bool had = _timedStatusEffects.ContainsKey("camp_fire");
        if (!onEstate) return had && ClearTimedStatusEffect("camp_fire");
        if (had) return false;
        return ApplyTimedStatusEffect("camp_fire", 1, durationOverride: 0);
    }
}
