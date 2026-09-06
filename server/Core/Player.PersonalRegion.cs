using System;
using Durango.Utils;
using System.Collections.Generic;
using System.Linq;
using Messages;
using Shared.Estate;
using Shared.Region;
using Shared.Teleport;
using Yaml;
using Yaml.Util;

namespace Durango.Online;

// เกาะส่วนตัวของผู้เล่น — สร้าง/เข้า/ตั้งสิทธิ์เข้า
public partial class Player
{
    /// <summary>ประกอบ PersonalRegionInfo จากเซฟผู้เล่น — PersonalEstate ใส่ทีหลังเมื่อมีระบบที่ดิน</summary>
    private PersonalRegionInfo BuildPersonalRegionInfo()
    {
        if (string.IsNullOrEmpty(_context.PersonalRegionId))
        {
            return default;
        }

        EnsurePersonalWorldRegistered();

        LicenseCategory[] admission = Array.Empty<LicenseCategory>();
        if (_context.PersonalRegionAdmission is { Count: > 0 })
        {
            admission = _context.PersonalRegionAdmission
                .Select(v => (LicenseCategory)Math.Clamp(v, 0, 3))
                .ToArray();
        }

        string templateId = _context.PersonalRegionTemplateId ?? _context.PersonalRegionId;
        var region = new Region
        {
            Id = _context.PersonalRegionId,
            TerrainId = templateId,
            TemplateId = templateId,
            Role = Role.Personal,
            Name = null,
            CreatedAt = 0
        };

        return new PersonalRegionInfo
        {
            PersonalRegion = new Messages.PersonalRegion
            {
                Region = region,
                OwnerId = EntityId,
                PioneerExp = 0,
                AdmissionCategories = admission
            },
            PersonalEstate = FindOwnedEstateLicense(OwnerType.PersonalPlayer)
        };
    }

    private Messages.PersonalRegion BuildPersonalRegionMessage()
    {
        PersonalRegionInfo info = BuildPersonalRegionInfo();
        return info.PersonalRegion ?? default;
    }

    private void EnsurePersonalWorldRegistered()
    {
        if (string.IsNullOrEmpty(_context.PersonalRegionId) ||
            string.IsNullOrEmpty(_context.PersonalRegionTemplateId))
        {
            return;
        }

        WorldRegistry registry = _world.Registry;
        if (registry == null)
        {
            return;
        }
        registry.RegisterPersonalRegion(_context.PersonalRegionId, _context.PersonalRegionTemplateId);
        registry.GetOrCreate(_context.PersonalRegionId);
    }

    private static bool IsAllowedPersonalTemplate(string templateId)
    {
        List<string> ids = Singleton<Constants>.Instance?.PersonalRegion?.RegionTemplateIds;
        if (ids == null || ids.Count == 0)
        {
            return !string.IsNullOrEmpty(templateId);
        }
        return ids.Contains(templateId);
    }

    private string MakePersonalRegionId()
    {
        // **ค่าของเรา** — id คงที่ต่อตัวละคร รีสตาร์ตแล้วยังชี้โลกเดิมได้
        string shortId = EntityId.Length <= 8 ? EntityId : EntityId[..8];
        return "personal_" + shortId;
    }

    private void HandleRecommendPersonalRegion(RecommendPersonalRegion msg, uint seq)
    {
        string templateId = msg.TemplateId;
        if (string.IsNullOrEmpty(templateId))
        {
            Send(new Abort { Text = "ต้องเลือกภูมิประเทศของเกาะส่วนตัว" }, seq);
            return;
        }
        if (!IsAllowedPersonalTemplate(templateId))
        {
            Send(new Abort { Text = "ภูมิประเทศนี้ใช้สร้างเกาะส่วนตัวไม่ได้" }, seq);
            return;
        }

        if (!string.IsNullOrEmpty(_context.PersonalRegionId))
        {
            if (string.IsNullOrEmpty(_context.PersonalRegionTemplateId))
            {
                _context.PersonalRegionTemplateId = templateId;
            }
            EnsurePersonalWorldRegistered();
            Send(BuildPersonalRegionMessage(), seq);
            Console.WriteLine($"[เกาะส่วนตัว] {Short(EntityId)} มีเกาะแล้ว {_context.PersonalRegionId}");
            return;
        }

        _context.PersonalRegionId = MakePersonalRegionId();
        _context.PersonalRegionTemplateId = templateId;
        _context.PersonalRegionAdmission ??= new List<int>();
        EnsurePersonalWorldRegistered();
        if (!string.IsNullOrEmpty(_context.Path))
        {
            _context.Save();
        }
        OnContextChanged();
        Send(BuildPersonalRegionMessage(), seq);
        Console.WriteLine($"[เกาะส่วนตัว] {Short(EntityId)} สร้าง {_context.PersonalRegionId} จาก {templateId}");
    }

    private void HandleReturnToEstate(ReturnToEstate msg, uint seq)
    {
        if (msg.OwnerType != OwnerType.PersonalPlayer)
        {
            Send(new Abort { Text = "ยังไม่มีที่ดินชนิดนี้ให้กลับไป" }, seq);
            return;
        }
        if (string.IsNullOrEmpty(_context.PersonalRegionId))
        {
            Send(new Abort { Text = "ยังไม่มีเกาะส่วนตัว — สร้างจากหน้าที่ดินส่วนตัวก่อน" }, seq);
            return;
        }
        EnsurePersonalWorldRegistered();
        float duration = 1f; // **ค่าของเรา**
        Send(new Messages.Timer { Duration = duration }, seq);
        Console.WriteLine($"[เกาะส่วนตัว] {Short(EntityId)} กลับเกาะ {_context.PersonalRegionId}");
        string dest = _context.PersonalRegionId;
        System.Threading.Timer timer = null;
        timer = new System.Threading.Timer(_ =>
        {
            try
            {
                _context.RegionId = dest;
                _context.AppearPlayer.Move.Movements = null;
                if (!string.IsNullOrEmpty(_context.Path))
                {
                    _context.Save();
                }
                Send(new Emigrated { Type = TeleportType.Unknown });
            }
            catch (Exception e)
            {
                Console.WriteLine($"[เกาะส่วนตัว] ย้ายเกาะไม่สำเร็จ: {e.Message}");
            }
            finally
            {
                timer?.Dispose();
            }
        }, null, (int)(duration * 1000f), System.Threading.Timeout.Infinite);
    }

    private void HandleVisitEstate(VisitEstate msg, uint seq)
    {
        if (msg.OwnerType != OwnerType.PersonalPlayer)
        {
            Send(new Abort { Text = "ยังเดินทางไปที่ดินชนิดนี้ไม่ได้" }, seq);
            return;
        }
        string dest = msg.RegionId;
        if (string.IsNullOrEmpty(dest) || !dest.StartsWith("personal_", StringComparison.OrdinalIgnoreCase))
        {
            Send(new Abort { Text = "ไม่ทราบเกาะส่วนตัวปลายทาง" }, seq);
            return;
        }
        float duration = 1f;
        Send(new Messages.Timer { Duration = duration }, seq);
        Console.WriteLine($"[เกาะส่วนตัว] {Short(EntityId)} เยี่ยม {dest}");
        System.Threading.Timer timer = null;
        timer = new System.Threading.Timer(_ =>
        {
            try
            {
                _context.RegionId = dest;
                _context.AppearPlayer.Move.Movements = null;
                if (!string.IsNullOrEmpty(_context.Path))
                {
                    _context.Save();
                }
                Send(new Emigrated { Type = TeleportType.Unknown });
            }
            catch (Exception e)
            {
                Console.WriteLine($"[เกาะส่วนตัว] เยี่ยมเกาะไม่สำเร็จ: {e.Message}");
            }
            finally
            {
                timer?.Dispose();
            }
        }, null, (int)(duration * 1000f), System.Threading.Timeout.Infinite);
    }

    private void HandleSetPersonalRegionAdmission(SetPersonalRegionAdmission msg)
    {
        if (string.IsNullOrEmpty(_context.PersonalRegionId))
        {
            return;
        }
        if (msg.AdmissionCategories == null || msg.AdmissionCategories.Length == 0)
        {
            _context.PersonalRegionAdmission = new List<int>();
        }
        else
        {
            _context.PersonalRegionAdmission = msg.AdmissionCategories.Select(c => (int)c).ToList();
        }
        if (!string.IsNullOrEmpty(_context.Path))
        {
            _context.Save();
        }
        Console.WriteLine($"[เกาะส่วนตัว] {Short(EntityId)} ตั้ง admission = {_context.PersonalRegionAdmission.Count}");
    }

    private const int PersonalEstateMaxSize = 30;

    private EstateLicenses BuildEstateLicenses()
    {
        EstateLicense? personal = null;
        EstateLicense? urban = null;
        int largestPersonal = 0;
        int largestUrban = 0;
        foreach (var kv in _world.EnumerateEstates())
        {
            if (kv.Value.OwnerId != EntityId) continue;
            EstateLicense lic = _world.ToLicense(kv.Key, kv.Value);
            if (kv.Value.Type == (int)OwnerType.PersonalPlayer)
            {
                personal = lic;
                if (kv.Value.Size > largestPersonal) largestPersonal = kv.Value.Size;
            }
            else if (kv.Value.Type == (int)OwnerType.Player)
            {
                urban = lic;
                if (kv.Value.Size > largestUrban) largestUrban = kv.Value.Size;
            }
        }
        return new EstateLicenses
        {
            PersonalEstate = personal,
            UrbanEstate = urban,
            LargestPersonalEstateSize = largestPersonal,
            LargestUrbanEstateSize = largestUrban
        };
    }

    private string CurrentRegionIdForEstate()
    {
        if (!string.IsNullOrEmpty(_context.RegionId)) return _context.RegionId;
        return _world.TerrainId ?? "1";
    }

    private void BroadcastEstateGridsAround(Point2 cell)
    {
        int tileX = cell.x * World.EstateGridSize;
        int tileY = cell.y * World.EstateGridSize;
        int cx = tileX / 16;
        int cy = tileY / 16;
        var chunks = new List<Point2>();
        for (int x = cx - 1; x <= cx + 1; x++)
        for (int y = cy - 1; y <= cy + 1; y++)
        {
            if (x >= 0 && y >= 0) chunks.Add(new Point2(x, y));
        }
        _world.BroadCast(_world.BuildEstateGridsForChunks(chunks));
    }

    private void HandleDeclareEstate(DeclareEstate msg, uint seq)
    {
        if (msg.OwnerType != OwnerType.PersonalPlayer && msg.OwnerType != OwnerType.Player)
        {
            Send(new Abort { Text = "ประกาศที่ดินชนิดนี้ยังไม่รองรับ" }, seq);
            return;
        }
        if (msg.OwnerType == OwnerType.PersonalPlayer)
        {
            if (string.IsNullOrEmpty(_context.PersonalRegionId))
            {
                Send(new Abort { Text = "ต้องมีเกาะส่วนตัวก่อนจึงจะประกาศที่ดินได้" }, seq);
                return;
            }
            if (!string.Equals(_context.RegionId, _context.PersonalRegionId, StringComparison.OrdinalIgnoreCase))
            {
                Send(new Abort { Text = "ต้องอยู่บนเกาะส่วนตัวของตัวเองก่อนประกาศที่ดิน" }, seq);
                return;
            }
        }
        EstateLicense? license = _world.DeclareEstate(EntityId, msg.OwnerType, msg.Cell, CurrentRegionIdForEstate());
        if (!license.HasValue)
        {
            Send(new Abort { Text = "ประกาศที่ดินไม่ได้ — ช่องถูกจองแล้วหรือมีที่ดินชนิดนี้อยู่แล้ว" }, seq);
            return;
        }
        Send(license.Value, seq);
        BroadcastEstateGridsAround(msg.Cell);
        Console.WriteLine($"[ที่ดิน] {Short(EntityId)} ประกาศ {msg.OwnerType} cell [{msg.Cell.x},{msg.Cell.y}] → {license.Value.EstateId}");
        OnContextChanged();
    }

    private void HandleExpandEstate(ExpandEstate msg, uint seq)
    {
        EstateLicense? license = _world.ExpandEstate(msg.EstateId, EntityId, msg.Cell, PersonalEstateMaxSize);
        if (!license.HasValue)
        {
            Send(new Abort { Text = "ขยายที่ดินไม่ได้" }, seq);
            return;
        }
        Send(license.Value, seq);
        BroadcastEstateGridsAround(msg.Cell);
        OnContextChanged();
    }

    private void HandleShrinkEstate(ShrinkEstate msg, uint seq)
    {
        EstateLicense? license = _world.ShrinkEstate(msg.EstateId, EntityId, msg.Cell);
        if (!license.HasValue)
        {
            Send(new Abort { Text = "ลดขนาดที่ดินไม่ได้" }, seq);
            return;
        }
        Send(license.Value, seq);
        BroadcastEstateGridsAround(msg.Cell);
        OnContextChanged();
    }

    private void HandleRemoveEstate(RemoveEstate msg)
    {
        EstateRecord rec = _world.GetEstate(msg.EstateId);
        Point2 cell = default;
        if (rec != null && rec.Cells.Count > 0)
        {
            string[] parts = rec.Cells[0].Split(',');
            cell = new Point2(int.Parse(parts[0]), int.Parse(parts[1]));
        }
        if (_world.RemoveEstate(msg.EstateId, EntityId))
        {
            if (rec != null) BroadcastEstateGridsAround(cell);
            Console.WriteLine($"[ที่ดิน] {Short(EntityId)} รื้อ {msg.EstateId}");
            OnContextChanged();
        }
    }

    private void HandleSetEstateLicense(SetEstateLicense msg, uint seq)
    {
        EstateRecord rec = _world.GetEstate(msg.EstateId);
        if (rec == null || rec.OwnerId != EntityId)
        {
            Send(new Abort { Text = "ไม่พบที่ดินหรือไม่ใช่ของตน" }, seq);
            return;
        }
        rec.AccessForOthers = (int)msg.AccessRights.ForOthers;
        _world.Save();
        Send(default(OK), seq);
        if (rec.Cells.Count > 0)
        {
            string[] parts = rec.Cells[0].Split(',');
            BroadcastEstateGridsAround(new Point2(int.Parse(parts[0]), int.Parse(parts[1])));
        }
        OnContextChanged();
    }

    private void HandleExtendEstate(ExtendEstateActivation msg, uint seq)
    {
        EstateRecord rec = _world.GetEstate(msg.EstateId);
        if (rec == null || rec.OwnerId != EntityId)
        {
            Send(new Abort { Text = "ไม่พบที่ดินหรือไม่ใช่ของตน" }, seq);
            return;
        }
        // **ค่าของเรา** — ต่ออายุฟรี 7 วัน
        double now = Times.UnixTimeNow();
        double baseTime = rec.ExpiresAt.HasValue && rec.ExpiresAt.Value > now ? rec.ExpiresAt.Value : now;
        rec.ExpiresAt = baseTime + 7 * 24 * 3600;
        _world.Save();
        Send(_world.ToLicense(msg.EstateId, rec), seq);
        if (rec.Cells.Count > 0)
        {
            string[] parts = rec.Cells[0].Split(',');
            BroadcastEstateGridsAround(new Point2(int.Parse(parts[0]), int.Parse(parts[1])));
        }
        OnContextChanged();
    }

    private EstateLicense? FindOwnedEstateLicense(OwnerType type)
    {
        foreach (var kv in _world.EnumerateEstates())
        {
            if (kv.Value.OwnerId == EntityId && kv.Value.Type == (int)type)
            {
                return _world.ToLicense(kv.Key, kv.Value);
            }
        }
        return null;
    }
}
