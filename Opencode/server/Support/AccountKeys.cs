using System;

namespace Durango.Online;

/// <summary>
/// กุญแจบัญชี — ตัวที่บอกว่า "ตัวละครตัวนี้เป็นของใคร"
///
/// ═══ ทำไมต้องมี ═══
/// เดิมเซิร์ฟนี้ **ไม่มีแนวคิดเรื่องบัญชีอยู่ในโค้ดเลยแม้แต่คำเดียว** (<c>grep account_id server/</c> = 0 hit)
/// ⇒ <c>POST /accounts</c> แจกรายชื่อตัวละครทุกตัวบนเซิร์ฟให้ใครก็ได้ แล้วหน้าเลือกตัวละครในเกม
/// เอามาทำเป็นปุ่มกดเข้าเล่นได้ตรง ๆ ⇒ **ผู้เล่นคนที่ 2 เปิดเกมจะเห็นตัวละครของคนที่ 1
/// อยู่ในสล็อตตัวเอง กดเข้าเล่นได้ทันที** ไม่ต้องแฮก ไม่ต้องใช้เครื่องมือ
///
/// ═══ กุญแจมาจากไหน ═══
/// ตัวเกมส่ง <c>account_id</c> มากับ <c>/sessions</c> และ <c>/accounts</c> อยู่แล้วทุกคำขอ
/// (client/Durango.System/Platform.cs:118 <c>BuildSessionForm</c> ใส่ <c>NPSN</c> ลงไป)
/// แต่ <c>NPSN</c> ของต้นฉบับคืน <c>string.Empty</c> เสมอ (บรรทัด 22 · ไม่มีคลาสลูกไหน override)
/// เพราะเป็นช่องสำหรับบัญชี NEXON ที่เราไม่มี
/// ⇒ แพตช์ฝั่งเกมให้ <c>NPSN</c> คืน "กุญแจประจำเครื่อง" ที่สร้างครั้งเดียวแล้วเก็บไฟล์ไว้
/// (client/Durango.System/DeviceAccount.cs) — เส้นทางเดิมทุกเส้นจึงพากุญแจมาให้เองโดยไม่ต้องแก้อะไรอีก
///
/// ═══ ⚠️ ระดับความแข็งแรงที่ได้จริง — อย่าเข้าใจผิด ═══
/// นี่คือ **กุญแจประจำเครื่อง (bearer key)** ไม่ใช่รหัสผ่าน:
///   • หยุดเรื่องที่ร้ายที่สุดได้ — คนอื่นไม่เห็นและหยิบตัวละครเราไปเล่นไม่ได้อีกแล้ว
///   • แต่ยังวิ่งบน HTTP ธรรมดา ⇒ ใครดักกลางทางได้ก็ได้กุญแจไป
///   • ใครเข้าถึงไฟล์ในเครื่องผู้เล่นได้ก็ก๊อปกุญแจไปใช้ได้
///   • ย้ายเครื่อง = กุญแจคนละอัน = เข้าตัวละครเดิมไม่ได้ (ต้องก๊อปไฟล์กุญแจไปเอง)
/// ⇒ พอสำหรับเบต้าวงปิด · ถ้าจะเปิดสาธารณะจริงต้องมี login + HTTPS
/// </summary>
public static class AccountKeys
{
    /// <summary>ยาวสุดที่ยอมรับ — กันคนยัดสตริงยาว ๆ มากินหน่วยความจำ/ทำ log บวม</summary>
    private const int MaxLength = 128;

    /// <summary>
    /// ทำความสะอาดกุญแจที่รับมาจากคำขอ — คืน <c>null</c> ถ้าใช้ไม่ได้
    ///
    /// คืน null แปลว่า "คำขอนี้ไม่มีบัญชี" ซึ่งผู้เรียกต้องปฏิเสธ ไม่ใช่ปล่อยผ่าน
    /// (ตัวเกมรุ่นเก่าที่ยังไม่ได้แพตช์จะส่งค่าว่างมา — ต้องเข้าไม่ได้ ไม่ใช่เข้าได้แบบเดิม)
    /// </summary>
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string key = raw.Trim();
        if (key.Length > MaxLength) return null;

        // ยอมเฉพาะตัวอักษร/ตัวเลข/ขีด — กันอักขระแปลกที่จะไปโผล่ใน log หรือชื่อไฟล์
        foreach (char c in key)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') return null;
        }
        return key;
    }

    /// <summary>เทียบกุญแจสองอัน — ไม่มีอันไหนว่างได้ (ว่าง = ไม่ใช่เจ้าของ เสมอ)</summary>
    public static bool Same(string a, string b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>ตัดให้สั้นพอสำหรับเขียน log — ห้ามพิมพ์กุญแจเต็มลง log (log อ่านได้จากหลายที่)</summary>
    public static string ForLog(string key) =>
        string.IsNullOrEmpty(key) ? "(ไม่มี)" : key[..Math.Min(8, key.Length)] + "…";
}
