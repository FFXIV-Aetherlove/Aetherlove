using System.Collections.Generic;
using System.Numerics;
using AetherLove.Config;

namespace AetherLove.Services;

public enum AppTheme
{
    CrystalVoid = 0,
    VanillaSunrise = 1,
    AllaganPassion = 2,
}

/// <summary>Global theme registry. Call <see cref="Initialise"/> once at start-up.</summary>
public static class ThemeService
{
    private static Configuration? _config;

    public static IReadOnlyDictionary<AppTheme, ThemeDefinition> Themes { get; } =
        new Dictionary<AppTheme, ThemeDefinition>
        {
            [AppTheme.CrystalVoid] = new ThemeDefinition
            {
                Name = "Crystal Void",
                BackgroundImageFile = "phone_bg_purple_v2.png",
                Accent = new Vector4(0.73f, 0.42f, 0.79f, 1f),
                AccentLight = new Vector4(0.85f, 0.56f, 0.90f, 1f),
                AccentDark = new Vector4(0.48f, 0.25f, 0.63f, 1f),
                ChipFill = new Vector4(0.15f, 0.07f, 0.23f, 1f),
                SecondaryStart = new Vector4(0.62f, 0.40f, 0.92f, 1f),
                SecondaryEnd = new Vector4(0.98f, 0.45f, 0.78f, 1f),
                ButtonNormal = new Vector4(0.50f, 0.22f, 0.70f, 0.90f),
                ButtonHovered = new Vector4(0.68f, 0.34f, 0.88f, 1.00f),
                ButtonActive = new Vector4(0.38f, 0.12f, 0.55f, 1.00f),
            },

            [AppTheme.VanillaSunrise] = new ThemeDefinition
            {
                Name = "Vanilla Sunrise",
                BackgroundImageFile = "phone_bg_yellow_v2.png",
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
                BackgroundImageFile = "phone_bg_allagan_v2.png",
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
        };

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.CrystalVoid;
    public static ThemeDefinition Current { get; private set; } = Themes[AppTheme.CrystalVoid];

    /// <summary>Loads the saved theme preference. Call once at start-up.</summary>
    public static void Initialise(Configuration config)
    {
        _config = config;
        CurrentTheme = config.SelectedTheme;
        Current = Themes.TryGetValue(CurrentTheme, out var t) ? t : Themes[AppTheme.CrystalVoid];
    }

    /// <summary>Switches the active theme and persists the selection. No-op if already active.</summary>
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
