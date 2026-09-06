using System.Collections.Generic;
using Durango.Utils.Converter;
using Newtonsoft.Json;

namespace Durango.Utils;

// พอร์ตจาก L10N.Gettext (assembly แยกที่ไม่มีใน nexonSRC) — รูปร่าง JSON เดียวกับที่
// GettextConverter อ่าน: object {msgid: {locale: ข้อความ}|null} หรือ string ตรง ๆ
// ความต่างจากต้นฉบับ: ต้นฉบับ resolve ตาม locale ของ client เครื่อง host
// ที่นี่เซิร์ฟเลือกเอง th_TH → en_US → ภาษาแรกที่เจอ → msgid
[JsonConverter(typeof(GettextConverter))]
public class Gettext
{
    public string MsgId;
    public Dictionary<string, string> Dict;

    public Gettext() { }

    public Gettext(string msgId)
    {
        MsgId = msgId;
    }

    public Gettext(string msgId, Dictionary<string, string> dict)
    {
        MsgId = msgId;
        Dict = dict;
    }

    public override string ToString()
    {
        if (!string.IsNullOrEmpty(MsgId) && Dict != null)
        {
            if (Dict.TryGetValue("th_TH", out var th) && !string.IsNullOrEmpty(th)) return th;

            // [6 ก.ย. 2026] คำแปลไทยจากไฟล์ .mo — ต้องลองก่อน en_US
            //
            // ⚠️ ชุดข้อมูลที่สกัดมามีค่าแปลเป็น null เกือบทั้งหมด ⇒ บรรทัดบนแทบไม่เคยได้ผล
            // คำแปลจริง 33,262 ข้อความอยู่ใน data/locales/th/LC_MESSAGES/messages.mo
            // ซึ่งคีย์คือ msgid ภาษาเกาหลีตัวเดียวกับที่เก็บไว้ตรงนี้พอดี (ดู Support/MoCatalog.cs)
            //
            // ทำไมเซิร์ฟต้องแปลเอง: ชื่อไอเทมถูกส่งเป็นข้อความสำเร็จรูปไปให้เกม
            // (client/Durango.Logic.Item/ItemData.cs:132 `Name = itemInfo.Name`) ไม่ได้ส่ง msgid
            // ให้เกมแปลผ่าน catalog ของตัวเอง ⇒ ไม่แปลตรงนี้ = ผู้เล่นเห็นภาษาเกาหลีทั้งเกม
            if (Durango.Online.MoCatalog.Ready)
            {
                string fromCatalog = Durango.Online.MoCatalog.Translate(MsgId);
                if (!string.IsNullOrEmpty(fromCatalog) && !ReferenceEquals(fromCatalog, MsgId)) return fromCatalog;
            }

            if (Dict.TryGetValue("en_US", out var en) && !string.IsNullOrEmpty(en)) return en;
            foreach (var v in Dict.Values)
            {
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }

        // ไม่มี Dict เลย (ข้อความที่เก็บเป็นสตริงตรง ๆ) — ยังแปลได้ถ้าอยู่ใน catalog
        if (!string.IsNullOrEmpty(MsgId) && Durango.Online.MoCatalog.Ready)
        {
            return Durango.Online.MoCatalog.Translate(MsgId);
        }
        return MsgId ?? string.Empty;
    }

    public static implicit operator string(Gettext g) => g?.ToString() ?? string.Empty;
}
