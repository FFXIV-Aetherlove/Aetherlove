using System.Collections.Generic;

namespace AetherLove.Emoji;

/// <summary>Local (per-install) favorite-emoji list, backed by <see cref="Config.Configuration.FavoriteEmojis"/>.
/// Every mutation persists immediately, matching the app's other client-side toggles. Keyed by bare shortcode
/// name (no colons).</summary>
internal static class EmojiFavorites
{
    private static List<string> List => Plugin.Configuration.FavoriteEmojis;

    internal static bool Any => List.Count > 0;

    internal static IReadOnlyList<string> All => List;

    internal static bool Contains(string name) => List.Contains(name);

    /// <summary>Adds (newest last) or removes, then persists. Returns the new state (true = now favorited).</summary>
    internal static bool Toggle(string name)
    {
        var nowFavorite = !List.Contains(name);
        if (nowFavorite)
        {
            List.Add(name);
        }
        else
        {
            List.Remove(name);
        }
        Plugin.Configuration.Save();
        return nowFavorite;
    }
}
