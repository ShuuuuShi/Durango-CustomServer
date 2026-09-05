using System;
using System.Collections.Generic;

namespace Durango.Online;

/// <summary>
/// ชุดพื้นผิว/โทนสีของเกาะ (<c>tile_set</c> · <c>color_set</c>) — ตัวที่ตัดสินว่าเกาะหน้าตาแบบไหน
///
/// ═══ อาการเดิม ═══
/// <c>info.yml</c> ของหลายเกาะมี <c>"tile_set": ""</c> ⇒ <c>TerrainMeta.TileSet</c> เป็นค่าว่าง
/// (client/Durango.Terrain/TerrainMeta.cs:146 เอาค่าจากไฟล์มาตรง ๆ ไม่มี fallback)
/// แล้วตัวเกมเอาชื่อนี้ไปเทียบกับตารางที่ฝังอยู่ในฉาก **สามที่**:
///   • <c>AmbientLightingManager.OverrideColorSets[i].Name</c>   (สีแสงตามไบโอม)
///   • <c>CustomColorCorrectionEffect._overrideSets[j].Name</c>  (โทนสีภาพรวม)
///   • <c>BgmManager._tileSetBgm[j].TileSet</c>                  (เพลงประจำเกาะ)
/// ไม่ตรงสักตาราง = ใช้ค่าเริ่มต้นหมด ⇒ **เกาะหิมะเรนเดอร์เป็นทุ่งหญ้า · เกาะทะเลทรายก็เขียว**
/// และไม่มี error อะไรเลย เพราะโค้ดแค่วนหาไม่เจอแล้วผ่านไป
///
/// ═══ รายชื่อนี้มาจากไหน — ไม่ได้เดา ═══
/// ดึงออกมาจากตัวเกมเองด้วยคำสั่ง <c>tilesetdump</c> ของ BotBridge (อ่าน
/// <c>CustomColorCorrectionEffect._overrideSets</c> ตอนอยู่ในโลกจริง) ได้ 18 ชื่อ
///
/// ═══ การจับคู่เกาะ→ชุด ═══
/// ท้าย id ของเกาะเป็นรหัสภูมิประเทศสองตัวอักษร และ **ตรงกับ tile_set ทุกกรณีที่มีข้อมูล 4/4**:
/// <code>
///   ri45sa → savanna     ri50sn → snowfields
///   ra60sw → swamp       ua60vol → volcanic
/// </code>
/// ⇒ ใช้กติกาเดียวกันเติมให้เกาะที่ช่องว่าง: ri35de→desert · ri35te→temperate ·
///    ri40tr→tropical · ri55tu→tundra
/// ส่วนเกาะที่เราสร้างเองมี <c>theme</c> อยู่ใน <c>config.yml</c> ของ zip ตรง ๆ (tropical/savanna/
/// tundra/snow) ⇒ ใช้ theme ก่อนเสมอ แล้วค่อยถอยมาดูรหัสท้าย id
///
/// ⚠️ ชื่อที่ไม่อยู่ในรายการ 18 ตัวจะถูกทิ้ง — ส่งชื่อมั่วไปก็เหมือนส่งค่าว่าง แต่หลอกให้คิดว่าแก้แล้ว
/// </summary>
public static class TileSets
{
    /// <summary>18 ชื่อที่ตัวเกมรู้จักจริง (ดึงจาก CustomColorCorrectionEffect ด้วย tilesetdump)</summary>
    public static readonly IReadOnlyList<string> Known = new[]
    {
        "ancora", "swamp", "swamp_black", "grassland", "savanna", "savanna_baobab",
        "temperate", "temperate_blue", "tropical", "tropical_blue", "tropical_outpost",
        "tropical_poison", "snowfields", "snowfields_heavy", "desert", "tundra",
        "tundra_blue", "volcanic"
    };

    private static readonly HashSet<string> KnownSet = new(Known, StringComparer.Ordinal);

    /// <summary>ธีมของตัวสร้างเกาะ (config.yml → theme) → ชื่อชุดที่เกมรู้จัก</summary>
    private static readonly Dictionary<string, string> ByTheme = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tropical"] = "tropical",
        ["temperate"] = "temperate",
        ["savanna"] = "savanna",
        ["tundra"] = "tundra",
        ["snow"] = "snowfields",
        ["snowfields"] = "snowfields",
        ["desert"] = "desert",
        ["swamp"] = "swamp",
        ["volcanic"] = "volcanic",
        ["grassland"] = "grassland"
    };

    /// <summary>รหัสภูมิประเทศท้าย id ของเกาะ → ชื่อชุด (ดูหลักฐาน 4/4 ที่หัวคลาส)</summary>
    private static readonly Dictionary<string, string> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gr"] = "grassland",
        ["te"] = "temperate",
        ["tp"] = "tropical",
        ["tr"] = "tropical",
        ["de"] = "desert",
        ["sa"] = "savanna",
        ["sv"] = "savanna",
        ["sn"] = "snowfields",
        ["td"] = "tundra",
        ["tu"] = "tundra",
        ["sw"] = "swamp",
        ["vol"] = "volcanic"
    };

    public static bool IsKnown(string name) => name != null && KnownSet.Contains(name);

    /// <summary>
    /// เดาชุดพื้นผิวของเกาะจากธีม (ถ้ามี) แล้วถอยไปดูรหัสท้าย id — null ถ้าไม่เข้าเงื่อนไขไหนเลย
    /// </summary>
    public static string Guess(string terrainId, string theme)
    {
        if (!string.IsNullOrEmpty(theme) && ByTheme.TryGetValue(theme, out string byTheme)) return byTheme;
        if (string.IsNullOrEmpty(terrainId)) return null;

        // ตัดตัวเลข/ตัวห้อยท้ายออกก่อน: "ri35de" → "de" · "sn20snow" → "snow" · "ri30td01" → "td"
        string tail = "";
        for (int i = terrainId.Length - 1; i >= 0; i--)
        {
            char c = terrainId[i];
            if (char.IsDigit(c))
            {
                if (tail.Length > 0) break;   // เจอเลขหลังตัวอักษรแล้ว = จบรหัส
                continue;
            }
            tail = c + tail;
        }
        if (tail.Length == 0) return null;
        if (ByTheme.TryGetValue(tail, out string full)) return full;      // "snow" → snowfields
        return ByCode.TryGetValue(tail, out string byCode) ? byCode : null;
    }
}
