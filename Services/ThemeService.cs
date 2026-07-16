using System.Collections.Generic;
using System.Numerics;
using AetherLove.Config;

namespace AetherLove.Services;

public enum AppTheme
{
    CrystalVoid = 0,
    VanillaSunrise = 1,
    AllaganPassion = 2,
    YorhaTypeAe = 3,
    WorldOfLovecraft = 4,
}

/// <summary>Global theme registry. Call <see cref="Initialise"/> once at start-up.</summary>
public static class ThemeService
{
    private static Configuration? _config;

    public static IReadOnlyDictionary<AppTheme, ThemeDefinition> Themes { get; } =
        new Dictionary<AppTheme, ThemeDefinition>
        {
            // The v3 art set (Crystal Void, Vanilla Sunrise, Allagan Passion, World of Lovecraft) shares one
            // 1050x1670 canvas with an identical content area, so those four use identical geometry.
            [AppTheme.CrystalVoid] = new ThemeDefinition
            {
                Name = "Crystal Void",
                BackgroundImageFile = "phone_bg_purple_v3.png",
                WindowWidth = 525f,
                BezelTop = 38f,
                BezelBottom = 41f,
                BezelLeft = 66f,
                BezelRight = 66f,
                CloseButtonTL = new Vector2(496f, 24f),
                CloseButtonSize = new Vector2(29f, 28f),
                MinimizeButtonTL = new Vector2(496f, 53f),
                MinimizeButtonSize = new Vector2(29f, 27f),
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
                BackgroundImageFile = "phone_bg_yellow_v3.png",
                WindowWidth = 525f,
                BezelTop = 38f,
                BezelBottom = 41f,
                BezelLeft = 66f,
                BezelRight = 66f,
                CloseButtonTL = new Vector2(496f, 24f),
                CloseButtonSize = new Vector2(29f, 28f),
                MinimizeButtonTL = new Vector2(496f, 53f),
                MinimizeButtonSize = new Vector2(29f, 27f),
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

            [AppTheme.AllaganPassion] = new ThemeDefinition
            {
                Name = "Allagan Passion",
                BackgroundImageFile = "phone_bg_allagan_v3.png",
                WindowWidth = 525f,
                BezelTop = 38f,
                BezelBottom = 41f,
                BezelLeft = 66f,
                BezelRight = 66f,
                CloseButtonTL = new Vector2(496f, 24f),
                CloseButtonSize = new Vector2(29f, 28f),
                MinimizeButtonTL = new Vector2(496f, 53f),
                MinimizeButtonSize = new Vector2(29f, 27f),
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
                BackgroundImageFile = "phone_bg_nier2_v1.png",
                // 1200x1670 art with side decorations; the window widens to 600 to render at native aspect.
                WindowWidth = 600f,
                BezelTop = 36f,
                BezelBottom = 52f,
                BezelLeft = 104f,
                BezelRight = 104f,
                CloseButtonTL = new Vector2(532f, 23f),
                CloseButtonSize = new Vector2(30f, 28f),
                MinimizeButtonTL = new Vector2(532f, 52f),
                MinimizeButtonSize = new Vector2(30f, 27f),
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
                BackgroundImageFile = "phone_bg_wow_v3.png",
                WindowWidth = 525f,
                BezelTop = 38f,
                BezelBottom = 41f,
                BezelLeft = 66f,
                BezelRight = 66f,
                CloseButtonTL = new Vector2(496f, 24f),
                CloseButtonSize = new Vector2(29f, 28f),
                MinimizeButtonTL = new Vector2(496f, 53f),
                MinimizeButtonSize = new Vector2(29f, 27f),
                Accent = new Vector4(0.93f, 0.72f, 0.34f, 1f),
                AccentLight = new Vector4(1.00f, 0.85f, 0.52f, 1f),
                AccentDark = new Vector4(0.55f, 0.38f, 0.10f, 1f),
                ChipFill = new Vector4(0.05f, 0.10f, 0.20f, 1f),
                SecondaryStart = new Vector4(0.95f, 0.78f, 0.40f, 1f),
                SecondaryEnd = new Vector4(0.14f, 0.42f, 0.76f, 1f),
                ButtonNormal = new Vector4(0.55f, 0.38f, 0.10f, 0.90f),
                ButtonHovered = new Vector4(0.78f, 0.58f, 0.22f, 1.00f),
                ButtonActive = new Vector4(0.40f, 0.27f, 0.06f, 1.00f),
            },
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
