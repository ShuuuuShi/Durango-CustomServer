using System;
using Durango.UI.Control;
using L10N;
using UnityEngine;

namespace Durango.UI;

public class FollowPlayerInfoWidget : PlayerInfoWidget
{
	[SerializeField]
	private SelectableButton _removeButton;

	public event Action<string> RemoveClicked;

	private void Start()
	{
		_removeButton.Clicked = delegate
		{
			if (RemoveClicked != null)
			{
				RemoveClicked(base.EntityId);
			}
		};
		_removeButton.Text = T._("즐겨찾기 제거");
	}
}
