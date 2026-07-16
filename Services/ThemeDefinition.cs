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

    /// <summary>Window width in design pixels; height is always 835.</summary>
    public float WindowWidth { get; init; } = 464f;

    /// <summary>Content insets over the bezel art, in design pixels; defaults fit the v3 frames.</summary>
    public float BezelTop { get; init; } = 34f;
    public float BezelBottom { get; init; } = 48f;
    public float BezelLeft { get; init; } = 44f;
    public float BezelRight { get; init; } = 44f;

    /// <summary>Hit rects (design px) of the minimize and close buttons drawn into the bezel art.</summary>
    public Vector2 MinimizeButtonTL { get; init; } = new(370f, 0f);
    public Vector2 MinimizeButtonSize { get; init; } = new(30f, 27f);
    public Vector2 CloseButtonTL { get; init; } = new(401f, 0f);
    public Vector2 CloseButtonSize { get; init; } = new(26f, 27f);


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

    /// <summary>Secondary endpoints darkened for a small filled pill; alpha is applied at draw time.</summary>
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

    /// <summary>Chip-fill tint dark enough to keep white text readable.</summary>
    private static Vector4 Dark(Vector4 c) => new(c.X * 0.22f, c.Y * 0.22f, c.Z * 0.22f, 1f);

    /// <summary>Milder darkening for a small pill fill; still legible under a white glyph.</summary>
    private static Vector4 PillDark(Vector4 c) => new(c.X * 0.55f, c.Y * 0.55f, c.Z * 0.55f, 1f);
}
