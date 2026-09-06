using System;
using System.Text;
using Building;
using Crafting;
using Durango.UI.Control;
using Durango.UI.Popup;
using Durango.Utils;
using JetBrains.Annotations;
using L10N;
using Messages;
using UnityEngine;
using Yaml;
using Yaml.Util;

namespace Durango.UI;

public class ExpectResultWidget : UIWidget
{
	[SerializeField]
	private UILabel _helpLabel;

	[SerializeField]
	private GameObject _moreInfoButton;

	[SerializeField]
	private GameObject _helpTouchBox;

	[SerializeField]
	private UIWidget _previewWidget;

	[SerializeField]
	private UISprite _iconResult;

	[SerializeField]
	private UILabel _iconResultLabel;

	[SerializeField]
	private UIModelViewer _previewTexture;

	[SerializeField]
	private TweenerPlayer _rareResultEffect;

	[SerializeField]
	private UILabel _textName;

	[SerializeField]
	private ExpectResultDetailWidget _expectResultDetailWidget;

	[SerializeField]
	private UIWidget _bonusItemWidget;

	[SerializeField]
	private IntSelector _quantitySelector;

	[SerializeField]
	private UILabel _textSuccessRate;

	[SerializeField]
	private UIWidget _remodelingWidget;

	[SerializeField]
	private Color[] _resultLevelRateTextColors;

	[SerializeField]
	private int[] _resultLevelRatePercentages;

	private bool _isCraft;

	private CraftSlotContainer _craft;

	private BuildSlotContainer _build;

	private CraftEstimationInfo? _craftEstimation;

	private ArtifactPreview? _previewMsg;

	// ── แพตช์ UI ฝั่ง PC ───────────────────────────────────────────────────
	// prefab ของ PC ผูกวิดเจ็ตไม่ครบเท่าของมือถือ ฟิลด์ที่ไม่ถูกผูกจะเป็น null
	// ของเดิมอ้างตรง ๆ พอเป็น null เลย NRE กลาง Open() แล้วหน้าต่างไม่เด้งขึ้นมาเลย
	// วิดเจ็ตที่ขาดเป็นแค่ป้าย/แผงเสริม ข้ามได้ ไม่กระทบการใส่วัสดุและการกดสร้าง
	private bool _reportedMissingWidgets;

	private bool IsRemodeling => !_isCraft && _build != null && _build.Blueprint.IsRemodeling;

	protected override void OnStart()
	{
		base.OnStart();
		if (!Application.isPlaying)
		{
			return;
		}
		if (_helpTouchBox != null)
		{
			UIEventListener uIEventListener = UIEventListener.Get(_helpTouchBox);
			uIEventListener.onClick = (UIEventListener.VoidDelegate)Delegate.Combine(uIEventListener.onClick, new UIEventListener.VoidDelegate(ShowHelpTooltip));
		}
		if (_previewWidget != null)
		{
			UIEventListener uIEventListener2 = UIEventListener.Get(_previewWidget.gameObject);
			uIEventListener2.onClick = (UIEventListener.VoidDelegate)Delegate.Combine(uIEventListener2.onClick, new UIEventListener.VoidDelegate(ShowPreviewPopup));
		}
		if (_bonusItemWidget != null)
		{
			UIEventListener uIEventListener3 = UIEventListener.Get(_bonusItemWidget.gameObject);
			uIEventListener3.onClick = (UIEventListener.VoidDelegate)Delegate.Combine(uIEventListener3.onClick, (UIEventListener.VoidDelegate)delegate
			{
				if (_craft != null)
				{
					int? level = null;
					if (_craftEstimation.HasValue)
					{
						level = _craftEstimation.Value.CraftLevel;
					}
					ReceiveRewardsPopup receiveRewardsPopup = UIManager.Popup.Tooltip<ReceiveRewardsPopup>();
					receiveRewardsPopup.ShowRecipeBonusInfo(_craft.Recipe.Id, level);
				}
			});
		}
		if (_quantitySelector != null)
		{
			IntSelector quantitySelector = _quantitySelector;
			quantitySelector.ValueChanged = (Action)Delegate.Combine(quantitySelector.ValueChanged, new Action(OnQuantityChanged));
		}
	}

	protected override void OnDisable()
	{
		base.OnDisable();
		_previewMsg = null;
	}

	public void Set(CraftSlotContainer slotContainer)
	{
		_isCraft = true;
		_craft = slotContainer;
		ReportMissingWidgetsOnce();
		RefreshHelpLabel();
		SetVisible(_remodelingWidget, value: false);
		if (_craft.TechSupportBaseSlotInfo == null)
		{
			int max = Mathf.Max(1, _craft.CalcMaxQuantity());
			SetVisible(_quantitySelector, value: true);
			if (_quantitySelector != null)
			{
				_quantitySelector.Set(_craft.Quantity, 1, max);
			}
		}
		else
		{
			SetVisible(_quantitySelector, value: false);
		}
		BonusPrototypes[] array = SingletonDict<string, BonusPrototypes[]>.Get(slotContainer.Recipe.Id);
		SetVisible(_bonusItemWidget, array != null && array.Length > 0);
	}

	public void Set(BuildSlotContainer slotContainer)
	{
		_isCraft = false;
		_build = slotContainer;
		ReportMissingWidgetsOnce();
		RefreshHelpLabel();
		SetVisible(_remodelingWidget, slotContainer.Blueprint.IsRemodeling);
		SetVisible(_quantitySelector, value: false);
		SetVisible(_bonusItemWidget, value: false);
	}

	public void Refresh()
	{
		if (_expectResultDetailWidget != null)
		{
			if (_isCraft)
			{
				_expectResultDetailWidget.Set(_craft);
			}
			else
			{
				_expectResultDetailWidget.Set(_build);
			}
		}
		UpdateLayout();
	}

	public void ClearEstimation()
	{
		if (_isCraft)
		{
			SetCraftEstimation(null);
		}
		else
		{
			SetBuildEstimation(null);
		}
		if (_expectResultDetailWidget != null)
		{
			_expectResultDetailWidget.ClearEstimation();
		}
	}

	public void SetPreviewTextureMode()
	{
		SetVisible(_iconResult, value: false);
		SetVisible(_iconResultLabel, value: false);
		SetVisible(_previewTexture, value: true);
		SetVisible(_moreInfoButton, value: true);
		if (_previewWidget != null)
		{
			_previewWidget.height = 215;
		}
		UpdateLayout();
	}

	public void SetIconMode(bool expectPreviewTexture, bool isBig = false)
	{
		SetVisible(_iconResult, value: true);
		SetVisible(_previewTexture, value: false);
		SetVisible(_moreInfoButton, value: false);
		SetVisible(_iconResultLabel, expectPreviewTexture);
		if (_iconResult != null)
		{
			_iconResult.alpha = ((!expectPreviewTexture) ? 1f : 0.2f);
		}
		SetText(_iconResultLabel, T._("모든 재료를 선택하면 미리보기 가능"));
		if (_previewWidget != null)
		{
			_previewWidget.height = ((!isBig) ? 120 : 215);
		}
		UpdateLayout();
	}

	private void UpdateLayout()
	{
		RectLayoutComponent component = GetComponent<RectLayoutComponent>();
		if (component != null)
		{
			component.UpdateLayout();
		}
		UIUtility.UpdateAnchors(base.transform);
	}

	public void SetCraftEstimation(CraftEstimationInfo? info)
	{
		_craftEstimation = info;
		CraftEstimation? estimation = (info.HasValue ? info.Value.CraftEstimation : ((CraftEstimation?)null));
		Crafting.Recipe recipe = ((_craft != null) ? _craft.Recipe : null);
		string arg = ((recipe != null) ? recipe.Name : string.Empty);
		int? resultLevel = null;
		bool isImmuneToTime = false;
		bool isTimeLimited = false;
		if (estimation.HasValue)
		{
			CraftEstimation value = estimation.Value;
			Prototype itemPrototype = PrototypeYaml.GetItemPrototype(value.PrototypeId, value.Level);
			if (itemPrototype != null)
			{
				if (_iconResult != null)
				{
					_iconResult.spriteName = itemPrototype.Icon;
				}
				isImmuneToTime = itemPrototype.ImmuneToTime;
				isTimeLimited = itemPrototype.TimeLimited;
			}
			resultLevel = value.Level;
			arg = value.Name;
		}
		else if (_iconResult != null)
		{
			_iconResult.spriteName = ((recipe != null) ? recipe.Icon : string.Empty);
		}
		string arg2 = ((_craft != null) ? CreateLevelText(resultLevel, _craft.GetAverageMaterialsLevel(0)) : string.Empty);
		int num = (recipe?.Count ?? 0) * ((_craft == null) ? 1 : _craft.Quantity);
		SetText(_textName, (num > 1)
			? string.Format("{0} {1} [size=24][FFFFFF7F]/ {2}[-][/size]", arg, arg2, T._("{0}개", num))
			: $"{arg} {arg2}");
		SetText(_textSuccessRate, estimation.HasValue
			? $"{estimation.Value.SuccessRate:P0}  <em>[icon=icon_question_big]</em>"
			: "-  <em>[icon=icon_question_big]</em>");
		if (_expectResultDetailWidget != null)
		{
			_expectResultDetailWidget.SetEstimation(estimation, recipe as RecipeReform, isImmuneToTime, isTimeLimited);
		}
		if (!estimation.HasValue || estimation.Value.UnrevealedRareTagCount == 0)
		{
			SetVisible(_rareResultEffect, value: false);
			return;
		}
		SetVisible(_rareResultEffect, value: true);
		if (_rareResultEffect != null)
		{
			_rareResultEffect.Play();
		}
	}

	public void SetBuildEstimation(BuildEstimation? estimation)
	{
		Building.Blueprint blueprint = ((_build != null) ? _build.Blueprint : null);
		string arg = ((blueprint != null) ? blueprint.Name : string.Empty);
		int? resultLevel = null;
		if (estimation.HasValue)
		{
			resultLevel = estimation.Value.Level;
			SetPreview(estimation.Value.ArtifactPreview);
		}
		if (_iconResult != null)
		{
			_iconResult.spriteName = ((blueprint != null) ? blueprint.Icon : string.Empty);
		}
		string arg2 = ((_build != null) ? CreateLevelText(resultLevel, _build.GetAverageMaterialsLevel(0)) : string.Empty);
		SetText(_textName, $"{arg} {arg2}");
		if (_expectResultDetailWidget != null)
		{
			_expectResultDetailWidget.SetEstimation(estimation);
		}
		if (!estimation.HasValue || estimation.Value.UnrevealedRareTagCount == 0)
		{
			SetVisible(_rareResultEffect, value: false);
		}
		else
		{
			SetVisible(_rareResultEffect, value: true);
			if (_rareResultEffect != null)
			{
				_rareResultEffect.Play();
			}
		}
		SetText(_textSuccessRate, $"{1f:P0}  <em>[icon=icon_question_big]</em>");
	}

	public void SetRemodelingEstimation([NotNull] Artifact artifact, ArtifactPreview? artifactPreview)
	{
		SetPreview(artifactPreview);
		if (_iconResult != null)
		{
			_iconResult.spriteName = ((artifact.Blueprint != null) ? artifact.Blueprint.Icon : string.Empty);
		}
		SetText(_textName, string.Format("{0} {1}", (artifact.Blueprint != null) ? artifact.Blueprint.Name : string.Empty, NGUIText.EncodeColor(T._("{0:lv:}", artifact.ArtifactState.Level), PresetColor.UIYellow)));
		SetText(_textSuccessRate, $"{1f:P0}  <em>[icon=icon_question_big]</em>");
		if (_expectResultDetailWidget != null)
		{
			_expectResultDetailWidget.SetEstimation(artifact);
		}
		SetVisible(_rareResultEffect, value: false);
	}

	public void SetTechSupportEstimation(TechSupportBaseSlotInfo slotInfo)
	{
		TechSupportTarget target = slotInfo?.Target ?? default(TechSupportTarget);
		RecipeReform reformRecipe = TechSupportSystem.GetReformRecipe(target.GetReformSlot());
		Prototype prototype = ((target.Item == null) ? null : PrototypeYaml.GetItemPrototype(target.Item.PrototypeId, target.Item.Level));
		if (_iconResult != null)
		{
			_iconResult.spriteName = ((prototype == null) ? string.Empty : prototype.Icon);
		}
		SetText(_textName, (target.Item != null)
			? string.Format("{0} {1}", target.Item.Name, NGUIText.EncodeColor(T._("{0:lv:}", target.Item.Level), PresetColor.UIYellow))
			: string.Empty);
		SetText(_textSuccessRate, $"{1f:P0}  <em>[icon=icon_question_big]</em>");
		if (_expectResultDetailWidget != null)
		{
			_expectResultDetailWidget.SetEstimation(GameSystem<TechSupportSystem>.Instance().GetEstimate(target), reformRecipe);
		}
		SetVisible(_rareResultEffect, value: false);
	}

	private void SetPreview(ArtifactPreview? artifactPreview)
	{
		if (artifactPreview.HasValue)
		{
			SetPreviewTextureMode();
			ArtifactPreview value = artifactPreview.Value;
			_previewMsg = value;
			if (_previewTexture == null)
			{
				return;
			}
			_previewTexture.SetArtifactModel(new UIModelViewer.ArtifactArguments
			{
				Display = value.Display,
				Size = value.Size,
				Rotation = value.Rotation,
				IsModular = value.IsModular
			}, new UIModelViewer.Arguments
			{
				CameraAngle = 35f,
				Rotation = -45f
			});
		}
		else
		{
			_previewMsg = null;
			SetIconMode(expectPreviewTexture: false, isBig: true);
		}
	}

	private void RefreshHelpLabel()
	{
		SetText(_helpLabel, (!IsRemodeling) ? T._("예상 결과") : T._("개조 결과"));
	}

	// ซ่อน/แสดงวิดเจ็ตที่ prefab อาจไม่มี — ไม่มีก็ข้ามไปเงียบ ๆ
	private static void SetVisible(Component widget, bool value)
	{
		if (widget != null)
		{
			widget.gameObject.SetActive(value);
		}
	}

	private static void SetVisible(GameObject go, bool value)
	{
		if (go != null)
		{
			go.SetActive(value);
		}
	}

	private static void SetText(UILabel label, string text)
	{
		if (label != null)
		{
			label.text = text;
		}
	}

	// บอกครั้งเดียวต่อวิดเจ็ตว่า prefab ตัวนี้ขาดอะไร — ไว้ไล่ปัญหา UI ฝั่ง PC ต่อ
	private void ReportMissingWidgetsOnce()
	{
		if (_reportedMissingWidgets)
		{
			return;
		}
		_reportedMissingWidgets = true;
		StringBuilder stringBuilder = new StringBuilder();
		if (_helpLabel == null)
		{
			stringBuilder.Append(" _helpLabel");
		}
		if (_moreInfoButton == null)
		{
			stringBuilder.Append(" _moreInfoButton");
		}
		if (_previewWidget == null)
		{
			stringBuilder.Append(" _previewWidget");
		}
		if (_iconResult == null)
		{
			stringBuilder.Append(" _iconResult");
		}
		if (_iconResultLabel == null)
		{
			stringBuilder.Append(" _iconResultLabel");
		}
		if (_previewTexture == null)
		{
			stringBuilder.Append(" _previewTexture");
		}
		if (_rareResultEffect == null)
		{
			stringBuilder.Append(" _rareResultEffect");
		}
		if (_textName == null)
		{
			stringBuilder.Append(" _textName");
		}
		if (_expectResultDetailWidget == null)
		{
			stringBuilder.Append(" _expectResultDetailWidget");
		}
		if (_bonusItemWidget == null)
		{
			stringBuilder.Append(" _bonusItemWidget");
		}
		if (_quantitySelector == null)
		{
			stringBuilder.Append(" _quantitySelector");
		}
		if (_textSuccessRate == null)
		{
			stringBuilder.Append(" _textSuccessRate");
		}
		if (_remodelingWidget == null)
		{
			stringBuilder.Append(" _remodelingWidget");
		}
		if (stringBuilder.Length != 0)
		{
			Debug.LogWarning("[ExpectResultWidget] prefab '" + base.name + "' ไม่ได้ผูกวิดเจ็ต:" + stringBuilder);
		}
	}

	private void ShowHelpTooltip(GameObject obj)
	{
		string text = (_isCraft ? ((_craft == null || _craft.TechSupportBaseSlotInfo == null) ? MakeCraftSuccessRateHelpText() : T._("장비의 개조 슬롯 결과만 표시됩니다.")) : ((!IsRemodeling) ? T._("결과물은 예상 결과물과 다를 수 있습니다.") : T._("투입된 재료에 따라 건물 외형이 변경됩니다.")));
		if (!string.IsNullOrEmpty(text) && _textSuccessRate != null)
		{
			UIWidget childSprite = UIUtility.GetChildSprite(_textSuccessRate, 0);
			WidgetTooltipControl widgetTooltipControl = UIManager.Popup.Tooltip<WidgetTooltipControl>();
			widgetTooltipControl.Set(null, text, 500);
			widgetTooltipControl.Direction = TooltipBase.TooltipDirection.Horizontal;
			widgetTooltipControl.Show(childSprite, Vector2.zero, 10f);
		}
	}

	private void ShowPreviewPopup(GameObject go)
	{
		if (_previewMsg.HasValue)
		{
			ModelPreviewPopup modelPreviewPopup = UIManager.Popup.Tooltip<ModelPreviewPopup>();
			modelPreviewPopup.Show(_previewMsg.Value, T._("미리보기"));
		}
	}

	private string MakeCraftSuccessRateHelpText()
	{
		using Reusable<StringBuilder> reusable = ReusableStringBuilder.Pop();
		StringBuilder value = reusable.Value;
		Crafting.Recipe recipe = ((_craft != null) ? _craft.Recipe : null);
		if (recipe != null && recipe.RequiredAbility.HasValue)
		{
			value.Append("<kv>");
			value.AppendFormat("key={0},", T._("사용 능력"));
			value.AppendFormat("value=<em>{0}</em>", recipe.RequiredAbility.Value.GetName());
			value.Append("</kv>");
			value.Append("<br>10</br>");
		}
		if (_craftEstimation.HasValue && _craftEstimation.Value.CraftEstimation.HasValue)
		{
			CraftEstimation value2 = _craftEstimation.Value.CraftEstimation.Value;
			value.Append("<kv>");
			value.AppendFormat("key={0},", T._("난이도"));
			value.AppendFormat("value=<em>{0:0}</em>", value2.RequiredAbilityValue);
			value.Append("</kv>");
			value.Append("<br>10</br>");
			value.Append("<kv>");
			value.AppendFormat("key={0},", T._("성공률"));
			value.AppendFormat("value=<em>{0:P0}</em>", value2.SuccessRate);
			value.Append("</kv>");
			value.Append("<br>10</br>");
			value.Append("<kv>");
			value.AppendFormat("key={0},", T._("대성공률"));
			value.AppendFormat("value=<em>{0:P1}</em>", value2.GreatSuccessRate);
			value.Append("</kv>");
		}
		if (value.Length > 0)
		{
			value.Append("<hr/>");
		}
		value.Append("<li>");
		value.Append(T._("성공률은 제작자의 제작능력, 제작법의 난이도, 도구 수준 등에 영향을 받습니다."));
		value.Append("</li>");
		value.Append("<br>10</br>");
		value.Append("<li>");
		value.Append(T._("결과물의 예상 결과물과 다를 수 있습니다."));
		value.Append("</li>");
		value.Append("<br>10</br>");
		value.Append("<li>");
		value.Append(T._("제작 실패시 결과물이 없을 수도 있습니다."));
		value.Append("</li>");
		return value.ToString();
	}

	private string CreateLevelText(int? resultLevel, float averageMaterialLevel)
	{
		string text;
		Color c;
		if (resultLevel.HasValue)
		{
			int percentage = ((!(averageMaterialLevel > 0f)) ? 100 : Mathf.CeilToInt((float)resultLevel.Value / averageMaterialLevel * 100f));
			text = resultLevel.Value.ToString();
			c = UIUtility.GetValueByPercentage(percentage, _resultLevelRatePercentages, _resultLevelRateTextColors);
		}
		else
		{
			text = "?";
			c = PresetColor.UIYellow;
		}
		return NGUIText.EncodeColor(T._("{0:lv:}", text), c);
	}

	private void OnQuantityChanged()
	{
		if (_isCraft && _craft != null)
		{
			_craft.SetQuantity(_quantitySelector.Value);
		}
	}
}
