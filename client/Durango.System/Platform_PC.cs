using Durango.UI;
using UnityEngine;

namespace Durango.System;

public class Platform_PC : Platform
{
	public override bool IsPCStore => true;

	public override string AppBundleId => "com.nexon.durango.wildlands";

	public override bool IsLoginTypeGuest => false;

	public override bool IsConnectFacebook => false;

	public override bool IsConnectGooglePlus => false;

	public override bool IsAvailableOfferwall => false;

	public override bool UsePCUI => true;

	public override int DefaultUISize => 1280;

	public override bool UsePCRenderer => true;

	public override bool SupportPortrait => false;

	public override int DefaultRenderTargetSize => 1024;

	public override string PrologueMovieUrl => "https://d1skbslnewf3os.cloudfront.net/prologue_movie_pc.mp4";

	public override bool GetScreenResolution(bool isPortrait, out int width, out int height)
	{
		Point2 screenResolution = GetScreenResolution();
		width = screenResolution.x;
		height = screenResolution.y;
		return true;
	}

	public static Point2 GetScreenResolution()
	{
		Point2 result = new Point2
		{
			x = (int)((float)Screen.width * 96f / Screen.dpi),
			y = (int)((float)Screen.height * 96f / Screen.dpi)
		};
		int num = Mathf.Max((int)((float)(result.x * UIManager.UISize) / 1280f), 1600);
		int y = Mathf.RoundToInt((float)num * UIAnchorPolicy.DefaultAspectRatio);
		result.x = num;
		result.y = y;
		return result;
	}
}
