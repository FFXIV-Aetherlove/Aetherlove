using System;
using System.IO;
using System.Security.Cryptography;
using Dalamud.Interface.Textures;

namespace AetherLove.Services;

/// <summary>
/// Writes avatar bytes to disk under a content-hashed filename (<c>{key}_{hash}.webp</c>) and returns the
/// texture. The hash makes <c>GetFromFile</c> reload when the avatar changes — overwriting one fixed path
/// would keep serving the texture it already cached for that path. Older copies for the same key are swept.
/// </summary>
public static class AvatarDiskCache
{
    public static ISharedImmediateTexture? Store(string cacheDir, string key, byte[] bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }
        try
        {
            Directory.CreateDirectory(cacheDir);
            var path = Path.Combine(cacheDir, $"{key}_{Convert.ToHexString(SHA256.HashData(bytes), 0, 6)}.webp");
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, bytes);
            }
            var tex = Plugin.TextureProvider.GetFromFile(path);
            SweepStale(cacheDir, key, path);
            return tex;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[AvatarDiskCache] Could not store an avatar.");
            return null;
        }
    }

    private static void SweepStale(string cacheDir, string key, string keep)
    {
        foreach (var file in Directory.EnumerateFiles(cacheDir, $"{key}*.webp"))
        {
            if (string.Equals(file, keep, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[AvatarDiskCache] Could not delete a stale avatar cache file.");
            }
        }
    }
}
