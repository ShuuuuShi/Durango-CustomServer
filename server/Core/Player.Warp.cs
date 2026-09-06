using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Network;
using Durango.Utils;
using Messages;
using Shared.Etc;
using Shared.Teleport;
using Yaml;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
// ระบบ "จุดกลับ" และการวาร์ป — [6 ก.ย. 2026]
//
// ก่อนมีไฟล์นี้: เซิร์ฟ **ไม่เคยส่ง Points(2033) เลยสักครั้ง** ⇒ ฝั่งเกม MapSystem.Points
// เป็นค่าว่างตลอดเกม ⇒ ปุ่มบนแผนที่ทั้งแถวกดแล้วเงียบ และเมนู "귀환 지점으로 지정" ที่เตียง
// ยิงมาแล้วไม่มีใครรับ (ไม่มี error ให้เห็น ผู้เล่นกดซ้ำไปเรื่อย ๆ)
//
// ═══ ลำดับที่ฝั่งเกมเดินจริง ═══
//
//   ตั้งบ้าน:  แตะเตียง → เมนู "귀환 지점으로 지정" (Interaction.SetAsHome = 410)
//              client → SetAsHome(2102) {EntityId, Tile}
//              server → OK(1231)  แล้วผู้เล่นเห็นข้อความยืนยัน
//              (client/Durango.Logic.Interactions/ArtifactInteractions.cs:1183-1194 — รอ .On<OK>)
//
//   ตั้งจุดเกิด: เดินผ่าน trigger ในฉาก → SetReturningPoint(2105) {Tile}
//              (client/PlayerTriggerMakeCheckPoint.cs:11)
//
//   กลับบ้าน:  เปิดแผนที่ → ปุ่มไอคอนบ้าน → client/MapSystem.cs:446-458 ReturnToHome()
//              client → ReturnToHome(2100)  (struct ว่าง — ปลายทางไม่ได้มากับข้อความ)
//              server → Timer(1134) ที่ seq เดิม  แล้วครบเวลาค่อย Teleported(2037) แบบ ReplyOf=0
//
//   ไปท่าเรือ: client → WarpToPort(9081241) (struct ว่างเหมือนกัน) → ชุดคำตอบเดียวกัน
//
// ═══ กับดักที่ต้องระวัง (ยืนยันจากซอร์สฝั่งเกมแล้ว) ═══
//
//   ① **Timer ต้องเป็นคำตอบแรกและตัวเดียวที่ seq นั้น**
//      client/MapSystem.cs:599-612 ผูก .On<Timer>(…).Rest(… WarpTimer.Stop())
//      ⇒ ตอบ OK หรืออะไรก็ตามก่อน Timer = .Rest ทำงานทันที หลอดหยุด แล้ว Timer ที่ตามมา
//        ไม่มีใครรับ (client/Durango.Network/Connection.cs:904-907 ลบ handler หลังตอบแรก)
//      ⇒ ใช้ .Rest ให้เป็นประโยชน์: ส่ง Abort ที่ seq นี้ = หลอดหยุด + ข้อความเด้งให้ผู้เล่นเห็น
//
//   ② **Teleported ต้องส่ง ReplyOf = 0** — ฝั่งเกมรับด้วย global handler
//      (client/PlayerManager.cs:346-353) ส่งที่ seq จะไปโดน .Rest แทน แล้วตัวละครไม่ขยับ
//
//   ③ **Teleported.Tile หน่วยเป็นช่อง ไม่ใช่ world unit** — ฝั่งเกมคูณ 200 เอง
//      แต่ตำแหน่งที่เซิร์ฟจำต้องเป็น world unit ⇒ ต้องอัปเดตสองที่คนละหน่วย
//      ไม่อัปเดตฝั่งเซิร์ฟ = ด่านระยะทุกตัว (เก็บของ/รื้อ/สร้าง) ยังคิดจากตำแหน่งเก่าแบบเงียบ ๆ
//
//   ④ **TeleportType ต้องเป็น Returning** — ใส่ Warp/WarpBack จะไปทำให้บทไกด์
//      client/Durango.Logic.PlayGuide/WarpToDo.cs นับภารกิจ "วาร์ป" สำเร็จผิดตัว
//
// ═══ สิ่งที่ยังไม่ทำ (ตั้งใจ) ═══
//   • ค่าวาร์ป (constants.json → warp → warp_cost) — ต้องมีระบบเงิน t_stone ก่อน ⇒ ฟรีไปก่อน
//   • วาร์ปข้ามเกาะ — ReturnToHome ข้ามเกาะต้องส่ง Emigrated(2099) ซึ่ง **ตัดการเชื่อมต่อทันที**
//     (client/GameManager.cs:322-336) แล้วต้องจัดการย้ายไฟล์เซฟแบบเดียวกับระบบล่องเรือ
//     ⇒ รอบนี้จำกัดบ้านไว้ที่เกาะเดียวกันเท่านั้น และบอกผู้เล่นตรง ๆ ถ้าบ้านอยู่คนละเกาะ
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    /// <summary>
    /// นาฬิกาที่รอย้ายตัวผู้เล่นเมื่อครบเวลาวาร์ป
    ///
    /// ⚠️ ต่างจากระบบคราฟต์/ก่อสร้างตรงที่ callback ตัวนี้ **แก้สถานะผู้เล่นจริง**
    /// (ตำแหน่งใน context) ไม่ใช่แค่ Send — ทำแบบนั้นได้เพราะสิ่งที่แก้เป็นของผู้เล่นคนนี้คนเดียว
    /// ไม่มีใครอื่นอ่าน/เขียนพร้อมกัน และ Connection.Send ล็อกของมันเองอยู่แล้ว
    /// </summary>
    private readonly List<System.Threading.Timer> _warpTimers = new();

    /// <summary>**ค่าของเรา** — วาร์ปค้างพร้อมกันได้กี่คิว (กันยิงรัวจองหน่วยความจำ)</summary>
    private const int MaxConcurrentWarps = 3;

    private void RegisterWarpHandlers()
    {
        _connection.Recv(delegate(SetAsHome msg, PacketHeader header)
        {
            HandleSetAsHomeMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(SetReturningPoint msg, PacketHeader header)
        {
            HandleSetReturningPointMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(ReturnToHome msg, PacketHeader header)
        {
            HandleReturnToHomeMsg(header.Seq);
        });
        _connection.Recv(delegate(WarpToPort msg, PacketHeader header)
        {
            HandleWarpToPortMsg(header.Seq);
        });

        // ลบหมุด "จุดที่ตายล่าสุด" ออกจากแผนที่ — ฝั่งเกมยิงเปล่าแล้ว **ไม่รอคำตอบ**
        // (client/MapSystem.cs:655-657 Send(default(RemoveDeathPoint)) ไม่ต่อ .On/.Rest)
        // ⇒ ห้ามตอบที่ seq · หน้าที่เดียวคือส่งชุดจุดสำคัญใหม่ที่ไม่มี DeathPoint แล้ว
        //
        // หมายเหตุ: เซิร์ฟยังไม่มีระบบเก็บจุดที่ตาย (SendPoints ส่ง DeathPoint = null อยู่แล้ว)
        // แต่ต้องรับข้อความนี้ ไม่งั้นมันตกไปที่ "ไม่มี handler" ทุกครั้งที่ผู้เล่นกดปุ่ม
        _connection.Recv(delegate(RemoveDeathPoint msg, PacketHeader header)
        {
            SendPoints();
        });
        _connection.ConnetionClosed += ClearWarpTimers;

        // ส่งชุดจุดสำคัญตอนเข้าเกม — ไม่ส่ง = ฝั่งเกมไม่รู้ว่ามีบ้าน ปุ่มบนแผนที่เป็นสีเทาทั้งแถว
        SendPoints();
    }

    // ── ตั้งบ้าน ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ตั้งสิ่งปลูกสร้างเป็นจุดกลับ — ต้องเป็น "เตียง" ของตัวเองที่ยืนอยู่ใกล้
    ///
    /// ข้อมูลจริง: 12 แบบแปลนมี component <c>Home</c> (bed_01..bed_04 · tent · temptent ฯลฯ)
    /// — เช็คจาก component ไม่ใช่รายชื่อ id เพราะไฟล์อาจเพิ่มแบบใหม่ทีหลัง
    /// </summary>
    private void HandleSetAsHomeMsg(SetAsHome msg, uint seq)
    {
        if (!MayTouchArtifact(msg.EntityId, "ตั้งเป็นจุดกลับ"))
        {
            Send(new Abort { Text = "ตั้งจุดกลับที่นี่ไม่ได้" }, seq);
            return;
        }
        if (_world.ArtifactManager.Get(msg.EntityId) is not { } artifact)
        {
            Send(new Abort { Text = "ไม่พบสิ่งปลูกสร้างนี้" }, seq);
            return;
        }

        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(artifact.EntityType);
        if (blueprint?.Components == null || !blueprint.Components.Contains("Home"))
        {
            Send(new Abort { Text = "ตั้งจุดกลับได้เฉพาะที่นอนเท่านั้น" }, seq);
            return;
        }

        _context.HomeArtifactId = msg.EntityId;
        OnContextChanged();

        Console.WriteLine($"[วาร์ป] {Short(EntityId)} ตั้ง {blueprint.Id} " +
                          $"ที่ [{artifact.Tile.x},{artifact.Tile.y}] เป็นจุดกลับ");

        // ฝั่งเกมรอ .On<OK> ตัวเดียว แล้วเด้งข้อความยืนยันเอง
        // (client/Durango.Logic.Interactions/ArtifactInteractions.cs:1190-1193)
        Send(default(OK), seq);
        SendPoints();
    }

    private void HandleSetReturningPointMsg(SetReturningPoint msg, uint seq)
    {
        // [7 ก.ย. 2026] เดิมเชื่อ tile ดิบจาก client ⇒ ยิงพิกัดนอกแผนที่/ติดลบได้
        // ตั้งจุดกลับนอกเกาะแล้ววาร์ปกลับไปจะหลุดออกนอกโลก
        if (!IsTileInsideWorld(msg.Tile))
        {
            Send(new Abort { Text = "จุดกลับอยู่นอกเกาะ" }, seq);
            return;
        }
        _context.ReturningX = msg.Tile.x;
        _context.ReturningY = msg.Tile.y;
        OnContextChanged();
        Send(default(OK), seq);
        SendPoints();
    }

    /// <summary>ช่องนี้อยู่ในขอบเขตของเกาะจริงไหม — กันพิกัดปลอมจาก client</summary>
    private bool IsTileInsideWorld(Point2 tile)
    {
        if (tile.x < 0 || tile.y < 0) return false;
        int width = _world.NumTilesX;
        int height = _world.NumTilesY;
        if (width <= 0 || height <= 0) return true;   // ไม่รู้ขนาด = กันแค่ค่าติดลบ
        return tile.x < width && tile.y < height;
    }

    // ── กลับบ้าน / ไปท่าเรือ ─────────────────────────────────────────────────────────

    private void HandleReturnToHomeMsg(uint seq)
    {
        if (!TryResolveHomeTile(out Point2 tile, out string error))
        {
            // ตอบที่ seq เดิม = ไปโดน .Rest ของฝั่งเกม ⇒ หลอดวาร์ปหยุด + ข้อความเด้ง
            // (ไม่ตอบเลย = ตัวละครยืนเล่นท่าวาร์ปค้าง ~5 วิ แล้วเงียบ โดยไม่รู้ว่าทำไม)
            Send(new Abort { Text = error }, seq);
            return;
        }
        BeginWarp(tile, seq, "กลับจุดกลับ");
    }

    private void HandleWarpToPortMsg(uint seq)
    {
        TerrainPois pois = LoadPois(null);
        List<Point2> ports = pois?.PortPoints;
        if (ports == null || ports.Count == 0)
        {
            Send(new Abort { Text = "เกาะนี้ไม่มีท่าเรือ" }, seq);
            return;
        }
        BeginWarp(NearestTo(ports), seq, "ไปท่าเรือ");
    }

    /// <summary>
    /// จุดที่จะกลับไป — บ้านก่อน ถ้าไม่มีค่อยใช้จุดเกิดที่ตั้งไว้ ไม่มีอีกก็จุดเข้าเกาะ
    ///
    /// ⚠️ ต้องเช็คว่าบ้านยังอยู่จริง — ผู้เล่นรื้อเตียงทิ้งได้ และ entity id ที่ค้างอยู่ในไฟล์เซฟ
    /// จะพาไปวาร์ปลงที่ว่างกลางเกาะแบบเงียบ ๆ
    /// </summary>
    private bool TryResolveHomeTile(out Point2 tile, out string error)
    {
        error = null;
        tile = default;

        if (!string.IsNullOrEmpty(_context.HomeArtifactId))
        {
            if (_world.ArtifactManager.Get(_context.HomeArtifactId) is { } home)
            {
                tile = home.Tile;
                return true;
            }
            // บ้านหายไปแล้ว (ถูกรื้อ / อยู่คนละเกาะ) — ล้างทิ้งเพื่อไม่ให้ค้างพาไปผิดที่ทุกครั้ง
            Console.WriteLine($"[วาร์ป] {Short(EntityId)} บ้าน {_context.HomeArtifactId} ไม่อยู่บนเกาะนี้แล้ว — ล้างค่า");
            _context.HomeArtifactId = null;
            OnContextChanged();
            SendPoints();
            error = "ที่นอนที่ตั้งไว้หายไปแล้ว — ตั้งจุดกลับใหม่ก่อน";
            return false;
        }

        if (_context.ReturningX.HasValue && _context.ReturningY.HasValue)
        {
            tile = new Point2(_context.ReturningX.Value, _context.ReturningY.Value);
            return true;
        }

        // ไม่เคยตั้งอะไรเลย — ใช้จุดเข้าเกาะ ซึ่งเป็นที่เดียวกับตอนฟื้นคืนชีพ
        tile = _world.EntryPoint;
        return true;
    }

    /// <summary>ช่องที่ใกล้ผู้เล่นที่สุดในลิสต์ — ใช้เลือกท่าเรือเมื่อเกาะมีหลายท่า</summary>
    private Point2 NearestTo(List<Point2> candidates)
    {
        Point2 best = candidates[0];
        long bestDistance = long.MaxValue;
        Movement[] movements = _context.AppearPlayer.Move.Movements;
        if (movements == null || movements.Length == 0 ||
            movements[0].Path == null || movements[0].Path.Length == 0)
        {
            return best;   // ไม่รู้ตำแหน่ง — เอาตัวแรกไป ดีกว่าไม่ให้วาร์ปเลย
        }

        WorldPosition pos = movements[0].Path[0].Position;
        int px = (int)(pos.x / 200f);
        int py = (int)(pos.y / 200f);
        foreach (Point2 tile in candidates)
        {
            long dx = tile.x - px;
            long dy = tile.y - py;
            long distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = tile;
        }
        return best;
    }

    /// <summary>
    /// เริ่มวาร์ป — ตอบ <c>Timer</c> ที่ seq ทันที แล้วนัดย้ายตัวเมื่อครบเวลา
    ///
    /// ⚠️ ห้ามส่งอะไรที่ seq นี้อีกหลัง Timer (ดูกับดัก ① ที่หัวไฟล์)
    /// </summary>
    private void BeginWarp(Point2 tile, uint seq, string what)
    {
        // [7 ก.ย. 2026] ตายแล้ววาร์ปไม่ได้ — เดิมกดจากหน้าจอตายแล้วย้ายตัวได้จริง
        if (!_context.AppearPlayer.IsAlive)
        {
            Send(new Abort { Text = "ตอนนี้วาร์ปไม่ได้" }, seq);
            return;
        }
        // เพดานจำนวนคิววาร์ปที่ค้างพร้อมกัน — **ค่าของเรา** กันยิงรัวจนจอง timer ไม่จำกัด
        lock (_warpTimers)
        {
            if (_warpTimers.Count >= MaxConcurrentWarps)
            {
                Send(new Abort { Text = "กำลังวาร์ปอยู่แล้ว" }, seq);
                return;
            }
        }

        float duration = Math.Max(0f, WarpTuning.WarpTime);
        Send(new Messages.Timer { Duration = duration }, seq);

        Console.WriteLine($"[วาร์ป] {Short(EntityId)} {what} → [{tile.x},{tile.y}] (รอ {duration:0.#} วิ)");

        if (duration <= 0f)
        {
            FinishWarp(tile);
            return;
        }

        System.Threading.Timer timer = null;
        timer = new System.Threading.Timer(delegate
        {
            try
            {
                FinishWarp(tile);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[วาร์ป] ย้ายตัวไม่สำเร็จ: {e.Message}");
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

    /// <summary>
    /// ย้ายตัวจริง — ต้องอัปเดตทั้งฝั่งเซิร์ฟและฝั่งเกม **คนละหน่วยกัน**
    /// (เหตุผลเต็มที่กับดัก ③ หัวไฟล์ · ลอกลำดับจาก Player.Combat.cs:729-735 ตอนฟื้นคืนชีพ)
    /// </summary>
    private void FinishWarp(Point2 tile)
    {
        Movement[] movements = _context.AppearPlayer.Move.Movements;
        if (movements != null && movements.Length > 0 &&
            movements[0].Path != null && movements[0].Path.Length > 0)
        {
            movements[0].Path[0].Position = new WorldPosition(tile.x * 200, tile.y * 200);
        }

        // ReplyOf = 0 — ฝั่งเกมรับด้วย global handler (กับดัก ②)
        Send(new Teleported { Tile = tile, Type = TeleportType.Returning });
        OnContextChanged();
    }

    private void ClearWarpTimers()
    {
        lock (_warpTimers)
        {
            foreach (System.Threading.Timer timer in _warpTimers) timer.Dispose();
            _warpTimers.Clear();
        }
    }

    // ── ชุดจุดสำคัญที่ฝั่งเกมใช้เปิด/ปิดปุ่มบนแผนที่ ──────────────────────────────────

    /// <summary>
    /// ส่ง <c>Points</c>(2033) — **ต้อง ReplyOf = 0 เท่านั้น**
    ///
    /// ฝั่งเกมรับด้วย global <c>On&lt;Points&gt;</c> (client/MapSystem.cs:162-165) ไม่ได้ผูก
    /// <c>.On()</c> ไว้กับคำขอไหน — กับดักเดียวกับที่ Player.Map.cs เตือนไว้เรื่อง ExploredPOIs
    ///
    /// <c>ReturningPoint</c> เป็นฟิลด์บังคับ (ไม่ใช่ nullable) ⇒ ต้องมีค่าเสมอ
    /// ไม่งั้นฝั่งเกมอ่านได้ Region ว่างแล้วปุ่มกลับจุดเกิดพาไปที่ผิด
    /// </summary>
    private void SendPoints()
    {
        Region region = CurrentRegion();

        EntityTile? homePoint = null;
        if (!string.IsNullOrEmpty(_context.HomeArtifactId) &&
            _world.ArtifactManager.Get(_context.HomeArtifactId) is { } home)
        {
            MergedBlueprint blueprint = BlueprintStore.GetBlueprint(home.EntityType);
            homePoint = new EntityTile
            {
                Region = region,
                Tile = home.Tile,
                EntityId = home.EntityId,
                // ชื่อที่โชว์ในข้อความ "กลับไปที่ <ชื่อ>" (client/PointsExtension.cs:41-45)
                EntityName = blueprint?.Name?.ToString() ?? string.Empty
            };
        }

        Point2 returning = _context.ReturningX.HasValue && _context.ReturningY.HasValue
            ? new Point2(_context.ReturningX.Value, _context.ReturningY.Value)
            : _world.EntryPoint;

        Send(new Points
        {
            HomePoint = homePoint,
            ReturningPoint = new RegionTile { Region = region, Tile = returning },
            // สามตัวนี้เป็นระบบที่เซิร์ฟยังไม่ทำ — ปล่อย null ฝั่งเกมจะซ่อนปุ่มนั้นไปเอง
            // (client/Durango.UI/WorldMapGroup.cs:190-210 เช็ค HasValue ก่อนเปิดปุ่ม)
            DeathPoint = null,        // จุดที่ตายล่าสุด — ต้องมีระบบเก็บศพก่อน
            LastReturnPoint = null,   // ปุ่ม "กลับที่เดิม" หลังวาร์ป
            CampPoint = null          // แคมป์ — ยังไม่มีระบบ
        });
    }

    /// <summary>เกาะที่ผู้เล่นยืนอยู่ตอนนี้ในรูปแบบที่โปรโตคอลต้องการ</summary>
    private Region CurrentRegion()
    {
        if (RegionCatalog.TryGet(_world.TerrainId, out Region region)) return region;
        // เกาะที่ generate เองอาจไม่มีในสารบบ — ประกอบเท่าที่รู้ ดีกว่าส่ง Region ว่าง
        return new Region { Id = _world.TerrainId, TerrainId = _world.TerrainId };
    }
}
