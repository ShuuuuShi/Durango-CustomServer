using System.Text;
using Durango.Logic.Explore;
using Durango.Logic.PlayGuide;
using Durango.UI;
using Durango.UI.Control;
using Durango.Utils;
using L10N;
using Messages;
using Shared.Economy;
using Yaml;

namespace Durango.Logic;

public class ArchipelagoToDoCollection : ToDoCollection
{
	public enum State
	{
		Doing,
		Reportable,
		Done,
		CanDo
	}

	public readonly Observable<int> CurrentPoint = new Observable<int>();

	public int ClearPoint;

	public Durango.Logic.Explore.Region ActiveRegion;

	public Dialogue Intro;

	public Dialogue Outro;

	public string Description;

	public RewardInfo? Reward;

	public State CurrentState;

	public bool ShowUI;

	public bool HasEnoughPoint => (int)CurrentPoint >= ClearPoint;

	public ArchipelagoToDoCollection()
	{
		SetHelpClicked(delegate
		{
			MissionGroup missionGroup = UIManager.FindScript<MissionGroup>();
			if (!(missionGroup == null))
			{
				MissionInfoPopup.Data mission = new MissionInfoPopup.Data
				{
					ClientName = string.Format("[icon=icon_mission]  {0}", T._("개척 임무")),
					Subject = Title,
					Reward = Reward
				};
				using (Reusable<StringBuilder> reusable = ReusableStringBuilder.Pop())
				{
					StringBuilder value = reusable.Value;
					foreach (ToDoBase toDo in ToDoList)
					{
						if (toDo is ArchipelagoToDo archipelagoToDo)
						{
							value.Append(T._("<em>{0}</em> ({1:pt:})\n", archipelagoToDo.LocalText, archipelagoToDo.Point));
						}
					}
					value.Append("\n");
					value.Append(Description);
					mission.Description = value.ToString();
				}
				missionGroup.Open(mission, isAcceptable: false);
			}
		});
	}

	public override string GetSubIcon()
	{
		return "mission_unstable_factor";
	}

	public override Detail? GetDetail()
	{
		if (!ShowUI)
		{
			return null;
		}
		switch (CurrentState)
		{
		case State.Doing:
			return new Detail
			{
				IsHeaderVisible = true,
				IsTodoListVisible = true,
				Progress = new Pair<int, int>(CurrentPoint, ClearPoint)
			};
		case State.Reportable:
		{
			string arg = string.Format("<em>[icon=icon_mission_todo] {0}</em>", T._("개척 임무 단계 완료"));
			string arg2 = T._("개척 임무를 완료했습니다.\n진행 상황을 보고하세요.");
			return new Detail
			{
				CommonText = $"\n[size=24]{arg}[/size]<br>10</br>{arg2}",
				CommonTextAlignment = NGUIText.Alignment.Center,
				ButtonText = T._("임무 보고"),
				ButtonClicked = ReportArchipelagoMission,
				ButtonEffect = PresetButton.Effect.Emphasis,
				ButtonStyle = PresetButton.Style.Solid
			};
		}
		case State.Done:
		{
			string arg = T._("<em>다음 단계 진행</em>");
			string arg2 = T._("개척 임무를 완료했습니다.\n다음 섬으로 이동하세요.");
			return new Detail
			{
				CommonText = $"\n[size=24]{arg}[/size]<br>10</br>{arg2}",
				CommonTextAlignment = NGUIText.Alignment.Center,
				ButtonText = string.Format("[icon=icon_map_warphole] {0}", T._("다음 섬으로")),
				ButtonClicked = WarpToNextArchipelagoRegion,
				ButtonStyle = PresetButton.Style.Border
			};
		}
		case State.CanDo:
		{
			string arg = T._("<em>다른 군도의 개척 임무</em>");
			string arg2 = T._("<em>{0}</em> 섬에서 진행중인 개척 임무가 있습니다.", ActiveRegion.Name);
			return new Detail
			{
				CommonText = $"\n[size=24]{arg}[/size]<br>38</br>{arg2}",
				CommonTextAlignment = NGUIText.Alignment.Center,
				ButtonText = T._("개척 임무 새로 받기"),
				ButtonClicked = RequestNewArchipelagoMission,
				ButtonStyle = PresetButton.Style.Border
			};
		}
		default:
			return null;
		}
	}

	public void Update(NotifyArchipelagoTodoProceed todoProgress)
	{
		CurrentPoint.Value = todoProgress.CurrentPoint;
		Messages.ArchipelagoToDo todo = todoProgress.Todo;
		foreach (ToDoBase toDo in ToDoList)
		{
			if (toDo is ArchipelagoToDo archipelagoToDo && !(archipelagoToDo.Key != todo.Id))
			{
				archipelagoToDo.CurrentProgress = todo.Progress;
			}
		}
	}

	private static void ReportArchipelagoMission()
	{
		GameSystem<ArchipelagoMissionSystem>.Instance().RequestRegionClear();
	}

	private static void WarpToNextArchipelagoRegion()
	{
		string nextRegion = GameSystem<ArchipelagoMissionSystem>.Instance().GetNextRegion();
		if (string.IsNullOrEmpty(nextRegion))
		{
			return;
		}
		GameSystem<MapSystem>.Instance().GetRegion(nextRegion, delegate(Messages.Region region)
		{
			ArchipelagoMissionSystem.RequestWarpCost(delegate(long cost)
			{
				string comment = T._("<em>{0}</em> 섬으로 이동하시겠습니까?", region.Name);
				UIManager.MessageBox.ShowPayConfirm(cost, Currency.TStone, comment, null, delegate(bool ok)
				{
					if (ok)
					{
						ExploreSystem.WarpToNextArchipelagoRegion(region);
					}
				});
			});
		});
	}

	private static void RequestNewArchipelagoMission()
	{
		UIManager.MessageBox.Show(T._("개척 임무를 새로 받으면\n진행중인 개척 임무는 <em>초기화</em>됩니다.\n\n이 군도의 개척 임무를 새로 받으시겠습니까?"), null, delegate(bool ok)
		{
			if (ok)
			{
				ArchipelagoMissionSystem.RequestReissueArchipelagoTodos();
			}
		});
	}
}
