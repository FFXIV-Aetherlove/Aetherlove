using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using AetherLove.Shared.Flairs;
using AetherLove.Shared.Profile.Enums;

namespace AetherLove.Services;

/// <summary>Client-side cache of the server flair catalog, refreshed on every (re)connect; entries carry all languages.</summary>
public sealed class FlairCatalog
{
    private readonly AetherLoveHubClient _hub;
    private Dictionary<Guid, FlairDto> _byId = new();

    public FlairCatalog(AetherLoveHubClient hub)
    {
        _hub = hub;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var catalog = await _hub.GetFlairCatalogAsync(ct).ConfigureAwait(false);
            var map = new Dictionary<Guid, FlairDto>(catalog.Length);
            foreach (var f in catalog)
            {
                map[f.Id] = f;
            }
            _byId = map;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[FlairCatalog] Refresh failed.");
        }
    }

    public FlairDto? Get(Guid id) => _byId.TryGetValue(id, out var f) ? f : null;

    public static Language ResolveLanguage(string pluginLanguage) =>
        Enum.TryParse<Language>(pluginLanguage, ignoreCase: true, out var lang) ? lang : Language.English;

    public static string Text(FlairDto f, Language lang) => Pick(lang,
        f.TextEnglish, f.TextSpanish, f.TextFrench, f.TextRussian, f.TextGerman, f.TextPortuguese);

    public static string Description(FlairDto f, Language lang) => Pick(lang,
        f.DescriptionEnglish, f.DescriptionSpanish, f.DescriptionFrench, f.DescriptionRussian, f.DescriptionGerman, f.DescriptionPortuguese);

    private static string Pick(Language lang, string en, string? es, string? fr, string? ru, string? de, string? pt)
    {
        var s = lang switch
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
