using System.Numerics;

namespace AetherLove.UI;

/// <summary>
/// Fixed-pixel layout helper for the phone UI, authored at <see cref="Design"/> (464x835) and scaled
/// uniformly off the single <see cref="S"/> knob. Host windows size to <c>Px(Design)</c> and push the
/// per-size <see cref="UiFonts"/> handles (built at px×<see cref="S"/>); every authored length goes
/// through <see cref="Px"/>, so window, fonts, spacing and emoji all scale together. <see cref="S"/> is
/// driven only by the user's <see cref="PhoneScalePreset"/> choice, deliberately ignoring Dalamud's own
/// font scale so the phone bezel stays in our control.
/// </summary>
public static class UiScale
{
    /// <summary>The phone's design resolution, in pixels, before <see cref="S"/> is applied.</summary>
    public static readonly Vector2 Design = new(464f, 835f);

    /// <summary>The single uniform scale knob; set via <see cref="Apply"/>, defaults to 1 (Small).</summary>
    public static float S { get; private set; } = 1f;

    /// <summary>The multiplier each size preset maps to.</summary>
    public static float MultiplierFor(PhoneScalePreset preset) => preset switch
    {
        PhoneScalePreset.Medium => 1.15f,
        PhoneScalePreset.Large  => 1.30f,
        _                       => 1.0f,
    };

    /// <summary>Sets <see cref="S"/> from a size preset; takes effect on the next frame's PreDraw.</summary>
    public static void Apply(PhoneScalePreset preset) => S = MultiplierFor(preset);

    /// <summary>Scales an authored pixel length by <see cref="S"/>.</summary>
    public static float Px(float v) => v * S;

    /// <summary>Scales an authored pixel size/offset by <see cref="S"/>.</summary>
    public static Vector2 Px(float x, float y) => new(x * S, y * S);

    /// <summary>Scales an authored pixel vector by <see cref="S"/>.</summary>
    public static Vector2 Px(Vector2 v) => v * S;
}
