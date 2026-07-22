using System;
using System.Numerics;
using AetherLove.Services;
using Dalamud.Bindings.ImGui;

namespace AetherLove.UI;

/// <summary>Interaction state passed to a home-button renderer each frame.</summary>
public readonly record struct HomeButtonState(bool Hovered, bool Pressed);

/// <summary>Renders the phone's bottom home button. One instance is assigned per theme
/// (<see cref="ThemeDefinition.HomeButton"/>) so each design owns its own size, position nudge, styling and
/// animation. The host (MainPluginWindow) submits the invisible hit button, then calls <see cref="Draw"/> with the
/// screen-space anchor (the centre of the bottom bezel band, already offset by <see cref="CenterYOffset"/>) and a
/// monotonic time for animation. Add a new look by subclassing this and assigning it to a theme.</summary>
public abstract class HomeButtonRenderer
{
    /// <summary>Clickable hit-area size in design px, centred on the anchor.</summary>
    public Vector2 HitSize { get; init; } = new(120f, 22f);

    /// <summary>Horizontal nudge of the anchor from the phone's centre line, design px (+ = right), so the button
    /// lines up with the theme's frame art (the anchor is the frame centre, not the content centre).</summary>
    public float CenterXOffset { get; init; }

    /// <summary>Vertical nudge of the anchor from the bottom-bezel midpoint, design px (+ = down), so the button
    /// lines up with the theme's frame art.</summary>
    public float CenterYOffset { get; init; }

    /// <summary>Optional loc key for a hover tooltip (e.g. "os.home"); the host shows it while the button is hovered.</summary>
    public string? TooltipKey { get; init; }

    /// <summary>Draw the button. <paramref name="center"/> is screen-space; <paramref name="t"/> is seconds.</summary>
    public abstract void Draw(ImDrawListPtr dl, Vector2 center, HomeButtonState state, ThemeDefinition theme, float t);

    protected static uint Rgba(Vector4 c, float a) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(c.X, c.Y, c.Z, Math.Clamp(a, 0f, 1f)));
}

/// <summary>The default iOS-style white home pill. Used by every theme that doesn't override the home button.</summary>
public sealed class PillHomeButton : HomeButtonRenderer
{
    public override void Draw(ImDrawListPtr dl, Vector2 center, HomeButtonState state, ThemeDefinition theme, float t)
    {
        var alpha = state.Hovered ? 0.95f : 0.45f;
        var half = Px(42f, 2.5f);
        dl.AddRectFilled(center - half, center + half, Rgba(new Vector4(1f, 1f, 1f, 1f), alpha), half.Y);
    }
}

/// <summary>A neon square home button: a rounded square whose edges glow softly (outward bleed passes plus a crisp
/// bright rim and a thin inner light-tube highlight), over a faint dark body so it reads as a physical key, with a
/// gentle breathing pulse. Matches the blue neon of a frame's line art. For themes whose art has a dedicated bottom
/// cradle (e.g. Allagan's gap between the neon lines). Tune size, corner rounding, glow colour and pulse speed from
/// the theme.</summary>
public sealed class NeonSquareHomeButton : HomeButtonRenderer
{
    /// <summary>Neon edge colour (also the body tint and outward glow).</summary>
    public Vector4 GlowColor { get; init; } = new(0.25f, 0.62f, 1.00f, 1f);
    /// <summary>Square side length, design px.</summary>
    public float Size { get; init; } = 30f;
    /// <summary>Corner rounding, design px.</summary>
    public float Rounding { get; init; } = 7f;
    /// <summary>Seconds for one full (subtle) breathing pulse.</summary>
    public float PulseSeconds { get; init; } = 2.6f;

    public override void Draw(ImDrawListPtr dl, Vector2 center, HomeButtonState state, ThemeDefinition theme, float t)
    {
        const float tau = MathF.PI * 2f;
        var reduce = AccessibilityService.ReduceMotion;
        var period = MathF.Max(0.5f, PulseSeconds);
        var pulse = reduce ? 0.5f : 0.5f + 0.5f * MathF.Sin(t * (tau / period));
        var hover = state.Hovered ? 1f : 0f;
        var press = state.Pressed ? 1f : 0f;

        var half = Px(Size) * 0.5f * (1f - 0.05f * press);
        var tl = center - new Vector2(half, half);
        var br = center + new Vector2(half, half);
        var round = Px(Rounding);
        const ImDrawFlags flags = ImDrawFlags.RoundCornersAll;

        dl.AddRectFilled(tl, br, Rgba(new Vector4(0.02f, 0.05f, 0.10f, 1f), 0.30f + 0.18f * hover), round, flags);
        dl.AddRectFilled(tl, br, Rgba(GlowColor, 0.05f + 0.05f * pulse + 0.09f * hover), round, flags);

        // Outward glow: expanding rounded-rect outlines, fainter the further they bleed.
        var glowA = 0.10f + 0.06f * pulse + 0.16f * hover;
        const int passes = 4;
        for (var i = 1; i <= passes; i++)
        {
            var exp = Px(1.7f * i);
            var a = glowA * (1f - (i - 1) / (float)passes);
            dl.AddRect(tl - new Vector2(exp, exp), br + new Vector2(exp, exp), Rgba(GlowColor, a), round + exp, flags,
                Px(2.2f));
        }

        dl.AddRect(tl, br, Rgba(GlowColor, 0.85f + 0.15f * pulse), round, flags, Px(2f));
        var inset = new Vector2(Px(2f), Px(2f));
        dl.AddRect(tl + inset, br - inset, Rgba(new Vector4(0.75f, 0.90f, 1f, 1f), 0.28f + 0.20f * pulse),
            MathF.Max(0f, round - Px(2f)), flags, Px(1f));
    }
}

/// <summary>A solid golden rounded-bar home button, wider than tall, with a soft breathing "golden pulse" (outward
/// glow plus a body/rim brighten), a metallic top sheen and a bright rim. For frames without a neon cradle. Tune the
/// gold, width/height, corner rounding and pulse speed from the theme.</summary>
public sealed class GoldenPillHomeButton : HomeButtonRenderer
{
    /// <summary>Bar colour (fill and glow).</summary>
    public Vector4 GoldColor { get; init; } = new(0.93f, 0.72f, 0.34f, 1f);
    /// <summary>Bar width, design px.</summary>
    public float Width { get; init; } = 54f;
    /// <summary>Bar height, design px.</summary>
    public float Height { get; init; } = 18f;
    /// <summary>Corner rounding, design px.</summary>
    public float Rounding { get; init; } = 6f;
    /// <summary>Seconds for one full golden pulse.</summary>
    public float PulseSeconds { get; init; } = 2.4f;

    public override void Draw(ImDrawListPtr dl, Vector2 center, HomeButtonState state, ThemeDefinition theme, float t)
    {
        const float tau = MathF.PI * 2f;
        var reduce = AccessibilityService.ReduceMotion;
        var period = MathF.Max(0.5f, PulseSeconds);
        var pulse = reduce ? 0.5f : 0.5f + 0.5f * MathF.Sin(t * (tau / period));
        var hover = state.Hovered ? 1f : 0f;
        var press = state.Pressed ? 1f : 0f;

        var hw = Px(Width) * 0.5f * (1f - 0.03f * press);
        var hh = Px(Height) * 0.5f * (1f - 0.03f * press);
        var tl = center - new Vector2(hw, hh);
        var br = center + new Vector2(hw, hh);
        var round = Px(Rounding);
        const ImDrawFlags flags = ImDrawFlags.RoundCornersAll;

        var glowA = 0.14f + 0.12f * pulse + 0.20f * hover;
        const int passes = 4;
        for (var i = 1; i <= passes; i++)
        {
            var exp = Px(2f * i);
            var a = glowA * (1f - (i - 1) / (float)passes);
            dl.AddRect(tl - new Vector2(exp, exp), br + new Vector2(exp, exp), Rgba(GoldColor, a), round + exp, flags,
                Px(2.5f));
        }

        var lift = 0.10f * pulse + 0.12f * hover;
        var body = new Vector4(MathF.Min(1f, GoldColor.X + lift), MathF.Min(1f, GoldColor.Y + lift),
            MathF.Min(1f, GoldColor.Z + lift * 0.6f), 1f);
        dl.AddRectFilled(tl, br, Rgba(body, 0.92f), round, flags);

        dl.AddRectFilled(tl, new Vector2(br.X, center.Y - hh * 0.15f),
            Rgba(new Vector4(1f, 0.95f, 0.75f, 1f), 0.16f + 0.10f * pulse), round, ImDrawFlags.RoundCornersTop);
        dl.AddRect(tl, br, Rgba(new Vector4(1f, 0.90f, 0.60f, 1f), 0.55f + 0.15f * pulse), round, flags, Px(1.2f));
    }
}
