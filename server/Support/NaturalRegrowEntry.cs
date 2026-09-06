using Newtonsoft.Json;

namespace Durango.Online;

/// <summary>
/// หนึ่งช่องที่รอของธรรมชาติงอกกลับ (คิวอยู่ที่ <c>WorldContext.NaturalRegrow</c>)
///
/// งอกกลับเป็น **ชนิดเดิมที่ช่องเดิม** ตั้งใจให้เป็นแบบนี้:
/// ข้อมูลเกม (entity_types/natural.json) มีเงื่อนไขการเกิดครบ — biomass · fertility ·
/// temperature · humidity · survivability รายไบโอม · ระยะห่างจากทะเล/แม่น้ำ/หน้าผา ฯลฯ
/// แต่ฝั่งเซิร์ฟยังไม่มีค่าพวกนั้นรายช่องให้คำนวณ (มีแต่ไบโอม)
/// ⇒ ถ้าจะสุ่มชนิดใหม่ตามเงื่อนไขจะต้อง**เดา**ค่าที่ขาด ซึ่งผิดกฎของโปรเจกต์
///   การคืนชนิดเดิมใช้ข้อมูลที่แน่นอน 100% (มันเคยอยู่ตรงนั้นจริง) และให้ผลที่ผู้เล่นคาดหวังได้
///   ทั้งยังรักษาหน้าตาของเกาะไว้ตามที่ terrain ออกแบบมา
/// </summary>
public class NaturalRegrowEntry
{
    [JsonProperty("x")] public int X;

    [JsonProperty("y")] public int Y;

    /// <summary>ชนิดที่จะงอกกลับ — ชนิดเดิมที่ถูกเก็บไป</summary>
    [JsonProperty("entity_type")] public ushort EntityType;

    /// <summary>เวลาโลก (วินาที) ที่ถึงกำหนดงอก</summary>
    [JsonProperty("due")] public double DueAt;
}
