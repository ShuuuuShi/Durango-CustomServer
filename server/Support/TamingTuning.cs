using System;
using Durango.Utils;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

/// <summary>
/// ค่าคงที่ของการจับสัตว์ป่า — อ่านจาก <c>data/assets/constants.json → taming</c>
///
/// **ทุกค่าในนี้เป็นของ NEXON ไม่ใช่ของเรา** — ไฟล์ข้อมูลมีครบทั้งสูตรและตัวเลข:
/// <code>
/// "taming": {
///   "success_ratio":        "2 * (1 / (1 + exp(-(2.2 / 6 * d_l + 2.2))) - 0.5)",
///   "adjust_by_life_ratio": "1 - 0.8 * pow(r / R, 2)",
///   "taming_time":     3.0,
///   "tamable_hp_rate": 0.3,
///   "taming_cooltime": 5.0,
///   "tamable_dealing_rate": 0.2
/// }
/// </code>
///
/// สูตรสองตัวแรกเป็นข้อความ ต้องคิดด้วย <see cref="StatFormula"/> (รองรับ exp/pow แล้ว)
///
/// ⚠️ <c>tamable_dealing_rate</c> ยังไม่ได้ใช้ — เดาความหมายไม่ออกจากชื่อและไม่มีที่ไหน
/// ในซอร์สฝั่งเกมอ่านมัน จึงไม่แตะดีกว่าเดาแล้วทำสมดุลเพี้ยน
///
/// ค่าสำรองที่ใส่ไว้ตรงกับไฟล์จริงเป๊ะ — มีไว้กันเซิร์ฟพังตอนไฟล์หาย ไม่ใช่ค่าที่เราคิดเอง
/// </summary>
public static class TamingTuning
{
    private static bool _loaded;
    private static string _successRatio = "2 * (1 / (1 + exp(-(2.2 / 6 * d_l + 2.2))) - 0.5)";
    private static string _adjustByLifeRatio = "1 - 0.8 * pow(r / R, 2)";
    private static float _tamingTime = 3.0f;
    private static float _tamableHpRate = 0.3f;
    private static float _cooltime = 5.0f;

    public static string SuccessRatio { get { EnsureLoaded(); return _successRatio; } }
    public static string AdjustByLifeRatio { get { EnsureLoaded(); return _adjustByLifeRatio; } }
    public static float TamingTime { get { EnsureLoaded(); return _tamingTime; } }
    public static float TamableHpRate { get { EnsureLoaded(); return _tamableHpRate; } }
    public static float Cooltime { get { EnsureLoaded(); return _cooltime; } }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        JObject root = Json.ReadFromFile<JObject>("constants");
        if (root?["taming"] is not JObject taming)
        {
            Console.WriteLine("[จับสัตว์] ⚠️ อ่าน constants.json → taming ไม่ได้ — ใช้ค่าสำรอง");
            return;
        }

        _successRatio = (string)taming["success_ratio"] ?? _successRatio;
        _adjustByLifeRatio = (string)taming["adjust_by_life_ratio"] ?? _adjustByLifeRatio;
        _tamingTime = (float?)taming["taming_time"] ?? _tamingTime;
        _tamableHpRate = (float?)taming["tamable_hp_rate"] ?? _tamableHpRate;
        _cooltime = (float?)taming["taming_cooltime"] ?? _cooltime;

        Console.WriteLine($"[จับสัตว์] เงื่อนไข: เลือดไม่เกิน {_tamableHpRate:P0} · " +
                          $"ใช้เวลา {_tamingTime:0.#} วิ · รอ {_cooltime:0.#} วิระหว่างครั้ง");
    }
}
