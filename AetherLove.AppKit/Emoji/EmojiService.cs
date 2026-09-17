// Attribution: Derived from XIVInstantMessenger's EmojiLoader
// Source: https://github.com/NightmareXIV/XIVInstantMessenger

using System.Collections.Generic;
using System.IO;
using AetherLove.Services.Media;
using Dalamud.Interface.Textures;

namespace AetherLove.Emoji;

/// <summary>Every emoji PNG from the downloaded <c>emoji/</c> pack, keyed by file name. The set is loaded
/// on demand and swapped whole, so a pack that lands mid-session shows up after one <see cref="Reload"/>
/// and readers never see a half-filled table.</summary>
public sealed class EmojiService
{
    private IReadOnlyDictionary<string, ISharedImmediateTexture> _emoji =
        new Dictionary<string, ISharedImmediateTexture>(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>True once at least one emoji has been found on disk.</summary>
    public bool Ready => _emoji.Count > 0;

    /// <summary>Rescans the emoji folder and replaces the table. Safe to call any time; a missing folder
    /// leaves the table empty, which every consumer already treats as "no emoji yet".</summary>
    public void Reload()
    {
        var dir = MediaPaths.Downloaded(MediaPaths.Emoji);
        var found = new Dictionary<string, ISharedImmediateTexture>(System.StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir))
        {
            UiHost.Log.Information("[EmojiService] No emoji folder yet at " + dir);
            _emoji = found;
            return;
        }

        foreach (var file in Directory.GetFiles(dir, "*.png"))
        {
            var key = Path.GetFileNameWithoutExtension(file);
            found[key] = UiHost.TextureProvider.GetFromFile(file);
        }

        _emoji = found;
        UiHost.Log.Information($"[EmojiService] Loaded {found.Count} emoji.");
    }

    public ISharedImmediateTexture? GetEmoji(string name)
    {
        _emoji.TryGetValue(name, out var tex);
        return tex;
    }

    public IReadOnlyDictionary<string, ISharedImmediateTexture> All => _emoji;
}
