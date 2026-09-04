using Newtonsoft.Json;

namespace Durango.Online;

public class Crop
{
	[JsonProperty(PropertyName = "grown_looks")]
	public string[] GrownLooks;
}
