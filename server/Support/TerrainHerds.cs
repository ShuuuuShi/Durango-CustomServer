using System;
using System.Collections.Generic;
using System.Text;

namespace Durango.Online;

/// <summary>
/// จุดที่ฝูงสัตว์เกิดบนเกาะ — อ่านจาก <c>herds.yml</c> ใน terrain zip
///
/// รูปแบบไฟล์จริง (สำรวจจาก terrain zip ทุกไฟล์ในโปรเจกต์):
/// <code>
/// herds:
///   beach:
///   - id: 201
///     tile:
///     - 188
///     - 192
///   land:
///   - id: 301
///     ...
/// chunks: ...          &lt;- คีย์ระดับบนอื่น ๆ ที่เราไม่ใช้
/// water_points: ...
/// sleeping_points: ...
/// </code>
///
/// ชื่อกลุ่มที่พบ: <c>land · beach · lake_shallow · lake_deep · ocean</c>
/// — **ตรงกับคีย์ใน <c>region_templates.json → herds</c> เป๊ะ** ซึ่งเป็นตัวบอกว่ากลุ่มนั้น
/// เกิดสัตว์ชนิดอะไรบ้าง ⇒ สองไฟล์นี้ประกบกัน: ไฟล์เกาะบอก "ตรงไหน" แม่แบบบอก "ตัวอะไร"
///
/// ทำไมอ่าน YAML เอง: เหตุผลเดียวกับ <see cref="TerrainPois"/> — โปรเจกต์ไม่มีไลบรารี YAML
/// และไฟล์นี้ใช้รูปแบบเดียวตายตัว อ่านเองตรง ๆ ถูกกว่าลาก dependency เข้ามาทั้งตัว
/// </summary>
public class TerrainHerds
{
    /// <summary>ชื่อกลุ่ม (land/beach/…) → พิกัดจุดเกิดฝูงตามลำดับในไฟล์</summary>
    public Dictionary<string, List<Point2>> Groups { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty
    {
        get
        {
            foreach (KeyValuePair<string, List<Point2>> group in Groups)
            {
                if (group.Value.Count > 0) return false;
            }
            return true;
        }
    }

    public IReadOnlyList<Point2> Of(string group) =>
        group != null && Groups.TryGetValue(group, out List<Point2> list) ? list : Array.Empty<Point2>();

    /// <summary>
    /// อ่าน herds.yml — คืน null เมื่อไฟล์ว่างหรืออ่านไม่ได้
    ///
    /// อ่านเฉพาะใต้คีย์ <c>herds:</c> เท่านั้น พอเจอคีย์ระดับบนตัวอื่น (คอลัมน์ 0) ก็หยุด
    /// เพราะ <c>chunks:</c> / <c>water_points:</c> มีรูปร่างต่างกันและเราไม่ได้ใช้
    /// </summary>
    public static TerrainHerds Parse(byte[] yaml)
    {
        if (yaml == null || yaml.Length == 0) return null;

        var herds = new TerrainHerds();
        try
        {
            string[] lines = Encoding.UTF8.GetString(yaml).Replace("\r\n", "\n").Split('\n');
            bool inHerds = false;
            List<Point2> group = null;       // กลุ่มที่กำลังอ่านอยู่
            bool readingTile = false;        // อยู่ใต้ "tile:" หรือยัง
            int pendingX = int.MinValue;     // เก็บ x ไว้รอ y (พิกัดเขียนคนละบรรทัด)

            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.Length == 0 || line.TrimStart().StartsWith("#")) continue;

                int indent = 0;
                while (indent < line.Length && line[indent] == ' ') indent++;
                string body = line.Substring(indent);

                // คีย์ระดับบนสุด: เข้า/ออกบล็อก herds
                if (indent == 0 && body.EndsWith(":"))
                {
                    inHerds = body.Equals("herds:", StringComparison.OrdinalIgnoreCase);
                    group = null;
                    readingTile = false;
                    pendingX = int.MinValue;
                    continue;
                }
                if (!inHerds) continue;

                // ⚠️ ต้องเช็ค "- " ก่อนเช็คชื่อกลุ่ม เพราะรายการฝูงอยู่ระดับย่อหน้าเดียวกับชื่อกลุ่ม
                // (YAML ยอมให้ list ไม่ต้องย่อหน้าเพิ่มจาก key ของมัน) — เคยสลับลำดับแล้วกลุ่มถูกล้างทิ้งทุกบรรทัด
                //   herds:
                //     land:          <- indent 2 ชื่อกลุ่ม
                //     - id: 1        <- indent 2 เหมือนกัน แต่เป็นรายการ
                //       tile:        <- indent 4
                //       - 88
                if (!body.StartsWith("- "))
                {
                    // ชื่อกลุ่ม เช่น "  land:" — ถ้ามีค่าตามหลัง (เช่น "{}" หรือ "[]") ถือว่ากลุ่มว่าง
                    if (indent == 2 && body.EndsWith(":"))
                    {
                        string name = body.Substring(0, body.Length - 1).Trim();
                        if (!herds.Groups.TryGetValue(name, out group))
                        {
                            group = new List<Point2>();
                            herds.Groups[name] = group;
                        }
                        readingTile = false;
                        pendingX = int.MinValue;
                        continue;
                    }
                    if (indent == 2)
                    {
                        group = null;             // "  land: {}" — กลุ่มว่าง ข้ามไป
                        readingTile = false;
                        continue;
                    }
                }
                if (group == null) continue;

                // "  - id: 201" เริ่มฝูงใหม่ ⇒ ล้างสถานะที่ค้างจากฝูงก่อนหน้า
                if (body.StartsWith("- "))
                {
                    string item = body.Substring(2).Trim();
                    if (item.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
                    {
                        readingTile = false;
                        pendingX = int.MinValue;
                        continue;
                    }
                    // "    - 188" — ตัวเลขของ tile
                    if (readingTile && int.TryParse(item, out int number))
                    {
                        if (pendingX == int.MinValue)
                        {
                            pendingX = number;
                        }
                        else
                        {
                            group.Add(new Point2(pendingX, number));
                            pendingX = int.MinValue;
                            readingTile = false;
                        }
                    }
                    continue;
                }

                if (body.StartsWith("tile:", StringComparison.OrdinalIgnoreCase))
                {
                    string rest = body.Substring("tile:".Length).Trim();
                    readingTile = true;
                    pendingX = int.MinValue;
                    // รองรับรูปแบบบรรทัดเดียว "tile: [188, 192]" เผื่อไฟล์ที่เจนเอง
                    if (rest.StartsWith("[") && rest.EndsWith("]"))
                    {
                        string[] parts = rest.Substring(1, rest.Length - 2).Split(',');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[0].Trim(), out int x) &&
                            int.TryParse(parts[1].Trim(), out int y))
                        {
                            group.Add(new Point2(x, y));
                        }
                        readingTile = false;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[herd] อ่าน herds.yml ไม่ได้: {e.Message}");
            return null;
        }

        return herds.IsEmpty ? null : herds;
    }
}
