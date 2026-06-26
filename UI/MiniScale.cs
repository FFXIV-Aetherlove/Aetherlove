using System.Numerics;

namespace AetherLove.UI;

/// <summary>
/// Independent scale knob for the minimised "bubble" window, driven by the user's mini-size
/// <see cref="PhoneScalePreset"/> choice. Unlike <see cref="UiScale"/> (which scales the full phone), this is
/// not tied to the main phone size: <see cref="PhoneScalePreset.Medium"/> = 1.0 = the bubble's authored size,
/// so the smaller/larger presets size the bubble around its current look.
/// </summary>
public static class MiniScale
{
    /// <summary>The uniform mini-bubble scale; set via <see cref="Apply"/>, defaults to 1 (Medium).</summary>
    public static float S { get; private set; } = 1f;

    /// <summary>The multiplier each size preset maps to, anchored on Medium = 1.0 (the current size).</summary>
    public static float MultiplierFor(PhoneScalePreset preset) => preset switch
    {
        PhoneScalePreset.Small => 0.75f,
        PhoneScalePreset.Large => 1.30f,
        PhoneScalePreset.XL    => 1.70f,
        PhoneScalePreset.XXL   => 2.20f,
        _                      => 1.0f,
    };

    /// <summary>Sets <see cref="S"/> from a size preset; takes effect on the next frame's PreDraw.</summary>
    public static void Apply(PhoneScalePreset preset) => S = MultiplierFor(preset);

    public static float Px(float v) => v * S;
    public static Vector2 Px(float x, float y) => new(x * S, y * S);
    public static Vector2 Px(Vector2 v) => v * S;
}
