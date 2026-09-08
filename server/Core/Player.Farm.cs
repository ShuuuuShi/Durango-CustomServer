using System;
using System.Collections.Generic;
using Durango.Network;
using Messages;
using Shared.Ability;
using Shared.Item;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  ไร่นา / พืช / ไฟ / ผลของสิ่งปลูกสร้าง
//
//  เก็บเกี่ยวแปลงที่โตแล้วเดินได้แล้ว: Touch → Collectible ของ grows_to → Collect → ของเข้ากระเป๋า
//  ดู TryHandleFarmHarvest / HandleTouchMsg (Growable) / ArtifactManager.ClearFarming
//
//  ✘ รดน้ำ / ใส่ปุ๋ย / ถอน / เร่งโต ยัง Abort (ไม่บล็อกเส้นเก็บเกี่ยว)
//    เหตุผลรายตัวเขียนกำกับไว้ที่ handler
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    /// <summary>
    /// ส่วนท้ายชื่อโมเดลตอนติดไฟ — ของ NEXON ล้วน ไม่ใช่ค่าที่เราตั้ง
    /// ต้นฉบับใช้ที่ nexonSRC/Durango.Offline/Cheats.cs:220 (และที่พอร์ตมาแล้ว
    /// server/Core/Cheats.cs:288 · server/Core/Player.Building.cs:1093)
    /// </summary>
    private const string BurningLookSuffix = "_burning";

    /// <summary>ช่องโมเดลที่ของ Burnable ทุกชนิดตกลงมาใช้ (ไม่มีชนิดไหนมี looks รายช่อง)</summary>
    private const string BurnableLookSlot = "common";

    /// <summary>
    /// [7 ก.ย. 2026] หลังนี้ไฟติดอยู่ไหม — ใช้ตัดสินว่าเมนูควรโชว์ "จุดไฟ" หรือ "ดับไฟ"
    /// (เรียกจาก <c>HandleTouchMsg</c> ใน Core/Player.cs)
    ///
    /// **สภาพไฟไม่มีฟิลด์เก็บของตัวเอง** — ทั้ง ArtifactState และ ArtifactDisplay ไม่มีบูลีน
    /// "ติดไฟ" เลย สิ่งเดียวที่เปลี่ยนตอนจุดคือชื่อโมเดลในช่อง <c>common</c>
    /// (<see cref="HandleBurnableMsg"/> ตั้งเป็น <c>default_look + "_burning"</c>)
    /// ⇒ อ่านกลับจากที่เดียวกันคือความจริงเดียวที่มี ไม่ใช่การเดา
    ///
    /// หลังที่ไม่รู้จัก/ยังไม่มีโมเดลในช่องนั้น ⇒ ถือว่าไฟดับ = โชว์ปุ่ม "จุดไฟ"
    /// ซึ่งเป็นฝั่งที่ปลอดภัยกว่า: กดแล้วได้ผลตามที่เห็น (จุดซ้ำตอนติดอยู่แล้วก็แค่เงียบ)
    /// </summary>
    private static bool IsBurning(AppearArtifact? artifact, Yaml.MergedBlueprint blueprint)
    {
        if (artifact is not { } value || string.IsNullOrEmpty(blueprint?.DefaultLook)) return false;
        Dictionary<string, string> parts = value.Display.Parts;
        if (parts == null || !parts.TryGetValue(BurnableLookSlot, out string look)) return false;
        return string.Equals(look, blueprint.DefaultLook + BurningLookSuffix, StringComparison.Ordinal);
    }

    private void RegisterFarmHandlers()
    {
        // ── ไฟ: จุด / ดับ ───────────────────────────────────────────────────────────

        // FireBurnable (2096) — "불 붙이기" จุดกองไฟ/เตา
        // client/Durango.Logic.Interactions/ArtifactInteractions.cs:80 ผูก Interaction.Fire
        // → :577-583 Fire() ยิงแบบ **ไม่ผูกรอ** (ไม่มี .On) ⇒ ผลที่มองเห็นมาทาง
        //   push ArtifactDisplay(2059) ที่ SetDisplayPart ยิงให้เอง
        _connection.Recv(delegate(FireBurnable msg, PacketHeader header)
        {
            HandleBurnableMsg(msg.EntityId, header.Seq, lit: true);
        });

        // ExtinguishBurnable (2097) — "불 끄기" ดับไฟ
        // ArtifactInteractions.cs:81 ผูก Interaction.Extinguish → :586-592 ยิงแบบไม่ผูกรอเช่นกัน
        _connection.Recv(delegate(ExtinguishBurnable msg, PacketHeader header)
        {
            HandleBurnableMsg(msg.EntityId, header.Seq, lit: false);
        });

        // ── สอบถามผลของปุ๋ยก่อนใส่ (คำถาม ไม่ใช่การกระทำ) ────────────────────────────

        // GetExpectedCropBooster (37123) → ExpectedCropBooster (37124)
        //
        // ArtifactInteractions.cs:880-928 Fertilize(): ทุกครั้งที่ผู้เล่น "ติ๊กเลือกปุ๋ยเพิ่ม/ออก"
        // ในหน้าต่างใส่ปุ๋ย มันจะ AttachLoadingRingToHelperLabel() แล้วยิงตัวนี้ (:908-912)
        // เพื่อถามว่า "ปุ๋ยชุดนี้ให้ผลพิเศษอะไร" แล้วรอ .On<ExpectedCropBooster> (:913)
        // ⇒ **ไม่ตอบ = วงแหวนโหลดหมุนค้างตลอด** ทุกครั้งที่ติ๊ก (มี .Rest ที่ :920 ก็จริง
        //    แต่ .Rest ทำงานเมื่อมีคำตอบชนิดอื่นมาถึงเท่านั้น ไม่ใช่ timeout)
        //
        // ตอบ "ไม่มีผลพิเศษ": CropBooster = "" ⇒ ฝั่งเกมแปลเป็น "없음" (ไม่มี) ที่ :915
        // นี่คือความจริงของเซิร์ฟนี้ ไม่ใช่การยอมแพ้ — ตารางที่บอกว่าปุ๋ยชนิดไหนให้ booster ตัวไหน
        // **ไม่มีอยู่ในข้อมูลที่สกัดมา** (performance.json → fertilizer มีแต่ปริมาณปุ๋ย
        // ไม่มีคำว่า booster เลย · grep "crop_booster" ทั่ว server/data/assets = ไม่เจอ)
        // และ FertilizePlant (ตัวใส่ปุ๋ยจริง) เซิร์ฟก็ยังไม่มี handler ⇒ ตอบว่ามี booster = โกหก
        _connection.Recv(delegate(GetExpectedCropBooster msg, PacketHeader header)
        {
            Send(new ExpectedCropBooster
            {
                CropBooster = string.Empty,
                BoosterLevel = 0
            }, header.Seq);
        });

        // ── การกระทำที่เซิร์ฟยังทำจริงไม่ได้ — ตอบ Abort พร้อมข้อความ ────────────────

        // UprootPlant (3807) — "뽑기" ถอนต้นที่ปลูกไว้ทิ้ง
        // ArtifactInteractions.cs:866-877 Uproot() รอ .On<Timer> เพื่อเดินหลอด "uproot"
        // (constants.json → farm.uprooting_time = 3 วินาที คือค่าที่จะใช้ถ้าทำได้)
        //
        // ทำไมยังทำไม่ได้: ถอนต้น = ต้องล้าง **ทั้งสองอย่างพร้อมกัน**
        //   (1) ArtifactState.Farming → null  (2) ArtifactDisplay.Crop → null
        // ตัวที่สองล้างได้ด้วย UpdateArtifactDisplay แต่ตัวแรกล้างไม่ได้เลย
        // (Core/ArtifactManager.cs เปิดให้แก้ Farming ทางเดียวคือ SeedPlant = ปลูกใหม่)
        // ล้างแค่โมเดล = ป้ายข้อมูลยังบอกว่ามีพืชอยู่ แถม ProcessFarming (:375-403)
        // จะเห็นว่า Display.Crop ไม่ตรงกับต้นโต แล้ว **เสกต้นกลับมาเอง** ตอนครบเวลา
        // ⇒ ครึ่ง ๆ กลาง ๆ แบบนั้นแย่กว่าไม่ทำ
        _connection.Recv(delegate(UprootPlant msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบถอนต้นพืช" }, header.Seq);
        });

        // GrowRapidly (3712) — "즉시 성장" จ่ายเพชรเร่งให้พืชโตทันที
        // ArtifactInteractions.cs:1044-1065 เปิดกล่องจ่ายเงิน Currency.Gem แล้ว :1067-1077
        // ยิงตัวนี้ รอ .On<OK> เพื่อเล่นเอฟเฟกต์ "สร้างเสร็จ"
        //
        // ทำไมยังทำไม่ได้ (สองชั้น):
        //   1. ฝั่งเกม **ยิงตัวนี้ไม่ได้อยู่แล้ว** — :1050-1054 return ทันทีถ้า
        //      Farming.RapidGrowthCost เป็น null ซึ่งเซิร์ฟตั้ง null ไว้ตลอด
        //      (Core/ArtifactManager.cs:353-355 — ตั้งใจ เพราะยังไม่มีระบบเพชร)
        //   2. ต่อให้ยิงมาได้ ก็ต้องเลื่อน Farming.GrowsUntil ซึ่งแก้ไม่ได้ (เหตุผลเดียวกับ UprootPlant)
        _connection.Recv(delegate(GrowRapidly msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบเร่งการเติบโต" }, header.Seq);
        });

        // Sprinkle (37121) — "물뿌리개" เครื่องรดน้ำอัตโนมัติรดแปลงรอบตัวมันทีเดียว
        // ArtifactInteractions.cs:800-846: ยิงแล้วรอ **สามชนิดบน seq เดียว**
        //   .On<SprinkledInfo>(:814) วาดกรอบสีบนช่องที่โดนน้ำ/ปุ๋ย
        //   .On<Timer>(:831) เดินหลอด + ท่าทาง "Farming_sprinkler"
        //   .On<OK>(:836) หยุดหลอด · .Rest(:840) หยุดหลอดเมื่อได้คำตอบชนิดอื่น
        // ⇒ Abort ตกที่ .Rest พอดี หลอดที่ Play(10f) ไว้ล่วงหน้า (:802) จึงหยุดทันที ไม่ค้าง
        //
        // ทำไมยังทำไม่ได้: ต้องมีทั้ง ArtifactState.Sprinkler (จำนวนน้ำ/ปุ๋ยที่ชาร์จไว้ —
        // เซิร์ฟไม่เคยตั้ง ⇒ client/ArtifactInfoMainWidget.cs:736 ไม่โชว์อะไรเลย)
        // และต้องเพิ่ม Farming.Water ของทุกแปลงในรัศมี ซึ่งแก้ไม่ได้ (เหตุผลเดียวกับ UprootPlant)
        // ค่าที่จะใช้ถ้าทำได้มีพร้อมแล้วใน constants.json → sprinkler.sprinkle_water
        // {duration: 3, energy: 5} และ fertilizer_tags: ["fertilizer_liquid"]
        _connection.Recv(delegate(Sprinkle msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานเครื่องรดน้ำ" }, header.Seq);
        });

        // ChangeFarmingEncyclopediaMastery (37128) — เลือก/สลับ "ความชำนาญ" ในสารานุกรมเพาะปลูก
        // client/FarmingEncyclopediaSystem.cs:63-71 ยิงแบบ **ไม่ผูกรอ**
        // (เรียกจาก client/Durango.UI.Popup/FarmingEncyclopediaPopup.cs:116 สลับ · :137 เลือก)
        // ผลที่ถูกต้องคือเซิร์ฟ push FarmingEncyclopediaProgress กลับไป ซึ่งฝั่งเกมรับด้วย
        // global On<FarmingEncyclopediaProgress> (FarmingEncyclopediaSystem.cs:16)
        //
        // ทำไมยังทำไม่ได้: เซิร์ฟตอบ GetEncyclopedia ด้วย **ตารางว่าง** อยู่แล้ว
        // (Core/Player.Animals.cs:540-561 — ยังไม่มีระบบสารานุกรมจริง) ⇒ ยังไม่มี
        // FarmingEncyclopediaData ของเมล็ดไหนให้แก้เลย การ push ค่าความชำนาญกลับไป
        // เท่ากับแต่งความคืบหน้าปลอมให้ผู้เล่นเห็น
        _connection.Recv(delegate(ChangeFarmingEncyclopediaMastery msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานสารานุกรมการเพาะปลูก" }, header.Seq);
        });

        // TakeEffect (821) — "효과 받기" มารับบัฟจากของที่จุดไว้ (กระถางธูปแคลน ฯลฯ)
        // ArtifactInteractions.cs:142-148 ยิงแบบไม่ผูกรอ (คู่กับ ChargeEffect ที่ :141)
        //
        // ทำไมยังทำไม่ได้ (สองชั้น):
        //   1. ต้องหัก ArtifactState.Effector.RemainCount ลง — Core/ArtifactManager.cs:455
        //      มีแต่ ChargeEffect ที่ **ตั้งเป็น 100** ไม่มีตัวหักลง และแก้เองไม่ได้
        //   2. ตัวบัฟเองหาไม่เจอ: grep "incense"/"thurible" ใน survival/status_effects.json
        //      (372 รายการ) = ไม่เจอสักตัว ⇒ ไม่รู้ว่าจะให้บัฟอะไร การเดา id บัฟคือการแต่งของ
        _connection.Recv(delegate(TakeEffect msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบรับผลจากสิ่งปลูกสร้าง" }, header.Seq);
        });

        // InvestToCrack (3663) — หย่อน "หินนำทางทรัพยากร" ลงหลุมอุกกาบาตเพื่อเปิดหลุม
        // ArtifactInteractions.cs:1080-1130 Invest(): เช็คหินในกระเป๋าฝั่งเกมก่อน (:1108)
        // แล้วเดินไปหาหลุม → ยิงตัวนี้ รอ .On<Timer>(:1121, หลอด 4 วิ) แล้ว .On<OK>(:1124)
        // ค่าที่จะใช้ถ้าทำได้อ่านไว้แล้วครบใน server/Support/CrackTuning.cs
        // (required_investment · activated_time 600 · investment_duration 4)
        //
        // ทำไมยังทำไม่ได้ (สองชั้น):
        //   1. หักหินนำทางไม่ได้ — มันเป็น "บัตรกำนัล" ในกระเป๋าเงิน แต่เซิร์ฟส่ง
        //      Wallet = null เสมอ (Core/Player.Inventory.cs:666) ⇒ ไม่มีของให้หัก
        //      เปิดหลุมให้ฟรี = แจกทรัพยากรวาร์ปโดยไม่มีต้นทุน
        //   2. ต้องตั้ง Crack.ActivatedSince/Until ซึ่งแก้ไม่ได้ — Core/World.cs:269-285
        //      สร้าง Crack ตอนเปิดโลกได้ทางเดียว ไม่มี API แก้ทีหลัง
        _connection.Recv(delegate(InvestToCrack msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบเปิดหลุมอุกกาบาต" }, header.Seq);
        });

        // SkipPostprocess (2450) — จ่ายเพชรข้ามช่วง "마무리" (มาร์มูรี) หลังสร้างเสร็จ
        // client/BuildSystem.cs:445 ใส่ปุ่มลงเมนู · ArtifactInteractions.cs:82 ผูก handler
        // → :343-362 เปิดกล่อง ShowPayConfirm(cost, Currency.Gem) แล้วยิงแบบไม่ผูกรอ
        //
        // ทำไมตอบ Abort ทั้งที่ "ทำได้ทางเทคนิค":
        // เลื่อนเวลาให้เสร็จทันทีทำได้จริงด้วย SetBuildingState(entityId, Built, postprocess ที่หมดอายุ)
        // แต่ **หักค่าใช้จ่ายไม่ได้** เพราะไม่มีระบบเพชร (Wallet = null) ⇒ กลายเป็น
        // "ข้ามเวลารอฟรี" ซึ่งล้มด่านที่ Core/Player.Building.cs:601-605 ตั้งใจตั้งไว้
        // (ปุ่ม "완성" ห้ามโผล่ก่อน Postprocess.EndsAt) — ให้ฟรีคือทำระบบเวลาสร้างพังทั้งระบบ
        // ถ้าวันไหนมีระบบเพชรแล้ว เปลี่ยนตรงนี้เป็น SetBuildingState ได้ทันที
        _connection.Recv(delegate(SkipPostprocess msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบข้ามเวลาด้วยเพชร" }, header.Seq);
        });
    }

    /// <summary>
    /// เก็บเกี่ยวแปลงที่โตแล้ว — ใช้ message <c>Collect</c> ชุดเดียวกับของธรรมชาติ
    ///
    /// คืน <c>true</c> เมื่อเป้าเป็นแปลงที่มี Farming (จัดการแล้ว ทั้งสำเร็จและปฏิเสธ)
    /// คืน <c>false</c> เมื่อไม่ใช่แปลง ให้ <c>HandleCollectMsg</c> ไปต่อสายของธรรมชาติ/ซาก
    ///
    /// ไม่ยิง <c>NoteQuestEvent(Farmed)</c> — Daily <c>daily_farming_a_01</c> นับตอนปลูกเท่านั้น
    /// ไม่ยิง <c>NoteQuestEvent(Collected)</c> — อย่าไปเดินเควสเก็บของธรรมชาติ
    /// </summary>
    private bool TryHandleFarmHarvest(Collect msg, uint seq)
    {
        AppearArtifact? found = _world.ArtifactManager.Get(msg.EntityId);
        if (found is not { } artifact) return false;
        if (!artifact.States.Farming.HasValue) return false;

        double now = Gauge.CurrentTime;
        if (!_world.ArtifactManager.TryGetMatureCrop(msg.EntityId, now, out string seed, out Crop crop))
        {
            RejectCollect(seq, "พืชยังไม่โตเต็มที่", msg);
            return true;
        }

        if (!string.Equals(msg.GeneratorId, FarmHarvest.GeneratorId(crop), StringComparison.Ordinal))
        {
            RejectCollect(seq, $"ไม่มี generator '{msg.GeneratorId}' ของแปลงนี้", msg);
            return true;
        }

        string owner = _world.ArtifactManager.OwnerOf(msg.EntityId);
        if (!string.IsNullOrEmpty(owner) && !string.Equals(owner, EntityId, StringComparison.Ordinal))
        {
            RejectCollect(seq, "ไม่ใช่แปลงของคุณ", msg);
            return true;
        }

        if (!IsWithinCollectRange(artifact.Tile))
        {
            RejectCollect(seq, "อยู่ไกลเกินไป", msg);
            return true;
        }

        var items = new List<Item>();
        foreach (string proto in FarmHarvest.ProductPrototypes(seed, crop))
        {
            Item? item = Cheats.MakeItem(proto, 1);
            if (!item.HasValue) continue;
            Item value = item.Value;
            value.CollectibleId = crop.GrowsTo;
            value.GeneratorId = FarmHarvest.GeneratorId(crop);
            items.Add(value);
        }
        if (items.Count == 0)
        {
            RejectCollect(seq, "ไม่พบไอเทมผลผลิต", msg);
            return true;
        }

        if (!_world.ArtifactManager.ClearFarming(msg.EntityId))
        {
            RejectCollect(seq, "ล้างแปลงไม่สำเร็จ", msg);
            return true;
        }

        float duration = Math.Max(GatheringTuning.MinCollectSeconds,
            CollectibleTable.Duration(CollectibleTable.Effort(1)));
        var collected = new Collected
        {
            Items = items.ToArray(),
            Result = Result.Success,
            ActionInfo = new ActionInfo
            {
                ActionLevel = 1,
                PotentialLevel = 1,
                RelatedCategory = Shared.Skill.Category.Farming,
                RelatedAbility = Derived.Invalid,
                SuccessRatio = 1f
            },
            RanOut = true
        };

        Send(default(ReplySequenceMark), seq);
        Send(new Messages.Timer { Duration = duration }, seq);
        Console.WriteLine($"[ปลูก] {Short(EntityId)} เก็บเกี่ยว {crop.GrowsTo} ×{items.Count} " +
                          $"จาก {Short(msg.EntityId)} — รอ {duration:0.#} วิ");
        ScheduleFarmHarvestFinish(collected, items, seq, duration);
        OnContextChanged();
        return true;
    }

    /// <summary>
    /// ตอบ <c>GetCollectible</c> ของแปลง — ฝั่งเกมยิงมาเมื่อได้ <c>CollectibleChanged</c>
    /// (เส้นเก็บของธรรมชาติ) แปลงที่โตแล้วยังต้องได้ generator ชุดเดิม
    /// </summary>
    private bool TrySendFarmCollectible(string entityId, uint seq)
    {
        AppearArtifact? found = _world.ArtifactManager.Get(entityId);
        if (found is not { } artifact || !artifact.States.Farming.HasValue) return false;

        if (_world.ArtifactManager.TryGetMatureCrop(entityId, Gauge.CurrentTime, out string seed, out Crop crop))
        {
            Send(FarmHarvest.BuildCollectible(entityId, seed, crop), seq);
        }
        else
        {
            Send(new Collectible { EntityId = entityId, Generators = Array.Empty<Generator>() }, seq);
        }
        return true;
    }

    private void ScheduleFarmHarvestFinish(Collected collected, List<Item> items, uint seq, float duration)
    {
        if (duration <= 0f || duration > GatheringTuning.MaxCollectSeconds)
        {
            CompleteFarmHarvestAfterDelay(collected, items, seq);
            return;
        }

        Collected collectedCopy = collected;
        List<Item> itemsCopy = items;
        uint seqCopy = seq;
        System.Threading.Timer timer = null;
        timer = new System.Threading.Timer(delegate
        {
            try
            {
                CompleteFarmHarvestAfterDelay(collectedCopy, itemsCopy, seqCopy);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ปลูก] จบการเก็บเกี่ยวไม่สำเร็จ: {e.Message}");
                try { FinishCollect(collectedCopy, seqCopy); } catch { /* ignore */ }
            }
            finally
            {
                lock (_collectTimers)
                {
                    _collectTimers.Remove(timer);
                }
                timer?.Dispose();
            }
        }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        lock (_collectTimers)
        {
            _collectTimers.Add(timer);
        }
        timer.Change((int)(duration * 1000f), System.Threading.Timeout.Infinite);
    }

    private void CompleteFarmHarvestAfterDelay(Collected collected, List<Item> items, uint seq)
    {
        AddItems(items);
        Send(new InventoryUpdated { EntityId = EntityId, Items = items.ToArray() });
        AddExpForAction(SkillTuning.GatherWeight, Shared.Skill.Category.Farming, "เก็บเกี่ยวพืช");
        FinishCollect(collected, seq);
        OnContextChanged();
        Console.WriteLine($"[ปลูก] {Short(EntityId)} จบเก็บเกี่ยว · ของ {items.Count} ชิ้น");
    }

    /// <summary>
    /// จุด/ดับของที่มี component "Burnable" — สลับโมเดลช่อง <c>common</c> แล้วกระจายทั้งเกาะ
    ///
    /// ด่านที่ต้องผ่าน (เรียงตามลำดับที่ตรวจ):
    ///   1. มีหลังนี้อยู่จริงบนเกาะ
    ///   2. แบบแปลนมี component "Burnable" และมี default_look
    ///      (ข้อมูลจริง 19 ชนิด — ทุกชนิดมีโมเดล default_look + "_burning" อยู่ใน artifact_models.json)
    ///   3. สร้างเสร็จแล้ว — เงื่อนไขเดียวกับที่เมนู "ใช้งาน" ทั้งหมดใช้
    ///      (Core/Player.cs:1105-1107: กองไฟที่ยังไม่ใส่วัสดุห้ามใช้งานได้)
    ///   4. อยู่ใกล้พอ — ระยะเดียวกับที่ MayTouchArtifact ใช้ (Core/Player.cs:1329-1334)
    ///
    /// ⚠️ **การตีความของเรา**: ตรงนี้ **ไม่** ใช้ <c>MayTouchArtifact</c> เพราะตัวนั้นบังคับ
    /// "ต้องเป็นเจ้าของ" ซึ่งเหมาะกับการรื้อ/เปิดตู้ แต่กองไฟเป็นของใช้ร่วมกัน
    /// (ต้นฉบับไม่ได้เช็คเจ้าของในเมนู Fire/Extinguish เลย) และผลของคำสั่งนี้เป็นแค่โมเดล
    /// สลับกลับได้ด้วยการกดครั้งเดียว ⇒ ใช้แค่ด่านระยะทาง ซึ่งกันสคริปต์ยิงข้ามเกาะได้อยู่แล้ว
    /// </summary>
    private void HandleBurnableMsg(string entityId, uint seq, bool lit)
    {
        if (_world.ArtifactManager.Get(entityId) is not { } artifact)
        {
            Send(new Abort { Text = "ไม่พบสิ่งปลูกสร้างหลังนี้" }, seq);
            return;
        }

        // อ้างชื่อเต็ม (Yaml.*) แทน using Yaml; เพราะเนมสเปซนั้นมีชื่อชนกับ Messages ได้ง่าย
        Yaml.MergedBlueprint blueprint = Yaml.BlueprintStore.GetBlueprint(artifact.EntityType);
        if (blueprint?.Components == null
            || Array.IndexOf(blueprint.Components, "Burnable") < 0
            || string.IsNullOrEmpty(blueprint.DefaultLook))
        {
            Send(new Abort { Text = "ของชิ้นนี้จุดไฟไม่ได้" }, seq);
            return;
        }

        if (artifact.States.BuildingState != Shared.Building.BuildingState.Completed)
        {
            Send(new Abort { Text = "ยังสร้างไม่เสร็จ" }, seq);
            return;
        }

        int reach = ArtifactReachTiles + Math.Max(artifact.Size.x, artifact.Size.y);
        if (!IsWithinTiles(artifact.Tile, reach))
        {
            Send(new Abort { Text = "อยู่ไกลเกินไป" }, seq);
            return;
        }

        // SetDisplayPart คืน false เมื่อโมเดลเดิมตรงกับที่จะตั้งอยู่แล้ว (จุดซ้ำ/ดับซ้ำ)
        // กรณีนั้นไม่ใช่ความผิดพลาด — สภาพที่ผู้เล่นต้องการก็เป็นแบบนั้นอยู่แล้ว ⇒ เงียบไว้
        _world.ArtifactManager.SetDisplayPart(entityId, BurnableLookSlot,
            lit ? blueprint.DefaultLook + BurningLookSuffix : blueprint.DefaultLook);
    }
}
