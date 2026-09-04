using System;
using System.Collections;
using System.IO;
using Durango.Utils;
using UnityEngine;

public class ScreenshotManager : MonoBehaviour
{
	private enum ImageType
	{
		IMAGE,
		SCREENSHOT
	}

	public static Action<string> OnScreenshotSaved;

	private static ScreenshotManager instance;

	private static GameObject go;

	public static ScreenshotManager Instance
	{
		get
		{
			if (instance == null)
			{
				go = new GameObject();
				go.name = "ScreenshotManager";
				instance = go.AddComponent<ScreenshotManager>();
			}
			return instance;
		}
	}

	public static event Action<Texture2D> OnScreenshotTaken;

	public static event Action<string> OnImageSaved;

	private void Awake()
	{
		if (instance != null && instance != this)
		{
			UnityEngine.Object.Destroy(base.gameObject);
		}
	}

	private static string GetPath(string fileName, string albumName)
	{
		if (string.IsNullOrEmpty(fileName))
		{
			fileName = DateTime.Now.ToString("yyyy-MM-dd hh-mm-ss");
		}
		string result = null;
		if (Application.isEditor)
		{
			result = $"{albumName}/{fileName}";
		}
		else if (Application.platform != RuntimePlatform.Android)
		{
			result = AppData.CombinePath(fileName);
		}
		return result;
	}

	public static void SaveScreenshot(string fileName = null, string albumName = "Screenshots", string fileType = "jpeg", Rect screenArea = default(Rect))
	{
		if (screenArea == default(Rect))
		{
			screenArea = new Rect(0f, 0f, Screen.width, Screen.height);
		}
		Instance.StartCoroutine(Instance.GrabScreenshot(fileName, albumName, fileType, screenArea));
	}

	private IEnumerator GrabScreenshot(string fileName, string albumName, string fileType, Rect screenArea)
	{
		yield return new WaitForEndOfFrame();
		Texture2D texture = new Texture2D((int)screenArea.width, (int)screenArea.height, TextureFormat.RGB24, mipmap: false);
		texture.ReadPixels(screenArea, 0, 0);
		texture.Apply();
		byte[] bytes;
		string fileExt;
		if (fileType == "png")
		{
			bytes = texture.EncodeToPNG();
			fileExt = ".png";
		}
		else
		{
			bytes = texture.EncodeToJPG();
			fileExt = ".jpeg";
		}
		if (OnScreenshotTaken != null)
		{
			OnScreenshotTaken(texture);
		}
		else
		{
			UnityEngine.Object.Destroy(texture);
		}
		string path = GetPath(fileName, albumName) + fileExt;
		Instance.StartCoroutine(Instance.Save(bytes, path, ImageType.SCREENSHOT));
	}

	public static void SaveImage(Texture2D texture, string fileName = null, string albumName = "Screenshots", string fileType = "png", int quality = 90)
	{
		Instance.Awake();
		byte[] bytes;
		string text;
		if (fileType == "png")
		{
			bytes = texture.EncodeToPNG();
			text = ".png";
		}
		else
		{
			bytes = texture.EncodeToJPG(quality);
			text = ".jpeg";
		}
		string path = GetPath(fileName, albumName) + text;
		Instance.StartCoroutine(Instance.Save(bytes, path, ImageType.IMAGE));
	}

	public static void SaveImage(MemoryStream memoryStream, string fileName = null, string albumName = "Screenshots", string fileExt = ".jpeg")
	{
		Instance.Awake();
		string path = GetPath(fileName, albumName) + fileExt;
		Instance.StartCoroutine(Instance.Save(memoryStream, path, ImageType.IMAGE));
	}

	private IEnumerator Save(byte[] bytes, string path, ImageType imageType)
	{
		bool saved = false;
		if (Application.platform != RuntimePlatform.IPhonePlayer && Application.platform != RuntimePlatform.Android)
		{
			using (FileStream fileStream = AppData.OpenFile(path, FileMode.Create))
			{
				fileStream?.Write(bytes, 0, bytes.Length);
			}
			yield return Instance.StartCoroutine(Instance.Wait(0.5f));
		}
		switch (imageType)
		{
		case ImageType.IMAGE:
			if (OnImageSaved != null)
			{
				OnImageSaved(path);
			}
			break;
		case ImageType.SCREENSHOT:
			if (OnScreenshotSaved != null)
			{
				OnScreenshotSaved(path);
			}
			break;
		}
	}

	private IEnumerator Save(MemoryStream memoryStream, string path, ImageType imageType)
	{
		bool saved = false;
		if (Application.isEditor)
		{
			using (FileStream fileStream = AppData.OpenFile(path, FileMode.Create))
			{
				if (fileStream != null)
				{
					memoryStream.WriteTo(fileStream);
				}
			}
			yield return Instance.StartCoroutine(Instance.Wait(0.5f));
		}
		else if (Application.platform != RuntimePlatform.IPhonePlayer && Application.platform != RuntimePlatform.Android)
		{
			while (!saved)
			{
				yield return null;
				saved = true;
			}
		}
		switch (imageType)
		{
		case ImageType.IMAGE:
			if (OnImageSaved != null)
			{
				OnImageSaved(path);
			}
			break;
		case ImageType.SCREENSHOT:
			if (OnScreenshotSaved != null)
			{
				OnScreenshotSaved(path);
			}
			break;
		}
	}

	private IEnumerator Wait(float delay)
	{
		float pauseTarget = Time.realtimeSinceStartup + delay;
		while (Time.realtimeSinceStartup < pauseTarget)
		{
			yield return null;
		}
	}
}
