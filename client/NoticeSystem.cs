using System;
using Durango.Logic.Notification;
using Durango.System;
using Durango.Utils;
using L10N;
using Newtonsoft.Json;
using Shared.Region;

public class NoticeSystem : GameSystem<NoticeSystem>, INotificationable
{
	public enum NoticeState
	{
		None,
		Read,
		New
	}

	private struct Notice
	{
		[JsonProperty(PropertyName = "url")]
		public string Url;
	}

	private const string LastReadNoticeKey = "last_read_notice";

	private readonly Notification _notification = new Toggle(Durango.Logic.Notification.Type.Normal);

	private NoticeState _state;

	private Notice _notice;

	public NoticeState State
	{
		get
		{
			return _state;
		}
		set
		{
			if (_state != value)
			{
				_state = value;
				OnStateUpdated();
			}
		}
	}

	public Notification Notification => _notification;

	public event Action StateUpdated;

	private void Start()
	{
		Singleton<GameManager>.Instance().MainSceneLoaded += delegate
		{
			Role role = GameManager.Region.Role();
			if (role != Role.Invalid && role != Role.Tutorial)
			{
				string url = GameManager.GatewayUrl + "/notice";
				Http.RequestYml<Notice>(url, UpdateNotice, disableCache: true);
			}
		};
	}

	public void Show()
	{
		Platform.Instance.ShowNotice();
		SetRead();
	}

	public void ShowLastest()
	{
		if (State != NoticeState.None)
		{
			Platform.Instance.ShowWeb(T._("공지"), _notice.Url);
			SetRead();
		}
	}

	private void OnStateUpdated()
	{
		_notification.On = State == NoticeState.New;
		if (StateUpdated != null)
		{
			StateUpdated();
		}
	}

	private void UpdateNotice(Notice notice)
	{
		NoticeState state = ((!string.IsNullOrEmpty(notice.Url)) ? (IsReadNotic(notice) ? NoticeState.Read : NoticeState.New) : NoticeState.None);
		_notice = notice;
		State = state;
	}

	private void SetRead()
	{
		if (State == NoticeState.New)
		{
			State = NoticeState.Read;
			Preferences.SetString("last_read_notice", _notice.Url);
		}
	}

	private static bool IsReadNotic(Notice notice)
	{
		string text = Preferences.GetString("last_read_notice", string.Empty);
		return text == notice.Url;
	}

	[ExposedInEditor(null)]
	private void UpdateNotice(string url)
	{
		UpdateNotice(new Notice
		{
			Url = url
		});
	}
}
