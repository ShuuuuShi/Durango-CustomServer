using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Logic.Clusters;
using Durango.Logic.Market;
using Durango.UI.Control;
using L10N;
using UnityEngine;

namespace Durango.UI;

public class MarketCategoriesWidget : MonoBehaviour
{
	[SerializeField]
	private KGridScrollView _categories;

	[LocalizableString]
	[SerializeField]
	private string _viewAllText;

	[SerializeField]
	private SpriteData _viewAllIcon;

	[LocalizableString]
	[SerializeField]
	private string _searchText;

	[SerializeField]
	private SpriteData _searchIcon;

	private UIWidget _widget;

	private Category[] _categoryList;

	private bool _isInit;

	public UIWidget Widget => (!(_widget == null)) ? _widget : (_widget = GetComponent<UIWidget>());

	public event Action<Category.Main> MainCategorySelected;

	public event Action SearchSelected;

	private void Init()
	{
		if (_isInit)
		{
			return;
		}
		_isInit = true;
		_categoryList = GameSystem<MarketSystem>.Instance().CategoryYamlData;
		if (_categoryList == null)
		{
			_isInit = false;
			return;
		}
		if (GameManager.ClusterMode != Mode.Online)
		{
			KeyValuePair<string, string>[] allowedMarketCategoriesInFreeMode = new KeyValuePair<string, string>[7]
			{
				new KeyValuePair<string, string>("accessory", string.Empty),
				new KeyValuePair<string, string>("seed", "seed"),
				new KeyValuePair<string, string>("clothing", string.Empty),
				new KeyValuePair<string, string>("material", "material_building"),
				new KeyValuePair<string, string>("weapon/tool", string.Empty),
				new KeyValuePair<string, string>("building/furniture", string.Empty),
				new KeyValuePair<string, string>("taming", string.Empty)
			};
			_categoryList = _categoryList.Where((Category category3) => allowedMarketCategoriesInFreeMode.Any((KeyValuePair<string, string> x) => x.Key == category3.MainCategory.Id)).ToArray();
			Category[] categoryList = _categoryList;
			foreach (Category category in categoryList)
			{
				string sub = allowedMarketCategoriesInFreeMode.Where((KeyValuePair<string, string> x) => x.Key == category.MainCategory.Id).FirstOrDefault().Value;
				if (!string.IsNullOrEmpty(sub) && category.Subs != null)
				{
					category.Subs = category.Subs.Where((Category.Sub x) => x.Id == sub).ToArray();
				}
			}
		}
		ListObjectPool nodes = _categories.Nodes;
		nodes.Init(OnInitNodes);
		nodes.Set(_categoryList.Length + 2);
		for (int num2 = 0; num2 < _categoryList.Length; num2++)
		{
			GameObject gameObject = nodes[num2 + 2];
			Category category2 = _categoryList[num2];
			gameObject.transform.Find("name").GetComponent<UILabel>().text = category2.MainCategory.Name;
			gameObject.transform.Find("icon").GetComponent<UISprite>().spriteName = category2.MainCategory.Icon;
		}
		nodes[0].transform.Find("name").GetComponent<UILabel>().text = T._(_viewAllText);
		UISprite component = nodes[0].transform.Find("icon").GetComponent<UISprite>();
		component.spriteName = _viewAllIcon.sprite;
		nodes[1].transform.Find("name").GetComponent<UILabel>().text = T._(_searchText);
		component = nodes[1].transform.Find("icon").GetComponent<UISprite>();
		component.spriteName = _searchIcon.sprite;
		_categories.ResetPosition();
	}

	private void Start()
	{
		Init();
	}

	private void OnInitNodes(GameObject obj)
	{
		Selectable component = obj.GetComponent<Selectable>();
		component.Clicked = (Action)Delegate.Combine(component.Clicked, new Action(OnClickCategory));
	}

	private void OnClickCategory()
	{
		UISound.PlayClick(UISound.ClickType.ButtonMedium);
		int num = _categories.Nodes.IndexOf(Selectable.Current.gameObject);
		switch (num)
		{
		case 0:
			if (MainCategorySelected != null)
			{
				MainCategorySelected(null);
			}
			break;
		case 1:
			if (SearchSelected != null)
			{
				SearchSelected();
			}
			break;
		default:
			if (MainCategorySelected != null)
			{
				MainCategorySelected(_categoryList[num - 2].MainCategory);
			}
			break;
		}
	}

	public void SelectCategory(Category.Main category)
	{
		int num = -1;
		if (category != null)
		{
			for (int i = 0; i < _categoryList.Length; i++)
			{
				if (_categoryList[i].MainCategory.Id == category.Id)
				{
					num = i + 2;
				}
			}
		}
		ListObjectPool nodes = _categories.Nodes;
		for (int j = 0; j < nodes.Count; j++)
		{
			SelectableWidget component = nodes[j].GetComponent<SelectableWidget>();
			component.Selected = num == j;
		}
	}
}
