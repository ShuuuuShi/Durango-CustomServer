using System;
using System.Collections.Generic;
using Durango.Logic.Clusters;
using Durango.System.Config;
using Durango.UI.Control;
using UnityEngine;

namespace Durango.UI;

public class ConfigTabWidget : MonoBehaviour
{
	public Action<string> TabClicked;

	[SerializeField]
	private KScrollView _scrollView;

	private int _currentIndex = -1;

	public bool IsInit { get; private set; }

	public string CurrentCategory { get; private set; }

	public void Init()
	{
		if (!IsInit)
		{
			IsInit = true;
			CreateTabs();
			SelectTab(0);
		}
	}

	public void Reposition()
	{
		_scrollView.ScrollView.movement = ((!UIManager.IsPortraitWidget(base.gameObject)) ? UIScrollView.Movement.Vertical : UIScrollView.Movement.Horizontal);
		_scrollView.Reposition();
	}

	private void CreateTabs()
	{
		_scrollView.Nodes.Clear();
		foreach (string item in EnumerateSettings())
		{
			ConfigTabItem configTabItem = _scrollView.Nodes.Add<ConfigTabItem>();
			configTabItem.Set(item);
			configTabItem.Clicked = (Action)Delegate.Combine(configTabItem.Clicked, new Action(OnTabClick));
		}
	}

	private static IEnumerable<string> EnumerateSettings()
	{
		foreach (KeyValuePair<string, List<Setting>> kv in ConfigInstance.Settings)
		{
			List<Setting> settings = kv.Value;
			if (GameManager.ClusterMode != Mode.Online && kv.Key != "default" && kv.Key != "screen")
			{
				continue;
			}
			bool isHidden = true;
			for (int i = 0; i < settings.Count; i++)
			{
				if (!Setting.IsHidden(settings[i]))
				{
					isHidden = false;
					break;
				}
			}
			if (!isHidden)
			{
				yield return kv.Key;
			}
		}
	}

	private void OnTabClick()
	{
		int num = _scrollView.Nodes.IndexOf(Selectable.Current.gameObject);
		if (num != -1)
		{
			SelectTab(num);
		}
	}

	public void SelectTab(string category)
	{
		for (int i = 0; i < _scrollView.Nodes.Count; i++)
		{
			ConfigTabItem component = _scrollView.Nodes[i].GetComponent<ConfigTabItem>();
			if (component != null && component.Category == category)
			{
				SelectTab(i);
				break;
			}
		}
	}

	private void SelectTab(int index)
	{
		if (_currentIndex == index)
		{
			return;
		}
		for (int i = 0; i < _scrollView.Nodes.Count; i++)
		{
			ConfigTabItem component = _scrollView.Nodes[i].GetComponent<ConfigTabItem>();
			if (!(component == null))
			{
				component.Selected = i == index;
				if (i == index)
				{
					_currentIndex = index;
					CurrentCategory = component.Category;
				}
			}
		}
		if (TabClicked != null)
		{
			TabClicked(CurrentCategory);
		}
	}
}
