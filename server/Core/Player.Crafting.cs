using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Durango.Network;
using Durango.Utils;
using Messages;
using MsgPack;
using Newtonsoft.Json;
using Shared.Item;
using UnityEngine;
using Yaml;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
// ระบบคราฟต์ — [5 ก.ย. 2026]
//
// ลำดับ message ที่ "ตัวเกมคาดหวัง" (ยืนยันจากซอร์สจริง ไม่ได้เดา):
//
//   client → Craft(6) {RecipeId, Materials{slotId→itemId[]}, ToolItemId, Workbench}
//            (client/CraftSystem.cs:141-155 DoNextCraftQueue)
//            ทันทีที่ส่ง client เปิดหลอดความคืบหน้าด้วยเวลาชั่วคราว Ping+10 วิ (:159)
//
//   server → **ทุกตัวต้องตอบที่ seq ของ Craft เดียวกัน** เพราะ handler ทั้งชุดผูกกับ seq นั้น
//            (client/CraftSystem.cs:157-215 RegisterPostCraftEvents):
//              · Timer(1134)                  → CraftingTimer.Play(Duration) ตั้งเวลาจริงของหลอด
//              · CraftStartedOnWorkbench(66)  → เข้าคิวโต๊ะ (entrusted) แล้วไปคราฟต์ตัวถัดไปในคิว
//              · Crafted(122)                 → สำเร็จ/ล้มเหลว แล้วไปคราฟต์ตัวถัดไปในคิว
//              · EnergyWarning(3648)          → หยุดหลอดไว้ถามผู้เล่นก่อน
//              · อย่างอื่น (Abort/Error)      → .Rest() → CraftingTimer.Stop() = ยกเลิกสะอาด
//
// ⚠️ กับดักใหญ่ที่สุดของระบบนี้: ปกติ client **ลบ handler ของ seq ทิ้งทันทีที่ได้คำตอบตัวแรก**
//    (client/Durango.Network/Connection.cs:905-908) ⇒ ส่ง Timer แล้ว Crafted จะตกไป global
//    handler ซึ่ง "ไม่มี" (ทั้งเกมมีที่เดียวคือ .On ใน RegisterPostCraftEvents) = ของหายเงียบ ๆ
//    ทางแก้ที่ตัวเกมเตรียมไว้คือ **แพ็กเก็ต TypeCode 0** ที่ทำหน้าที่ "เปิด/ปิดชุดคำตอบต่อเนื่อง"
//    (client/Durango.Network/Connection.cs:838-861: เจอครั้งแรก = เปิด, ครั้งที่สอง = ปิดแล้วลบ
//     handler) ระหว่างเปิดอยู่ client จะไม่ลบ handler ทิ้ง ⇒ ตอบกี่ตัวก็ได้
//    ⇒ Craft หนึ่งครั้ง = เปิดชุด · Timer · Crafted · ปิดชุด (ดู <see cref="ReplySequenceMark"/>)
//    ตัวเกมไม่เคยส่ง TypeCode 0 ออกมาเอง (ไล่ดู client/Durango.Network/Connection.cs ทั้งไฟล์แล้ว
//    มีแต่ทางรับ) ⇒ เป็นกลไกฝั่งเซิร์ฟล้วน ๆ
//
// ข้อมูล: data/assets/item/recipes.json (720 สูตร) — โหลดเองในไฟล์นี้เพราะ Yaml.Recipe ของเซิร์ฟ
// (Support/YamlArtifact.cs:10-13) เก็บแค่ add_on ไว้ทำเมนูตกแต่ง ไม่มีฟิลด์ที่การคราฟต์ต้องใช้
// ═══════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// ค่าที่ **เราตั้งเอง** ของระบบคราฟต์ — ไม่มีในไฟล์ข้อมูลของเกม รวมไว้ที่เดียวตรงนี้
/// ตัวเลขที่มาจากไฟล์จริง (duration · energy · count · min_level/max_level · slots)
/// อยู่ใน data/assets/item/recipes.json ห้ามย้ายมาไว้ที่นี่
/// </summary>
public static class CraftTuning
{
    /// <summary>
    /// อัตราสำเร็จที่ส่งให้เกม — **ค่าของเรา** คงที่ 100%
    ///
    /// ⚠️ ของจริงเป็นสูตรข้อความใน constants.json → success_probability
    ///   "1 - ( max(0, d - a - correction) / 100.) ** 2"
    /// ซึ่งต้องมีค่า a (ความสามารถของผู้เล่น) ที่มาจากระบบสกิลซึ่งเซิร์ฟยังไม่มี
    /// (Core/Player.cs:283-294 ตอบ GetSkills เป็นชุดว่าง) และโปรเจกต์นี้ไม่มีตัวประเมินสูตร
    /// (หมายเหตุเดียวกับ Core/SurvivalState.cs:56-60) ⇒ เดาไม่ได้ จึงให้สำเร็จเสมอไปก่อน
    /// </summary>
    public const float SuccessRate = 1f;

    /// <summary>**ค่าของเรา** — โอกาสสำเร็จยอดเยี่ยม (constants.json → craft_great_success ก็เป็นสูตร)</summary>
    public const float GreatSuccessRate = 0f;

    /// <summary>
    /// **ค่าของเรา** — เพดานเวลาคราฟต์ที่ยอมหน่วงคำตอบ Crafted (วินาที)
    /// กันสูตรที่ข้อมูลเพี้ยนทำให้ผู้เล่นค้างหลอดยาวผิดปกติ (ค่าจริงสูงสุดในไฟล์คือ 18 วิ)
    /// </summary>
    public const float MaxCraftSeconds = 60f;

    /// <summary>
    /// **ค่าของเรา** — ความจุคิวของโต๊ะคราฟต์ที่ตอบไปใน Workbench(3000)
    /// 0 = ยังไม่รองรับการฝากคราฟต์ (ดูหมายเหตุที่ <see cref="Player.HandleGetWorkbenchMsg"/>)
    /// </summary>
    public const uint WorkbenchCapacity = 0u;
}

/// <summary>
/// แพ็กเก็ตเปล่า TypeCode 0 = เครื่องหมาย "เปิด/ปิดชุดคำตอบต่อเนื่อง" ของ seq หนึ่ง
///
/// ทำไมต้องประกาศเอง: ไม่มี message ตัวไหนใน GameCode/Messages ที่ TypeCode = 0
/// (ไล่แล้วทั้งโฟลเดอร์) แต่ client รอรับอยู่จริงที่ Durango.Network/Connection.cs:838-861
/// MessagePacking.Pack&lt;T&gt; ลงทะเบียนชนิดใหม่ให้เองตอนส่งครั้งแรกถ้ามี TypeCode + Pack/Unpack
/// (GameCode/MessagePacking.cs:103-112) ⇒ ประกาศไว้ที่นี่พอ ไม่ต้องแตะไฟล์ที่ generate มา
///
/// client อ่านแค่ header ของแพ็กเก็ตนี้ ไม่แตะ payload เลย (ดู ProcessPacket) — payload ว่างจึงพอ
/// </summary>
public struct ReplySequenceMark
{
    public const uint TypeCode = 0u;

    public static void Pack(Packer packer, ReplySequenceMark val, bool hint = false)
    {
        if (hint)
        {
            packer.PackArrayHeader(1);
            packer.Pack(0u);
        }
        else
        {
            packer.PackArrayHeader(0);
        }
    }

    public static ReplySequenceMark Unpack(Unpacker unpacker) => default;

    public override string ToString() => "<ReplySequenceMark>";
}

// ── ตัวข้อมูลของ data/assets/item/recipes.json ────────────────────────────────────
// ชื่อฟิลด์ snake_case ตรงกับ JSON ⇒ Newtonsoft อ่านตรง ๆ (แบบเดียวกับ Support/YamlArtifact.cs)
// ความหมายของแต่ละฟิลด์ยืนยันจากฝั่ง client ที่อ่านไฟล์เดียวกัน:
//   client/Yaml/Recipe.cs (รูปร่างไฟล์) + client/Crafting/RecipeContainer.cs:63-142 (การแปลงเป็นสูตร)

public class CraftRecipeData
{
    public CraftType type;                                  // 0=Craft 1=Modify 2=Reform (Shared.Item.CraftType)
    public Gettext name;
    public string category;
    public string subcategory;
    public string prototype_id;                             // ของที่ได้ (เฉพาะ type=Craft)
    public int count;                                       // ได้กี่ชิ้นต่อครั้ง
    public int min_level;
    public int max_level;
    public string energy;                                   // ค่าใช้จ่าย energy — ในไฟล์เป็นสตริงตัวเลข
    public string effort;
    public int duration;                                    // วินาทีที่ผู้เล่นยืนทำ (เวลาของหลอด Timer)
    public int duration_wait;                               // วินาทีที่โต๊ะใช้ทำต่อหลังฝาก (entrusted)
    public bool entrusts;                                   // ฝากโต๊ะทำแทนได้ไหม
    public Dictionary<string, int> tool_tags;               // เครื่องมือที่ใช้ได้ (tag → เลเวลขั้นต่ำ)
    public Dictionary<string, int> workbench_tags;          // โต๊ะที่ใช้ได้ (tag → เลเวลขั้นต่ำ)
    public CraftRecipeSlotData[] slots;
    public CraftRecipeOutput[] prototypes;                  // ของที่ได้แบบมีเงื่อนไข (ทับ prototype_id)
    public Shared.Ability.Derived? required_ability;
    public string required_recipe;
}

public class CraftRecipeSlotData
{
    public string slot_id;
    public Gettext slot_name;
    public int count_min;
    public int count_max;
    public Dictionary<string, int> required_tags;           // tag → เลเวลขั้นต่ำ (OR กันภายในกลุ่ม)
    public Dictionary<string, int> required_materials;      // tag → เลเวลขั้นต่ำ (OR กันภายในกลุ่ม)
}

public class CraftRecipeOutput
{
    public string prototype_id;
    public string level;
    public CraftRecipeCriterion[] criteria;
}

public class CraftRecipeCriterion
{
    public string slot_id;
    public string tag_id;
    public string condition;                                // ในไฟล์มีแค่ ">0" (398 จุด) กับ "<0" (2 จุด)
}

/// <summary>
/// ตารางสูตรคราฟต์จาก data/assets/item/recipes.json — โหลดครั้งเดียวตอนถูกเรียกใช้ครั้งแรก
///
/// ไม่ไปเสียบใน Support/DataStore.Load เพราะไฟล์นั้นเป็นของระบบอื่น (แก้พร้อมกันแล้วชนกัน)
/// และ Json.DataDir ถูกตั้งไว้แล้วตั้งแต่ DataStore.Load ⇒ อ่านไฟล์ที่นี่ได้ตรง ๆ
/// </summary>
public static class CraftRecipeStore
{
    private static readonly object Lock = new();
    private static Dictionary<string, CraftRecipeData> _byId;
    private static string[] _craftableIds;

    private static void EnsureLoaded()
    {
        if (_byId != null) return;
        lock (Lock)
        {
            if (_byId != null) return;
            var loaded = Json.ReadFromFile<Dictionary<string, CraftRecipeData>>("item/recipes");
            _byId = loaded ?? new Dictionary<string, CraftRecipeData>();
            _craftableIds = _byId.Where(pair => pair.Value != null && pair.Value.type == CraftType.Craft)
                                 .Select(pair => pair.Key)
                                 .ToArray();
            Console.WriteLine($"[craft] โหลดสูตรคราฟต์ {_byId.Count} รายการ " +
                              $"(ทำของใหม่ได้จริง {_craftableIds.Length}) จาก item/recipes.json");
        }
    }

    public static CraftRecipeData Get(string id)
    {
        EnsureLoaded();
        return string.IsNullOrEmpty(id) ? null : _byId.GetValueOrDefault(id);
    }

    /// <summary>
    /// id ของสูตรที่ "ปลดล็อกให้ผู้เล่น" ใน Recipes(120)
    ///
    /// เอาเฉพาะ type = Craft (625 จาก 720) — สูตร Modify/Reform อีก 95 ตัว (ทอด/ย่าง/ปรับปรุงอุปกรณ์)
    /// ต้องแก้ของเดิมทั้งชิ้น (เพิ่ม tag/ช่องปรับปรุง/สี) ซึ่งเซิร์ฟยังทำไม่ได้
    /// ⚠️ เหตุผลที่ต้องคัดออกตั้งแต่ตอนนี้ ไม่ใช่ปล่อยให้กดแล้วค่อยปฏิเสธ: เกมไม่แสดงข้อความ
    /// ใด ๆ เมื่อการคราฟต์ถูก Abort — .Rest() แค่หยุดหลอดเงียบ ๆ (client/CraftSystem.cs:206-214)
    /// ⇒ โชว์สูตรที่ทำไม่ได้ = ปุ่มกดแล้วไม่เกิดอะไรโดยไม่บอกเหตุผล
    /// </summary>
    public static string[] CraftableIds()
    {
        EnsureLoaded();
        return _craftableIds;
    }
}

public partial class Player
{
    /// <summary>
    /// สูตรที่ผู้เล่นกด "ถูกใจ" — **อยู่ในหน่วยความจำต่อ connection เท่านั้น**
    ///
    /// ⚠️ ของจริงต้องเก็บลงไฟล์เซฟ แต่ PlayerContext ยังไม่มีช่องไว้ให้ (Core/PlayerContext.cs)
    /// และไฟล์นั้นเป็นของระบบอื่น ⇒ ต่อใหม่แล้วรายการถูกใจหาย (ไม่กระทบการคราฟต์)
    /// </summary>
    private readonly HashSet<string> _likedRecipes = new();

    private readonly HashSet<string> _likedBlueprints = new();

    /// <summary>
    /// นาฬิกาที่รอส่ง Crafted ของสูตรที่มี duration &gt; 0 — เก็บไว้เพื่อ Dispose ตอนตัดการเชื่อมต่อ
    ///
    /// ⚠️ ทำไมต้องใช้ threading timer แทนการนับในลูปหลัก: ลูป 120 รอบ/วินาทีเรียก
    /// <see cref="Process"/> ซึ่งอยู่ใน Core/Player.cs — ไฟล์ที่ระบบนี้แตะไม่ได้ (มี agent อื่นทำงาน
    /// พร้อมกัน) จึงไม่มีจุดเสียบงานรายเฟรม
    /// **สิ่งเดียวที่ callback ทำคือ <c>_connection.Send</c>** ซึ่งปลอดภัยข้ามเธรดโดยตัวมันเอง
    /// (GameCode/Durango.Online/Connection.cs:146 ล็อก _sendLock ทั้งก้อน แล้วแค่เขียนลงบัฟเฟอร์
    ///  ส่วนการยิงออก socket ยังทำโดยลูปหลักที่ StartSend เหมือนเดิม)
    /// การแก้ inventory/context ทั้งหมดทำเสร็จตั้งแต่ตอนรับ Craft บนเธรดหลักแล้ว
    /// </summary>
    private readonly List<System.Threading.Timer> _craftTimers = new();

    private void RegisterCraftingHandlers()
    {
        // ── รายการสูตร ──────────────────────────────────────────────────────────────
        // ⚠️ ตัวนี้ทับ handler เดิมที่ Core/Player.cs:178-181 ซึ่งตอบ Recipes เปล่า ๆ
        // ทับได้เพราะ Connection.RegisterMessageHandlerToRegistry ลบ key เดิมก่อนใส่ใหม่
        // (GameCode/Durango.Online/Connection.cs:198-202) และ RegisterSystemHandlers ถูกเรียก
        // ท้าย constructor (Core/Player.cs:522) = หลัง handler เดิมทุกตัว
        _connection.Recv(delegate(GetRecipes msg, PacketHeader header)
        {
            SendRecipes(header.Seq);
        });
        // กดหัวใจที่สูตร — client รอ Recipes ชุดใหม่ทั้งชุดกลับมาที่ seq เดิม
        // (client/RecipeSystem.cs:195-209 LikeRecipe → .On<Recipes>)
        _connection.Recv(delegate(SetRecipeLike msg, PacketHeader header)
        {
            if (!string.IsNullOrEmpty(msg.RecipeId))
            {
                if (msg.Like) _likedRecipes.Add(msg.RecipeId);
                else _likedRecipes.Remove(msg.RecipeId);
            }
            SendRecipes(header.Seq);
        });
        // กดหัวใจที่พิมพ์เขียว — รอ ArtifactBlueprints ชุดใหม่ (client/RecipeSystem.cs:211-225)
        _connection.Recv(delegate(SetBlueprintLike msg, PacketHeader header)
        {
            if (!string.IsNullOrEmpty(msg.BlueprintId))
            {
                if (msg.Like) _likedBlueprints.Add(msg.BlueprintId);
                else _likedBlueprints.Remove(msg.BlueprintId);
            }
            Send(new ArtifactBlueprints
            {
                Ids = CraftableBlueprintIds(),
                LikedBlueprintIds = _likedBlueprints.ToArray(),
                NewBlueprintIds = Array.Empty<string>()
            }, header.Seq);
        });

        // ── คราฟต์จริง ──────────────────────────────────────────────────────────────
        _connection.Recv(delegate(Craft msg, PacketHeader header)
        {
            HandleCraftMsg(msg, header.Seq);
        });
        // หน้าต่างคราฟต์ยิงถามผลลัพธ์ที่คาดว่าจะได้ทุกครั้งที่เปลี่ยนวัตถุดิบ
        // (client/Durango.UI/CraftGroupBase.cs:348 → CraftSlotContainer.GetEstimation)
        // ไม่ตอบก็ไม่ค้าง (มี .Rest ที่ CraftSystem.cs:225) แต่ช่องผลลัพธ์จะว่างเปล่า
        _connection.Recv(delegate(EstimateCraft msg, PacketHeader header)
        {
            HandleEstimateCraftMsg(msg, header.Seq);
        });
        // สถานะคิวของโต๊ะคราฟต์ — ยังไม่มีระบบฝากคราฟต์ จึงตอบคิวว่าง (ห้ามเงียบ ดูหมายเหตุในเมธอด)
        _connection.Recv(delegate(GetWorkbench msg, PacketHeader header)
        {
            HandleGetWorkbenchMsg(msg, header.Seq);
        });
        // ยกเลิก/เร่งงานที่ฝากไว้บนโต๊ะ — เซิร์ฟไม่เคยรับฝาก จึงไม่มีงานให้ยกเลิก
        // ทั้งสองตัวมี .All(...) รอผลอยู่ (client/CraftSystem.cs:286-318) ⇒ ต้องตอบ ไม่งั้นปุ่มค้าง
        // Abort ทำให้ Packet.IsSuccess = false ⇒ client รู้ว่าไม่สำเร็จ (GameCode/.../Packet.cs:116-127)
        _connection.Recv(delegate(CancelCrafting msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่รองรับการฝากคราฟต์ไว้ที่โต๊ะ" }, header.Seq);
        });
        _connection.Recv(delegate(SkipEntrustedCraft msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่รองรับการฝากคราฟต์ไว้ที่โต๊ะ" }, header.Seq);
        });
        // ย้อมสี/ฟอกสี — ใช้ท่อเดียวกับการคราฟต์ (client/CraftSystem.cs:258-284 Dyeing เรียก
        // RegisterPostCraftEvents ตัวเดียวกัน) ⇒ **ไม่ตอบ = หลอดคราฟต์ค้างจนเต็มแล้วไม่มีอะไรเกิด**
        // ยังทำไม่ได้จริงเพราะการย้อมต้องคำนวณสีจากตาราง .raw ที่ไม่มีบนเซิร์ฟ
        // (Support/ItemIconTex.cs พอร์ตมาแบบย่อ) ⇒ ตอบ Abort ให้ .Rest หยุดหลอดสะอาด ๆ
        RecvFallback(delegate(Dye msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่รองรับการย้อมสี" }, header.Seq);
        });
        RecvFallback(delegate(Bleach msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่รองรับการฟอกสี" }, header.Seq);
        });
        // ปรับปรุงอุปกรณ์ (reform) — client รอเฉพาะ OK (client/CraftSystem.cs:110) ไม่มี .Rest
        // ตอบ Abort = ปุ่มไม่ทำอะไร ซึ่งตรงกับสภาพจริง ดีกว่าปล่อยเงียบให้ดูเหมือนเซิร์ฟแฮงก์
        RecvFallback(delegate(RequestTechSupport msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่รองรับการปรับปรุงอุปกรณ์" }, header.Seq);
        });

        _connection.ConnetionClosed += ClearCraftTimers;
    }

    /// <summary>
    /// ลงทะเบียน handler แบบ "ตัวสำรอง" — ใส่ให้เฉพาะเมื่อยังไม่มีระบบไหนรับ message นี้
    ///
    /// ทำไมต้องมี: <c>Connection.Recv</c> ลบ handler เดิมทิ้งแล้วใส่ตัวใหม่เสมอ
    /// (GameCode/Durango.Online/Connection.cs:198-202) ⇒ message ที่คาบเกี่ยวหลายระบบ
    /// (เช่น <c>Dye</c> ที่อยู่ทั้งหมวดคราฟต์และหมวดของ/กระเป๋าใน docs/protocol-coverage.md)
    /// ตัวที่ลงทีหลังจะทับของจริงที่อีกระบบทำไว้แล้ว ⇒ ตัวสำรองต้องหลบให้ของจริงเสมอ
    /// </summary>
    private void RecvFallback<T>(Connection.MessageHandler<T> handler)
    {
        var field = typeof(T).GetField("TypeCode",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        if (field == null || !field.IsLiteral) return;
        if (_connection.HasHandler((uint)field.GetValue(null))) return;
        _connection.Recv(handler);
    }

    // ── รายการสูตร/พิมพ์เขียว ────────────────────────────────────────────────────────

    /// <summary>
    /// ตอบ Recipes(120) — client เอา Ids ไปตั้งว่าสูตรไหน "ปลดล็อกแล้ว"
    /// (client/RecipeSystem.cs:135-143 → RecipeContainer.SetAvailableList)
    ///
    /// [7 ก.ย. 2026] **ปลดตามสกิลจริงแล้ว** — เดิมส่งทุกสูตร (625) เพราะตอนนั้น GetSkills ตอบชุดว่าง
    /// ตอนนี้ Player.Skills.cs ทับ GetSkills ด้วยของจริง ⇒ เงื่อนไขที่ต้องลัดหายไปแล้ว
    /// เส้นทาง: skills.json → rewards[] → rewards.json → recipe_ids (เส้นเดียวกับ client)
    ///
    /// ชุดที่เปิดตลอด = สูตรที่ไม่มีรางวัลสกิลไหนปลดเลย (88 ตัว ดู SkillDataStore.RecipesWithoutSkill)
    /// </summary>
    private void SendRecipes(uint replyOf)
    {
        HashSet<string> unlocked = UnlockedRecipeIds();
        var ids = new List<string>();
        foreach (string id in CraftRecipeStore.CraftableIds())
        {
            if (unlocked.Contains(id)) ids.Add(id);
        }
        Console.WriteLine($"[craft] {Short(EntityId)} ส่งสูตรที่ปลดแล้ว {ids.Count} จาก " +
                          $"{CraftRecipeStore.CraftableIds().Length} สูตร (ReplyOf={replyOf})");
        Send(new Recipes
        {
            Ids = ids.ToArray(),
            LikedRecipeIds = _likedRecipes.ToArray(),
            NewRecipeIds = Array.Empty<string>()
        }, replyOf);
    }

    /// <summary>พิมพ์เขียวที่โชว์ในโหมดสร้าง — เกณฑ์เดียวกับ Core/Player.cs:1175-1186</summary>
    private static string[] CraftableBlueprintIds()
    {
        var list = new List<string>();
        foreach (MergedBlueprint blueprint in BlueprintStore.GetAllBlueprints())
        {
            if (blueprint.IsShowCraftMode) list.Add(blueprint.Id);
        }
        return list.ToArray();
    }

    // ── คราฟต์ ──────────────────────────────────────────────────────────────────────

    private void HandleCraftMsg(Craft msg, uint seq)
    {
        CraftRecipeData recipe = CraftRecipeStore.Get(msg.RecipeId);
        if (recipe == null)
        {
            Send(new Abort { Text = "ไม่รู้จักสูตรนี้" }, seq);
            return;
        }
        // Modify(1)/Reform(2) ไม่ได้ "สร้างของใหม่" แต่ไปแก้ของเดิม (เพิ่ม tag/ช่องปรับปรุง)
        // ซึ่งต้องมีระบบ ModifiableCount/ReformSlots ที่เซิร์ฟยังไม่ทำ ⇒ ปฏิเสธตรง ๆ ดีกว่ากินของ
        if (recipe.type != CraftType.Craft)
        {
            Send(new Abort { Text = "ยังไม่รองรับสูตรประเภทดัดแปลง/ปรับปรุง" }, seq);
            return;
        }
        if (!CheckWorkbench(recipe, msg.Workbench, out string workbenchError))
        {
            Send(new Abort { Text = workbenchError }, seq);
            return;
        }
        if (!ResolveMaterials(recipe, msg.Materials, out List<Item> materials, out string materialError))
        {
            Send(new Abort { Text = materialError }, seq);
            return;
        }
        if (!CheckTool(recipe, msg.ToolItemId, out string toolError))
        {
            Send(new Abort { Text = toolError }, seq);
            return;
        }

        Item[] products = MakeProducts(recipe, msg.Materials, materials);
        if (products.Length == 0)
        {
            Send(new Abort { Text = "สูตรนี้ไม่มีของที่ผลิตได้" }, seq);
            return;
        }

        // ── ตั้งแต่บรรทัดนี้ถือว่าคราฟต์สำเร็จแล้ว: หักของ → เติมของ → ค่อยบอกผล ──
        // ลำดับสำคัญ: client/InventorySystem.cs:605-616 OnCraftSucceed หาไอเทมจาก **กระเป๋า**
        // ด้วย id ที่มากับ Crafted แล้วติดธง "ของใหม่" ⇒ InventoryUpdated ต้องถึงก่อน Crafted
        string[] consumedIds = materials.Select(item => item.Id).ToArray();
        _context.InventoryItems.RemoveAll(item => consumedIds.Contains(item.Id));
        Send(new InventoryUpdated { EntityId = EntityId, RemovedItemIds = consumedIds });
        AddItems(products);                                   // เข้ากระเป๋า + OnContextChanged (เซฟ)
        Send(new InventoryUpdated { EntityId = EntityId, Items = products });

        // [7 ก.ย. 2026] ให้ exp ตอนหักของ+เติมของแล้ว — ไม่รอ Timer/FinishCraft
        // (inventory เปลี่ยนตั้งแต่ตรงนี้แล้ว ถ้าให้ตอนส่ง Crafted จะซ้ำ/ช้าโดยใช่เหตุ)
        AddExpForAction(SkillTuning.CraftWeight, MapRecipeSkillCategory(recipe.category),
                        $"คราฟต์ {msg.RecipeId}");

        SpendCraftEnergy(recipe);

        var crafted = new Crafted
        {
            Result = Result.Success,
            ActionInfo = MakeActionInfo(recipe, products[0].Level),
            Items = products
        };

        // เวลาคราฟต์จากไฟล์จริง (recipes.json → duration) — 424 สูตรจาก 625 เป็น 0 = เสร็จทันที
        // [7 ก.ย. 2026] คูณตัวคูณจากสกิลหมวดคราฟต์ที่เก่งที่สุด (ดู Player.SkillEffects.cs)
        // สูตรที่ duration = 0 อยู่แล้วก็ยังเป็น 0 (คูณแล้วไม่เปลี่ยน) ⇒ ไม่กระทบของที่เสร็จทันที
        float duration = Math.Clamp(recipe.duration * CraftDurationScale(), 0f, CraftTuning.MaxCraftSeconds);

        // เปิดชุดคำตอบต่อเนื่อง แล้วบอกเวลาจริงของหลอดทันที (ไม่งั้น client ใช้ Ping+10 วิ ค้างไว้)
        Send(default(ReplySequenceMark), seq);
        Send(new Messages.Timer { Duration = duration }, seq);
        if (duration <= 0f)
        {
            FinishCraft(crafted, seq);
            return;
        }
        ScheduleCraftFinish(crafted, seq, duration);
    }

    /// <summary>ส่งผลคราฟต์แล้วปิดชุดคำตอบต่อเนื่องของ seq นั้น (ไม่ปิด = handler ฝั่ง client ค้างค้าง)</summary>
    private void FinishCraft(Crafted crafted, uint seq)
    {
        Send(crafted, seq);
        Send(default(ReplySequenceMark), seq);
    }

    /// <summary>
    /// นัดส่ง Crafted เมื่อครบเวลา — ดูเหตุผลที่ใช้ threading timer ที่ <see cref="_craftTimers"/>
    /// </summary>
    private void ScheduleCraftFinish(Crafted crafted, uint seq, float duration)
    {
        System.Threading.Timer timer = null;
        timer = new System.Threading.Timer(delegate
        {
            try
            {
                FinishCraft(crafted, seq);        // Send เท่านั้น — ไม่แตะ inventory/context
            }
            catch (Exception e)
            {
                Console.WriteLine($"[craft] ส่งผลคราฟต์ไม่สำเร็จ: {e.Message}");
            }
            finally
            {
                lock (_craftTimers)
                {
                    _craftTimers.Remove(timer);
                }
                timer?.Dispose();
            }
        }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        lock (_craftTimers)
        {
            _craftTimers.Add(timer);
        }
        timer.Change((int)(duration * 1000f), System.Threading.Timeout.Infinite);
    }

    private void ClearCraftTimers()
    {
        lock (_craftTimers)
        {
            foreach (System.Threading.Timer timer in _craftTimers) timer.Dispose();
            _craftTimers.Clear();
        }
    }

    // ── การตรวจเงื่อนไข ─────────────────────────────────────────────────────────────

    /// <summary>
    /// โต๊ะที่ใช้ได้
    ///
    /// ⚠️ ตรวจได้ไม่ครบเท่าของจริง: เกมเทียบ tag ของโต๊ะกับ workbench_tags ของสูตร
    /// (client/Crafting/Recipe.cs:47-68 IsValidWorkbench → Artifact.GetTag) แต่ tag ของ artifact
    /// มาจาก AppearArtifact.Tags ที่เซิร์ฟเป็นคนส่ง (client/Artifact.cs:545-557 SetTagList)
    /// ซึ่งเซิร์ฟนี้ยังไม่เคยเติมให้เลย (Core/Cheats.cs:109-226 สร้าง artifact โดยไม่ตั้ง Tags
    /// และ data/assets/entity_types/artifact.json ก็ไม่มีฟิลด์ tags)
    /// ⇒ ที่นี่ตรวจเท่าที่ข้อมูลจริงยืนยันได้: มีโต๊ะตัวนั้นอยู่จริง และพิมพ์เขียวของมันมี
    ///   component "Workbench" (artifact.json → components) — ดูข้อจำกัดในรายงาน
    /// </summary>
    private bool CheckWorkbench(CraftRecipeData recipe, PropKey? workbench, out string error)
    {
        error = null;
        if (recipe.workbench_tags == null || recipe.workbench_tags.Count == 0) return true;
        if (!workbench.HasValue || string.IsNullOrEmpty(workbench.Value.EntityId))
        {
            error = "สูตรนี้ต้องทำที่โต๊ะ";
            return false;
        }
        AppearArtifact? artifact = _world.ArtifactManager.Get(workbench.Value.EntityId);
        if (!artifact.HasValue)
        {
            error = "ไม่พบโต๊ะที่อ้างถึง";
            return false;
        }
        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(artifact.Value.EntityType);
        if (blueprint?.Components == null || !blueprint.Components.Contains("Workbench"))
        {
            error = "สิ่งปลูกสร้างนี้ไม่ใช่โต๊ะคราฟต์";
            return false;
        }
        return true;
    }

    /// <summary>
    /// เครื่องมือ — tool_tags ที่มีแค่ {"bare_hands": …} แปลว่า "ไม่ต้องใช้เครื่องมือ"
    /// ตามที่ตัวแปลงฝั่งเกมทำ (client/Durango.Logic.Item/OrTagFilter.cs:31-34 คืนตัวกรองว่าง)
    /// เครื่องมือไม่ถูกใช้หมดไป ของจริงเสียความทนทานตาม constants.json → durability.deltas.craft
    /// ซึ่งเซิร์ฟยังไม่มีระบบความทนทานของไอเทม ⇒ ตรวจว่ามีจริงเฉย ๆ
    /// </summary>
    private bool CheckTool(CraftRecipeData recipe, string toolItemId, out string error)
    {
        error = null;
        if (IsEmptyTagFilter(recipe.tool_tags)) return true;
        if (string.IsNullOrEmpty(toolItemId))
        {
            error = "สูตรนี้ต้องใช้เครื่องมือ";
            return false;
        }
        Item? tool = FindInventoryItem(toolItemId);
        if (!tool.HasValue || !MatchesAnyTag(tool.Value, recipe.tool_tags))
        {
            error = "เครื่องมือใช้กับสูตรนี้ไม่ได้";
            return false;
        }
        return true;
    }

    /// <summary>
    /// จับคู่ของในช่องวัตถุดิบกับกระเป๋าจริง แล้วตรวจว่าตรงเงื่อนไขของช่องนั้น
    ///
    /// เกณฑ์เดียวกับฝั่งเกม (client/Durango.Logic.Item/ItemData.cs:405-410 HasTagsAndMaterials):
    ///   required_tags กับ required_materials เป็นคนละกลุ่ม — **ภายในกลุ่มเป็น OR ระหว่างกลุ่มเป็น AND**
    ///   และทั้งสองกลุ่มเทียบกับ "tag ของไอเทม" เหมือนกัน (RecipeContainer.cs:314-315 ยัดทั้งคู่เข้า
    ///   OrTagFilter) พร้อมเงื่อนไขเลเวล tagData.Level &gt;= ที่สูตรขอ (ItemData.cs:606-618)
    ///
    /// จำนวน: count_min..count_max ต่อหนึ่งครั้ง — การคราฟต์หลายชิ้นเกมแยกส่ง Craft ทีละใบอยู่แล้ว
    /// (client/CraftSystem.cs:119-139 MakeCraftQueue วนตาม Quantity)
    /// </summary>
    private bool ResolveMaterials(CraftRecipeData recipe, Dictionary<string, string[]> sent,
                                  out List<Item> materials, out string error)
    {
        materials = new List<Item>();
        error = null;
        var used = new HashSet<string>();
        int slotCount = recipe.slots?.Length ?? 0;
        for (int i = 0; i < slotCount; i++)
        {
            CraftRecipeSlotData slot = recipe.slots[i];
            string[] ids = null;
            sent?.TryGetValue(slot.slot_id ?? string.Empty, out ids);
            int given = ids?.Length ?? 0;
            if (given < slot.count_min || (slot.count_max > 0 && given > slot.count_max))
            {
                error = $"จำนวนวัตถุดิบช่อง {slot.slot_id} ไม่ถูกต้อง";
                return false;
            }
            for (int j = 0; j < given; j++)
            {
                string itemId = ids[j];
                // ห้ามใช้ไอเทมชิ้นเดียวกันซ้ำสองช่อง — ไม่งั้นคราฟต์ได้โดยแทบไม่เสียของ
                if (string.IsNullOrEmpty(itemId) || !used.Add(itemId))
                {
                    error = "วัตถุดิบซ้ำหรือไม่ถูกต้อง";
                    return false;
                }
                Item? item = FindInventoryItem(itemId);
                if (!item.HasValue)
                {
                    error = "ไม่พบวัตถุดิบในกระเป๋า";
                    return false;
                }
                if (!MatchesSlot(item.Value, slot))
                {
                    error = $"วัตถุดิบไม่ตรงเงื่อนไขช่อง {slot.slot_id}";
                    return false;
                }
                materials.Add(item.Value);
            }
        }
        return true;
    }

    private static bool MatchesSlot(Item item, CraftRecipeSlotData slot)
    {
        bool tagsOk = IsEmptyTagFilter(slot.required_tags) || MatchesAnyTag(item, slot.required_tags);
        bool materialsOk = IsEmptyTagFilter(slot.required_materials) || MatchesAnyTag(item, slot.required_materials);
        return tagsOk && materialsOk;
    }

    /// <summary>
    /// ตัวกรองว่าง = ไม่บังคับอะไร — รวมกรณีพิเศษ {"bare_hands": …} เดี่ยว ๆ ที่ฝั่งเกมตีเป็นว่าง
    /// (client/Durango.Logic.Item/OrTagFilter.cs:31-34)
    /// </summary>
    private static bool IsEmptyTagFilter(Dictionary<string, int> filter)
    {
        if (filter == null || filter.Count == 0) return true;
        return filter.Count == 1 && filter.ContainsKey("bare_hands");
    }

    private static bool MatchesAnyTag(Item item, Dictionary<string, int> filter)
    {
        if (item.Tags == null) return false;
        foreach (var required in filter)
        {
            foreach (Messages.Tag tag in item.Tags)
            {
                if (tag.Id == required.Key && tag.Level >= required.Value) return true;
            }
        }
        return false;
    }

    /// <summary>ของในกระเป๋าที่ "ใช้ได้" — ตัดของที่ใส่อยู่ออก เหมือน ItemSlot.IsSuitableItem (!IsEquipments)</summary>
    private Item? FindInventoryItem(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        if (_context.EquippedItems != null && _context.EquippedItems.ContainsValue(itemId)) return null;
        int index = _context.InventoryItems.FindIndex(item => item.Id == itemId);
        return index < 0 ? null : _context.InventoryItems[index];
    }

    // ── ผลลัพธ์ ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ของที่ได้จากสูตร — prototype_id เป็นค่าเริ่มต้น และ prototypes[] ทับได้ตามวัตถุดิบที่ใส่
    /// (ในไฟล์มี 85 สูตรที่มี prototypes เช่น needle → needle_bone เมื่อช่อง main เป็นของที่มี tag "bone")
    /// </summary>
    private static Item[] MakeProducts(CraftRecipeData recipe, Dictionary<string, string[]> sent,
                                       List<Item> materials)
    {
        string prototypeId = ResolvePrototypeId(recipe, sent, materials);
        if (string.IsNullOrEmpty(prototypeId)) return Array.Empty<Item>();
        int level = ProductLevel(recipe, materials);
        int count = Math.Max(1, recipe.count);
        var list = new List<Item>(count);
        for (int i = 0; i < count; i++)
        {
            Item? item = Cheats.MakeItem(prototypeId, level);   // ตัวสร้างไอเทมชุดเดียวกับที่ cheat "it" ใช้
            if (item.HasValue) list.Add(item.Value);
        }
        return list.ToArray();
    }

    private static string ResolvePrototypeId(CraftRecipeData recipe, Dictionary<string, string[]> sent,
                                             List<Item> materials)
    {
        if (recipe.prototypes != null)
        {
            foreach (CraftRecipeOutput output in recipe.prototypes)
            {
                if (output?.criteria == null || string.IsNullOrEmpty(output.prototype_id)) continue;
                bool all = true;
                foreach (CraftRecipeCriterion criterion in output.criteria)
                {
                    if (!MatchesCriterion(criterion, sent, materials)) { all = false; break; }
                }
                if (all) return output.prototype_id;
            }
        }
        return recipe.prototype_id;
    }

    /// <summary>
    /// เงื่อนไขทับ prototype — ในไฟล์จริงมีแค่ ">0" (398 จุด) กับ "<0" (2 จุด)
    /// **การตีความเป็นของเรา**: ">0" = ช่องนั้นมีของที่ติด tag นี้อย่างน้อยหนึ่งชิ้น · "&lt;0" = ไม่มีเลย
    /// (จำนวนติดลบเป็นไปไม่ได้ จึงอ่านว่า "ตรงข้าม") — ยืนยันจากซอร์สเกมไม่ได้เพราะฝั่ง client
    /// ไม่ได้อ่านฟิลด์ prototypes เลย (client/Yaml/Recipe.cs ไม่มีฟิลด์นี้) มันเป็นข้อมูลของเซิร์ฟล้วน ๆ
    /// </summary>
    private static bool MatchesCriterion(CraftRecipeCriterion criterion, Dictionary<string, string[]> sent,
                                         List<Item> materials)
    {
        if (criterion == null || string.IsNullOrEmpty(criterion.tag_id)) return false;
        string[] ids = null;
        sent?.TryGetValue(criterion.slot_id ?? string.Empty, out ids);
        bool found = false;
        if (ids != null)
        {
            foreach (string id in ids)
            {
                Item item = materials.FirstOrDefault(m => m.Id == id);
                if (item.Tags == null) continue;
                if (item.Tags.Any(tag => tag.Id == criterion.tag_id)) { found = true; break; }
            }
        }
        return criterion.condition == "<0" ? !found : found;
    }

    /// <summary>
    /// เลเวลของที่ได้ — **ค่าของเรา**: เฉลี่ยเลเวลวัตถุดิบ แล้วบีบเข้าช่วง min_level..max_level ของสูตร
    /// ของจริงคำนวณจากความสามารถของผู้เล่น + คุณภาพวัตถุดิบผ่านสูตรใน constants.json ซึ่งเซิร์ฟ
    /// ยังประเมินไม่ได้ (ดูหมายเหตุที่ <see cref="CraftTuning.SuccessRate"/>)
    /// ใช้ค่าเฉลี่ยเพราะเป็นตัวเดียวที่ผูกกับ "ของที่ใส่จริง" โดยไม่ต้องเดาสูตร
    /// </summary>
    private static int ProductLevel(CraftRecipeData recipe, List<Item> materials)
    {
        int level = recipe.min_level > 0 ? recipe.min_level : 1;
        if (materials.Count > 0)
        {
            level = (int)Math.Round(materials.Average(item => (double)item.Level));
        }
        int min = recipe.min_level > 0 ? recipe.min_level : 1;
        int max = recipe.max_level > 0 ? recipe.max_level : min;
        return Math.Clamp(level, min, Math.Max(min, max));
    }

    /// <summary>
    /// แปลงสตริง <c>recipes.json → category</c> เป็นหมวดสกิลสำหรับให้ exp
    /// ไม่รู้จัก → null = ได้แค่เลเวลตัวละคร ไม่ได้ exp หมวด (ไม่เดา)
    /// </summary>
    private static Shared.Skill.Category? MapRecipeSkillCategory(string category)
    {
        if (string.IsNullOrEmpty(category))
        {
            return null;
        }
        if (category.StartsWith("cook", StringComparison.OrdinalIgnoreCase))
        {
            return Shared.Skill.Category.Cooking;
        }
        if (category.StartsWith("clothing", StringComparison.OrdinalIgnoreCase))
        {
            return Shared.Skill.Category.Armorcrafting;
        }
        if (category.StartsWith("material_process", StringComparison.OrdinalIgnoreCase) ||
            category.StartsWith("process", StringComparison.OrdinalIgnoreCase))
        {
            return Shared.Skill.Category.Process;
        }
        if (category.StartsWith("weapon", StringComparison.OrdinalIgnoreCase) ||
            category.StartsWith("tool", StringComparison.OrdinalIgnoreCase))
        {
            return Shared.Skill.Category.Weaponcrafting;
        }
        if (category.StartsWith("modular", StringComparison.OrdinalIgnoreCase) ||
            category.StartsWith("build", StringComparison.OrdinalIgnoreCase) ||
            category.StartsWith("construct", StringComparison.OrdinalIgnoreCase))
        {
            return Shared.Skill.Category.Constructing;
        }
        return null;
    }

    /// <summary>
    /// ActionInfo ที่ติดไปกับ Crafted — เกมใช้จริงเฉพาะตอนล้มเหลว
    /// (client/Durango.UI/CraftGroupBase.cs:719-732 OnFailCraft → Util.ActionInfoDetailString)
    /// RelatedCategory = Invalid เพราะสูตรเก็บหมวดเป็นสตริง (category) ไม่ใช่ Shared.Skill.Category
    /// และไม่มีตารางแปลงในข้อมูล ⇒ ไม่เดา (Invalid เป็นค่าที่ ActionInfo.Unpack รองรับอยู่แล้ว)
    /// </summary>
    private static ActionInfo MakeActionInfo(CraftRecipeData recipe, int level)
    {
        return new ActionInfo
        {
            ActionLevel = level,
            PotentialLevel = recipe.max_level,
            RelatedCategory = Shared.Skill.Category.Invalid,
            SuccessRatio = CraftTuning.SuccessRate,
            RelatedAbility = recipe.required_ability ?? Shared.Ability.Derived.Invalid
        };
    }

    /// <summary>
    /// หัก energy ตามที่สูตรกำหนด (recipes.json → energy เก็บเป็นสตริงตัวเลข เช่น "9")
    ///
    /// ⚠️ ไม่ส่ง EnergyWarning(3648) แม้ energy จะไม่พอ: การเตือนของเกมเป็นการ "หยุดรอคำยืนยัน"
    /// กลางคัน (client/CraftSystem.cs:189-205 LowEnergyWarning.Show แล้วรอผู้เล่นกด) ซึ่งต้องมี
    /// ฝั่งเซิร์ฟค้างคำขอไว้รอคำตอบ — ยังไม่ได้ทำ ถ้าส่งไปแล้วไม่รับคำตอบต่อ หลอดจะค้างถาวร
    /// </summary>
    private void SpendCraftEnergy(CraftRecipeData recipe)
    {
        if (!float.TryParse(recipe.energy, NumberStyles.Float, CultureInfo.InvariantCulture, out float energy)
            || energy <= 0f)
        {
            return;
        }
        _survival.Add(SurvivalState.KeyEnergy, -energy);
        FlushSurvival();     // ค่ากระโดด ⇒ ต้องส่งเส้นใหม่ทันที ไม่รอรอบตรวจ (ดู Core/SurvivalState.cs)
    }

    // ── ผลลัพธ์ที่คาดว่าจะได้ (หน้าต่างคราฟต์) ────────────────────────────────────────

    private void HandleEstimateCraftMsg(EstimateCraft msg, uint seq)
    {
        CraftRecipeData recipe = CraftRecipeStore.Get(msg.RecipeId);
        if (recipe == null || recipe.type != CraftType.Craft)
        {
            // มี .Rest รออยู่ (client/CraftSystem.cs:225-231) ⇒ ตอบ Abort แล้วช่องผลลัพธ์ขึ้น "-"
            Send(new Abort { Text = "ประเมินผลสูตรนี้ไม่ได้" }, seq);
            return;
        }
        // ประเมินจากของที่ใส่ไว้ "เท่าที่ใส่แล้ว" — ตอนกำลังเลือกวัตถุดิบยังไม่ครบก็ถามมาแล้ว
        // (CraftSlotContainer.GetEstimation ส่ง canFinished:false) ⇒ ห้ามปฏิเสธเพราะของไม่ครบ
        var picked = new List<Item>();
        if (msg.Materials != null)
        {
            foreach (var pair in msg.Materials)
            {
                if (pair.Value == null) continue;
                foreach (string id in pair.Value)
                {
                    Item? item = FindInventoryItem(id);
                    if (item.HasValue) picked.Add(item.Value);
                }
            }
        }
        string prototypeId = ResolvePrototypeId(recipe, msg.Materials, picked);
        Prototype prototype = string.IsNullOrEmpty(prototypeId) ? null : PrototypeYaml.GetItemPrototype(prototypeId);
        if (prototype == null)
        {
            Send(new Abort { Text = "ไม่รู้จักของที่สูตรนี้ผลิต" }, seq);
            return;
        }
        int level = ProductLevel(recipe, picked);
        var tags = new Dictionary<string, int>();
        if (prototype.Tags != null)
        {
            foreach (var tag in prototype.Tags) tags[tag.Key] = level;
        }
        Send(new CraftEstimationInfo
        {
            CraftLevel = level,
            CraftEstimation = new CraftEstimation
            {
                PrototypeId = prototypeId,
                Level = level,
                Name = prototype.Name,
                // (ค่าปัจจุบัน, ค่าสูงสุด) — ของที่เพิ่งคราฟต์เต็มหลอดเสมอ ตรงกับ Cheats.MakeItem
                // ที่ตั้ง Durability = Gauge(1, 0, [node(0,1)]) (Core/Cheats.cs:31)
                Durability = new Vector2(1f, 1f),
                Tags = tags,
                UnrevealedRareTagCount = 0,
                ModifiableCount = 0,
                SuccessRate = CraftTuning.SuccessRate,
                GreatSuccessRate = CraftTuning.GreatSuccessRate,
                RequiredAbilityValue = 0f
            }
        }, seq);
    }

    // ── โต๊ะคราฟต์ ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// ตอบ Workbench(3000) ให้ GetWorkbench — คิวว่างเสมอ
    ///
    /// ⚠️ ต้องตอบถึงแม้จะว่าง: หน้าต่างคิวของโต๊ะวาดช่องจาก Craftings/Crafteds ที่ได้จาก message นี้
    /// (client/Durango.UI/InteractionMenuListWidgetBase.cs:365,414) ไม่ตอบ = ไอคอนหมุนค้าง
    ///
    /// ยังทำระบบฝากคราฟต์ (entrusted) ไม่ได้เต็มรูปเพราะสถานะคิวของโต๊ะต้องเดินทางไปกับ
    /// Touched.Workbench (GameCode/Messages/Touched.cs:25) ซึ่งประกอบอยู่ใน Core/Player.cs
    /// (HandleTouchMsg) — ไฟล์นอกขอบเขตของระบบนี้ ⇒ ทุกสูตรจึงคราฟต์แบบ "ทำเองกับมือ" หมด
    /// แม้ entrusts = true (200 สูตร) ซึ่งปลอดภัยกว่า: ถ้าตอบ CraftStartedOnWorkbench ไปทั้งที่
    /// ไม่มีคิวจริง ของจะหายเข้าโต๊ะที่ไม่มีใครเปิดได้
    /// </summary>
    private void HandleGetWorkbenchMsg(GetWorkbench msg, uint seq)
    {
        Send(new Workbench
        {
            EntityId = msg.EntityId,
            Capacity = CraftTuning.WorkbenchCapacity,
            Craftings = Array.Empty<Messages.Crafting>(),
            Crafteds = Array.Empty<CraftedResult>()
        }, seq);
    }
}
