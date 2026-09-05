using System;
using System.IO;

namespace Durango.Online;

/// <summary>
/// เขียน/อ่านไฟล์เซฟแบบไม่กินข้อมูล — ใช้ร่วมกันทั้ง .world และ .player
///
/// ปัญหาเดิม (พบ 5 ก.ย. 2026 ตอนไล่ทบทวนความทนทานของเซิร์ฟ):
///   1. เขียนทับไฟล์เดิมตรง ๆ ด้วย File.WriteAllBytes ⇒ ถ้าโดน kill / ไฟดับ / ดิสก์เต็ม
///      กลางเขียน ไฟล์เซฟจะพังทั้งไฟล์ ไม่เหลืออะไรเลย
///   2. ตอนโหลด ถ้า parse ไม่ผ่านจะคืน null เฉย ๆ แล้วผู้เรียก (Host / WorldRegistry)
///      **สร้างโลกใหม่ทับ** ⇒ ไฟล์พังครั้งเดียว = เกาะหายถาวรแบบเงียบ ๆ
///
/// วิธีแก้: เขียนลงไฟล์ .tmp ให้เสร็จก่อน แล้วค่อยสลับเข้าที่ด้วย File.Replace ซึ่งเป็น
/// atomic บน NTFS และแถมไฟล์สำรอง .bak ให้ในตัว ⇒ ระหว่างเขียน ไฟล์เดิมยังอยู่ครบเสมอ
/// ตอนอ่าน ถ้าไฟล์หลักพังก็ถอยไปอ่าน .bak แทนที่จะทำเป็นว่าไม่มีอะไรเลย
///
/// ไม่ได้กันทุกกรณี (ดิสก์พังจริง ๆ ก็จบ) แต่กันกรณีที่เกิดจริงบ่อยสุดคือปิดเซิร์ฟกลางเซฟ
/// </summary>
public static class SafeSave
{
    /// <summary>เขียนไฟล์แบบสลับเข้าที่ — คืน false เมื่อเขียนไม่สำเร็จ (ของเดิมยังอยู่)</summary>
    public static bool WriteAtomic(string path, byte[] data, string what)
    {
        // ⚠️ ด่านสำคัญที่สุดของทั้งไฟล์ — ข้อมูลว่าง **ห้ามเขียนเด็ดขาด**
        // เพราะ File.Replace ข้างล่างจะดันไฟล์เซฟดีเดิมไปเป็น .bak แล้วเอาไฟล์ 0 ไบต์วางแทน
        // พอรอบถัดไปพลาดซ้ำ .bak ก็โดนทับด้วยไฟล์ว่างอีก ⇒ ของหายทั้งไฟล์หลักและไฟล์สำรอง
        // โดยไม่มีสัญญาณเตือนใด ๆ (กว่าจะรู้คือตอนผู้เล่นเข้ามาแล้วกลายเป็นตัวละครใหม่)
        if (string.IsNullOrEmpty(path) || data == null || data.Length == 0)
        {
            Console.WriteLine($"[{what}] ⚠️ ข้อมูลที่จะเซฟว่างเปล่า — **ไม่เขียนทับไฟล์เดิม** " +
                              $"(ไฟล์เซฟที่มีอยู่ยังปลอดภัย) path={path}");
            return false;
        }
        string tmp = path + ".tmp";
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Flush(true) = บังคับให้ลงจานจริง ไม่ค้างใน cache ของ OS
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                // สลับไฟล์ + เก็บของเดิมไว้เป็น .bak (File.Replace ทำสองอย่างนี้แบบ atomic)
                File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, path);   // รอบแรก ยังไม่มีไฟล์ให้แทนที่
            }
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[{what}] เซฟไม่สำเร็จ: {e.Message}");
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch (Exception) { }
            return false;
        }
    }

    /// <summary>
    /// อ่านไฟล์เซฟ — ถ้าไฟล์หลักพัง ลอง .bak ให้อัตโนมัติ
    /// คืน null เมื่ออ่านไม่ได้จริง ๆ ทั้งคู่ (ผู้เรียกจะสร้างใหม่)
    /// </summary>
    public static T ReadWithBackup<T>(string path, string what, Func<byte[], T> parse) where T : class
    {
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate))
            {
                continue;
            }
            try
            {
                T value = parse(File.ReadAllBytes(candidate));
                if (value != null)
                {
                    if (candidate != path)
                    {
                        // ดังไว้ก่อน — ผู้ดูแลจะได้รู้ว่าไฟล์หลักมีปัญหา ไม่ใช่ปล่อยผ่านเงียบ ๆ
                        Console.WriteLine($"[{what}] ⚠️ ไฟล์หลักเสีย — กู้จากไฟล์สำรอง {candidate}");
                    }
                    return value;
                }
                Console.WriteLine($"[{what}] ⚠️ {candidate} อ่านแล้วได้ค่าว่าง");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{what}] ⚠️ อ่าน {candidate} ไม่สำเร็จ: {e.Message}");
            }
        }
        return null;
    }
}
