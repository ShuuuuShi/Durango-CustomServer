using UnityEngine;

public class MediaPlayer2UITexture : MonoBehaviour
{
	[SerializeField]
	private MediaPlayerCtrl _mediaPlayer;

	[SerializeField]
	private UITexture _texture;

	private void Start()
	{
		// มือถือ/โหมด UI มือถืออาจถอด MediaPlayerCtrl — อย่าแตะ null
		if (_mediaPlayer != null)
		{
			_mediaPlayer.OnVideoTextureUpdated = MediaPlayer_VideoTextureUpdated;
		}
		if (Application.isEditor || Application.platform == RuntimePlatform.IPhonePlayer || Application.platform == RuntimePlatform.WindowsPlayer)
		{
			if (_texture != null)
			{
				_texture.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
			}
		}
	}

	private void OnEnable()
	{
		// โหมด UI มือถือใช้ SetStill ใส่ภาพนิ่ง — อย่าเคลียร์ texture ทิ้งทุกครั้งที่ enable
		if (!Durango.System.Platform.Instance.UsePCUI || Application.isMobilePlatform)
		{
			return;
		}
		if (_texture != null)
		{
			_texture.mainTexture = null;
		}
	}

	/// <summary>ใส่ภาพนิ่งแทนเฟรมวิดีโอ (ดู TitleMenuGroup.ShowMobileStillBackground)</summary>
	public void SetStill(Texture still)
	{
		if (_texture != null)
		{
			_texture.mainTexture = still;
			_texture.transform.localRotation = Quaternion.identity;
		}
	}

	private void MediaPlayer_VideoTextureUpdated(Texture videoTexture)
	{
		if (_texture != null)
		{
			_texture.mainTexture = videoTexture;
		}
	}
}
