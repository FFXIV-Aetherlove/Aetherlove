namespace AetherLove.Services.Localization;

public sealed class EnglishLanguageService : ILanguageService
{
    public string LanguageName => "English";
    public System.Collections.Generic.IReadOnlyDictionary<string, string> Strings => Map;

    public string WelcomeTitle => "Welcome to AetherLove";
    public string WelcomeBody1 => "AetherLove is a social matchmaking plugin for Final Fantasy XIV: a place to find your people, whether that means a steadfast co-op partner, a roleplaying companion, or something deeper.";
    public string WelcomeBody2 => "Browse other adventurers' profiles, express interest with a swipe, and when the feeling is mutual, start a private conversation, all without ever leaving Eorzea.";
    public string WelcomePrivacyHeading => "Your privacy:";
    public string WelcomePrivacyBody => "All chats and private conversations are end-to-end encrypted: the owners of AetherLove cannot read your private messages.";
    public string WelcomeFeatureDiscoverTitle => "Discover";
    public string WelcomeFeatureDiscoverBody => "Browse beautifully crafted profile cards.";
    public string WelcomeFeatureConnectTitle => "Connect";
    public string WelcomeFeatureConnectBody => "Match with players who share your interests.";
    public string WelcomeFeatureChatTitle => "Chat";
    public string WelcomeFeatureChatBody => "Message your matches directly inside the plugin.";
    public string WelcomePluginLanguageLabel => "Plugin language";
    public string WelcomePluginLanguageTooltip => "Choose the language AetherLove's interface will display in.\nYou can change this later in settings.";
    public string WelcomeFooter => "Setup takes about 3 minutes. Press Next to see how it works.";

    // English is the source of truth: keys are aggregated from the per-area fragments in Strings/.
    public static readonly System.Collections.Generic.Dictionary<string, string> Map = LanguageMap.Merge(
        OnboardingEn.Strings, ChatEn.Strings, ProfileEn.Strings,
        DeckEn.Strings, SettingsEn.Strings, CommonEn.Strings, HubErrorsEn.Strings,
        NotificationsEn.Strings, NewsEn.Strings, PlacesEn.Strings, HangoutsEn.Strings, OsEn.Strings);
}
