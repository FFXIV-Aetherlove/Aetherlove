using System;
using AetherLove.Services;
using AetherLove.Shared.Profile.Enums;
using Dalamud.Bindings.ImGui;

namespace AetherLove.UI;

/// <summary>Rendering for supporter name styles; animated variants honor ReduceMotion. The server only
/// sends a style while the owner is a supporter, so there is no gating here.</summary>
public static class SupporterStyle
{
    private const uint Crimson = 0xFF5A47F0;
    private const uint Gold = 0xFF3CC9FF;
    private const uint Emerald = 0xFF7FCB3C;
    private const uint Sapphire = 0xFFFFA34D;
    private const uint Violet = 0xFFFF7CB3;
    private const uint Rose = 0xFFAE6BFF;

    /// <summary>Color for a styled name, keeping the caller's alpha bits (card fades). None = fallback.</summary>
    public static uint NameColor(NameStyle style, uint fallback)
    {
        if (style == NameStyle.None)
        {
            return fallback;
        }
        var alpha = fallback & 0xFF000000u;
        var reduceMotion = AccessibilityService.ReduceMotion;
        var rgb = style switch
        {
            NameStyle.Crimson => Crimson,
            NameStyle.Gold => Gold,
            NameStyle.Emerald => Emerald,
            NameStyle.Sapphire => Sapphire,
            NameStyle.Violet => Violet,
            NameStyle.Rose => Rose,
            NameStyle.RainbowCycle => reduceMotion ? Violet : HueCycle(0.08f),
            NameStyle.Shimmer => reduceMotion ? Gold : Blend(Gold, 0xFFFFFFFF, Wave(1.4f)),
            NameStyle.Pulse => reduceMotion ? Rose : Blend(Rose, 0xFFFFFFFF, Wave(2.2f) * 0.6f),
            _ => fallback,
        };
        return alpha | (rgb & 0x00FFFFFFu);
    }

    /// <summary>0..1 sine wave over global time.</summary>
    private static float Wave(float speed) =>
        0.5f + 0.5f * MathF.Sin((float)(ImGui.GetTime() % (Math.PI * 200.0)) * speed);

    private static uint HueCycle(float cyclesPerSecond) =>
        Hue((float)(ImGui.GetTime() * cyclesPerSecond % 1.0));

    /// <summary>Rainbow color for a wrapped 0..1 hue (alpha FF).</summary>
    public static uint Hue(float hue01, float s = 0.75f)
    {
        var h = (hue01 - MathF.Floor(hue01)) * 6f;
        var i = (int)h;
        var f = h - i;
        const float v = 1f;
        var p = v * (1f - s);
        var q = v * (1f - s * f);
        var t = v * (1f - s * (1f - f));
        var (r, g, b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return 0xFF000000u | ((uint)(b * 255f) << 16) | ((uint)(g * 255f) << 8) | (uint)(r * 255f);
    }

    private static uint Blend(uint a, uint b, float t)
    {
        static uint Ch(uint x, int shift) => (x >> shift) & 0xFF;
        uint L(uint ca, uint cb) => (uint)(ca + (cb - (float)ca) * t);
        return 0xFF000000u
            | (L(Ch(a, 16), Ch(b, 16)) << 16)
            | (L(Ch(a, 8), Ch(b, 8)) << 8)
            | L(Ch(a, 0), Ch(b, 0));
    }
}
