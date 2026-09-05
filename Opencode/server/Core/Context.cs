using JetBrains.Annotations;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/Context.cs
public class Context
{
    [NotNull]
    public readonly WorldContext World;

    [NotNull]
    public readonly PlayerContext Player;

    /// <summary>
    /// สล็อตของตัวละคร — **แก้จากต้นฉบับโดยตั้งใจ**
    ///
    /// ต้นฉบับเขียน <c>World.PlayerSlot</c> ซึ่งถูกในบริบทของ NEXON เพราะเซิร์ฟในตัวเกมเป็น
    /// ผู้เล่นคนเดียว = หนึ่งคนหนึ่งโลก เลขสล็อตของโลกกับของผู้เล่นจึงเป็นตัวเดียวกัน
    ///
    /// เซิร์ฟนี้เป็นหลายคนในโลกเดียว: ทุก Context ใช้ <c>_worldCtx</c> ก้อนเดียวกัน
    /// ซึ่ง <c>Host.cs:186</c> ตั้ง <c>PlayerSlot = 0</c> ไว้ ⇒ ถ้าอ่านจากโลก ทุกคนได้ 0 หมด
    /// ⇒ <c>Host.NextSlot()</c> คืน 1 เสมอ ⇒ **สร้างตัวละครใหม่ทับตัวเดิมในสล็อต 1 ทุกครั้ง**
    /// (เจอของจริงมาแล้ว — ตัวละครในสล็อต 1 หายไปตอนสร้างตัวใหม่)
    ///
    /// เลขที่ถูกอยู่ที่ <c>PlayerContext.PlayerSlot</c> ซึ่งถูกเซฟลงไฟล์และตั้งทุกที่ที่สร้างสล็อต
    /// (<c>Host.cs:203, 421, 433</c>)
    /// </summary>
    public int PlayerSlot => Player.PlayerSlot;

    public string EntityId => Player.EntityId;

    public Context(WorldContext world, PlayerContext player)
    {
        World = world;
        Player = player;
    }
}
