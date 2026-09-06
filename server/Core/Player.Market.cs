using System;
using System.Collections.Generic;
using Durango.Network;
using Messages;

namespace Durango.Online;

// ═══════════════════════════════════════════════════════════════════════════════════
//  ตลาดผู้เล่น (Market) — แท็บประวัติ / ประกาศขาย / รายการโปรด / รับเงินค่าขาย
//
//  เซิร์ฟเรามีตลาดแค่ "แคตตาล็อกกลาง" (server/Core/MarketManager.cs:21 Products —
//  ปั้นของจาก prototype ที่ติดแท็ก door/window/wall_deco/... แล้วให้ซื้อได้ฟรี)
//  ยังไม่มีระบบ "ผู้เล่นประกาศขายเอง" จริง ⇒ ไม่มีรายการประกาศ/ขายแล้ว/ซื้อแล้ว ของใครทั้งนั้น
//
//  ปัญหาเดิม: เกมเปิดหน้าตลาด → ยิง 5011/5012/5013 มาแล้วเซิร์ฟเงียบ
//  ฝั่งเกมตั้ง Request.IsLoading = true ไว้ก่อนยิง (client/Durango.Logic.Market/Commodities.cs:43)
//  แล้วเคลียร์ธงนี้ได้ที่เดียวคือใน OnResult ตอนได้ Products กลับ (Commodities.cs:101-103)
//  ⇒ ไม่ตอบ = แท็บนั้นหมุน "กำลังโหลด" ค้างตลอดกาล และกดโหลดหน้าถัดไปไม่ได้อีกเลย
//  ⇒ ต้องตอบ Products **ว่างแต่ถูกโครงสร้าง**
//  (ข้อเท็จจริงบนสายจริง: default(Products) กับ Array ว่าง แพ็กออกไปเหมือนกันเป๊ะ —
//   Products.Pack แปลง _Products=null เป็น PackArrayHeader(0) ให้อยู่แล้ว
//   ที่ server/GameCode/Messages/Products.cs:22-25 และ Products.Unpack คืน new Product[num]
//   ซึ่งไม่มีวันเป็น null ⇒ ที่เขียน Array.Empty<Product>() ไว้คือ "อ่านแล้วเจตนาชัด"
//   ไม่ใช่การกันบั๊ก — อย่าเข้าใจผิดว่า default(Products) ทำให้ฝั่งเกมได้ null)
//
//  หมายเหตุทิศทาง: Products (5100) เป็น **เซิร์ฟ→เกม** อย่างเดียว จุดที่ "ส่ง" Products
//  มีแค่ client/Durango.Online/Player.cs:908 ซึ่งคือเซิร์ฟจำลองที่ฝังในตัวเกม ไม่ใช่ฝั่งผู้เล่น
//  ⇒ **ไม่ลงทะเบียน handler ให้ 5100**
//
//  ตัวที่ลงทะเบียนไว้ที่อื่นแล้ว (ห้ามซ้ำ เพราะ Recv ตัวหลังทับตัวหน้า):
//    · GetExpiredProducts (5015)  → server/Core/Player.Social.cs:84
//    · SearchProducts / GetFavoriteProducts / BuyProduct → server/Core/Player.cs:228-240
//
//  ต่อสายแล้ว (ตรวจซ้ำ 7 ก.ย. 2026): RegisterSystemHandlers() เรียก RegisterMarketHandlers()
//  อยู่ที่ server/Core/Player.Systems.cs:58 แล้ว — **ห้ามเพิ่มบรรทัดเรียกซ้ำอีก**
//  (มีเมธอดชื่อนี้นิยามไว้ที่เดียวคือไฟล์นี้ บรรทัดนั้นจึงชี้มาที่นี่แน่นอน)
//  RegisterSystemHandlers() ถูกเรียกที่ Player.cs:550 ซึ่งอยู่หลังบล็อก Recv ที่ Player.cs:228-240
//  ⇒ ลงที่นี่ทีหลังจะทับของ Player.cs ได้ แต่ 8 ตัวในไฟล์นี้ TypeCode ไม่ชนกับใคร วางตรงไหนก็ได้
// ═══════════════════════════════════════════════════════════════════════════════════

public partial class Player
{
    // รายการโปรดของผู้เล่นคนนี้ (เก็บเฉพาะในหน่วยความจำระหว่างเชื่อมต่อ — ยังไม่ผูก PlayerContext)
    // เก็บเป็น "รหัสสินค้า" ของแคตตาล็อกกลาง (MarketManager.MakeProduct ตั้ง Id = Guid ใหม่ตอนสร้าง
    // ครั้งแรก แล้วแคชไว้ใน _products ⇒ รหัสคงที่ตลอดอายุ World)  **การตีความของเรา**
    private readonly HashSet<string> _marketFavoriteProductIds = new();

    private void RegisterMarketHandlers()
    {
        // ── แท็บประวัติในหน้าตลาด ───────────────────────────────────────────────────
        // ทั้งสามตัวเดินผ่านทางเดียวกัน: MarketHistoryWidget เลือกแท็บ → Commodities.Get(reset)
        // (client/Durango.Logic.Market/Commodities.cs:31) → CreateGetProductMessage (Commodities.cs:59)
        // → MarketSystem.GetProducts (client/MarketSystem.cs:222) → SetProductsHandler (:227)
        // ซึ่งผูก .On<Products> ไว้ตัวเดียวที่ :232  ⇒ ต้องตอบ Products เท่านั้น ตอบ Abort ไม่ได้
        // (Abort จะตกไปเข้า .Rest (:238) → onResult(null) → Commodities.cs:52 `if (product.HasValue)`
        //  เป็นเท็จ ⇒ ไม่เข้า OnResult ⇒ IsLoading ค้าง true ถาวร แท็บนั้นพังไปเลย)

        // GetRegisteredProducts (5011) — แท็บ "ของที่ลงประกาศไว้"
        // ยิงจาก client/Durango.Logic.Market/Commodities.cs:71 (ProductType.Registered)
        // เซิร์ฟไม่มีระบบให้ผู้เล่นลงประกาศ ⇒ ประกาศของผู้เล่นย่อมไม่มี → ตอบชุดว่างจริงตามสภาพ
        _connection.Recv(delegate(GetRegisteredProducts msg, PacketHeader header)
        {
            Send(new Products { _Products = Array.Empty<Product>() }, header.Seq);
        });

        // GetSoldProducts (5012) — แท็บ "ประวัติการขาย"
        // ยิงจาก client/Durango.Logic.Market/Commodities.cs:83 (ProductType.Sold)
        // ไม่มีการขายเกิดขึ้นได้เลยบนเซิร์ฟนี้ ⇒ ตอบชุดว่าง (ห้ามแต่งประวัติปลอม)
        _connection.Recv(delegate(GetSoldProducts msg, PacketHeader header)
        {
            Send(new Products { _Products = Array.Empty<Product>() }, header.Seq);
        });

        // GetPurchasedProducts (5013) — แท็บ "ประวัติการซื้อ"
        // ยิงจาก client/Durango.Logic.Market/Commodities.cs:77 (ProductType.Purchased)
        // BuyProduct ของเรา (server/Core/Player.cs:1604 HandleBuyProductMsg) แจกของแล้วจบ
        // ไม่ได้บันทึกใบเสร็จไว้
        // ⇒ ยังไม่มีประวัติให้คืน → ตอบชุดว่าง  **การตีความของเรา**
        _connection.Recv(delegate(GetPurchasedProducts msg, PacketHeader header)
        {
            Send(new Products { _Products = Array.Empty<Product>() }, header.Seq);
        });

        // ── ลงประกาศขายของ ─────────────────────────────────────────────────────────
        // RegisterMultipleProducts (82309) — ยืนยันในหน้าต่างตั้งราคาแล้วกดลงประกาศ
        // client/Durango.UI/SellItemWidget.cs:193 → MarketSystem.RegisterCommodity
        // → client/MarketSystem.cs:166 Send(new RegisterMultipleProducts{...}) แบบ **ไม่ผูก .On**
        // ⇒ Abort จะตกเข้า handler กลาง GameManager.DefaultAbortHandler
        //   (ผูกไว้ที่ client/GameManager.cs:308 ตัวเมธอดอยู่ที่ :348)
        //   ซึ่งเด้ง SystemMsg ให้ผู้เล่นเห็น — ดีกว่าเงียบแล้วผู้เล่นนึกว่าลงประกาศสำเร็จ
        // สำคัญ: เราไม่ยึดของออกจากกระเป๋า ⇒ ผู้เล่นไม่เสียของ
        _connection.Recv(delegate(RegisterMultipleProducts msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่เปิดใช้งานระบบลงประกาศขายของผู้เล่น" }, header.Seq);
        });

        // ── รายการโปรด ─────────────────────────────────────────────────────────────
        // ทั้งสองตัวยิงจาก MarketSystem.ToggleFavorite (client/MarketSystem.cs:258-271) แล้วรับ
        // ผลผ่าน SetProductsHandler เหมือนแท็บประวัติ: .On<Products> → เอา Id ทุกตัวในคำตอบไป
        // **แทนที่** _favoritesIds ทั้งชุด (client/MarketSystem.cs:274-280 Clear() แล้ว AddRange())
        // ⇒ คำตอบต้องเป็น "รายการโปรดทั้งหมดหลังแก้แล้ว" ไม่ใช่แค่ตัวที่เพิ่งกด
        // ของที่คืนไปเป็นสินค้าจริงจากแคตตาล็อกกลาง (MarketManager.Products) ไม่ได้แต่งขึ้นเอง
        //
        // ข้อจำกัดที่ต้องรู้ (ไม่ใช่บั๊กของไฟล์นี้ แต่กระทบผลลัพธ์ที่ผู้เล่นเห็น):
        // ToggleFavorite เข้าได้ต่อเมื่อ _favoritesIds ไม่เป็น null (client/MarketSystem.cs:260)
        // ซึ่งตั้งครั้งเดียวจากคำตอบ GetFavoriteProducts (client/MarketSystem.cs:309) — เซิร์ฟเรา
        // ตอบตัวนั้นด้วย default(Products) ที่ Player.cs:232 ⇒ แพ็กเป็น array ว่าง ⇒ ฝั่งเกมได้
        // HashSet ว่าง (ไม่ใช่ null) ⇒ ระบบโปรด "เปิดใช้ได้" และ 123482/423809 ยิงมาถึงเราจริง
        // แต่ **แท็บรายการโปรด** (ProductType.Favorites, Commodities.cs:94-95) เดินผ่าน
        // GetFavoriteProducts เหมือนกัน ⇒ ยังโชว์ว่างอยู่ดีจนกว่าคนดูแล Player.cs จะให้ตัวนั้น
        // ตอบจากชุดเดียวกันนี้ (_marketFavoriteProductIds)  — เราแตะไฟล์นั้นไม่ได้

        // AddToFavoriteProducts (123482) — กดหัวใจติดดาวสินค้า
        _connection.Recv(delegate(AddToFavoriteProducts msg, PacketHeader header)
        {
            if (!string.IsNullOrEmpty(msg.ProductId))
            {
                _marketFavoriteProductIds.Add(msg.ProductId);
            }
            Send(BuildFavoriteProducts(), header.Seq);
        });

        // RemoveFromFavoriteProducts (423809) — กดเอาออกจากรายการโปรด
        _connection.Recv(delegate(RemoveFromFavoriteProducts msg, PacketHeader header)
        {
            if (!string.IsNullOrEmpty(msg.ProductId))
            {
                _marketFavoriteProductIds.Remove(msg.ProductId);
            }
            Send(BuildFavoriteProducts(), header.Seq);
        });

        // ── รับเงินค่าขาย ───────────────────────────────────────────────────────────
        // ปุ่มพวกนี้โผล่เฉพาะแท็บ "ประวัติการขาย" ซึ่งบนเซิร์ฟเราว่างเสมอ (ดู 5012 ข้างบน)
        // ⇒ ตามทฤษฎีเกมยิงมาไม่ได้ แต่ลงทะเบียนกันไว้ ไม่ให้หลุดเป็น log "ไม่มี handler"

        // MarketCollectPayment (5101) — กดปุ่ม "รับ" ที่การ์ดสินค้าที่ขายไปแล้วหนึ่งชิ้น
        // client/Durango.UI/CommodityNode.cs:246 ยิงแบบ **ไม่ผูก .On**
        // ⇒ Abort ไปโผล่เป็น SystemMsg ผ่าน DefaultAbortHandler
        _connection.Recv(delegate(MarketCollectPayment msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่มีรายได้จากการขายให้รับ" }, header.Seq);
        });

        // MarketCollectAllPayments (5102) — ปุ่ม "รับทั้งหมด" ท้ายแท็บประวัติการขาย
        // client/Durango.UI/MarketHistoryWidget.cs:278 ผูก .On<Products> ไว้ แล้วถ้าได้ Products
        // กลับจะสั่ง PaymentReceived() → CommodityList.PaymentReceived(null) ซึ่งไปไล่ตั้งสถานะ
        // ทุกใบในลิสต์เป็น "รับเงินแล้ว" (client/Durango.UI/CommodityList.cs:234-268)
        // ⇒ ตอบ Products = โกหกว่าจ่ายแล้ว จึงตอบ Abort แทน
        //   ตรวจทางเดินของ Abort แล้ว: HandleMsg (client/Durango.Network/Connection.cs:868) หา
        //   handler เฉพาะ seq ก่อน — ที่ผูกไว้มีแต่ Products (5100) ไม่มี Abort (1024) ⇒ ตกไป
        //   ใช้ handler กลางที่ :885 ซึ่งคือ DefaultAbortHandler (ผูกที่ client/GameManager.cs:308
        //   ตัวเมธอดที่ :348 → UIManager.SystemMsg)
        //   ⇒ ผู้เล่นเห็น SystemMsg และรายการไม่ถูกตีเป็น "รับเงินแล้ว"  (ไม่มี .Rest ผูกไว้ที่นี่
        //   ⇒ replyMessageHandler.Handler เป็น null ไม่มีผลข้างเคียงอื่น)
        _connection.Recv(delegate(MarketCollectAllPayments msg, PacketHeader header)
        {
            Send(new Abort { Text = "ยังไม่มีรายได้จากการขายให้รับ" }, header.Seq);
        });
    }

    // คัดสินค้าจากแคตตาล็อกกลางเฉพาะตัวที่อยู่ในรายการโปรด แล้วห่อเป็น Products
    // (โครง Products ตาม server/GameCode/Messages/Products.cs — ฟิลด์ชื่อ _Products)
    //
    // ตัวกรอง Items != null: MakeProduct จะไม่ใส่ Items ให้เลยถ้า Cheats.MakeItem คืน null
    // (server/Core/MarketManager.cs:65-69) ⇒ แคตตาล็อกมีรายการ "เปล่า" ปนอยู่ได้
    // เราตัดทิ้งด้วยเกณฑ์เดียวกับที่ MarketManager.SearchProduct ใช้ (MarketManager.cs:94)
    // เพื่อไม่ให้ส่งของที่ฝั่งเกมเอาไปสร้าง Commodity ไม่ได้ออกไป
    private Products BuildFavoriteProducts()
    {
        List<Product> picked = new();
        if (_marketFavoriteProductIds.Count > 0)
        {
            Product[] all = _world.MarketManager.Products;
            for (int i = 0; i < all.Length; i++)
            {
                if (!string.IsNullOrEmpty(all[i].Id)
                    && all[i].Items != null
                    && _marketFavoriteProductIds.Contains(all[i].Id))
                {
                    picked.Add(all[i]);
                }
            }
        }
        return new Products { _Products = picked.ToArray() };
    }
}
