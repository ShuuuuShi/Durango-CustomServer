using System;
using UnityEngine;

namespace Durango.AssetTest_PC;

[Serializable]
public struct SimpleBloom
{
	public float Intensity;

	public float Threshold;

	public static SimpleBloom Lerp(SimpleBloom a, SimpleBloom b, float t)
	{
		return new SimpleBloom
		{
			Intensity = Mathf.Lerp(a.Intensity, b.Intensity, t),
			Threshold = Mathf.Lerp(a.Threshold, b.Threshold, t)
		};
	}
}
