using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Logic;
using Durango.Logic.Item;
using Durango.Logic.Skill;
using Durango.UI.Control;
using Durango.UI.Popup;
using Durango.Utils;
using Durango.Utils.Extensions;
using L10N;
using Messages;
using NestedPrefab;
using Shared.Ability;
using UnityEngine;
using Yaml;
using Yaml.Util;

namespace Durango.UI;

[Uri("PetConstant")]
public class PetGroup : UIBase
{
	public enum PetOwnType
	{
		[T.EnumName("보유")]
		Holding,
		[T.EnumName("방목")]
		Grazing
	}

	private static readonly string[] PetVouchers = new string[3] { "voucher_revert_milestone", "voucher_revert_active_skill", "voucher_revert_rank" };

	[SerializeField]
	private RectLayoutComponent _layout;

	[SerializeField]
	private UITitle _titleWidget;

	[SerializeField]
	private UIWidget _mainWidget;

	[SerializeField]
	private NestedPrefabLinker _petListTabLinker;

	[SerializeField]
	private PetListWidget _petList;

	[SerializeField]
	private PetPreviewWidget _petPreview;

	[SerializeField]
	private PetInfoWidget _petInfoWidget;

	[SerializeField]
	private UIWidget _noData;

	[SerializeField]
	private RectLayoutComponent _mainLayout;

	[SerializeField]
	private GameObject _titleInfos;

	[SerializeField]
	private UILabel _petCountLabel;

	[SerializeField]
	private GameObject _petCountButton;

	[SerializeField]
	private UILabel _grazedPetCountLabel;

	[SerializeField]
	private GameObject _grazedPetCountButton;

	[SerializeField]
	private GameObject _petVoucherButton;

	[SerializeField]
	private GameObject[] _cardInfoButtons;

	private string _selectedPetId;

	private PetsInfo? _info;

	private HorizontalTabList _petListTabs;

	private PetOwnType _currentTabType;

	private void Start()
	{
		_openCloseSound = UISound.GroupType.Pet;
		_petListTabs = _petListTabLinker.Object.GetComponent<HorizontalTabList>();
		_petListTabs.BeginLoad();
		PetOwnType[] array = Enums<PetOwnType>.All();
		for (int i = 0; i < array.Length; i++)
		{
			_petListTabs.AddText(array[i].GetName());
		}
		_petListTabs.EndLoadByFitOnWidget();
		_petListTabs.Clicked += SelectTab;
		_titleWidget.Object.SetTitle(T._("동물 관리"));
		_petList.PetSelected += OnPetSelect;
		_petInfoWidget.PetActionClicked += OnPetActionClick;
		_petPreview.Renamed += RenamePet;
		_petPreview.MilestonePicked += PetMilestonePick;
		_petPreview.ActiveSkillPicked += PetActiveSkillPick;
		_petPreview.MilestoneHelpClicked += ShowPetMilestonHelp;
		UIEventListener.Get(_petCountButton.gameObject).onClick = OnClickPetCountButton;
		UIEventListener.Get(_grazedPetCountButton.gameObject).onClick = OnClickGrazedPetCountButton;
		UIEventListener.Get(_petVoucherButton.gameObject).onClick = OnClickPetVoucherButton;
		GameSystem<StatisticsSystem>.Instance().StatisticsUpdated += RefreshPetCountLabel;
		GameSystem<InventorySystem>.Instance().WalletUpdated += RefreshPetList;
		Durango.Utils.Singleton<PetManager>.Instance().PetActiveSkillUsed += OnPetActiveSkillUsed;
		base.OnOpenSucceed += Opened;
		if (_cardInfoButtons != null)
		{
			GameObject[] cardInfoButtons = _cardInfoButtons;
			foreach (GameObject gameObject in cardInfoButtons)
			{
				if (!(gameObject == null))
				{
					UIEventListener uIEventListener = UIEventListener.Get(gameObject);
					uIEventListener.onClick = (UIEventListener.VoidDelegate)Delegate.Combine(uIEventListener.onClick, new UIEventListener.VoidDelegate(OnClickHelpButton));
				}
			}
		}
		SetChildrenActive(activated: false);
	}

	protected override bool TryClose()
	{
		_selectedPetId = string.Empty;
		return base.TryClose();
	}

	public void Open(string petId)
	{
		_selectedPetId = petId;
		Open();
	}

	private void Opened()
	{
		_mainWidget.gameObject.SetActive(value: false);
		_noData.gameObject.SetActive(value: false);
		_titleInfos.gameObject.SetActive(value: false);
		UIManager.Popup.LoadingRing.AttachToWidget(_layout.gameObject);
		RefreshPetList();
	}

	private void RefreshPetList()
	{
		if (base.IsOpened)
		{
			PetManager.GetPetList(SetPetList);
		}
	}

	private void SetPetList(PetsInfo? info)
	{
		if (base.IsOpened)
		{
			_info = info;
			UIManager.Popup.LoadingRing.DetachFromWidget(_layout.gameObject);
			bool flag = _info.HasValue && KUtility.GetSize(_info.Value.Pets.Data) > 0;
			bool flag2 = _info.HasValue && KUtility.GetSize(_info.Value.GrazedPets.Data) > 0;
			bool flag3 = flag || flag2;
			_mainWidget.gameObject.SetActive(flag3);
			_titleInfos.gameObject.SetActive(flag3);
			_noData.gameObject.SetActive(!flag3);
			_layout.UpdateLayout();
			UIUtility.UpdateAnchors(base.transform);
			_petListTabs.UpdateLayout(HorizontalTabList.FitStyle.FitOnWidget);
			if (flag3)
			{
				SelectTab((int)((!flag) ? PetOwnType.Grazing : (flag2 ? _currentTabType : PetOwnType.Holding)));
			}
		}
	}

	private void SelectTab(int index)
	{
		PetOwnType petOwnType = (_currentTabType = (PetOwnType)index);
		_petListTabs.Select(index);
		PetsInfo? info = _info;
		if (info.HasValue)
		{
			_petList.Set(petOwnType, _info.Value, GetCallback(petOwnType));
		}
		Messages.Pet[] pets = GetPets(petOwnType);
		if (KUtility.GetSize(pets) > 0)
		{
			string firstPetId = _petList.GetFirstPetId();
			OnPetSelect(FindPet(firstPetId).GetValueOrDefault());
		}
		RefreshPetCountLabel();
	}

	private Messages.Pet[] GetPets(PetOwnType ownType)
	{
		if (_info.HasValue)
		{
			switch (ownType)
			{
			case PetOwnType.Holding:
				return _info.Value.Pets.Data;
			case PetOwnType.Grazing:
				return _info.Value.GrazedPets.Data;
			}
		}
		return null;
	}

	private Action<GameObject> GetCallback(PetOwnType petOwnType)
	{
		if (petOwnType == PetOwnType.Grazing)
		{
			return ShowPetSelectorPopup;
		}
		return null;
	}

	private Messages.Pet? FindPet(string id)
	{
		PetsInfo? info = _info;
		if (!info.HasValue)
		{
			return null;
		}
		Messages.Pet[] data = _info.Value.Pets.Data;
		if (data != null)
		{
			int num = data.IndexOf((Messages.Pet p) => p.EntityId == id);
			if (num != -1)
			{
				return data[num];
			}
		}
		Messages.Pet[] data2 = _info.Value.GrazedPets.Data;
		if (data2 != null)
		{
			int num2 = data2.IndexOf((Messages.Pet p) => p.EntityId == id);
			if (num2 != -1)
			{
				return data2[num2];
			}
		}
		return null;
	}

	private void OnPetSelect(Messages.Pet pet)
	{
		_selectedPetId = pet.EntityId;
		_petList.Select(pet);
		_petPreview.Set(pet, _info.GetValueOrDefault());
		_petInfoWidget.Set(pet, _info.GetValueOrDefault());
		_mainLayout.UpdateLayout();
	}

	private void RenamePet(Messages.Pet pet)
	{
		TextInputPopup textInputPopup = UIManager.Popup.Tooltip<TextInputPopup>();
		textInputPopup.Show(delegate(string newName)
		{
			PetManager.RenamePet(pet.EntityId, newName, RefreshPetList);
		}, T._("새로운 이름을 적어주세요"), pet.Name);
	}

	[Uri("PetMilestonePick")]
	private void ShowPetMilestonePick()
	{
		VehiclePet vehiclePet = PlayerBehavior.LocalPlayer.Driver.Vehicle as VehiclePet;
		if (vehiclePet == null)
		{
			return;
		}
		Messages.Pet? pet = Durango.Utils.Singleton<PetManager>.Instance().GetPet(vehiclePet.EntityId);
		if (!pet.HasValue || pet.Value.Statistics.MilestonesInformation == null)
		{
			return;
		}
		MilestoneInfo[] milestonesInformation = pet.Value.Statistics.MilestonesInformation;
		for (int i = 0; i < KUtility.GetSize(milestonesInformation); i++)
		{
			MilestoneInfo milestoneInfo = milestonesInformation[i];
			if (!milestoneInfo.Acquired && milestoneInfo.Level <= pet.Value.Statistics.Level)
			{
				PetMilestonePick(pet.Value, milestoneInfo.MilestoneTableId);
				break;
			}
		}
	}

	private void PetMilestonePick(Messages.Pet pet, int milestoneId)
	{
		UIManager.FindScript<PetMilestonePickGroup>().ShowPetMilestonePick(pet, milestoneId, RefreshPetList);
	}

	private void PetActiveSkillPick(Messages.Pet pet)
	{
		UIManager.FindScript<PetMilestonePickGroup>().ShowPetActiveSkillPick(pet, RefreshPetList);
	}

	private void ShowPetMilestonHelp(Messages.Pet pet)
	{
		PetMilestoneHelpPopup petMilestoneHelpPopup = UIManager.Popup.Tooltip<PetMilestoneHelpPopup>();
		petMilestoneHelpPopup.Set(pet);
		petMilestoneHelpPopup.Show();
	}

	private void OnPetActionClick(PetInfoWidget.PetAction action, Messages.Pet pet)
	{
		switch (action)
		{
		case PetInfoWidget.PetAction.Spawn:
			PetManager.SpawnMyPet(pet.EntityId);
			UIBase.CloseAllUI();
			break;
		case PetInfoWidget.PetAction.Return:
			PetManager.ReturnMyPet(pet.EntityId);
			UIBase.CloseAllUI();
			break;
		case PetInfoWidget.PetAction.Reinify:
			ReinifyPet(pet, RefreshPetList);
			break;
		case PetInfoWidget.PetAction.Release:
			ReleasePet(pet, RefreshPetList);
			break;
		case PetInfoWidget.PetAction.RevertRank:
			RevertPetRank(pet, RefreshPetList);
			break;
		case PetInfoWidget.PetAction.PutInToStorage:
		{
			PetsInfo? info3 = _info;
			if (!info3.HasValue)
			{
				break;
			}
			Messages.Pet[] pets2 = _info.Value.GrazedPets.Data;
			if (KUtility.GetSize(pets2) < _info.Value.GrazableCount)
			{
				string[] ids = ((pets2 != null) ? pets2.Select((Messages.Pet p) => p.EntityId).Concat(new string[1] { pet.EntityId }).ToArray() : new string[1] { pet.EntityId });
				PetManager.GrazePets(ids, RefreshPetList);
				break;
			}
			SelectPetPopup selectPetPopup2 = UIManager.Popup.Tooltip<SelectPetPopup>();
			selectPetPopup2.SetTitle(T._("동물 교체")).SetInfo(T._("방목 중인 동물이 가득 찼습니다.\n교체할 동물을 선택하세요.")).SetList(pets2)
				.SetOnConfirm(delegate(Messages.Pet selected)
				{
					string[] ids2 = ((pets2 != null) ? (from p in pets2
						select p.EntityId into id
						where id != selected.EntityId
						select id).Concat(new string[1] { pet.EntityId }).ToArray() : new string[1] { pet.EntityId });
					PetManager.GrazePets(ids2, RefreshPetList);
				})
				.SetConfirmButtonText(T._("교체하기"))
				.Show();
			break;
		}
		case PetInfoWidget.PetAction.TakeOutFromStorage:
		{
			PetsInfo? info = _info;
			if (!info.HasValue)
			{
				break;
			}
			Messages.Pet[] pets = _info.Value.GrazedPets.Data;
			if (pets == null)
			{
				break;
			}
			PetsInfo? info2 = _info;
			int num = (info2.HasValue ? KUtility.GetSize(_info.Value.Pets.Data) : 0);
			int num2 = (int)GameSystem<StatisticsSystem>.Instance().GetDeriveds(Derived.MaxTamingPet);
			if (num < num2)
			{
				string[] array = (from p in pets
					select p.EntityId into id
					where id != pet.EntityId
					select id).ToArray();
				if (array.Length != pets.Length)
				{
					PetManager.GrazePets(array, RefreshPetList);
				}
				break;
			}
			IEnumerable<Messages.Pet> list = _info.Value.Pets.Data.Where(delegate(Messages.Pet p)
			{
				CageInfo? cageInfo = p.CageInfo;
				return !cageInfo.HasValue || string.IsNullOrEmpty(p.CageInfo.Value.RegionId);
			});
			SelectPetPopup selectPetPopup = UIManager.Popup.Tooltip<SelectPetPopup>();
			selectPetPopup.SetTitle(T._("동물 교체")).SetInfo(T._("관리 중인 동물이 가득 찼습니다.\n교체할 동물을 선택하세요.")).SetList(list)
				.SetOnConfirm(delegate(Messages.Pet selected)
				{
					string[] ids2 = (from p in pets
						select p.EntityId into id
						where id != pet.EntityId
						select id).Concat(new string[1] { selected.EntityId }).ToArray();
					PetManager.GrazePets(ids2, RefreshPetList);
				})
				.SetConfirmButtonText(T._("교체하기"))
				.Show();
			break;
		}
		}
	}

	private void RefreshPetCountLabel()
	{
		if (base.IsOpened)
		{
			PetsInfo? info = _info;
			int num = (info.HasValue ? KUtility.GetSize(_info.Value.Pets.Data) : 0);
			int num2 = (int)GameSystem<StatisticsSystem>.Instance().GetDeriveds(Derived.MaxTamingPet);
			_petCountLabel.text = $"{num} [FFFFFF7F]/ {num2}[-]";
			PetsInfo? info2 = _info;
			int num3 = (info2.HasValue ? KUtility.GetSize(_info.Value.GrazedPets.Data) : 0);
			PetsInfo? info3 = _info;
			int num4 = (info3.HasValue ? _info.Value.GrazableCount : 0);
			_grazedPetCountLabel.text = $"{num3} [FFFFFF7F]/ {num4}[-]";
		}
	}

	private void OnPetActiveSkillUsed(PetActiveSkillUsed msg)
	{
		SoundManager.PlayEvent("ui_animal_specialability");
		if (!string.IsNullOrEmpty(msg.ClipName))
		{
			PetAI petObject = Durango.Utils.Singleton<PetManager>.Instance().GetPetObject(Durango.Utils.Singleton<PetManager>.Instance().GetPlayerPetId());
			if (petObject != null)
			{
				petObject.TargetAnimal.Play(msg.ClipName);
			}
		}
	}

	private void OnClickHelpButton(GameObject obj)
	{
		CardNewsPopup cardNewsPopup = UIManager.Popup.Tooltip<CardNewsPopup>();
		if (cardNewsPopup.Load("pet_renewal"))
		{
			cardNewsPopup.Show();
		}
	}

	public static void OnClickPetCountButton(GameObject obj)
	{
		UISound.PlayClick(UISound.ClickType.ButtonDefault);
		SkillSystem skillSystem = GameSystem<SkillSystem>.Instance();
		Durango.Logic.Skill.Skill skill = skillSystem.FindSkill("capture", "enhance");
		if (skill != null)
		{
			Node node = null;
			if (skill.Bundle.Base != null && skill.Bundle.Base.Level == 0)
			{
				node = skill.Bundle.Base.Get(1);
			}
			if (node == null)
			{
				node = skillSystem.FindSkill(skill.Id, skill.SubId, Mathf.Min(skill.MaxLevel, skill.Level + 1));
			}
			UIManager.FindScript<SkillGroup>().Open(node);
		}
	}

	public static void OnClickGrazedPetCountButton(GameObject obj)
	{
		UIManager.FindScript<ShopGroup>().Open("pet_grazable_slot", select: true);
	}

	public static void OnClickPetVoucherButton(GameObject go)
	{
		ShopVouchersPopup shopVouchersPopup = UIManager.Popup.Tooltip<ShopVouchersPopup>();
		shopVouchersPopup.Show(PetVouchers);
	}

	public static void ReinifyPet(Messages.Pet pet, Action onSuccess)
	{
		string[] tags = Yaml.Util.Singleton<Constants>.Instance.Pet.ReinifyTags;
		Yaml.Tag tag = null;
		int i = 0;
		for (int size = KUtility.GetSize(tags); i < size; i++)
		{
			tag = SingletonDict<string, Yaml.Tag>.Get(tags[i]);
			if (tag != null)
			{
				break;
			}
		}
		if (tag == null)
		{
			return;
		}
		Predicate<ItemData> predicate = delegate(ItemData item)
		{
			int j = 0;
			for (int size2 = KUtility.GetSize(tags); j < size2; j++)
			{
				if (item.HasTag(tags[j]))
				{
					return true;
				}
			}
			return false;
		};
		int num = Durango.Logic.Item.Util.Counting(GameSystem<InventorySystem>.Instance().PlayerItemList, predicate);
		MessageBox messageBox = UIManager.MessageBox;
		messageBox.AddKeyValueInfo(T._("필요한 아이템 개수"), string.Format("[icon={1}] {0}", 1, tag.Icon));
		messageBox.AddKeyValueInfo(T._("가지고 있는 아이템 개수"), string.Format("[icon={1}] {0}", num, tag.Icon));
		messageBox.Show(T._("<em>귀속 해제</em>하시겠습니까?"), T._("[icon=icon_make_alert] 귀속 해제에는 {0} 속성을 가진 아이템이 {1}개 필요합니다.", tag.Name, 1), delegate(int index)
		{
			if (index == 0)
			{
				string itemId = null;
				List<ItemData> playerItemList = GameSystem<InventorySystem>.Instance().PlayerItemList;
				int j = 0;
				for (int size2 = KUtility.GetSize(playerItemList); j < size2; j++)
				{
					if (predicate(playerItemList[j]))
					{
						itemId = playerItemList[j].Id;
						break;
					}
				}
				PetManager.ReinifyPet(pet.EntityId, itemId, delegate
				{
					UIManager.SystemMsg(T._("귀속이 해제되었습니다."));
					SoundManager.PlayEvent("ui_button_animal_unbind");
					onSuccess();
				});
			}
		}, new MessageBox.Button
		{
			Text = string.Format("{0} [preset=round_box?[icon={1}]    {2}]", T._("귀속 해제"), tag.Icon, 1),
			Style = PresetButton.Style.Solid
		}, new MessageBox.Button
		{
			Text = T._("취소"),
			Style = PresetButton.Style.Border
		});
	}

	public static void ReleasePet(Messages.Pet pet, Action onSuccess)
	{
		UIManager.MessageBox.Show(T._("동물을 야생에 풀어주시겠습니까?"), T._("<alert>[icon=icon_make_alert] 한 번 풀어준 동물은 다시 소환할 수 없으며, 동물 가방에 보관중인 아이템도 사라집니다.</alert>"), delegate(int index)
		{
			if (index == 0)
			{
				PetManager.ReleasePet(pet.EntityId, delegate
				{
					UIManager.SystemMsg(T._("동물을 풀어주었습니다."));
					if (onSuccess != null)
					{
						onSuccess();
					}
				});
			}
		}, new MessageBox.Button
		{
			Text = T._("풀어주기"),
			Style = PresetButton.Style.Solid,
			Sound = "ui_button_release"
		}, new MessageBox.Button
		{
			Text = T._("취소"),
			Style = PresetButton.Style.Border
		});
	}

	public static void RevertPetRank(Messages.Pet pet, Action onSuccess)
	{
		PetResetRankPopup petResetRankPopup = UIManager.Popup.Tooltip<PetResetRankPopup>();
		petResetRankPopup.Set(pet, onSuccess);
		petResetRankPopup.Show();
	}

	private void ShowPetSelectorPopup(GameObject _)
	{
		Messages.Pet[] array = ((!_info.HasValue) ? null : _info.Value.Pets.Data);
		if (array == null)
		{
			return;
		}
		List<Messages.Pet> list = (from pet in array
			where pet.CageInfo.HasValue && string.IsNullOrEmpty(pet.CageInfo.Value.RegionId)
			orderby !pet.IsSpawned
			select pet).ToList();
		if (list.Count <= 0)
		{
			UIManager.SystemMsg(T._("방목 가능한 동물이 없습니다."));
			return;
		}
		SelectPetPopup selectPetPopup = UIManager.Popup.Tooltip<SelectPetPopup>();
		selectPetPopup.SetTitle(T._("동물 방목하기")).SetInfo(T._("축사에 있는 동물은 방목할 수 없습니다.")).SetList(list)
			.SetOnConfirm(delegate(Messages.Pet pet)
			{
				OnPetActionClick(PetInfoWidget.PetAction.PutInToStorage, pet);
			})
			.SetConfirmButtonText(T._("방목하기"))
			.Show();
	}
}
