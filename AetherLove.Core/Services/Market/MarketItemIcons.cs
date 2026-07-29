using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Services.Market;

/// <summary>Shared game-item icon lookup for the Market app and the chat cards. Caches the
/// ISharedImmediateTexture and resolves the handle per frame; the resolved handle must never be stored
/// across frames.</summary>
public static class MarketItemIcons
{
    private static readonly Dictionary<uint, ISharedImmediateTexture?> Cache = [];

    public static ImTextureID? Get(ushort iconId)
    {
        if (iconId == 0)
        {
            return null;
        }
        if (!Cache.TryGetValue(iconId, out var tex))
        {
            try
            {
                tex = UiHost.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[Market] Icon {iconId} failed to load: {ex.Message}");
                tex = null;
            }
            Cache[iconId] = tex;
        }
        return tex?.GetWrapOrDefault()?.Handle;
    }
}
