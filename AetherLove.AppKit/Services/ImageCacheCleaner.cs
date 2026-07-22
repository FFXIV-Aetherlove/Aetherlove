using System;
using System.IO;

namespace AetherLove.Services;

/// <summary>Janitor for the on-disk caches of other players' photos; wiped on startup and shutdown so no lasting local archive accumulates.</summary>
internal static class ImageCacheCleaner
{
    internal static string DeckCacheDir => Dir("DeckCache");
    internal static string ProfileDetailCacheDir => Dir("ProfileDetailCache");
    internal static string MatchOverlayCacheDir => Dir("MatchOverlayCache");

    private static string Dir(string name) =>
        Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, name);

    internal static void ClearDir(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
            {
                return;
            }
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                TryDelete(file);
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[ImageCacheCleaner] Could not enumerate a cache directory.");
        }
    }

    internal static void ClearExcept(string dir, params string[] keepPrefixes)
    {
        try
        {
            if (!Directory.Exists(dir))
            {
                return;
            }
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(file);
                var keep = false;
                foreach (var prefix in keepPrefixes)
                {
                    if (name.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        keep = true;
                        break;
                    }
                }
                if (!keep)
                {
                    TryDelete(file);
                }
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[ImageCacheCleaner] Could not enumerate a cache directory.");
        }
    }

    internal static void PurgeAll()
    {
        ClearDir(DeckCacheDir);
        ClearDir(ProfileDetailCacheDir);
        ClearExcept(MatchOverlayCacheDir, "self_");
    }

    /// <summary>Full local media wipe (the /love clearcache command): all on-disk photo/avatar caches except chat/messenger stores and the user's library.</summary>
    internal static void PurgeAllPhotoCaches()
    {
        var root = UiHost.PluginInterface.ConfigDirectory.FullName;
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(sub);
                if (name is "ChatCache" or "MessengerCache")
                {
                    continue;
                }
                if (name.EndsWith("Cache", StringComparison.OrdinalIgnoreCase))
                {
                    ClearDir(sub);
                }
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[ImageCacheCleaner] Could not enumerate config cache directories.");
        }
        ClearDir(Dir("WebpProbe"));
        ClearDir(Dir("os_appicons"));
        ClearDir(Path.Combine(root, "apps", "messenger", "ImageCache"));
        ClearDir(Path.Combine(root, "apps", "messenger", "AvatarCache"));
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[ImageCacheCleaner] Could not delete a cached image.");
        }
    }
}
