namespace AetherOS.Apps.Racer.Rendering;

using System;
using System.Collections.Generic;
using System.Numerics;

using AetherLove.Shared.Racing;

/// <summary>Cached drawing cues derived from the course's grade: bank faces beside the road and
/// light strips on it. Renderer-independent and built once per race. The integrated elevation is a
/// display baseline that chooses a face width and nothing else: the road, the runners' feet and the
/// camera are never displaced, and no track array is touched.</summary>
internal static class RaceTerrainRelief
{
    public const int MaxPanels = 280;
    public const float SampleStep = 4f;
    public const float RailGap = 0.65f;
    public const float MaxFaceWidth = 1.8f;
    public const float EndTaperLength = 12f;

    /// <summary>The seam every panel shares, as a fraction of its face from the rail outward, so it
    /// runs continuous across shared edges and down the tapered toes.</summary>
    public const float SeamFrac = 0.48f;

    private const float FlatSpan = 0.08f;
    private const float ShadeGradeFloor = 0.006f;
    private const float MinShoulder = 0.23f;
    private const float MinFaceWidth = 0.12f;
    private const float DeckExcluded = 0.05f;
    private const float UnderExcluded = 0.01f;
    private const float ConvexEpsilon = 0.00001f;

    /// <summary>One bank face: A and B on the rail side, C and D on the outer side. <see cref="Height"/>
    /// is the larger endpoint face width after the taper, kept for drawing thresholds only.</summary>
    public readonly record struct Panel(Vector2 A, Vector2 B, Vector2 C, Vector2 D,
        float S, float Grade, float Height, bool Cut, int Side, uint Detail);

    public readonly record struct RoadShade(Vector2 LeftA, Vector2 LeftB, Vector2 RightB, Vector2 RightA,
        float Grade, float S);

    public sealed record Profile(Panel[] Panels, RoadShade[] RoadShades, float MinElevation, float MaxElevation)
    {
        public static readonly Profile Empty = new([], [], 0f, 0f);
    }

    /// <summary>Courses that read as a canyon: the cap sits on the outer side of the bank and the
    /// face is widest on the valley floor.</summary>
    public static bool HasCanyon(string key) => key is "stone-ladder" or "hollow-way" or "long-burn" or "glassway";

    /// <summary>Builds the profile. <paramref name="roadClear"/> answers whether a world disc is clear
    /// of every road branch, decks included; a face is kept only when its whole footprint is.</summary>
    public static Profile Build(AetherRaceLive.Track track, string courseKey, Func<Vector2, float, bool> roadClear)
    {
        var span = track.LapRows > 0 ? track.LapLength : track.Length;
        var count = (int)MathF.Ceiling(span / SampleStep) + 1;
        var samples = new AetherRaceLive.TrackSample[count];
        var elevation = new float[count];
        for (var i = 0; i < count; i++)
        {
            var s = MathF.Min(span, i * SampleStep);
            samples[i] = track.AtLerp(s);
            if (i > 0)
            {
                var ds = s - ((i - 1) * SampleStep);
                elevation[i] = elevation[i - 1] + ((samples[i - 1].Grade + samples[i].Grade) * 0.5f * ds);
            }
        }

        // A loop's bank must meet itself at the lap seam even when the authored grade integral
        // carries a residual.
        var residual = track.LapRows > 0 ? elevation[^1] : 0f;
        var low = 0f;
        var high = 0f;
        for (var i = 0; i < count; i++)
        {
            elevation[i] -= residual * MathF.Min(span, i * SampleStep) / span;
            low = MathF.Min(low, elevation[i]);
            high = MathF.Max(high, elevation[i]);
        }

        var canyon = HasCanyon(courseKey);
        if (high - low < FlatSpan && !canyon)
        {
            return new Profile([], [], low, high);
        }

        var panels = new List<Panel>(Math.Min(MaxPanels, count * 2));
        var shades = new List<RoadShade>(count);
        for (var i = 1; i < count; i++)
        {
            var sa = (i - 1) * SampleStep;
            var sb = MathF.Min(span, i * SampleStep);
            if (sa < RaceDressing.TapeClear || sb > span - RaceDressing.TapeClear)
            {
                continue;
            }

            var a = samples[i - 1];
            var b = samples[i];
            var da = track.DeckAt(sa);
            var db = track.DeckAt(sb);
            if (da.Deck > DeckExcluded || db.Deck > DeckExcluded || da.Under > UnderExcluded || db.Under > UnderExcluded)
            {
                continue;
            }

            var pa = new Vector2(a.X, a.Y);
            var pb = new Vector2(b.X, b.Y);
            var na = new Vector2(-MathF.Sin(a.Heading), MathF.Cos(a.Heading));
            var nb = new Vector2(-MathF.Sin(b.Heading), MathF.Cos(b.Heading));
            var grade = (a.Grade + b.Grade) * 0.5f;
            if (MathF.Abs(grade) > ShadeGradeFloor)
            {
                var shade = new RoadShade(pa + (na * a.Width * 0.5f), pb + (nb * b.Width * 0.5f),
                    pb - (nb * b.Width * 0.5f), pa - (na * a.Width * 0.5f), grade, sa);
                if (Convex(shade.LeftA, shade.LeftB, shade.RightB, shade.RightA))
                {
                    shades.Add(shade);
                }
            }

            // Grade strips keep coming after the face budget is spent.
            if (panels.Count >= MaxPanels)
            {
                continue;
            }

            var heightA = FaceWidth(elevation[i - 1], low, high, canyon);
            var heightB = FaceWidth(elevation[i], low, high, canyon);
            var height = MathF.Max(heightA, heightB);
            if (!canyon && height < MinShoulder)
            {
                continue;
            }

            for (var side = -1; side <= 1 && panels.Count < MaxPanels; side += 2)
            {
                var ia = pa + (na * (side * ((a.Width * 0.5f) + RailGap)));
                var ib = pb + (nb * (side * ((b.Width * 0.5f) + RailGap)));
                var oa = ia + (na * (side * heightA));
                var ob = ib + (nb * (side * heightB));
                var panel = new Panel(ia, ib, ob, oa, sa, grade, height, canyon, side,
                    unchecked((uint)(i * 2654435761u) ^ (side < 0 ? 0x8da6b343u : 0xd8163841u)));
                if (Convex(in panel) && Clear(in panel, roadClear))
                {
                    panels.Add(panel);
                }
            }
        }

        TaperEnds(panels);
        return new Profile([.. panels], [.. shades], low, high);
    }

    /// <summary>Runs are found after clearance and capacity decisions, per side, so an ending made by
    /// a crossing branch or the face budget tapers like the course's own ends. Shrinking stays inside
    /// the footprint that was checked; the zero-width endpoints are drawn as triangles.</summary>
    private static void TaperEnds(List<Panel> panels)
    {
        var sideIndices = new List<int>(panels.Count);
        for (var side = -1; side <= 1; side += 2)
        {
            sideIndices.Clear();
            for (var i = 0; i < panels.Count; i++)
            {
                if (panels[i].Side == side)
                {
                    sideIndices.Add(i);
                }
            }

            for (var start = 0; start < sideIndices.Count;)
            {
                var end = start;
                while (end + 1 < sideIndices.Count
                    && panels[sideIndices[end + 1]].S == panels[sideIndices[end]].S + SampleStep)
                {
                    end++;
                }

                if (start == end)
                {
                    // A lone four-bound wall cannot rise and fall without more geometry.
                    var index = sideIndices[start];
                    panels[index] = panels[index] with { Height = 0f };
                }
                else
                {
                    var beginS = panels[sideIndices[start]].S;
                    var endS = panels[sideIndices[end]].S + SampleStep;
                    var length = MathF.Min(EndTaperLength, (endS - beginS) * 0.5f);
                    for (var i = start; i <= end; i++)
                    {
                        var index = sideIndices[i];
                        var panel = panels[index];
                        var ta = EndTaper(panel.S, beginS, endS, length);
                        var tb = EndTaper(panel.S + SampleStep, beginS, endS, length);
                        var outerA = Vector2.Lerp(panel.A, panel.D, ta);
                        var outerB = Vector2.Lerp(panel.B, panel.C, tb);
                        panels[index] = panel with
                        {
                            C = outerB,
                            D = outerA,
                            Height = MathF.Max(Vector2.Distance(panel.A, outerA), Vector2.Distance(panel.B, outerB)),
                        };
                    }
                }

                start = end + 1;
            }
        }

        panels.RemoveAll(static panel => panel.Height <= 0f);
    }

    private static float EndTaper(float s, float begin, float end, float length)
    {
        var t = Math.Clamp(MathF.Min(s - begin, end - s) / length, 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static float FaceWidth(float elevation, float low, float high, bool canyon)
    {
        var relative = (elevation - low) / MathF.Max(0.1f, high - low);
        var width = canyon ? 0.46f + ((1f - relative) * 1.1f) : 0.12f + (relative * 0.78f);
        return Math.Clamp(width, MinFaceWidth, MaxFaceWidth);
    }

    private static bool Convex(in Panel p) => Convex(p.A, p.B, p.C, p.D);

    /// <summary>ImGui's filled quad needs a convex, consistently wound face; tight or folded offset
    /// geometry is skipped rather than sent to it.</summary>
    private static bool Convex(Vector2 pa, Vector2 pb, Vector2 pc, Vector2 pd)
    {
        var a = Cross(pb - pa, pc - pb);
        var b = Cross(pc - pb, pd - pc);
        var c = Cross(pd - pc, pa - pd);
        var d = Cross(pa - pd, pb - pa);
        if (!float.IsFinite(a) || !float.IsFinite(b) || !float.IsFinite(c) || !float.IsFinite(d))
        {
            return false;
        }

        return (a > ConvexEpsilon && b > ConvexEpsilon && c > ConvexEpsilon && d > ConvexEpsilon)
            || (a < -ConvexEpsilon && b < -ConvexEpsilon && c < -ConvexEpsilon && d < -ConvexEpsilon);
    }

    private static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);

    /// <summary>Overlapping discs cover the entire face, so every cap and seam drawn inside it is
    /// clear too. The radius is a sum rather than a diagonal so a skewed corner quad is covered.</summary>
    private static bool Clear(in Panel panel, Func<Vector2, float, bool> clear)
    {
        var along = MathF.Max(Vector2.Distance(panel.A, panel.B), Vector2.Distance(panel.D, panel.C));
        var across = MathF.Max(Vector2.Distance(panel.A, panel.D), Vector2.Distance(panel.B, panel.C));
        var radius = (along / 16f) + (across / 8f) + 0.025f;
        for (var i = 0; i <= 8; i++)
        {
            var inner = Vector2.Lerp(panel.A, panel.B, i / 8f);
            var outer = Vector2.Lerp(panel.D, panel.C, i / 8f);
            for (var j = 0; j <= 4; j++)
            {
                if (!clear(Vector2.Lerp(inner, outer, j / 4f), radius))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
