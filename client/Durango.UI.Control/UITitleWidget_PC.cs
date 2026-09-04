using System;
using System.Collections.Generic;
using Durango.Logic;
using Messages;
using Shared.Ability;
using UnityEngine;

namespace Durango.UI.Control;

public class UITitleWidget_PC : UITitleWidget
{
	[SerializeField]
	private UIWidget _borderWidget;

	[SerializeField]
	[EnumList(typeof(UITitle.TitleCurrencyType), false, 0, -1)]
	private GameObject[] _currencies;

	[SerializeField]
	private UILabel _skillPointLabel;

	[SerializeField]
	private UILabel _petCountLabel;

	protected override void Awake()
	{
		base.Awake();
		GameObject[] currencies = _currencies;
		foreach (GameObject gameObject in currencies)
		{
			gameObject.SetActive(value: false);
		}
		UpdateLayout();
	}

	protected override void OnStart()
	{
		base.OnStart();
		if (Application.isPlaying)
		{
			UIEventListener.Get(_currencies[1].gameObject).onClick = PetGroup.OnClickPetCountButton;
			UIEventListener.Get(_currencies[2].gameObject).onClick = PetGroup.OnClickPetVoucherButton;
		}
	}

	protected override void OnEnable()
	{
		base.OnEnable();
		if (Application.isPlaying)
		{
			if (_currencies[0].activeInHierarchy)
			{
				UpdateSkillPoint();
			}
			if (_currencies[1].activeInHierarchy)
			{
				UpdatePetCount();
			}
		}
	}

	protected override void UpdateLayout()
	{
		if (base.Parent != null)
		{
			Background.leftAnchor.Set(base.transform, 0f, 0f);
			Background.rightAnchor.Set(base.transform, 1f, 0f);
			if (base.Parent.Anchor == UIBase.AnchorType.FullscreenMobileOnly)
			{
				Background.topAnchor.Set(base.transform, 1f, 1f);
				Background.rightAnchor.SetScreen(1f, 0f);
			}
			else
			{
				Background.topAnchor.Set(base.transform, 1f, 0f);
			}
			Background.ResetAnchors();
			_borderWidget.leftAnchor.Set(base.Parent.transform, 0f, -1f);
			_borderWidget.rightAnchor.Set(base.Parent.transform, 1f, 1f);
			_borderWidget.bottomAnchor.Set(base.Parent.transform, 0f, -1f);
			_borderWidget.topAnchor.Set(base.Parent.transform, 1f, 1f);
			_borderWidget.ResetAnchors();
			if (base.Parent.Anchor == UIBase.AnchorType.Fullscreen && TitleLabel != null)
			{
				TitleBarMenuGroup titleBarMenuGroup = UIManager.FindScript<TitleBarMenuGroup>();
				if (titleBarMenuGroup != null)
				{
					Transform titleBarRightAnchor = titleBarMenuGroup.TitleBarRightAnchor;
					if (titleBarRightAnchor != null)
					{
						TitleLabel.rightAnchor.Set(titleBarRightAnchor, 0f, -10f);
					}
				}
			}
		}
		Layout.UpdateLayout();
		UIUtility.UpdateAnchors(base.transform);
		RefreshTitleNextContainer();
	}

	public void ShowBorder(bool show)
	{
		_borderWidget.gameObject.SetActive(show);
	}

	public void SetTitleCurrencies(IEnumerable<UITitle.TitleCurrencyType> titleCurrencies)
	{
		if (titleCurrencies == null)
		{
			return;
		}
		foreach (UITitle.TitleCurrencyType value in Enum.GetValues(typeof(UITitle.TitleCurrencyType)))
		{
			EnableCurrency(value, enable: false);
		}
		foreach (UITitle.TitleCurrencyType titleCurrency in titleCurrencies)
		{
			EnableCurrency(titleCurrency, enable: true);
		}
		UpdateLayout();
	}

	private void EnableCurrency(UITitle.TitleCurrencyType type, bool enable)
	{
		GameObject gameObject = _currencies[(int)type];
		if (gameObject.activeSelf == enable)
		{
			return;
		}
		gameObject.SetActive(enable);
		switch (type)
		{
		case UITitle.TitleCurrencyType.SkillPoint:
			if (enable)
			{
				GameSystem<SkillSystem>.Instance().SkillListUpdated += UpdateSkillPoint;
				UpdateSkillPoint();
			}
			else
			{
				GameSystem<SkillSystem>.Instance().SkillListUpdated -= UpdateSkillPoint;
			}
			break;
		case UITitle.TitleCurrencyType.PetCount:
			if (enable)
			{
				GameSystem<StatisticsSystem>.Instance().StatisticsUpdated += UpdatePetCount;
				UpdatePetCount();
			}
			else
			{
				GameSystem<StatisticsSystem>.Instance().StatisticsUpdated -= UpdatePetCount;
			}
			break;
		}
	}

	private void UpdateSkillPoint()
	{
		if (_currencies[0].activeInHierarchy)
		{
			_skillPointLabel.text = $"<em>{GameSystem<SkillSystem>.Instance().RemainSkillPoint}</em> <weak>/ {GameSystem<SkillSystem>.Instance().SkillPoint}</weak>";
		}
	}

	private void UpdatePetCount()
	{
		if (_currencies[1].activeInHierarchy)
		{
			PetManager.GetPetList(delegate(PetsInfo? petsInfo)
			{
				int num = (petsInfo.HasValue ? KUtility.GetSize(petsInfo.Value.Pets.Data) : 0);
				int num2 = (int)GameSystem<StatisticsSystem>.Instance().GetDeriveds(Derived.MaxTamingPet);
				_petCountLabel.text = $"<em>{num}</em> <weak>/ {num2}</weak>";
			});
		}
	}
}
