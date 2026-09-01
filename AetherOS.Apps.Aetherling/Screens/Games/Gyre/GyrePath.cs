using System;
using System.Collections.Generic;
using System.Numerics;

namespace AetherOS.Apps.Aetherling.Screens.Games.Gyre;

/// <summary>One track in canvas units: the authored control points run through a centripetal
/// Catmull-Rom spline, resampled by arc length, then a curvature clamp rounds any corner tighter than
/// the marbles can visually take. Authored corners may be sharp; the clamp is the rule, not the author.</summary>
internal sealed class GyrePath
{
    private const float Step = 4f;
    private const float MinTurnRadius = 120f;

    private readonly Vector2[] _points;
    private readonly (float Start, float End)[] _tunnels;
    private readonly (float Start, float End)[] _overpasses;

    public float Length { get; }

    public float SpawnDelay { get; }

    public GyrePath(GyrePathDto dto)
    {
        SpawnDelay = dto.SpawnDelay;
        _tunnels = Spans(dto.Tunnels);
        _overpasses = Spans(dto.Overpasses);
        var raw = Resample(Centripetal(dto.Points));
        ClampCurvature(raw);
        var final = Resample(raw);
        _points = final.ToArray();
        Length = Step * (_points.Length - 1);
    }

    public Vector2 PosAt(float d)
    {
        var f = Math.Clamp(d, 0f, Length) / Step;
        var i = Math.Clamp((int)f, 0, _points.Length - 2);
        return Vector2.Lerp(_points[i], _points[i + 1], f - i);
    }

    public Vector2 TangentAt(float d)
    {
        var i = Math.Clamp((int)(Math.Clamp(d, 0f, Length) / Step), 0, _points.Length - 2);
        var t = _points[i + 1] - _points[i];
        return t.LengthSquared() > 0.0001f ? Vector2.Normalize(t) : new Vector2(1f, 0f);
    }

    public float Frac(float d) => Length <= 0f ? 0f : Math.Clamp(d / Length, 0f, 1f);

    public bool InTunnel(float d) => In(_tunnels, Frac(d));

    public bool InOverpass(float d) => In(_overpasses, Frac(d));

    private static bool In((float Start, float End)[] spans, float f)
    {
        foreach (var (s, e) in spans)
        {
            if (f >= s && f <= e)
            {
                return true;
            }
        }
        return false;
    }

    private static (float, float)[] Spans(float[][] raw)
    {
        var spans = new (float, float)[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            spans[i] = (raw[i][0], raw[i][1]);
        }
        return spans;
    }

    private static List<Vector2> Centripetal(float[][] controls)
    {
        var p = new List<Vector2> { new(controls[0][0], controls[0][1]) };
        foreach (var c in controls)
        {
            p.Add(new Vector2(c[0], c[1]));
        }
        p.Add(p[^1]);

        var outPts = new List<Vector2>();
        for (var i = 0; i < p.Count - 3; i++)
        {
            var (p0, p1, p2, p3) = (p[i], p[i + 1], p[i + 2], p[i + 3]);
            var t0 = 0f;
            var t1 = t0 + MathF.Pow(MathF.Max(Vector2.Distance(p0, p1), 0.0001f), 0.5f);
            var t2 = t1 + MathF.Pow(MathF.Max(Vector2.Distance(p1, p2), 0.0001f), 0.5f);
            var t3 = t2 + MathF.Pow(MathF.Max(Vector2.Distance(p2, p3), 0.0001f), 0.5f);
            var n = Math.Max(2, (int)(Vector2.Distance(p1, p2) / 2f));
            for (var j = 0; j < n; j++)
            {
                var t = t1 + ((t2 - t1) * j / n);
                var a1 = Lerp(p0, p1, t0, t1, t);
                var a2 = Lerp(p1, p2, t1, t2, t);
                var a3 = Lerp(p2, p3, t2, t3, t);
                var b1 = Lerp(a1, a2, t0, t2, t);
                var b2 = Lerp(a2, a3, t1, t3, t);
                outPts.Add(Lerp(b1, b2, t1, t2, t));
            }
        }
        outPts.Add(p[^2]);
        return outPts;

        static Vector2 Lerp(Vector2 a, Vector2 b, float ta, float tb, float t) =>
            tb - ta < 0.000001f ? a : Vector2.Lerp(a, b, (t - ta) / (tb - ta));
    }

    private static List<Vector2> Resample(List<Vector2> src)
    {
        var res = new List<Vector2> { src[0] };
        var acc = 0f;
        var a = src[0];
        for (var i = 1; i < src.Count; i++)
        {
            var b = src[i];
            var d = Vector2.Distance(a, b);
            while (acc + d >= Step)
            {
                var t = (Step - acc) / d;
                a = Vector2.Lerp(a, b, t);
                res.Add(a);
                d = Vector2.Distance(a, b);
                acc = 0f;
            }
            acc += d;
            a = b;
        }
        if (res[^1] != src[^1])
        {
            res.Add(src[^1]);
        }
        return res;
    }

    private static void ClampCurvature(List<Vector2> pts)
    {
        const int Window = 6;
        for (var pass = 0; pass < 40; pass++)
        {
            var moved = false;
            for (var i = Window; i < pts.Count - Window; i++)
            {
                if (Circumradius(pts[i - Window], pts[i], pts[i + Window]) >= MinTurnRadius)
                {
                    continue;
                }
                pts[i] = Vector2.Lerp(pts[i], (pts[i - 1] + pts[i + 1]) * 0.5f, 0.5f);
                moved = true;
            }
            if (!moved)
            {
                return;
            }
        }
    }

    private static float Circumradius(Vector2 a, Vector2 b, Vector2 c)
    {
        var area2 = MathF.Abs(((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X)));
        if (area2 < 0.000001f)
        {
            return float.MaxValue;
        }
        return Vector2.Distance(a, b) * Vector2.Distance(b, c) * Vector2.Distance(c, a) / (2f * area2);
    }
}
