using System;
using System.Collections.Generic;
using Durango.Network;
using Messages;
using Newtonsoft.Json;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  แผนที่ + จุดสำคัญ (POI)
//
//  ═══ ทำไมแผนที่ถึงว่างเปล่ามาตลอด ═══
//  เกมของ NEXON เป็นฝ่าย "เจอเอง" แล้วบอกเซิร์ฟ ไม่ใช่เซิร์ฟป้อนหมุดมาให้:
//    1. เดินเข้าใกล้ prop → client/Durango.Logic.Map/POIUpdater.cs:148-159 ดู BlueprintId
//       (dock → Port · neutral_warphole → CargoWarphole · warp_accelerator → Rift)
//       และบรรทัด 122 ดู ArtifactState.Crack.HasValue → Crack (หลุมอุกกาบาต)
//    2. ระยะ ≤ 500 → ยิง ExplorePOI(908) มาบอกเซิร์ฟ
//    3. **รอ .On<OK>** แล้วค่อยขอ GetExploredPOIs(902) ใหม่ (POIUpdater.cs:211-225)
//    4. เซิร์ฟตอบ ExploredPOIs(903) → MapSystem.DoExploredPOIs วาดหมุดลงแผนที่
//
//  ⚠️ เดิมเซิร์ฟ **ไม่มี handler ExplorePOI เลย** ⇒ ไม่มี OK ⇒ ขั้น 3 ไม่เคยเกิด
//     และ GetExploredPOIs ก็ตอบลิสต์ว่างตายตัว ⇒ **แผนที่ไม่มีหมุดเลยสักอันตลอดเกม**
//     ไม่มี error ให้เห็น เพราะฝั่งเกมแค่วนลูป POIs ที่ว่างเปล่า
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    private void RegisterMapHandlers()
    {
        // จำนวนจุดสำคัญของเกาะ — client/Durango.UI.Popup/RouteInfoTooltip.cs:78-79
        // ⚠️ Tooltip.Show() ถูกเรียกจาก callback ของตัวนี้เท่านั้น (RouteInfoTooltip.cs:147-152)
        // ไม่ตอบ = tooltip ไม่โผล่ = ไม่มีปุ่ม "ออกเรือ" ให้กด = เดินทางไม่ได้เลย โดยไม่มี error
        _connection.Recv(delegate(GetPOICount msg, PacketHeader header)
        {
            // นับจากไฟล์ terrain ตรง ๆ ไม่ต้องเปิดโลกของเกาะนั้น (เปลืองหน่วยความจำโดยใช่เหตุ
            // เพราะ tooltip แค่ขอตัวเลขไปโชว์) — pois.yml คือแหล่งเดียวกับที่ World ใช้วางจริง
            TerrainPois pois = LoadPois(msg.RegionId);
            Send(new POICount
            {
                PortCount = (byte)(pois?.PortPoints.Count ?? 0),
                WarpholeCount = (byte)(pois?.Warpholes.Count ?? 0),
                RiftCount = (byte)(pois?.Rifts.Count ?? 0),
                // เดิมตายตัวเป็น 0 เพราะตัวอ่าน pois.yml ยัด craters รวมกับ rifts
                // ⇒ ตัวนับหลุมอุกกาบาตในเกม (POIUpdater.EntireCraterCount) เป็นศูนย์เสมอ
                CraterCount = (byte)(pois?.Craters.Count ?? 0)
            }, header.Seq);
        });

        _connection.Recv(delegate(GetExploredPOIs msg, PacketHeader header)
        {
            // ⚠️ ต้องตอบแบบ ReplyOf ตรง seq เท่านั้น — ถ้าส่ง ReplyOf=0 จะตกไป global handler
            // (client/MapSystem.cs:166,247-256) แล้วไปวาด indicator ของเกาะปลายทางทับแผนที่เกาะปัจจุบัน
            SendExploredPOIs(RegionKey(msg.RegionId), header.Seq);
        });

        _connection.Recv(delegate(ExplorePOI msg, PacketHeader header)
        {
            HandleExplorePOIMsg(msg, header.Seq);
        });

        // หมอกบนแผนที่ — ตอนเข้าเกมเราส่ง DefoggedChunks ให้แล้ว (Player.SendDefoggedChunks
        // เหมือนต้นฉบับ client/Durango.Online/Player.cs:498) แต่ฝั่งเกมยังขอซ้ำทุกครั้งที่
        // หน้าต่างแผนที่ถูกสร้างใหม่ (client/Durango.UI/MapContext.cs:257 ใน AddOnReady)
        // ⇒ หลังต่อใหม่/เปลี่ยนเกาะ ถ้าไม่ตอบ แผนที่จะถูกหมอกบังทั้งผืน
        // ⚠️ ตอบแบบ ReplyOf = 0 เพราะฝั่งเกมรับด้วย global On<DefoggedChunks> (MapContext.cs:169)
        //    ไม่ได้ผูก .On() ไว้กับคำขอ
        _connection.Recv(delegate(GetDefoggedChunks msg, PacketHeader header)
        {
            SendDefoggedChunks();
        });

        // ลูกศรชี้จุดสำคัญที่ใกล้ที่สุด — client/Durango.UI/PlayGuideHelperGroupBase.cs:330
        // ไม่ตอบ = บทไกด์สั่งให้ "ไปหาท่าเรือ" แล้วไม่มีลูกศรชี้ทางให้เลย
        _connection.Recv(delegate(RequestNearestPOI msg, PacketHeader header)
        {
            Send(new NearestPOI { Type = msg.Type, Tile = FindNearestPOI(msg.Type, msg.Tile) }, header.Seq);
        });
    }

    // ── จุดที่สำรวจแล้ว ─────────────────────────────────────────────────────────────

    /// <summary>เกาะที่คำขออ้างถึง — ว่างแปลว่า "เกาะที่ยืนอยู่ตอนนี้"</summary>
    private string RegionKey(string regionId) =>
        string.IsNullOrEmpty(regionId) ? _world.TerrainId : regionId;

    private void HandleExplorePOIMsg(ExplorePOI msg, uint seq)
    {
        Dictionary<string, ExploredPoint> found = _context.ExploredPOIs ??= new Dictionary<string, ExploredPoint>();
        string key = $"{_world.TerrainId}|{msg.Tile.x},{msg.Tile.y}";

        // ⚠️ ต้องตอบ OK เสมอ แม้จุดนี้เคยเจอแล้ว — ฝั่งเกมยิงซ้ำทุกครั้งที่หลุมอุกกาบาต
        // เปลี่ยนสถานะเปิด/ปิด (POIUpdater.NearbyCrackFound บรรทัด 176-186) แล้วรอ OK
        // เพื่อขอรายการใหม่ · ไม่ตอบ = หมุดค้างสถานะเก่าถาวร
        bool isNew = !found.ContainsKey(key);
        found[key] = new ExploredPoint
        {
            RegionId = _world.TerrainId,
            X = msg.Tile.x,
            Y = msg.Tile.y,
            Type = (int)msg.Type,
            EntityType = msg.EntityType
        };
        Send(new OK(), seq);

        if (isNew)
        {
            Console.WriteLine($"[แผนที่] {EntityId[..Math.Min(8, EntityId.Length)]} พบ {msg.Type} " +
                              $"ที่ [{msg.Tile.x},{msg.Tile.y}] บน {_world.TerrainId}");
            OnContextChanged();
        }
    }

    private void SendExploredPOIs(string regionId, uint seq)
    {
        var list = new List<PointOfInterest>();
        if (_context.ExploredPOIs != null)
        {
            foreach (ExploredPoint point in _context.ExploredPOIs.Values)
            {
                if (point.RegionId != regionId) continue;
                list.Add(ToMessage(point));
            }
        }

        Send(new ExploredPOIs
        {
            POIs = list.ToArray(),
            FullCountRewarded = false,
            IsOpenedMap = false
        }, seq);
    }

    /// <summary>
    /// แปลงจุดที่จำไว้ให้เป็นหมุดบนแผนที่
    ///
    /// <c>Icon</c>/<c>Title</c> ฝั่งเกมไม่ได้ส่งมาให้ตอน ExplorePOI (client ใส่แค่ Tile/Type/EntityType)
    /// **เซิร์ฟเป็นคนเติม** และมันจำเป็นเฉพาะหลุมอุกกาบาต/รอยร้าว เพราะ
    /// <c>MapSystem.AddCraterOrCrackIndicator</c> (บรรทัด 310-317) ใช้ <c>poi.Icon</c> ตรง ๆ
    /// **ไอคอนว่าง = หมุดไม่มีรูป** ส่วนท่าเรือ/รูวาร์ปฝั่งเกมฝังชื่อไอคอนไว้เองแล้ว
    /// (icon_map_port / icon_map_warphole ที่บรรทัด 301,306)
    ///
    /// ชื่อไอคอน <c>icon_map_poi_crack</c> เป็นของเกมเอง ไม่ได้ตั้งขึ้น — ยืนยันจาก
    /// <c>client/Durango.UI/ArtifactInfoMainWidget.cs:615</c> ที่เขียน <c>[icon=icon_map_poi_crack]</c>
    /// </summary>
    private static PointOfInterest ToMessage(ExploredPoint point)
    {
        var type = (Shared.System.PointOfInterest)point.Type;
        return new PointOfInterest
        {
            Tile = new Point2(point.X, point.Y),
            Type = type,
            Icon = type is Shared.System.PointOfInterest.Crater or Shared.System.PointOfInterest.Crack
                ? "icon_map_poi_crack"
                : null,
            Title = null,   // tooltip เท่านั้น (MapSystem.cs:370-373 ข้ามไปถ้าว่าง)
            EntityType = point.EntityType,
            IsExplored = true
        };
    }

    // ── จุดสำคัญที่ใกล้ที่สุด ────────────────────────────────────────────────────────

    /// <summary>
    /// หาจุดสำคัญชนิดที่ขอที่ใกล้ช่องนั้นที่สุดบนเกาะปัจจุบัน — null ถ้าเกาะนี้ไม่มีชนิดนั้นเลย
    /// (<c>NearestPOI.Tile</c> เป็น nullable อยู่แล้ว ฝั่งเกมรับ null ได้)
    /// </summary>
    private Point2? FindNearestPOI(Shared.System.PointOfInterest type, Point2 from)
    {
        TerrainPois pois = LoadPois(null);
        if (pois == null) return null;

        List<Point2> candidates = type switch
        {
            Shared.System.PointOfInterest.Port => pois.PortPoints,
            Shared.System.PointOfInterest.Warphole or
                Shared.System.PointOfInterest.CargoWarphole => pois.Warpholes,
            Shared.System.PointOfInterest.Rift => pois.Rifts,
            Shared.System.PointOfInterest.Crater or
                Shared.System.PointOfInterest.Crack => pois.Craters,
            _ => null
        };
        if (candidates == null || candidates.Count == 0) return null;

        Point2 best = candidates[0];
        long bestDistance = long.MaxValue;
        foreach (Point2 tile in candidates)
        {
            long dx = tile.x - from.x;
            long dy = tile.y - from.y;
            long distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = tile;
        }
        return best;
    }

    private TerrainPois LoadPois(string regionId)
    {
        try
        {
            return TerrainLoader.Load(RegionKey(regionId))?.Pois;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[แผนที่] อ่าน POI ของ {regionId} ไม่ได้: {e.Message}");
            return null;
        }
    }
}

/// <summary>
/// จุดสำคัญหนึ่งจุดที่ผู้เล่นคนนี้เคยเดินไปเจอ — เก็บลงไฟล์ผู้เล่น
///
/// ทำไมเก็บกับผู้เล่นไม่ใช่กับโลก: แผนที่ในเกมเป็น "สิ่งที่ฉันสำรวจแล้ว" ของแต่ละคน
/// (client/POIUpdater.cs เก็บ <c>_exploreredPOIs</c> ต่อเครื่อง แล้วเคลียร์ทุกครั้งที่ Init)
/// ⇒ ผู้เล่นใหม่ต้องออกเดินหาเอง ไม่ใช่ได้หมุดครบตั้งแต่เข้าเกม
///
/// เก็บเป็นชนิดของเราเองไม่ใช่ <c>Messages.PointOfInterest</c> ตรง ๆ เพราะ struct ของโปรโตคอล
/// มี <c>Point2</c> กับ enum ที่ Newtonsoft อ่านกลับมาไม่ตรงชนิด แล้วจะเจอกับดักเดียวกับ
/// <c>Item.Ext</c> (ดู ItemExtRepair) — เก็บเป็นตัวเลขล้วนปลอดภัยกว่า
/// </summary>
public class ExploredPoint
{
    [JsonProperty("region")] public string RegionId;
    [JsonProperty("x")] public int X;
    [JsonProperty("y")] public int Y;

    /// <summary>ค่าของ <c>Shared.System.PointOfInterest</c> (Port=0 Warphole=1 Crater=2 …)</summary>
    [JsonProperty("type")] public int Type;

    [JsonProperty("entity_type")] public ushort? EntityType;
}
