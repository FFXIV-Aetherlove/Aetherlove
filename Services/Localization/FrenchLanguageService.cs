namespace AetherLove.Services.Localization;

public sealed class FrenchLanguageService : ILanguageService
{
    public string LanguageName => "French";
    public System.Collections.Generic.IReadOnlyDictionary<string, string> Strings => Map;
    private static readonly System.Collections.Generic.Dictionary<string, string> Map = LanguageMap.Merge(
        OnboardingFr.Strings, ChatFr.Strings, ProfileFr.Strings,
        DeckFr.Strings, SettingsFr.Strings, CommonFr.Strings, HubErrorsFr.Strings,
        NotificationsFr.Strings, NewsFr.Strings, PlacesFr.Strings, HangoutsFr.Strings);

    public string WelcomeTitle => "Bienvenue sur AetherLove";
    public string WelcomeBody1 => "AetherLove est un plugin de rencontres sociales pour Final Fantasy XIV — un endroit pour trouver votre communauté, qu'il s'agisse d'un partenaire de co-op fiable, d'un compagnon de jeu de rôle ou de quelque chose de plus profond.";
    public string WelcomeBody2 => "Parcourez les profils d'autres aventuriers, exprimez votre intérêt d'un geste et, lorsque le sentiment est partagé, commencez une conversation privée — le tout sans jamais quitter Eorzea.";
    public string WelcomePrivacyHeading => "Votre vie privée :";
    public string WelcomePrivacyBody => "Tous les chats et conversations privées sont chiffrés de bout en bout — les propriétaires d'AetherLove ne peuvent pas lire vos messages privés.";
    public string WelcomeFeatureDiscoverTitle => "Découvrir";
    public string WelcomeFeatureDiscoverBody => "Parcourez des fiches de profil soigneusement conçues.";
    public string WelcomeFeatureConnectTitle => "Connecter";
    public string WelcomeFeatureConnectBody => "Rencontrez des joueurs qui partagent vos centres d'intérêt.";
    public string WelcomeFeatureChatTitle => "Discuter";
    public string WelcomeFeatureChatBody => "Envoyez des messages à vos correspondances directement dans le plugin.";
    public string WelcomePluginLanguageLabel => "Langue du plugin";
    public string WelcomePluginLanguageTooltip => "Choisissez la langue d'affichage de l'interface d'AetherLove.\nVous pouvez la modifier ultérieurement dans les paramètres.";
    public string WelcomeFooter => "La configuration prend environ 3 minutes. Appuyez sur Suivant pour voir comment ça fonctionne.";
}
