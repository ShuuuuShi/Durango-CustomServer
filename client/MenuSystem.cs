using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Logic;
using Durango.Logic.Clusters;
using Durango.Network;
using Durango.System;
using Durango.Utils;
using Durango.Utils.Extensions;
using Messages;

public class MenuSystem : GameSystem<MenuSystem>
{
	private const string StorageKey = "RecentlyUnlockedMenuList";

	private static readonly MenuType[] HideInTutorial = new MenuType[14]
	{
		MenuType.Mail,
		MenuType.Faction,
		MenuType.Pet,
		MenuType.Notice,
		MenuType.Market,
		MenuType.Clan,
		MenuType.Estate,
		MenuType.Shop,
		MenuType.Event,
		MenuType.LearningGuide,
		MenuType.Party,
		MenuType.PvpIsland,
		MenuType.Story,
		MenuType.Music
	};

	private static readonly MenuType[] HideInSafeHouse = new MenuType[10]
	{
		MenuType.Market,
		MenuType.Clan,
		MenuType.Estate,
		MenuType.Shop,
		MenuType.Event,
		MenuType.LearningGuide,
		MenuType.Party,
		MenuType.PvpIsland,
		MenuType.Story,
		MenuType.Music
	};

	private static readonly MenuType[] HideInWarpRush = new MenuType[17]
	{
		MenuType.Estate,
		MenuType.Quest,
		MenuType.Faction,
		MenuType.LearningGuide,
		MenuType.Clan,
		MenuType.Pet,
		MenuType.Event,
		MenuType.Shop,
		MenuType.Market,
		MenuType.Encyclopedia,
		MenuType.Party,
		MenuType.PvpIsland,
		MenuType.Mail,
		MenuType.Social,
		MenuType.Notice,
		MenuType.PlayerSelection,
		MenuType.Story
	};

	private static readonly MenuType[] HiddenInOnline = new MenuType[6]
	{
		MenuType.Connect,
		MenuType.CharacterOnMenu,
		MenuType.MusicOnMenu,
		MenuType.StoryOnMenu,
		MenuType.MoveToTitle,
		MenuType.WarpShop
	};

	private static readonly MenuType[] ShowInOffline = new MenuType[10]
	{
		MenuType.CharacterOnMenu,
		MenuType.Inventory,
		MenuType.Connect,
		MenuType.Encyclopedia,
		MenuType.MusicOnMenu,
		MenuType.StoryOnMenu,
		MenuType.Screenshot,
		MenuType.Config,
		MenuType.MoveToTitle,
		MenuType.WorldMap
	};

	private static readonly MenuType[] ShowInEditable = new MenuType[12]
	{
		MenuType.CharacterOnMenu,
		MenuType.Craft,
		MenuType.Inventory,
		MenuType.Connect,
		MenuType.WarpShop,
		MenuType.Encyclopedia,
		MenuType.MusicOnMenu,
		MenuType.StoryOnMenu,
		MenuType.Screenshot,
		MenuType.Config,
		MenuType.MoveToTitle,
		MenuType.WorldMap
	};

	private static readonly MenuType[] ShowInPvpIsland = new MenuType[6]
	{
		MenuType.Character,
		MenuType.CategoryCharacter,
		MenuType.Inventory,
		MenuType.WorldMap,
		MenuType.Screenshot,
		MenuType.Config
	};

	private bool[] _menuEnabled;

	private bool[] _recentlyUnlocked;

	private DelayedFunction _enableMenuUpdated;

	public event Action EnableMenuUpdated;

	private void Awake()
	{
		MenuType[] array = Enums<MenuType>.All();
		_menuEnabled = new bool[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			_menuEnabled[i] = true;
		}
		_recentlyUnlocked = new bool[array.Length];
		Singleton<GameManager>.Instance().MainSceneLoaded += GameManager_MainSceneLoaded;
		Singleton<GameManager>.Instance().WelcomeReceived += OnWelcome;
		_enableMenuUpdated = new DelayedFunction(delegate
		{
			if (EnableMenuUpdated != null)
			{
				EnableMenuUpdated();
			}
		});
	}

	private void GameManager_MainSceneLoaded()
	{
		MenuType[] array = Enums<MenuType>.All();
		foreach (MenuType type in array)
		{
			if (IsHiddenMenu(type))
			{
				EnableMenu(type, enable: false);
			}
		}
		EnableMenu(MenuType.Offerwall, Platform.Instance.IsAvailableOfferwall);
	}

	public static bool IsHiddenMenu(MenuType type)
	{
		if (Platform.Instance.UsePCUI && type == MenuType.Connect)
		{
			return true;
		}
		switch (GameManager.ClusterMode)
		{
		case Mode.Online:
			if (HiddenInOnline.Contains(type))
			{
				return true;
			}
			break;
		case Mode.Offline:
			return !ShowInOffline.Contains(type);
		case Mode.Editable:
			return !ShowInEditable.Contains(type);
		}
		if (GameManager.Region.IsTutorial())
		{
			return HideInTutorial.Contains(type);
		}
		if (GameManager.Region.IsSafeHouse())
		{
			return HideInSafeHouse.Contains(type);
		}
		if (GameManager.Region.IsWarpRush())
		{
			return HideInWarpRush.Contains(type);
		}
		if (GameManager.Region.IsPvpIsland())
		{
			return !ShowInPvpIsland.Contains(type);
		}
		return false;
	}

	private void OnWelcome(Welcome welcome)
	{
		byte[] array = welcome.Storage.Data?.Get("RecentlyUnlockedMenuList");
		if (KUtility.GetSize(array) == 0)
		{
			InitRecentlyUnlocked(GameManager.ClusterMode == Mode.Online);
		}
		else if (!LoadRecentlyUnlocked(array))
		{
			InitRecentlyUnlocked(value: true);
		}
	}

	private bool LoadRecentlyUnlocked(byte[] bytes)
	{
		Dictionary<string, bool> dictionary = Json.Read<Dictionary<string, bool>>(bytes);
		if (dictionary == null)
		{
			return false;
		}
		foreach (KeyValuePair<string, bool> item in dictionary)
		{
			if (!item.Key.TryEnum<MenuType>(out var value))
			{
				return false;
			}
			_recentlyUnlocked[(int)value] = item.Value;
		}
		return true;
	}

	private void InitRecentlyUnlocked(bool value)
	{
		MenuType[] hideInSafeHouse = HideInSafeHouse;
		foreach (MenuType menuType in hideInSafeHouse)
		{
			_recentlyUnlocked[(int)menuType] = value;
		}
	}

	private void SaveRecentlyUnlocked()
	{
		Dictionary<string, bool> dictionary = new Dictionary<string, bool>();
		MenuType[] hideInSafeHouse = HideInSafeHouse;
		for (int i = 0; i < hideInSafeHouse.Length; i++)
		{
			MenuType menuType = hideInSafeHouse[i];
			dictionary.Add(menuType.ToString(), _recentlyUnlocked[(int)menuType]);
		}
		SetStorageItem msg = default(SetStorageItem);
		msg.Key = "RecentlyUnlockedMenuList";
		msg.Value = Json.WriteToBytes(dictionary);
		Connections.Frontend.Send(msg);
	}

	public void EnableMenu(MenuType type, bool enable, bool checkHidden = true)
	{
		if (checkHidden && IsHiddenMenu(type))
		{
			enable = false;
		}
		if (_menuEnabled[(int)type] != enable)
		{
			_menuEnabled[(int)type] = enable;
			_enableMenuUpdated.Call(this);
		}
	}

	public bool IsEnabled(MenuType type)
	{
		return _menuEnabled[(int)type];
	}

	public IEnumerable<MenuType> GetRecentlyUnlockedMenus()
	{
		return Enums<MenuType>.All().Where(IsRecentlyUnlocked);
	}

	public bool IsRecentlyUnlocked(MenuType type)
	{
		if (!IsEnabled(type))
		{
			return false;
		}
		return _recentlyUnlocked[(int)type];
	}

	public void SetRecentlyUnlocked(MenuType type, bool on)
	{
		if (_recentlyUnlocked[(int)type] != on)
		{
			_recentlyUnlocked[(int)type] = on;
			SaveRecentlyUnlocked();
			_enableMenuUpdated.Call(this);
		}
	}
}
