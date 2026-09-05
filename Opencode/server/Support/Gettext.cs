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
            if (Dict.TryGetValue("en_US", out var en) && !string.IsNullOrEmpty(en)) return en;
            foreach (var v in Dict.Values)
            {
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        return MsgId ?? string.Empty;
    }

    public static implicit operator string(Gettext g) => g?.ToString() ?? string.Empty;
}
