using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Interface.Textures;

namespace AetherLove.UI;

/// <summary>Optional custom icon art loaded from <c>Media/icons/&lt;name&gt;.png</c> next to the plugin. Callers fall
/// back to a FontAwesome glyph when the file is absent, so bespoke art can be dropped in later with no code change
/// (a missing file is re-checked each frame, so a newly added icon appears without a plugin reload).</summary>
public static class CustomIcons
{
    private static readonly Dictionary<string, ISharedImmediateTexture> _found = new();

    /// <summary>The loaded texture for <paramref name="name"/>, or null when no art has been provided yet.</summary>
    public static ISharedImmediateTexture? Get(string name)
    {
        if (_found.TryGetValue(name, out var cached))
        {
            return cached;
        }
        try
        {
            var dir = Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? string.Empty;
            var path = Path.Combine(dir, "Media", "icons", name + ".png");
            if (File.Exists(path))
            {
                var tex = UiHost.TextureProvider.GetFromFile(path);
                _found[name] = tex;
                return tex;
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, $"[CustomIcons] Failed to load '{name}'.");
        }
        return null;
    }
}
