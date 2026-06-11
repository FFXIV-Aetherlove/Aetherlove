namespace AetherLove.Services.Localization;

public sealed class SpanishLanguageService : ILanguageService
{
    public string LanguageName => "Spanish";
    public System.Collections.Generic.IReadOnlyDictionary<string, string> Strings => Map;
    private static readonly System.Collections.Generic.Dictionary<string, string> Map = LanguageMap.Merge(
        OnboardingEs.Strings, ChatEs.Strings, ProfileEs.Strings,
        DeckEs.Strings, SettingsEs.Strings, CommonEs.Strings, HubErrorsEs.Strings,
        NotificationsEs.Strings);

    public string WelcomeTitle => "Bienvenido a AetherLove";
    public string WelcomeBody1 => "AetherLove es un plugin de emparejamiento social para Final Fantasy XIV — un lugar donde encontrar a tu gente, ya sea un compañero de juego fiel, un compañero de rol o algo más profundo.";
    public string WelcomeBody2 => "Explora los perfiles de otros aventureros, muestra tu interés con un gesto y, cuando el sentimiento sea mutuo, inicia una conversación privada — todo sin salir jamás de Eorzea.";
    public string WelcomePrivacyHeading => "Tu privacidad:";
    public string WelcomePrivacyBody => "Todos los chats y conversaciones privadas están cifrados de extremo a extremo — los propietarios de AetherLove no pueden leer tus mensajes privados.";
    public string WelcomeFeatureDiscoverTitle => "Descubrir";
    public string WelcomeFeatureDiscoverBody => "Explora tarjetas de perfil con un diseño cuidado.";
    public string WelcomeFeatureConnectTitle => "Conectar";
    public string WelcomeFeatureConnectBody => "Encuentra jugadores que comparten tus intereses.";
    public string WelcomeFeatureChatTitle => "Chatear";
    public string WelcomeFeatureChatBody => "Envía mensajes a tus coincidencias directamente desde el plugin.";
    public string WelcomePluginLanguageLabel => "Idioma del plugin";
    public string WelcomePluginLanguageTooltip => "Elige el idioma en que se mostrará la interfaz de AetherLove.\nPuedes cambiarlo más tarde en los ajustes.";
    public string WelcomeFooter => "La configuración tarda unos 3 minutos. Pulsa Siguiente para ver cómo funciona.";
}
