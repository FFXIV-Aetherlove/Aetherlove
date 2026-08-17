using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Calculator.Engine;

internal sealed class FuncInfo
{
    internal FuncInfo(string name, int minArgs, int maxArgs)
    {
        Name = name;
        MinArgs = minArgs;
        MaxArgs = maxArgs;
    }

    internal string Name { get; }

    internal int MinArgs { get; }

    internal int MaxArgs { get; }
}

/// <summary>The function catalog plus every guarded math primitive the evaluator uses.</summary>
internal static class Funcs
{
    private const double RadiansPerDegree = Math.PI / 180d;
    private const double DegreesPerRadian = 180d / Math.PI;

    /// <summary>In degree mode a whole-number angle should read as an exact 0, the way a handheld shows it.</summary>
    private const double DegreeSnap = 1e-12;

    private static readonly Dictionary<string, FuncInfo> Table = BuildTable();

    internal static bool TryResolve(string name, out FuncInfo info)
    {
        return Table.TryGetValue(name, out info!);
    }

    internal static bool Power(double a, double b, ref EvalContext ctx, out double value)
    {
        value = 0d;
        if (b == 0d)
        {
            value = 1d;
            return true;
        }

        if (a == 0d)
        {
            if (b < 0d)
            {
                return ctx.Fail(CalcErrors.DivZero, out value);
            }

            value = 0d;
            return true;
        }

        if (a < 0d && Math.Abs(b - Math.Round(b)) > 0d)
        {
            return ctx.Fail(CalcErrors.Domain, out value);
        }

        var result = Math.Pow(a, b);
        if (double.IsNaN(result) || double.IsInfinity(result))
        {
            return ctx.Fail(CalcErrors.Overflow, out value);
        }

        value = result;
        return true;
    }

    internal static bool Invoke(FuncInfo info, double[] args, ref EvalContext ctx, out double value)
    {
        value = 0d;
        var env = ctx.Env;
        if (env is null)
        {
            return ctx.Fail(CalcErrors.Undefined, out value);
        }

        var a = args.Length > 0 ? args[0] : 0d;
        var b = args.Length > 1 ? args[1] : 0d;
        switch (info.Name)
        {
            case "sin":
                value = Snap(Math.Sin(ToRadians(a, env)), env);
                return true;
            case "cos":
                value = Snap(Math.Cos(ToRadians(a, env)), env);
                return true;
            case "tan":
                if (env.Angle == AngleMode.Degrees && IsDegreeAsymptote(a))
                {
                    return ctx.Fail(CalcErrors.Domain, out value);
                }

                value = Snap(Math.Tan(ToRadians(a, env)), env);
                return true;
            case "asin":
                if (a < -1d || a > 1d)
                {
                    return ctx.Fail(CalcErrors.Domain, out value);
                }

                value = FromRadians(Math.Asin(a), env);
                return true;
            case "acos":
                if (a < -1d || a > 1d)
                {
                    return ctx.Fail(CalcErrors.Domain, out value);
                }

                value = FromRadians(Math.Acos(a), env);
                return true;
            case "atan":
                value = FromRadians(Math.Atan(a), env);
                return true;
            case "atan2":
                if (a == 0d && b == 0d)
                {
                    return ctx.Fail(CalcErrors.Domain, out value);
                }

                value = FromRadians(Math.Atan2(a, b), env);
                return true;
            case "sinh":
                value = Math.Sinh(a);
                return true;
            case "cosh":
                value = Math.Cosh(a);
                return true;
            case "tanh":
                value = Math.Tanh(a);
                return true;
            case "asinh":
                value = Math.Asinh(a);
                return true;
            case "acosh":
                if (a < 1d)
                {
                    return ctx.Fail(CalcErrors.Domain, out value);
                }

                value = Math.Acosh(a);
                return true;
            case "atanh":
                if (a <= -1d || a >= 1d)
                {
                    return ctx.Fail(CalcErrors.Domain, out value);
                }

                value = Math.Atanh(a);
                return true;
            case "sqrt":
                if (a < 0d)
                {
                    return ctx.Fail(CalcErrors.Domain, out value);
                }

                value = Math.Sqrt(a);
                return true;
            case "cbrt":
                value = Math.Cbrt(a);
                return true;
            case "abs":
                value = Math.Abs(a);
                return true;
            case "ln":
                if (a <= 0d)
                {
                    return ctx.Fail(CalcErrors.Domain, out value);
                }

                value = Math.Log(a);
                return true;
            case "log":
                if (a <= 0d)
                {
                    return ctx.Fail(CalcErrors.Domain, out value);
                }

                if (args.Length == 1)
                {
                    value = Math.Log10(a);
                    return true;
                }

                if (b <= 0d || b == 1d)
                {
                    return ctx.Fail(CalcErrors.Domain, out value);
                }

                value = Math.Log(a) / Math.Log(b);
                return true;
            case "log10":
                if (a <= 0d)
                {
                    return ctx.Fail(CalcErrors.Domain, out value);
                }

                value = Math.Log10(a);
                return true;
            case "log2":
                if (a <= 0d)
                {
                    return ctx.Fail(CalcErrors.Domain, out value);
                }

                value = Math.Log2(a);
                return true;
            case "exp":
                value = Math.Exp(a);
                if (double.IsInfinity(value))
                {
                    return ctx.Fail(CalcErrors.Overflow, out value);
                }

                return true;
            case "pow":
                return Power(a, b, ref ctx, out value);
            case "floor":
                value = Math.Floor(a);
                return true;
            case "ceil":
                value = Math.Ceiling(a);
                return true;
            case "int":
                value = Math.Floor(a);
                return true;
            case "frac":
                // Partners "int" above, so int(a) + frac(a) == a holds for negatives too.
                value = a - Math.Floor(a);
                return true;
            case "sign":
                value = Math.Sign(a);
                return true;
            case "round":
                return Round(args, ref ctx, out value);
            case "min":
                value = Extreme(args, false);
                return true;
            case "max":
                value = Extreme(args, true);
                return true;
            case "mod":
                if (b == 0d)
                {
                    return ctx.Fail(CalcErrors.DivZero, out value);
                }

                value = a - (b * Math.Floor(a / b));
                return true;
            case "gcd":
                return Gcd(a, b, ref ctx, out value);
            case "lcm":
                return Lcm(a, b, ref ctx, out value);
            case "ncr":
                return Choose(a, b, false, ref ctx, out value);
            case "npr":
                return Choose(a, b, true, ref ctx, out value);
            default:
                return ctx.Fail(CalcErrors.Undefined, out value);
        }
    }

    private static Dictionary<string, FuncInfo> BuildTable()
    {
        var table = new Dictionary<string, FuncInfo>(StringComparer.OrdinalIgnoreCase);
        var single = new[]
        {
            "sin", "cos", "tan", "asin", "acos", "atan", "sinh", "cosh", "tanh", "asinh", "acosh", "atanh",
            "sqrt", "cbrt", "abs", "ln", "log10", "log2", "exp", "floor", "ceil", "int", "frac", "sign",
        };
        foreach (var name in single)
        {
            table[name] = new FuncInfo(name, 1, 1);
        }

        table["log"] = new FuncInfo("log", 1, 2);
        table["round"] = new FuncInfo("round", 1, 2);
        table["atan2"] = new FuncInfo("atan2", 2, 2);
        table["pow"] = new FuncInfo("pow", 2, 2);
        table["mod"] = new FuncInfo("mod", 2, 2);
        table["gcd"] = new FuncInfo("gcd", 2, 2);
        table["lcm"] = new FuncInfo("lcm", 2, 2);
        table["ncr"] = new FuncInfo("ncr", 2, 2);
        table["npr"] = new FuncInfo("npr", 2, 2);
        table["min"] = new FuncInfo("min", 1, CalcLimits.MaxArguments);
        table["max"] = new FuncInfo("max", 1, CalcLimits.MaxArguments);
        return table;
    }

    private static double ToRadians(double v, CalcEnv env)
    {
        return env.Angle == AngleMode.Degrees ? v * RadiansPerDegree : v;
    }

    private static double FromRadians(double v, CalcEnv env)
    {
        return env.Angle == AngleMode.Degrees ? v * DegreesPerRadian : v;
    }

    private static double Snap(double v, CalcEnv env)
    {
        if (env.Angle == AngleMode.Degrees && Math.Abs(v) < DegreeSnap)
        {
            return 0d;
        }

        return v;
    }

    private static bool IsDegreeAsymptote(double degrees)
    {
        var m = degrees % 180d;
        if (m < 0d)
        {
            m += 180d;
        }

        return Math.Abs(m - 90d) < 1e-9;
    }

    private static bool Round(double[] args, ref EvalContext ctx, out double value)
    {
        value = 0d;
        var a = args[0];
        if (args.Length == 1)
        {
            value = Math.Round(a, MidpointRounding.AwayFromZero);
            return true;
        }

        var digits = args[1];
        if (Math.Abs(digits - Math.Round(digits)) > 1e-9 || digits < 0d || digits > 15d)
        {
            return ctx.Fail(CalcErrors.Domain, out value);
        }

        value = Math.Round(a, (int)Math.Round(digits), MidpointRounding.AwayFromZero);
        return true;
    }

    private static double Extreme(double[] args, bool maximum)
    {
        var best = args[0];
        for (var i = 1; i < args.Length; i++)
        {
            if (maximum ? args[i] > best : args[i] < best)
            {
                best = args[i];
            }
        }

        return best;
    }

    private static bool Gcd(double a, double b, ref EvalContext ctx, out double value)
    {
        value = 0d;
        if (!TryWholeNumber(a, out var x) || !TryWholeNumber(b, out var y))
        {
            return ctx.Fail(CalcErrors.Domain, out value);
        }

        x = Math.Abs(x);
        y = Math.Abs(y);
        while (y >= 1d)
        {
            var t = x % y;
            x = y;
            y = t;
        }

        value = x;
        return true;
    }

    private static bool Lcm(double a, double b, ref EvalContext ctx, out double value)
    {
        if (!Gcd(a, b, ref ctx, out var divisor))
        {
            value = 0d;
            return false;
        }

        if (divisor == 0d)
        {
            value = 0d;
            return true;
        }

        var result = Math.Abs(Math.Round(a) / divisor * Math.Round(b));
        if (double.IsInfinity(result))
        {
            return ctx.Fail(CalcErrors.Overflow, out value);
        }

        value = result;
        return true;
    }

    private static bool Choose(double n, double r, bool ordered, ref EvalContext ctx, out double value)
    {
        value = 0d;
        if (!TryWholeNumber(n, out var wholeN) || !TryWholeNumber(r, out var wholeR))
        {
            return ctx.Fail(CalcErrors.Domain, out value);
        }

        if (wholeN < 0d || wholeR < 0d
            || wholeN > CalcLimits.MaxCombinatorial || wholeR > CalcLimits.MaxCombinatorial)
        {
            return ctx.Fail(CalcErrors.Domain, out value);
        }

        if (wholeR > wholeN)
        {
            value = 0d;
            return true;
        }

        // nCr(n, r) == nCr(n, n-r), so taking the smaller half bounds the loop at n/2 instead of n.
        if (!ordered && wholeR > wholeN - wholeR)
        {
            wholeR = wholeN - wholeR;
        }

        var count = (int)wholeR;
        var result = 1d;
        if (ordered)
        {
            for (var i = 0; i < count && !double.IsInfinity(result); i++)
            {
                result *= wholeN - i;
            }
        }
        else
        {
            for (var i = 1; i <= count && !double.IsInfinity(result); i++)
            {
                result = result * (wholeN - count + i) / i;
            }
        }

        if (double.IsInfinity(result) || double.IsNaN(result))
        {
            return ctx.Fail(CalcErrors.Overflow, out value);
        }

        value = Math.Round(result);
        return true;
    }

    private static bool TryWholeNumber(double v, out double whole)
    {
        whole = Math.Round(v);
        return Math.Abs(v - whole) <= 1e-9;
    }
}
