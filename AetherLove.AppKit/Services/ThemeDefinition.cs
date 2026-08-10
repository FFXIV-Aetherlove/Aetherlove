using System;
using System.Numerics;
using AetherLove.UI;

namespace AetherLove.Services;

/// <summary>Immutable colour palette for one visual theme. Vector4 values are ImGui RGBA in [0,1].</summary>
public sealed class ThemeDefinition
{
    public required string Name { get; init; }
    public required string BackgroundImageFile { get; init; }

    /// <summary>A purchased theme's frame art, held in memory only (the file on disk is encrypted). When it
    /// resolves, the phone draws it instead of <see cref="BackgroundImageFile"/>, which stays set to a
    /// built-in so a frame still shows while the seal is opening.</summary>
    public Func<Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap?>? BezelTexture { get; init; }

    public required Vector4 Accent { get; init; }
    public required Vector4 AccentLight { get; init; }
    public required Vector4 AccentDark { get; init; }

    /// <summary>Optional override for the guided tour's emphasis color, for themes whose accent is hard to
    /// read against the tour's dim (e.g. gold). Falls back to <see cref="Accent"/>.</summary>
    public Vector4? TourAccent { get; init; }
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

    /// <summary>Where the OS status bar starts (design px); it spans down to <see cref="BezelTop"/>. Themes
    /// with a thin top frame push it below the frame line, inside the glass.</summary>
    public float StatusBarTop { get; init; } = 0f;

    /// <summary>Tint for the status bar contents (clock, signal, battery). Default white; a theme whose top frame
    /// is light can set a dark tint for legibility.</summary>
    public Vector4 StatusBarTint { get; init; } = new(1f, 1f, 1f, 1f);

    /// <summary>Horizontal placement of the clock within the status strip: 0 = left, 0.5 = centre, 1 = right
    /// (always clamped so it clears the right-hand icon cluster). Default centre.</summary>
    public float StatusBarTimeAlign { get; init; } = 0.5f;

    /// <summary>Extra inset (design px) pulling the right-hand icon cluster (signal, battery) further left, so it
    /// clears a theme's top-right frame ornament. Default 0.</summary>
    public float StatusBarRightInset { get; init; } = 0f;

    /// <summary>Hit rects (design px) of the minimize and close buttons. By default they are invisible hit areas over
    /// buttons the frame art already carries; set <see cref="DrawWindowControls"/> to have the host draw them.</summary>
    public Vector2 MinimizeButtonTL { get; init; } = new(370f, 0f);
    public Vector2 MinimizeButtonSize { get; init; } = new(30f, 27f);
    public Vector2 CloseButtonTL { get; init; } = new(401f, 0f);
    public Vector2 CloseButtonSize { get; init; } = new(26f, 27f);

    /// <summary>When true, the host draws the close + minimize buttons itself (a rounded key with a glyph that lights
    /// up in the accent on hover, like the home button) at the rects above, instead of relying on the frame art.
    /// Per theme: leave false for a frame whose art already carries the buttons; set true and position the rects for a
    /// frame that doesn't.</summary>
    public bool DrawWindowControls { get; init; }

    /// <summary>Optional neon colour for the drawn window controls; overrides the home button's glow when they should
    /// not match it (e.g. Allagan's red buttons over its blue home key). Falls back to the home glow / accent.</summary>
    public Vector4? WindowControlColor { get; init; }

    /// <summary>The bottom home-button renderer for this theme: its size, position nudge, styling and animation.
    /// Defaults to the iOS-style white pill; override per theme with a bespoke renderer (e.g.
    /// <see cref="NeonSquareHomeButton"/>). See <see cref="HomeButtonRenderer"/>.</summary>
    public HomeButtonRenderer HomeButton { get; init; } = new PillHomeButton();


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
