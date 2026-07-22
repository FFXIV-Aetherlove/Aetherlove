using System;
using System.Collections.Generic;
using System.Globalization;
using AetherLove.Config;

namespace AetherLove.Services.Localization;

/// <summary>Global language registry. Initialise once, then read <see cref="Current"/> every frame.</summary>
public static class LanguageProvider
{
    private static Configuration? _config;

    private static readonly Dictionary<string, ILanguageService> Services =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["English"] = new EnglishLanguageService(),
            ["Spanish"] = new SpanishLanguageService(),
            ["French"] = new FrenchLanguageService(),
            ["Russian"] = new RussianLanguageService(),
            ["German"] = new GermanLanguageService(),
            ["Portuguese"] = new PortugueseLanguageService(),
        };

    private static readonly ILanguageService Fallback = Services["English"];

    /// <summary>English service, used as the fallback for any key a language hasn't translated.</summary>
    public static ILanguageService English => Fallback;

    public static ILanguageService Current { get; private set; } = Fallback;

    /// <summary>Languages with a translation service; spoken-only languages such as Japanese are excluded.</summary>
    public static int UiLanguageCount => Services.Count;

    /// <summary>Culture of the selected plugin language, not the player's OS culture.</summary>
    public static CultureInfo CurrentCulture => CultureInfo.GetCultureInfo(IsoCode(Current.LanguageName));

    /// <summary>Formats with <see cref="CurrentCulture"/>, swapping the no-break spaces some locales embed;
    /// the bundled game font has no glyph for them and renders tofu boxes.</summary>
    public static string FormatDate(DateTime date, string format) =>
        NormalizeSpaces(date.ToString(format, CurrentCulture));

    /// <inheritdoc cref="FormatDate(DateTime, string)"/>
    public static string FormatDate(DateOnly date, string format) =>
        NormalizeSpaces(date.ToString(format, CurrentCulture));

    /// <summary>Swaps no-break (U+00A0) and narrow no-break (U+202F) spaces for a regular space.</summary>
    public static string NormalizeSpaces(string text) =>
        text.Replace('\u00A0', ' ').Replace('\u202F', ' ');

    private static string IsoCode(string languageName) => languageName switch
    {
        "Spanish" => "es",
        "French" => "fr",
        "Russian" => "ru",
        "German" => "de",
        "Portuguese" => "pt",
        _ => "en",
    };

    private static readonly Dictionary<string, string> IsoToLanguageName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = "English",
            ["es"] = "Spanish",
            ["fr"] = "French",
            ["ru"] = "Russian",
            ["de"] = "German",
            ["pt"] = "Portuguese",
        };

    /// <summary>Merges an app-owned string pack into the language's table; app keys must be prefixed to avoid collisions.</summary>
    public static void RegisterAppStrings(string isoCode, IReadOnlyDictionary<string, string> strings)
    {
        if (!IsoToLanguageName.TryGetValue(isoCode, out var name)
            || !Services.TryGetValue(name, out var svc)
            || svc.Strings is not Dictionary<string, string> map)
        {
            return;
        }
        foreach (var (key, value) in strings)
        {
            map[key] = value;
        }
    }

    public static event Action? LanguageChanged;

    public static void Initialise(Configuration config)
    {
        _config = config;
        Current = Services.TryGetValue(config.PluginLanguage, out var svc) ? svc : Fallback;
    }

    public static void SetLanguage(string languageName)
    {
        var svc = Services.TryGetValue(languageName, out var found) ? found : Fallback;
        if (Current == svc)
        {
            return;
        }

        Current = svc;

        if (_config != null)
        {
            _config.PluginLanguage = svc.LanguageName;
            _config.Save();
        }

        LanguageChanged?.Invoke();
    }
}
