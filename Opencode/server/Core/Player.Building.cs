using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Network;
using Durango.Utils;
using Messages;
using Shared.Building;
using Shared.Etc;
using Yaml;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
// ระบบสร้างสิ่งปลูกสร้าง — [6 ก.ย. 2026]
//
// ก่อนมีไฟล์นี้: ผู้เล่น**รื้อได้แต่สร้างไม่ได้** สร้างของใหม่ได้ทางเดียวคือคำสั่ง cheat "prop"
// ซึ่งปิดไว้ให้เฉพาะแอดมิน ⇒ เกมขาดครึ่งหนึ่งของแกนหลัก (Durango คือเกมสร้างฐาน)
//
// ═══ ลำดับที่ฝั่งเกมเดินจริง (client/BuildSystem.cs + BuildSlotContainer.cs) ═══
//
//   1. เลือกแบบแปลนแล้วลากกรอบลงพื้น
//      client → OccupyArtifactSite(2057) {BlueprintId, ItemId, Tile, Floor, Size, Stories, Rotation, ModularEntityId}
//      server → Timer(1134) ตั้งเวลาหลอด "จองพื้นที่" แล้ว Occupied(301) เมื่อครบเวลา
//               (client/BuildSystem.cs:539-556 SendOccupyArtifactSite)
//               ⚠️ ทั้งคู่ต้องตอบที่ seq เดียวกัน และต้องครอบด้วย ReplySequenceMark
//                  ไม่งั้น client ลบ handler ทิ้งตั้งแต่ได้ Timer (เหตุผลเต็มที่ Player.Crafting.cs)
//
//   2. แตะหลังที่จองไว้ → เมนู "건설" (Interaction.BuildArtifact = 101)
//      client → GetArtifact(2018) {EntityId, Tile}
//      server → ArtifactMaterials(2091) {EntityId, Materials{slotId → Item[]}}
//               = ของที่ใส่ค้างไว้แล้ว (client/BuildSystem.cs:366-388 RequestArtifactMaterials)
//
//   3. ใส่วัสดุลงช่อง (ทำหลายรอบได้ ไม่ต้องครบในครั้งเดียว)
//      client → PutMaterialsIntoArtifact(2092) {EntityId, Tile, Materials{slotId → itemId[]}}
//      server → OK(1231)   (client/BuildSystem.cs:199-213 PutMaterials)
//
//   4. หน้าต่างแสดง "ผลที่คาดว่าจะได้" ระหว่างเลือกของ
//      client → EstimateBuild(2414) {EntityId, Tile, ToolId, Materials}
//      server → BuildEstimation(2415) {Level, Durability, Tags, UnrevealedRareTagCount, ArtifactPreview}
//
//   5. กดปุ่มสร้าง
//      client → BuildArtifact(2090) {EntityId, Tile, ToolItemId}
//      server → Timer(1134) แล้วเปลี่ยนสถานะเป็น Built + กระจาย ArtifactBuilt(2093)
//
//   6. รอ "มาร์มูรี" (postprocess) ครบแล้วแตะ → เมนู "완성" (Interaction.CompleteArtifact = 110)
//      client → CompleteArtifact(2094) {EntityId, Tile}
//      server → เปลี่ยนเป็น Completed + กระจาย ArtifactCompleted(2095)
//
// ═══ สิ่งที่ยังไม่ทำในรอบนี้ (ตั้งใจ ไม่ใช่ลืม) ═══
//   • EnergyWarning(3648) — ฝั่งเกมจะเด้งกล่องถามแล้วตอบ Confirm กลับมาที่ seq เดิม
//     ระบบคราฟต์ก็ยังไม่ทำเหมือนกัน ⇒ ทำทีเดียวทั้งสองที่ดีกว่าทำครึ่งเดียวตรงนี้
//   • ความทนทานที่ลดลงตามเวลา — ทั้งเซิร์ฟใช้หลอดคงที่ 1.0 อยู่แล้ว (Cheats.MakeAppearArtifact)
//     ของจริงหลังที่จองไว้แล้วไม่สร้างจะหายพร้อมวัสดุ **จงใจไม่ทำ** เพราะเบต้าไม่ควรกินของผู้เล่น
//   • HelpPostprocess(112) คนอื่นมาช่วยย่นเวลา — ต้องมีแนวคิด "คนอื่นยุ่งกับหลังเราได้"
//     ซึ่งขัดกับด่านเจ้าของที่เพิ่งปิดช่องโหว่ไป (Player.cs MayTouchArtifact)
//   • RemodelArtifact / CapsulateArtifact / PackArtifact — คนละระบบ
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    /// <summary>
    /// นาฬิกาที่รอส่งคำตอบตัวที่สองของชุด (Occupied / ArtifactBuilt)
    ///
    /// เหตุผลที่ใช้ threading timer เหมือนระบบคราฟต์: ดู <see cref="_craftTimers"/>
    /// **ต่างกันตรงที่ระบบนี้แก้สถานะโลกด้วย** ⇒ การแก้โลกทุกอย่างทำให้เสร็จตั้งแต่ตอนรับคำขอ
    /// บนเธรดหลักแล้ว callback จึงเหลือแค่ <c>Send</c> เหมือนกัน (ปลอดภัยข้ามเธรด)
    /// </summary>
    private readonly List<System.Threading.Timer> _buildTimers = new();

    /// <summary>
    /// **ค่าของเรา** — เพดานเวลาที่ยอมหน่วงคำตอบของขั้นตอนก่อสร้าง (วินาที)
    ///
    /// สูตรจองพื้นที่คือ <c>2 + (area * 1)</c> ⇒ บ้าน 10×10 = 102 วินาที ซึ่งยาวเกินไป
    /// สำหรับหลอดที่ผู้เล่นต้องยืนรอ และถ้าข้อมูลเพี้ยนอาจได้ค่ามหาศาล
    /// </summary>
    private const float MaxBuildSeconds = 120f;

    /// <summary>**ค่าของเรา** — ด้านที่ยาวที่สุดของแบบแปลนที่ปรับขนาดได้ (กันการจองทั้งเกาะ)</summary>
    private const int MaxVariableSide = 32;

    /// <summary>**ค่าของเรา** — เพดานจำนวนวัสดุต่อช่อง (กันสูตร size_factor เพี้ยนขอของหลักหมื่น)</summary>
    private const int MaxSlotItems = 200;

    private void RegisterBuildingHandlers()
    {
        _connection.Recv(delegate(OccupyArtifactSite msg, PacketHeader header)
        {
            HandleOccupyArtifactSiteMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(GetArtifact msg, PacketHeader header)
        {
            HandleGetArtifactMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(PutMaterialsIntoArtifact msg, PacketHeader header)
        {
            HandlePutMaterialsIntoArtifactMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(EstimateBuild msg, PacketHeader header)
        {
            HandleEstimateBuildMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(BuildArtifact msg, PacketHeader header)
        {
            HandleBuildArtifactMsg(msg, header.Seq);
        });
        _connection.Recv(delegate(CompleteArtifact msg, PacketHeader header)
        {
            HandleCompleteArtifactMsg(msg, header.Seq);
        });
        _connection.ConnetionClosed += ClearBuildTimers;
    }

    // ── 1. จองพื้นที่ ────────────────────────────────────────────────────────────────

    private void HandleOccupyArtifactSiteMsg(OccupyArtifactSite msg, uint seq)
    {
        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(msg.BlueprintId);
        if (blueprint == null)
        {
            Console.WriteLine($"[สร้าง] {Short(EntityId)} ขอจองพื้นที่ด้วยแบบแปลนที่ไม่รู้จัก: {msg.BlueprintId}");
            Send(new Abort { Text = "ไม่รู้จักแบบแปลนนี้" }, seq);
            return;
        }

        Point2 size = ResolveSize(blueprint, msg.Size, msg.Rotation);
        int area = Math.Max(1, size.x * size.y);

        // ⚠️ ด่านระยะ — เหตุผลเดียวกับ MayTouchArtifact: ฝั่งเกมเดินเข้าไปหาก่อนยิงคำสั่งเสมอ
        // (client/BuildSystem.cs:530 MoveToPosition(..., 141f)) ⇒ ผู้เล่นปกติอยู่ใกล้กว่านี้มาก
        // ไม่มีด่านนี้ = เขียนสคริปต์โรยสิ่งปลูกสร้างทั้งเกาะจากที่เดียวได้
        if (!IsWithinTiles(msg.Tile, ArtifactReachTiles + Math.Max(size.x, size.y)))
        {
            Console.WriteLine($"[สร้าง] ปฏิเสธ {Short(EntityId)}: ช่อง [{msg.Tile.x},{msg.Tile.y}] อยู่ไกลเกินไป");
            Send(new Abort { Text = "อยู่ไกลเกินไป" }, seq);
            return;
        }

        // สูตรทั้งสองมาจาก constants.json → build → site_selection ตรง ๆ (ดู Support/BuildTuning.cs)
        float duration = (float)Math.Clamp(
            BuildTuning.EvalByArea(BuildTuning.SiteDuration, area, 2 + area), 0.0, MaxBuildSeconds);
        float energy = (float)Math.Max(0.0,
            BuildTuning.EvalByArea(BuildTuning.SiteEnergy, area, 1 + area * 2));

        AppearArtifact site = MakeSiteArtifact(blueprint, msg, size);

        // แก้โลกให้เสร็จตรงนี้ บนเธรดหลัก — callback ของนาฬิกาจะได้เหลือแค่ Send
        _world.ConstructArtifact(site, null, EntityId);
        SpendBuildEnergy(energy);

        Console.WriteLine($"[สร้าง] {Short(EntityId)} จองพื้นที่ {blueprint.Id} " +
                          $"ที่ [{site.Tile.x},{site.Tile.y}] {size.x}×{size.y} " +
                          $"(รอ {duration:0.#} วิ · พลังงาน {energy:0.#})");

        var occupied = new Occupied
        {
            EntityId = site.EntityId,
            TileX = site.Tile.x,
            TileY = site.Tile.y,
            Floor = site.Floor
        };

        Send(default(ReplySequenceMark), seq);          // เปิดชุดคำตอบต่อเนื่อง
        Send(new Messages.Timer { Duration = duration }, seq);
        if (duration <= 0f)
        {
            FinishOccupy(occupied, seq);
            return;
        }
        ScheduleBuildReply(() => FinishOccupy(occupied, seq), duration, "จองพื้นที่");
    }

    private void FinishOccupy(Occupied occupied, uint seq)
    {
        Send(occupied, seq);
        Send(default(ReplySequenceMark), seq);          // ปิดชุด — ไม่ปิด handler ฝั่งเกมค้าง
    }

    /// <summary>
    /// ขนาดที่จะกินบนพื้น
    ///
    /// แบบแปลนที่ <c>is_size_variable</c> ผู้เล่นลากกรอบเองได้ (รั้ว/พื้น/กำแพง) ⇒ เชื่อค่าที่ส่งมา
    /// แต่ต้องมีเพดาน ไม่งั้น client ที่ถูกแก้ส่ง 60000×60000 มาจองทั้งเกาะได้ในแพ็กเก็ตเดียว
    /// แบบอื่นบังคับใช้ขนาดจากไฟล์เสมอ
    ///
    /// การสลับด้านตอนหมุน 90°/270° ลอกจาก Cheats.MakeAppearArtifact ซึ่งพอร์ตมาจากต้นฉบับ
    /// </summary>
    private static Point2 ResolveSize(MergedBlueprint blueprint, Point2 requested, Rotation rotation)
    {
        Point2 size = blueprint.Size;
        if (blueprint.IsSizeVariable && requested.x > 0 && requested.y > 0)
        {
            size = new Point2(Math.Min(requested.x, MaxVariableSide), Math.Min(requested.y, MaxVariableSide));
        }
        if (rotation == Rotation.Quarter || rotation == Rotation.ThreeQuarter)
        {
            size = new Point2(size.y, size.x);
        }
        return size;
    }

    /// <summary>
    /// สร้าง <c>AppearArtifact</c> ของ "พื้นที่ที่จองไว้" (ยังไม่ใช่ตัวอาคาร)
    ///
    /// ฟิลด์ที่ขาดไม่ได้เรียนรู้มาจาก Cheats.MakeAppearArtifact — ตัวที่ลืมแล้วเงียบ:
    ///   • States.EntityId  ไม่ตั้ง = ข้อความอัปเดตสถานะถูกทิ้งทั้งหมด
    ///   • States.Level     ไม่ตั้ง = ป้ายชื่อขึ้น "Lv.0"
    ///   • States.MaxHealth ไม่ตั้ง = หลอดเลือดอ่านเป็น 0/0
    ///
    /// <c>Display.Parts</c> ตั้งใจปล่อยว่าง — ตอนสถานะ Occupied ฝั่งเกมวาด "กรอบพื้นที่" เอง
    /// ไม่ได้ใช้โมเดล (client/Artifact.cs:819 MakeGroundSite) โมเดลจะถูกเติมตอนใส่วัสดุ
    /// </summary>
    private AppearArtifact MakeSiteArtifact(MergedBlueprint blueprint, OccupyArtifactSite msg, Point2 size)
    {
        var artifact = new AppearArtifact
        {
            EntityId = Guid.NewGuid().ToString(),
            EntityType = (ushort)blueprint.EntityType,
            IsAlive = true,
            Tile = msg.Tile,
            Size = size,
            Height = blueprint.Height,
            Floor = msg.Floor,
            Stories = msg.Stories,
            Rotation = msg.Rotation,
            FounderEntityId = EntityId,
            ArchitectEntityIds = new[] { EntityId }
        };

        // Modular (บ้านที่ต่อเติมได้) ต้องมีชั้นและตารางของติดผนัง ไม่งั้นฝั่งเกมพังตอนเข้าไปข้างใน
        // (เงื่อนไขเดียวกับ Cheats.MakeAppearArtifact)
        if (blueprint.Components != null && blueprint.Components.Contains("Modular"))
        {
            artifact.Display.AddOns = new Dictionary<int, Pair<string, string>>();
            artifact.Stories ??= 1;
        }

        artifact.Display.EntityId = artifact.EntityId;
        artifact.Display.Condition = Condition.Normal;
        artifact.Display.Parts = new Dictionary<string, string>();

        artifact.States.EntityId = artifact.EntityId;
        artifact.States.BuildingState = BuildingState.Occupied;
        artifact.States.Level = (byte)Math.Clamp(blueprint.MinLevel, 1, 255);
        artifact.States.MaxHealth = Cheats.ArtifactMaxHealth;
        artifact.States.Durability = FullDurability();
        return artifact;
    }

    /// <summary>
    /// หลอดความทนทานแบบคงที่เต็มหลอด
    ///
    /// ทั้งเซิร์ฟใช้แบบนี้เหมือนกันหมด (Cheats.MakeAppearArtifact) — สิ่งปลูกสร้างไม่ผุตามเวลา
    /// ของจริงมีสูตรผุใน constants.json → build → destruct แต่ต้องมีระบบซ่อม/สภาพครบก่อน
    /// ⇒ ยังไม่ทำ ดีกว่าทำครึ่งเดียวแล้วบ้านผู้เล่นหายเอง
    /// </summary>
    private static Gauge FullDurability() =>
        new(1f, 0f, new[] { new GaugeNode { Time = 0.0, Value = 1f } });

    // ── 2. ขอดูวัสดุที่ใส่ไว้แล้ว ──────────────────────────────────────────────────────

    private void HandleGetArtifactMsg(GetArtifact msg, uint seq)
    {
        if (_world.ArtifactManager.Get(msg.EntityId) is not { } artifact)
        {
            Send(new Abort { Text = "ไม่พบสิ่งปลูกสร้างนี้" }, seq);
            return;
        }
        Send(new ArtifactMaterials
        {
            EntityId = artifact.EntityId,
            Materials = WireMaterials(artifact.EntityId)
        }, seq);
    }

    /// <summary>วัสดุที่ใส่ไว้แล้ว แปลงเป็นรูปที่ส่งลงสาย (slotId → Item[])</summary>
    private Dictionary<string, Item[]> WireMaterials(string entityId)
    {
        Dictionary<string, List<Item>> stored = _world.ArtifactManager.GetBuildMaterials(entityId);
        var wire = new Dictionary<string, Item[]>(stored.Count);
        foreach (var pair in stored)
        {
            wire[pair.Key] = pair.Value?.ToArray() ?? Array.Empty<Item>();
        }
        return wire;
    }

    // ── 3. ใส่วัสดุ ─────────────────────────────────────────────────────────────────

    private void HandlePutMaterialsIntoArtifactMsg(PutMaterialsIntoArtifact msg, uint seq)
    {
        if (!TryGetBuildTarget(msg.EntityId, "ใส่วัสดุ", out AppearArtifact artifact,
                               out MergedBlueprint blueprint, out string error))
        {
            Send(new Abort { Text = error }, seq);
            return;
        }

        if (!ResolveBuildMaterials(artifact, blueprint, msg.Materials,
                                   out Dictionary<string, List<Item>> picked, out error))
        {
            Send(new Abort { Text = error }, seq);
            return;
        }

        // ── ตั้งแต่บรรทัดนี้ถือว่าใส่สำเร็จ: หักของ → เก็บเข้าหลัง → ตั้งหน้าตา ──
        var consumed = new List<string>();
        foreach (var pair in picked)
        {
            foreach (Item item in pair.Value) consumed.Add(item.Id);
            _world.ArtifactManager.AddBuildMaterials(artifact.EntityId, pair.Key, pair.Value);

            // หน้าตาของช่องขึ้นกับวัสดุที่เลือกใส่ (blueprints.json → slots[].looks: tag → model_key)
            string model = LookForSlot(blueprint, pair.Key, pair.Value);
            if (model != null) _world.ArtifactManager.SetDisplayPart(artifact.EntityId, pair.Key, model);
        }

        _context.InventoryItems.RemoveAll(item => consumed.Contains(item.Id));
        Send(new InventoryUpdated { EntityId = EntityId, RemovedItemIds = consumed.ToArray() });
        OnContextChanged();
        _world.Save();

        Console.WriteLine($"[สร้าง] {Short(EntityId)} ใส่วัสดุ {consumed.Count} ชิ้นลง {blueprint.Id}");
        Send(default(OK), seq);

        // ส่งสถานะวัสดุชุดใหม่ให้หน้าต่างรีเฟรช — ฝั่งเกมรับ ArtifactMaterials แบบ global ด้วย
        // (client/BuildSystem.cs:68 On<ArtifactMaterials>) ⇒ ไม่ต้องผูก seq
        Send(new ArtifactMaterials
        {
            EntityId = artifact.EntityId,
            Materials = WireMaterials(artifact.EntityId)
        });
    }

    // ── 4. ผลที่คาดว่าจะได้ ─────────────────────────────────────────────────────────

    private void HandleEstimateBuildMsg(EstimateBuild msg, uint seq)
    {
        if (_world.ArtifactManager.Get(msg.EntityId) is not { } artifact)
        {
            Send(new Abort { Text = "ไม่พบสิ่งปลูกสร้างนี้" }, seq);
            return;
        }
        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(artifact.EntityType);
        if (blueprint == null)
        {
            Send(new Abort { Text = "ไม่รู้จักแบบแปลนของหลังนี้" }, seq);
            return;
        }

        // เลเวลที่จะได้ = เฉลี่ยเลเวลของวัสดุทั้งหมด (ที่ใส่แล้ว + ที่กำลังจะใส่)
        // เกณฑ์เดียวกับระบบคราฟต์ (Player.Crafting.ProductLevel) เพราะข้อมูลเกมไม่ได้บอกสูตรฝั่งสร้าง
        var levels = new List<int>();
        foreach (var pair in _world.ArtifactManager.GetBuildMaterials(artifact.EntityId))
        {
            if (pair.Value == null) continue;
            foreach (Item item in pair.Value) levels.Add(item.Level);
        }
        if (msg.Materials != null)
        {
            foreach (var pair in msg.Materials)
            {
                if (pair.Value == null) continue;
                foreach (string itemId in pair.Value)
                {
                    Item? item = FindInventoryItem(itemId);
                    if (item.HasValue) levels.Add(item.Value.Level);
                }
            }
        }

        int minLevel = Math.Max(1, blueprint.MinLevel);
        int level = levels.Count > 0 ? (int)Math.Round(levels.Average()) : minLevel;
        level = Math.Clamp(level, minLevel, Math.Max(minLevel, blueprint.MaxLevel));

        // แท็กที่หลังนี้จะได้ — ของจริงมีแหล่งเดียวคือตารางแท็กโต๊ะคราฟต์ (Support/WorkbenchTags.cs)
        // หลังที่ไม่ใช่โต๊ะจะได้ตารางว่าง ซึ่งฝั่งเกมแสดงเป็น "ไม่มีแท็ก" ได้ถูกต้องอยู่แล้ว
        var tags = new Dictionary<string, int>();
        Tag[] benchTags = WorkbenchTags.Of(artifact.EntityType);
        if (benchTags != null)
        {
            foreach (Tag tag in benchTags)
            {
                if (!string.IsNullOrEmpty(tag.Id)) tags[tag.Id] = tag.Level;
            }
        }

        Send(new BuildEstimation
        {
            Level = level,
            // constants.json → build → default_durability (ของจริง = 7) — ไม่มีสูตรรายหลังในไฟล์
            Durability = BuildTuning.DefaultDurability,
            Tags = tags,
            // 0 = ไม่มีแท็กหายากซ่อนอยู่ — ข้อมูลเกมไม่มีตารางแท็กหายากของสิ่งปลูกสร้างเลย
            // ใส่เลขมั่วจะทำให้เอฟเฟกต์ "ลุ้นของหายาก" เด้งทุกครั้งโดยไม่มีอะไรจริงรองรับ
            UnrevealedRareTagCount = 0,
            ArtifactPreview = new ArtifactPreview
            {
                Size = artifact.Size,
                Rotation = artifact.Rotation,
                Display = artifact.Display,
                IsModular = blueprint.Components != null && blueprint.Components.Contains("Modular")
            }
        }, seq);
    }

    // ── 5. ลงมือสร้าง ───────────────────────────────────────────────────────────────

    private void HandleBuildArtifactMsg(BuildArtifact msg, uint seq)
    {
        if (!TryGetBuildTarget(msg.EntityId, "สร้าง", out AppearArtifact artifact,
                               out MergedBlueprint blueprint, out string error))
        {
            Send(new Abort { Text = error }, seq);
            return;
        }

        if (artifact.States.BuildingState != BuildingState.Occupied)
        {
            Send(new Abort { Text = "หลังนี้สร้างไปแล้ว" }, seq);
            return;
        }
        if (!CheckBuildTool(blueprint, msg.ToolItemId, out error))
        {
            Send(new Abort { Text = error }, seq);
            return;
        }
        if (!AllSlotsFilled(artifact, blueprint, out error))
        {
            Send(new Abort { Text = error }, seq);
            return;
        }

        // พลังงาน: constants.json → build → building.energy (ของจริง = "1" คงที่ ไม่มีตัวแปร)
        SpendBuildEnergy((float)Math.Max(0.0, BuildTuning.EvalByArea(BuildTuning.BuildEnergy, 1, 1.0)));

        // เติมหน้าตาช่องที่ยังว่าง (ช่องที่ไฟล์ไม่มี looks จะไม่มีโมเดลของตัวเอง)
        // ไม่เติม = บางหลังสร้างเสร็จแล้วมองไม่เห็นบนจอ (บั๊กเดียวกับที่เคยเจอมาแล้ว 43 ชนิด)
        FillRemainingDisplayParts(artifact.EntityId, blueprint);

        double now = Gauge.CurrentTime;
        int postprocessSeconds = Math.Max(0, blueprint.PostprocessTime);
        Postprocess? postprocess = new Postprocess
        {
            StartedAt = now,
            EndsAt = now + postprocessSeconds,
            Helpers = Array.Empty<string>(),
            MaxHelperCount = Math.Max(0, blueprint.PostprocessHelperMax),
            RemodelSlotId = string.Empty
        };

        _world.ArtifactManager.SetBuildingState(artifact.EntityId, BuildingState.Built, postprocess);
        _world.Save();

        Console.WriteLine($"[สร้าง] {Short(EntityId)} สร้าง {blueprint.Id} เสร็จ " +
                          $"(รอมาร์มูรีอีก {postprocessSeconds} วิ)");

        // เวลาหลอด "กำลังสร้าง" — ไฟล์ไม่มีค่านี้แยก ใช้สูตรเดียวกับตอนจองพื้นที่
        // (ทั้งสองหลอดคือ "ยืนทำงานหน้าไซต์" เหมือนกัน) — **การเลือกนี้เป็นของเรา**
        int area = Math.Max(1, artifact.Size.x * artifact.Size.y);
        float duration = (float)Math.Clamp(
            BuildTuning.EvalByArea(BuildTuning.SiteDuration, area, 2 + area), 0.0, MaxBuildSeconds);

        var built = new ArtifactBuilt { EntityId = artifact.EntityId, BuilderId = EntityId };

        Send(default(ReplySequenceMark), seq);
        Send(new Messages.Timer { Duration = duration }, seq);
        if (duration <= 0f)
        {
            FinishBuild(built, seq);
            return;
        }
        ScheduleBuildReply(() => FinishBuild(built, seq), duration, "สร้าง");
    }

    private void FinishBuild(ArtifactBuilt built, uint seq)
    {
        Send(built, seq);
        Send(default(ReplySequenceMark), seq);
        // คนอื่นบนเกาะต้องได้เอฟเฟกต์ตอนสร้างเสร็จด้วย (client/BuildSystem.cs:122 On<ArtifactBuilt>)
        _world.BroadCast(built);
    }

    // ── 6. ทำให้สมบูรณ์ ─────────────────────────────────────────────────────────────

    private void HandleCompleteArtifactMsg(CompleteArtifact msg, uint seq)
    {
        if (!TryGetBuildTarget(msg.EntityId, "ทำให้สมบูรณ์", out AppearArtifact artifact,
                               out MergedBlueprint blueprint, out string error))
        {
            Send(new Abort { Text = error }, seq);
            return;
        }

        if (artifact.States.BuildingState != BuildingState.Built)
        {
            Send(new Abort { Text = "หลังนี้ยังสร้างไม่เสร็จ" }, seq);
            return;
        }

        // ต้องรอ "มาร์มูรี" ให้ครบก่อน — ฝั่งเกมเดินหลอดเองจาก Postprocess.EndsAt ที่เราส่งไป
        // (client/Artifact.cs:1059-1100 PostprocessTimeUpdate) ⇒ ปุ่มโผล่ก่อนเวลาไม่ได้อยู่แล้ว
        // แต่ client ที่ถูกแก้ยิงมาก่อนได้ ⇒ ต้องเช็คฝั่งเซิร์ฟด้วย
        if (artifact.States.Postprocess is { } pp && Gauge.CurrentTime < pp.EndsAt)
        {
            Send(new Abort { Text = $"ยังต้องรออีก {pp.EndsAt - Gauge.CurrentTime:0} วินาที" }, seq);
            return;
        }

        _world.ArtifactManager.SetBuildingState(artifact.EntityId, BuildingState.Completed, null);
        // วัสดุถูกใช้ไปกับตัวอาคารแล้ว — เก็บต่อไม่มีประโยชน์และทำให้ไฟล์เซฟบวม
        _world.ArtifactManager.ClearBuildMaterials(artifact.EntityId);
        _world.Save();

        Console.WriteLine($"[สร้าง] {Short(EntityId)} ทำให้ {blueprint.Id} สมบูรณ์แล้ว");

        var completed = new ArtifactCompleted { EntityId = artifact.EntityId };
        Send(completed, seq);
        _world.BroadCast(completed);
    }

    // ── ตัวช่วย ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// หาหลังเป้าหมาย + แบบแปลน พร้อมด่านสิทธิ์
    ///
    /// ใช้ <see cref="MayTouchArtifact"/> ตัวเดียวกับการรื้อ/เปิดตู้ ⇒ **เจ้าของเท่านั้น**
    /// ของจริงคนอื่นมาช่วยสร้างได้ แต่ระบบเจ้าของที่เพิ่งปิดช่องโหว่ไปยังไม่มีแนวคิด "อนุญาตเป็นราย ๆ"
    /// ⇒ เลือกฝั่งปลอดภัยไว้ก่อน (คนอื่นสร้างของเราไม่ได้ ดีกว่าใครก็ยัดของลงหลังใครก็ได้)
    /// </summary>
    private bool TryGetBuildTarget(string entityId, string what, out AppearArtifact artifact,
                                   out MergedBlueprint blueprint, out string error)
    {
        artifact = default;
        blueprint = null;
        error = null;

        if (!MayTouchArtifact(entityId, what))
        {
            error = "ทำกับสิ่งปลูกสร้างนี้ไม่ได้";
            return false;
        }
        if (_world.ArtifactManager.Get(entityId) is not { } found)
        {
            error = "ไม่พบสิ่งปลูกสร้างนี้";
            return false;
        }
        artifact = found;
        blueprint = BlueprintStore.GetBlueprint(artifact.EntityType);
        if (blueprint == null)
        {
            error = "ไม่รู้จักแบบแปลนของหลังนี้";
            return false;
        }
        return true;
    }

    /// <summary>
    /// จับคู่ของที่ client บอกว่าจะใส่ กับของจริงในกระเป๋า แล้วตรวจว่าตรงเงื่อนไขของช่อง
    ///
    /// เกณฑ์เดียวกับระบบคราฟต์เป๊ะ (Player.Crafting.MatchesSlot) เพราะฝั่งเกมใช้ตัวกรองตัวเดียวกัน
    /// (client/Durango.Logic.Item/ItemData.cs HasTagsAndMaterials → OrTagFilter)
    ///   required_tags กับ required_materials ภายในกลุ่มเป็น OR ระหว่างกลุ่มเป็น AND
    ///
    /// จำนวน: ห้ามเกิน <c>count</c> ของช่อง **นับรวมของที่ใส่ไว้ก่อนหน้าแล้ว**
    /// ไม่นับรวม = ยัดของลงช่องเดิมได้ไม่จำกัด (ช่องละ 1 ชิ้นกลายเป็น 1000 ชิ้น)
    /// </summary>
    private bool ResolveBuildMaterials(AppearArtifact artifact, MergedBlueprint blueprint,
                                       Dictionary<string, string[]> sent,
                                       out Dictionary<string, List<Item>> picked, out string error)
    {
        picked = new Dictionary<string, List<Item>>();
        error = null;

        if (sent == null || sent.Count == 0)
        {
            error = "ไม่ได้เลือกวัสดุ";
            return false;
        }
        if (blueprint.Slots == null || blueprint.Slots.Length == 0)
        {
            error = "แบบแปลนนี้ไม่มีช่องวัสดุ";
            return false;
        }

        Dictionary<string, List<Item>> already = _world.ArtifactManager.GetBuildMaterials(artifact.EntityId);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pair in sent)
        {
            string slotId = pair.Key;
            string[] ids = pair.Value;
            if (ids == null || ids.Length == 0) continue;

            Yaml.BlueprintSlot slot = blueprint.Slots.FirstOrDefault(s => s?.slot_id == slotId);
            if (slot == null)
            {
                error = $"แบบแปลนนี้ไม่มีช่อง {slotId}";
                return false;
            }

            int capacity = SlotCapacity(slot, artifact, blueprint);
            int have = already.TryGetValue(slotId, out List<Item> list) ? list?.Count ?? 0 : 0;
            if (have + ids.Length > capacity)
            {
                error = $"ช่อง {slotId} ใส่ได้อีกแค่ {Math.Max(0, capacity - have)} ชิ้น";
                return false;
            }

            var items = new List<Item>(ids.Length);
            foreach (string itemId in ids)
            {
                // ห้ามใช้ไอเทมชิ้นเดียวกันซ้ำสองช่อง — เหตุผลเดียวกับระบบคราฟต์
                if (string.IsNullOrEmpty(itemId) || !used.Add(itemId))
                {
                    error = "วัสดุซ้ำหรือไม่ถูกต้อง";
                    return false;
                }
                Item? item = FindInventoryItem(itemId);
                if (!item.HasValue)
                {
                    error = "ไม่พบวัสดุในกระเป๋า";
                    return false;
                }
                if (!MatchesBuildSlot(item.Value, slot))
                {
                    error = $"วัสดุไม่ตรงเงื่อนไขช่อง {slotId}";
                    return false;
                }
                items.Add(item.Value);
            }
            picked[slotId] = items;
        }

        if (picked.Count == 0)
        {
            error = "ไม่ได้เลือกวัสดุ";
            return false;
        }
        return true;
    }

    /// <summary>
    /// จำนวนชิ้นที่ช่องหนึ่งรับได้
    ///
    /// แบบแปลนที่ปรับขนาดได้ (รั้ว/พื้น) ต้องการวัสดุมากขึ้นตามขนาด — ฝั่งเกมคูณด้วย
    /// <c>GetSlotCountModifier(artifact.Size)</c> (client/BuildSlotContainer.cs:37-42)
    /// ซึ่งคิดจาก <c>size_factor</c> ของช่อง
    ///
    /// ⚠️ <c>size_factor</c> เป็นสูตรข้อความ คิดด้วย <see cref="StatFormula"/>
    /// สูตรอ่านไม่ออก ⇒ ถอยไปที่ <c>count</c> เปล่า ๆ คือทางที่ **ขอวัสดุน้อยกว่า**
    /// (ผู้เล่นได้เปรียบ ดีกว่าสร้างไม่ได้เลยเพราะเซิร์ฟขอของที่เกมไม่ได้ขอ)
    /// </summary>
    private static int SlotCapacity(Yaml.BlueprintSlot slot, AppearArtifact artifact, MergedBlueprint blueprint)
    {
        int count = Math.Max(1, slot.count);
        if (!blueprint.IsSizeVariable || string.IsNullOrEmpty(slot.size_factor)) return count;

        var vars = new Dictionary<string, double>
        {
            ["width"] = artifact.Size.x,
            ["height"] = artifact.Size.y,
            ["x"] = artifact.Size.x,
            ["y"] = artifact.Size.y,
            ["area"] = Math.Max(1, artifact.Size.x * artifact.Size.y)
        };
        if (!StatFormula.TryEval(slot.size_factor, vars, out double factor) || factor <= 0.0) return count;
        return (int)Math.Clamp(Math.Round(count * factor), count, MaxSlotItems);
    }

    private static bool MatchesBuildSlot(Item item, Yaml.BlueprintSlot slot)
    {
        bool tagsOk = IsEmptyTagFilter(slot.required_tags) || MatchesAnyTag(item, slot.required_tags);
        bool materialsOk = IsEmptyTagFilter(slot.required_materials) || MatchesAnyTag(item, slot.required_materials);
        return tagsOk && materialsOk;
    }

    /// <summary>ครบทุกช่องหรือยัง — ช่องที่ยังขาดจะถูกบอกชื่อกลับไปให้ผู้เล่นรู้ว่าขาดอะไร</summary>
    private bool AllSlotsFilled(AppearArtifact artifact, MergedBlueprint blueprint, out string error)
    {
        error = null;
        if (blueprint.Slots == null) return true;

        Dictionary<string, List<Item>> stored = _world.ArtifactManager.GetBuildMaterials(artifact.EntityId);
        foreach (Yaml.BlueprintSlot slot in blueprint.Slots)
        {
            if (slot?.slot_id == null) continue;
            int need = SlotCapacity(slot, artifact, blueprint);
            int have = stored.TryGetValue(slot.slot_id, out List<Item> list) ? list?.Count ?? 0 : 0;
            if (have < need)
            {
                error = $"ช่อง {slot.slot_id} ยังขาดวัสดุอีก {need - have} ชิ้น";
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// เครื่องมือที่ใช้สร้างได้ — blueprints.json → tool_tags (tag → เลเวลขั้นต่ำ)
    /// ตารางว่าง = มือเปล่าก็สร้างได้ (เกณฑ์เดียวกับระบบคราฟต์)
    /// </summary>
    private bool CheckBuildTool(MergedBlueprint blueprint, string toolItemId, out string error)
    {
        error = null;
        if (IsEmptyTagFilter(blueprint.ToolTags)) return true;

        if (string.IsNullOrEmpty(toolItemId))
        {
            error = "ต้องใช้เครื่องมือ";
            return false;
        }
        Item? tool = FindInventoryItem(toolItemId);
        if (!tool.HasValue || !MatchesAnyTag(tool.Value, blueprint.ToolTags))
        {
            error = "เครื่องมือใช้สร้างหลังนี้ไม่ได้";
            return false;
        }
        return true;
    }

    /// <summary>
    /// โมเดลของช่องหนึ่ง ตามวัสดุที่ผู้เล่นใส่
    ///
    /// <c>looks</c> ในไฟล์คีย์ด้วย **ชื่อแท็กของวัสดุ** (bed_01/common มี leaf·stem·leather·fur·
    /// feather·fabric ซึ่งตรงกับ required_materials ของช่องนั้นเป๊ะ) ⇒ จับคู่ด้วยแท็กของของที่ใส่
    /// ไม่เจอ ⇒ ใช้ <c>default_look_tag</c> ⇒ ยังไม่เจอ ⇒ เอาโมเดลตัวแรกที่มี
    /// </summary>
    private static string LookForSlot(MergedBlueprint blueprint, string slotId, List<Item> items)
    {
        Yaml.BlueprintSlot slot = blueprint.Slots?.FirstOrDefault(s => s?.slot_id == slotId);
        if (slot?.looks == null || slot.looks.Count == 0) return null;

        if (items != null)
        {
            foreach (Item item in items)
            {
                if (item.Tags == null) continue;
                foreach (Tag tag in item.Tags)
                {
                    if (tag.Id != null && slot.looks.TryGetValue(tag.Id, out ArtifactLook byTag)
                        && !string.IsNullOrEmpty(byTag?.model_key))
                    {
                        return byTag.model_key;
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(slot.default_look_tag)
            && slot.looks.TryGetValue(slot.default_look_tag, out ArtifactLook byDefault)
            && !string.IsNullOrEmpty(byDefault?.model_key))
        {
            return byDefault.model_key;
        }

        foreach (var pair in slot.looks)
        {
            if (!string.IsNullOrEmpty(pair.Value?.model_key)) return pair.Value.model_key;
        }
        return null;
    }

    /// <summary>
    /// เติมโมเดลให้ช่องที่ยังไม่มี ตอนกดสร้าง
    ///
    /// ช่องที่ไฟล์ไม่มี <c>looks</c> จะไม่มีโมเดลของตัวเอง — ถ้าหลังนั้นไม่มีช่องไหนมีโมเดลเลย
    /// ให้ใช้ <c>default_look</c> ของแบบแปลน เหมือนที่ Cheats.MakeAppearArtifact ทำ
    /// </summary>
    private void FillRemainingDisplayParts(string entityId, MergedBlueprint blueprint)
    {
        Dictionary<string, List<Item>> stored = _world.ArtifactManager.GetBuildMaterials(entityId);
        bool any = false;

        if (blueprint.Slots != null)
        {
            foreach (Yaml.BlueprintSlot slot in blueprint.Slots)
            {
                if (slot?.slot_id == null) continue;
                stored.TryGetValue(slot.slot_id, out List<Item> items);
                string model = LookForSlot(blueprint, slot.slot_id, items);
                if (model == null) continue;
                _world.ArtifactManager.SetDisplayPart(entityId, slot.slot_id, model);
                any = true;
            }
        }

        if (any || string.IsNullOrEmpty(blueprint.DefaultLook)) return;
        // ไม่มีช่องไหนให้โมเดลเลย ⇒ ใช้หน้าตาเริ่มต้นของแบบแปลน (เงื่อนไข Burnable ตามต้นฉบับ)
        string fallback = blueprint.Components != null && blueprint.Components.Contains("Burnable")
            ? blueprint.DefaultLook + "_burning"
            : blueprint.DefaultLook;
        _world.ArtifactManager.SetDisplayPart(entityId, "common", fallback);
    }

    /// <summary>หักพลังงาน — ทางเดียวกับระบบคราฟต์ (Player.Crafting.SpendCraftEnergy)</summary>
    private void SpendBuildEnergy(float energy)
    {
        if (energy <= 0f) return;
        _survival.Add(SurvivalState.KeyEnergy, -energy);
        FlushSurvival();      // ค่ากระโดด ⇒ ส่งเส้นใหม่ทันที ไม่รอรอบตรวจ
    }

    /// <summary>นัดส่งคำตอบตัวที่สองเมื่อครบเวลา — callback ทำแค่ Send (ดู <see cref="_buildTimers"/>)</summary>
    private void ScheduleBuildReply(System.Action send, float duration, string what)
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
                Console.WriteLine($"[สร้าง] ส่งผล{what}ไม่สำเร็จ: {e.Message}");
            }
            finally
            {
                lock (_buildTimers) { _buildTimers.Remove(timer); }
                timer?.Dispose();
            }
        }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

        lock (_buildTimers) { _buildTimers.Add(timer); }
        timer.Change((int)(duration * 1000f), System.Threading.Timeout.Infinite);
    }

    private void ClearBuildTimers()
    {
        lock (_buildTimers)
        {
            foreach (System.Threading.Timer timer in _buildTimers) timer.Dispose();
            _buildTimers.Clear();
        }
    }
}
