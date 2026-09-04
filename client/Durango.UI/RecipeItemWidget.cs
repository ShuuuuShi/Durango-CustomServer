using Crafting;
using Durango.UI.Control;
using UnityEngine;

namespace Durango.UI;

public class RecipeItemWidget : SelectableWidget
{
	[SerializeField]
	private UILabel _nameLabel;

	[SerializeField]
	private UISprite _icon;

	[SerializeField]
	private GameObject _newIcon;

	[SerializeField]
	private TweenerPlayer _likeIcon;

	[SerializeField]
	private UISprite _seasonIcon;

	public CategoryItem Item { get; private set; }

	public void Set(RecipeSubListWidget.Data data)
	{
		bool? canCraft = data.CanCraft;
		if (!canCraft.HasValue)
		{
			data.CanCraft = GameSystem<RecipeSystem>.Instance().CanCraftNow(data.Item);
		}
		base.Disabled = !data.CanCraft.Value;
		CategoryItem item = Item;
		CategoryItem categoryItem = (Item = data.Item);
		_nameLabel.text = categoryItem.Name;
		_icon.spriteName = categoryItem.Icon;
		_likeIcon.gameObject.SetActive(categoryItem.Like);
		if (item == categoryItem && !_likeIcon.gameObject.activeSelf && categoryItem.Like)
		{
			_likeIcon.Play();
		}
		_newIcon.gameObject.SetActive(categoryItem.Notification.On);
		SeasonUtil.SetSmallIcon(_seasonIcon, categoryItem.Season);
	}

	protected override void OnInit()
	{
		base.CanClickWhenDisabled = true;
	}

	protected override void OnRefresh(State state)
	{
		if (base.Pressed)
		{
			SetWidgetState(State.Pressed);
		}
		else if (base.Selected)
		{
			SetWidgetState(State.Selected);
		}
		else if (base.Hovered)
		{
			SetWidgetState(State.Hovered);
		}
		else
		{
			base.OnRefresh(state);
		}
	}
}
