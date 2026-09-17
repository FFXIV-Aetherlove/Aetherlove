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

    private static readonly object PackGate = new();

    private static readonly Dictionary<string, List<IReadOnlyDictionary<string, string>>> RegisteredPacks =
        new(StringComparer.OrdinalIgnoreCase);

    private static volatile Dictionary<string, IReadOnlyDictionary<string, string>> _tables =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The active language's table with every published app pack merged in.</summary>
    public static IReadOnlyDictionary<string, string> CurrentStrings => Table(Current);

    /// <summary>The English table with every published app pack merged in.</summary>
    public static IReadOnlyDictionary<string, string> EnglishStrings => Table(Fallback);

    private static IReadOnlyDictionary<string, string> Table(ILanguageService svc) =>
        _tables.TryGetValue(svc.LanguageName, out var merged) ? merged : svc.Strings;

    /// <summary>Stages an app-owned string pack for the language; app keys must be prefixed to avoid collisions.
    /// Nothing is visible to <see cref="Loc"/> until <see cref="PublishAppStrings"/> runs.</summary>
    public static void RegisterAppStrings(string isoCode, IReadOnlyDictionary<string, string> strings)
    {
        if (!IsoToLanguageName.TryGetValue(isoCode, out var name) || !Services.ContainsKey(name))
        {
            return;
        }
        lock (PackGate)
        {
            if (!RegisteredPacks.TryGetValue(name, out var packs))
            {
                packs = new List<IReadOnlyDictionary<string, string>>();
                RegisteredPacks[name] = packs;
            }
            packs.Add(strings);
        }
    }

    /// <summary>Builds fresh merged tables from the base tables and every registered pack, then swaps them in
    /// with one reference write. Readers on other threads see either the old tables or the new, never a
    /// dictionary being written to.</summary>
    public static void PublishAppStrings()
    {
        lock (PackGate)
        {
            var tables = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, packs) in RegisteredPacks)
            {
                var merged = new Dictionary<string, string>(Services[name].Strings);
                foreach (var pack in packs)
                {
                    foreach (var (key, value) in pack)
                    {
                        merged[key] = value;
                    }
                }
                tables[name] = merged;
            }
            _tables = tables;
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
