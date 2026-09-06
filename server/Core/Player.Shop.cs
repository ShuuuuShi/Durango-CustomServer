using System;
using Durango.Network;
using Messages;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  ร้านค้าเงินจริง / คูปอง (Shop)
//
//  ═══ ทำไมต้องมีไฟล์นี้ ทั้ง ๆ ที่เซิร์ฟนี้ไม่ขายของ ═══
//  ShopSystem ของเกมยิงคำขอ 5 ตัวรวดทันทีที่เมนูร้านค้าเปิดใช้งานได้
//  (client/ShopSystem.cs:175-185 OnReady → GetPurchases · GetAcceptableSubPurchases ·
//   GetCommodities · GetUserFirstPurchaseHistory · GetSpecialDeals)
//  ไม่ตอบ = ตัวจับเวลาใน AsyncCachedData ค้าง · SpecialDeals เป็น null ตลอด ·
//  รายการซื้อไม่เคย refresh ⇒ หน้าร้านค้าค้างหมุนเงียบ ๆ + log "ไม่มี handler"
//
//  ═══ จุดยืนของเซิร์ฟส่วนตัวนี้ ═══
//  นี่คือร้าน **เงินจริง** ของเกมเดิม (IAP / บัตรกำนัล / คูปอง TEN ของ NEXON)
//  เซิร์ฟนี้ไม่มีระบบชำระเงิน ไม่มีกระเป๋าเงินจริง และไม่มีสิทธิ์แจกของที่เคยขายด้วยเงิน
//  ⇒ นโยบายคือ **"ตอบชุดว่างที่ถูกโครงสร้าง + ปฏิเสธการซื้อทุกครั้งอย่างสุภาพ"**
//     - ห้ามคืนรายการสินค้า (จะกลายเป็นแจกของฟรี — ปุ่มซื้อกดได้ทันทีเพราะฝั่งเกมไม่เช็คเงินจริง)
//     - ห้ามแต่งประวัติการซื้อ / ประวัติซื้อครั้งแรก (ผู้เล่นจะเข้าใจผิดว่าเคยจ่ายเงิน)
//     - ห้ามแต่งดีลพิเศษ: server/data/assets/purchaser/special_deals.json มีแต่ "ข้อความแบนเนอร์"
//       (banner_title / banner_item_description ...) **ไม่มี** ราคา สกุลเงิน หรือเวลาหมดอายุ
//       ซึ่งเป็นฟิลด์บังคับของ SpecialDeal (Messages/SpecialDeal.cs: ExpiresAt/PriceCurrency/
//       PriceAmount) ⇒ ประกอบของจริงไม่ได้ ต้องว่างเท่านั้น
//
//  ═══ ทำไม "ปฏิเสธ" ถึงใช้ Abort ═══
//  ฝั่งเกมถือว่า TypeCode 1022(Error) / 1024(Abort) / 3650(TimedOut) = ล้มเหลว
//  (client/Durango.Network/Packet.cs:90-101 Packet.IsSuccess) และมี handler กลางที่เอา
//  Abort.Text ไปขึ้นข้อความบนจอให้เอง (client/GameManager.cs:308 ลงทะเบียน
//  Connections.Frontend.On<Abort>(DefaultAbortHandler) · ตัวเมธอดอยู่ที่ :348-351
//  → UIManager.SystemMsg(LimitText(msg.Text), 4f))
//  ⇒ ส่ง Abort พร้อมข้อความ = ผู้เล่นเห็นเหตุผล + UI ปลดล็อกถูกต้อง
//  (Abort ต้องมี Text เสมอ — default(Abort) ทำให้ LimitText(null) แครชฝั่งเกม)
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    // ข้อความปฏิเสธมาตรฐานของร้านค้า — ใช้ซ้ำทุกคำสั่งที่เป็น "การจ่ายเงิน"
    // **ค่าของเรา**: ต้นฉบับ NEXON ไม่มีข้อความนี้ (ฝั่งนั้นซื้อได้จริง) เราแต่งขึ้นเพื่อบอกความจริง
    private const string ShopDisabledText = "เซิร์ฟเวอร์นี้ไม่มีระบบร้านค้าเงินจริง จึงซื้อไอเทมนี้ไม่ได้";

    private void RegisterShopHandlers()
    {
        // ── รายการสินค้า ────────────────────────────────────────────────────────

        // GetCommodities (856700) — ขอรายการสินค้าที่ "ซื้อได้ตอนนี้"
        // ยิงจาก client/ShopSystem.cs:444-460 GetPurchasableCommodities ผ่าน AsyncCachedData
        // (cache 10 วิ) รอ .On<Commodities> และมี .Rest คลุมกรณีล้มเหลว — คำตอบถูกส่งต่อไป
        // SetPurchasableList (ShopSystem.cs:462-480) ซึ่งเดินลูปตาม KUtility.GetSize(infos)
        // ⇒ ชุดว่างปลอดภัย (ไม่ใช่ null ก็ได้ แต่ส่ง Array.Empty ชัดเจนกว่า)
        //
        // ตั้งใจตอบว่าง: รายการสินค้าจริงอยู่ใน commodities.json ก็จริง แต่ฝั่งเกมไม่ได้เช็ค
        // การชำระเงินเอง — ถ้าเราคืนรายการมา ปุ่ม "ซื้อ" จะกดได้และกลายเป็นของฟรีทันที
        _connection.Recv(delegate(GetCommodities msg, PacketHeader header)
        {
            Send(new Commodities { CommodityInfos = Array.Empty<CommodityInfo>() }, header.Seq);
        });

        // GetSpecialDeals (259680) — ดีลลดราคาแบบมีเวลาจำกัด
        // ยิงจาก client/ShopSystem.cs:192-198 (มี .On<SpecialDeals>) ⇒ ตอบผูก header.Seq
        // ระวัง: SetSpecialDeals (ShopSystem.cs:605-628) ถ้า Deals.Length > 0 จะตั้ง coroutine
        // ขอซ้ำตอนดีลหมดอายุ — ชุดว่างจึงไม่ทำให้เกิดลูปยิงซ้ำ
        // ข้อมูลจริงที่มี (special_deals.json) เป็นข้อความแบนเนอร์ล้วน ไม่มีราคา/เวลา ⇒ ว่าง
        _connection.Recv(delegate(GetSpecialDeals msg, PacketHeader header)
        {
            Send(new SpecialDeals { Deals = Array.Empty<SpecialDeal>() }, header.Seq);
        });

        // ── ประวัติ / ของที่ซื้อไว้แล้ว ─────────────────────────────────────────

        // GetPurchases (510397) — รายการของที่ซื้อแล้วแต่ยังไม่ได้กดรับ
        // ยิงแบบ **ไม่ผูก .On** (client/ShopSystem.cs:187-190) — ฝั่งเกมรับด้วย global
        // Connections.Frontend.On<Purchases>(OnPurchases) (ShopSystem.cs:76)
        // ⇒ ตอบแบบ ReplyOf=0 เหมือนกรณี GetSupportRequests ใน Player.Social.cs
        // OnPurchases (ShopSystem.cs:482-527) จะตัดรายการเก่าทิ้งให้เองเมื่อได้ชุดว่าง
        _connection.Recv(delegate(GetPurchases msg, PacketHeader header)
        {
            Send(new Purchases { _Purchases = Array.Empty<Purchase>() });
        });

        // GetAcceptableSubPurchases (259674) — "ของแถมย่อย" ในแพ็กเกจที่ยังกดรับได้
        // ยิงแบบ **ไม่ผูก .On** (client/ShopSystem.cs:200-203) — รับด้วย global
        // On<AcceptableSubPurchases> (ShopSystem.cs:77) ⇒ ตอบ ReplyOf=0
        // OnAcceptableSubPurchases (ShopSystem.cs:280-329) เช็ค msg.Ids == null แล้ว return
        // เฉย ๆ ซึ่งจะ **ไม่ยิง event AcceptableSubPurchasesUpdated** ⇒ ส่ง Array.Empty
        // (ไม่ใช่ null) เพื่อให้ฝั่งเกมเดินลูป 0 รอบแล้วอัปเดต UI ว่า "ไม่มีของค้างรับ" จริง ๆ
        _connection.Recv(delegate(GetAcceptableSubPurchases msg, PacketHeader header)
        {
            Send(new AcceptableSubPurchases { Ids = Array.Empty<AcceptableSubPurchase>() });
        });

        // GetUserFirstPurchaseHistory (856720) — ประวัติ "ซื้อครั้งแรก" ของแต่ละสินค้า
        // (ใช้ติดป้ายโบนัสซื้อครั้งแรก) ยิงจาก client/ShopSystem.cs:205-221 พร้อม
        // .On<UserFirstPurchaseHistory> ⇒ ตอบผูก header.Seq
        // ระวัง: callback วนลูป msg._UserFirstPurchaseHistory **โดยไม่เช็ค null**
        // (ShopSystem.cs:210-215) ⇒ ต้องส่ง Array.Empty เท่านั้น ห้ามปล่อยเป็น null
        _connection.Recv(delegate(GetUserFirstPurchaseHistory msg, PacketHeader header)
        {
            Send(new UserFirstPurchaseHistory { _UserFirstPurchaseHistory = Array.Empty<UserFirstPurchase>() }, header.Seq);
        });

        // ── คำสั่งที่เป็น "การจ่ายเงิน" — ปฏิเสธทุกครั้ง ────────────────────────

        // PurchaseCommodity (856710) — ซื้อด้วยสกุลเงินในเกม/เงินจริง
        // ยิงจาก client/ShopSystem.cs:529-552 รอ .On<Purchased> และมี .Rest(onFail)
        // onFail อยู่ที่ client/Durango.UI/ShopGroup.cs:733-742 → SetIsBuying(false)
        // (ปิดไอคอนโหลด) ⇒ ตอบ Abort แล้ว UI ปลดล็อกถูกต้อง + ขึ้นข้อความให้ผู้เล่นเห็น
        _connection.Recv(delegate(PurchaseCommodity msg, PacketHeader header)
        {
            Send(new Abort { Text = ShopDisabledText }, header.Seq);
        });

        // PurchaseCommodityWithVoucher (841253) — ซื้อด้วยบัตรกำนัล (voucher)
        // ยิงจากที่เดียวกัน (client/ShopSystem.cs:532-537 — เลือกสาขานี้เมื่อ
        // InventorySystem.Wallet.PurchasableVoucherCount(commodity) > 0) และรอ .On<Purchased>
        // เหมือนกันทุกอย่าง ⇒ ปฏิเสธด้วยเหตุผลเดียวกัน
        _connection.Recv(delegate(PurchaseCommodityWithVoucher msg, PacketHeader header)
        {
            Send(new Abort { Text = ShopDisabledText }, header.Seq);
        });

        // AcceptPurchase (5247809) — กดรับของที่ซื้อไว้ (ทั้งชิ้น หรือเฉพาะของแถมย่อย SubId)
        // ยิง 2 จุด: client/ShopSystem.cs:568-594 AcceptPurchase (ไม่มี SubId) และ
        //           client/ShopSystem.cs:331-396 AcceptSubPurchase (มี SubId)
        // ทั้งคู่ใช้ .All(packet => Packet.IsSuccess(packet)) ⇒ Abort = ล้มเหลว แล้วฝั่งเกม
        // ถอยกลับด้วยการยิง GetPurchases/GetAcceptableSubPurchases ใหม่ (ได้ชุดว่างจากด้านบน)
        // ⇒ รายการที่มันลบออกไปล่วงหน้าจะถูกซิงก์กลับให้ตรงความจริงเอง ไม่มีลูปค้าง
        // หมายเหตุ: เราไม่เคยแจก Purchase ให้เลย ⇒ PurchaseId ที่เข้ามาย่อมไม่มีอยู่จริงเสมอ
        _connection.Recv(delegate(AcceptPurchase msg, PacketHeader header)
        {
            Send(new Abort { Text = "ไม่พบรายการสั่งซื้อนี้" }, header.Seq);
        });

        // AcceptTENCoupon (2345690) — กรอกโค้ดคูปองของ NEXON (TEN) เพื่อรับของทางกล่องจดหมาย
        // ยิงจาก client/Durango.System.Config/ConfigInstance.cs:620-633 SendCoupon
        // ส่ง CouponNum + ToyToken (โทเคนบัญชี NEXON) แล้วรอ **.On<OK>** อย่างเดียว
        // (ไม่มี .Rest) — ถ้าได้ OK จะขึ้นแจ้งเตือน "ส่งของขวัญแล้ว ตรวจกล่องจดหมาย"
        // ⇒ ห้ามตอบ OK เด็ดขาด (จะโกหกว่ามีของส่งเข้ากล่องจดหมาย)
        // ตอบ Abort แทน: .On<OK> ไม่ทำงาน แต่ DefaultAbortHandler ขึ้นข้อความให้ผู้เล่นเห็น
        // (การตรวจคูปองจริงต้องยิงไปเซิร์ฟ NEXON ซึ่งเซิร์ฟนี้ทำไม่ได้และไม่ควรทำ)
        _connection.Recv(delegate(AcceptTENCoupon msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ใช้คูปองไม่ได้" }, header.Seq);
        });

        // TransferDurangoCoin (5092384) — โอน "ดูรังโกคอยน์" (สกุลเงินเติมด้วยเงินจริง) ให้ผู้เล่นอื่น
        // ยิงจาก client/ShopSystem.cs:647-660 SendDurangoCoin (อยู่ในไฟล์เดียวกับร้านค้า
        // และยิงบน Connections.Frontend = สายเดียวกับไฟล์นี้) ปุ่มยืนยันอยู่ที่
        // client/Durango.UI.Popup/TransferCoinPopup.cs:186
        //
        // เหตุผลที่ต้องมี handler ทั้งที่โอนจริงไม่ได้:
        //   1) ฝั่งเกมใช้ .All(packet => ...) ⇒ ถ้าเซิร์ฟ **ไม่ตอบอะไรเลย** callback ไม่ถูกเรียก
        //      และ entry ใน _replyPacketHandlers ค้างถาวร — HandleMsg ลบทิ้งได้ก็ต่อเมื่อมีแพ็กเก็ต
        //      ที่ ReplyOf ตรงกันเข้ามา (client/Durango.Network/Connection.cs:905-908)
        //   2) TransferCoinPopup เรียก Hide() ทันทีหลังกดยืนยัน ⇒ ถ้าเงียบ ผู้เล่นจะเห็นแค่หน้าต่างปิด
        //      แล้วเข้าใจว่า "โอนสำเร็จ" ทั้งที่ไม่มีอะไรเกิดขึ้น
        // ตอบ Abort ⇒ IsSuccess=false (onSuccess ไม่ทำงาน = ไม่มีป๊อปอัป "โอนสำเร็จ" หลอก)
        // + DefaultAbortHandler ขึ้นข้อความบอกเหตุผลให้ผู้เล่นเห็น
        // **ค่าของเรา**: เซิร์ฟนี้ไม่มีกระเป๋าดูรังโกคอยน์จริง (grep Wallet ใน server/Core/ พบแต่
        // การส่งค่า null ให้ฝั่งเกม) ⇒ หักเงินผู้ส่งไม่ได้ ถ้าตอบ OK จะกลายเป็นเสกเงินให้ผู้รับฟรี
        _connection.Recv(delegate(TransferDurangoCoin msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ไม่มีระบบดูรังโกคอยน์ จึงโอนให้กันไม่ได้" }, header.Seq);
        });
    }
}
