using System;
using System.Collections.Generic;
using System.Globalization;
using Durango.Network;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  ระบบ "ค้นหาจุดสำคัญ" (expedition/search POI) — SearchPOIs + GetLastSearchedTime
//
//  ═══ ทำไมต้องมีไฟล์นี้ ═══
//  เดิมเซิร์ฟไม่มี handler ทั้งสองตัว (ล็อก "ไม่มี handler" ทั้งคู่) ⇒ ผลในเกม:
//    · กดปุ่มค้นหา (แรดาร์หารูวาร์ป) — เกมส่ง SearchPOIs(904) ไปแล้ว "เงียบ"
//      ตอนนี้ unhandled-packet path ของ Connection ตอบ Abort กลับทุก seq อยู่แล้ว
//      (GameCode/Durango.Online/Connection.cs:433-436) แต่ผู้เล่นก็ยังไม่ได้แผนที่เรดาร์
//    · ทุกครั้งที่เข้าฉากหลัก เกมขอเวลาค้นหาล่าสุด (GetLastSearchedTime 906) เพื่อวาด
//      หลอดคูลดาวน์ของปุ่ม — ไม่มีตัวตอบ = สถานะปุ่มผิดตั้งแต่เข้าเกม
//
//  ═══ ลำดับที่ฝั่งเกมใช้จริง (ไล่จากซอร์ส client ทั้งหมด) ═══
//    1. ปุ่ม "ค้นหา" โผล่ในเมนูบริบทเฉพาะเกาะออนไลน์ที่ไม่ใช่ทดสอบ/ส่วนตัว
//       (client/InteractionSystem.cs:728-734 DefaultContextActionFinder เพิ่ม
//       Interaction.SearchWarphole = 10253 — client/InteractionData/Interaction.cs:526)
//    2. กดปุ่ม → InteractionGroup.SearchWarpholes (client/Durango.UI/InteractionGroup.cs:404,435-446)
//       → InteractionSystem.SearchWarpholes (client/InteractionSystem.cs:808-818):
//       ส่ง SearchPOIs(904) แบบไม่มีฟิลด์ แล้ว **รอ SearchedPOIs(905) กลับที่ seq เดิม**
//       (.On ผูกกับ seq — client/Durango.Network/ReplyMessageHandlerRegistrar.cs:19-26)
//    3. ได้คำตอบ → เล่นท่า "Warp_Find" แล้วเปิดแผนที่เรดาร์ DetectWarpHoleUI.ShowScanner(msg.Results)
//       (client/Durango.UI.InGame/DetectWarpHoleUI.cs:43-47) — ลูกศรแต่ละดวงอ่าน
//       Result.Tile (จุด) กับ Result.Type (สี) จาก DetectWarpHoleScanner.SetSearchResults
//       (client/Durango.UI.InGame/DetectWarpHoleScanner.cs:102-116) ⇒ **สองฟิลด์นี้ต้องมีเสมอ**
//    4. นอกจากนี้ทุกครั้งที่ฉากหลักพร้อม เกมขอ GetLastSearchedTime(906)
//       (client/InteractionSystem.cs:160,182-188 OnReady → AddOnReady) แล้วเอา
//       LastSearchedTime.SearchedAt ไปตั้ง _warpholeSearchedAt ตรง ๆ ⇒ ตอบผิดชนิด/ไม่ตอบ
//       = หลอดคูลดาวน์เริ่มเกมเสมอเป็น "ว่าง"
//
//  ═══ ทำไมคูลดาวน์ต้องบังคับ "ฝั่งเซิร์ฟ" ═══
//  ปุ่มค้นหาของเกม **ไม่ได้กันการกดซ้ำ** — OnClick ยิงต่อให้ทุกครั้งที่ปุ่มแสดงอยู่
//  (client/Durango.UI/ContextActionButtonBase.cs:202-212 — State.Cooltime เป็นแค่ภาพ
//  fillAmount ของสปรไต์, 164-167 SetState ไม่มีเงื่อนไขครอบ OnClick) ⇒ ถ้าเซิร์ฟไม่เช็ค
//  ผู้เล่นกดรัวได้ทุกเฟรม แล้วต้องเป็นเซิร์ฟเองที่ตัดสิน — และมันมี "ทางบอกผู้เล่น" อยู่แล้ว:
//  ตอบ Abort ตาม seq จะตกไปตัวจัดการ global On<Abort> ของเกม (client/GameManager.cs:269)
//  → DefaultAbortHandler โชว์ SystemMsg ข้อความเรา (client/GameManager.cs:309-312) ·
//  การไหลของ reply ที่ผูก seq แต่ไม่มี handler ตรงชนิดแล้วไป global =
//  client/Durango.Network/Connection.cs:868-908 (HandleMsg)
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    private void RegisterExplorationHandlers()
    {
        // ค้นหาจุดสำคัญ — client รอ SearchedPOIs(905) ที่ seq เดิมเท่านั้น
        // (client/InteractionSystem.cs:810 .On(delegate(SearchedPOIs ...)))
        _connection.Recv(delegate(SearchPOIs msg, PacketHeader header)
        {
            HandleSearchPOIs(header.Seq);
        });

        // เวลาค้นหาล่าสุด — client ยิงทุกครั้งที่เข้าฉากหลัก (OnReady)
        // (client/InteractionSystem.cs:182-188) และรอ LastSearchedTime(907) กลับที่ seq เดิม
        // SearchedAt เป็น 0.0 ได้ = "ไม่เคยค้น" — ฝั่งเกมเอาไปบวกคูลดาวน์แล้วได้ until=0
        // ซึ่ง ContextActionButtonsBase.SetActionCooltime ตีความว่า "ไม่มีคูลดาวน์"
        // (client/Durango.UI/ContextActionButtonsBase.cs:192-217)
        _connection.Recv(delegate(GetLastSearchedTime msg, PacketHeader header)
        {
            Send(new LastSearchedTime { SearchedAt = SearchStore.Get(EntityId) }, header.Seq);
        });
    }

    private void HandleSearchPOIs(uint seq)
    {
        // เวลาต้องเป็น "เวลาเซิร์ฟ" (unix วินาที) เพราะฝั่งเกมเอา SearchedAt ไปเทียบกับ
        // Connections.Frontend.GetPredictedServerTime() — นาฬิกาที่ซิงก์จากเซิร์ฟ
        // (client/Durango.UI/ContextActionButtonsBase.cs:172,194-196 ·
        //  ซิงก์ผ่าน ServerTime: GameServer.cs:243) — ใช้เวลาเครื่อง client/เซิร์ฟปนกัน = หลอดเดินเอง
        double now = Times.UnixTimeNow();
        ExplorerTuning tuning = ExplorerTuningOf();

        // ── คูลดาวน์ — ค่าจาก constants.json → explorer → search_cooltime (ไฟล์จริง = 10 วินาที) ──
        // ฝั่งเกมแค่ "วาด" หลอดจาก SearchedAt + Derived.PoiSearchingCooldownTime
        // (client/Durango.UI/ContextActionGroupBase.cs:133-137) แต่ไม่เคยกันการส่ง ⇒ บังคับที่นี่
        double last = SearchStore.Get(EntityId);
        if (last > 0.0 && now - last < tuning.SearchCooltime)
        {
            int remain = Math.Max(1, (int)Math.Ceiling(tuning.SearchCooltime - (now - last)));
            Send(new Abort { Text = $"กำลังสำรวจอยู่ — ค้นหาได้อีกครั้งใน {remain} วินาที" }, seq);
            return;
        }

        List<SearchResult> results = CollectSearchResults(tuning);

        // จับเวลาก่อนตอบ — ไม่ว่าผลจะว่างหรือไม่ ก็ถือว่า "ค้นหา 1 ครั้ง" (เกมเล่นอนิเมชัน
        // Warp_Find ทุกครั้งที่ได้คำตอบ: client/Durango.UI/InteractionGroup.cs:439-443)
        SearchStore.Set(EntityId, now);

        // ── แรงที่ใช้ — constants.json → explorer → search_energy (ไฟล์จริง = "5") ──
        // ตัดแบบเดียวกับการเก็บเกี่ยว (_survival.Add + FlushSurvival — Player.Gathering.cs:239-241)
        // ไม่มีการ "เตือนแรงไม่พอ" เหมือนเก็บเกี่ยว เพราะ SearchWarpholes ไม่ได้ผูก .On(EnergyWarning)
        // (client/InteractionSystem.cs:808-818 ผูกแค่ SearchedPOIs) — ส่งไปก็ตกใบ้ ๆ ไม่มีใครอ่าน
        // ⚠️ เกมแท้ตัดแรงก่อนตอบหรือไม่ ยืนยันไม่ได้จากซอร์ส client — ต้องเทสในเกม (ดูรายงาน)
        if (tuning.SearchEnergy > 0f)
        {
            _survival.Add(SurvivalState.KeyEnergy, -tuning.SearchEnergy);
            FlushSurvival();
        }

        Send(new SearchedPOIs { Results = results.ToArray(), SearchedAt = now }, seq);

        Console.WriteLine($"[สำรวจ] {Short(EntityId)} ค้นหาได้ {results.Count} จุด " +
                          $"บน {_world.TerrainId} (คูลดาวน์ {tuning.SearchCooltime}s)");
    }

    // ── เลือกจุดที่ "ค้นเจอ" ────────────────────────────────────────────────────────

    /// <summary>
    /// ผลค้นหา = รูวาร์ปกลาง + แท่งเร่งวาร์ปบนเกาะปัจจุบัน เรียงใกล้ → ไกล แล้วตัดตาม
    /// <c>explorer.number_to_result</c> (ไฟล์จริง = 3)
    ///
    /// ทำไมชนิดนี้: ปุ่มที่ยิง SearchPOIs คือ Interaction.SearchWarphole และผลไปโชว์ใน
    /// DetectWarpHoleUI เรดาร์หารูวาร์ป (client/Durango.UI/InteractionGroup.cs:435-446 ·
    /// client/Durango.UI.InGame/DetectWarpHoleUI.cs:43-47) — จุดที่วาดมาจาก pois.yml
    /// ชุดเดียวกับที่ World วาง prop จริง (World.PlaceTerrainPois) ⇒ เรดาร์ชี้ได้ตรงของจริง
    ///
    /// ระยะ: คำนวณหน่วย world (ช่อง × 200 — client/Durango.Terrain/Util.cs:92-96
    /// TilePositionToWorldPosition = tile*200) เหมือน Player.Warp.FindNearestPortTile ·
    /// รูวาร์ปไม่กรองระยะ (ไฟล์ constants ไม่มีคีย์กำหนด) ส่วนแท่งเร่งวาร์ปกรองด้วย
    /// <c>explorer.rift_search_range</c> (ไฟล์จริง = 50000 units ≈ 250 ช่อง ≈ เกาะทั้งลูก)
    /// </summary>
    private List<SearchResult> CollectSearchResults(ExplorerTuning tuning)
    {
        var results = new List<SearchResult>();
        TerrainPois pois = LoadPois(null);
        if (pois == null)
        {
            return results;
        }

        // ตำแหน่งผู้เล่นตอนนี้ (world units) — helper เดียวกับที่สัตว์เลี้ยงใช้ตอนเรียกออกมา
        WorldPosition pos = PlayerPosition();
        long rangeSq = (long)tuning.RiftSearchRange * (long)tuning.RiftSearchRange;

        var candidates = new List<(long DistSq, SearchResult Item)>();

        foreach (Point2 tile in pois.Warpholes)
        {
            long distSq = DistSqToTile(pos, tile);
            candidates.Add((distSq, new SearchResult
            {
                Tile = tile,
                Type = Shared.System.PointOfInterest.Warphole
            }));
        }

        foreach (Point2 tile in pois.Rifts)
        {
            long distSq = DistSqToTile(pos, tile);
            if (distSq > rangeSq) continue;
            candidates.Add((distSq, new SearchResult
            {
                Tile = tile,
                Type = Shared.System.PointOfInterest.Rift
            }));
        }

        // ฝั่งเกมจัดเรียงเองอีกรอบตอนวาดลูกศร (DetectWarpHoleScanner.SetSearchResults
        // client/Durango.UI.InGame/DetectWarpHoleScanner.cs:102-105) — เราเรียงให้เพื่อให้
        // การตัดทิ้งตาม number_to_result เหลือ "จุดที่ใกล้ที่สุด" เสมอ ไม่ใช่อันที่ยอดไฟล์มาก่อน
        candidates.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
        for (int i = 0; i < candidates.Count && results.Count < tuning.NumberToResult; i++)
        {
            results.Add(candidates[i].Item);
        }
        return results;
    }

    /// <summary>ระยะ² จากผู้เล่นไปช่อง POI — หน่วย world (ช่อง × 200) ไม่ต้องถอดราก</summary>
    private static long DistSqToTile(WorldPosition pos, Point2 tile)
    {
        long dx = (long)(tile.x * 200 - pos.x);
        long dy = (long)(tile.y * 200 - pos.y);
        return dx * dx + dy * dy;
    }

    // ── ค่าปรับจากไฟล์เกม ───────────────────────────────────────────────────────────

    private static ExplorerTuning _explorerTuning;
    private static bool _explorerTuningLoaded;

    /// <summary>
    /// ค่าระบบค้นหาทั้งหมดอยู่ที่ data/assets/constants.json → explorer — ไม่มีเลขไหนสมมุติเอง
    ///
    /// ฝั่งเกม deserialize บล็อกนี้แบบ Required.Always (client/Yaml/Explorer.cs:5-9) แต่
    /// **ไม่มีจุดไหนในโค้ดเกมอ่านมันใช้** ⇒ เป็นค่า "ฝั่งเซิร์ฟ" ของแท้ (เซิร์ฟเดิมเป็นคนบังคับ
    /// คูลดาวน์/คิดผล) จึงโหลดที่นี่เท่านั้น — พอร์ตเพิ่มใน Support/YamlConstants ภายหลังได้
    /// ถ้ามีระบบอื่นต้องใช้ร่วม (ตอนนี้ใช้ไฟล์เดียวกับ Player.Domestication.LoadConstants)
    /// </summary>
    private static ExplorerTuning ExplorerTuningOf()
    {
        if (_explorerTuningLoaded) return _explorerTuning;
        _explorerTuningLoaded = true;

        // ค่าสำรอง = ค่าในไฟล์เกมจริงที่จัดส่งมากับโปรเจกต์ (เหมือน WarpTime ของ Player.Warp.cs:157-159)
        var tuning = new ExplorerTuning
        {
            SearchCooltime = 10,
            SearchEnergy = 5f,
            NumberToResult = 3,
            RiftSearchRange = 50000
        };

        try
        {
            JObject root = Json.ReadFromFile<JObject>("constants");
            if (root?["explorer"] is JObject ex)
            {
                if ((int?)ex["search_cooltime"] is int cooltime) tuning.SearchCooltime = cooltime;
                // ไฟล์เก็บเป็นข้อความ ("5") — แปลงแบบ InvariantCulture เหมือน Player.Domestication.cs:1213-1217
                if (ex["search_energy"] != null &&
                    double.TryParse((string)ex["search_energy"], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double energy))
                {
                    tuning.SearchEnergy = (float)energy;
                }
                if ((int?)ex["number_to_result"] is int number) tuning.NumberToResult = number;
                if ((int?)ex["rift_search_range"] is int range) tuning.RiftSearchRange = range;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[สำรวจ] ⚠️ อ่าน constants.json → explorer ไม่ได้ ใช้ค่าจากไฟล์จริงแทน: {e.Message}");
        }

        _explorerTuning = tuning;
        return tuning;
    }

    /// <summary>ค่าปรับระบบค้นหาจาก constants.json → explorer (โครงเดียวกับไฟล์เกม)</summary>
    private sealed class ExplorerTuning
    {
        /// <summary>search_cooltime — เวลา (วินาที) ที่ค้นหาซ้ำไม่ได้</summary>
        public double SearchCooltime;

        /// <summary>search_energy — แรงที่หายไปต่อการค้นหา 1 ครั้ง</summary>
        public float SearchEnergy;

        /// <summary>number_to_result — จำนวนจุดสูงสุดที่คืนต่อครั้ง</summary>
        public int NumberToResult;

        /// <summary>rift_search_range — รัศมีค้นหาแท่งเร่งวาร์ป (world units)</summary>
        public long RiftSearchRange;
    }

    // ══════════════════════════════════════════════════════════════════════════════════
    //  ที่เก็บ "เวลาค้นหาล่าสุด" (ใช้ร่วมกันทุกผู้เล่นในโปรเซสเดียว)
    // ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// เวลาค้นหาล่าสุดของผู้เล่นแต่ละคน (unix วินาที — หน่วยเดียวกับ Times.UnixTimeNow)
    ///
    /// ทำไมเป็น static store ไม่ใช่ฟิลด์ใน PlayerContext: ตอนนี้เก็บเพื่อ **คูลดาวน์ 10 วินาที**
    /// เท่านั้น — คุณค่าของข้อมูลสั้นกว่ารอบเซฟผู้เล่น และ PlayerContext ถูกเขียนทับทุกครั้งที่
    /// เซฟ (SafeSave) การเพิ่มฟิลด์ต้องระวังรอบโหลดไฟล์เก่า ⇒ เก็บแบบ PetStore ไว้ก่อน
    /// (Player.Animals.cs:1376 — "เปิดเป็น public เพื่อให้เสียบ persistence ทีหลังได้")
    ///
    /// ⚠️ ไม่ต้องล็อกเธรด เพราะ handler ทั้งหมดถูกเรียกจาก main loop เส้นเดียว
    /// (หลักการเดียวกับ PetStore/Player.WarehouseStore)
    ///
    /// ถ้าจะ persist ต่อ: ใส่ <c>SearchedAt</c> ลง PlayerContext (ข้าง ExploredPOIs) แล้วให้
    /// PlayerContext โหลดคืนมาเรียก SearchStore.Set ตอนสร้าง Player — ต้องแก้ 2 จุด:
    /// Core/PlayerContext.cs (ฟิลด์ + serialize) และจุดที่ Host โหลดผู้เล่น (Host.cs)
    /// </summary>
    public static class SearchStore
    {
        private static readonly Dictionary<string, double> SearchedAtByPlayer = new(StringComparer.Ordinal);

        /// <summary>0.0 = ไม่เคยค้นหา (ฝั่งเกมตีความเป็น "ไม่มีคูลดาวน์" — ContextActionButtonsBase.cs:196)</summary>
        public static double Get(string entityId) =>
            SearchedAtByPlayer.TryGetValue(entityId ?? string.Empty, out double at) ? at : 0.0;

        public static void Set(string entityId, double searchedAt)
        {
            if (string.IsNullOrEmpty(entityId)) return;
            SearchedAtByPlayer[entityId] = searchedAt;
        }
    }
}
