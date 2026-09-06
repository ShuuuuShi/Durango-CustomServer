using Durango.Network;
using Messages;

namespace Durango.Online;

/// <summary>
/// จุดรวมการลงทะเบียน handler ของระบบต่าง ๆ
///
/// ทำไมต้องมีไฟล์นี้: constructor ของ Player ยาวขึ้นเรื่อย ๆ ตามจำนวน handler และเวลาทำ
/// หลายระบบพร้อมกันจะแก้ไฟล์เดียวกันชนกันตลอด ⇒ แต่ละระบบเปิดไฟล์ของตัวเอง
/// (<c>Player.Inventory.cs</c>, <c>Player.Crafting.cs</c>, …) แล้วมาต่อสายที่นี่จุดเดียว
///
/// วิธีเพิ่มระบบใหม่:
///   1. สร้าง <c>Core/Player.&lt;ระบบ&gt;.cs</c> — <c>public partial class Player</c>
///   2. ใส่เมธอด <c>private void Register&lt;ระบบ&gt;Handlers()</c> ที่ลงทะเบียน <c>_connection.Recv(...)</c>
///   3. เรียกจาก <see cref="RegisterSystemHandlers"/> ด้านล่าง
///
/// เรียกจาก constructor ของ Player ก่อนส่ง state ชุดแรก ⇒ handler พร้อมรับก่อนที่ client
/// จะเริ่มยิงคำขอเข้ามา
/// </summary>
public partial class Player
{
    private void RegisterSystemHandlers()
    {
        // [7 ก.ย. 2026] Keepalive (254) — ฝั่งเกมส่งทุก 30 วินาทีแบบไม่รอคำตอบ
        // (client/Durango.Network/Connection.cs:216) ไม่มีผลต่อเกม แต่ถ้าไม่ลงทะเบียน
        // log จะขึ้น "ไม่มี handler สำหรับ type=254" รกทุกครึ่งนาที ⇒ รับเงียบ ๆ
        _connection.Recv(delegate(Keepalive msg, PacketHeader header)
        {
        });

        RegisterInventoryHandlers();   // Player.Inventory.cs — ของ/กระเป๋า/คลัง/ใช้ของ
        RegisterCraftingHandlers();    // Player.Crafting.cs  — คราฟต์/สูตร/โต๊ะคราฟต์
        RegisterCombatHandlers();      // Player.Combat.cs    — ท่าต่อสู้/ความเสียหาย/ตาย-เกิดใหม่
        RegisterAnimalHandlers();      // Player.Animals.cs   — สัตว์/สัตว์เลี้ยง/กรง/ทำให้เชื่อง
        RegisterSkillHandlers();       // Player.Skills.cs    — สกิล/เลเวล/exp (ทับ GetStatistics+GetSkills ของ Player.cs)
        RegisterGatheringHandlers();   // Player.Gathering.cs — เก็บเกี่ยวของธรรมชาติ (ตัดไม้/เก็บพืช/ทุบหิน)
        RegisterCageHandlers();        // Player.Cage.cs      — โรงเลี้ยงสัตว์ (ทับ handler กรงที่ตอบ Abort ไว้)
        RegisterDomesticationHandlers(); // Player.Domestication.cs — ทำให้เชื่อง (ทับ Abort ของ Animals/Inventory)

        RegisterHuntingHandlers();     // Player.Hunting.cs   — จับสัตว์ป่าเป็นบังเหียน
        RegisterTutorialHandlers();    // Player.Tutorial.cs  — คำสั่งจัดฉากของบทเรียนเริ่มเกม
        RegisterPetSaveHandlers();     // Player.PetSave.cs   — โหลด/เซฟสัตว์เลี้ยง+จำนวนครั้งที่ตาย
        RegisterMapHandlers();         // Player.Map.cs       — หมุดจุดสำคัญบนแผนที่ (ทับ handler เดิมใน Player.cs)
        RegisterBuildingHandlers();    // Player.Building.cs  — จองพื้นที่/ใส่วัสดุ/สร้าง/ทำให้สมบูรณ์
        RegisterWarpHandlers();        // Player.Warp.cs      — ตั้งจุดกลับ/กลับบ้าน/วาร์ปไปท่าเรือ

        // ── [6 ก.ย. 2026] รับมาจากงานฝั่ง Opencode (commit 5d9f78a) ──────────────────
        // สามระบบนี้ตอบ "โครงว่างที่ถูกต้อง" ไม่ใช่ข้อมูลปลอม — ฝั่งเกมต้องได้คำตอบถึงจะตั้ง
        // ธง initialized ของตัวเอง ไม่งั้นระบบนั้นค้างสถานะ Disabled ตลอดกาลแบบเงียบ ๆ
        // (เช่น FactionSystem.IsMissionInitialized ตั้งได้ที่เดียวคือตอนรับ MissionInfos)
        RegisterMissionHandlers();     // Player.Missions.cs  — ภารกิจกลุ่ม
        RegisterQuestHandlers();       // Player.Quest.cs     — GetQuests/GetQuestState (ทับของ Player.cs)
        RegisterSocialHandlers();      // Player.Social.cs    — ปาร์ตี้/เพื่อน/บันทึก/แคลน/โนมัด/ผู้กลับ/ตลาด

        // ── [6 ก.ย. 2026] แพ็กเกจ 18 ระบบจาก workflow durango-fill-packages ─────────
        // สแกนแล้วว่าไม่มี TypeCode ซ้ำกับไฟล์เก่าและซ้ำกันเอง (21 จุดที่ซ้ำทั้งหมด
        // เป็นการทับแบบตั้งใจของไฟล์เก่า Cage/Domestication/Skills/Quest กับต้นทางเดิม)
        RegisterS02Handlers();         // Player.S02.cs       — แพ็กเกจ S02 (ประตู/ป้าย/ของสะสม)
        RegisterEstateHandlers();      // Player.Estate.cs    — ที่ดิน/เอสเตท
        RegisterClanHandlers();        // Player.Clan.cs      — แคลน
        RegisterAllyHandlers();        // Player.Ally.cs      — พันธมิตร
        RegisterPartyHandlers();       // Player.Party.cs     — ปาร์ตี้
        RegisterFriendHandlers();      // Player.Friend.cs    — เพื่อน
        RegisterMailHandlers();        // Player.Mail.cs      — ไปรษณีย์
        RegisterFactionHandlers();     // Player.Faction.cs   — แฟกชัน
        RegisterQuestFlowHandlers();   // Player.QuestFlow.cs — เควสไกด์/สายเควส
        RegisterMarketHandlers();      // Player.Market.cs    — ตลาดวัสดุ
        RegisterShopHandlers();        // Player.Shop.cs      — ร้านค้า
        RegisterTravelHandlers();      // Player.Travel.cs    — เดินทางข้ามเกาะ/ท่าเรือ
        RegisterResearchHandlers();    // Player.Research.cs  — วิจัย
        RegisterMusicHandlers();       // Player.Music.cs     — เครื่องดนตรี
        RegisterRepairHandlers();      // Player.Repair.cs    — ซ่อมของ
        RegisterFarmHandlers();        // Player.Farm.cs      — ไร่/ปลูกพืช
        RegisterVehicleHandlers();     // Player.Vehicle.cs   — ยานพาหนะ
        RegisterEventHandlers();       // Player.Event.cs     — อีเวนต์
        RegisterLifeHandlers();        // Player.Life.cs      — ชีวิตประจำวัน (กินน้ำ/อาบน้ำ/ฟื้นคืนชีพ/ฉายา/คลังของ/เครื่องประดับ)

        // ── หลังทุกระบบพร้อมแล้ว ─────────────────────────────────────────────────
        ReviveIfDeadOnLogin();         // Player.Hunting.cs   — กันตัวละครค้างตายถาวร
    }
}
