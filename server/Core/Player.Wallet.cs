using System;
using System.Collections.Generic;
using Durango.Network;
using Messages;
using Shared.Economy;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  กระเป๋าเงิน — เซิร์ฟนี้ใช้สกุลเดียวคือ T Stone
//
//  ═══ ทำไมเป็น TStone ไม่ใช่สกุลที่ตั้งชื่อเอง ═══
//  ต้นฉบับมีสกุลนี้อยู่แล้วเป็นตัวแรกของ enum (server/GameCode/Shared.Economy/Currency.cs:
//  Invalid=-1, TStone=0, Gem, Coin, CashshopMileage, RPiece, MobileCoin, PcCoin, WarpMatter)
//  และฝั่งเกมมีไอคอน/รูปแบบข้อความให้ครบแล้ว — client/Durango.Logic.Item/Inventory.cs:
//    :197 "<t_stone/> {0:N0}" · :216 "[preset=round_box?<t_stone/>    {0:N0}]" · :250 "tstone_icon"
//  ⇒ เลือกใช้ของที่มีอยู่ ไม่ประดิษฐ์ใหม่ (กฎ: อิงต้นฉบับ)
//
//  ═══ "สกุลเดียว" ทำยังไงโดยไม่แตะไฟล์ต้นฉบับ ═══
//  ห้ามแก้ enum Currency (อยู่ใน GameCode) ⇒ ไม่ได้ "ลบ" สกุลอื่นออกจากโปรโตคอล
//  แต่ทำให้มันไม่มีอยู่จริงสองชั้น:
//    1. ฝั่งเซิร์ฟ (ตัวจริง) — กระเป๋าที่ส่งออกไปมีคีย์เดียวคือ Currency.TStone
//       และทุกการหักเงินผ่าน TrySpendTStone เท่านั้น ⇒ ต่อให้ฝั่งเกมคิดว่าจ่ายด้วยเพชร
//       เซิร์ฟก็หัก T Stone อยู่ดี เพราะเซิร์ฟไม่รู้จักสกุลอื่นเลย
//    2. ฝั่งเกม (ให้ UI ตรงกับความจริง) — client/WalletExtension.cs `Currency.Normalize()`
//       คืน TStone ทุกกรณี ซึ่งเป็นคอขวดที่ยอดเงิน/ไอคอน/รูปแบบข้อความผ่านหมดทุกเส้น
//       (GetPaidBalance · GetUnpaidBalance · CurrencyFormat · CurrencyEmphasisFormat · GetIcon)
//       ต้นฉบับออกแบบ Normalize ไว้แปลงสกุลอยู่แล้ว (Coin → MobileCoin/PcCoin ตามแพลตฟอร์ม)
//
//  ═══ Paid vs Unpaid ═══
//  ต้นฉบับแยก "เงินที่ซื้อด้วยเงินจริง" (Paid) กับ "เงินที่หามาในเกม" (Unpaid)
//  เซิร์ฟนี้ไม่มีการชำระเงินจริง ⇒ ยอดทั้งหมดอยู่ฝั่ง **Unpaid** และ Paid ว่างเสมอ
//  ฝั่งเกมบวกสองช่องรวมกันอยู่แล้วตอนอ่านยอด (client/WalletExtension.cs:12 GetBalance)
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    /// <summary>ยอด T Stone ปัจจุบัน (อ่านจากไฟล์เซฟของผู้เล่นคนนี้)</summary>
    public long TStone => _context?.TStone ?? 0L;

    /// <summary>
    /// กระเป๋าเงินที่ส่งไปกับ <c>Inventory</c> และ <c>WalletUpdated</c>
    ///
    /// ⚠️ ห้ามส่ง <c>null</c> — ฝั่งเกมกัน null ให้แล้วก็จริง (WalletExtension.cs:20,25)
    /// แต่ส่ง null = ทุกยอดเป็น 0 และหน้าจอที่โชว์เงินจะว่างเปล่าถาวร
    /// (นี่คืออาการเดิมก่อนมีไฟล์นี้ — Core/Player.Inventory.cs ส่ง Wallet = null)
    /// </summary>
    public Wallet BuildWallet()
    {
        return new Wallet
        {
            // ไม่มีการชำระเงินจริงบนเซิร์ฟนี้ ⇒ ช่อง Paid ว่างเสมอ
            PaidBalances = new Dictionary<Currency, long>(),
            UnpaidBalances = new Dictionary<Currency, long> { { Currency.TStone, TStone } },
            Vouchers = Array.Empty<VoucherInfo>()
        };
    }

    /// <summary>
    /// เพิ่ม T Stone ให้ผู้เล่นคนนี้
    /// </summary>
    /// <param name="amount">จำนวน (0 หรือติดลบ = ไม่ทำอะไร)</param>
    /// <param name="reason">เขียนลง log ให้ไล่ที่มาได้ตอนสมดุลเพี้ยน — เหมือนที่ AddExp ทำ</param>
    public void AddTStone(long amount, string reason)
    {
        if (amount <= 0 || _context == null) return;
        // กันล้น long ตอนมีบั๊กแจกเงินซ้ำ — ค่าเพดานเดียวกับที่ฝั่งเกมยอมรับใน NumberInputPopup
        // (client/Durango.UI/ClanInfoPage.cs:380 ใส่เพดาน 99,999,999 ตอนบริจาคเข้ากองทุนเผ่า)
        _context.TStone = Math.Min(_context.TStone + amount, MaxTStone);
        Console.WriteLine($"[เงิน] {ShortEntityId()} +{amount:N0} T Stone → {_context.TStone:N0} (จาก {reason})");
        PushWallet();
    }

    /// <summary>
    /// หัก T Stone — คืน <c>false</c> ถ้าเงินไม่พอ (ไม่หักอะไรเลย)
    ///
    /// **ทุกการจ่ายเงินในเซิร์ฟนี้ต้องผ่านเมธอดนี้** ไม่ว่าฝั่งเกมจะบอกว่าจ่ายด้วยสกุลอะไร
    /// เพราะเซิร์ฟไม่รู้จักสกุลอื่น (ดูหมายเหตุหัวไฟล์)
    /// </summary>
    public bool TrySpendTStone(long amount, string reason)
    {
        if (_context == null) return false;
        if (amount <= 0) return true;               // ของฟรี — ถือว่าจ่ายผ่าน
        if (_context.TStone < amount) return false; // เงินไม่พอ — ห้ามหักบางส่วน
        _context.TStone -= amount;
        Console.WriteLine($"[เงิน] {ShortEntityId()} -{amount:N0} T Stone → {_context.TStone:N0} (จ่าย {reason})");
        PushWallet();
        return true;
    }

    /// <summary>ให้ฝั่งแอดมินสั่งดันยอดใหม่ได้ (Core/Host.cs PushWalletTo)</summary>
    public void SendWalletNow() => PushWallet();

    /// <summary>
    /// แจ้งยอดใหม่ให้ฝั่งเกม + สั่งเซฟ
    ///
    /// ฝั่งเกมรับด้วย global handler ไม่ผูก seq (client/InventorySystem.cs:82
    /// <c>Connections.Frontend.On&lt;WalletUpdated&gt;(ReceiveWalletUpdated)</c>) ⇒ ส่งแบบ ReplyOf = 0
    /// และมันเช็ค EntityId ก่อนรับ (client/InventorySystem.cs:143-148) ⇒ ต้องใส่ให้ตรง
    /// </summary>
    private void PushWallet()
    {
        Send(new WalletUpdated { EntityId = EntityId, Wallet = BuildWallet() });
        OnContextChanged();
    }

    /// <summary>
    /// เพดานยอดเงิน — **ค่าของเรา**
    ///
    /// ข้อมูลเกมไม่ได้กำหนดเพดานกระเป๋าไว้ (costs.json มีแต่ราคา ไม่มีเพดาน)
    /// ใช้เลขเดียวกับเพดานช่องกรอกจำนวนของต้นฉบับเพื่อไม่ให้ UI แสดงค่าที่กรอกกลับไม่ได้
    /// </summary>
    private const long MaxTStone = 99_999_999L;

    private string ShortEntityId()
        => string.IsNullOrEmpty(EntityId) ? "?" : EntityId[..Math.Min(8, EntityId.Length)];
}
