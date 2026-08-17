using System;
using System.Numerics;
using AetherOS.Apps.Aetherling.Engine;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Rendering;

/// <summary>Renders the dynamic mouth: a <see cref="MouthShape"/> sampled into anti-aliased
/// draw-list strokes at the manifest's mouth anchor. The shapes and the deformer live in the
/// engine; this file only knows how to ink one shape.</summary>
internal static class MouthDraw
{
    /// <summary>Fallback ink, sampled from the adult overlay's own line work; every shipped
    /// manifest declares its own <c>lineColor</c> and this only covers a sheet that does not.</summary>
    internal static readonly Vector4 DefaultLine = new(0.118f, 0.267f, 0.314f, 1f);

    private const float LinePx = 2.2f;

    /// <summary>Closed curves run heavier: a lone thin line vanishes into the face at rest.</summary>
    private const float ClosedLinePx = 2.8f;

    /// <summary>Whole-mouth settle below the measured anchor, 256-space px.</summary>
    private const float RestDrop = 3f;

    private static readonly Vector4 TongueColor = new(0.93f, 0.58f, 0.60f, 1f);

    /// <summary>Inks <paramref name="shape"/> centred on <paramref name="screenAnchor"/>.
    /// <paramref name="scale256"/> converts 256-space px to screen px; <paramref name="poseScale"/>
    /// squashes the mouth with the body; <paramref name="mouthScale"/> is the species multiplier.</summary>
    public static void Draw(
        ImDrawListPtr dl,
        Vector2 screenAnchor,
        float scale256,
        Vector2 poseScale,
        bool flipX,
        in MouthShape shape,
        float mouthScale,
        Vector4 lineColor,
        float alpha)
    {
        const int Samples = 24;
        Span<float> heights = [shape.Y0, shape.Y1, shape.Y2, shape.Y3, shape.Y4];
        var half = shape.Width * mouthScale * 0.5f;
        var sx = scale256 * poseScale.X * (flipX ? -1f : 1f);
        var sy = scale256 * poseScale.Y;

        Vector2 At(float x256, float y256) => new(
            screenAnchor.X + (x256 * sx),
            screenAnchor.Y + (((y256 * mouthScale) + RestDrop) * sy));

        Span<Vector2> top = stackalloc Vector2[Samples + 1];
        for (var s = 0; s <= Samples; s++)
        {
            var u = (float)s / Samples;
            top[s] = At((u - 0.5f) * 2f * half, CatmullRom(heights, u));
        }

        var ink = lineColor.W <= 0f ? DefaultLine : lineColor;
        var line = ImGui.ColorConvertFloat4ToU32(ink with { W = ink.W * alpha });
        var poseThin = scale256 * ((poseScale.X + poseScale.Y) * 0.5f);

        if (shape.Openness > 0.03f)
        {
            // The open interior: a lower lip bellying down from the corners, filled a shade
            // darker than the ink, then the outline stroked around the ring. The path resets
            // after each stroke or fill, so the ring is built twice.
            var fill = ImGui.ColorConvertFloat4ToU32(
                new Vector4(ink.X * 0.55f, ink.Y * 0.55f, ink.Z * 0.6f, ink.W * alpha));
            var drop = shape.Openness * shape.Width * 0.55f;

            Span<float> bottomY = stackalloc float[Samples + 1];
            for (var s = 0; s <= Samples; s++)
            {
                var u = (float)s / Samples;
                bottomY[s] = CatmullRom(heights, u) + (drop * MathF.Sin(MathF.PI * u));
            }

            for (var pass = 0; pass < 2; pass++)
            {
                for (var s = 0; s <= Samples; s++)
                {
                    dl.PathLineTo(top[s]);
                }

                for (var s = Samples; s >= 0; s--)
                {
                    dl.PathLineTo(At((((float)s / Samples) - 0.5f) * 2f * half, bottomY[s]));
                }

                if (pass == 0)
                {
                    dl.PathFillConvex(fill);

                    // The tongue on any visibly open mouth: interior darkest, tongue lighter,
                    // the classic cute-open-mouth read.
                    if (shape.Openness > 0.08f)
                    {
                        var tongueRise = drop * 0.5f;
                        const int TongueSamples = 12;
                        for (var s = 0; s <= TongueSamples; s++)
                        {
                            var v = (float)s / TongueSamples;
                            var u = 0.26f + (0.48f * v);
                            var lip = CatmullRom(heights, u) + (drop * MathF.Sin(MathF.PI * u));
                            dl.PathLineTo(At((u - 0.5f) * 2f * half, lip - (tongueRise * MathF.Sin(MathF.PI * v))));
                        }

                        for (var s = TongueSamples; s >= 0; s--)
                        {
                            var v = (float)s / TongueSamples;
                            var u = 0.26f + (0.48f * v);
                            var lip = CatmullRom(heights, u) + (drop * MathF.Sin(MathF.PI * u));
                            dl.PathLineTo(At((u - 0.5f) * 2f * half, lip));
                        }

                        dl.PathFillConvex(
                            ImGui.ColorConvertFloat4ToU32(TongueColor with { W = TongueColor.W * alpha }));
                    }
                }
                else
                {
                    dl.PathStroke(line, ImDrawFlags.Closed, MathF.Max(1f, LinePx * poseThin));
                }
            }
        }
        else
        {
            for (var s = 0; s <= Samples; s++)
            {
                dl.PathLineTo(top[s]);
            }

            dl.PathStroke(line, ImDrawFlags.None, MathF.Max(1f, ClosedLinePx * poseThin));
        }
    }

    /// <summary>Catmull-Rom through the five heights, ends clamped, sampled at u in [0,1].</summary>
    private static float CatmullRom(ReadOnlySpan<float> pts, float u)
    {
        var f = Math.Clamp(u, 0f, 1f) * (pts.Length - 1);
        var i = Math.Min((int)f, pts.Length - 2);
        var t = f - i;
        var p0 = pts[Math.Max(i - 1, 0)];
        var p1 = pts[i];
        var p2 = pts[i + 1];
        var p3 = pts[Math.Min(i + 2, pts.Length - 1)];
        return 0.5f * ((2f * p1)
                       + ((p2 - p0) * t)
                       + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t * t)
                       + (((3f * p1) - p0 - (3f * p2) + p3) * t * t * t));
    }
}
