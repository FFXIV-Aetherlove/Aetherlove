using System.Collections.Generic;
using System.IO;
using Dalamud.Interface.Textures;

namespace AetherLove.Services;

/// <summary>Lazy-loaded cache of language-flag textures shown on profile headers and the language picker.</summary>
public static class LanguageFlagService
{
    private static readonly Dictionary<string, string> NameToFile = new()
    {
        ["English"] = "flag_en.png",
        ["Spanish"] = "flag_es.png",
        ["French"] = "flag_fr.png",
        ["Russian"] = "flag_ru.png",
        ["German"] = "flag_de.png",
        ["Portuguese"] = "flag_pt.png",
        ["Japanese"] = "flag_jp.png",
    };

    private static readonly Dictionary<string, ISharedImmediateTexture?> Cache = new();
    private static string? _mediaDir;

    public static ISharedImmediateTexture? GetFlag(string languageName)
    {
        if (Cache.TryGetValue(languageName, out var existing))
        {
            return existing;
        }

        if (!NameToFile.TryGetValue(languageName, out var file))
        {
            Cache[languageName] = null;
            return null;
        }

        _mediaDir ??= Path.Combine(
            Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? "",
            "Media");

        var path = Path.Combine(_mediaDir, file);
        if (!File.Exists(path))
        {
            Cache[languageName] = null;
            return null;
        }

        var tex = UiHost.TextureProvider.GetFromFile(path);
        Cache[languageName] = tex;
        return tex;
    }
}
