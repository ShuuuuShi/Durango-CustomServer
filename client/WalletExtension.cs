using Durango.Logic.Shop;
using Durango.System;
using JetBrains.Annotations;
using Messages;
using Shared.Economy;
using Shared.Voucher;
using UnityEngine;
using Yaml;
using Yaml.Util;

public static class WalletExtension
{
	public static long GetBalance(this Wallet wallet, Currency currency)
	{
		return wallet.GetPaidBalance(currency) + wallet.GetUnpaidBalance(currency);
	}

	public static long GetPaidBalance(this Wallet wallet, Currency currency)
	{
		return (wallet.PaidBalances != null) ? wallet.PaidBalances.Get(currency.Normalize(), 0L) : 0;
	}

	public static long GetUnpaidBalance(this Wallet wallet, Currency currency)
	{
		return (wallet.UnpaidBalances != null) ? wallet.UnpaidBalances.Get(currency.Normalize(), 0L) : 0;
	}

	// [7 ก.ย. 2026] เซิร์ฟนี้ใช้สกุลเงินเดียวคือ T Stone — ยุบทุกสกุลมาที่ตัวเดียว
	//
	// ทำไมแก้ที่นี่จุดเดียว: Normalize() เป็นคอขวดที่ทุกเส้นทางเงินฝั่งเกมผ่านหมด
	//   · ยอดเงิน  — GetPaidBalance / GetUnpaidBalance (สองเมธอดข้างบนไฟล์นี้)
	//   · ข้อความ  — Durango.Logic.Item/Inventory.cs:195 CurrencyFormat
	//                 · :213 CurrencyEmphasisFormat
	//   · ไอคอน    — Durango.Logic.Item/Inventory.cs:248 GetIcon
	// ⇒ แก้ตรงนี้แล้วจุดที่อ้าง Currency.Gem / WarpMatter / Coin ฯลฯ อีก 88 จุดใน 25 ไฟล์
	//   เปลี่ยนตามเองทั้งหมด ไม่ต้องไล่แก้ทีละที่ (และไม่ต้องแตะ enum ใน GameCode ซึ่งห้ามแก้)
	//
	// ต้นฉบับออกแบบเมธอดนี้ไว้แปลงสกุลอยู่แล้ว — ของเดิมแปลง Coin → MobileCoin/PcCoin
	// ตามแพลตฟอร์ม เราแค่ขยายให้แปลงทุกสกุลเป็น TStone
	//
	// ฝั่งเซิร์ฟเป็นตัวบังคับจริง: กระเป๋าที่ส่งมามีคีย์เดียวคือ Currency.TStone และการหักเงิน
	// ทุกครั้งผ่าน Player.TrySpendTStone (server/Core/Player.Wallet.cs) ⇒ ต่อให้หน้าจอไหน
	// ยังเขียนว่าจ่ายด้วยเพชร เซิร์ฟก็หัก T Stone อยู่ดี
	public static Currency Normalize(this Currency type)
	{
		if (type == Currency.Invalid)
		{
			return Currency.Invalid;
		}
		return Currency.TStone;
	}

	public static int GetVoucherCount(this Wallet wallet, [CanBeNull] string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return 0;
		}
		int i = 0;
		for (int size = KUtility.GetSize(wallet.Vouchers); i < size; i++)
		{
			VoucherInfo voucherInfo = wallet.Vouchers[i];
			if (voucherInfo.VoucherId == id)
			{
				return voucherInfo.Count;
			}
		}
		return 0;
	}

	public static bool HasVouchers(this Wallet wallet, GuideType type)
	{
		int i = 0;
		for (int size = KUtility.GetSize(wallet.Vouchers); i < size; i++)
		{
			VoucherInfo voucherInfo = wallet.Vouchers[i];
			if (voucherInfo.Count > 0 && SingletonDict<string, Voucher>.TryGetValue(voucherInfo.VoucherId, out var value) && value.GuideType == type)
			{
				return true;
			}
		}
		return false;
	}

	public static int PurchasableVoucherCount(this Wallet wallet, Durango.Logic.Shop.Commodity commodity)
	{
		if (!commodity.VoucherPurchasable())
		{
			return 0;
		}
		int voucherCount = wallet.GetVoucherCount(commodity.Data.VoucherId);
		return Mathf.FloorToInt((float)voucherCount / (float)commodity.Data.VoucherAmount);
	}
}
