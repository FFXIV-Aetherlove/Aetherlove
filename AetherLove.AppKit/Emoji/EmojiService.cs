// Attribution: Derived from XIVInstantMessenger's EmojiLoader
// Source: https://github.com/NightmareXIV/XIVInstantMessenger

using System.Collections.Generic;
using System.IO;
using Dalamud.Interface.Textures;

namespace AetherLove.Emoji;

/// <summary>Loads and caches every emoji PNG from <c>Media/emoji/</c>.</summary>
public sealed class EmojiService
{
    private readonly Dictionary<string, ISharedImmediateTexture> _emoji =
        new(System.StringComparer.OrdinalIgnoreCase);

    public EmojiService()
    {
        Load();
    }

    private void Load()
    {
        var dir = Path.Combine(
            Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName)!,
            "Media", "emoji");

        if (!Directory.Exists(dir))
        {
            UiHost.Log.Warning("[EmojiService] emoji folder not found: " + dir);
            return;
        }

        foreach (var file in Directory.GetFiles(dir, "*.png"))
        {
            var key = Path.GetFileNameWithoutExtension(file);
            _emoji[key] = UiHost.TextureProvider.GetFromFile(file);
        }

        UiHost.Log.Information($"[EmojiService] Loaded {_emoji.Count} emoji.");
    }

    public ISharedImmediateTexture? GetEmoji(string name)
    {
        _emoji.TryGetValue(name, out var tex);
        return tex;
    }

    public IReadOnlyDictionary<string, ISharedImmediateTexture> All => _emoji;
}
