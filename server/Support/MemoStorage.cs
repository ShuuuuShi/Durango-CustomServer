using System.Collections.Generic;

namespace Durango.Logic.Encyclopedia;

// พอร์ตย่อจาก nexonSRC/MemoSystem.cs (struct EncyclopediaStorage/MemoStorage)
// เป็นรูปร่าง blob ที่เก็บใน PlayerContext.Storage["encyclopedia"] — client อ่าน/เขียนเองผ่าน SetStorageItem
// ความต่าง: ต้นฉบับเติมรายการ tooltip/fiction ที่มีข้อความทั้งหมดตอนสร้าง context ใหม่
// ที่นี่เซิร์ฟไม่มีตารางภาษา (LocalizeSystem อยู่ฝั่ง client) ⇒ เริ่มเป็นลิสต์ว่าง client จะอัปเดตเองทาง SetStorageItem
public static class MemoStorageDefaults
{
    public const string StorageKey = "encyclopedia";

    public struct EncyclopediaStorage
    {
        public MemoStorage Memo;
    }

    public struct MemoStorage
    {
        public List<KeyValuePair<MemoType, List<int>>> Memos;
    }

    public static EncyclopediaStorage Empty()
    {
        return new EncyclopediaStorage
        {
            Memo = new MemoStorage
            {
                Memos = new List<KeyValuePair<MemoType, List<int>>>()
            }
        };
    }
}
