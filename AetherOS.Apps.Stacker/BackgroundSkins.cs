using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherLove;
using Dalamud.Interface.Textures;

namespace AetherOS.Apps.Stacker;

/// <summary>Loads and slices a background sprite sheet: two tiles, light then dark, no bleed margin.
/// Falls back to <c>bg_retro.png</c> when a skin doesn't ship its own background file.</summary>
internal static class BackgroundSkins
{
    private static readonly Dictionary<string, ISharedImmediateTexture?> Cache = new();

    /// <summary>The named skin's background texture, or the retro default if it has none of its own.</summary>
    public static ISharedImmediateTexture? Get(string skinName)
    {
        if (!Cache.TryGetValue(skinName, out var tex))
        {
            var dir = Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? "";
            var path = Path.Combine(dir, "Media", "stacker", $"bg_{skinName}.png");
            if (!File.Exists(path))
            {
                path = Path.Combine(dir, "Media", "stacker", "bg_retro.png");
            }
            tex = File.Exists(path) ? UiHost.TextureProvider.GetFromFile(path) : null;
            Cache[skinName] = tex;
        }
        return tex;
    }

    public static (Vector2 Uv0, Vector2 Uv1) LightUv(Vector2 textureSize) => TileUv(textureSize, first: true);

    public static (Vector2 Uv0, Vector2 Uv1) DarkUv(Vector2 textureSize) => TileUv(textureSize, first: false);

    /// <summary>Splits the sheet into its two tiles along whichever axis is twice the other, so it works
    /// whether the artist laid them out side by side or stacked.</summary>
    private static (Vector2 Uv0, Vector2 Uv1) TileUv(Vector2 textureSize, bool first)
    {
        if (textureSize.X >= textureSize.Y)
        {
            var half = textureSize.X * 0.5f;
            var x0 = first ? 0f : half;
            return (new Vector2(x0 / textureSize.X, 0f), new Vector2((x0 + half) / textureSize.X, 1f));
        }
        var halfH = textureSize.Y * 0.5f;
        var y0 = first ? 0f : halfH;
        return (new Vector2(0f, y0 / textureSize.Y), new Vector2(1f, (y0 + halfH) / textureSize.Y));
    }
}
