using System;
using System.IO;

namespace AetherLove.Services;

/// <summary>
/// Janitor for the on-disk image caches that hold other players' photos: the swipe deck portraits
/// (<c>DeckCache</c>), the photo set of a viewed profile (<c>ProfileDetailCache</c>), and match-overlay peer
/// avatars (<c>MatchOverlayCache</c>). The files exist only so the textures can be drawn; they are pruned to
/// the live working set as each cache is rewritten and wiped on startup and shutdown, so the plugin never
/// accumulates a lasting local archive of other users' images.
/// </summary>
internal static class ImageCacheCleaner
{
    internal static string DeckCacheDir => Dir("DeckCache");
    internal static string ProfileDetailCacheDir => Dir("ProfileDetailCache");
    internal static string MatchOverlayCacheDir => Dir("MatchOverlayCache");

    private static string Dir(string name) =>
        Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, name);

    /// <summary>Deletes every file in <paramref name="dir"/>, best-effort.</summary>
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
            Plugin.Log.Warning(ex, "[ImageCacheCleaner] Could not enumerate a cache directory.");
        }
    }

    /// <summary>Deletes files in <paramref name="dir"/> whose name doesn't start with one of
    /// <paramref name="keepPrefixes"/>, used to keep the current item while dropping the rest.</summary>
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
            Plugin.Log.Warning(ex, "[ImageCacheCleaner] Could not enumerate a cache directory.");
        }
    }

    /// <summary>Wipes the deck and profile-detail caches and every match-overlay avatar except the user's own
    /// (<c>self_*</c>). Run on startup to clear a prior session's leftovers and on shutdown.</summary>
    internal static void PurgeAll()
    {
        ClearDir(DeckCacheDir);
        ClearDir(ProfileDetailCacheDir);
        ClearExcept(MatchOverlayCacheDir, "self_");
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[ImageCacheCleaner] Could not delete a cached image.");
        }
    }
}
