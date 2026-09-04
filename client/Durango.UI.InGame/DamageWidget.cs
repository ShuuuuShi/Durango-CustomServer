using Durango.UI.Control;
using L10N;
using Messages;
using Shared.Battle;
using UnityEngine;

namespace Durango.UI.InGame;

public class DamageWidget : UIWidget
{
	[SerializeField]
	private UILabel _partTextLabel;

	[SerializeField]
	private UILabel _subTextLabel;

	[SerializeField]
	private UILabel _mainTextLabel;

	[SerializeField]
	private TweenerPlayer _tweener;

	[SerializeField]
	private RectLayout _layout;

	public void Set(Damage damage)
	{
		_partTextLabel.text = $"{damage.Direction.GetName()}\n{damage.Part.GetName()}";
		string text = null;
		Color color = new Color32(byte.MaxValue, 184, 0, byte.MaxValue);
		int fontSize = 60;
		string text2 = null;
		Color color2 = new Color32(byte.MaxValue, 184, 0, byte.MaxValue);
		switch (damage.Result)
		{
		case DamageResult.Hit:
			text = damage.Value.ToString();
			break;
		case DamageResult.Guarded:
		case DamageResult.AutoGuarded:
			text = T._("막음");
			color = PresetColor.UISkyBlue;
			fontSize = 24;
			break;
		case DamageResult.Dodged:
		case DamageResult.Evaded:
		case DamageResult.AutoDodged:
			text = T._("피함");
			fontSize = 24;
			break;
		case DamageResult.Missed:
			text = damage.Value.ToString();
			color = new Color32(byte.MaxValue, 215, 111, byte.MaxValue);
			fontSize = 48;
			text2 = T._("빗나감");
			color2 = PresetColor.UIYellow;
			break;
		default:
			text = damage.Value.ToString();
			color = PresetColor.UILightGray;
			fontSize = 24;
			break;
		}
		if ((damage.Effects & DamageEffects.Critical) > DamageEffects.None)
		{
			text2 = T._("치명타");
			color2 = new Color32(byte.MaxValue, 184, 0, byte.MaxValue);
		}
		if (string.IsNullOrEmpty(text))
		{
			_mainTextLabel.gameObject.SetActive(value: false);
		}
		else
		{
			_mainTextLabel.gameObject.SetActive(value: true);
			_mainTextLabel.text = text;
			_mainTextLabel.color = color;
			_mainTextLabel.fontSize = fontSize;
		}
		if (string.IsNullOrEmpty(text2))
		{
			_subTextLabel.gameObject.SetActive(value: false);
		}
		else
		{
			_subTextLabel.gameObject.SetActive(value: true);
			_subTextLabel.text = text2;
			_subTextLabel.color = color2;
		}
		_layout.UpdateLayout();
	}

	public void ShowAnimation()
	{
		_tweener.Play();
	}
}
