using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils.Extensions;
using Messages;
using Shared.Economy;
using Shared.Market;
using Yaml;
using Yaml.Util;

namespace Durango.Online;

public class MarketManager
{
	private Product[] _products;

	private static readonly string[] Tags = new string[8] { "door", "window", "wall_deco", "empty_door", "plantable", "armor", "weapon", "instrument" };

	public Product[] Products
	{
		get
		{
			if (_products == null)
			{
				List<Product> list = new List<Product>();
				list.AddRange(from prototype in SingletonDict<string, List<Prototype>>.Instance.Where(delegate(KeyValuePair<string, List<Prototype>> pair)
					{
						if (pair.Value == null)
						{
							return false;
						}
						List<Prototype> prototypes = pair.Value;
						return Tags.Any((string tag) => prototypes.Any((Prototype x) => x.Tags.ContainsKey(tag))) ? true : false;
					})
					select MakeProduct(prototype.Key));
				list.AddRange(from prototype in SingletonDict<string, List<Prototype>>.Instance.Where(delegate(KeyValuePair<string, List<Prototype>> pair)
					{
						PerformanceYaml.Rein rein = PerformanceYaml.GetRein(pair.Key);
						Yaml.Pet value;
						return rein != null && SingletonDict<int, Yaml.Pet>.TryGetValue(rein.PetEntityType, out value) && value.IsCraft;
					})
					select MakeProduct(prototype.Key));
				_products = list.ToArray();
			}
			return _products;
		}
	}

	private Product MakeProduct(string prototypeId)
	{
		Product result = new Product
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
			result.Items = new Item[1] { item.Value };
		}
		return result;
	}

	public Item[] BuyProduct(string productId)
	{
		Product value = Products.FirstOrDefault((Product product) => product.Id == productId);
		if (string.IsNullOrEmpty(value.Id))
		{
			return null;
		}
		Item item = value.Items.FirstOrDefault();
		if (string.IsNullOrEmpty(item.Prototype))
		{
			return null;
		}
		int num = Products.IndexOf(value);
		Item? item2 = Cheats.MakeItem(item.Prototype, item.Level);
		if (item2.HasValue)
		{
			Products[num].Items = new Item[1] { item2.Value };
		}
		return value.Items;
	}

	public Products SearchProduct(SearchProducts option)
	{
		IEnumerable<Product> products = Products;
		products = products.Where(delegate(Product product)
		{
			if (product.Items == null)
			{
				return false;
			}
			string id = product.Items.FirstOrDefault(delegate(Item item)
			{
				if (!string.IsNullOrEmpty(option.ItemName) && !string.IsNullOrEmpty(item.Name) && !item.Name.Contains(option.ItemName))
				{
					return false;
				}
				Prototype prototype = PrototypeYaml.GetItemPrototype(item.Prototype);
				if (prototype == null)
				{
					return false;
				}
				if (KUtility.GetSize(option.SubCategories) > 0)
				{
					if (KUtility.GetSize(option.SubCategories) <= 0)
					{
						return false;
					}
					if (option.SubCategories.All((string subCategory) => !prototype.SubCategories.Any((string s) => s == subCategory)))
					{
						return false;
					}
				}
				return string.IsNullOrEmpty(option.Category) || string.IsNullOrEmpty(prototype.Category) || prototype.Category == option.Category;
			}).Id;
			return !string.IsNullOrEmpty(id);
		}).Skip(option.Skip).Take(OptionSystem.GetMarketSearchLimit());
		return new Products
		{
			_Products = products.ToArray()
		};
	}
}
