using System;
using System.Collections.Generic;
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
