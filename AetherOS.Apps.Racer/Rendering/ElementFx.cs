namespace AetherOS.Apps.Racer.Rendering;

using System;
using System.Numerics;

/// <summary>How an element's marks move: the channel that survives the colour being taken away.</summary>
public enum FxMotion
{
    /// <summary>No signature of its own (the neutral look).</summary>
    None,

    /// <summary>Fire: buoyant, narrowing, flickering. Never falls.</summary>
    Rise,

    /// <summary>Lightning: zero travel, strobes, over inside a third of a second.</summary>
    Strike,

    /// <summary>Wind: orbits and passes, never travels, and carries the other five.</summary>
    Sweep,

    /// <summary>Ice: the slowest fall in the pool, with a lazy sway, and it comes to rest.</summary>
    Drift,

    /// <summary>Water: heavy, oriented along its own velocity, and its death is an event.</summary>
    Fall,

    /// <summary>Earth: the heaviest gravity, and the only mark that lands twice.</summary>
    Tumble,
}

/// <summary>Whether a mark makes light, takes it, or gives nothing back. Decides whether the
/// house key light applies to a mark at all.</summary>
public enum FxLight
{
    /// <summary>Wind: carries no highlight and must not gain one.</summary>
    Neither,

    /// <summary>Fire: its own hot core; a catchlight on a flame is a lie.</summary>
    Emits,

    /// <summary>Lightning: forces white regardless of the caller's colour. The electric tell.</summary>
    EmitsWhite,

    /// <summary>Ice and water: a facet of the mark's own hue brightened toward white.</summary>
    Catches,

    /// <summary>Earth, alone: a shade, never a highlight, and the reason it needs two colours.</summary>
    Absorbs,
}

/// <summary>One element's look: three colours rather than one, plus its motion and light signatures.</summary>
/// <param name="Key">The frozen lowercase element key.</param>
/// <param name="Tint">The element's light: washes, rings, glows, chips, the weather cast.</param>
/// <param name="Body">The mark's own fill: the ember, the flake, the pebble, the droplet.</param>
/// <param name="Cool">Where <paramref name="Body"/> ramps to over a mark's life; equals it for four of six.</param>
/// <param name="Motion">The motion signature.</param>
/// <param name="Light">Emit, catch, or absorb.</param>
public readonly record struct ElementLook(
    string Key,
    Vector4 Tint,
    Vector4 Body,
    Vector4 Cool,
    FxMotion Motion,
    FxLight Light);

/// <summary>
/// The elemental FX authority: one table of six looks that drawing code reads instead of each
/// site deciding for itself. No behaviour, no state, no draw calls. Earth alone carries a
/// <see cref="ElementLook.Tint"/> different from its <see cref="ElementLook.Body"/> (gold light,
/// umber matter), because earth is the only one of the six that absorbs.
/// </summary>
public static class ElementFx
{
    /// <summary>The Rec.709 weighting the wash solve and the weather cast both lean on.</summary>
    public static float Luminance(in Vector4 c) => (0.2126f * c.X) + (0.7152f * c.Y) + (0.0722f * c.Z);

    /// <summary>The race stage's ground, opaque on purpose: a race is a place the page travels to.
    /// Every ground wash alpha is solved against this, so it is the one colour that may not move
    /// without re-deriving the whole roster.
    ///
    /// <para>Deliberately near-neutral. The wash composites at alphas around a tenth, so a ground
    /// with chroma of its own decides the result instead: over the app's plum every one of the seven
    /// courses lands red-brightest, and a green element renders brown. The equal-luminance solve
    /// only carries hue when the ground it is solved over has almost none.</para></summary>
    public static readonly Vector4 Night = new(0.07f, 0.075f, 0.10f, 1f);

    /// <summary>A float colour packed for a drawlist, in managed code. The native converter is an
    /// interop crossing and the ground pass asks for one of these per drawn primitive.</summary>
    public static uint U32(in Vector4 c)
    {
        return Byte(c.X) | (Byte(c.Y) << 8) | (Byte(c.Z) << 16) | (Byte(c.W) << 24);
    }

    private static uint Byte(float v) => (uint)((Math.Clamp(v, 0f, 1f) * 255f) + 0.5f);

    /// <summary>The house light, as the direction to the key (upper-left). Normalised.</summary>
    public static readonly Vector2 KeyLight = Vector2.Normalize(new Vector2(-0.30f, -0.34f));

    /// <summary>The same light as the direction it travels (down-right). Derived, never authored,
    /// so the two conventions cannot drift apart.</summary>
    public static Vector2 KeyTravel => -KeyLight;

    /// <summary>The look for a course, a weather or a runner with no element.</summary>
    public static readonly ElementLook Neutral = new(
        string.Empty,
        new Vector4(1f, 1f, 1f, 0.55f),
        new Vector4(1f, 1f, 1f, 0.55f),
        new Vector4(1f, 1f, 1f, 0.55f),
        FxMotion.None,
        FxLight.Neither);

    private static readonly ElementLook Fire = new(
        "fire",
        new Vector4(0.95f, 0.50f, 0.35f, 0.95f),
        new Vector4(1.00f, 0.72f, 0.28f, 0.95f),
        new Vector4(0.88f, 0.28f, 0.10f, 0.95f),
        FxMotion.Rise,
        FxLight.Emits);

    private static readonly ElementLook Lightning = new(
        "lightning",
        new Vector4(0.85f, 0.75f, 1.00f, 0.95f),
        new Vector4(0.85f, 0.78f, 1.00f, 0.95f),
        new Vector4(0.68f, 0.55f, 0.95f, 0.95f),
        FxMotion.Strike,
        FxLight.EmitsWhite);

    private static readonly ElementLook Wind = new(
        "wind",
        new Vector4(0.62f, 0.88f, 0.55f, 0.95f),
        new Vector4(0.62f, 0.88f, 0.55f, 0.95f),
        new Vector4(0.62f, 0.88f, 0.55f, 0.95f),
        FxMotion.Sweep,
        FxLight.Neither);

    private static readonly ElementLook Ice = new(
        "ice",
        new Vector4(0.72f, 0.90f, 1.00f, 0.95f),
        new Vector4(0.80f, 0.94f, 1.00f, 0.95f),
        new Vector4(0.80f, 0.94f, 1.00f, 0.95f),
        FxMotion.Drift,
        FxLight.Catches);

    private static readonly ElementLook Water = new(
        "water",
        new Vector4(0.50f, 0.74f, 0.95f, 0.95f),
        new Vector4(0.50f, 0.74f, 0.95f, 0.95f),
        new Vector4(0.50f, 0.74f, 0.95f, 0.95f),
        FxMotion.Fall,
        FxLight.Catches);

    /// <summary>Earth: the wheel's gold is the element's light (dust, rings, the ground wash, the
    /// chip) and the umber is its matter (the rock), because earth absorbs.</summary>
    private static readonly ElementLook Earth = new(
        "earth",
        new Vector4(0.88f, 0.72f, 0.38f, 0.95f),
        new Vector4(0.72f, 0.55f, 0.34f, 0.95f),
        new Vector4(0.72f, 0.55f, 0.34f, 0.95f),
        FxMotion.Tumble,
        FxLight.Absorbs);

    /// <summary>Never throws and never returns null: an unknown string gets
    /// <see cref="Neutral"/> and the screen stays legible.</summary>
    public static ElementLook For(string? element) => element switch
    {
        "fire" => Fire,
        "lightning" => Lightning,
        "wind" => Wind,
        "ice" => Ice,
        "water" => Water,
        "earth" => Earth,
        _ => Neutral,
    };

    /// <summary>An element's colour re-authored to land at a given luminance. Hue and saturation
    /// survive; brightness is dictated.</summary>
    public static Vector4 AtLuminance(in Vector4 colour, float luminance)
    {
        var l = Luminance(colour);
        if (l <= 0.0001f)
        {
            return new Vector4(luminance, luminance, luminance, colour.W);
        }

        var k = luminance / l;
        return new Vector4(
            MathF.Min(1f, colour.X * k),
            MathF.Min(1f, colour.Y * k),
            MathF.Min(1f, colour.Z * k),
            colour.W);
    }

    /// <summary>A facet of a colour brightened toward white rather than a white dot pasted on it.</summary>
    public static Vector4 Lit(in Vector4 col, float toward, float alpha) => new(
        col.X + ((1f - col.X) * toward),
        col.Y + ((1f - col.Y) * toward),
        col.Z + ((1f - col.Z) * toward),
        col.W * alpha);
}
