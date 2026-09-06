using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Durango.Online;

/// <summary>
/// ตัวอ่านไฟล์คำแปล GNU gettext (<c>.mo</c>) — ใช้แปลข้อความเกาหลีของ NEXON เป็นไทย
///
/// ═══ ทำไมเซิร์ฟต้องแปลเอง (ทั้งที่ตัวเกมมี catalog อยู่แล้ว) ═══
/// ชื่อไอเทม/สิ่งปลูกสร้างที่ผู้เล่นเห็น **ไม่ได้ผ่าน catalog ฝั่งเกมเลย** — มันคือข้อความ
/// ที่เซิร์ฟส่งไปสำเร็จรูปแล้ว:
///   • <c>client/Durango.Logic.Item/ItemData.cs:132</c> — <c>Name = itemInfo.Name;</c>
///     (รับ <c>Messages.Item.Name</c> จากเซิร์ฟมาโชว์ตรง ๆ ไม่เรียก <c>T._()</c>)
///   • ส่วนที่เกมแปลเองคือข้อมูลที่มันโหลดจากไฟล์ของตัวเอง ผ่าน
///     <c>client/.../GettextConverter.cs</c> → <c>T.ParseMsgIdAndGetString(msgid, dict)</c>
/// ⇒ ต่อให้วาง <c>locales/th</c> ครบทุกชื่อโฟลเดอร์ ชื่อของก็ยังเป็นเกาหลีอยู่ดี
///
/// ═══ ทำไมข้อมูลในไฟล์ .json แปลไม่ได้ ═══
/// <c>data/assets/**</c> เก็บข้อความเป็น <c>{"msgid ภาษาเกาหลี": {locale: คำแปล}}</c>
/// แต่ในชุดที่สกัดมา **ค่าเป็น <c>null</c> เกือบทั้งหมด** ⇒ <see cref="Durango.Utils.Gettext"/>
/// ไม่มีคำแปลให้เลือก ตกกลับไปใช้ msgid (เกาหลี) เสมอ
/// ⇒ คำแปลจริงอยู่ใน <c>messages.mo</c> (33,262 ข้อความ) ซึ่งเป็นคนละไฟล์กัน
///
/// ═══ รูปแบบไฟล์ ═══
/// สเปก GNU gettext: magic <c>0x950412de</c> (หรือกลับด้าน = big-endian) ·
/// ตาราง offset ของ msgid กับ msgstr อย่างละชุด · สตริงเป็น UTF-8 ไม่มี null ปิดท้ายในความยาว
/// รายการแรก (msgid ว่าง) คือ header ของไฟล์ ไม่ใช่คำแปล — ข้ามไป
///
/// ⚠️ msgid ที่มี <c></c> คั่น = context ของ gettext (msgctxt) เก็บทั้งคู่ไว้:
/// ตัวเต็มสำหรับจับตรง ๆ และตัวหลัง <c></c> เผื่อฝั่งเซิร์ฟมีแต่ข้อความเปล่า
/// </summary>
public static class MoCatalog
{
    private const uint MagicLittleEndian = 0x950412deu;
    private const uint MagicBigEndian = 0xde120495u;

    private static Dictionary<string, string> _map;

    /// <summary>จำนวนคำแปลที่โหลดได้ — 0 = ยังไม่โหลด หรือไม่มีไฟล์</summary>
    public static int Count => _map?.Count ?? 0;

    /// <summary>โหลดแล้วหรือยัง (ใช้ตัดสินใจว่าจะเรียก <see cref="Translate"/> หรือข้ามไปเลย)</summary>
    public static bool Ready => _map is { Count: > 0 };

    /// <summary>
    /// โหลดคำแปลจากโฟลเดอร์ data — เรียกครั้งเดียวตอนบูต
    ///
    /// หาไฟล์ตามลำดับ <c>locales/&lt;ภาษา&gt;/LC_MESSAGES/messages.mo</c>
    /// ไม่มีไฟล์ = ไม่พัง แค่ไม่แปล (เขียนเตือนออก log ครั้งเดียว)
    /// </summary>
    public static void Load(string dataDir, string language = "th")
    {
        if (_map != null) return;
        _map = new Dictionary<string, string>(StringComparer.Ordinal);

        string path = Path.Combine(dataDir ?? "", "locales", language, "LC_MESSAGES", "messages.mo");
        if (!File.Exists(path))
        {
            Console.WriteLine($"[ภาษา] ไม่พบไฟล์คำแปล {path} — ข้อความจะเป็นภาษาเกาหลีตามต้นฉบับ");
            return;
        }

        try
        {
            Parse(File.ReadAllBytes(path), _map);
            Console.WriteLine($"[ภาษา] โหลดคำแปล {language} {_map.Count} ข้อความ");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ภาษา] อ่าน {path} ไม่สำเร็จ: {e.Message} — ข้อความจะเป็นภาษาเกาหลี");
        }
    }

    /// <summary>
    /// แปลข้อความ — คืนตัวเดิมถ้าไม่มีคำแปล (ปลอดภัยเสมอ ไม่คืนค่าว่าง)
    /// </summary>
    public static string Translate(string text)
    {
        if (string.IsNullOrEmpty(text) || _map == null || _map.Count == 0) return text;
        if (_map.TryGetValue(text, out string translated) && !string.IsNullOrEmpty(translated))
        {
            return translated;
        }
        return text;
    }

    private static void Parse(byte[] data, Dictionary<string, string> into)
    {
        if (data.Length < 20) throw new InvalidDataException("ไฟล์สั้นเกินกว่าจะเป็น .mo");

        uint magic = BitConverter.ToUInt32(data, 0);
        bool swap;
        if (magic == MagicLittleEndian) swap = !BitConverter.IsLittleEndian;
        else if (magic == MagicBigEndian) swap = BitConverter.IsLittleEndian;
        else throw new InvalidDataException($"magic ไม่ตรงสเปก gettext ({magic:x8})");

        int count = (int)ReadUInt(data, 8, swap);
        int originalTable = (int)ReadUInt(data, 12, swap);
        int translationTable = (int)ReadUInt(data, 16, swap);

        for (int i = 0; i < count; i++)
        {
            int oLen = (int)ReadUInt(data, originalTable + i * 8, swap);
            int oOff = (int)ReadUInt(data, originalTable + i * 8 + 4, swap);
            int tLen = (int)ReadUInt(data, translationTable + i * 8, swap);
            int tOff = (int)ReadUInt(data, translationTable + i * 8 + 4, swap);

            if (oLen <= 0) continue;                                   // รายการแรก = header ของไฟล์
            if (oOff < 0 || tOff < 0 || oOff + oLen > data.Length || tOff + tLen > data.Length) continue;

            string msgid = Encoding.UTF8.GetString(data, oOff, oLen);
            string msgstr = Encoding.UTF8.GetString(data, tOff, tLen);
            if (string.IsNullOrEmpty(msgstr)) continue;

            // พหูพจน์คั่นด้วย \0 — เอาแบบแรกพอ (ไทยไม่มีรูปพหูพจน์)
            int nul = msgstr.IndexOf('\0');
            if (nul >= 0) msgstr = msgstr.Substring(0, nul);

            into[msgid] = msgstr;

            // msgctxt คั่นด้วย  — เก็บข้อความเปล่าไว้ด้วย เผื่อฝั่งเซิร์ฟไม่มี context
            int ctx = msgid.IndexOf('');
            if (ctx >= 0 && ctx + 1 < msgid.Length)
            {
                string bare = msgid.Substring(ctx + 1);
                if (!into.ContainsKey(bare)) into[bare] = msgstr;
            }
        }
    }

    private static uint ReadUInt(byte[] data, int offset, bool swap)
    {
        if (offset < 0 || offset + 4 > data.Length) throw new InvalidDataException("อ่านเลยขอบไฟล์");
        uint value = BitConverter.ToUInt32(data, offset);
        return swap ? BinaryPrimitivesReverse(value) : value;
    }

    private static uint BinaryPrimitivesReverse(uint value) =>
        (value >> 24) | ((value >> 8) & 0x0000FF00u) | ((value << 8) & 0x00FF0000u) | (value << 24);
}
