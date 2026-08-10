using System.Numerics;

namespace AetherLove.UI;

/// <summary>Scale knob for the minimised "bubble" window, independent of the main phone size;
/// <see cref="PhoneScalePreset.Medium"/> = 1.0 = the bubble's authored size.</summary>
public static class MiniScale
{
    /// <summary>The uniform mini-bubble scale; set via <see cref="Apply"/>, defaults to 1 (Medium).</summary>
    public static float S { get; private set; } = 1f;

    public static float MultiplierFor(PhoneScalePreset preset) => preset switch
    {
        PhoneScalePreset.XS    => 0.58f,
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
