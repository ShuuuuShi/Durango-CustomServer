using System;
using System.Collections.Generic;
using Durango.UI.Control;
using UnityEngine;
using Yaml;
using Yaml.Util;

namespace Durango.UI;

public class SelectPersonalRegion : MonoBehaviour, IEditPlayerDisplayPage
{
	[SerializeField]
	private KScrollView _scrollView;

	[SerializeField]
	private SelectableButton _button;

	[SerializeField]
	private Texture[] _regionTextures;

	private AnimationWidget _animWidget;

	public string SelectedRegionid { get; private set; }

	public event Action<string> Selected;

	public event Action Confirmed;

	private void Awake()
	{
		SelectableButton button = _button;
		button.Clicked = (Action)Delegate.Combine(button.Clicked, (Action)delegate
		{
			if (!string.IsNullOrEmpty(SelectedRegionid))
			{
				if (Selected != null)
				{
					Selected(SelectedRegionid);
				}
				if (Confirmed != null)
				{
					Confirmed();
				}
			}
		});
		ListObjectPool nodes = _scrollView.Nodes;
		nodes.BeginLoad();
		List<string> regionTemplateIds = Singleton<Constants>.Instance.PersonalRegion.RegionTemplateIds;
		int size = KUtility.GetSize(regionTemplateIds);
		for (int num = 0; num < size; num++)
		{
			GameObject next = nodes.GetNext();
			SelectableWidget component = next.GetComponent<SelectableWidget>();
			int index = num;
			component.Clicked = (Action)Delegate.Combine(component.Clicked, (Action)delegate
			{
				SelectNode(index);
			});
			UITexture uITexture = next.FindComponent<UITexture>("Texture");
			// **ค่าของเรา**: prefab ฝั่ง PC มี _regionTextures น้อยกว่าจำนวน region_template_ids
			// ใน constants ของเซิร์ฟ (IndexOutOfRangeException กลาง Awake — log เกม 6 ก.ย. 2026)
			// ⇒ วนใช้รูปซ้ำแทนดึงเกินขอบ ถ้า prefab ไม่มีรูปเลยก็ปล่อยค่าเดิมไว้ อย่าให้แครช
			if (_regionTextures != null && _regionTextures.Length > 0)
			{
				uITexture.mainTexture = _regionTextures[num % _regionTextures.Length];
			}
		}
		nodes.EndLoad();
		_scrollView.ResetPosition();
		SelectNode(UnityEngine.Random.Range(0, size));
	}

	private void SelectNode(int index)
	{
		for (int i = 0; i < _scrollView.Nodes.Count; i++)
		{
			SelectableWidget selectableWidget = _scrollView.Nodes.Get<SelectableWidget>(i);
			selectableWidget.Selected = i == index;
		}
		List<string> regionTemplateIds = Singleton<Constants>.Instance.PersonalRegion.RegionTemplateIds;
		if (index >= 0 && index < KUtility.GetSize(regionTemplateIds))
		{
			SelectedRegionid = regionTemplateIds[index];
		}
	}

	public void Initialize(EditPlayerDisplayProxy display)
	{
		_animWidget = AnimationWidget.Get(base.gameObject, 0.3f, 0f, deactiveWhenFadeout: true);
	}

	public void Show(bool instant)
	{
		base.gameObject.SetActive(value: true);
		_animWidget.SetAlpha(1f, !instant);
		int index = Singleton<Constants>.Instance.PersonalRegion.RegionTemplateIds.IndexOf(SelectedRegionid);
		_scrollView.MoveToVisibleArea(index, instant: true);
	}

	public void Hide(bool instant)
	{
		_animWidget.SetAlpha(0f, !instant);
	}

	public Transform GetModelPosition()
	{
		return null;
	}

	public void SetConfirmText(string text)
	{
		_button.Text = text;
	}

	public void WaitForLoading(bool loading)
	{
		UIManager.ShowLoadingIcon(loading);
		_button.Disabled = loading;
	}
}
