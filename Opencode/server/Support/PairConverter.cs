using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;

namespace Durango.Utils.Converter;

// พอร์ตจาก nexonSRC/Durango.Utils.Converter/PairConverter.cs — Pair<T1,T2> เป็น array [a, b]
public class PairConverter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        Type type = value.GetType();
        PropertyInfo p1 = type.GetProperty("Item1");
        PropertyInfo p2 = type.GetProperty("Item2");
        writer.WriteStartArray();
        serializer.Serialize(writer, p1?.GetValue(value));
        serializer.Serialize(writer, p2?.GetValue(value));
        writer.WriteEndArray();
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        Type t = Nullable.GetUnderlyingType(objectType) ?? objectType;
        Type[] args = t.GetGenericArguments();
        reader.Read();
        object a = serializer.Deserialize(reader, args[0]);
        reader.Read();
        object b = serializer.Deserialize(reader, args[1]);
        reader.Read();
        return Activator.CreateInstance(t, a, b);
    }

    public override bool CanConvert(Type objectType)
    {
        Type t = Nullable.GetUnderlyingType(objectType) ?? objectType;
        return t.IsValueType && t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Pair<,>);
    }
}
