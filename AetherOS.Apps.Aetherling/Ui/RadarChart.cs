using System;
using System.Numerics;
using AetherOS.PetKit.Engine;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Ui;

/// <summary>The hexagon of the six elements: lifetime crystals eaten per element, the one place the
/// diet is allowed to be numbers.
///
/// <para>The scale is RELATIVE: the outer ring is whatever the biggest count currently is, so the
/// shape is readable from the very first crystal and keeps its shape as the numbers climb. An
/// absolute scale spent the first weeks of a pet's life as a dot in the middle. Every axis carries
/// its own count, because a relative chart says nothing about size on its own.</para></summary>
internal static class RadarChart
{
    private const int Axes = 6;
    private const int Rings = 3;

    /// <summary>Draws the chart centred at <paramref name="centre"/>. <paramref name="reveal"/>
    /// sweeps the fill in on first show; pass 1 under reduce motion. <paramref name="turnThreshold"/>
    /// is marked as a gold ring once anything has actually reached it.</summary>
    public static void Draw(
        OsAppContext ctx, ImDrawListPtr dl, Vector2 centre, float radius, int[] counts, int turnThreshold, float reveal)
    {
        var biggest = 0;
        foreach (var count in counts)
        {
            biggest = Math.Max(biggest, count);
        }
        var scale = MathF.Max(1f, biggest);

        for (var ring = 1; ring <= Rings; ring++)
        {
            var r = radius * ring / Rings;
            for (var i = 0; i <= Axes; i++)
            {
                dl.PathLineTo(centre + (AxisDir(i) * r));
            }
            dl.PathStroke(Look.U32(new Vector4(1f, 1f, 1f, ring == Rings ? 0.20f : 0.09f)),
                ImDrawFlags.Closed, Px(1f));
        }
        for (var i = 0; i < Axes; i++)
        {
            dl.AddLine(centre, centre + (AxisDir(i) * radius), Look.U32(new Vector4(1f, 1f, 1f, 0.08f)), Px(1f));
        }

        // The turn, marked only once something has reached it: a ring drawn past the edge of its own
        // chart would be a promise about a scale that is not on screen.
        if (turnThreshold > 0 && turnThreshold <= biggest)
        {
            var r = radius * turnThreshold / scale;
            for (var i = 0; i <= Axes; i++)
            {
                dl.PathLineTo(centre + (AxisDir(i) * r));
            }
            dl.PathStroke(Look.U32(Look.Spark, 0.35f), ImDrawFlags.Closed, Px(1.2f));
        }

        // The shape. A floor keeps a zero ledger visible as a small seed rather than nothing.
        Span<Vector2> points = stackalloc Vector2[Axes];
        for (var i = 0; i < Axes; i++)
        {
            var value = i < counts.Length ? counts[i] : 0;
            var fraction = MathF.Max(0.05f, value / scale) * Math.Clamp(reveal, 0f, 1f);
            points[i] = centre + (AxisDir(i) * (radius * fraction));
        }
        for (var i = 0; i < Axes; i++)
        {
            dl.PathLineTo(points[i]);
        }
        dl.PathFillConvex(Look.U32(Look.Crystal with { W = 0.22f }));
        for (var i = 0; i <= Axes; i++)
        {
            dl.PathLineTo(points[i % Axes]);
        }
        dl.PathStroke(Look.U32(Look.Crystal with { W = 0.75f }), ImDrawFlags.Closed, Px(1.6f));

        // A dot per axis, and a label carrying its own count in its element's colour.
        for (var i = 0; i < Axes; i++)
        {
            var element = Elements.All[i];
            var value = i < counts.Length ? counts[i] : 0;
            dl.AddCircleFilled(points[i], Px(3f), Look.U32(element.Accent, 0.95f), 10);

            var labelAt = centre + (AxisDir(i) * (radius + Px(16f)));
            var label = ctx.Localize(Elements.NameKey(element));
            var extent = ImGui.CalcTextSize(label) * 0.78f;
            var top = labelAt.Y - extent.Y;
            Look.Centred(dl, label, labelAt.X, top, Look.U32(element.Accent, 0.85f), 0.78f);
            Look.Centred(dl, value.ToString(ctx.Culture), labelAt.X, top + Look.LineStep(0.78f),
                Look.U32(value > 0 ? element.Accent : Look.Whisper, value > 0 ? 1f : 0.6f), 0.86f);
        }
    }

    /// <summary>Axis i's unit direction, fire at the top and the rest clockwise.</summary>
    private static Vector2 AxisDir(int i)
    {
        var angle = (-MathF.PI / 2f) + (MathF.Tau * (i % Axes) / Axes);
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }
}
