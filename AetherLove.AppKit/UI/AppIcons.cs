using System.Collections.Generic;
using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.UI;

/// <summary>Loads a home-tile app icon from the plugin's <c>Media/appicons/&lt;id&gt;.png</c>, cached per app id.
/// Returns a freshly-resolved handle each call (a cached raw shared-texture handle would dangle across a
/// texture reload); null when the app ships no such file, which keeps the gradient-plus-glyph tile.</summary>
public static class AppIcons
{
    private static readonly Dictionary<string, ISharedImmediateTexture?> Cache = new();

    public static ImTextureID? Tile(string appId)
    {
        if (!Cache.TryGetValue(appId, out var tex))
        {
            var path = Services.Media.MediaPaths.Shipped(Services.Media.MediaPaths.AppIcons, appId + ".png");
            tex = File.Exists(path) ? UiHost.TextureProvider.GetFromFile(path) : null;
            Cache[appId] = tex;
        }
        return tex?.GetWrapOrDefault()?.Handle;
    }
}
