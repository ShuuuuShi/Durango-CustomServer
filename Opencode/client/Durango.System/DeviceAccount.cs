using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Durango.System;

/// <summary>
/// [เพิ่มเอง 5 ก.ย. 2026] กุญแจบัญชีประจำเครื่อง — ตัวที่บอกเซิร์ฟว่า "ตัวละครพวกนี้เป็นของฉัน"
///
/// ═══ ทำไมต้องมี ═══
/// ต้นฉบับส่งช่อง <c>account_id</c> ไปกับ <c>/sessions</c> และ <c>/accounts</c> อยู่แล้วทุกคำขอ
/// (<see cref="Platform.BuildSessionForm"/> บรรทัด 118 ใส่ <c>NPSN</c> ลงไป)
/// แต่ <c>Platform.NPSN</c> คืน <c>string.Empty</c> เสมอ (Platform.cs:22 · ไม่มีคลาสลูกไหน override)
/// เพราะเป็นช่องสำหรับบัญชี NEXON ที่เซิร์ฟส่วนตัวไม่มี
///
/// ⚠️ ผลคือ **เซิร์ฟแยกไม่ออกว่าใครเป็นใคร** ⇒ <c>/accounts</c> คืนตัวละครทุกตัวบนเซิร์ฟให้ทุกคน
/// แล้วหน้าเลือกตัวละครเอามาทำเป็นปุ่มกดเข้าเล่นได้ตรง ๆ ⇒ ผู้เล่นคนที่ 2 เปิดเกมจะเห็น
/// ตัวละครของคนที่ 1 อยู่ในสล็อตตัวเอง กดเข้าเล่นได้ทันทีโดยไม่ต้องแฮกอะไรเลย
///
/// ⇒ คลาสนี้สร้างกุญแจสุ่มครั้งเดียวแล้วเก็บลงไฟล์ข้างตัวเกม เส้นทางเดิมทุกเส้นจึงพากุญแจ
/// ไปให้เซิร์ฟเองโดยไม่ต้องแก้จุดเรียกสักจุด
///
/// ═══ เก็บไฟล์ไว้ที่ไหน ═══
/// ใช้กติกาเดียวกับที่ <c>TitleMenuGroup.ReadLocalClusterJson</c> ใช้หา <c>clusters.json</c>:
///   PC     — โฟลเดอร์เดียวกับ Durango.exe
///   มือถือ — <c>Application.persistentDataPath</c>
///
/// ═══ ⚠️ ข้อจำกัดที่ต้องรู้ ═══
/// นี่คือ "กุญแจประจำเครื่อง" ไม่ใช่รหัสผ่าน — ใครก๊อปไฟล์นี้ไปก็เข้าตัวละครเราได้
/// และย้ายเครื่อง = กุญแจคนละอัน = เข้าตัวละครเดิมไม่ได้ (ต้องก๊อปไฟล์ไปเอง)
/// พอสำหรับเบต้าวงปิด ถ้าจะเปิดสาธารณะจริงต้องมี login + HTTPS
/// </summary>
public static class DeviceAccount
{
    private const string FileName = "account.key";

    private static string _key;

    /// <summary>กุญแจของเครื่องนี้ — สร้างครั้งแรกที่เรียก แล้วใช้ค่าเดิมตลอด</summary>
    public static string Key
    {
        get
        {
            if (!string.IsNullOrEmpty(_key))
            {
                return _key;
            }
            _key = Load() ?? Create();
            return _key;
        }
    }

    private static string Path
    {
        get
        {
            string dir = (Application.platform == RuntimePlatform.Android
                          || Application.platform == RuntimePlatform.IPhonePlayer)
                ? Application.persistentDataPath
                : global::System.IO.Path.GetDirectoryName(Application.dataPath);
            return string.IsNullOrEmpty(dir) ? null : global::System.IO.Path.Combine(dir, FileName);
        }
    }

    private static string Load()
    {
        try
        {
            string path = Path;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }
            string text = File.ReadAllText(path).Trim();
            return IsValid(text) ? text : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[durango] อ่านกุญแจบัญชีไม่ได้: " + e.Message);
            return null;
        }
    }

    private static string Create()
    {
        string key = Guid.NewGuid().ToString("N");
        try
        {
            string path = Path;
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, key, new UTF8Encoding(false));
                Debug.Log("[durango] สร้างกุญแจบัญชีใหม่ที่ " + path);
            }
        }
        catch (Exception e)
        {
            // เขียนไม่ได้ (โฟลเดอร์อ่านอย่างเดียว?) — ยังเล่นรอบนี้ได้ แต่รอบหน้าจะเป็นคนละบัญชี
            // ⇒ เตือนให้ชัด เพราะอาการที่ผู้เล่นเห็นคือ "ตัวละครหายไปตอนเปิดเกมใหม่"
            Debug.LogWarning("[durango] ⚠️ เขียนไฟล์กุญแจบัญชีไม่ได้: " + e.Message +
                             " — เปิดเกมรอบหน้าจะกลายเป็นบัญชีใหม่และไม่เห็นตัวละครเดิม");
        }
        return key;
    }

    /// <summary>ต้องตรงกับที่ฝั่งเซิร์ฟยอมรับ (server/Support/AccountKeys.Normalize)</summary>
    private static bool IsValid(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 128)
        {
            return false;
        }
        foreach (char c in key)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }
        return true;
    }
}
