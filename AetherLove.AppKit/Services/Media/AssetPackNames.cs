using System;
using AetherLove.Services.Localization;
using AetherLove.Shared.Assets;

namespace AetherLove.Services.Media;

/// <summary>What a pack is called on screen.</summary>
public static class AssetPackNames
{
    /// <summary>The pack's translated display name, or its raw name for a pack the tables do not know yet.</summary>
    public static string Display(string pack)
    {
        var key = AssetPacks.DisplayKey(pack);
        var text = Loc.T(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? pack : text;
    }
}
