using System.Collections.Generic;

namespace AetherLove.Services.Localization;

/// <summary>UI strings for a single language.</summary>
public interface ILanguageService
{
    string LanguageName { get; }

    /// <summary>Keyed UI strings. May be partial for non-English languages; missing keys fall back to
    /// English via <see cref="Loc"/>.</summary>
    IReadOnlyDictionary<string, string> Strings { get; }

    string WelcomeTitle { get; }
    string WelcomeBody1 { get; }
    string WelcomeBody2 { get; }
    string WelcomePrivacyHeading { get; }
    string WelcomePrivacyBody { get; }
    string WelcomeFeatureDiscoverTitle { get; }
    string WelcomeFeatureDiscoverBody { get; }
    string WelcomeFeatureConnectTitle { get; }
    string WelcomeFeatureConnectBody { get; }
    string WelcomeFeatureChatTitle { get; }
    string WelcomeFeatureChatBody { get; }
    string WelcomePluginLanguageLabel { get; }
    string WelcomePluginLanguageTooltip { get; }
    string WelcomeFooter { get; }
}
