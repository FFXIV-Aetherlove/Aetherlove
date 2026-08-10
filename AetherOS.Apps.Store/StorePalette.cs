using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Store;

/// <summary>The Store's own colours. Deliberately NOT the phone theme: a shop is a brand, and a storefront
/// that repaints itself to match whatever frame the player bought reads as part of the phone rather than as
/// a place they walked into. Everything here is the AetherOS mark: blue over a near-black blue, crimson as
/// the counterweight. Spark gold is the one colour that comes from elsewhere, because the currency is gold
/// in the Wallet and in the success scene and has to stay gold here too.</summary>
internal static class StorePalette
{
    public static readonly Vector4 Blue = new(0.24f, 0.48f, 1.00f, 1f);
    public static readonly Vector4 BlueLight = new(0.45f, 0.65f, 1.00f, 1f);
    public static readonly Vector4 BlueDark = new(0.11f, 0.24f, 0.58f, 1f);

    public static readonly Vector4 Crimson = new(0.84f, 0.13f, 0.27f, 1f);
    public static readonly Vector4 CrimsonLight = new(0.95f, 0.32f, 0.44f, 1f);

    /// <summary>The page behind everything.</summary>
    public static readonly Vector4 Ground = new(0.027f, 0.043f, 0.094f, 1f);

    /// <summary>A card or bar sitting on the ground.</summary>
    public static readonly Vector4 Surface = new(0.063f, 0.094f, 0.180f, 1f);

    public static readonly Vector4 Body = new(0.92f, 0.94f, 0.98f, 1f);
    public static readonly Vector4 Hint = new(0.58f, 0.63f, 0.74f, 1f);

    public static uint BlueU32 => ImGui.ColorConvertFloat4ToU32(Blue);
    public static uint BlueLightU32 => ImGui.ColorConvertFloat4ToU32(BlueLight);
    public static uint CrimsonU32 => ImGui.ColorConvertFloat4ToU32(Crimson);
    public static uint BodyU32 => ImGui.ColorConvertFloat4ToU32(Body);
    public static uint HintU32 => ImGui.ColorConvertFloat4ToU32(Hint);

    public static uint BlueWithAlpha(float a) => ImGui.ColorConvertFloat4ToU32(Blue with { W = a });
    public static uint SurfaceWithAlpha(float a) => ImGui.ColorConvertFloat4ToU32(Surface with { W = a });

    /// <summary>Button fills for the Store's own buttons, replacing the theme's three-state ramp.</summary>
    public static readonly Vector4 ButtonNormal = new(0.15f, 0.30f, 0.66f, 0.92f);
    public static readonly Vector4 ButtonHovered = new(0.24f, 0.48f, 1.00f, 1f);
    public static readonly Vector4 ButtonActive = new(0.09f, 0.19f, 0.46f, 1f);

    /// <summary>The gradient pairs categories and other swatch surfaces are dealt from, in order, so a
    /// storefront of five categories still looks like one set rather than five unrelated accents. Assigned by
    /// position, never by the category's own stored colour.</summary>
    private static readonly (Vector4 Top, Vector4 Bottom)[] Ramp =
    [
        (new(0.20f, 0.42f, 0.92f, 1f), new(0.07f, 0.14f, 0.38f, 1f)),
        (new(0.78f, 0.14f, 0.30f, 1f), new(0.30f, 0.05f, 0.14f, 1f)),
        (new(0.36f, 0.30f, 0.86f, 1f), new(0.12f, 0.09f, 0.34f, 1f)),
        (new(0.15f, 0.52f, 0.78f, 1f), new(0.05f, 0.18f, 0.32f, 1f)),
        (new(0.58f, 0.20f, 0.62f, 1f), new(0.20f, 0.06f, 0.24f, 1f)),
    ];

    /// <summary>The nth swatch gradient, wrapping if a storefront ever grows past the ramp.</summary>
    public static (Vector4 Top, Vector4 Bottom) Swatch(int index)
    {
        var i = index % Ramp.Length;
        return Ramp[i < 0 ? i + Ramp.Length : i];
    }

    /// <summary>The accent that belongs with a swatch, for a glyph or a rule drawn on top of it.</summary>
    public static Vector4 SwatchAccent(int index)
    {
        var (top, _) = Swatch(index);
        return new Vector4(
            MathF.Min(1f, top.X + 0.25f), MathF.Min(1f, top.Y + 0.25f), MathF.Min(1f, top.Z + 0.25f), 1f);
    }
}
