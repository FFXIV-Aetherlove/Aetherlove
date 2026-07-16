using System;
using System.IO;
using System.Security.Cryptography;
using Dalamud.Interface.Textures;

namespace AetherLove.Services;

/// <summary>Writes avatar bytes under a content-hashed filename; overwriting one fixed path would keep
/// serving the texture <c>GetFromFile</c> already cached for it.</summary>
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
            var path = Path.Combine(cacheDir, $"{key}_{Convert.ToHexString(SHA256.HashData(bytes), 0, 6)}{ImageFormat.ExtensionFor(bytes)}");
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
        foreach (var file in Directory.EnumerateFiles(cacheDir, $"{key}_*"))
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
