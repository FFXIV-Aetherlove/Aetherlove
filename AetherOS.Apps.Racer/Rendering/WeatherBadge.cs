using System;
using System.Numerics;
using AetherLove.Shared.Racing;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Racer.Rendering;

/// <summary>The sky a race runs under, as a small drawn chip for the offer card: a dark disc with one
/// mark on it, inked in the weather's own element colour.
///
/// <para>Drawn rather than shipped as art, the same choice the flags and the creature's own outline
/// made: seven marks that scale with the phone, tint themselves from <see cref="ElementFx"/>, and add
/// nothing to the media folder to keep in sync.</para></summary>
internal static class WeatherBadge
{
    /// <summary>The element whose colour a sky wears. Clear belongs to none and stays neutral.</summary>
    private static string ElementOf(string weatherKey) =>
        AetherRaceLive.Weathers.TryGetValue(weatherKey, out var w) ? w.Element : string.Empty;

    /// <summary>The badge reads the standalone sky label the offer card already owned, rather than a
    /// second set of names for the same seven skies. The mid-strip form the parade prints
    /// (`os.racer_wx_*`) stays its own, since that one carries articles.</summary>
    public static string NameKey(string weatherKey) => $"os.racer_sky_{weatherKey}";

    public static string TipKey(string weatherKey) => $"os.racer_sky_{weatherKey}_tip";

    /// <summary>Draws the chip centred on <paramref name="centre"/>, <paramref name="side"/> across.</summary>
    public static void Draw(ImDrawListPtr dl, string weatherKey, Vector2 centre, float side, float dim = 1f)
    {
        var element = ElementOf(weatherKey);
        var tint = element.Length == 0
            ? new Vector4(0.81f, 0.83f, 0.89f, 1f)
            : ElementFx.For(element).Tint;
        tint.W = dim;

        var radius = side * 0.5f;
        dl.AddCircleFilled(centre, radius, ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.06f, 0.09f, 0.72f * dim)), 32);
        dl.AddCircle(centre, radius, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.16f * dim)), 32, MathF.Max(1f, side * 0.02f));

        var ink = ImGui.ColorConvertFloat4ToU32(tint);
        var weight = MathF.Max(1.1f, side * 0.068f);

        // The marks are authored in the same 80-unit box the preview was drawn in, centred on 40,40, and
        // land inside the disc rather than on it: a mark touching the ring reads as clipped.
        const float Inset = 0.78f;
        Vector2 P(float x, float y) =>
            centre + new Vector2((x - 40f) / 80f * side * Inset, (y - 40f) / 80f * side * Inset);

        switch (weatherKey)
        {
            case "rain":
                Cloud(dl, P, ink, weight);
                Streak(dl, P, ink, weight, 28f);
                Streak(dl, P, ink, weight, 42f);
                Streak(dl, P, ink, weight, 56f);
                break;
            case "snowfall":
                Flake(dl, P, ink, weight);
                break;
            case "gale":
                Gust(dl, P, ink, weight, 30f, 46f, -1f);
                Gust(dl, P, ink, weight, 44f, 54f, 1f);
                dl.AddLine(P(16f, 58f), P(36f, 58f), ink, weight);
                break;
            case "haze":
                Shimmer(dl, P, ink, weight, 30f);
                Shimmer(dl, P, ink, weight, 44f);
                Shimmer(dl, P, ink, weight, 58f);
                break;
            case "static":
                Bolt(dl, P, ink);
                break;
            case "dustveil":
                Swirl(dl, P, ink, weight);
                break;
            default:
                Sun(dl, P, ink, weight);
                break;
        }
    }

    private static void Sun(ImDrawListPtr dl, Func<float, float, Vector2> p, uint ink, float weight)
    {
        dl.AddCircle(p(40f, 40f), (p(53f, 40f) - p(40f, 40f)).X, ink, 24, weight);
        dl.AddLine(p(40f, 14f), p(40f, 7f), ink, weight);
        dl.AddLine(p(40f, 73f), p(40f, 80f), ink, weight);
        dl.AddLine(p(14f, 40f), p(7f, 40f), ink, weight);
        dl.AddLine(p(73f, 40f), p(80f, 40f), ink, weight);
        dl.AddLine(p(22f, 22f), p(17f, 17f), ink, weight);
        dl.AddLine(p(58f, 58f), p(63f, 63f), ink, weight);
        dl.AddLine(p(58f, 22f), p(63f, 17f), ink, weight);
        dl.AddLine(p(22f, 58f), p(17f, 63f), ink, weight);
    }

    /// <summary>Three bumps and a floor: a cloud read at chip size is its silhouette, not its detail.</summary>
    private static void Cloud(ImDrawListPtr dl, Func<float, float, Vector2> p, uint ink, float weight)
    {
        var left = p(27f, 34f);
        var mid = p(41f, 28f);
        var right = p(54f, 36f);
        dl.PathArcTo(left, (p(35f, 34f) - left).X, MathF.PI, MathF.Tau, 12);
        dl.PathArcTo(mid, (p(51f, 28f) - mid).X, MathF.PI * 1.05f, MathF.Tau * 0.98f, 14);
        dl.PathArcTo(right, (p(62f, 36f) - right).X, MathF.PI * 1.1f, MathF.Tau, 12);
        dl.PathLineTo(p(22f, 44f));
        dl.PathLineTo(p(20f, 34f));
        dl.PathStroke(ink, ImDrawFlags.None, weight);
    }

    private static void Streak(ImDrawListPtr dl, Func<float, float, Vector2> p, uint ink, float weight, float x)
    {
        dl.AddLine(p(x, 52f), p(x - 5f, 66f), ink, weight);
    }

    /// <summary>Eight spokes out of one point, nothing else: a flake with arms on its arms turns to mush
    /// at chip size.</summary>
    private static void Flake(ImDrawListPtr dl, Func<float, float, Vector2> p, uint ink, float weight)
    {
        const float Reach = 26f;
        for (var i = 0; i < 8; i++)
        {
            var a = MathF.Tau * i / 8f;
            var dx = MathF.Cos(a) * Reach;
            var dy = MathF.Sin(a) * Reach;
            dl.AddLine(p(40f, 40f), p(40f + dx, 40f + dy), ink, weight);
        }
    }

    /// <summary>A gust: a straight run of wind that curls back on itself at the end.</summary>
    private static void Gust(ImDrawListPtr dl, Func<float, float, Vector2> p, uint ink, float weight,
        float y, float endX, float curl)
    {
        var hook = p(endX, y + (4f * curl));
        var r = MathF.Abs((p(endX + 4f, y) - hook).X);
        dl.PathLineTo(p(16f, y));
        dl.PathLineTo(p(endX, y));
        dl.PathArcTo(hook, r, curl < 0f ? MathF.PI * 0.5f : -MathF.PI * 0.5f,
            curl < 0f ? -MathF.PI * 0.9f : MathF.PI * 0.9f, 16);
        dl.PathStroke(ink, ImDrawFlags.None, weight);
    }

    /// <summary>One shimmer line: heat seen over a hot road, two humps of it.</summary>
    private static void Shimmer(ImDrawListPtr dl, Func<float, float, Vector2> p, uint ink, float weight, float y)
    {
        dl.PathLineTo(p(18f, y));
        dl.PathBezierCubicCurveTo(p(24f, y - 7f), p(30f, y - 7f), p(36f, y), 12);
        dl.PathBezierCubicCurveTo(p(42f, y + 7f), p(48f, y + 7f), p(54f, y), 12);
        dl.PathBezierCubicCurveTo(p(58f, y - 4f), p(62f, y - 5f), p(66f, y - 3f), 10);
        dl.PathStroke(ink, ImDrawFlags.None, weight);
    }

    private static void Bolt(ImDrawListPtr dl, Func<float, float, Vector2> p, uint ink)
    {
        dl.PathLineTo(p(46f, 12f));
        dl.PathLineTo(p(24f, 44f));
        dl.PathLineTo(p(38f, 44f));
        dl.PathLineTo(p(30f, 68f));
        dl.PathLineTo(p(56f, 34f));
        dl.PathLineTo(p(41f, 34f));
        dl.PathFillConvex(ink);
    }

    /// <summary>Dust: a curl of it lifting off the floor, with three specks still hanging.</summary>
    private static void Swirl(ImDrawListPtr dl, Func<float, float, Vector2> p, uint ink, float weight)
    {
        var c = p(38f, 40f);
        var outer = MathF.Abs((p(54f, 40f) - c).X);
        dl.PathArcTo(c, outer, MathF.PI * 0.15f, MathF.PI * 1.55f, 22);
        dl.PathStroke(ink, ImDrawFlags.None, weight);

        var inner = MathF.Abs((p(46f, 40f) - c).X);
        dl.PathArcTo(c, inner, MathF.PI * 1.35f, MathF.PI * 0.25f, 18);
        dl.PathStroke(ink, ImDrawFlags.None, weight * 0.85f);

        dl.AddLine(p(20f, 62f), p(44f, 62f), ink, weight);
        var speck = MathF.Max(1f, weight * 0.5f);
        dl.AddCircleFilled(p(60f, 28f), speck, ink, 8);
        dl.AddCircleFilled(p(64f, 46f), speck, ink, 8);
        dl.AddCircleFilled(p(54f, 62f), speck, ink, 8);
    }
}
