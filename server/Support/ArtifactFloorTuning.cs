using System;
using Durango.Utils;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

/// <summary>
/// ค่าของการต่อเติมชั้น — อ่านจาก <c>data/assets/constants.json → artifact_floor</c>
///
/// **ค่าของ NEXON** ไฟล์จริงมี:
/// <code>
/// "artifact_floor": { "floorable_types": [9992, 9997], "max_stories": 3 }
/// </code>
///
/// ⚠️ ใช้เป็น **เพดานฝั่งเซิร์ฟ** ไม่ใช่แค่ค่าโชว์ — ฝั่งเกมใช้ค่านี้ปิดปุ่มต่อเติม
/// (client/BuildSystem.cs:456 · client/Durango.Logic.Interactions/ArtifactInteractions.cs:1366)
/// แต่ client ที่ถูกแก้ข้ามด่านนั้นได้ แล้วส่ง <c>Stories</c> เท่าไรก็ได้มาให้เซิร์ฟ
/// ⇒ เซิร์ฟต้อง clamp เอง ไม่งั้นเครื่องผู้เล่นคนอื่นจอง array ตามเลขนั้นแล้ว OutOfMemory
/// (ดู Player.Building.ClampStories)
/// </summary>
public static class ArtifactFloorTuning
{
    private static bool _loaded;
    private static int _maxStories = 3;

    /// <summary>จำนวนชั้นสูงสุดที่ต่อเติมได้</summary>
    public static int MaxStories { get { EnsureLoaded(); return _maxStories; } }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        JObject root = Json.ReadFromFile<JObject>("constants");
        if (root?["artifact_floor"] is not JObject floor)
        {
            Console.WriteLine("[สร้าง] ⚠️ อ่าน constants.json → artifact_floor ไม่ได้ — ใช้ค่าสำรอง");
            return;
        }
        // กันค่าเพี้ยนในไฟล์ด้วย: เพดานต้องอยู่ในช่วงที่สมเหตุสมผลเสมอ
        _maxStories = Math.Clamp((int?)floor["max_stories"] ?? _maxStories, 1, 16);
    }
}
