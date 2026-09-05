using System;
using System.Collections.Generic;
using System.Globalization;

namespace Durango.Online;

/// <summary>
/// ตัวคิดสูตรค่าสถานะจากไฟล์ data ของเกม
///
/// ทำไมต้องมี: ค่าสถานะในไฟล์ของ NEXON ไม่ใช่ตัวเลขล้วน แต่เป็น "สูตรตามเลเวล" เขียนเป็นสตริง
/// เช่นใน <c>entity_types/animal.json</c>
/// <code>
/// life_max = (1.06 * ((combat_level + 24) ** 2)) * unstable_factor
/// attack   = (21.5 + combat_level * 0.9) * unstable_factor
/// </code>
/// ⇒ ถ้าไม่คิดสูตรก็ต้องเดาตัวเลขเอา ซึ่งผิดกฎของโปรเจกต์ (ทุกค่าต้องอิงต้นฉบับ)
///
/// ไวยากรณ์ที่รองรับ — สำรวจจากไฟล์จริงแล้วว่าใช้แค่นี้:
/// ตัวเลข · ตัวแปรที่ผู้เรียกส่งเข้ามา · <c>+ - * / **</c> · วงเล็บ · เครื่องหมายลบหน้าตัว
/// · ฟังก์ชัน <c>int() abs() round() exp() sqrt()</c> และ <c>min(a,b) max(a,b) pow(a,b)</c>
/// (สำรวจไฟล์จริง: animal.json ใช้แค่ <c>* + **</c> · performance.json เพิ่ม <c>- / int()</c>
///  และสูตรขนาดกรงใช้ <c>min(150, 60 + 15 * int(level/10))</c>)
///
/// <c>**</c> เป็นการยกกำลังแบบ Python (ข้อมูลชุดนี้ถอดมาจากฝั่ง Python ของ NEXON) และ
/// **ผูกขวา** เหมือน Python คือ <c>2 ** 3 ** 2</c> = 512 ไม่ใช่ 64
///
/// เจอสิ่งที่ไม่รองรับ (ชื่อฟังก์ชันแปลก ๆ ตัวแปรที่ไม่รู้จัก) จะคืน <c>false</c>
/// ให้ผู้เรียกตัดสินใจเอง — **ไม่เดาค่าให้** เพราะค่าที่เดามาแล้วเงียบอันตรายกว่าค่าที่หายไป
/// </summary>
public static class StatFormula
{
    /// <summary>คิดสูตรที่มีตัวแปรตัวเดียว (กรณีที่พบบ่อยสุด)</summary>
    public static bool TryEval(string expr, string variable, double value, out double result) =>
        TryEval(expr, new Dictionary<string, double> { [variable] = value }, out result);

    /// <summary>
    /// คิดสูตร — คืน false เมื่ออ่านไม่จบ เจอตัวแปรที่ไม่รู้จัก หรือได้ NaN/Infinity
    /// </summary>
    public static bool TryEval(string expr, IReadOnlyDictionary<string, double> vars, out double result)
    {
        result = 0.0;
        if (string.IsNullOrWhiteSpace(expr)) return false;

        int pos = 0;
        try
        {
            double value = Additive(expr, ref pos, vars);
            Skip(expr, ref pos);
            if (pos != expr.Length) return false;                       // มีตัวอักษรเหลือ = อ่านไม่จบ
            if (double.IsNaN(value) || double.IsInfinity(value)) return false;
            result = value;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>คิดสูตร ถ้าไม่ผ่านคืนค่าสำรองที่ผู้เรียกกำหนด (ใช้ตอนที่ค่าหายแล้วเกมพังกว่า)</summary>
    public static double EvalOr(string expr, IReadOnlyDictionary<string, double> vars, double fallback) =>
        TryEval(expr, vars, out double value) ? value : fallback;

    private static void Skip(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    private static double Additive(string s, ref int i, IReadOnlyDictionary<string, double> vars)
    {
        double left = Multiplicative(s, ref i, vars);
        while (true)
        {
            Skip(s, ref i);
            if (i >= s.Length) return left;
            char c = s[i];
            if (c != '+' && c != '-') return left;
            i++;
            double right = Multiplicative(s, ref i, vars);
            left = c == '+' ? left + right : left - right;
        }
    }

    private static double Multiplicative(string s, ref int i, IReadOnlyDictionary<string, double> vars)
    {
        double left = Power(s, ref i, vars);
        while (true)
        {
            Skip(s, ref i);
            if (i >= s.Length) return left;
            // ระวัง: '*' เดี่ยวคือคูณ แต่ '**' คือยกกำลัง ต้องไม่กินไปก่อน
            if (s[i] == '*' && i + 1 < s.Length && s[i + 1] == '*') return left;
            char c = s[i];
            if (c != '*' && c != '/') return left;
            i++;
            double right = Power(s, ref i, vars);
            left = c == '*' ? left * right : left / right;
        }
    }

    /// <summary>ยกกำลัง <c>**</c> — ผูกขวาตามแบบ Python จึงเรียกตัวเองซ้ำทางขวา</summary>
    private static double Power(string s, ref int i, IReadOnlyDictionary<string, double> vars)
    {
        double left = Unary(s, ref i, vars);
        Skip(s, ref i);
        if (i + 1 < s.Length && s[i] == '*' && s[i + 1] == '*')
        {
            i += 2;
            double right = Power(s, ref i, vars);
            return Math.Pow(left, right);
        }
        return left;
    }

    private static double Unary(string s, ref int i, IReadOnlyDictionary<string, double> vars)
    {
        Skip(s, ref i);
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
        {
            char sign = s[i];
            i++;
            double value = Unary(s, ref i, vars);
            return sign == '-' ? -value : value;
        }
        return Primary(s, ref i, vars);
    }

    private static double Primary(string s, ref int i, IReadOnlyDictionary<string, double> vars)
    {
        Skip(s, ref i);
        if (i >= s.Length) throw new FormatException();

        if (s[i] == '(')
        {
            i++;
            double value = Additive(s, ref i, vars);
            Skip(s, ref i);
            if (i >= s.Length || s[i] != ')') throw new FormatException();
            i++;
            return value;
        }

        if (char.IsDigit(s[i]) || s[i] == '.')
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            return double.Parse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        if (char.IsLetter(s[i]) || s[i] == '_')
        {
            int start = i;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
            string name = s.Substring(start, i - start);

            Skip(s, ref i);
            if (i < s.Length && s[i] == '(')                            // เรียกฟังก์ชัน
            {
                i++;
                var args = new List<double>();
                Skip(s, ref i);
                if (i < s.Length && s[i] == ')')
                {
                    i++;                                                // ฟังก์ชันไม่มีอาร์กิวเมนต์
                }
                else
                {
                    while (true)
                    {
                        args.Add(Additive(s, ref i, vars));
                        Skip(s, ref i);
                        if (i < s.Length && s[i] == ',') { i++; continue; }
                        if (i >= s.Length || s[i] != ')') throw new FormatException();
                        i++;
                        break;
                    }
                }

                return name switch
                {
                    // int() ของ Python ตัดเศษเข้าหาศูนย์
                    "int" when args.Count == 1 => Math.Truncate(args[0]),
                    "abs" when args.Count == 1 => Math.Abs(args[0]),
                    "round" when args.Count == 1 => Math.Round(args[0], MidpointRounding.AwayFromZero),
                    // min/max สองตัว — ใช้ในสูตรขนาดกรง เช่น "min(150, 60 + 15 * int(level/10))"
                    "min" when args.Count == 2 => Math.Min(args[0], args[1]),
                    "max" when args.Count == 2 => Math.Max(args[0], args[1]),
                    // สูตรจับสัตว์ใน constants.json → taming ใช้สองตัวนี้
                    //   success_ratio      = "2 * (1 / (1 + exp(-(2.2 / 6 * d_l + 2.2))) - 0.5)"
                    //   adjust_by_life_ratio = "1 - 0.8 * pow(r / R, 2)"
                    "exp" when args.Count == 1 => Math.Exp(args[0]),
                    "sqrt" when args.Count == 1 => Math.Sqrt(args[0]),
                    "pow" when args.Count == 2 => Math.Pow(args[0], args[1]),
                    _ => throw new FormatException()                    // ฟังก์ชันที่ไม่รู้จัก = ไม่เดา
                };
            }

            if (vars != null && vars.TryGetValue(name, out double value)) return value;
            throw new FormatException();                                // ตัวแปรที่ไม่รู้จัก = ไม่เดา
        }

        throw new FormatException();
    }
}
