namespace AetherLove.Services.Localization;

public sealed class PortugueseLanguageService : ILanguageService
{
    public string LanguageName => "Portuguese";
    public System.Collections.Generic.IReadOnlyDictionary<string, string> Strings => Map;
    private static readonly System.Collections.Generic.Dictionary<string, string> Map = LanguageMap.Merge(
        OnboardingPt.Strings, ChatPt.Strings, ProfilePt.Strings,
        DeckPt.Strings, SettingsPt.Strings, CommonPt.Strings, HubErrorsPt.Strings,
        NotificationsPt.Strings, PlacesPt.Strings, HangoutsPt.Strings);

    public string WelcomeTitle => "Bem-vindo ao AetherLove";
    public string WelcomeBody1 => "O AetherLove é um plugin de relacionamento social para Final Fantasy XIV — um lugar para encontrares novas amizades, seja um parceiro para jogares, um companheiro de roleplay, ou até algo mais sério.";
    public string WelcomeBody2 => "Explora os perfis de outros aventureiros, demonstra interesse com um deslize, quando o sentimento for mútuo, inicia uma conversa privada — tudo sem nunca saíres de Eorzea.";
    public string WelcomePrivacyHeading => "A tua privacidade:";
    public string WelcomePrivacyBody => "Todas as conversas privadas são encriptadas de ponta a ponta — os responsáveis pelo AetherLove não conseguem ler as tuas mensagens privadas.";
    public string WelcomeFeatureDiscoverTitle => "Descobrir";
    public string WelcomeFeatureDiscoverBody => "Explora bonitos perfis.";
    public string WelcomeFeatureConnectTitle => "Ligar";
    public string WelcomeFeatureConnectBody => "Conecta-te com jogadores que partilham os mesmos interesses.";
    public string WelcomeFeatureChatTitle => "Conversar";
    public string WelcomeFeatureChatBody => "Envia mensagens aos teus matches diretamente no plugin.";
    public string WelcomePluginLanguageLabel => "Idioma do plugin";
    public string WelcomePluginLanguageTooltip => "Escolhe o idioma em que a interface do AetherLove será apresentada.\nPodes alterar mais tarde nas definições.";
    public string WelcomeFooter => "A configuração demora cerca de 3 minutos. Carrega em Seguinte para veres como funciona.";
}
