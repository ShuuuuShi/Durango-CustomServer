using System;
using System.Collections.Generic;
using Shared.Economy;
using Shared.Season2;
using UnityEngine;

namespace Durango.UI.Control;

[ExecuteInEditMode]
public abstract class CurrencyWidgetBase : MonoBehaviour
{
	[Serializable]
	private struct ResourceType
	{
		public Shared.Season2.ResourceType Type;

		public bool Total;
	}

	[SerializeField]
	private Currency _currencyType = Currency.Invalid;

	[SerializeField]
	private string _voucherId;

	[SerializeField]
	private bool _clanFund;

	[SerializeField]
	private bool _skillPoint;

	[SerializeField]
	private ResourceType _resourceType = new ResourceType
	{
		Type = Shared.Season2.ResourceType.Invalid
	};

	[SerializeField]
	protected bool _hideExtraButton;

	[SerializeField]
	protected PresetCurrencyWidget _presetPrefab;

	[SerializeField]
	[HideInInspector]
	protected PresetCurrencyWidget _component;

	public Currency CurrencyType => _currencyType;

	public bool IsSkillPoint => _skillPoint;

	private void Start()
	{
		if (!Application.isPlaying)
		{
			return;
		}
		// [7 ก.ย. 2026] เซิร์ฟนี้ใช้สกุลเงินเดียวคือ T Stone ⇒ ยุบช่องเงินซ้ำให้เหลือช่องเดียว
		//
		// ⚠️ ช่องเงินพวกนี้ **วางไว้ใน prefab แล้วตั้งค่าตัวเองจาก _currencyType ที่ Unity เซฟไว้**
		// (Refresh() ข้างล่างอ่าน _currencyType ตอน MakeComponent) ไม่มีโค้ด C# ตัวไหนเรียกเลย
		// ⇒ grep หา SetCurrencyType ไม่เจอ และแก้ที่ CurrencyWidgetList/MenuListGroup ไม่ถึง
		// (อาการจริง: หัวจอเมนู 3 ช่อง · หน้ากระเป๋า 2 ช่อง เลข 12,500 เท่ากันทุกช่อง
		//  เพราะ WalletExtension.Normalize() แปลงทุกสกุลเป็น TStone แต่ตัววิดเจ็ตยังคนละตัว)
		//
		// กติกา: ในพ่อคนเดียวกัน เก็บ "ช่องเงินตัวแรก" ไว้ตัวเดียว ที่เหลือปิดทิ้ง
		// แตะเฉพาะช่องที่เป็นเงินจริง (_currencyType != Invalid) ⇒ ช่องแต้มสกิล · คูปอง ·
		// กองทุนเผ่า · หินวาร์ปรัช ไม่ได้รับผลกระทบ เพราะพวกนั้น _currencyType เป็น Invalid
		if (_currencyType != Currency.Invalid)
		{
			if (HasEarlierMoneySibling())
			{
				base.gameObject.SetActive(value: false);
				return;
			}
			_currencyType = Currency.TStone;
		}
		MakeComponent();
	}

	/// <summary>มีช่องเงินตัวอื่นอยู่ก่อนหน้าเราใต้พ่อคนเดียวกันไหม</summary>
	private bool HasEarlierMoneySibling()
	{
		Transform parent = base.transform.parent;
		if (parent == null)
		{
			return false;
		}
		int self = base.transform.GetSiblingIndex();
		for (int i = 0; i < self; i++)
		{
			CurrencyWidgetBase other = parent.GetChild(i).GetComponent<CurrencyWidgetBase>();
			if (other != null && other.gameObject.activeSelf && other._currencyType != Currency.Invalid)
			{
				return true;
			}
		}
		return false;
	}

	private void OnEnable()
	{
		if (Application.isPlaying || (bool)_component || !MakeComponent())
		{
			return;
		}
		Stack<Transform> stack = new Stack<Transform>();
		stack.Push(_component.transform);
		while (stack.Count > 0)
		{
			Transform transform = stack.Pop();
			transform.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
			int i = 0;
			for (int childCount = transform.childCount; i < childCount; i++)
			{
				stack.Push(transform.GetChild(i));
			}
		}
	}

	private void OnDisable()
	{
		if (!Application.isPlaying)
		{
			DestroyComponent();
		}
	}

	protected abstract bool MakeComponent();

	private void DestroyComponent()
	{
		if ((bool)_component)
		{
			UnityEngine.Object.DestroyImmediate(_component.gameObject);
			_component = null;
		}
	}

	protected void Refresh()
	{
		if (Application.isPlaying)
		{
			if (_currencyType != Currency.Invalid)
			{
				SetCurrencyType(_currencyType);
			}
			else if (!string.IsNullOrEmpty(_voucherId))
			{
				SetVoucherType(_voucherId);
			}
			else if (_clanFund)
			{
				SetClanFund();
			}
			else if (_skillPoint)
			{
				SetSkillPoint();
			}
			else if (_resourceType.Type != Shared.Season2.ResourceType.Invalid)
			{
				SetWarpRushResource(_resourceType.Type, _resourceType.Total);
			}
		}
	}

	private void ResetCurrency()
	{
		_currencyType = Currency.Invalid;
		_voucherId = null;
		_clanFund = false;
		_skillPoint = false;
		_resourceType.Type = Shared.Season2.ResourceType.Invalid;
	}

	public void SetCurrencyType(Currency type)
	{
		ResetCurrency();
		_currencyType = type;
		if (!(_component == null))
		{
			_component.SetCurrencyType(type);
		}
	}

	public void SetVoucherType(string voucherId)
	{
		ResetCurrency();
		_voucherId = voucherId;
		if (!(_component == null))
		{
			_component.SetVoucherType(_voucherId);
		}
	}

	public void SetClanFund()
	{
		ResetCurrency();
		_clanFund = true;
		if (!(_component == null))
		{
			_component.SetClanFund();
		}
	}

	public void SetSkillPoint()
	{
		ResetCurrency();
		_skillPoint = true;
		if (!(_component == null))
		{
			_component.SetSkillPoint();
		}
	}

	public void SetWarpRushResource(Shared.Season2.ResourceType warpRushStoneType, bool total)
	{
		ResetCurrency();
		_resourceType.Type = warpRushStoneType;
		_resourceType.Total = total;
		if (!(_component == null))
		{
			_component.SetWarpRushResource(warpRushStoneType, total);
		}
	}

	public void HideExtraButton(bool hide)
	{
		_hideExtraButton = hide;
		if (!(_component == null))
		{
			_component.HideExtraButton(_hideExtraButton);
		}
	}
}
