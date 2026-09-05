using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Durango.Utils;

// พอร์ตจาก nexonSRC/Durango.Utils/Json.cs
// ความต่างเดียว: ReadFromFile ต้นฉบับโหลดจาก Unity Resources ("offline/assets/...")
// ที่นี่โหลดจากดิสก์ data/assets/ (ไฟล์ชุดเดียวกับที่ client โหลดทาง /assets ตอน Online)
public static class Json
{
    private static string _dataDir;

    /// <summary>โฟลเดอร์ data ของเซิร์ฟ (มี assets/ อยู่ข้างใน) — Program ตั้งตอนบูต</summary>
    public static string DataDir
    {
        get => _dataDir ?? Path.Combine(AppContext.BaseDirectory, "data");
        set => _dataDir = value;
    }

    /// <summary>
    /// ค่าตั้งของ Newtonsoft ที่ใช้ทั้งโปรเจกต์ — **ต้องเป็น public** เพราะที่อื่นต้องกู้ค่าจาก
    /// JObject ด้วยชุดเดียวกัน ไม่งั้นจะไม่ได้ converter ที่จำเป็น (เช่น GaugeConverter)
    /// แล้วได้อ็อบเจกต์เปล่าแบบเงียบ ๆ (ดู Support/CageTypes.NormalizeLoaded)
    /// </summary>
    public static JsonSerializerSettings Setting { get; } = new()
    {
        Converters =
        {
            new Converter.PairConverter(),
            new Converter.GettextConverter(),
            new Converter.GaugeConverter()
        }
    };

    public static T Read<T>(string json, bool logException = false)
    {
        if (string.IsNullOrEmpty(json)) return default;
        try
        {
            return JsonConvert.DeserializeObject<T>(json, Setting);
        }
        catch (Exception e)
        {
            // เซิร์ฟอ่าน game data ตอนบูต — parse พังต้องเห็นทันที ไม่เงียบ
            Console.WriteLine($"[json] อ่าน {typeof(T).Name} ไม่สำเร็จ: {e.Message}");
            return default;
        }
    }

    public static T Read<T>(byte[] data, bool logException = false)
    {
        if (data == null || data.Length == 0) return default;
        return Read<T>(Encoding.UTF8.GetString(data), logException);
    }

    public static T ReadFromFile<T>(string fileName)
    {
        // ต้นฉบับ: Resources.Load("offline/assets/crops") → ที่นี่: data/assets/crops.json
        string rel = fileName.StartsWith("offline/assets/") ? fileName.Substring("offline/assets/".Length) : fileName;
        string path = Path.Combine(DataDir, "assets", rel.Replace('/', Path.DirectorySeparatorChar) + ".json");
        if (!File.Exists(path))
        {
            Console.WriteLine("[json] ไม่พบไฟล์ data — " + path);
            return default;
        }
        return Read<T>(File.ReadAllText(path));
    }

    public static string Write<T>(T data, bool indented = false)
    {
        try
        {
            return JsonConvert.SerializeObject(data, indented ? Formatting.Indented : Formatting.None, Setting);
        }
        catch (Exception e)
        {
            Console.WriteLine("[json] เขียนไม่สำเร็จ: " + e.Message);
            return string.Empty;
        }
    }

    public static byte[] WriteToBytes<T>(T data, bool indented = false)
    {
        return Encoding.UTF8.GetBytes(Write(data, indented));
    }
}
