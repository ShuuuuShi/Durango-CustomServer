using Durango.Logic.Clusters;
using Durango.Utils.Extensions;
using UnityEngine;

namespace Durango.System.Config;

public class Setting
{
	public string Key;

	public SettingType Type;

	public bool DebugBuild;

	public bool HideOnRelease;

	public bool HideOnPrologue;

	public bool HideOnOffline;

	public RuntimePlatform[] Platform;

	public NPCountry[] Countries;

	public string[] Locales;

	public static bool IsHidden(Setting op)
	{
		if (op.HideOnOffline && GameManager.ClusterMode != Mode.Online)
		{
			return true;
		}
		if (op.HideOnRelease && !Debug.isDebugBuild)
		{
			return true;
		}
		if (op.HideOnPrologue && GameManager.IsPrologueMode)
		{
			return true;
		}
		if (KUtility.GetSize(op.Platform) > 0 && !op.Platform.Contains(Application.platform))
		{
			return true;
		}
		if (KUtility.GetSize(op.Countries) > 0)
		{
			NPCountry country = Durango.System.Platform.Instance.Country;
			if (!op.Countries.Contains(country))
			{
				return true;
			}
		}
		if (KUtility.GetSize(op.Locales) > 0 && !op.Locales.ContainsIgnoreCase(LocalizeSystem.Locale))
		{
			return true;
		}
		return false;
	}
}
