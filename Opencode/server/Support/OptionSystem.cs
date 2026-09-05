namespace Durango.Logic;

// พอร์ตย่อจาก nexonSRC OptionSystem — เซิร์ฟใช้แค่ลิมิตผลค้นหาตลาด
// ค่าเดียวกับที่ GameServer.SendWelcome ส่งให้ client ใน Options: market.search.limit = 20
public static class OptionSystem
{
    public const int MarketSearchLimit = 20;

    public static int GetMarketSearchLimit() => MarketSearchLimit;
}
