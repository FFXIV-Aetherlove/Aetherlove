namespace AetherLove.Services.Localization;

/// <summary>Resolves a keyed UI string for the active language, falling back to English then the key.</summary>
public static class Loc
{
    public static string T(string key)
    {
        if (LanguageProvider.Current.Strings.TryGetValue(key, out var value))
        {
            return value;
        }
        if (LanguageProvider.English.Strings.TryGetValue(key, out var english))
        {
            return english;
        }
        return key;
    }

    public static string T(string key, params object[] args) => string.Format(T(key), args);
}
