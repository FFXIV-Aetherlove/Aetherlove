namespace AetherLove.Services.Localization;

public sealed class RussianLanguageService : ILanguageService
{
    public string LanguageName => "Russian";
    public System.Collections.Generic.IReadOnlyDictionary<string, string> Strings => Map;
    private static readonly System.Collections.Generic.Dictionary<string, string> Map = LanguageMap.Merge(
        OnboardingRu.Strings, ChatRu.Strings, ProfileRu.Strings,
        DeckRu.Strings, SettingsRu.Strings, CommonRu.Strings, HubErrorsRu.Strings,
        NotificationsRu.Strings, NewsRu.Strings, PlacesRu.Strings, HangoutsRu.Strings);

    public string WelcomeTitle => "Добро пожаловать в AetherLove";
    public string WelcomeBody1 => "AetherLove — это плагин социальных знакомств для Final Fantasy XIV. Место, где можно найти своих людей: надёжного партнёра по игре, товарища для ролевых сессий или что-то большее.";
    public string WelcomeBody2 => "Просматривайте профили других искателей приключений, проявляйте интерес свайпом и, когда чувство окажется взаимным, начните приватный разговор — не покидая Эорзею.";
    public string WelcomePrivacyHeading => "Ваша конфиденциальность:";
    public string WelcomePrivacyBody => "Все чаты и личные переписки защищены сквозным шифрованием — администрация AetherLove не имеет доступа к вашим личным сообщениям.";
    public string WelcomeFeatureDiscoverTitle => "Открывать";
    public string WelcomeFeatureDiscoverBody => "Просматривайте красиво оформленные карточки профилей.";
    public string WelcomeFeatureConnectTitle => "Знакомиться";
    public string WelcomeFeatureConnectBody => "Находите игроков с общими интересами.";
    public string WelcomeFeatureChatTitle => "Общаться";
    public string WelcomeFeatureChatBody => "Пишите своим парам прямо в плагине.";
    public string WelcomePluginLanguageLabel => "Язык плагина";
    public string WelcomePluginLanguageTooltip => "Выберите язык интерфейса AetherLove.\nИзменить его можно позже в настройках.";
    public string WelcomeFooter => "Настройка займёт около 3 минут. Нажмите «Далее», чтобы узнать, как это работает.";
}
