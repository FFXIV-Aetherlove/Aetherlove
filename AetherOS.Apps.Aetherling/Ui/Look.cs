using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Ui;

/// <summary>The one place the app's colours and easings live. Everything here is dark on purpose: this app
/// never adopts the phone theme, because it is not supposed to look like it belongs.</summary>
internal static class Look
{
    public static readonly Vector4 Void = new(0.016f, 0.020f, 0.035f, 1f);
    public static readonly Vector4 Crystal = new(0.475f, 0.878f, 0.847f, 1f);
    public static readonly Vector4 CrystalPale = new(0.812f, 0.992f, 0.973f, 1f);
    public static readonly Vector4 Spark = new(0.98f, 0.82f, 0.36f, 1f);
    public static readonly Vector4 Whisper = new(0.72f, 0.78f, 0.86f, 0.55f);

    public static uint U32(Vector4 c) => DrawFx.U32(c);

    public static uint U32(Vector4 c, float alpha) => DrawFx.U32(c with { W = c.W * alpha });

    public static float EaseOut(float t) => 1f - MathF.Pow(1f - Math.Clamp(t, 0f, 1f), 3f);

    public static float EaseInOut(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f ? 4f * t * t * t : 1f - (MathF.Pow((-2f * t) + 2f, 3f) / 2f);
    }

    /// <summary>A slow breath, 0 to 1 to 0, for anything that should look alive without moving.</summary>
    public static float Breathe(double time, float period, float phase = 0f) =>
        0.5f + (0.5f * MathF.Sin((float)(time * (MathF.Tau / period)) + phase));

    /// <summary>Stacked soft discs. The crystal's halo and the void's vignette are both this.</summary>
    public static void Halo(ImDrawListPtr dl, Vector2 centre, float radius, Vector4 colour, float alpha, int rings = 5)
    {
        if (alpha <= 0f)
        {
            return;
        }
        for (var i = rings; i >= 1; i--)
        {
            var r = radius * (i / (float)rings);
            var a = alpha * 0.22f * (1f - ((i - 1) / (float)rings));
            dl.AddCircleFilled(centre, r, U32(colour with { W = a }), 40);
        }
    }

    /// <summary>A field of slow drifting motes. Stateless: every mote is a pure function of its index and the
    /// clock, so the field survives a screen change without carrying anything with it.</summary>
    public static void Motes(
        ImDrawListPtr dl, Vector2 origin, Vector2 size, int count, Vector4 colour, float alpha, double time,
        bool reduceMotion)
    {
        if (alpha <= 0f)
        {
            return;
        }
        var clock = reduceMotion ? 0.0 : time;
        for (var i = 0; i < count; i++)
        {
            var seedX = (float)Fract(i * 0.6180339887);
            var seedY = (float)Fract(i * 0.7548776662);
            var speed = 0.18f + (seedX * 0.35f);
            var y = 1f - (float)Fract(seedY + (clock * speed * 0.06));
            var sway = MathF.Sin((float)(clock * (0.4f + seedY)) + (i * 1.7f)) * size.X * 0.04f;
            var pos = new Vector2(origin.X + (size.X * seedX) + sway, origin.Y + (size.Y * y));
            var twinkle = 0.35f + (0.65f * Breathe(clock, 3.2f + (seedX * 4f), i * 0.9f));
            var radius = 0.7f + (seedY * 1.6f);
            dl.AddCircleFilled(pos, radius * 2f, U32(colour with { W = alpha * twinkle * 0.35f }), 8);
            dl.AddCircleFilled(pos, radius, U32(colour with { W = alpha * twinkle }), 8);
        }
    }

    private static double Fract(double v) => v - Math.Floor(v);

    private static readonly Vector2[] GlowOffsets =
        [new(1f, 0f), new(-1f, 0f), new(0f, 1f), new(0f, -1f)];

    /// <summary>Centred text that glows: a wide halo behind it, then the letters themselves drawn four times
    /// offset and dim under the bright pass. A draw list has no shader, so bleeding the glyphs by hand is the
    /// only way the text itself lights up rather than merely sitting on a lit patch.</summary>
    public static void GlowText(
        ImDrawListPtr dl, string text, float centreX, float y, uint colour, float scale, Vector4 glow,
        float strength)
    {
        var height = ImGui.GetTextLineHeight() * scale;
        var width = ImGui.CalcTextSize(text).X * scale;
        Halo(dl, new Vector2(centreX, y + (height * 0.5f)), MathF.Max(width * 0.62f, height * 2.2f), glow,
            strength * 0.55f);

        var spread = MathF.Max(1f, height * 0.07f);
        var soft = U32(glow with { W = strength * 0.30f });
        foreach (var offset in GlowOffsets)
        {
            Centred(dl, text, centreX + (offset.X * spread), y + (offset.Y * spread), soft, scale);
        }
        Centred(dl, text, centreX, y, colour, scale);
    }

    /// <summary>The pool of light something is standing in. Squashed, because a circle on the floor of a
    /// stage reads as a ball rather than as light.</summary>
    public static void GroundGlow(
        ImDrawListPtr dl, Vector2 centre, float radiusX, float radiusY, Vector4 colour, float alpha,
        int rings = 5)
    {
        if (alpha <= 0f)
        {
            return;
        }
        for (var i = rings; i >= 1; i--)
        {
            var t = i / (float)rings;
            var a = alpha * 0.24f * (1f - ((i - 1) / (float)rings));
            FillEllipse(dl, centre, radiusX * t, radiusY * t, U32(colour with { W = a }));
        }
    }

    /// <summary>A column of light standing up off the floor: brightest up the middle, gone at both edges and
    /// at the top.
    ///
    /// Built from VERTICAL strips, each one a four-corner gradient. Horizontal slices are the obvious way and
    /// the wrong one: a flat-shaded slice has a hard left and right edge and a visible seam against the slice
    /// above it, so the shaft comes out as a banded wedge with sides cut off by a ruler. Strips put the
    /// gradient where the eye actually looks for it, and neighbouring strips share an edge alpha, so the
    /// lateral falloff is continuous however few of them there are.</summary>
    public static void LightShaft(
        ImDrawListPtr dl, Vector2 baseCentre, float width, float height, Vector4 colour, float alpha)
    {
        const int Strips = 32;
        if (alpha <= 0f || height <= 0f || width <= 0f)
        {
            return;
        }

        var half = width * 0.5f;
        var clear = U32(colour with { W = 0f });
        for (var i = 0; i < Strips; i++)
        {
            var uLeft = ((i / (float)Strips) * 2f) - 1f;
            var uRight = (((i + 1) / (float)Strips) * 2f) - 1f;
            var aLeft = alpha * Falloff(uLeft);
            var aRight = alpha * Falloff(uRight);
            if (aLeft <= 0.002f && aRight <= 0.002f)
            {
                continue;
            }

            // The middle reaches highest, so the silhouette is a shaft rather than a wall. The top edge is
            // fully transparent, which is what lets the strips have different heights invisibly.
            var reach = height * (0.55f + (0.45f * Falloff((uLeft + uRight) * 0.5f)));
            dl.AddRectFilledMultiColor(
                new Vector2(baseCentre.X + (half * uLeft), baseCentre.Y - reach),
                new Vector2(baseCentre.X + (half * uRight), baseCentre.Y),
                clear,
                clear,
                U32(colour with { W = aRight }),
                U32(colour with { W = aLeft }));
        }

        static float Falloff(float u)
        {
            var f = 1f - (u * u);
            return f <= 0f ? 0f : f * f;
        }
    }

    /// <summary>Rings spreading out from the feet and fading, evenly spaced in phase so the floor never goes
    /// quiet between them.</summary>
    public static void GroundRipples(
        ImDrawListPtr dl, Vector2 centre, float radiusX, float radiusY, Vector4 colour, float alpha, double time,
        int count = 3, float period = 5.4f)
    {
        for (var i = 0; i < count; i++)
        {
            var t = (float)Fract((time / period) + (i / (float)count));
            var a = alpha * (1f - t);
            if (a <= 0.002f)
            {
                continue;
            }
            var eased = EaseOut(t);
            StrokeEllipse(dl, centre, radiusX * eased, radiusY * eased, U32(colour with { W = a }));
        }
    }

    private const int EllipseSegments = 36;

    private static void FillEllipse(ImDrawListPtr dl, Vector2 centre, float rx, float ry, uint colour)
    {
        PathEllipse(dl, centre, rx, ry);
        dl.PathFillConvex(colour);
    }

    private static void StrokeEllipse(ImDrawListPtr dl, Vector2 centre, float rx, float ry, uint colour)
    {
        PathEllipse(dl, centre, rx, ry);
        dl.PathStroke(colour, ImDrawFlags.Closed, 1.4f);
    }

    private static void PathEllipse(ImDrawListPtr dl, Vector2 centre, float rx, float ry)
    {
        for (var s = 0; s < EllipseSegments; s++)
        {
            var a = MathF.Tau * s / EllipseSegments;
            dl.PathLineTo(new Vector2(centre.X + (MathF.Cos(a) * rx), centre.Y + (MathF.Sin(a) * ry)));
        }
    }

    /// <summary>Text centred on x, drawn straight to the list so it can sit over anything.</summary>
    public static void Centred(ImDrawListPtr dl, string text, float centreX, float y, uint colour, float scale = 1f)
    {
        // Width scales linearly with the drawn size, so measuring at the current font and scaling is exact.
        var size = ImGui.GetFontSize() * scale;
        var width = ImGui.CalcTextSize(text).X * scale;
        dl.AddText(ImGui.GetFont(), size, new Vector2(centreX - (width * 0.5f), y), colour, text);
    }

    /// <summary>Centred and wrapped to a width, measured rather than guessed: the same sentence is half as
    /// long again in German, and this line is one a player has to be able to read. Returns the rows drawn, so
    /// whatever sits under it can be placed against the real height.</summary>
    public static int CentredWrapped(
        ImDrawListPtr dl, string text, float centreX, float y, float maxWidth, uint colour, float scale)
    {
        var lineStep = LineStep(scale);
        var rows = WrapLines(text, maxWidth, scale);
        for (var row = 0; row < rows.Count; row++)
        {
            Centred(dl, rows[row], centreX, y + (row * lineStep), colour, scale);
        }
        return rows.Count;
    }

    /// <summary>How tall the same call would draw, for anything that has to size a box around it first.</summary>
    public static float WrappedHeight(string text, float maxWidth, float scale) =>
        WrapLines(text, maxWidth, scale).Count * LineStep(scale);

    public static float LineStep(float scale) => ImGui.GetTextLineHeight() * scale * 1.25f;

    private static List<string> WrapLines(string text, float maxWidth, float scale)
    {
        var rows = new List<string>();
        var line = new StringBuilder();
        foreach (var paragraph in text.Split('\n'))
        {
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && ImGui.CalcTextSize(candidate).X * scale > maxWidth)
                {
                    rows.Add(line.ToString());
                    line.Clear();
                    line.Append(word);
                    continue;
                }
                line.Clear();
                line.Append(candidate);
            }
            if (line.Length > 0)
            {
                rows.Add(line.ToString());
                line.Clear();
            }
        }
        return rows;
    }

    /// <summary>A rounded chip around a short piece of text, centred on x. Returns its height.</summary>
    public static float Pill(
        ImDrawListPtr dl, string text, float centreX, float y, Vector4 colour, float alpha, float scale = 1f)
    {
        var textW = ImGui.CalcTextSize(text).X * scale;
        var textH = ImGui.GetTextLineHeight() * scale;
        var padX = textH * 0.72f;
        var padY = textH * 0.28f;
        var tl = new Vector2(centreX - (textW * 0.5f) - padX, y);
        var br = new Vector2(centreX + (textW * 0.5f) + padX, y + textH + (padY * 2f));
        var radius = (br.Y - tl.Y) * 0.5f;
        dl.AddRectFilled(tl, br, U32(colour with { W = 0.14f * alpha }), radius);
        dl.AddRect(tl, br, U32(colour with { W = 0.45f * alpha }), radius, ImDrawFlags.None, 1.2f);
        Centred(dl, text, centreX, y + padY, U32(colour with { W = 0.95f * alpha }), scale);
        return br.Y - tl.Y;
    }

    /// <summary>A highlight band sweeping across a line, drawn by redrawing the same text clipped to the band.
    /// It rests for the back half of each period so it reads as an occasional glint, not a strobe.</summary>
    public static void Shine(
        ImDrawListPtr dl, string text, float centreX, float y, float scale, uint baseColour, uint shineColour,
        double time, float period = 4.2f)
    {
        Centred(dl, text, centreX, y, baseColour, scale);

        var cycle = (float)((time % period) / period);
        if (cycle > 0.5f)
        {
            return;
        }
        var width = ImGui.CalcTextSize(text).X * scale;
        var height = ImGui.GetTextLineHeight() * scale;
        var band = MathF.Max(width * 0.26f, 24f);
        var left = centreX - (width * 0.5f);
        var x = left - band + ((cycle / 0.5f) * (width + (band * 2f)));
        dl.PushClipRect(new Vector2(x, y - height), new Vector2(x + band, y + (height * 2f)), true);
        Centred(dl, text, centreX, y, shineColour, scale);
        dl.PopClipRect();
    }

    /// <summary>One soft bump per period, settled for the rest of it. Drives the tease line's push.</summary>
    public static float Push(double time, float period = 3.4f)
    {
        var phase = (float)(time % period);
        const float Rise = 0.55f;
        return phase >= Rise ? 0f : MathF.Sin((phase / Rise) * MathF.PI) * MathF.Exp(-phase * 3.2f);
    }

    /// <summary>Multi-line variant: the garbled blocks arrive pre-wrapped.</summary>
    public static void CentredBlock(
        ImDrawListPtr dl, string text, float centreX, float y, uint colour, float scale, float lineStep)
    {
        var line = 0;
        foreach (var row in text.Split('\n'))
        {
            Centred(dl, row, centreX, y + (line * lineStep), colour, scale);
            line++;
        }
    }
}
