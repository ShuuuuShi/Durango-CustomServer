using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Durango.Utils;

// พอร์ตจาก nexonSRC/Durango.Utils/Enums.cs
public static class Enums<T> where T : struct, IComparable, IFormattable, IConvertible
{
    [StructLayout(LayoutKind.Explicit)]
    private struct EnumUnion32
    {
        [FieldOffset(0)] public T Enum;
        [FieldOffset(0)] public int Int;
    }

    private static T[] _values;
    private static T[] _greaterValues;
    private static int _greaterInt;

    public static int ToInt(T e)
    {
        var u = new EnumUnion32 { Enum = e };
        return u.Int;
    }

    public static T ToEnum(int value)
    {
        var u = new EnumUnion32 { Int = value };
        return u.Enum;
    }

    public static T[] All()
    {
        if (_values != null) return _values;
        return _values = (T[])Enum.GetValues(typeof(T));
    }

    public static T[] Greater(T greater)
    {
        int g = ToInt(greater);
        if (_greaterValues != null && _greaterInt == g) return _greaterValues;
        _greaterInt = g;
        _greaterValues = All().Where(x => ToInt(x) > g).ToArray();
        return _greaterValues;
    }
}
