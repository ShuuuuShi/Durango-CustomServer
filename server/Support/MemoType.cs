namespace Durango.Logic.Encyclopedia;

// พอร์ตจาก nexonSRC/Durango.Logic.Encyclopedia/MemoType.cs (ฝั่ง client)
// ต่างจาก Shared.Memo.MemoType (ฝั่งเซิร์ฟ) — เกมมีสองชุดจริง
public enum MemoType
{
    Invalid = -1,
    Fiction = 0,
    Tooltip = 1,
    Survival = 2,
    Collect = 100,
    Faction = 101
}
