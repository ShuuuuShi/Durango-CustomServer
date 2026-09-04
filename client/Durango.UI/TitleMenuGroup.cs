using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using BestHTTP;
using Durango.Logic.Clusters;
using Durango.Logic.Encyclopedia;
using Durango.Network;
using Durango.System;
using Durango.UI.Control;
using Durango.Utils;
using Durango.Utils.Extensions;
using L10N;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yaml.Util;

namespace Durango.UI;

public class TitleMenuGroup : MonoBehaviour
{
	public enum State
	{
		Invalid = -1,
		Initial,
		GetClusterList,
		SelectCluster,
		SelectPlayer,
		Knock,
		CheckDataLoaded,
		CheckSoundManager,
		CheckSpriteManager,
		GetUser,
		NPAGetUser,
		FadeOutPrologue,
		PrologueLoading,
		CheckPrerequsite,
		PostPrerequsite,
		GetAdmission,
		GetFrontend,
		TryConnect,
		Connecting,
		Welcome,
		FadeOutLoading,
		Loading,
		Error,
		IdleInHardcapPosition,
		GetTimedTicketInfo,
		IdleInTimedTicketWaiting
	}

	[Serializable]
	private struct TitleOptions
	{
		public string VideoName;

		public SoundEventType SoundEvent;

		public GameObject[] Objects;
	}

	private const float IdleTimeInHardcap = 15f;

	public Action<bool, State, string> WebResponsed;

	[SerializeField]
	private MediaPlayerCtrl _videoPlayer;

	[SerializeField]
	private UIWidget _videoWidget;

	[EnumList(typeof(GameManager.EmigratedType), false, 0, -1)]
	[SerializeField]
	private List<TitleOptions> _titleList;

	[SerializeField]
	private UIFontSetting _fontSetting;

	[SerializeField]
	private UILabel _debugLogLabel;

	[SerializeField]
	private UILabel _debugWarningLabel;

	[SerializeField]
	private UILabel _debugErrorLabel;

	[SerializeField]
	protected TitleMenuUserControlBase UserControl;

	private string _errorMsg = string.Empty;

	private string _errorMsgDetail = string.Empty;

	private HTTPRequest _request;

	private int _connectAttempt;

	private State _curState = State.Invalid;

	private bool _autoSelectCluster;

	private float _initialStateTime;

	private float _lastHardCapRequestTime;

	private int _lastHardcapPosition = -1;

	private float _lastHardcapDelta = -1f;

	private bool _requestedRetryAdmission;

	private DateTime? _timedTicketInfoUpdatedTimeOnArrival;

	private DateTime _timedTicketClientUtcTimeOnArrival;

	private const float TimedTicketCheckPeriod = 15f;

	private float _timedTicketLastCheckTime;

	private bool _timedTicketInfoRequested;

	private const double TimedTicketInfoUpdateTimeLimit = 180.0;

	private static HashSet<string> _timedTicketSkippableClusters = new HashSet<string>();

	private int _errorFrame;

	private uint _soundInstanceId;

	private TitleLoadingGroup _loadingCurtain;

	private static bool IsLoginProcess => !GameManager.IsPlayerIdSelected && GameManager.Emigrated == GameManager.EmigratedType.None;

	private State CurState
	{
		get
		{
			return _curState;
		}
		set
		{
			State curState = _curState;
			_curState = value;
			if (_curState == State.Initial)
			{
				_initialStateTime = Time.realtimeSinceStartup;
			}
			float num = Time.realtimeSinceStartup - _initialStateTime;
			State state = _curState;
			State curState2 = _curState;
			if (_curState != State.GetAdmission && _curState != State.GetTimedTicketInfo)
			{
				UserControl.SetExplainLabel((!IsDataLoadState(_curState)) ? string.Empty : ManualTranslator.CheckingGameData);
			}
			UserControl.OnStateChanged(value);
			switch (_curState)
			{
			case State.Initial:
				_lastHardcapPosition = -1;
				_lastHardcapDelta = -1f;
				GameManager.SessionToken = string.Empty;
				if (IsLoginProcess)
				{
					GameManager.PlayerId = string.Empty;
				}
				UserControl.Clear();
				UserControl.IsLoginProcess = IsLoginProcess;
				_autoSelectCluster = false;
				if (string.IsNullOrEmpty(GameManager.LastEvictedMsg))
				{
					state = State.GetClusterList;
					break;
				}
				_errorMsg = GameManager.LastEvictedMsg;
				GameManager.LastEvictedMsg = string.Empty;
				state = State.Error;
				break;
			case State.GetClusterList:
			{
				// [แก้เอง 5 ก.ย. 2026] อ่าน clusters.json ที่วางไว้ข้างตัวเกมก่อน ถ้าไม่มีค่อยใช้
				// TextAsset ในเกมเหมือนเดิม — ทำให้เปลี่ยนที่อยู่เซิร์ฟได้โดยไม่ต้อง build DLL ใหม่
				// (ฟอร์แมตเดียวกับ TextAsset ต้นฉบับเป๊ะ รวมฟิลด์ "offline")
				string clusterJson = ReadLocalClusterJson();
				if (string.IsNullOrEmpty(clusterJson))
				{
					TextAsset textAsset = Resources.Load("offline/clusters") as TextAsset;
					clusterJson = ((textAsset != null) ? textAsset.text : null);
				}
				state = ((string.IsNullOrEmpty(clusterJson) || !UserControl.TryUpdateClusters(clusterJson)) ? State.Error : State.SelectCluster);
				break;
			}
			case State.SelectCluster:
			{
				UserControl.SetExplainLabel(ManualTranslator.TouchTheScreen);
				bool confirmed = false;
				Action action = delegate
				{
					if (!confirmed)
					{
						if (GameManager.PlayerId == null || string.IsNullOrEmpty(GameManager.GatewayUrl))
						{
							CurState = State.Error;
						}
						else
						{
							Cluster selectedCluster = UserControl.GetSelectedCluster();
							if (selectedCluster.OnConfirm != null)
							{
								selectedCluster.OnConfirm(GameManager.PlayerId);
							}
							confirmed = true;
							KUtility.DelayedCall(this, delegate
							{
								if (GameManager.Emigrated != GameManager.EmigratedType.None)
								{
									int randomMemo = MemoSystem.GetRandomMemo(MemoType.Tooltip);
									string text = ((randomMemo != -1) ? MemoSystem.GetMemoText(MemoType.Tooltip, randomMemo) : string.Empty);
									UserControl.SetExplainLabel(text, important: true);
								}
								CurState = State.Knock;
							}, (selectedCluster.Mode != Mode.Online) ? 1f : (-1f));
						}
					}
				};
				Action onPlayerSelection = delegate
				{
					CurState = State.SelectPlayer;
				};
				if (IsLoginProcess)
				{
					UserControl.ShowCluster(action, onPlayerSelection, delegate
					{
						if (CurState == State.SelectCluster)
						{
							Platform.Instance.Logout(delegate(bool success)
							{
								if (success)
								{
									CurState = State.Initial;
								}
								else
								{
									_errorMsg = T._("로그아웃에 실패했습니다.");
									CurState = State.Error;
								}
							});
						}
					}, _autoSelectCluster);
				}
				else
				{
					action();
				}
				break;
			}
			case State.SelectPlayer:
			{
				Cluster cluster = UserControl.GetSelectedCluster();
				Account account = UserControl.GetSelectedAccount();
				if (account == null)
				{
					state = State.Error;
					break;
				}
				int maxPlayerSlotCount = account.MaxPlayerSlotCount;
				int playerSlotCount = account.PlayerSlotCount;
				TitlePlayerSelectionGroupBase titlePlayerSelectionGroupBase = TitleUIManager.Find<TitlePlayerSelectionGroupBase>();
				Action<PlayerInfo> deleteClicked = null;
				if (cluster.Mode == Mode.Editable)
				{
					deleteClicked = delegate(PlayerInfo info)
					{
						UserControl.ShowMessageBox($"<em>{info.PlayerName}</em> " + T._("캐릭터를 삭제하시겠습니까?"), T._("해당 캐릭터에 속한 창작섬과 건축물, 아이템이 모두 삭제되며, 복구할 수 없습니다.\n\n정말 삭제하시겠습니까?"), delegate
						{
							cluster.OnDeletePlayer(info.PlayerEntityId);
							UserControl.CloseMessageBox();
							UserControl.UpdateServerAndPlayerInfo(forceUpdate: true);
							CurState = State.SelectPlayer;
						}, delegate
						{
							UserControl.CloseMessageBox();
						});
					};
				}
				titlePlayerSelectionGroupBase.Show(account, cluster.GetName(LocalizeSystem.Locale), playerSlotCount, maxPlayerSlotCount, delegate(string id, int slotIdx)
				{
					account.ApplyRecommendedPlayer(new Pair<string, int>(id, slotIdx));
					UserControl.SetContentActive(isActive: true);
					_autoSelectCluster = false;
					CurState = State.SelectCluster;
				}, delegate(int slotIdx)
				{
					account.ApplyRecommendedPlayer(new Pair<string, int>(string.Empty, slotIdx));
					UserControl.SetContentActive(isActive: true);
					_autoSelectCluster = true;
					CurState = State.SelectCluster;
				}, deleteClicked);
				titlePlayerSelectionGroupBase.SetBackButtonEvent(delegate
				{
					UserControl.SetContentActive(isActive: true);
					_autoSelectCluster = false;
					CurState = State.SelectCluster;
				});
				UserControl.SetContentActive(isActive: false);
				break;
			}
			case State.Knock:
			{
				StringBuilder stringBuilder = new StringBuilder();
				stringBuilder.Append("/knock");
				stringBuilder.Append("?version=");
				stringBuilder.Append(WWW.EscapeURL(CurrentBundleVersion.GetClientVersion()));
				stringBuilder.Append("&platform=");
				stringBuilder.Append(WWW.EscapeURL(Platform.Instance.AssetBundlePlatform.ToString()));
				stringBuilder.Append("&bundle_id=");
				stringBuilder.Append(WWW.EscapeURL(Platform.Instance.AppBundleId));
				RequestUrl(stringBuilder.ToString());
				break;
			}
			case State.CheckDataLoaded:
				if (Loader.LoadState != Loader.State.Succees)
				{
					Loader.Load(this);
				}
				break;
			case State.CheckSoundManager:
				Durango.Utils.Singleton<SoundManager>.Instance().Initialize();
				ResourceSingleton<UISpriteManager>.Instance().Load();
				break;
			case State.NPAGetUser:
			{
				Dictionary<string, string> dictionary = Platform.Instance.BuildSessionForm();
				if (GameManager.ConnectCluster != null)
				{
					dictionary.Add("player", GameManager.ConnectCluster.LocalPlayer);
				}
				RequestUrl("/sessions", dictionary, auth: false, HTTPMethods.Post);
				break;
			}
			case State.FadeOutPrologue:
				ActiveFadeOutTweener();
				break;
			case State.PrologueLoading:
				OrientationController.SetOrientation(OrientationController.Orientation.Landscape);
				StartCoroutine(CoLoadingLevel("Prologue"));
				break;
			case State.CheckPrerequsite:
				UserControl.SetExplainLabel(T._("필수 파일을 확인 중입니다."));
				CheckPrerequsite();
				break;
			case State.PostPrerequsite:
				UserControl.SetExplainLabel(T._("필수 파일을 불러오는 중입니다."));
				Durango.Utils.Singleton<AssetBundleManager>.Instance().PrecacheAssets();
				break;
			case State.GetTimedTicketInfo:
			{
				string timedTicketUrl = UserControl.GetSelectedCluster().TimedTicketUrl;
				bool timedTicketInfoRequested = _timedTicketInfoRequested;
				RequestHttpUrl(timedTicketUrl, null, auth: false, HTTPMethods.Get, timedTicketInfoRequested);
				break;
			}
			case State.GetAdmission:
				_timedTicketSkippableClusters.Add(UserControl.GetSelectedClusterKey());
				RequestUrl("/admission", null, auth: true, HTTPMethods.Get, _requestedRetryAdmission);
				break;
			case State.GetFrontend:
				RquestEntry(string.Empty);
				break;
			case State.TryConnect:
				UserControl.SetExplainLabel(T._("서버에 접속 중입니다."));
				state = OnTryConnect();
				break;
			case State.Welcome:
				UserControl.SetExplainLabel(T._("서버와 통신 중입니다."));
				Durango.Utils.Singleton<GameManager>.Instance().SendAuthMessage(delegate
				{
					CurState = State.FadeOutLoading;
				}, delegate(string error)
				{
					Connections.Frontend.Close(callClosedHandler: false);
					_errorMsg = error;
					CurState = State.Error;
				});
				break;
			case State.FadeOutLoading:
				ActiveFadeOutTweener();
				break;
			case State.Loading:
				StartCoroutine(CoLoadingLevel(Platform.Instance.MainSceneName));
				break;
			case State.IdleInHardcapPosition:
				OnErrorState(curState);
				_requestedRetryAdmission = false;
				break;
			case State.IdleInTimedTicketWaiting:
				OnErrorState(curState);
				_timedTicketInfoRequested = false;
				break;
			case State.Error:
				OnErrorState(curState);
				if (!string.IsNullOrEmpty(_errorMsgDetail))
				{
					UserControl.ShowMessageBox(T._("접속 불가"), _errorMsgDetail, delegate
					{
						if (UserControl.QuitWhenErrorOccurred)
						{
							Platform.Instance.Quit();
						}
						else
						{
							UserControl.CloseMessageBox();
						}
					});
					_errorMsgDetail = string.Empty;
				}
				_errorMsg = string.Empty;
				break;
			}
			if (state != curState2)
			{
				CurState = state;
			}
		}
	}

	private TitleLoadingGroup LoadingGroup
	{
		get
		{
			if (_loadingCurtain == null)
			{
				_loadingCurtain = TitleUIManager.Find<TitleLoadingGroup>();
			}
			return _loadingCurtain;
		}
	}

	private static string GetLastErrorMsg(State prevState)
	{
		string arg = T._("게임에 접속할 수 없습니다.\n네트워크 상태를 확인 후 다시 시도해 주세요.");
		if (IsDataLoadState(prevState))
		{
			arg = ManualTranslator.DataLoadErrorAndRetry;
		}
		else if (prevState == State.Connecting)
		{
			string text = T._("서버와의 연결 중 에러가 발생하였습니다.");
			string arg2 = T._("화면을 터치하여 다시 시도해 주세요.");
			string text2 = ((!Platform.Instance.UsePCUI) ? $"{text}\n{arg2}" : text);
			arg = text2;
		}
		return $"{arg} ({prevState})";
	}

	private static bool IsDataLoadState(State state)
	{
		if (state == State.CheckDataLoaded || state == State.CheckSoundManager || state == State.CheckSpriteManager)
		{
			return true;
		}
		return false;
	}

	private State OnTryConnect()
	{
		if (_connectAttempt >= 3)
		{
			return State.Error;
		}
		Durango.Utils.Singleton<GameManager>.Instance().TryConnect();
		_connectAttempt++;
		return State.Connecting;
	}

	public void StartGame()
	{
		base.gameObject.SetActive(value: true);
		SoundManager.SetSfxVolume(SoundManager.VolumeForSfx);
		SoundManager.SetAmbienceVolume(SoundManager.VolumeForAmbience);
		SoundManager.SetMidiVolume(SoundManager.VolumeForMidi);
		SoundManager.SetBgmVolume(SoundManager.VolumeForBgm);
		TitleUIRootResizer.AddOnScreenResized(OnScreenResized);
		if (GameManager.IsPlayerIdSelected)
		{
			LoadingGroup.HideTitleSceneWithCurtain();
		}
		else
		{
			ApplyEmigrationMode();
		}
		if (_fontSetting != null)
		{
			_fontSetting.Init();
		}
		float delay = -1f;
		KUtility.DelayedCall(this, delegate
		{
			CurState = State.Initial;
		}, delay);
	}

	private void ApplyEmigrationMode()
	{
		if (_titleList == null)
		{
			return;
		}
		foreach (TitleOptions title in _titleList)
		{
			GameObject[] objects = title.Objects;
			if (objects != null)
			{
				GameObject[] array = objects;
				foreach (GameObject gameObject in array)
				{
					gameObject.SetActive(value: false);
				}
			}
		}
		int emigrated = (int)GameManager.Emigrated;
		if (_titleList.Count <= emigrated)
		{
			return;
		}
		TitleOptions titleOptions = _titleList[emigrated];
		if (titleOptions.Objects != null)
		{
			GameObject[] objects2 = titleOptions.Objects;
			foreach (GameObject gameObject2 in objects2)
			{
				gameObject2.SetActive(value: true);
			}
		}
		_videoPlayer.Load(titleOptions.VideoName);
		AkAudioListener akAudioListener = UnityEngine.Object.FindObjectOfType<AkAudioListener>();
		if (akAudioListener != null)
		{
			SoundManager.SetListenerObject(akAudioListener.gameObject);
		}
		if (!string.IsNullOrEmpty(titleOptions.SoundEvent.Path))
		{
			SoundManager.IgnorePreparedCheck = true;
			_soundInstanceId = SoundManager.PlayEvent(titleOptions.SoundEvent, SoundPosition.Empty, exclusive: true);
			SoundManager.IgnorePreparedCheck = false;
		}
	}

	private void CheckPrerequsite()
	{
		PrerequisiteLoader loader = LoadingGroup.PrerequisiteLoader;
		LoadingGroup.gameObject.SetActive(value: true);
		loader.TotalCount = Durango.Utils.Singleton<AssetBundleManager>.Instance().PrerequsitesCount;
		Durango.Utils.Singleton<AssetBundleManager>.Instance().StartPrerequisiteLoading(loader.ProgressChanged, loader.DetailedProgressChanged, delegate(bool isSuccessed)
		{
			loader.gameObject.SetActive(value: false);
			if (isSuccessed)
			{
				CurState = State.PostPrerequsite;
			}
			else
			{
				CurState = State.Error;
			}
		}, delegate(int mega, int remainCount)
		{
			loader.TotalCount = remainCount;
			if (remainCount > 0)
			{
				UserControl.SetExplainLabel(GetPrerequsiteDownloadWarningMessage(mega), important: true);
			}
		});
	}

	private void ActiveFadeOutTweener()
	{
		SoundManager.StopEvent(_soundInstanceId, LoadingGroup.Duration);
		_soundInstanceId = 0u;
		if (GameManager.IsPlayerIdSelected)
		{
			FadeOutFinished();
		}
		else
		{
			LoadingGroup.Play(FadeOutFinished);
		}
	}

	private void FadeOutFinished()
	{
		GameManager.IsPlayerIdSelected = false;
		switch (CurState)
		{
		case State.FadeOutPrologue:
			CurState = State.PrologueLoading;
			break;
		case State.FadeOutLoading:
			CurState = State.Loading;
			break;
		}
	}

	private IEnumerator CoLoadingLevel(string level)
	{
		_videoPlayer.Stop();
		yield return new WaitForEndOfFrame();
		_videoPlayer.Destroy();
		SceneManager.LoadSceneAsync(level);
	}

	private void Update()
	{
		ProcessState();
		ProcessResponse();
	}

	private void ProcessState()
	{
		switch (CurState)
		{
		case State.CheckDataLoaded:
			switch (Durango.Utils.Singleton<AssetBundleManager>.Instance().CurrentStatus)
			{
			case AssetBundleManager.Status.Ready:
				if (Loader.LoadState == Loader.State.Succees)
				{
					CurState = State.CheckSoundManager;
				}
				else if (Loader.LoadState == Loader.State.Failure)
				{
					Loader.Stop();
					CurState = State.Error;
				}
				break;
			case AssetBundleManager.Status.Failed:
				CurState = State.Error;
				break;
			}
			break;
		case State.CheckSoundManager:
			switch (Durango.Utils.Singleton<SoundManager>.Instance().BankLoadState)
			{
			case SoundBanksLoader.State.Loaded:
				CurState = State.CheckSpriteManager;
				break;
			case SoundBanksLoader.State.LoadFailed:
				CurState = State.Error;
				break;
			}
			break;
		case State.CheckSpriteManager:
			switch (ResourceSingleton<UISpriteManager>.Instance().LoadingStatus)
			{
			case UISpriteManager.Status.Ready:
				CurState = State.NPAGetUser;
				break;
			case UISpriteManager.Status.Failed:
				CurState = State.Error;
				break;
			}
			break;
		case State.PostPrerequsite:
			if (Durango.Utils.Singleton<AssetBundleManager>.Instance().IsPrecachedAssetsReady())
			{
				if (string.IsNullOrEmpty(UserControl.GetSelectedCluster().TimedTicketUrl) || _timedTicketSkippableClusters.Contains(UserControl.GetSelectedClusterKey()))
				{
					CurState = State.GetAdmission;
					break;
				}
				_timedTicketInfoUpdatedTimeOnArrival = null;
				CurState = State.GetTimedTicketInfo;
			}
			break;
		case State.Connecting:
			if (Connections.Frontend.Connected())
			{
				CurState = State.Welcome;
			}
			else if (!Connections.Frontend.IsAttemptingToConnect())
			{
				CurState = State.TryConnect;
			}
			break;
		case State.Welcome:
			if (!Connections.Frontend.Connected())
			{
				CurState = State.Error;
			}
			break;
		case State.IdleInHardcapPosition:
		{
			float num = Time.realtimeSinceStartup - _lastHardCapRequestTime;
			if (num >= 15f)
			{
				_requestedRetryAdmission = true;
				CurState = State.GetAdmission;
			}
			break;
		}
		case State.IdleInTimedTicketWaiting:
			if (Time.realtimeSinceStartup - _timedTicketLastCheckTime >= 15f)
			{
				_timedTicketInfoRequested = true;
				CurState = State.GetTimedTicketInfo;
			}
			break;
		case State.Error:
		{
			bool flag = ((!Platform.Instance.UsePCUI) ? Input.GetMouseButtonDown(0) : UserControl.RetryConnect);
			UserControl.RetryConnect = false;
			if (!UserControl.IsMessageBoxOpen && flag && _errorFrame != Time.frameCount)
			{
				if (UserControl.QuitWhenErrorOccurred)
				{
					Platform.Instance.Quit();
					break;
				}
				CurState = State.Initial;
				_debugLogLabel.text = string.Empty;
				_debugWarningLabel.text = string.Empty;
				_debugErrorLabel.text = string.Empty;
			}
			break;
		}
		}
	}

	private void ProcessResponse()
	{
		if (_request == null || _request.MoveNext())
		{
			return;
		}
		if (_request.Response != null && _request.Response.IsSuccess)
		{
			string dataAsText = _request.Response.DataAsText;
			_request = null;
			if (WebResponsed != null)
			{
				WebResponsed(arg1: true, CurState, dataAsText);
			}
			OnRequestSucceed(dataAsText);
		}
		else
		{
			CheckError(_request.Response);
			_request = null;
			if (WebResponsed != null)
			{
				WebResponsed(arg1: false, CurState, string.Empty);
			}
			CurState = State.Error;
		}
	}

	private void OnRequestSucceed(string response)
	{
		JObject jObject = Json.Read<JObject>(response);
		if (jObject == null)
		{
			CurState = State.Error;
			return;
		}
		switch (CurState)
		{
		case State.Knock:
		{
			UserControl.UpdateVersionInfo(jObject.Get<string>("server_version"));
			if (!jObject.Get("compatible", defaultVal: false))
			{
				RedirectToDownloadUrl(jObject.Get<string>("download_url"));
				break;
			}
			string urlRoot = jObject.Get<string>("assetbundle_url_root");
			string infoHolderPath = jObject.Get<string>("assetbundle_index_url");
			Durango.Utils.Singleton<AssetBundleManager>.Instance().Initialize(infoHolderPath, urlRoot);
			CurState = State.CheckDataLoaded;
			break;
		}
		case State.NPAGetUser:
		{
			string text3 = jObject.Get<string>("session_token");
			if (string.IsNullOrEmpty(text3))
			{
				CurState = State.Error;
				break;
			}
			GameManager.SessionToken = text3;
			CurState = ((!string.IsNullOrEmpty(GameManager.PlayerId)) ? State.PostPrerequsite : State.FadeOutPrologue);
			break;
		}
		case State.GetTimedTicketInfo:
		case State.IdleInTimedTicketWaiting:
		{
			if (!jObject.Get("activated", defaultVal: false))
			{
				CurState = State.GetAdmission;
				break;
			}
			double result = 0.0;
			double.TryParse(JTokenExtensions.Get(jObject, "enterable_before", "0"), out result);
			DateTime dateTime = Times.UnixTimeToDateTimeUtc(result);
			double result2 = 0.0;
			double.TryParse(JTokenExtensions.Get(jObject, "info_updated_at", "0"), out result2);
			DateTime dateTime2 = Times.UnixTimeToDateTimeUtc(result2);
			if (!_timedTicketInfoUpdatedTimeOnArrival.HasValue)
			{
				_timedTicketInfoUpdatedTimeOnArrival = dateTime2;
				_timedTicketClientUtcTimeOnArrival = DateTime.UtcNow;
			}
			bool flag = _timedTicketInfoUpdatedTimeOnArrival.Value <= dateTime;
			TimeSpan value2 = DateTime.UtcNow - _timedTicketClientUtcTimeOnArrival;
			DateTime dateTime3 = _timedTicketInfoUpdatedTimeOnArrival.Value.Add(value2);
			if ((dateTime3 - dateTime2).TotalSeconds > 180.0)
			{
				flag = true;
			}
			if (flag)
			{
				_timedTicketSkippableClusters.Add(UserControl.GetSelectedClusterKey());
				CurState = State.NPAGetUser;
				break;
			}
			_timedTicketLastCheckTime = Time.realtimeSinceStartup;
			double num2 = (_timedTicketInfoUpdatedTimeOnArrival.Value - dateTime).TotalSeconds;
			if (num2 < 0.0)
			{
				num2 = 0.0;
			}
			float result3 = 0f;
			float.TryParse(JTokenExtensions.Get(jObject, "estimated_position_per_seconds", "0"), out result3);
			float result4 = 0f;
			float.TryParse(JTokenExtensions.Get(jObject, "estimated_duration_per_seconds", "0"), out result4);
			int position = (int)((double)result3 * num2);
			float estimatedWaitingTime = (float)((double)result4 * num2);
			_errorMsg = MakeTimedTicketMessage(position, estimatedWaitingTime);
			CurState = State.IdleInTimedTicketWaiting;
			break;
		}
		case State.GetAdmission:
		case State.IdleInHardcapPosition:
		{
			if (jObject.Get("admitted", defaultVal: false))
			{
				CurState = State.GetFrontend;
				break;
			}
			int num3 = JTokenExtensions.Get(jObject, "position", -1);
			if (num3 >= 0)
			{
				float realtimeSinceStartup = Time.realtimeSinceStartup;
				float duration = realtimeSinceStartup - _lastHardCapRequestTime;
				Cluster selectedCluster = UserControl.GetSelectedCluster();
				_errorMsg = ((!string.IsNullOrEmpty(selectedCluster.HardCap)) ? selectedCluster.HardCap : MakeHardcapMessage(num3, _lastHardcapPosition, duration));
				_lastHardcapPosition = num3;
				_lastHardCapRequestTime = realtimeSinceStartup;
				CurState = State.IdleInHardcapPosition;
			}
			else
			{
				string text4 = T._("현재 게임 서버가 혼잡하여 접속이 불가능합니다.\n잠시 후 다시 이용해 주시길 바랍니다.");
				string arg = T._("확인 버튼을 터치하면 게임이 종료됩니다.");
				string errorMsgDetail = ((!Platform.Instance.UsePCUI) ? $"{text4}\n{arg}" : text4);
				_errorMsgDetail = errorMsgDetail;
				UserControl.QuitWhenErrorOccurred = true;
				CurState = State.Error;
			}
			break;
		}
		case State.GetFrontend:
		{
			string text = jObject.Get<string>("dispatch_to");
			if (text != null)
			{
				Uri uri = new Uri(text);
				string gatewayUrl = $"{uri.Scheme}://{uri.Authority}";
				RquestEntry(gatewayUrl);
				break;
			}
			JArray addresses = jObject.Get("frontend_addresses") as JArray;
			List<KeyValuePair<string, int>> list = ParseAddresses(addresses);
			if (KUtility.GetSize(list) == 0)
			{
				CurState = State.Error;
				break;
			}
			if (GameManager.ConnectCluster != null)
			{
				string text2 = GameManager.ConnectCluster.GatewayUrlRoot;
				if (text2.StartsWith("http://"))
				{
					text2 = text2.Substring(7);
				}
				int num = text2.LastIndexOf(":");
				if (num != -1)
				{
					text2 = text2.Substring(0, num);
				}
				int value = list[0].Value;
				list[0] = new KeyValuePair<string, int>(text2, value);
				string source = jObject.Get<string>("cluster_mode");
				GameManager.SetCluster(GameManager.ClusterKey, GameManager.GatewayUrl, source.ToEnum(Mode.Offline));
				GameManager.ConnectCluster = null;
			}
			JArray addresses2 = jObject.Get("radiotower_addresses") as JArray;
			List<KeyValuePair<string, int>> endpoints = ParseAddresses(addresses2);
			Durango.Utils.Singleton<GameManager>.Instance().SetEndpoints(list);
			GameSystem<SocialSystem>.Instance().SetEndpoints(endpoints);
			_connectAttempt = 0;
			CurState = State.TryConnect;
			break;
		}
		}
	}

	/// <summary>
	/// [เพิ่มเอง 5 ก.ย. 2026] อ่าน clusters.json ที่วางไว้ข้างตัวเกม (ถ้ามี)
	/// ตำแหน่งไฟล์ใช้กติกาเดียวกับ Durango.Utils.AppData.BasePath ของต้นฉบับ:
	///   PC     — โฟลเดอร์เดียวกับ Durango.exe  (Path.GetDirectoryName(Application.dataPath))
	///   มือถือ — Application.persistentDataPath
	/// คืน null เมื่อไม่มีไฟล์/อ่านไม่ได้ ⇒ ผู้เรียกใช้ TextAsset ในเกมต่อเหมือนเดิม
	/// </summary>
	private static string ReadLocalClusterJson()
	{
		try
		{
			string dir = ((Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer)
				? Application.persistentDataPath
				: global::System.IO.Path.GetDirectoryName(Application.dataPath));
			if (string.IsNullOrEmpty(dir))
			{
				return null;
			}
			string path = global::System.IO.Path.Combine(dir, "clusters.json");
			if (!global::System.IO.File.Exists(path))
			{
				return null;
			}
			string text = global::System.IO.File.ReadAllText(path);
			UnityEngine.Debug.Log("[durango] ใช้ cluster จากไฟล์ " + path);
			return text;
		}
		catch (Exception ex)
		{
			UnityEngine.Debug.LogWarning("[durango] อ่าน clusters.json ไม่ได้: " + ex.Message);
			return null;
		}
	}

	private static List<KeyValuePair<string, int>> ParseAddresses(JArray addresses)
	{
		if (addresses == null || addresses.Count == 0)
		{
			return null;
		}
		List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>();
		foreach (JToken item in (IEnumerable<JToken>)addresses)
		{
			string address = item.GetString();
			if (Connection.TryParse(address, out var host, out var port))
			{
				list.Add(new KeyValuePair<string, int>(host, port));
			}
		}
		return list;
	}

	private string MakeTimedTicketMessage(int position, float estimatedWaitingTime)
	{
		string text = T._("현재 {0}명이 접속 대기 중입니다.", position);
		string text2 = ((!(estimatedWaitingTime > 3600f)) ? TimedeltaFormatter.Format(estimatedWaitingTime) : T._("{0} 이상", TimedeltaFormatter.Format(3600.0)));
		return text + T._("\n예상 대기 시간은 {0} 입니다.", text2);
	}

	private string MakeHardcapMessage(int position, int lastPosition, float duration)
	{
		string text = T._("현재 {0}명이 접속 대기 중입니다.", position);
		if (position >= lastPosition && _lastHardcapDelta <= 0f)
		{
			return text;
		}
		float num = _lastHardcapDelta;
		int num2 = lastPosition - position;
		if (num2 > 0)
		{
			num = ((!(duration > 0f)) ? 15f : duration) / (float)num2;
		}
		if (_lastHardcapDelta > 0f)
		{
			num = (num + _lastHardcapDelta) * 0.5f;
		}
		_lastHardcapDelta = num;
		float num3 = num * (float)position;
		string text2 = ((!(num3 > 3600f)) ? TimedeltaFormatter.Format(num3) : T._("{0} 이상", TimedeltaFormatter.Format(3600.0)));
		return text + T._("\n예상 대기 시간은 {0} 입니다.", text2);
	}

	private void CheckError(HTTPResponse response)
	{
		if (response != null)
		{
			string message = response.Message;
			JObject jObject = Json.Read<JObject>(response.DataAsText);
			JObject jObject2 = ((jObject == null) ? null : (jObject.Get("error") as JObject));
			if (jObject2 != null)
			{
				_errorMsgDetail = jObject2.Get<string>("message");
			}
			else
			{
				_errorMsgDetail = string.Format("[{0}] {1}\n{2}", response.StatusCode, response.Message, (!Debug.isDebugBuild) ? T._("로그인에 실패했습니다. 잠시 후 다시 시도해 주세요.") : response.DataAsText);
			}
		}
	}

	private void RequestHttpUrl(string url, Dictionary<string, string> fields = null, bool auth = false, HTTPMethods method = HTTPMethods.Get, bool skipExplainLabel = false)
	{
		_request = Http.Request(url, null, disableCache: true, auth, fields, method);
		if (!skipExplainLabel)
		{
			UserControl.SetExplainLabel(T._("서버와 통신 중입니다."));
		}
	}

	private void RequestUrl(string postFix, Dictionary<string, string> fields = null, bool auth = false, HTTPMethods method = HTTPMethods.Get, bool skipExplainLabel = false)
	{
		string url = GameManager.GatewayUrl + postFix;
		RequestHttpUrl(url, fields, auth, method, skipExplainLabel);
	}

	private void RquestEntry(string gatewayUrl = "")
	{
		if (string.IsNullOrEmpty(gatewayUrl))
		{
			gatewayUrl = GameManager.GatewayUrl;
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(gatewayUrl);
		stringBuilder.Append("/entry");
		stringBuilder.Append("?entity_id=");
		stringBuilder.Append(WWW.EscapeURL(GameManager.PlayerId));
		stringBuilder.Append("&platform=");
		stringBuilder.Append(WWW.EscapeURL(Platform.Instance.AssetBundlePlatform.ToString()));
		RequestHttpUrl(stringBuilder.ToString(), null, auth: true);
	}

	[Conditional("DEBUG_LEVEL_LOG")]
	private void Log(string text)
	{
		_debugLogLabel.text = text;
	}

	[Conditional("DEBUG_LEVEL_LOG")]
	[Conditional("DEBUG_LEVEL_WARN")]
	private void LogWarning(string text)
	{
		_debugWarningLabel.text = text;
	}

	private void LogError(string text)
	{
		if (Debug.isDebugBuild)
		{
			_debugErrorLabel.text = text;
		}
		Debug.LogError(text);
	}

	public static string GetPrerequsiteDownloadWarningMessage(int mega)
	{
		if (Platform.Instance.UsePCUI)
		{
			return T._("게임 플레이를 위해 추가 다운로드가 필요합니다. ({0}MB)", mega);
		}
		return T._("게임 플레이를 위해 추가 다운로드가 필요합니다.\nWi-Fi 사용을 권장합니다. ({0}MB)", mega);
	}

	private void OnErrorState(State prevState)
	{
		if (GameManager.IsPlayerIdSelected)
		{
			ApplyEmigrationMode();
		}
		GameManager.IsPlayerIdSelected = false;
		GameManager.Emigrated = GameManager.EmigratedType.None;
		GameManager.ConnectCluster = null;
		LoadingGroup.LoadingCurtain.SetActive(value: false);
		if (string.IsNullOrEmpty(_errorMsg))
		{
			_errorMsg = GetLastErrorMsg(prevState);
		}
		UserControl.SetExplainLabel(_errorMsg, important: true);
		_errorFrame = Time.frameCount;
	}

	private void OnScreenResized()
	{
		UpdateVideoLayout(TitleUIRootResizer.IsPortrait);
	}

	private void UpdateVideoLayout(bool isPortrait)
	{
		Platform.Instance.GetScreenResolution(isPortrait, out var width, out var height);
		float num = (float)width / (float)height;
		float num2 = (float)Screen.width / (float)Screen.height;
		if (isPortrait)
		{
			num = 1f / num;
			num2 = 1f / num2;
		}
		if (isPortrait)
		{
			float num3 = 1f;
			if (num < num2)
			{
				num3 = num2 / num;
			}
			_videoWidget.width = (int)((float)height * num * num3);
			_videoWidget.height = (int)((float)height * num3);
		}
		else
		{
			float num4 = 1f;
			if (num < num2)
			{
				num4 = num2 / num;
			}
			else if (num > num2)
			{
				num4 = num / num2;
			}
			_videoWidget.width = (int)((float)width * num4);
			_videoWidget.height = (int)((float)height * num4);
		}
		if (GameManager.Emigrated == GameManager.EmigratedType.None && isPortrait)
		{
			float x = (float)(_videoWidget.width - TitleUIRootResizer.ScreenWidth) * 0.5f;
			_videoWidget.transform.localPosition = new Vector3(x, 0f, 0f);
		}
		else
		{
			_videoWidget.transform.localPosition = Vector3.zero;
		}
	}

	protected virtual void RedirectToDownloadUrl(string downloadUrl)
	{
		if (downloadUrl != null)
		{
			UserControl.ShowMessageBox(T._("업데이트"), T._("새 버전 업데이트를 위해 다운로드 페이지로 이동합니다."), delegate
			{
				Application.OpenURL(downloadUrl);
			});
		}
		else
		{
			LogError("No download url");
			CurState = State.Error;
		}
	}
}
