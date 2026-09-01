using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Shared.Store;
using Dalamud.Bindings.ImGui;

namespace AetherLove.UI;

/// <summary>
/// The four special effects a boosted listing wears, drawn over a card the caller has already filled.
/// Shared because the Places browse, the Levemetes board and the Store's style picker all draw the same
/// four things, and a boost that looked different in the picker than on the listing would be a lie.
/// <para>Every effect degrades to a still rendition under <see cref="AccessibilityService.ReduceMotion"/>:
/// the same shapes, frozen at a flattering phase, so the card still reads as boosted.</para>
/// </summary>
public static class BoostFx
{
    /// <summary>Draw over a card whose body is already submitted. Nothing is drawn for an unknown style,
    /// so an old client meeting a style it does not have simply shows a plain card.</summary>
    public static void Draw(
        ImDrawListPtr dl, Vector2 tl, Vector2 br, float rounding, BoostStyle style, float intensity = 1f)
    {
        if (br.X - tl.X < 4f || br.Y - tl.Y < 4f)
        {
            return;
        }
        var t = AccessibilityService.ReduceMotion ? 3.1f : (float)ImGui.GetTime();
        var a = Math.Clamp(intensity, 0f, 1f);
        switch (style)
        {
            case BoostStyle.Aurora:
                DrawAurora(dl, tl, br, rounding, t, a);
                break;
            case BoostStyle.Ember:
                DrawEmber(dl, tl, br, rounding, t, a);
                break;
            case BoostStyle.Prism:
                DrawPrism(dl, tl, br, rounding, t, a);
                break;
            case BoostStyle.Starlight:
                DrawStarlight(dl, tl, br, rounding, t, a);
                break;
        }
    }

    /// <summary>The colour a style is recognised by: the picker's tile tint and the "Boosted" pill.</summary>
    public static Vector4 KeyColor(BoostStyle style) => style switch
    {
        BoostStyle.Ember => new Vector4(1f, 0.55f, 0.22f, 1f),
        BoostStyle.Prism => new Vector4(0.62f, 0.72f, 1f, 1f),
        BoostStyle.Starlight => new Vector4(0.98f, 0.92f, 0.66f, 1f),
        _ => new Vector4(0.46f, 0.90f, 0.82f, 1f),
    };

    /// <summary>Localization key for a style's name; the tables carry all six languages.</summary>
    public static string NameKey(BoostStyle style) => $"os.boost_style_{(short)style}";

    private static void DrawAurora(ImDrawListPtr dl, Vector2 tl, Vector2 br, float rounding, float t, float a)
    {
        // A soft band of colour travelling the rim: the whole perimeter is tinted faintly, and a moving
        // window of it is lit.
        const int Steps = 96;
        var head = (t * 0.11f) % 1f;
        var prev = Perimeter(tl, br, rounding, 0f);
        for (var i = 1; i <= Steps; i++)
        {
            var u = i / (float)Steps;
            var point = Perimeter(tl, br, rounding, u);
            var d = Wrapped(u - head);
            var lit = MathF.Max(0f, 1f - (d / 0.34f));
            var hue = 0.44f + (0.30f * Wrapped(u - head + 0.5f));
            var col = FromHue(hue, 0.62f, 1f);
            col.W = (0.14f + (0.72f * lit * lit)) * a;
            dl.AddLine(prev, point, DrawFx.U32(col), Px(2.4f));
            prev = point;
        }
        GlowRing(dl, tl, br, rounding, new Vector4(0.36f, 0.86f, 0.80f, 1f), 0.30f * a);
    }

    private static void DrawEmber(ImDrawListPtr dl, Vector2 tl, Vector2 br, float rounding, float t, float a)
    {
        var warm = new Vector4(1f, 0.52f, 0.18f, 1f);
        dl.AddRect(tl, br, DrawFx.U32(DrawFx.Rgba(warm, 0.55f * a)), rounding, ImDrawFlags.None, Px(1.6f));
        GlowRing(dl, tl, br, rounding, warm, 0.34f * a);

        var w = br.X - tl.X;
        var rise = MathF.Min(br.Y - tl.Y, Px(62f));
        const int Count = 16;
        for (var i = 0; i < Count; i++)
        {
            var seed = Hash(i * 7919);
            var speed = 0.28f + (0.30f * Hash(i * 104729));
            var phase = (seed + (t * speed)) % 1f;
            var drift = MathF.Sin((t * 0.9f) + (seed * 12f)) * Px(5f);
            var x = tl.X + (w * ((seed * 0.92f) + 0.04f)) + drift;
            var y = br.Y - Px(3f) - (rise * phase);
            var fade = (1f - phase) * (1f - phase);
            var r = Px(1.1f + (1.5f * Hash(i * 15485863)));
            var col = Vector4.Lerp(new Vector4(1f, 0.86f, 0.42f, 1f), warm, phase);
            col.W = 0.85f * fade * a;
            dl.AddCircleFilled(new Vector2(x, y), r, DrawFx.U32(col));
        }
    }

    private static void DrawPrism(ImDrawListPtr dl, Vector2 tl, Vector2 br, float rounding, float t, float a)
    {
        const int Steps = 112;
        var spin = (t * 0.08f) % 1f;
        var prev = Perimeter(tl, br, rounding, 0f);
        for (var i = 1; i <= Steps; i++)
        {
            var u = i / (float)Steps;
            var point = Perimeter(tl, br, rounding, u);
            var col = FromHue((u + spin) % 1f, 0.72f, 1f);
            col.W = 0.82f * a;
            dl.AddLine(prev, point, DrawFx.U32(col), Px(2.2f));
            prev = point;
        }
        // A brighter chase running the other way, so the rim reads as light rather than as a border.
        var chase = 1f - ((t * 0.24f) % 1f);
        var lead = Perimeter(tl, br, rounding, chase);
        dl.AddCircleFilled(lead, Px(3.2f), DrawFx.U32(new Vector4(1f, 1f, 1f, 0.75f * a)));
        GlowRing(dl, tl, br, rounding, new Vector4(0.70f, 0.76f, 1f, 1f), 0.26f * a);
    }

    private static void DrawStarlight(ImDrawListPtr dl, Vector2 tl, Vector2 br, float rounding, float t, float a)
    {
        var pale = new Vector4(0.98f, 0.94f, 0.72f, 1f);
        dl.AddRect(tl, br, DrawFx.U32(DrawFx.Rgba(pale, 0.42f * a)), rounding, ImDrawFlags.None, Px(1.4f));
        GlowRing(dl, tl, br, rounding, pale, 0.28f * a);

        var size = br - tl;
        const int Count = 20;
        for (var i = 0; i < Count; i++)
        {
            var sx = Hash(i * 2654435761);
            var sy = Hash((i * 40503) + 17);
            var phase = Hash((i * 92083) + 3);
            var twinkle = 0.5f + (0.5f * MathF.Sin((t * 2.1f) + (phase * 6.28f)));
            var pos = new Vector2(
                tl.X + (Px(4f) + ((size.X - Px(8f)) * sx)),
                tl.Y + (Px(4f) + ((size.Y - Px(8f)) * sy)));
            var arm = Px(1.6f + (2.6f * twinkle * Hash((i * 7717) + 5)));
            var col = DrawFx.Rgba(pale, 0.16f + (0.62f * twinkle * a));
            var u32 = DrawFx.U32(col);
            dl.AddLine(pos - new Vector2(arm, 0f), pos + new Vector2(arm, 0f), u32, Px(1f));
            dl.AddLine(pos - new Vector2(0f, arm), pos + new Vector2(0f, arm), u32, Px(1f));
        }
    }

    /// <summary>Three widening rounded rects outside the card, faded out: a cheap bloom that never covers
    /// what the card is showing.</summary>
    private static void GlowRing(
        ImDrawListPtr dl, Vector2 tl, Vector2 br, float rounding, Vector4 color, float alpha)
    {
        for (var i = 1; i <= 3; i++)
        {
            var grow = Px(i * 2.2f);
            var col = DrawFx.Rgba(color, alpha / (i * 1.7f));
            dl.AddRect(
                tl - new Vector2(grow, grow), br + new Vector2(grow, grow), DrawFx.U32(col),
                rounding + grow, ImDrawFlags.None, Px(1.6f));
        }
    }

    /// <summary>A point at normalized position <paramref name="u"/> around a rounded rect, walking the
    /// four straight runs and approximating each corner as its arc.</summary>
    private static Vector2 Perimeter(Vector2 tl, Vector2 br, float rounding, float u)
    {
        var r = MathF.Max(0f, MathF.Min(rounding, MathF.Min(br.X - tl.X, br.Y - tl.Y) * 0.5f));
        var straightX = MathF.Max(0f, (br.X - tl.X) - (2f * r));
        var straightY = MathF.Max(0f, (br.Y - tl.Y) - (2f * r));
        var arc = r * MathF.PI * 0.5f;
        var total = (2f * straightX) + (2f * straightY) + (4f * arc);
        if (total <= 0f)
        {
            return tl;
        }

        var d = ((u % 1f) + 1f) % 1f * total;
        if (d < straightX)
        {
            return new Vector2(tl.X + r + d, tl.Y);
        }
        d -= straightX;
        if (d < arc)
        {
            return Arc(new Vector2(br.X - r, tl.Y + r), r, -MathF.PI * 0.5f, d / r);
        }
        d -= arc;
        if (d < straightY)
        {
            return new Vector2(br.X, tl.Y + r + d);
        }
        d -= straightY;
        if (d < arc)
        {
            return Arc(new Vector2(br.X - r, br.Y - r), r, 0f, d / r);
        }
        d -= arc;
        if (d < straightX)
        {
            return new Vector2(br.X - r - d, br.Y);
        }
        d -= straightX;
        if (d < arc)
        {
            return Arc(new Vector2(tl.X + r, br.Y - r), r, MathF.PI * 0.5f, d / r);
        }
        d -= arc;
        if (d < straightY)
        {
            return new Vector2(tl.X, br.Y - r - d);
        }
        d -= straightY;
        return Arc(new Vector2(tl.X + r, tl.Y + r), r, MathF.PI, d / r);
    }

    private static Vector2 Arc(Vector2 center, float radius, float startAngle, float sweep) =>
        center + (new Vector2(MathF.Cos(startAngle + sweep), MathF.Sin(startAngle + sweep)) * radius);

    /// <summary>Shortest distance between two normalized positions on a loop.</summary>
    private static float Wrapped(float delta)
    {
        var d = ((delta % 1f) + 1f) % 1f;
        return d > 0.5f ? 1f - d : d;
    }

    private static Vector4 FromHue(float hue, float saturation, float value)
    {
        var h = (((hue % 1f) + 1f) % 1f) * 6f;
        var sector = (int)h;
        var f = h - sector;
        var p = value * (1f - saturation);
        var q = value * (1f - (saturation * f));
        var t = value * (1f - (saturation * (1f - f)));
        return (sector % 6) switch
        {
            0 => new Vector4(value, t, p, 1f),
            1 => new Vector4(q, value, p, 1f),
            2 => new Vector4(p, value, t, 1f),
            3 => new Vector4(p, q, value, 1f),
            4 => new Vector4(t, p, value, 1f),
            _ => new Vector4(value, p, q, 1f),
        };
    }

    /// <summary>A stable 0..1 from an index, so the particles sit in the same places every frame without
    /// a per-card allocation.</summary>
    private static float Hash(long seed)
    {
        var x = (ulong)seed * 6364136223846793005UL + 1442695040888963407UL;
        x ^= x >> 33;
        x *= 0xff51afd7ed558ccdUL;
        x ^= x >> 33;
        return (x >> 40) / (float)(1 << 24);
    }
}
