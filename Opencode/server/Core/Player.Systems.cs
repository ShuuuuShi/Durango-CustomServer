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
        RegisterWarpHandlers();        // Player.Warp.cs      — กลับบ้าน/ไปท่าเรือ/กระโดด
        RegisterExplorationHandlers(); // Player.Exploration.cs — ค้นหาจุดสำคัญ/คูลดาวน์เรดาร์
        RegisterMissionHandlers();     // Player.Missions.cs  — ภารกิจกลุ่ม (ตอบโครงว่างให้ระบบเริ่ม)
        RegisterQuestHandlers();       // Player.Quest.cs     — GetQuests/GetQuestState (ทับของ Player.cs)
        RegisterSocialHandlers();      // Player.Social.cs    — ปาร์ตี้/เพื่อน/บันทึก/แคลน/โนมัด/ผู้กลับ/ตลาดหมดประกาศ

        // ── หลังทุกระบบพร้อมแล้ว ─────────────────────────────────────────────────
        ReviveIfDeadOnLogin();         // Player.Hunting.cs   — กันตัวละครค้างตายถาวร
    }
}
