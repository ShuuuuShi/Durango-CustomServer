using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Logic;
using Durango.Utils.Extensions;
using Messages;
using Shared.Economy;
using Shared.Market;
using Yaml;
using Yaml.Util;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/MarketManager.cs (shim ตลาดของเซิร์ฟออฟไลน์ต้นฉบับ)
public class MarketManager
{
    private Product[] _products;

    private static readonly string[] Tags = { "door", "window", "wall_deco", "empty_door", "plantable", "armor", "weapon", "instrument" };

    // [6 ก.ย. 2026] วัสดุ — ผู้เล่นขอให้ "กดรับวัสดุจากตลาดได้เลย ไม่ต้องเดินเก็บเอง" (เบต้าเทส)
    // หมวดมาจากข้อมูลจริง item/prototype_data.json → category:
    //   material = วัสดุแปรรูป 384 · mineral = หิน/แร่ 54 ·
    //   plant_collectible = ไม้/กิ่ง/ผลดิบ 52 · animal_collectible = หนัง/เอ็น 56
    // หมวดพวกนี้เป็นหมวดเดียวกับที่ฝั่งเกมสร้างแท็บตลาด (nexonSRC/MarketSystem.cs:108-152
    // InitMarketCategoires ลูป prototype เดียวกัน) ⇒ ค้นหมวดไหนก็เจอของหมวดนั้น
    private static readonly string[] MaterialCategories = { "material", "mineral", "plant_collectible", "animal_collectible" };

    public Product[] Products
    {
        get
        {
            if (_products == null)
            {
                var list = new List<Product>();
                var added = new HashSet<string>();
                foreach (var pair in SingletonDict<string, List<Prototype>>.Instance)
                {
                    if (pair.Value == null) continue;
                    bool byTag = Tags.Any(tag => pair.Value.Any(x => x.Tags.ContainsKey(tag)));
                    bool byRein = IsCraftRein(pair.Key);
                    bool byMaterial = pair.Value.Any(x => x.Category != null && MaterialCategories.Contains(x.Category));
                    if ((byTag || byRein || byMaterial) && added.Add(pair.Key))
                    {
                        list.Add(MakeProduct(pair.Key));
                    }
                }
                _products = list.ToArray();
            }
            return _products;
        }
    }

    private static bool IsCraftRein(string prototypeId)
    {
        PerformanceYaml.Rein rein = PerformanceYaml.GetRein(prototypeId);
        return rein != null && SingletonDict<int, Yaml.Pet>.TryGetValue(rein.PetEntityType, out var value) && value.IsCraft;
    }

    private Product MakeProduct(string prototypeId)
    {
        Product result = new()
        {
            Id = Guid.NewGuid().ToString(),
            RegionId = "1",
            ListedAt = 0.0,
            ExpiresAt = 0.0,
            DeletesAt = 0.0,
            PurchasedAt = null,
            Price = 0L,
            Fee = 0L,
            Currency = Currency.TStone,
            State = ProductState.Registered,
            Level = 60,
            Durability = 10000f
        };
        Item? item = Cheats.MakeItem(prototypeId, result.Level);
        if (item.HasValue)
        {
            result.Items = new[] { item.Value };
        }
        return result;
    }

    public Item[] BuyProduct(string productId)
    {
        Product value = Products.FirstOrDefault(p => p.Id == productId);
        if (string.IsNullOrEmpty(value.Id)) return null;
        Item item = value.Items.FirstOrDefault();
        if (string.IsNullOrEmpty(item.Prototype)) return null;
        int num = Array.IndexOf(Products, value);
        Item? item2 = Cheats.MakeItem(item.Prototype, item.Level);
        if (item2.HasValue)
        {
            Products[num].Items = new[] { item2.Value };
        }
        return value.Items;
    }

    public Products SearchProduct(SearchProducts option)
    {
        IEnumerable<Product> products = Products;
        products = products
            .Where(p =>
            {
                if (p.Items == null) return false;
                string id = p.Items.FirstOrDefault(item =>
                {
                    if (!string.IsNullOrEmpty(option.ItemName) && !string.IsNullOrEmpty(item.Name) &&
                        !item.Name.Contains(option.ItemName))
                    {
                        return false;
                    }
                    Prototype prototype = PrototypeYaml.GetItemPrototype(item.Prototype);
                    if (prototype == null) return false;
                    if (KUtility.GetSize(option.SubCategories) > 0 &&
                        option.SubCategories.All(subCategory => !prototype.SubCategories.Any(s => s == subCategory)))
                    {
                        return false;
                    }
                    return string.IsNullOrEmpty(option.Category) || string.IsNullOrEmpty(prototype.Category) ||
                           prototype.Category == option.Category;
                }).Id;
                return !string.IsNullOrEmpty(id);
            })
            .Skip(option.Skip)
            .Take(OptionSystem.GetMarketSearchLimit());
        return new Products { _Products = products.ToArray() };
    }
}
