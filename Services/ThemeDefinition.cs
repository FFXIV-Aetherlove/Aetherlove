using System;
using System.Numerics;

namespace AetherLove.Services;

/// <summary>Immutable colour palette for one visual theme. Vector4 values are ImGui RGBA in [0,1].</summary>
public sealed class ThemeDefinition
{
    public required string Name { get; init; }
    public required string BackgroundImageFile { get; init; }

    public required Vector4 Accent { get; init; }
    public required Vector4 AccentLight { get; init; }
    public required Vector4 AccentDark { get; init; }
    public required Vector4 ChipFill { get; init; }

    /// <summary>Secondary gradient endpoints (e.g. purple→pink) used for content-preference chips.</summary>
    public required Vector4 SecondaryStart { get; init; }
    public required Vector4 SecondaryEnd { get; init; }

    public required Vector4 ButtonNormal { get; init; }
    public required Vector4 ButtonHovered { get; init; }
    public required Vector4 ButtonActive { get; init; }


    public uint AccentU32 => ToU32(Accent);
    public uint AccentLightU32 => ToU32(AccentLight);
    public uint AccentDarkU32 => ToU32(AccentDark);

    public Vector4 SecondaryFillStart => Dark(SecondaryStart);
    public Vector4 SecondaryFillEnd => Dark(SecondaryEnd);

    public uint AccentWithAlpha(float a) => WithAlpha(Accent, a);
    public uint AccentLightWithAlpha(float a) => WithAlpha(AccentLight, a);
    public uint AccentDarkWithAlpha(float a) => WithAlpha(AccentDark, a);

    public uint AccentLightRgb => AccentLightU32 & 0x00FFFFFF;
    public uint AccentDarkRgb => AccentDarkU32 & 0x00FFFFFF;

    /// <summary>Secondary gradient endpoints darkened for a small filled pill: vivid two-tone yet keeps a white
    /// glyph legible (mirrors how <see cref="AccentDarkRgb"/> backs the primary pills). Alpha applied at draw time.</summary>
    public uint SecondaryPillStartRgb => ToU32(PillDark(SecondaryStart)) & 0x00FFFFFF;
    public uint SecondaryPillEndRgb => ToU32(PillDark(SecondaryEnd)) & 0x00FFFFFF;

    public Vector4 ScrollbarGrab => Accent with { W = 0.85f };
    public Vector4 ScrollbarGrabHovered => AccentLight with { W = 1.00f };
    public Vector4 ScrollbarGrabActive => AccentDark with { W = 1.00f };


    /// <summary>Converts ImGui Vector4(R,G,B,A) to draw-list uint ABGR 0xAABBGGRR.</summary>
    private static uint ToU32(Vector4 c) =>
        ((uint)MathF.Round(Math.Clamp(c.W, 0f, 1f) * 255f) << 24) |
        ((uint)MathF.Round(Math.Clamp(c.Z, 0f, 1f) * 255f) << 16) |
        ((uint)MathF.Round(Math.Clamp(c.Y, 0f, 1f) * 255f) << 8) |
        ((uint)MathF.Round(Math.Clamp(c.X, 0f, 1f) * 255f));

    private static uint WithAlpha(Vector4 c, float a) =>
        ((uint)MathF.Round(Math.Clamp(a, 0f, 1f) * 255f) << 24) |
        ((uint)MathF.Round(Math.Clamp(c.Z, 0f, 1f) * 255f) << 16) |
        ((uint)MathF.Round(Math.Clamp(c.Y, 0f, 1f) * 255f) << 8) |
        ((uint)MathF.Round(Math.Clamp(c.X, 0f, 1f) * 255f));

    /// <summary>Dark, legible chip-fill tint for a bright secondary colour (keeps white text readable).</summary>
    private static Vector4 Dark(Vector4 c) => new(c.X * 0.22f, c.Y * 0.22f, c.Z * 0.22f, 1f);

    /// <summary>Moderate darkening for a small pill fill: vivid enough to read the secondary hue, dark enough for a white glyph.</summary>
    private static Vector4 PillDark(Vector4 c) => new(c.X * 0.55f, c.Y * 0.55f, c.Z * 0.55f, 1f);
}
