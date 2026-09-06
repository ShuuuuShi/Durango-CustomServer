using System;
using Durango.Utils;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

/// <summary>
/// ค่าคงที่ของการวาร์ป — อ่านจาก <c>data/assets/constants.json → warp</c>
///
/// **ทุกค่าเป็นของ NEXON** ไฟล์จริงมี:
/// <code>
/// "warp": {
///   "warp_time":              2,
///   "warp_cost":              "t_stone_reference * 12 * (dist / 300.)",
///   "warp_back_discount":     0.5,
///   "clan_warphole_cooltime": 1800
/// }
/// </code>
///
/// ⚠️ บล็อกนี้ไม่เคยถูกพอร์ตเข้าคลาสไหนมาก่อน (<c>Support/YamlConstants.cs</c> ไม่มีคำว่า warp เลย)
/// เพราะเซิร์ฟยังไม่มีระบบวาร์ป — เพิ่งมีในรอบนี้พร้อม <c>Core/Player.Warp.cs</c>
///
/// <c>warp_cost</c> ยังไม่ได้ใช้: เป็นสูตรที่ต้องมีระบบเงิน (t_stone) ซึ่งเซิร์ฟยังไม่ทำ
/// ⇒ วาร์ปฟรีไปก่อน ดีกว่าเก็บเงินด้วยตัวเลขที่เดาเอง
/// </summary>
public static class WarpTuning
{
    private static bool _loaded;
    private static float _warpTime = 2f;
    private static float _clanWarpholeCooltime = 1800f;

    /// <summary>วินาทีที่ผู้เล่นยืนรอก่อนถูกย้ายตัว (constants.json → warp → warp_time)</summary>
    public static float WarpTime { get { EnsureLoaded(); return _warpTime; } }

    public static float ClanWarpholeCooltime { get { EnsureLoaded(); return _clanWarpholeCooltime; } }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        JObject root = Json.ReadFromFile<JObject>("constants");
        if (root?["warp"] is not JObject warp)
        {
            Console.WriteLine("[วาร์ป] ⚠️ อ่าน constants.json → warp ไม่ได้ — ใช้ค่าสำรอง");
            return;
        }

        _warpTime = (float?)warp["warp_time"] ?? _warpTime;
        _clanWarpholeCooltime = (float?)warp["clan_warphole_cooltime"] ?? _clanWarpholeCooltime;
    }
}
