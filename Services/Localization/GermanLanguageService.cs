namespace AetherLove.Services.Localization;

public sealed class GermanLanguageService : ILanguageService
{
    public string LanguageName => "German";
    public System.Collections.Generic.IReadOnlyDictionary<string, string> Strings => Map;
    private static readonly System.Collections.Generic.Dictionary<string, string> Map = LanguageMap.Merge(
        OnboardingDe.Strings, ChatDe.Strings, ProfileDe.Strings,
        DeckDe.Strings, SettingsDe.Strings, CommonDe.Strings, HubErrorsDe.Strings,
        NotificationsDe.Strings, NewsDe.Strings);

    public string WelcomeTitle => "Willkommen bei AetherLove";
    public string WelcomeBody1 => "AetherLove ist ein soziales Matchmaking-Plugin für Final Fantasy XIV — ein Ort, um deine Leute zu finden, ob das einen zuverlässigen Co-op-Partner, einen Rollenspiel-Gefährten oder etwas tiefgründigeres bedeutet.";
    public string WelcomeBody2 => "Stöbere in den Profilen anderer Abenteurer, zeige Interesse mit einem Wisch und starte, wenn das Gefühl gegenseitig ist, ein privates Gespräch — alles ohne Eorzea je zu verlassen.";
    public string WelcomePrivacyHeading => "Deine Privatsphäre:";
    public string WelcomePrivacyBody => "Alle Chats und privaten Gespräche sind Ende-zu-Ende-verschlüsselt — die Betreiber von AetherLove können deine privaten Nachrichten nicht lesen.";
    public string WelcomeFeatureDiscoverTitle => "Entdecken";
    public string WelcomeFeatureDiscoverBody => "Stöbere in sorgfältig gestalteten Profilkarten.";
    public string WelcomeFeatureConnectTitle => "Verbinden";
    public string WelcomeFeatureConnectBody => "Finde Spieler, die deine Interessen teilen.";
    public string WelcomeFeatureChatTitle => "Chatten";
    public string WelcomeFeatureChatBody => "Schreibe deinen Matches direkt im Plugin.";
    public string WelcomePluginLanguageLabel => "Plugin-Sprache";
    public string WelcomePluginLanguageTooltip => "Wähle die Sprache der AetherLove-Oberfläche.\nDu kannst dies später in den Einstellungen ändern.";
    public string WelcomeFooter => "Die Einrichtung dauert ca. 3 Minuten. Drücke Weiter, um zu sehen, wie es funktioniert.";
}
