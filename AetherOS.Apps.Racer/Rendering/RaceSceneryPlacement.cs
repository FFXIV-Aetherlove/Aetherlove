namespace AetherOS.Apps.Racer.Rendering;

using System;
using System.Numerics;

using AetherLove.Shared.Racing;

/// <summary>One-time prop clearance against the final tapered banks. Built after the relief exists
/// and asked once per instance at race creation; no simulation or random state, nothing per frame.</summary>
internal sealed class RaceSceneryPlacement
{
    public const int RelocationAttempts = 3;
    public const float RelocationStep = 0.65f;

    private readonly Face[] _faces;

    private readonly record struct Face(RaceTerrainRelief.Panel Panel, Vector2 Min, Vector2 Max);

    public RaceSceneryPlacement(RaceTerrainRelief.Profile relief)
    {
        _faces = new Face[relief.Panels.Length];
        for (var i = 0; i < _faces.Length; i++)
        {
            var p = relief.Panels[i];
            _faces[i] = new Face(p, Vector2.Min(Vector2.Min(p.A, p.B), Vector2.Min(p.C, p.D)),
                Vector2.Max(Vector2.Max(p.A, p.B), Vector2.Max(p.C, p.D)));
        }
    }

    /// <summary>Is a world disc clear of every bank face? The disc holds an upright prop at every
    /// camera rotation, its height included, so a small object wholly inside a face fails too.</summary>
    public bool Clear(Vector2 centre, float radius)
    {
        if (!float.IsFinite(centre.X) || !float.IsFinite(centre.Y) || !float.IsFinite(radius) || radius < 0f)
        {
            return false;
        }

        var radiusSquared = radius * radius;
        foreach (var face in _faces)
        {
            if (centre.X + radius < face.Min.X || centre.X - radius > face.Max.X
                || centre.Y + radius < face.Min.Y || centre.Y - radius > face.Max.Y)
            {
                continue;
            }

            var p = face.Panel;
            if (Inside(centre, p.A, p.B, p.C) || Inside(centre, p.A, p.C, p.D)
                || SegmentDistanceSquared(centre, p.A, p.B) <= radiusSquared
                || SegmentDistanceSquared(centre, p.B, p.C) <= radiusSquared
                || SegmentDistanceSquared(centre, p.C, p.D) <= radiusSquared
                || SegmentDistanceSquared(centre, p.D, p.A) <= radiusSquared)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Keeps the original anchor when it clears the banks and every road branch, else tries
    /// three fixed steps outward along the nearest road segment's normal, each still within
    /// <paramref name="reach"/> of that segment's edge. False means the instance is omitted.</summary>
    public bool TryPlace(Vector2 original, float radius, AetherRaceLive.Track track, float reach,
        Func<Vector2, float, bool> roadClear, out Vector2 placed)
    {
        placed = original;
        if (Clear(original, radius) && roadClear(original, radius))
        {
            return true;
        }

        var rows = track.LapRows > 0 ? track.LapRows : track.Count;
        var segments = rows - (track.LapRows > 0 ? 0 : 1);
        var best = float.PositiveInfinity;
        var outward = Vector2.Zero;
        var limit = 0f;
        for (var i = 0; i < segments; i++)
        {
            var j = (i + 1) % rows;
            var a = new Vector2(track.Xs[i], track.Ys[i]);
            var b = new Vector2(track.Xs[j], track.Ys[j]);
            var edge = b - a;
            var lengthSquared = edge.LengthSquared();
            if (lengthSquared <= 0f)
            {
                continue;
            }

            var t = Math.Clamp(Vector2.Dot(original - a, edge) / lengthSquared, 0f, 1f);
            var delta = original - (a + (t * edge));
            var distanceSquared = delta.LengthSquared();
            if (distanceSquared >= best)
            {
                continue;
            }

            best = distanceSquared;
            var normal = new Vector2(-edge.Y, edge.X) / MathF.Sqrt(lengthSquared);
            outward = normal * (Vector2.Dot(delta, normal) < 0f ? -1f : 1f);
            var halfWidth = (track.Ws[i] + ((track.Ws[j] - track.Ws[i]) * t)) * 0.5f;
            limit = halfWidth + reach - radius - Vector2.Dot(delta, outward);
        }

        for (var attempt = 1; attempt <= RelocationAttempts; attempt++)
        {
            var distance = attempt * RelocationStep;
            if (distance > limit)
            {
                break;
            }

            var candidate = original + (outward * distance);
            if (!Clear(candidate, radius) || !roadClear(candidate, radius))
            {
                continue;
            }

            placed = candidate;
            return true;
        }

        return false;
    }

    private static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);

    private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        // A taper tip's zero-area triangle must not claim its whole edge line.
        if (MathF.Abs(Cross(b - a, c - a)) < 0.00000001f)
        {
            return false;
        }

        var u = Cross(b - a, p - a);
        var v = Cross(c - b, p - b);
        var w = Cross(a - c, p - c);
        return (u >= 0f && v >= 0f && w >= 0f) || (u <= 0f && v <= 0f && w <= 0f);
    }

    private static float SegmentDistanceSquared(Vector2 p, Vector2 a, Vector2 b)
    {
        var edge = b - a;
        var length2 = edge.LengthSquared();
        var t = length2 > 0f ? Math.Clamp(Vector2.Dot(p - a, edge) / length2, 0f, 1f) : 0f;
        return Vector2.DistanceSquared(p, a + (t * edge));
    }
}
