using System;
using System.Numerics;
using System.Text;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Store;

/// <summary>The store's shared eye-candy: the glossy sweep band, the card shine, the count-up easing and
/// the countdown formatting. Everything motion-gated collapses under reduce-motion at the call sites.</summary>
internal static class StoreFx
{
    private const float SweepPeriod = 5.2f;
    private const float SweepDuration = 1.1f;

    /// <summary>A glossy band that crosses the rect once every few seconds; <paramref name="phase"/>
    /// de-syncs concurrent surfaces so the shop glints rather than strobes.</summary>
    public static void Sweep(ImDrawListPtr dl, Vector2 tl, Vector2 br, float phase,
        bool reduceMotion, float strength = 1f)
    {
        if (reduceMotion)
        {
            return;
        }
        var cycle = (float)((ImGui.GetTime() + phase) % SweepPeriod);
        if (cycle > SweepDuration)
        {
            return;
        }

        var p = cycle / SweepDuration;
        p = 1f - (1f - p) * (1f - p);
        var w = br.X - tl.X;
        var h = br.Y - tl.Y;
        var slant = h * 0.5f;
        var bandW = MathF.Max(Px(18f), w * 0.22f);
        var travel = w + slant + bandW * 2f;
        var x = tl.X - bandW - slant + travel * p;

        dl.PushClipRect(tl, br, true);
        Span<float> alphas = [0.05f, 0.13f, 0.05f];
        for (var i = 0; i < 3; i++)
        {
            var x0 = x + bandW * 0.33f * i;
            dl.AddQuadFilled(
                new Vector2(x0 + slant, tl.Y),
                new Vector2(x0 + slant + bandW * 0.34f, tl.Y),
                new Vector2(x0 + bandW * 0.34f, br.Y),
                new Vector2(x0, br.Y),
                OsDrawShared.White(alphas[i] * strength));
        }
        dl.PopClipRect();
    }

    /// <summary>Cubic ease-out, the house count-up curve.</summary>
    public static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

    /// <summary>Back-out overshoot for pop-in scales.</summary>
    public static float Overshoot(float x)
    {
        var t = x - 1f;
        return 1f + 2.7f * t * t * t + 1.7f * t * t;
    }

    /// <summary>"2d 03:12:44" over a day, "03:12:44" under; empty at or past zero.</summary>
    public static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return string.Empty;
        }
        return remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}d {remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    /// <summary>Stars flying past the viewer behind the storefront. Each one rides a depth value out from
    /// the centre on a fixed bearing, accelerating and growing as it nears the glass, with a short streak
    /// behind it. Drawn in screen space at the top of the body, so it sits under every card and does not
    /// scroll with them. Deliberately faint: atmosphere, never content. Reduce-motion freezes the field
    /// into a still scatter with no streaks.</summary>
    public static void StarField(ImDrawListPtr dl, Vector2 tl, Vector2 size, bool reduceMotion)
    {
        const int count = 30;
        var time = reduceMotion ? 0f : (float)ImGui.GetTime();
        var accent = StorePalette.BlueLight;
        var centre = tl + size * 0.5f;
        // Past the corners, so a star leaves the frame rather than winking out inside it.
        var reach = size.Length() * 0.62f;

        for (var i = 0; i < count; i++)
        {
            // Golden-ratio bearings spread the field without any two sharing a track.
            var angle = Frac(i * 0.6180339887f + 0.13f) * MathF.Tau;
            var speed = 0.035f + Frac(i * 0.377f) * 0.045f;
            var depth = Frac(i * 0.7548776662f + 0.41f + time * speed);
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

            // Squaring the depth is what sells the perspective: slow near the centre, quick at the rim.
            var pos = centre + dir * (depth * depth * reach);
            var starSize = Px(1.6f) + Px(5.4f) * depth * depth;
            // Fade in leaving the centre and out at the rim, so nothing pops in or out mid-flight.
            var alpha = 0.19f * MathF.Min(1f, depth * 5f) * MathF.Min(1f, (1f - depth) * 4.5f);
            var colour = (i % 3) switch
            {
                0 => StoreChips.GoldColor,
                1 => new Vector4(1f, 1f, 1f, 1f),
                _ => accent,
            };

            if (!reduceMotion)
            {
                var trail = MathF.Max(0f, depth - 0.045f);
                dl.AddLine(centre + dir * (trail * trail * reach), pos,
                    ImGui.GetColorU32(colour with { W = alpha * 0.5f }), MathF.Max(1f, starSize * 0.18f));
            }
            IconDraw.AddCentered(dl, FontAwesomeIcon.Star, starSize, pos,
                ImGui.GetColorU32(colour with { W = alpha }));
        }
    }

    private static float Frac(float v) => v - MathF.Floor(v);

    /// <summary>Cursor-layout centered line of text.</summary>
    public static void CenterLine(string text, float winW, Vector4 color)
    {
        ImGui.SetCursorPosX(MathF.Max(0f, (winW - ImGui.CalcTextSize(text).X) * 0.5f));
        ImGui.TextColored(color, text);
    }

    /// <summary>Centred and wrapped. <see cref="CenterLine"/> alone runs a long sentence off the edge, and a
    /// wrap position would left-align every line after the first, so the split happens here and each line is
    /// centred on its own. German and Russian are the ones that need it.</summary>
    public static void CenterWrapped(string text, float winW, Vector4 color, float maxWidth)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split(' '))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && ImGui.CalcTextSize(candidate).X > maxWidth)
            {
                CenterLine(line.ToString(), winW, color);
                line.Clear();
                line.Append(word);
                continue;
            }
            line.Clear();
            line.Append(candidate);
        }
        if (line.Length > 0)
        {
            CenterLine(line.ToString(), winW, color);
        }
    }

    /// <summary>Filled ellipse via a 16-point polygon; the ImGui binding has no ellipse primitive.</summary>
    public static void Ellipse(ImDrawListPtr dl, Vector2 center, Vector2 radii, uint color)
    {
        for (var i = 0; i < 16; i++)
        {
            var a = i * MathF.Tau / 16f;
            dl.PathLineTo(center + new Vector2(MathF.Cos(a) * radii.X, MathF.Sin(a) * radii.Y));
        }
        dl.PathFillConvex(color);
    }

    /// <summary>The product's accent as theme-friendly colors: the card gradient pair and the raw accent.</summary>
    public static (Vector4 Top, Vector4 Bottom, Vector4 Accent) CardColors(uint accentColor)
    {
        var accent = new Vector4(
            ((accentColor >> 16) & 0xFF) / 255f,
            ((accentColor >> 8) & 0xFF) / 255f,
            (accentColor & 0xFF) / 255f,
            1f);
        var top = new Vector4(accent.X * 0.62f, accent.Y * 0.62f, accent.Z * 0.62f, 1f);
        var bottom = new Vector4(accent.X * 0.24f, accent.Y * 0.24f, accent.Z * 0.24f, 1f);
        return (top, bottom, accent);
    }
}
