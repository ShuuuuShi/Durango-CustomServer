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
        // ยังไม่มีระบบที่แยกไฟล์ — เพิ่มการเรียกที่นี่เมื่อสร้างไฟล์ Player.<ระบบ>.cs
    }
}
