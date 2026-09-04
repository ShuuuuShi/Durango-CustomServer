using System;
using System.Collections.Generic;
using Durango.Logic.Clusters;
using Durango.Network;
using JetBrains.Annotations;
using Messages;

namespace Durango.Logic.Market;

public class Commodities
{
	public readonly RequestOption Request = new RequestOption();

	public readonly List<Commodity> Goods = new List<Commodity>();

	[CanBeNull]
	public SearchOption SearchOption;

	public event Action GoodsListUpdated;

	public event Action<bool> OnRequestGoodsList;

	public void Reset()
	{
		Goods.Clear();
		Request.Index = 0;
		Request.NoMore = false;
		Request.IsLoading = false;
	}

	public void Get(bool reset)
	{
		if (reset)
		{
			Reset();
		}
		if (Request.NoMore || Request.IsLoading)
		{
			return;
		}
		int index = Request.Index;
		Request.Index += OptionSystem.GetMarketSearchLimit();
		Request.IsLoading = true;
		UIManager.ShowLoadingIcon(!reset);
		if (OnRequestGoodsList != null)
		{
			OnRequestGoodsList(reset);
		}
		ReplyMessageHandlerRegistrar wrappedMessage = CreateGetProductMessage(Request.Type, Request.Condition, index);
		GameSystem<MarketSystem>.Instance().GetProducts(wrappedMessage, delegate(Products? product)
		{
			if (product.HasValue)
			{
				OnResult(product.Value);
			}
		});
	}

	public ReplyMessageHandlerRegistrar CreateGetProductMessage(ProductType type, SortCondition condition, int pageIndex)
	{
		switch (type)
		{
		case ProductType.Searched:
		{
			SearchProducts msg = ((SearchOption != null) ? SearchOption.ToMessage() : default(SearchProducts));
			msg.Skip = pageIndex;
			msg.Sort = condition;
			return MarketSystem.Send(msg);
		}
		case ProductType.Registered:
			return MarketSystem.Send(new GetRegisteredProducts
			{
				Skip = pageIndex,
				Sort = condition
			});
		case ProductType.Purchased:
			return MarketSystem.Send(new GetPurchasedProducts
			{
				Skip = pageIndex,
				Sort = condition
			});
		case ProductType.Sold:
			return MarketSystem.Send(new GetSoldProducts
			{
				Skip = pageIndex,
				Sort = condition
			});
		case ProductType.Expired:
			return MarketSystem.Send(new GetExpiredProducts
			{
				Skip = pageIndex,
				Sort = condition
			});
		case ProductType.Favorites:
			return MarketSystem.Send(default(GetFavoriteProducts));
		default:
			throw new ArgumentOutOfRangeException("type", type, null);
		}
	}

	private void OnResult(Products products)
	{
		Request.IsLoading = false;
		UIManager.ShowLoadingIcon(show: false);
		bool flag = products._Products == null || products._Products.Length == 0;
		if (!flag)
		{
			flag = true;
			for (int i = 0; i < products._Products.Length; i++)
			{
				string id = products._Products[i].Id;
				int num = -1;
				for (int j = 0; j < Goods.Count; j++)
				{
					if (Goods[j].Id == id)
					{
						num = j;
						break;
					}
				}
				if (num == -1)
				{
					Commodity commodity = new Commodity();
					commodity.Set(products._Products[i]);
					Goods.Add(commodity);
					flag = false;
				}
				else
				{
					Goods[num].Set(products._Products[i]);
				}
			}
		}
		if (flag)
		{
			if (Request.NoMore)
			{
				return;
			}
			Request.NoMore = true;
		}
		if (GoodsListUpdated != null)
		{
			GoodsListUpdated();
		}
	}

	public void Buy(Commodity item)
	{
		if (item == null || InventorySystem.Wallet.GetBalance(item.CurrencyType) < item.Price || item.IsWaiting())
		{
			return;
		}
		item.SetToWait();
		GameSystem<MarketSystem>.Instance().BuyCommodity(item.Id, delegate(bool success)
		{
			item.Responsed();
			if (GameManager.ClusterMode == Mode.Online && success && Goods.Remove(item) && GoodsListUpdated != null)
			{
				GoodsListUpdated();
			}
		});
	}

	public void Unregister(Commodity item)
	{
		if (item == null || item.IsWaiting())
		{
			return;
		}
		item.SetToWait();
		GameSystem<MarketSystem>.Instance().UnregisterCommodity(item.Id, delegate(bool success)
		{
			item.Responsed();
			if (success && Goods.Remove(item) && GoodsListUpdated != null)
			{
				GoodsListUpdated();
			}
		});
	}

	public void Withdraw(Commodity item)
	{
		if (item == null || item.IsWaiting())
		{
			return;
		}
		item.SetToWait();
		GameSystem<MarketSystem>.Instance().WithdrawCommodity(item.Id, delegate(bool success)
		{
			item.Responsed();
			if (success && Goods.Remove(item))
			{
				if (Goods.Count == 0)
				{
					GameSystem<MarketSystem>.Instance().GetExpiredProduct();
				}
				if (GoodsListUpdated != null)
				{
					GoodsListUpdated();
				}
			}
		});
	}
}
