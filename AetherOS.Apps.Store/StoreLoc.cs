using System;
using AetherLove;
using AetherLove.Shared.Profile.Enums;
using AetherLove.Shared.Store;

namespace AetherOS.Apps.Store;

/// <summary>Picks the caller-language text out of the six-column store DTOs, English fallback, resolved
/// live from the plugin language so a language switch re-renders the catalog without a refetch.</summary>
internal static class StoreLoc
{
    public static Language Current =>
        Enum.TryParse<Language>(UiHost.Configuration.PluginLanguage, ignoreCase: true, out var lang)
            ? lang
            : Language.English;

    public static string Name(StoreProductDto p) => Pick(
        p.NameEnglish, p.NameSpanish, p.NameFrench, p.NameRussian, p.NameGerman, p.NamePortuguese);

    public static string Description(StoreProductDto p) => Pick(
        p.DescriptionEnglish, p.DescriptionSpanish, p.DescriptionFrench,
        p.DescriptionRussian, p.DescriptionGerman, p.DescriptionPortuguese);

    public static string Name(StoreCategoryDto c) => Pick(
        c.NameEnglish, c.NameSpanish, c.NameFrench, c.NameRussian, c.NameGerman, c.NamePortuguese);

    public static string Name(StoreSaleBannerDto s) => Pick(
        s.NameEnglish, s.NameSpanish, s.NameFrench, s.NameRussian, s.NameGerman, s.NamePortuguese);

    public static string Name(StoreBundleItemDto b) => Pick(
        b.NameEnglish, b.NameSpanish, b.NameFrench, b.NameRussian, b.NameGerman, b.NamePortuguese);

    public static string Title(StoreCollectionDto c) => Pick(
        c.TitleEnglish, c.TitleSpanish, c.TitleFrench, c.TitleRussian, c.TitleGerman, c.TitlePortuguese);

    public static string Subtitle(StoreCollectionDto c) => Pick(
        c.SubtitleEnglish, c.SubtitleSpanish, c.SubtitleFrench,
        c.SubtitleRussian, c.SubtitleGerman, c.SubtitlePortuguese);

    private static string Pick(string en, string? es, string? fr, string? ru, string? de, string? pt)
    {
        var s = Current switch
        {
            Language.Spanish => es,
            Language.French => fr,
            Language.Russian => ru,
            Language.German => de,
            Language.Portuguese => pt,
            _ => en,
        };
        return string.IsNullOrWhiteSpace(s) ? en : s!;
    }
}
