using System;
using Newtonsoft.Json;

namespace Durango.Utils.Converter;

// พอร์ตจาก nexonSRC/Durango.Utils.Converter/GaugeConverter.cs — Gauge เป็น {"min","max","cur"}
public class GaugeConverter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value is Gauge g)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("min");
            writer.WriteValue(g.Min());
            writer.WritePropertyName("max");
            writer.WriteValue(g.Max());
            writer.WritePropertyName("cur");
            writer.WriteValue(g.Get());
            writer.WriteEndObject();
        }
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        float min = 0f, max = 1f, cur = 1f;
        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            string key = reader.Value?.ToString();
            reader.Read();
            switch (key)
            {
                case "min": min = Convert.ToSingle(reader.Value); break;
                case "max": max = Convert.ToSingle(reader.Value); break;
                case "cur": cur = Convert.ToSingle(reader.Value); break;
                default: reader.Skip(); break;
            }
        }
        return new Gauge(max, min, new[] { new GaugeNode(0.0, cur) });
    }

    public override bool CanConvert(Type objectType) => objectType == typeof(Gauge);
}
