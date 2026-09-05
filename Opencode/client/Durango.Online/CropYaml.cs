using System.Collections.Generic;
using System.Linq;
using Durango.Utils;

namespace Durango.Online;

public static class CropYaml
{
	private static Dictionary<string, Dictionary<object, Crop>> _crops;

	public static Crop Get(string prototypeId)
	{
		if (_crops == null)
		{
			_crops = Json.ReadFromFile<Dictionary<string, Dictionary<object, Crop>>>("offline/assets/crops");
		}
		return _crops.Get(prototypeId)?.Select((KeyValuePair<object, Crop> crop) => crop.Value).FirstOrDefault();
	}
}
