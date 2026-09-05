using System;
using Newtonsoft.Json;

namespace Durango.Utils.Converter;

// พอร์ตจาก nexonSRC/Durango.Utils.Converter/GettextConverter.cs — รูปร่าง JSON ตามต้นฉบับ
// (เกมเก็บข้อความเป็น {"msgid(เกาหลี)": {locale: แปล}|null}) แต่ resolve เป็น string ด้วย Gettext ของเรา
public class GettextConverter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value is Gettext g)
        {
            if (!string.IsNullOrEmpty(g.MsgId) && g.Dict != null && g.Dict.Count > 0)
            {
                writer.WriteStartObject();
                writer.WritePropertyName(g.MsgId);
                serializer.Serialize(writer, g.Dict);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteValue(g.MsgId);
            }
        }
        else
        {
            writer.WriteNull();
        }
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        switch (reader.TokenType)
        {
            case JsonToken.String:
                return new Gettext((string)reader.Value);
            case JsonToken.StartObject:
            {
                // รูปร่าง {msgid: {locale: ข้อความ}|null}
                reader.Read();
                string msgid = reader.Value as string;
                reader.Read();
                Dictionary<string, string> dict = null;
                if (reader.TokenType == JsonToken.StartObject)
                {
                    dict = new Dictionary<string, string>();
                    while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                    {
                        string key = reader.Value as string;
                        reader.Read();
                        if (reader.TokenType == JsonToken.String)
                        {
                            dict[key] = (string)reader.Value;
                        }
                        else
                        {
                            reader.Skip();
                        }
                    }
                    // loop ออกที่ EndObject — เลื่อนข้ามเพื่อให้ Newtonsoft อ่าน property ถัดไปได้
                    reader.Read();
                }
                else if (reader.TokenType == JsonToken.Null)
                {
                    // {msgid: null} — ต้องเลื่อนข้าม null ด้วย ไม่งั้น readerค้างหน้า null
                    // แล้ว Newtonsoft จะถือว่า property ถัดไปเป็นของ object นี้ (ทำ JSON ทั้งไฟล์พัง)
                    reader.Read();
                }
                return new Gettext(msgid, dict);
            }
            default:
                throw new JsonSerializationException($"Gettext expects object or string, got {reader.TokenType}");
        }
    }

    public override bool CanConvert(Type objectType) => objectType == typeof(Gettext);
}
