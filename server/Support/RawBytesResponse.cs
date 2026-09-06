using System.IO;
using System.Net;

namespace Durango.Online;

/// <summary>
/// เสิร์ฟ byte[] ตรง ๆ พร้อม Content-Length ที่แม่น (มี <see cref="DirectLength"/>)
/// ใช้สำหรับไฟล์หน้าแอดมินที่ต้องการ Content-Type ถูกต้อง (text/html, text/css, image/png ฯลฯ)
///
/// ═══ ทำไมอยู่ไฟล์นี้ ไม่ได้อยู่ใน WebServer.cs ═══
/// <c>server/GameCode/**</c> เป็นโค้ดต้นฉบับของ NEXON — กฎเหล็กของโปรเจกต์คือห้ามแก้
/// และคลาสนี้เป็นของ "หน้าแอดมิน" ซึ่งเป็นของที่เราเพิ่มเอง ไม่ใช่ส่วนหนึ่งของโปรโตคอลเกม
///
/// ทำแบบนี้ได้เพราะต้นฉบับเปิดทางไว้ให้อยู่แล้ว (ไม่ต้องแก้อะไรเลยสักบรรทัด):
///   • <c>WebServer</c> เป็น <c>public class</c> ใน namespace <c>Durango.Online</c> เดียวกัน
///   • <c>WebServer.Response</c> เป็น <c>public abstract class</c>
///   • <c>DirectLength</c> เป็น <c>public virtual</c> · <c>Write</c> เป็น <c>public abstract</c>
/// (ยืนยันที่ GameCode/Durango.Online/WebServer.cs:16-58 — คลาสพี่น้องอย่าง
///  <c>RedirectResponse</c> / <c>NotModifiedResponse</c> ก็สืบทอดแบบเดียวกันนี้)
///
/// ⚠️ ต้องมี <see cref="DirectLength"/> — ถ้าไม่มี <c>WebServer.Process()</c> จะบัฟเฟอร์
/// ทั้งก้อนลง MemoryStream แล้ว ToArray() ซ้ำอีกรอบ (เหตุผลเต็มอยู่ที่คอมเมนต์ของ
/// <c>Response.DirectLength</c> ในไฟล์ต้นฉบับ)
/// </summary>
public sealed class RawBytesResponse : WebServer.Response
{
    private readonly byte[] _content;

    public RawBytesResponse(byte[] content, string contentType)
    {
        _content = content;
        ContentType = contentType;
        StatusCode = HttpStatusCode.OK;
    }

    public override long? DirectLength => _content.Length;

    public override void Write(Stream stream)
    {
        stream.Write(_content, 0, _content.Length);
    }
}
