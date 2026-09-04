using System.Collections.Generic;
using Durango.Utils;
using Durango.Utils.Extensions;
using Newtonsoft.Json;

namespace Durango.Online;

// พอร์ตจาก nexonSRC/Durango.Online/CropYaml.cs + Crop.cs
// ต้นฉบับอ่าน Resources "offline/assets/crops" — ที่นี่ Json.ReadFromFile อ่าน data/assets/crops.json
public class Crop
{
    [JsonProperty("grown_looks")]
    public string[] GrownLooks;
}

public static class CropYaml
{
    private static Dictionary<string, Dictionary<object, Crop>> _crops;

    public static Crop Get(string prototypeId)
    {
        if (_crops == null)
        {
            _crops = Json.ReadFromFile<Dictionary<string, Dictionary<object, Crop>>>("offline/assets/crops");
        }
        return _crops?.Get(prototypeId)
            ?.Select(pair => pair.Value)
            .FirstOrDefault();
    }
}
