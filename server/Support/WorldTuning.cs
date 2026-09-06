using System;
using System.IO;
using Durango.Utils;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

/// <summary>
/// ค่าปรับสมดุลของโลกที่อ่านจาก <c>data/config.json</c> → หมวด <c>World</c>
///
/// ⚠️ ก่อนหน้านี้ <c>config.json</c> เป็นแค่ไฟล์ให้หน้าแอดมินเปิดแก้ — **ไม่มีโค้ดฝั่งเซิร์ฟอ่านสักคีย์**
/// (grep "NaturalRegrowSeconds" / "ChunkSendRange" ทั้งโปรเจกต์ไม่เจอผู้อ่านเลย)
/// ⇒ แก้ค่าในหน้าแอดมินแล้วไม่มีอะไรเปลี่ยน ไฟล์นี้เริ่มต้นแก้เรื่องนั้นด้วยคีย์ที่ระบบนิเวศต้องใช้
///
/// อ่านครั้งเดียวตอนใช้งานครั้งแรก แล้วแคชไว้ — เรียก <see cref="Reload"/> ถ้าแก้ config ระหว่างรัน
/// </summary>
public static class WorldTuning
{
    private static JObject _world;

    /// <summary>
    /// ของธรรมชาติที่ถูกเก็บจนหมด จะงอกกลับที่เดิมหลังกี่วินาที
    ///
    /// **ค่าของเรา** — ข้อมูลเกมจริงไม่ได้กำหนดไว้ (ค้น constants.json ทั้งไฟล์แล้วไม่มีคีย์เรื่อง
    /// respawn/regrow ของธรรมชาติเลย) ⇒ ตั้งเป็นค่าปรับได้ใน config.json แทนที่จะฝังในโค้ด
    /// ค่าเริ่มต้น 1200 วิ (20 นาที) ≈ ครึ่งรอบกลางวัน-กลางคืนของเซิร์ฟนี้ (รอบละ 48 นาที)
    /// </summary>
    public static double NaturalRegrowSeconds => GetDouble("NaturalRegrowSeconds", 1200.0);

    public static void Reload() => _world = null;

    private static double GetDouble(string key, double fallback)
    {
        Load();
        JToken token = _world?[key];
        return token != null && double.TryParse(token.ToString(), out double value) && value > 0 ? value : fallback;
    }

    private static void Load()
    {
        if (_world != null) return;
        _world = new JObject();
        try
        {
            string path = Path.Combine(Json.DataDir ?? ".", "config.json");
            if (!File.Exists(path)) return;
            var root = JObject.Parse(File.ReadAllText(path));
            if (root["World"] is JObject world) _world = world;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ตั้งค่า] อ่าน config.json → World ไม่ได้ ({e.Message}) — ใช้ค่าเริ่มต้น");
        }
    }
}
