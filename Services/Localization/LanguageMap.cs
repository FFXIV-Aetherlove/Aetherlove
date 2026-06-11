using System.Collections.Generic;

namespace AetherLove.Services.Localization;

internal static class LanguageMap
{
    /// <summary>Merges the per-area string fragments into one lookup; later fragments win on key collisions.</summary>
    internal static Dictionary<string, string> Merge(params IReadOnlyDictionary<string, string>[] fragments)
    {
        var map = new Dictionary<string, string>();
        foreach (var fragment in fragments)
        {
            foreach (var pair in fragment)
            {
                map[pair.Key] = pair.Value;
            }
        }
        return map;
    }
}
