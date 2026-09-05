using System;
using System.Collections.Generic;
using System.Text;

namespace Durango.Online;

/// <summary>
/// จุดสำคัญที่มากับเกาะ — อ่านจาก <c>pois.yml</c> ใน terrain zip
///
/// ทำไมต้องมี: เกมของ NEXON เป็นตัว client ล้วน ๆ มันไม่ได้วางท่าเรือ/รูวาร์ปลงโลกเอง
/// เซิร์ฟตัวจริง (ที่ไม่มีซอร์ส) เป็นคนวาง แล้วส่งให้ client ผ่าน AppearArtifact
/// ตำแหน่งที่ควรวางมาพร้อมไฟล์เกาะอยู่แล้วในรูป <c>pois.yml</c> เช่น
/// <code>
/// port_points:
/// - - 63
///   - 71
/// warpholes: {}
/// </code>
/// พิกัดคือ "มุมของ footprint" ไม่ใช่จุดกึ่งกลาง
///
/// ทำไมไม่ใช้ไลบรารี YAML: โปรเจกต์ไม่มี และไฟล์นี้ใช้ YAML แค่รูปแบบเดียว
/// (map ของ list-of-pair) จึงอ่านเองตรง ๆ ถูกกว่าลาก dependency เข้ามาทั้งตัว
/// </summary>
public class TerrainPois
{
    /// <summary>ท่าเรือ — วาง dock (7001, 3x3) จุดเริ่มของระบบล่องเรือ</summary>
    public List<Point2> PortPoints { get; } = new();

    /// <summary>รูวาร์ปกลาง — neutral_warphole (9450, 6x6)</summary>
    public List<Point2> Warpholes { get; } = new();

    /// <summary>รอยแยก — warp_accelerator (6282) ชื่อในเกม "균열"</summary>
    public List<Point2> Rifts { get; } = new();

    /// <summary>
    /// หลุมอุกกาบาต — crack_01 (7037) ชื่อในเกม "닫힌 크레이터" (หลุมที่ยังปิดอยู่)
    ///
    /// ⚠️ **คนละอย่างกับ Rifts** ถึงจะอยู่ไฟล์เดียวกัน — เดิมโค้ดนี้ยัดสองคีย์ลงลิสต์เดียว
    /// ⇒ หลุมอุกกาบาตทุกหลุมโผล่มาเป็นแท่งเร่งวาร์ป (โมเดล crack_02) ผิดหมด
    /// และฝั่งเกมแยกสองอย่างนี้ชัดเจน:
    ///   • crack_01 มี component "Crack" ⇒ POIUpdater.cs:122 อ่าน ArtifactState.Crack
    ///     แล้วนับเป็น PointOfInterest.Crack (หมุดแผนที่ icon_map_poi_crack)
    ///   • warp_accelerator ⇒ POIUpdater.cs:156 นับเป็น PointOfInterest.Rift (ไม่มีหมุด)
    /// ⇒ วางผิดชนิด = หมุดหลุมอุกกาบาตหายจากแผนที่ทั้งเกาะ และ POICount.CraterCount เป็น 0 ตลอด
    /// </summary>
    public List<Point2> Craters { get; } = new();

    public bool IsEmpty => PortPoints.Count == 0 && Warpholes.Count == 0
                        && Rifts.Count == 0 && Craters.Count == 0;

    /// <summary>
    /// อ่าน pois.yml — คืน null เมื่อไฟล์ว่างหรืออ่านไม่ได้ (เกาะที่ generate เองบางลูกไม่มีไฟล์นี้)
    ///
    /// รองรับ 2 รูปแบบที่พบจริงในไฟล์ของเกม:
    ///   คีย์:            |  คีย์:
    ///   - - 63           |  - [63, 71]
    ///     - 71           |
    /// และข้ามคีย์ที่เป็น map ว่าง (<c>warpholes: {}</c>) หรือ list ว่าง (<c>craters: []</c>)
    /// </summary>
    public static TerrainPois Parse(byte[] yaml)
    {
        if (yaml == null || yaml.Length == 0)
        {
            return null;
        }

        var pois = new TerrainPois();
        try
        {
            string[] lines = Encoding.UTF8.GetString(yaml).Replace("\r\n", "\n").Split('\n');
            List<Point2> target = null;   // ลิสต์ของคีย์ที่กำลังอ่านอยู่ (null = คีย์ที่ไม่สนใจ)
            int pendingX = int.MinValue;  // เก็บ x ไว้รอ y ในรูปแบบหลายบรรทัด

            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.Length == 0 || line.TrimStart().StartsWith("#"))
                {
                    continue;
                }

                // บรรทัดคีย์ระดับบนสุด (ไม่มีช่องว่างนำหน้า และไม่ได้ขึ้นต้นด้วย -)
                if (!char.IsWhiteSpace(raw[0]) && !line.TrimStart().StartsWith("-"))
                {
                    int colon = line.IndexOf(':');
                    string key = colon >= 0 ? line.Substring(0, colon).Trim() : line.Trim();
                    string rest = colon >= 0 ? line.Substring(colon + 1).Trim() : "";
                    pendingX = int.MinValue;
                    target = key switch
                    {
                        "port_points" => pois.PortPoints,
                        "warpholes" => pois.Warpholes,
                        "rifts" => pois.Rifts,
                        "craters" => pois.Craters,
                        _ => null
                    };
                    // "คีย์: {}" หรือ "คีย์: []" = ว่าง ไม่มีรายการตามมา
                    if (rest == "{}" || rest == "[]")
                    {
                        target = null;
                    }
                    continue;
                }

                if (target == null)
                {
                    continue;
                }

                string body = line.TrimStart();
                if (body.StartsWith("- ["))
                {
                    // รูปแบบบรรทัดเดียว: - [63, 71]
                    string inner = body.Substring(body.IndexOf('[') + 1).TrimEnd(']', ' ');
                    string[] parts = inner.Split(',');
                    if (parts.Length >= 2
                        && int.TryParse(parts[0].Trim(), out int ix)
                        && int.TryParse(parts[1].Trim(), out int iy))
                    {
                        target.Add(new Point2(ix, iy));
                    }
                    continue;
                }

                // รูปแบบหลายบรรทัด: "- - 63" แล้วบรรทัดถัดไป "  - 71"
                string num = body.TrimStart('-', ' ');
                if (!int.TryParse(num, out int value))
                {
                    pendingX = int.MinValue;
                    continue;
                }
                if (pendingX == int.MinValue)
                {
                    pendingX = value;
                }
                else
                {
                    target.Add(new Point2(pendingX, value));
                    pendingX = int.MinValue;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[terrain] อ่าน pois.yml ไม่สำเร็จ: {e.Message}");
            return null;
        }

        return pois.IsEmpty ? null : pois;
    }
}
