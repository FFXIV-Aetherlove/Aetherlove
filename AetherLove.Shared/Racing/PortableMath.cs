namespace AetherLove.Shared.Racing;

/// <summary>
/// Deterministic <c>Log</c>, <c>Sin</c> and <c>Cos</c> for the race engine: the same bits on
/// every machine, forever. IEEE-754 mandates correct rounding for + - * / and sqrt and nothing
/// for the transcendentals, which .NET forwards to the platform math library; those libraries
/// disagree by ulps between OSes, and the race integrates the Gaussian across ~1,400 ticks, so
/// a one-ulp drift accumulates until it flips a branch and two machines watch different races
/// from the same seed. These are fdlibm's kernels (Sun Microsystems, 1993) transcribed to C#:
/// pure arithmetic plus integer bit work, deterministic by construction. They promise to equal
/// THEMSELVES bit-for-bit everywhere, not to equal <see cref="Math.Log(double)"/>.
///
/// <para><b>Do not "optimise" this.</b> Every parenthesis is load-bearing: reassociating
/// <c>a*b + c*d</c>, hoisting a subexpression across a rounding step, or contracting a
/// multiply-add into an FMA all change the answer. RyuJIT does not form FMAs on its own,
/// which is why this is safe in C# and would not be in C++.</para>
/// </summary>
public static class PortableMath
{
    // fdlibm __ieee754_log: x = 2^k * m with m in [sqrt(2)/2, sqrt(2)), then
    // log(m) = 2s + s*R(s^2) with s = f/(2+f), f = m-1. Max error < 1 ulp.
    private const double Ln2Hi = 6.93147180369123816490e-01;
    private const double Ln2Lo = 1.90821492927058770002e-10;
    private const double Lg1 = 6.666666666666735130e-01;
    private const double Lg2 = 3.999999999940941908e-01;
    private const double Lg3 = 2.857142874366239149e-01;
    private const double Lg4 = 2.222219843214978396e-01;
    private const double Lg5 = 1.818357216161805012e-01;
    private const double Lg6 = 1.531383769920937332e-01;
    private const double Lg7 = 1.479819860511658591e-01;

    /// <summary>2^54, for lifting a subnormal into the normal range before the exponent is
    /// read off.</summary>
    private const double TwoPow54 = 18014398509481984.0;

    // Cody-Waite reduction against a three-part pi/2, then fdlibm's kernels on the reduced
    // argument in [-pi/4, pi/4]. Pio21 carries 33 bits and nothing below, so n*Pio21 is EXACT
    // for every quadrant count this engine can produce; that is what makes the first
    // subtraction lossless.
    private const double InvPio2 = 6.36619772367581382433e-01;
    private const double Pio21 = 1.57079632673412561417e+00;
    private const double Pio21t = 6.07710050650619224932e-11;
    private const double Pio22 = 6.07710050630396597660e-11;
    private const double Pio22t = 2.02226624879595063154e-21;
    private const double Pio23 = 2.02226624871116645580e-21;
    private const double Pio23t = 8.47842766036889956997e-32;

    private const double S1 = -1.66666666666666324348e-01;
    private const double S2 = 8.33333333332248946124e-03;
    private const double S3 = -1.98412698298579493134e-04;
    private const double S4 = 2.75573137070700676789e-06;
    private const double S5 = -2.50507602534068634195e-08;
    private const double S6 = 1.58969099521155010221e-10;

    private const double C1 = 4.16666666666666019037e-02;
    private const double C2 = -1.38888888888741095749e-03;
    private const double C3 = 2.48015872894767294178e-05;
    private const double C4 = -2.75573143513906633035e-07;
    private const double C5 = 2.08757232129817482790e-09;
    private const double C6 = -1.13596475577881948265e-11;

    /// <summary>Natural log, to under 1 ulp, out of arithmetic alone.</summary>
    public static double Log(double x)
    {
        var bits = BitConverter.DoubleToInt64Bits(x);
        var hx = (int)(bits >> 32);
        var k = 0;

        if (hx < 0x0010_0000)
        {
            // Zero, negative, or subnormal. The first two answer the way the platform answers;
            // the third gets scaled up and continues.
            if (x <= 0.0)
            {
                return x == 0.0 ? double.NegativeInfinity : double.NaN;
            }

            k -= 54;
            x *= TwoPow54;
            bits = BitConverter.DoubleToInt64Bits(x);
            hx = (int)(bits >> 32);
        }

        if (hx >= 0x7ff0_0000)
        {
            return x; // +inf or NaN, unchanged
        }

        k += (hx >> 20) - 1023;
        hx &= 0x000f_ffff;

        // Nudge the mantissa into [sqrt(2)/2, sqrt(2)) and carry the half-exponent into k.
        // 0x95f64 is the offset that puts the split at sqrt(2); it is fdlibm's, verbatim.
        var i = (hx + 0x95f64) & 0x0010_0000;
        x = BitConverter.Int64BitsToDouble(((long)(uint)(hx | (i ^ 0x3ff0_0000)) << 32) | (bits & 0xffff_ffffL));
        k += i >> 20;

        var f = x - 1.0;
        var dk = (double)k;

        if ((0x000f_ffff & (2 + hx)) < 3)
        {
            // Within 2^-20 of 1: the general form loses everything to cancellation here, so
            // fdlibm takes the two-term series instead.
            if (f == 0.0)
            {
                return k == 0 ? 0.0 : (dk * Ln2Hi) + (dk * Ln2Lo);
            }

            var rr = f * f * (0.5 - (0.33333333333333333 * f));
            return k == 0 ? f - rr : (dk * Ln2Hi) - ((rr - (dk * Ln2Lo)) - f);
        }

        var s = f / (2.0 + f);
        var z = s * s;
        var w = z * z;
        var t1 = w * (Lg2 + (w * (Lg4 + (w * Lg6))));
        var t2 = z * (Lg1 + (w * (Lg3 + (w * (Lg5 + (w * Lg7))))));
        var r = t2 + t1;

        // fdlibm's branch selector, bit trick intact: positive only when hx sits inside
        // [0x6147a, 0x6b851], the mantissa band around sqrt(2) where |f| is largest and the
        // hfsq form rounds better. Both forms are the same identity; only the last bit is at
        // stake.
        if (((hx - 0x6147a) | (0x6b851 - hx)) > 0)
        {
            var hfsq = 0.5 * f * f;
            return k == 0
                ? f - (hfsq - (s * (hfsq + r)))
                : (dk * Ln2Hi) - ((hfsq - ((s * (hfsq + r)) + (dk * Ln2Lo))) - f);
        }

        return k == 0
            ? f - (s * (f - r))
            : (dk * Ln2Hi) - (((s * (f - r)) - (dk * Ln2Lo)) - f);
    }

    /// <summary>Sine, to under 1 ulp, out of arithmetic alone.
    ///
    /// <para><b>Range contract.</b> Accurate for <c>|x| &lt; 2^20</c>. Beyond that Cody-Waite
    /// reduction loses bits and the answer stops being a good sine, but it stays deterministic,
    /// which is the property this file actually promises. Past <c>1e18</c> a double's own
    /// spacing exceeds 100 radians, so the honest answer is NaN. None of that is reachable from
    /// the race engine: its largest argument is a course heading, which never leaves ±10.</para></summary>
    public static double Sin(double x)
    {
        if (double.IsNaN(x) || !(Math.Abs(x) < 1e18))
        {
            return double.NaN;
        }

        if (x is > -0.7853981633974483 and < 0.7853981633974483)
        {
            return KernelSin(x, 0.0, false);
        }

        var n = RemPio2(x, out var y0, out var y1);
        return (n & 3) switch
        {
            0 => KernelSin(y0, y1, true),
            1 => KernelCos(y0, y1),
            2 => -KernelSin(y0, y1, true),
            _ => -KernelCos(y0, y1),
        };
    }

    /// <summary>Cosine, to under 1 ulp, out of arithmetic alone. Same range contract as
    /// <see cref="Sin(double)"/>.</summary>
    public static double Cos(double x)
    {
        if (double.IsNaN(x) || !(Math.Abs(x) < 1e18))
        {
            return double.NaN;
        }

        if (x is > -0.7853981633974483 and < 0.7853981633974483)
        {
            return KernelCos(x, 0.0);
        }

        var n = RemPio2(x, out var y0, out var y1);
        return (n & 3) switch
        {
            0 => KernelCos(y0, y1),
            1 => -KernelSin(y0, y1, true),
            2 => -KernelCos(y0, y1),
            _ => KernelSin(y0, y1, true),
        };
    }

    /// <summary>The <c>float</c> face of <see cref="Log(double)"/>. Widening a float to double
    /// is exact and the double answer is good to a fraction of a float's last bit, so the single
    /// rounding on the way out lands on the correctly-rounded float.</summary>
    public static float Log(float x) => (float)Log((double)x);

    /// <summary>The <c>float</c> face of <see cref="Sin(double)"/>.</summary>
    public static float Sin(float x) => (float)Sin((double)x);

    /// <summary>The <c>float</c> face of <see cref="Cos(double)"/>.</summary>
    public static float Cos(float x) => (float)Cos((double)x);

    /// <summary>Cody-Waite: strip <c>n*pi/2</c> from x and hand back the remainder as an
    /// unevaluated double-double <c>y0 + y1</c>, because the kernels need more than 53 bits of
    /// the reduced angle when x lands near a multiple of pi/2. The two refinement stages fire
    /// only on that near-cancellation.</summary>
    private static long RemPio2(double x, out double y0, out double y1)
    {
        // Math.Round is roundToIntegralTiesToEven, one of the operations IEEE pins down, so it
        // IS the same everywhere. The quadrant count is a long so an out-of-contract argument
        // degrades in accuracy instead of hitting an out-of-range double-to-int conversion.
        var fn = Math.Round(x * InvPio2);
        var n = (long)fn;

        var r = x - (fn * Pio21);
        var w = fn * Pio21t;
        y0 = r - w;

        var ex = ((int)(BitConverter.DoubleToInt64Bits(x) >> 32) >> 20) & 0x7ff;
        var ey = ((int)(BitConverter.DoubleToInt64Bits(y0) >> 32) >> 20) & 0x7ff;

        if (ex - ey > 16)
        {
            var t = r;
            w = fn * Pio22;
            r = t - w;
            w = (fn * Pio22t) - ((t - r) - w);
            y0 = r - w;
            ey = ((int)(BitConverter.DoubleToInt64Bits(y0) >> 32) >> 20) & 0x7ff;
            if (ex - ey > 49)
            {
                t = r;
                w = fn * Pio23;
                r = t - w;
                w = (fn * Pio23t) - ((t - r) - w);
                y0 = r - w;
            }
        }

        y1 = (r - y0) - w;
        return n;
    }

    /// <summary>fdlibm <c>__kernel_sin</c> on <c>|x| &lt;= pi/4</c>. <paramref name="tail"/>
    /// says whether <paramref name="y"/> carries the low half of a reduced argument; when it
    /// does, the correction term <c>y*cos(x)</c> is folded in rather than dropped.</summary>
    private static double KernelSin(double x, double y, bool tail)
    {
        var z = x * x;
        var v = z * x;
        var r = S2 + (z * (S3 + (z * (S4 + (z * (S5 + (z * S6)))))));
        return tail
            ? x - (((z * ((0.5 * y) - (v * r))) - y) - (v * S1))
            : x + (v * (S1 + (z * r)));
    }

    /// <summary>fdlibm <c>__kernel_cos</c> on <c>|x| &lt;= pi/4</c>. The
    /// <c>w + (((1-w)-hz) + ...)</c> shape is a compensation step, not clutter: it recovers the
    /// bits <c>1 - z/2</c> throws away.</summary>
    private static double KernelCos(double x, double y)
    {
        var z = x * x;
        var r = z * (C1 + (z * (C2 + (z * (C3 + (z * (C4 + (z * (C5 + (z * C6))))))))));
        var hz = 0.5 * z;
        var w = 1.0 - hz;
        return w + (((1.0 - w) - hz) + ((z * r) - (x * y)));
    }
}
