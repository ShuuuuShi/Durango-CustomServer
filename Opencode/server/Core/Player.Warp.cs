using System;
using System.Collections.Generic;
using Durango.Network;
using Messages;
using Shared.Teleport;
using Yaml.Util;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  วาร์ป / กลับบ้าน / ไปท่าเรือ
//
//  เดิมเซิร์ฟไม่มี handler ทั้งสาย ⇒ เกมกดปุ่มแล้วเงียบ (WarpTimer นับครบแล้วไม่มีอะไรเกิด)
//  ลำดับที่ฝั่งเกมใช้จริง (client/MapSystem.cs:477-613 TryWarp → DoWarp):
//    1. ส่ง ReturnToHome(2100) / WarpToPort(9081241) แล้วเล่นอนิเมชัน "Warp_Begin"
//    2. **รอ Timer(1134) กลับมาที่ seq นั้น** → WarpTimer.Play(msg.Duration)
//    3. เซิร์ฟส่ง Teleported(2037) → client เด้งม่านโหลด ย้ายตัวเองไป Tile แล้วเล่น "Warp_End"
//       (client/PlayerManager.cs:346-354 → PlayerController.cs:586-616, TeleportType.Warp)
//
//  เวลาเวปมาจาก constants.json → warp.warp_time = 2 (วินาที) — ไม่ hardcode
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    /// <summary>ตัวนัดส่ง Teleported เมื่อครบเวลาเวป — กันไฟล์รั่วแบบเดียวกับ _buildTimers</summary>
    private readonly List<System.Threading.Timer> _warpTimers = new();

    private void RegisterWarpHandlers()
    {
        _connection.ConnetionClosed += ClearWarpTimers;

        // Dashed (2491) — กระโดด: client ยิงแล้ว **ไม่ผูกรอ** อะไรกลับมาเลย
        // (client/PlayerController.cs:655-657 TryJump → Send ธรรมดา ไม่มี .On)
        // แรงเป็นของ client คิดเองทั้งหมด (เช็ค "스태미나가 부족합니다" ก่อนยิงแล้ว) ⇒ เซิร์ฟไม่มี
        // อะไรจะตรวจ ไม่มีอะไรจะกระจาย (client ไม่มี On<Dashed> รับ) — ลงทะเบียนเปล่าไว้แค่ให้
        // ล็อกเลิกพิมพ์ "ไม่มี handler" ทุกครั้งที่กระโดด
        _connection.Recv(delegate(Dashed msg, PacketHeader header)
        {
        });

        // WarpToPort (9081241) — ปุ่ม "ท่าเรือ" บนแผนที่โลก
        // ปุ่มโผล่เมื่อเกาะมีท่าเรือ (client/Durango.UI/WorldMapGroup.cs:223 ValidFunc ดู POICount.Port)
        // ปลายทางเซิร์ฟเลือกเองจาก pois.yml → port_points (แหล่งเดียวกับที่เกมวาดหมุดท่าเรือ)
        _connection.Recv(delegate(WarpToPort msg, PacketHeader header)
        {
            Point2? port = FindNearestPortTile();
            if (!port.HasValue)
            {
                Send(new Abort { Text = "ไม่มีท่าเรือบนเกาะนี้" }, header.Seq);
                return;
            }
            BeginInRegionWarp(port.Value, header.Seq);
        });

        // ReturnToHome (2100) — ปุ่ม "บ้าน" (icon_house) บนแผนที่โลก
        // client เช็ค homePoint ?? returningPoint แล้วส่งมาแบบไม่บอกปลายทาง (client/MapSystem.cs:446-459)
        // เซิร์ฟยังไม่เคยส่ง Points(2033) ⇒ ฝั่งเกมไม่รู้จุดบ้าน ⇒ **เซิร์ฟเป็นคนตัดสิน**: นิยาม "บ้าน"
        // ของเซิร์ฟนี้คือ **เกาะตั้งต้น** (RegionId = null — ความหมายเดียวกับ HandleTravelMsg
        // ใน Core/Player.cs ที่ใช้ null = กลับเกาะตั้งต้น) · ถ้าอยู่เกาะตั้งต้นแล้ว ให้เวปกลับจุดเข้าเกาะ
        _connection.Recv(delegate(ReturnToHome msg, PacketHeader header)
        {
            HandleReturnToHomeMsg(header.Seq);
        });
    }

    // ── กลับบ้าน ─────────────────────────────────────────────────────────────────────

    private void HandleReturnToHomeMsg(uint seq)
    {
        if (!string.IsNullOrEmpty(_context.RegionId))
        {
            // อยู่เกาะอื่น → กลับเกาะตั้งต้น ใช้กลไก re-entry เดียวกับการออกเรือ (Emigrated)
            // แต่ Type = Warp ⇒ client ตั้ง EmigratedType.Warp ไม่ใช่ Explore
            // (client/GameManager.cs:322-335 — default branch ⇒ Warp · TeleportType.Unknown ⇒ Explore)
            Send(new Messages.Timer { Duration = WarpTime() }, seq);
            _context.RegionId = null;                       // null = เกาะตั้งต้น (ดู WorldRegistry.GetOrCreate)
            _context.AppearPlayer.Move.Movements = null;    // ให้จุดเข้าของเกาะตั้งต้นเป็นตัววางตำแหน่ง
            if (!string.IsNullOrEmpty(_context.Path))
            {
                _context.Save();
            }
            Console.WriteLine($"[เวป] {Short(EntityId)} กลับบ้าน {_world.TerrainId} → (เกาะตั้งต้น)");
            Send(new Emigrated { Type = TeleportType.Warp });
            return;
        }

        // อยู่เกาะตั้งต้นแล้ว (ปุ่มปกติจะซ่อน แต่บาง role ยังโชว์) → เวปกลับจุดเข้าเกาะ
        BeginInRegionWarp(_world.EntryPoint, seq);
    }

    // ── วาร์ปภายในเกาะเดียวกัน ───────────────────────────────────────────────────────

    /// <summary>
    /// เวปไป tile อื่นบนเกาะเดียวกัน — ตอบ Timer ตามที่ client รอ แล้วนัดส่ง Teleported
    /// เมื่อครบ warp_time (อนิเมชัน "Warp_Begin" เล่นจนครบ แล้วม่านโหลดค่อยย้ายตัว)
    ///
    /// ตำแหน่งจริงฝั่งเซิร์ฟปรับ **ทันที** ไม่รอหน่วง — ถ้าถูกตัดการเชื่อมต่อกลางเวป
    /// ผู้เล่นจะเข้าใหม่ที่ปลายทาง ไม่ใช่ค้างกลางอากาศ (สเกล ×200 ตามนิยามตำแหน่งเดียวกับ
    /// GetEntryPosition ใน Core/Player.cs และ Revive ใน Core/Player.Combat.cs)
    ///
    /// ⚠️ ไม่ broadcast Teleported — ฝั่งเกม handler นี้ย้าย "ตัวเอง" เท่านั้น
    /// (client/PlayerManager.cs:346 ไม่ดู EntityId) คนอื่นจะเห็นตำแหน่งใหม่เมื่อผู้เล่นเดินแล้ว
    /// client ส่ง Move ตามมาเอง เหมือนกรณีฟื้นหลังตาย
    /// </summary>
    private void BeginInRegionWarp(Point2 tile, uint seq)
    {
        Movement[] movements = _context.AppearPlayer.Move.Movements;
        if (movements != null && movements.Length > 0 && movements[0].Path != null && movements[0].Path.Length > 0)
        {
            movements[0].Path[0].Position = new WorldPosition(tile.x * 200, tile.y * 200);
        }
        if (!string.IsNullOrEmpty(_context.Path))
        {
            _context.Save();
        }
        Console.WriteLine($"[เวป] {Short(EntityId)} ย้ายไป [{tile.x},{tile.y}] บน {_world.TerrainId}");

        float duration = WarpTime();
        Send(new Messages.Timer { Duration = duration }, seq);
        ScheduleWarpReply(() => Send(new Teleported { Tile = tile, Type = TeleportType.Warp }), duration);
        OnContextChanged();
    }

    /// <summary>ท่าเรือที่ใกล้ตัวที่สุด — pois.yml คือแหล่งเดียวกับที่ World วางท่าเรือจริง</summary>
    private Point2? FindNearestPortTile()
    {
        TerrainPois pois = LoadPois(null);
        if (pois == null || pois.PortPoints.Count == 0)
        {
            return null;
        }

        Point2 best = pois.PortPoints[0];
        long bestDist = long.MaxValue;
        Movement[] movements = _context.AppearPlayer.Move.Movements;
        if (movements != null && movements.Length > 0 && movements[0].Path != null && movements[0].Path.Length > 0)
        {
            WorldPosition pos = movements[0].Path[0].Position;
            foreach (Point2 port in pois.PortPoints)
            {
                long dx = (long)(port.x * 200 - pos.x);
                long dy = (long)(port.y * 200 - pos.y);
                long dist = dx * dx + dy * dy;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = port;
                }
            }
        }
        return best;
    }

    /// <summary>เวลาเวปจาก constants.json → warp.warp_time (วินาที) — ไฟล์ขาดให้ 2 ตามไฟล์จริง</summary>
    private static float WarpTime()
    {
        float? time = Singleton<Yaml.Constants>.Instance?.Warp?.WarpTime;
        return time is > 0f ? time.Value : 2f;
    }

    /// <summary>นัดส่งคำตอบตอนครบเวลาเวป — callback ทำแค่ Send (pattern เดียวกับ ScheduleBuildReply)</summary>
    private void ScheduleWarpReply(Action send, float duration)
    {
        System.Threading.Timer timer = null;
        timer = new System.Threading.Timer(delegate
        {
            try
            {
                send();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[เวป] ส่ง Teleported ไม่สำเร็จ: {e.Message}");
            }
            finally
            {
                lock (_warpTimers) { _warpTimers.Remove(timer); }
                timer?.Dispose();
            }
        }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

        lock (_warpTimers) { _warpTimers.Add(timer); }
        timer.Change((int)(duration * 1000f), System.Threading.Timeout.Infinite);
    }

    private void ClearWarpTimers()
    {
        lock (_warpTimers)
        {
            foreach (System.Threading.Timer timer in _warpTimers) timer.Dispose();
            _warpTimers.Clear();
        }
    }
}
