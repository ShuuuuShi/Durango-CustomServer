using System;
using System.Collections.Generic;

namespace Yaml.Util;

// พอร์ตจาก nexonSRC/Yaml.Util/Singleton.cs + SingletonDict.cs + ISingletonable.cs
// ต้นฉบับให้ Loader ของ client เติมข้อมูลตอนโหลด /assets — เซิร์ฟเราเติมเองใน DataStore

public interface ISingletonable
{
    void Initialize(object inst);
}

public class Singleton<T> : ISingletonable where T : class
{
    public static T Instance { get; private set; }

    public void Initialize(object inst)
    {
        Instance = inst as T;
    }
}

public class SingletonDict<TK, TV> : Dictionary<TK, TV>, ISingletonable
{
    public static Dictionary<TK, TV> Instance { get; private set; }

    public void Initialize(object inst)
    {
        Instance = inst as Dictionary<TK, TV>;
    }

    public static TV Get(TK key, TV defaultValue = default)
    {
        if (Instance == null || key == null) return defaultValue;
        return Instance.TryGetValue(key, out var v) ? v : defaultValue;
    }

    public new static bool TryGetValue(TK key, out TV value)
    {
        if (Instance == null)
        {
            value = default;
            return false;
        }
        return Instance.TryGetValue(key, out value);
    }
}
