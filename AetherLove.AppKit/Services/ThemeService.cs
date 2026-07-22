using System.Collections.Generic;
using System.Numerics;
using AetherLove.Config;
using AetherLove.UI;

namespace AetherLove.Services;

public enum AppTheme
{
    CrystalVoid = 0,
    VanillaSunrise = 1,
    AllaganPassion = 2,
    YorhaTypeAe = 3,
    WorldOfLovecraft = 4,
    Aetherless = 5,
}

/// <summary>Global theme registry. Call <see cref="Initialise"/> once at start-up.</summary>
public static class ThemeService
{
    private static Configuration? _config;

    public static IReadOnlyDictionary<AppTheme, ThemeDefinition> Themes { get; } =
        new Dictionary<AppTheme, ThemeDefinition>
        {
            // 1050-wide canvas: the 930px frame plus a 120px side tab holding the close and minimize buttons.
            [AppTheme.CrystalVoid] = new ThemeDefinition
            {
                Name = "Crystal Void",
                BackgroundImageFile = "phone_bg_purple_v5.png",
                // Magenta neon square home button in the bottom cradle; same v4 frame geometry as Allagan.
                HomeButton = new NeonSquareHomeButton
                {
                    GlowColor = new Vector4(0.816f, 0.161f, 0.867f, 1f), // #d029dd neon purple
                    Size = 26f,
                    Rounding = 7f,
                    PulseSeconds = 2.6f,
                    TooltipKey = "os.home",
                    CenterXOffset = -1f, // v5 is 30px narrower than v4; the centre-anchored button shifts back right
                    CenterYOffset = 4f,
                    HitSize = new Vector2(48f, 48f),
                },
                // v5 frame (930x1670) maps 2:1 to a 465-wide window; v4 with the right-edge button housing cropped.
                WindowWidth = 465f,
                BezelLeft = 40f,
                BezelRight = 40f,
                BezelTop = 44f,
                StatusBarTop = 20f,
                CloseButtonTL = new Vector2(430f, 24f),
                CloseButtonSize = new Vector2(29f, 28f),
                MinimizeButtonTL = new Vector2(430f, 53f),
                MinimizeButtonSize = new Vector2(29f, 27f),
                DrawWindowControls = true,
                Accent = new Vector4(0.74f, 0.32f, 0.95f, 1f),
                AccentLight = new Vector4(0.85f, 0.52f, 1.00f, 1f),
                AccentDark = new Vector4(0.44f, 0.14f, 0.66f, 1f),
                ChipFill = new Vector4(0.10f, 0.07f, 0.22f, 1f),
                SecondaryStart = new Vector4(0.70f, 0.30f, 0.98f, 1f),
                SecondaryEnd = new Vector4(0.20f, 0.50f, 1.00f, 1f),
                ButtonNormal = new Vector4(0.46f, 0.16f, 0.72f, 0.90f),
                ButtonHovered = new Vector4(0.64f, 0.30f, 0.92f, 1.00f),
                ButtonActive = new Vector4(0.34f, 0.10f, 0.56f, 1.00f),
            },

            [AppTheme.VanillaSunrise] = new ThemeDefinition
            {
                Name = "Vanilla Sunrise",
                BackgroundImageFile = "phone_bg_yellow_v5.png",
                // Teal neon square home button in the bottom cradle; same v4 frame geometry as Allagan.
                HomeButton = new NeonSquareHomeButton
                {
                    GlowColor = new Vector4(0.078f, 0.878f, 0.784f, 1f), // #14e0c8 teal
                    Size = 26f,
                    Rounding = 7f,
                    PulseSeconds = 2.6f,
                    TooltipKey = "os.home",
                    CenterXOffset = -1f, // v5 is 30px narrower than v4; the centre-anchored button shifts back right
                    CenterYOffset = 4f,
                    HitSize = new Vector2(48f, 48f),
                },
                // v5 frame (932x1670) maps 2:1 to a 466-wide window; v4 with the right-edge button housing cropped.
                WindowWidth = 466f,
                BezelLeft = 40f,
                BezelRight = 41f,
                BezelTop = 44f,
                StatusBarTop = 20f,
                CloseButtonTL = new Vector2(430f, 24f),
                CloseButtonSize = new Vector2(29f, 28f),
                MinimizeButtonTL = new Vector2(430f, 53f),
                MinimizeButtonSize = new Vector2(29f, 27f),
                DrawWindowControls = true,
                Accent = new Vector4(1.00f, 0.72f, 0.30f, 1f),
                AccentLight = new Vector4(1.00f, 0.85f, 0.50f, 1f),
                AccentDark = new Vector4(0.72f, 0.44f, 0.06f, 1f),
                ChipFill = new Vector4(0.22f, 0.13f, 0.02f, 1f),
                SecondaryStart = new Vector4(1.00f, 0.78f, 0.30f, 1f),
                SecondaryEnd = new Vector4(0.40f, 0.90f, 0.68f, 1f),
                ButtonNormal = new Vector4(0.72f, 0.44f, 0.06f, 0.90f),
                ButtonHovered = new Vector4(1.00f, 0.72f, 0.30f, 1.00f),
                ButtonActive = new Vector4(0.50f, 0.28f, 0.02f, 1.00f),
            },

            // v4 frame (990x1670 full-bleed): the screen opening is tight on the left, wider on the right for the
            // close/minimize button tab.
            [AppTheme.AllaganPassion] = new ThemeDefinition
            {
                Name = "Allagan Passion",
                BackgroundImageFile = "phone_bg_allagan_v5.png",
                // Neon square home button cradled in the gap between the bottom neon lines: blue glowing edges.
                HomeButton = new NeonSquareHomeButton
                {
                    GlowColor = new Vector4(0.035f, 0.714f, 0.992f, 1f), // #09b6fd
                    Size = 26f,
                    Rounding = 7f,
                    PulseSeconds = 2.6f,
                    TooltipKey = "os.home",
                    CenterXOffset = -1f, // v5 is 30px narrower than v4; the centre-anchored button shifts back right
                    CenterYOffset = 4f,
                    HitSize = new Vector2(48f, 48f),
                },
                // v5 frame (930x1670) maps 2:1 to a 465-wide window; v4 with the right-edge button housing cropped.
                WindowWidth = 465f,
                BezelLeft = 40f,
                BezelRight = 40f,
                BezelTop = 44f,
                StatusBarTop = 20f,
                CloseButtonTL = new Vector2(430f, 24f),
                CloseButtonSize = new Vector2(29f, 28f),
                MinimizeButtonTL = new Vector2(430f, 53f),
                MinimizeButtonSize = new Vector2(29f, 27f),
                DrawWindowControls = true,
                // Red controls, unlike this theme's blue home-button neon.
                WindowControlColor = new Vector4(0.98f, 0.23f, 0.33f, 1f),
                Accent = new Vector4(0.95f, 0.28f, 0.40f, 1f),
                AccentLight = new Vector4(1.00f, 0.48f, 0.58f, 1f),
                AccentDark = new Vector4(0.60f, 0.10f, 0.20f, 1f),
                ChipFill = new Vector4(0.20f, 0.04f, 0.08f, 1f),
                SecondaryStart = new Vector4(0.95f, 0.30f, 0.40f, 1f),
                SecondaryEnd = new Vector4(0.36f, 0.56f, 0.96f, 1f),
                ButtonNormal = new Vector4(0.60f, 0.10f, 0.20f, 0.90f),
                ButtonHovered = new Vector4(0.85f, 0.22f, 0.35f, 1.00f),
                ButtonActive = new Vector4(0.40f, 0.05f, 0.12f, 1.00f),
            },

            // The accent is light, so own chat bubbles use dark text (ChatColors.OwnFgDefault).
            [AppTheme.YorhaTypeAe] = new ThemeDefinition
            {
                Name = "YoRHa Type Æ",
                BackgroundImageFile = "phone_bg_nier2_v3.png",
                // Cream/smoke square home button at the screen's bottom, over the decorative diamond line.
                HomeButton = new NeonSquareHomeButton
                {
                    GlowColor = new Vector4(0.871f, 0.851f, 0.800f, 1f), // #ded9cc cream/smoke
                    Size = 26f,
                    Rounding = 7f,
                    PulseSeconds = 2.6f,
                    TooltipKey = "os.home",
                    CenterYOffset = 3f,
                    HitSize = new Vector2(48f, 48f),
                },
                // v2 art 1198x1670 -> 599 window (native 2:1); screen opening measured at image 197..1000 x, 61..1583 y.
                WindowWidth = 599f,
                BezelTop = 36f,
                BezelBottom = 52f,
                BezelLeft = 99f,
                BezelRight = 99f,
                // Light silver top frame: dark clock/icons, clock pushed right so it clears the baked title.
                StatusBarTint = new Vector4(0f, 0f, 0f, 1f),
                StatusBarTimeAlign = 1f,
                CloseButtonTL = new Vector2(500f, 32f),
                CloseButtonSize = new Vector2(30f, 28f),
                MinimizeButtonTL = new Vector2(500f, 55f),
                MinimizeButtonSize = new Vector2(30f, 28f),
                DrawWindowControls = true,
                Accent = new Vector4(0.812f, 0.788f, 0.694f, 1f),
                AccentLight = new Vector4(0.855f, 0.831f, 0.733f, 1f),
                AccentDark = new Vector4(0.243f, 0.220f, 0.180f, 1f),
                ChipFill = new Vector4(0.161f, 0.141f, 0.110f, 1f),
                SecondaryStart = new Vector4(0.72f, 0.69f, 0.58f, 1f),
                SecondaryEnd = new Vector4(0.55f, 0.50f, 0.41f, 1f),
                ButtonNormal = new Vector4(0.28f, 0.25f, 0.20f, 0.92f),
                ButtonHovered = new Vector4(0.42f, 0.38f, 0.31f, 1.00f),
                ButtonActive = new Vector4(0.18f, 0.16f, 0.12f, 1.00f),
            },

            // The accent is light, so own chat bubbles use dark text (ChatColors.OwnFgDefault).
            [AppTheme.WorldOfLovecraft] = new ThemeDefinition
            {
                Name = "World of Lovecraft",
                BackgroundImageFile = "phone_bg_wow_v4.png",
                // Golden rounded bar home button in the bottom bezel, with a slow golden pulse.
                HomeButton = new GoldenPillHomeButton
                {
                    GoldColor = new Vector4(0.93f, 0.72f, 0.34f, 1f), // #edb857 WoW gold
                    Width = 54f,
                    Height = 18f,
                    Rounding = 6f,
                    PulseSeconds = 2.4f,
                    TooltipKey = "os.home",
                    HitSize = new Vector2(62f, 28f),
                },
                // v4 (930x1670) is v3 with 30px cropped off each side; maps 2:1 to a 465-wide window.
                WindowWidth = 465f,
                BezelTop = 38f,
                BezelBottom = 41f,
                BezelLeft = 36f,
                BezelRight = 36f,
                // Pull the signal/battery cluster left so it clears the ornate top-right gem.
                StatusBarRightInset = 40f,
                CloseButtonTL = new Vector2(432f, 64f),
                CloseButtonSize = new Vector2(23f, 22f),
                MinimizeButtonTL = new Vector2(432f, 83f),
                MinimizeButtonSize = new Vector2(23f, 21f),
                DrawWindowControls = true,
                // Blue controls to match the theme's gem, not the gold home pill.
                WindowControlColor = new Vector4(0.34f, 0.58f, 0.96f, 1f),
                Accent = new Vector4(0.93f, 0.72f, 0.34f, 1f),
                AccentLight = new Vector4(1.00f, 0.85f, 0.52f, 1f),
                AccentDark = new Vector4(0.55f, 0.38f, 0.10f, 1f),
                TourAccent = new Vector4(0.34f, 0.58f, 0.96f, 1f),
                ChipFill = new Vector4(0.05f, 0.10f, 0.20f, 1f),
                SecondaryStart = new Vector4(0.95f, 0.78f, 0.40f, 1f),
                SecondaryEnd = new Vector4(0.14f, 0.42f, 0.76f, 1f),
                ButtonNormal = new Vector4(0.55f, 0.38f, 0.10f, 0.90f),
                ButtonHovered = new Vector4(0.78f, 0.58f, 0.22f, 1.00f),
                ButtonActive = new Vector4(0.40f, 0.27f, 0.06f, 1.00f),
            },

            // Monochrome (black+white) v4 frame; identical geometry to Allagan/Vanilla/Crystal, cool-white neon square.
            [AppTheme.Aetherless] = new ThemeDefinition
            {
                Name = "Aetherless",
                BackgroundImageFile = "phone_bg_aetherless_v2.png",
                HomeButton = new NeonSquareHomeButton
                {
                    GlowColor = new Vector4(0.88f, 0.91f, 0.94f, 1f), // cool white (frame neon is #ffffff)
                    Size = 26f,
                    Rounding = 7f,
                    PulseSeconds = 2.6f,
                    TooltipKey = "os.home",
                    CenterXOffset = -1f, // v2 is 30px narrower than v1; the centre-anchored button shifts back right
                    CenterYOffset = 4f,
                    HitSize = new Vector2(48f, 48f),
                },
                // v2 frame (930x1670) maps 2:1 to a 465-wide window; v1 with the right-edge button housing cropped.
                WindowWidth = 465f,
                BezelLeft = 40f,
                BezelRight = 40f,
                BezelTop = 44f,
                StatusBarTop = 20f,
                CloseButtonTL = new Vector2(430f, 24f),
                CloseButtonSize = new Vector2(29f, 28f),
                MinimizeButtonTL = new Vector2(430f, 53f),
                MinimizeButtonSize = new Vector2(29f, 27f),
                DrawWindowControls = true,
                Accent = new Vector4(0.70f, 0.74f, 0.78f, 1f),
                AccentLight = new Vector4(0.88f, 0.91f, 0.94f, 1f),
                AccentDark = new Vector4(0.30f, 0.34f, 0.39f, 1f),
                ChipFill = new Vector4(0.09f, 0.11f, 0.13f, 1f),
                SecondaryStart = new Vector4(0.44f, 0.51f, 0.58f, 1f),
                SecondaryEnd = new Vector4(0.78f, 0.82f, 0.86f, 1f),
                ButtonNormal = new Vector4(0.24f, 0.28f, 0.33f, 0.92f),
                ButtonHovered = new Vector4(0.39f, 0.45f, 0.52f, 1.00f),
                ButtonActive = new Vector4(0.16f, 0.19f, 0.23f, 1.00f),
            },
        };

    /// <summary>Order themes appear in the appearance picker: the four v4 neon-square frames grouped together,
    /// then the two bespoke frames. Enum values stay fixed (persisted in config); only display order lives here.</summary>
    public static readonly AppTheme[] DisplayOrder =
    {
        AppTheme.CrystalVoid,
        AppTheme.VanillaSunrise,
        AppTheme.AllaganPassion,
        AppTheme.Aetherless,
        AppTheme.YorhaTypeAe,
        AppTheme.WorldOfLovecraft,
    };

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.CrystalVoid;
    public static ThemeDefinition Current { get; private set; } = Themes[AppTheme.CrystalVoid];

    public static void Initialise(Configuration config)
    {
        _config = config;
        CurrentTheme = config.SelectedTheme;
        Current = Themes.TryGetValue(CurrentTheme, out var t) ? t : Themes[AppTheme.CrystalVoid];
    }

    public static void SetTheme(AppTheme theme)
    {
        if (CurrentTheme == theme)
        {
            return;
        }
        CurrentTheme = theme;
        Current = Themes[theme];
        if (_config == null)
        {
            return;
        }
        _config.SelectedTheme = theme;
        _config.Save();
    }

}
