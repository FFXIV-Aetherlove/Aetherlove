using System.Numerics;

namespace AetherLove.UI;

/// <summary>Fixed-pixel layout scaling for the phone UI: every authored length goes through
/// <see cref="Px"/> so window, fonts and spacing scale together. <see cref="S"/> deliberately ignores
/// Dalamud's own font scale.</summary>
public static class UiScale
{
    /// <summary>The phone's design resolution, in pixels, before <see cref="S"/> is applied.</summary>
    public static readonly Vector2 Design = new(464f, 835f);

    /// <summary>The single uniform scale knob; set via <see cref="Apply"/>, defaults to 1 (Small).</summary>
    public static float S { get; private set; } = 1f;

    public static float MultiplierFor(PhoneScalePreset preset) => preset switch
    {
        PhoneScalePreset.XS     => 0.85f,
        PhoneScalePreset.Medium => 1.15f,
        PhoneScalePreset.Large  => 1.30f,
        PhoneScalePreset.XL     => 1.75f,
        PhoneScalePreset.XXL    => 2.00f,
        _                       => 1.0f,
    };

    /// <summary>Sets <see cref="S"/> from a size preset; takes effect on the next frame's PreDraw.</summary>
    public static void Apply(PhoneScalePreset preset) => S = MultiplierFor(preset);

    public static float Px(float v) => v * S;

    public static Vector2 Px(float x, float y) => new(x * S, y * S);

    public static Vector2 Px(Vector2 v) => v * S;
}
