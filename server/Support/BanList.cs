using System;
using System.Collections.Generic;
using System.IO;
using Durango.Utils;
using Newtonsoft.Json;

namespace Durango.Online;

/// <summary>
/// รายชื่อคนที่ถูกแบน — เก็บลงไฟล์ข้างไฟล์เซฟ
///
/// ═══ ทำไมต้องมี ═══
/// audit จัดว่า "ไม่มีเครื่องมือเตะ/แบน/ปิดปากเลยแม้แต่ตัวเดียว — เจอคนป่วนแล้วทำได้อย่างเดียว
/// คือปิดทั้งเซิร์ฟ" และย้ำว่าต่อให้เขียนระบบแบนก็ไม่มีความหมายถ้าไม่มีบัญชี
/// ⇒ ทำได้แล้วหลังมี <c>owner_key</c> (Support/AccountKeys)
///
/// ═══ แบนที่ระดับ "บัญชี" ไม่ใช่ตัวละคร ═══
/// แบนตัวละครอย่างเดียวไม่มีความหมาย เพราะสร้างตัวใหม่ได้ฟรี ⇒ แบนที่กุญแจบัญชี
/// (คนป่วนต้องลบไฟล์ <c>account.key</c> ทิ้งถึงจะเลี่ยงได้ ซึ่งก็เท่ากับทิ้งตัวละครเดิมทั้งหมด)
///
/// ⚠️ ไม่ใช่การแบนที่กันได้ 100% — กุญแจอยู่ในเครื่องผู้เล่น สร้างใหม่ได้
/// แต่พอสำหรับเบต้าวงปิด (คนป่วนเสียของทั้งหมดทุกครั้งที่โดนแบน)
/// </summary>
public static class BanList
{
    private sealed class Entry
    {
        [JsonProperty("key")] public string OwnerKey;
        [JsonProperty("reason")] public string Reason;
        [JsonProperty("at")] public double At;
    }

    private static readonly Dictionary<string, Entry> _bans = new(StringComparer.Ordinal);
    private static string _path;

    /// <summary>โหลดรายชื่อจากไฟล์ — เรียกตอนบูตหลังรู้ที่อยู่โฟลเดอร์เซฟแล้ว</summary>
    public static void Load(string path)
    {
        _path = path;
        _bans.Clear();
        try
        {
            if (!File.Exists(path)) return;
            Entry[] loaded = Json.Read<Entry[]>(File.ReadAllText(path));
            if (loaded == null) return;
            foreach (Entry entry in loaded)
            {
                if (!string.IsNullOrEmpty(entry?.OwnerKey)) _bans[entry.OwnerKey] = entry;
            }
            Console.WriteLine($"[แบน] โหลดรายชื่อที่ถูกแบน {_bans.Count} บัญชี");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[แบน] อ่าน {path} ไม่ได้: {e.Message}");
        }
    }

    public static bool IsBanned(string ownerKey) =>
        !string.IsNullOrEmpty(ownerKey) && _bans.ContainsKey(ownerKey);

    public static string ReasonOf(string ownerKey) =>
        !string.IsNullOrEmpty(ownerKey) && _bans.TryGetValue(ownerKey, out Entry e) ? e.Reason : null;

    public static int Count => _bans.Count;

    public static void Add(string ownerKey, string reason)
    {
        if (string.IsNullOrEmpty(ownerKey)) return;
        _bans[ownerKey] = new Entry { OwnerKey = ownerKey, Reason = reason, At = Times.UnixTimeNow() };
        Save();
        Console.WriteLine($"[แบน] เพิ่ม {AccountKeys.ForLog(ownerKey)} — {reason}");
    }

    public static bool Remove(string ownerKey)
    {
        if (string.IsNullOrEmpty(ownerKey) || !_bans.Remove(ownerKey)) return false;
        Save();
        Console.WriteLine($"[แบน] ปลดแบน {AccountKeys.ForLog(ownerKey)}");
        return true;
    }

    /// <summary>รายชื่อทั้งหมดสำหรับหน้าแอดมิน (ตัดกุญแจให้สั้นแล้ว ไม่ให้กุญแจเต็มหลุดออกไป)</summary>
    public static List<Dictionary<string, object>> Describe()
    {
        var result = new List<Dictionary<string, object>>();
        foreach (Entry entry in _bans.Values)
        {
            result.Add(new Dictionary<string, object>
            {
                ["key"] = AccountKeys.ForLog(entry.OwnerKey),
                ["reason"] = entry.Reason ?? "",
                ["at"] = entry.At
            });
        }
        return result;
    }

    private static void Save()
    {
        if (string.IsNullOrEmpty(_path)) return;
        try
        {
            var list = new List<Entry>(_bans.Values);
            // ใช้ตัวเขียนเดียวกับไฟล์เซฟ ⇒ ได้ด่านกัน "ข้อมูลว่างทับไฟล์เดิม" ไปด้วย (ดู SafeSave)
            SafeSave.WriteAtomic(_path, Json.WriteToBytes(list, indented: true), "ban-list");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[แบน] เขียนไฟล์ไม่สำเร็จ: {e.Message}");
        }
    }
}
