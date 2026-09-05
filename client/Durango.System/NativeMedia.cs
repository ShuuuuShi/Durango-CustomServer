using System;
using UnityEngine;

namespace Durango.System;

/// <summary>
/// [เพิ่มเอง 6 ก.ย. 2026] ตัวกันพังสำหรับ "ปลั๊กอินเนทีฟที่มีเฉพาะชุด PC"
///
/// ═══ ปัญหาที่เจอจริงบน Android ═══
/// ชุด PC มีปลั๊กอินเนทีฟ 14 ตัวใน <c>Durango_Data/Plugins/</c> แต่ APK มีแค่ 6 ตัว
/// (<c>libAkSoundEngine</c> · <c>libBlueDoveMediaRender</c> · <c>libmain</c> · <c>libmono</c> ·
///  <c>libsnappy</c> · <c>libunity</c>) — **ชุดเล่นวิดีโอไม่มีเลย**:
/// <c>EasyMovieTexture</c> · <c>avformat-57</c> · <c>avcodec-57</c> · <c>avutil-55</c> …
///
/// <c>MediaPlayerCtrl</c> เรียกปลั๊กอินพวกนี้ตรง ๆ ⇒ บน Android โยน
/// <c>DllNotFoundException: avformat-57</c> **ทุกครั้ง**
///
/// ⚠️ ที่ร้ายคือมันไม่ได้พังแค่วิดีโอ — <c>TitleMenuGroup.ApplyEmigrationMode</c> เรียก
/// <c>_videoPlayer.Load()</c> อยู่กลางเมธอด พอโยนออกมาแล้วโค้ดที่เหลือ**ไม่ได้ทำงานเลย**
/// ไล่ขึ้นไปถึง <c>StartGame()</c> ⇒ <c>_fontSetting.Init()</c> ไม่ถูกเรียก ⇒ ฟอนต์ไม่ถูกตั้ง
/// ⇒ **ตัวหนังสือบนหน้าไตเติลกลายเป็นกล่องขาวทั้งหน้า** (เห็นในภาพจาก MuMu)
/// และ <c>CurState = State.Initial</c> ไม่ถูกตั้ง ⇒ เมนูไม่เริ่มทำงาน
///
/// ⇒ ห่อทุกจุดที่แตะ <c>MediaPlayerCtrl</c> ด้วย <see cref="Try"/> — ไม่มีวิดีโอก็ข้ามไป
/// ส่วนที่เหลือของเมธอดต้องได้ทำงานต่อเสมอ
/// </summary>
public static class NativeMedia
{
	private static bool? _supported;

	private static bool _warned;

	/// <summary>
	/// แพลตฟอร์มนี้มีปลั๊กอินวิดีโอไหม
	///
	/// เช็คจากแพลตฟอร์มก่อน (Android/iOS = ไม่มีแน่นอน เพราะไม่ได้แพ็ก .so มาด้วย)
	/// แล้วยัง**ปิดตัวเองอัตโนมัติ**ถ้าเจอ <c>DllNotFoundException</c> จริง — เผื่อชุด PC
	/// บางเครื่องที่ปลั๊กอินหายไป จะได้ไม่โยนซ้ำทุกเฟรม
	/// </summary>
	public static bool Supported
	{
		get
		{
			if (!_supported.HasValue)
			{
				RuntimePlatform p = Application.platform;
				_supported = p != RuntimePlatform.Android && p != RuntimePlatform.IPhonePlayer;
			}
			return _supported.Value;
		}
	}

	/// <summary>
	/// เรียกโค้ดที่ใช้ปลั๊กอินวิดีโอแบบไม่ทำให้เมธอดที่เรียกมันตายทั้งเมธอด
	///
	/// <paramref name="what" /> คือคำอธิบายไทยสั้น ๆ ไว้ขึ้น log ตอนข้าม (ขึ้นครั้งเดียว)
    /// </summary>
	public static void Try(string what, Action body)
	{
		if (body == null)
		{
			return;
		}
		if (!Supported)
		{
			Warn(what);
			return;
		}
		try
		{
			body();
		}
		catch (DllNotFoundException)
		{
			// ปลั๊กอินหายจริง ๆ — ปิดถาวรไม่ให้ลองซ้ำทุกเฟรม
			_supported = false;
			Warn(what);
		}
		catch (EntryPointNotFoundException)
		{
			_supported = false;
			Warn(what);
		}
	}

	private static void Warn(string what)
	{
		if (!_warned)
		{
			_warned = true;
			Debug.Log("[วิดีโอ] แพลตฟอร์มนี้ไม่มีปลั๊กอินเล่นวิดีโอ — ข้าม: " + what);
		}
	}
}
