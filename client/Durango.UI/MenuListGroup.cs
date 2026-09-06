using System;
using Durango.Logic.Clusters;
using Durango.UI.Control;
using Durango.UI.Popup;
using Shared.Economy;
using Shared.Season2;
using UnityEngine;

namespace Durango.UI;

[Uri("Menu")]
public class MenuListGroup : MenuListGroupBase
{
	[SerializeField]
	private Transform _currencyContainer;

	[SerializeField]
	private CurrencyWidgetBase[] _currencyWidgets;

	[SerializeField]
	private GameObject _walletPopupButton;

	[SerializeField]
	private GameObject _walletFolded;

	[SerializeField]
	private GameObject _walletUnfolded;

	protected override void Start()
	{
		base.Start();
		UIEventListener uIEventListener = UIEventListener.Get(_walletPopupButton);
		uIEventListener.onClick = (UIEventListener.VoidDelegate)Delegate.Combine(uIEventListener.onClick, (UIEventListener.VoidDelegate)delegate
		{
			_walletFolded.SetActive(value: false);
			_walletUnfolded.SetActive(value: true);
			WalletInfoPopup walletInfoPopup = UIManager.Popup.Tooltip<WalletInfoPopup>();
			walletInfoPopup.AddOnFinished(delegate
			{
				_walletFolded.SetActive(value: true);
				_walletUnfolded.SetActive(value: false);
			});
			walletInfoPopup.Show(_walletPopupButton.transform, Vector2.down * 50f);
		});
		UIEventListener uIEventListener2 = UIEventListener.Get(TouchBlockBox.gameObject);
		uIEventListener2.onClick = (UIEventListener.VoidDelegate)Delegate.Combine(uIEventListener2.onClick, (UIEventListener.VoidDelegate)delegate
		{
			Close();
		});
		UIEventListener uIEventListener3 = UIEventListener.Get(TouchBlockBox.gameObject);
		uIEventListener3.onDrag = (UIEventListener.VectorDelegate)Delegate.Combine(uIEventListener3.onDrag, (UIEventListener.VectorDelegate)delegate
		{
			Close();
			UIManager.SetCurrentUITouchEvent(enable: false);
		});
		_currencyContainer.gameObject.SetActive(value: false);
		InitCurrencyWidget();
		if (GameManager.ClusterMode != Mode.Online)
		{
			_walletPopupButton.SetActive(value: false);
		}
	}

	protected override bool TryOpen()
	{
		if (!base.TryOpen())
		{
			return false;
		}
		_currencyContainer.gameObject.SetActive(value: true);
		return true;
	}

	protected override bool TryClose()
	{
		_currencyContainer.gameObject.SetActive(value: false);
		return base.TryClose();
	}

	private void InitCurrencyWidget()
	{
		if (GameManager.Region.IsWarpRush())
		{
			_currencyWidgets[0].gameObject.SetActive(value: false);
			_currencyWidgets[1].SetWarpRushResource(ResourceType.AlphaStone, total: false);
			_currencyWidgets[2].SetWarpRushResource(ResourceType.BravoStone, total: false);
		}
		else
		{
			// [7 ก.ย. 2026] เซิร์ฟนี้ใช้สกุลเงินเดียวคือ T Stone ⇒ เหลือช่องเดียว
			//
			// นี่คือ 3 ช่องเงินบนหัวจอเมนู — เป็นวิดเจ็ตที่วางไว้ใน prefab ตายตัว 3 ตัว
			// ไม่ได้ผ่าน CurrencyWidgetList/CurrencyGroup เลย (คนละเส้นกัน) ⇒ ที่แก้ไว้ตรงนั้นไม่ถึง
			// ของเดิมผูก [0]=Coin [1]=Gem [2]=TStone และเพราะ WalletExtension.Normalize()
			// แปลงทุกสกุลเป็น TStone ทั้งสามช่องจึงโชว์ยอดเดียวกันหมด (อาการที่เห็น: 12,500 × 3)
			//
			// ปิดสองช่องแรก เหลือช่อง TStone ช่องเดียว — ไม่แตะ prefab (แก้ไม่ได้อยู่แล้ว)
			_currencyWidgets[0].gameObject.SetActive(value: false);
			_currencyWidgets[1].gameObject.SetActive(value: false);
			_currencyWidgets[2].SetCurrencyType(Currency.TStone);
		}
	}
}
