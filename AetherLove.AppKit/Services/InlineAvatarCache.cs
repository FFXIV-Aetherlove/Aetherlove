using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Interface.Textures;

namespace AetherLove.Services;

/// <summary>Textures for avatars that ride inline on a DTO (party members, Echo seats): decoded once per
/// account, re-stored when the byte length changes, which is how an avatar swap shows up without hashing
/// every frame. <paramref name="folder"/> keeps each feature's files apart under the config directory.</summary>
public static class InlineAvatarCache
{
    private static readonly Dictionary<(string Folder, Guid Id), (int Stamp, ISharedImmediateTexture? Tex)> Cache = [];

    public static ISharedImmediateTexture? Resolve(string folder, Guid accountId, byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }
        var key = (folder, accountId);
        if (Cache.TryGetValue(key, out var cached) && cached.Stamp == bytes.Length)
        {
            return cached.Tex;
        }
        var dir = Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, folder);
        var tex = AvatarDiskCache.Store(dir, accountId.ToString("N"), bytes);
        Cache[key] = (bytes.Length, tex);
        return tex;
    }
}
