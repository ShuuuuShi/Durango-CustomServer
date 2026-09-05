using System.Collections.Generic;
using Durango.Utils;
using Yaml.Util;

namespace Yaml;

// พอร์ตจาก nexonSRC/Yaml — กลุ่ม data class ของ item prototype
// PrototypeYaml = SingletonDict<string, List<Prototype>> โหลดจาก /assets/item/prototype_data
public class PrototypeYaml : SingletonDict<string, List<Prototype>>
{
    public static Prototype GetItemPrototype(string prototypeId, int level)
    {
        List<Prototype> list = SingletonDict<string, List<Prototype>>.Get(prototypeId);
        if (list == null) return null;
        foreach (Prototype p in list)
        {
            if (p.MinLevel <= level && level <= p.MaxLevel) return p;
        }
        return null;
    }

    public static Prototype GetItemPrototype(string prototypeId)
    {
        List<Prototype> list = SingletonDict<string, List<Prototype>>.Get(prototypeId);
        if (list == null || list.Count == 0) return null;
        return list[0];
    }
}
