using System;
using System.IO;

namespace Durango.Utils;

// พอร์ตจาก nexonSRC/Durango.Utils/AppData.cs — คงพฤติกรรมเดิม (saves อยู่ใต้ offline/{cluster}/)
// ความต่าง: BasePath ต้นฉบับคือ AppData ของเกม ที่นี่ชี้โฟลเดอร์ AppData ข้าง ๆ ตัวเซิร์ฟ
public static class AppData
{
    private static readonly char[] Separator = { '/', '\\' };

    public static string BasePath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "AppData");

    public static string CombinePath(string path) => Path.Combine(BasePath, path);

    public static void DeleteFile(string filename)
    {
        try { File.Delete(CombinePath(filename)); }
        catch (Exception) { }
    }

    public static string[] GetFiles(string directoryPath, string searchPattern, SearchOption option)
    {
        string dir = CreateDirectory(directoryPath);
        return string.IsNullOrEmpty(dir) ? null : Directory.GetFiles(dir, searchPattern, option);
    }

    public static string[] GetDirectories(string directoryPath, string searchPattern, SearchOption option)
    {
        string dir = CreateDirectory(directoryPath);
        return string.IsNullOrEmpty(dir) ? null : Directory.GetDirectories(dir, searchPattern, option);
    }

    public static string CreateDirectory(string directoryPath)
    {
        string[] parts = directoryPath.Split(Separator);
        string cur = BasePath;
        foreach (string part in parts)
        {
            cur = Path.Combine(cur, part);
            if (!Directory.Exists(cur)) Directory.CreateDirectory(cur);
        }
        return cur;
    }
}
