using System.Collections.Generic;
using System.IO;
using AetherLove;
using Dalamud.Interface.Textures;

namespace AetherOS.Apps.Stacker;

/// <summary>Loads a single-image skin asset (not a tile sheet): <c>Media/stacker/&lt;role&gt;_&lt;skin&gt;.png</c>,
/// falling back to <c>&lt;role&gt;_default.png</c> when a skin has none of its own. Used for the global
/// background tile and the Hold/Next box label art.</summary>
internal static class StackerTextures
{
    private static readonly Dictionary<string, ISharedImmediateTexture?> Cache = new();

    public static ISharedImmediateTexture? Get(string role, string skin)
    {
        var key = $"{role}_{skin}";
        if (!Cache.TryGetValue(key, out var tex))
        {
            var dir = Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? "";
            var path = Path.Combine(dir, "Media", "stacker", $"{role}_{skin}.png");
            if (!File.Exists(path))
            {
                path = Path.Combine(dir, "Media", "stacker", $"{role}_default.png");
            }
            tex = File.Exists(path) ? UiHost.TextureProvider.GetFromFile(path) : null;
            Cache[key] = tex;
        }
        return tex;
    }
}
