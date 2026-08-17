using System;

namespace AetherOS.Apps.Calculator.Engine;

/// <summary>
/// The numeric routines behind the graph screen's CALC menu. Every one of them returns false rather
/// than a number it cannot stand behind, and every one caps its own iteration count.
/// </summary>
public static class CalcSolve
{
    private const int ScanSamples = 256;
    private const int BisectIterations = 200;
    private const int GoldenIterations = 200;
    private const int IntegralMaxDepth = 20;
    private const int IntegralMaxEvaluations = 20000;
    private const double GoldenRatioInverse = 0.6180339887498949d;

    private delegate bool SampleFunc(double x, out double y);

    public static bool TryRoot(Expression f, CalcEnv env, double lo, double hi, out double x)
    {
        x = 0d;
        if (f is null || env is null || !TryOrderInterval(ref lo, ref hi))
        {
            return false;
        }

        var saved = SaveVariable(env);
        try
        {
            return TryScanRoot(Sampler(f, env), lo, hi, out x);
        }
        finally
        {
            RestoreVariable(env, saved);
        }
    }

    public static bool TryExtremum(Expression f, CalcEnv env, double lo, double hi, bool maximum, out double x, out double y)
    {
        x = 0d;
        y = 0d;
        if (f is null || env is null || !TryOrderInterval(ref lo, ref hi))
        {
            return false;
        }

        var saved = SaveVariable(env);
        try
        {
            var fn = Sampler(f, env);
            var step = (hi - lo) / ScanSamples;
            var haveBest = false;
            var bestX = lo;
            var bestY = 0d;
            for (var i = 0; i <= ScanSamples; i++)
            {
                var xi = i == ScanSamples ? hi : lo + (step * i);
                if (!fn(xi, out var yi))
                {
                    continue;
                }

                if (!haveBest || (maximum ? yi > bestY : yi < bestY))
                {
                    haveBest = true;
                    bestX = xi;
                    bestY = yi;
                }
            }

            if (!haveBest)
            {
                return false;
            }

            var a = Math.Max(lo, bestX - step);
            var b = Math.Min(hi, bestX + step);
            if (!TryGolden(fn, a, b, maximum, out var gx, out var gy))
            {
                x = bestX;
                y = bestY;
                return true;
            }

            var better = maximum ? gy >= bestY : gy <= bestY;
            x = better ? gx : bestX;
            y = better ? gy : bestY;
            return true;
        }
        finally
        {
            RestoreVariable(env, saved);
        }
    }

    public static bool TryIntersect(Expression f, Expression g, CalcEnv env, double lo, double hi, out double x, out double y)
    {
        x = 0d;
        y = 0d;
        if (f is null || g is null || env is null || !TryOrderInterval(ref lo, ref hi))
        {
            return false;
        }

        var saved = SaveVariable(env);
        try
        {
            var left = Sampler(f, env);
            var right = Sampler(g, env);
            bool Difference(double t, out double d)
            {
                d = 0d;
                if (!left(t, out var a) || !right(t, out var b))
                {
                    return false;
                }

                d = a - b;
                return !double.IsNaN(d) && !double.IsInfinity(d);
            }

            if (!TryScanRoot(Difference, lo, hi, out var hit))
            {
                return false;
            }

            if (!left(hit, out var value))
            {
                return false;
            }

            x = hit;
            y = value;
            return true;
        }
        finally
        {
            RestoreVariable(env, saved);
        }
    }

    public static bool TryDerivative(Expression f, CalcEnv env, double at, out double value)
    {
        value = 0d;
        if (f is null || env is null || double.IsNaN(at) || double.IsInfinity(at))
        {
            return false;
        }

        var saved = SaveVariable(env);
        try
        {
            var fn = Sampler(f, env);
            var h = 1e-4d * Math.Max(1d, Math.Abs(at));
            if (!fn(at + h, out var wide1) || !fn(at - h, out var wide2))
            {
                return false;
            }

            var half = h / 2d;
            if (!fn(at + half, out var near1) || !fn(at - half, out var near2))
            {
                return false;
            }

            var coarse = (wide1 - wide2) / (2d * h);
            var fine = (near1 - near2) / (2d * half);
            var richardson = ((4d * fine) - coarse) / 3d;
            if (double.IsNaN(richardson) || double.IsInfinity(richardson))
            {
                return false;
            }

            value = richardson;
            return true;
        }
        finally
        {
            RestoreVariable(env, saved);
        }
    }

    public static bool TryIntegral(Expression f, CalcEnv env, double lo, double hi, out double value)
    {
        value = 0d;
        if (f is null || env is null || double.IsNaN(lo) || double.IsNaN(hi))
        {
            return false;
        }

        if (double.IsInfinity(lo) || double.IsInfinity(hi))
        {
            return false;
        }

        if (lo == hi)
        {
            return true;
        }

        var sign = 1d;
        if (hi < lo)
        {
            (lo, hi) = (hi, lo);
            sign = -1d;
        }

        var saved = SaveVariable(env);
        try
        {
            var fn = Sampler(f, env);
            var mid = 0.5d * (lo + hi);
            if (!fn(lo, out var fa) || !fn(mid, out var fm) || !fn(hi, out var fb))
            {
                return false;
            }

            var whole = (hi - lo) / 6d * (fa + (4d * fm) + fb);
            var tolerance = 1e-10d * Math.Max(1d, Math.Abs(whole));
            var budget = IntegralMaxEvaluations;
            if (!Simpson(fn, lo, hi, fa, fm, fb, whole, tolerance, IntegralMaxDepth, ref budget, out var total))
            {
                return false;
            }

            if (double.IsNaN(total) || double.IsInfinity(total))
            {
                return false;
            }

            value = sign * total;
            return true;
        }
        finally
        {
            RestoreVariable(env, saved);
        }
    }

    private static SampleFunc Sampler(Expression f, CalcEnv env)
    {
        return (double x, out double y) =>
        {
            env.Vars[CalcEnv.GraphVariable] = x;
            return Calc.TryEvaluate(f, env, out y, out _);
        };
    }

    private static double SaveVariable(CalcEnv env)
    {
        return env.Vars.TryGetValue(CalcEnv.GraphVariable, out var value) ? value : 0d;
    }

    private static void RestoreVariable(CalcEnv env, double value)
    {
        env.Vars[CalcEnv.GraphVariable] = value;
    }

    private static bool TryOrderInterval(ref double lo, ref double hi)
    {
        if (double.IsNaN(lo) || double.IsNaN(hi) || double.IsInfinity(lo) || double.IsInfinity(hi))
        {
            return false;
        }

        if (hi < lo)
        {
            (lo, hi) = (hi, lo);
        }

        return hi > lo;
    }

    private static double Tolerance(double x)
    {
        return 1e-12d * Math.Max(1d, Math.Abs(x));
    }

    private static bool TryScanRoot(SampleFunc fn, double lo, double hi, out double x)
    {
        x = 0d;
        var step = (hi - lo) / ScanSamples;
        var haveLeft = fn(lo, out var yLeft);
        if (haveLeft && yLeft == 0d)
        {
            x = lo;
            return true;
        }

        var xLeft = lo;
        for (var i = 1; i <= ScanSamples; i++)
        {
            var xRight = i == ScanSamples ? hi : lo + (step * i);
            var haveRight = fn(xRight, out var yRight);
            if (haveRight && yRight == 0d)
            {
                x = xRight;
                return true;
            }

            if (haveLeft && haveRight && ((yLeft < 0d && yRight > 0d) || (yLeft > 0d && yRight < 0d)))
            {
                if (TryBisect(fn, xLeft, xRight, yLeft, yRight, out x))
                {
                    return true;
                }
            }

            xLeft = xRight;
            yLeft = yRight;
            haveLeft = haveRight;
        }

        return false;
    }

    private static bool TryBisect(SampleFunc fn, double lo, double hi, double yLo, double yHi, out double x)
    {
        x = 0d;
        var scale = Math.Max(Math.Abs(yLo), Math.Abs(yHi));
        for (var i = 0; i < BisectIterations; i++)
        {
            var mid = 0.5d * (lo + hi);
            if (!fn(mid, out var yMid))
            {
                return false;
            }

            if (yMid == 0d || (hi - lo) <= Tolerance(mid))
            {
                // A sign change straddles a pole as readily as a root; only a residual that actually
                // collapsed is one, so an asymptote is refused rather than reported at its own abscissa.
                if (Math.Abs(yMid) > 1e-6d * Math.Max(1d, scale))
                {
                    return false;
                }
                x = mid;
                return true;
            }

            if ((yLo < 0d) == (yMid < 0d))
            {
                lo = mid;
                yLo = yMid;
            }
            else
            {
                hi = mid;
            }
        }

        x = 0.5d * (lo + hi);
        return true;
    }

    private static bool TryGolden(SampleFunc fn, double a, double b, bool maximum, out double x, out double y)
    {
        x = 0d;
        y = 0d;
        if (b <= a)
        {
            return false;
        }

        var c = b - ((b - a) * GoldenRatioInverse);
        var d = a + ((b - a) * GoldenRatioInverse);
        if (!fn(c, out var fc) || !fn(d, out var fd))
        {
            return false;
        }

        for (var i = 0; i < GoldenIterations && (b - a) > Tolerance(c); i++)
        {
            if (maximum ? fc > fd : fc < fd)
            {
                b = d;
                d = c;
                fd = fc;
                c = b - ((b - a) * GoldenRatioInverse);
                if (!fn(c, out fc))
                {
                    return false;
                }
            }
            else
            {
                a = c;
                c = d;
                fc = fd;
                d = a + ((b - a) * GoldenRatioInverse);
                if (!fn(d, out fd))
                {
                    return false;
                }
            }
        }

        var best = 0.5d * (a + b);
        if (!fn(best, out var value))
        {
            return false;
        }

        x = best;
        y = value;
        return true;
    }

    private static bool Simpson(SampleFunc fn, double a, double b, double fa, double fm, double fb, double whole, double tolerance, int depth, ref int budget, out double value)
    {
        value = 0d;
        var mid = 0.5d * (a + b);
        var leftMid = 0.5d * (a + mid);
        var rightMid = 0.5d * (mid + b);
        if (budget < 2)
        {
            return false;
        }

        budget -= 2;
        if (!fn(leftMid, out var flm) || !fn(rightMid, out var frm))
        {
            return false;
        }

        var left = (mid - a) / 6d * (fa + (4d * flm) + fm);
        var right = (b - mid) / 6d * (fm + (4d * frm) + fb);
        var refined = left + right;
        if (depth <= 0 || Math.Abs(refined - whole) <= 15d * tolerance)
        {
            value = refined + ((refined - whole) / 15d);
            return true;
        }

        if (!Simpson(fn, a, mid, fa, flm, fm, left, tolerance / 2d, depth - 1, ref budget, out var leftValue))
        {
            return false;
        }

        if (!Simpson(fn, mid, b, fm, frm, fb, right, tolerance / 2d, depth - 1, ref budget, out var rightValue))
        {
            return false;
        }

        value = leftValue + rightValue;
        return true;
    }
}
