using System;
using Durango.Utils;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

/// <summary>
/// ค่าคงที่ของหลุมอุกกาบาต — อ่านจาก <c>data/assets/constants.json → crack</c>
///
/// **ทุกค่าเป็นของ NEXON** ไฟล์จริงมีครบ:
/// <code>
/// "crack": {
///   "crater_look":          "crater_01",
///   "aqua_crater_look":     "aqua_crater_01",
///   "required_investment":  "max(1, int(level * 0.2))",
///   "required_voucher_id":  "voucher_resource_induced_stone",
///   "activated_time":       600,
///   "investment_duration":  4
/// }
/// </code>
///
/// ═══ ใช้ทำอะไร ═══
/// หลุมอุกกาบาตในเกมมีสองหน้า: ตอนปิดใช้โมเดล <c>crack_01</c> ตามแบบแปลน · ตอนเปิดสลับเป็น
/// <c>crater_look</c> แล้วนับถอยหลัง <c>activated_time</c> วินาที
/// (client/Artifact.cs:1007-1026 <c>CrackTimerUpdate</c> อ่าน ActivatedSince/Until มาวาดหลอดเวลา)
///
/// ค่าสำรองตรงกับไฟล์จริงเป๊ะ — มีไว้กันเซิร์ฟพังตอนไฟล์หาย ไม่ใช่ค่าที่เราคิดเอง
/// </summary>
public static class CrackTuning
{
    private static bool _loaded;
    private static string _requiredInvestment = "max(1, int(level * 0.2))";
    private static string _voucherId = "voucher_resource_induced_stone";
    private static string _craterLook = "crater_01";
    private static string _aquaCraterLook = "aqua_crater_01";
    private static float _activatedTime = 600f;
    private static float _investmentDuration = 4f;

    /// <summary>สูตรจำนวนหินนำทางที่ต้องใช้เปิดหลุม — ตัวแปรคือ <c>level</c></summary>
    public static string RequiredInvestment { get { EnsureLoaded(); return _requiredInvestment; } }

    public static string VoucherId { get { EnsureLoaded(); return _voucherId; } }

    /// <summary>โมเดลตอนหลุมเปิดแล้ว (บนบก)</summary>
    public static string CraterLook { get { EnsureLoaded(); return _craterLook; } }

    /// <summary>โมเดลตอนหลุมเปิดแล้ว (ในน้ำ — aqua_crack_01)</summary>
    public static string AquaCraterLook { get { EnsureLoaded(); return _aquaCraterLook; } }

    /// <summary>หลุมเปิดอยู่นานกี่วินาที</summary>
    public static float ActivatedTime { get { EnsureLoaded(); return _activatedTime; } }

    public static float InvestmentDuration { get { EnsureLoaded(); return _investmentDuration; } }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        JObject root = Json.ReadFromFile<JObject>("constants");
        if (root?["crack"] is not JObject crack)
        {
            Console.WriteLine("[หลุมอุกกาบาต] ⚠️ อ่าน constants.json → crack ไม่ได้ — ใช้ค่าสำรอง");
            return;
        }

        _requiredInvestment = (string)crack["required_investment"] ?? _requiredInvestment;
        _voucherId = (string)crack["required_voucher_id"] ?? _voucherId;
        _craterLook = (string)crack["crater_look"] ?? _craterLook;
        _aquaCraterLook = (string)crack["aqua_crater_look"] ?? _aquaCraterLook;
        _activatedTime = (float?)crack["activated_time"] ?? _activatedTime;
        _investmentDuration = (float?)crack["investment_duration"] ?? _investmentDuration;
    }
}
