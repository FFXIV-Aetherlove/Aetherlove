using System;
using System.Collections.Generic;
using System.IO;
using AetherLove.Services;
using Dalamud.Interface.Textures;

namespace AetherLove.Os;

/// <summary>Textures for the party roster's avatars, shared by every surface that draws party people (the
/// widget card, the chat's own lines) so the same bytes are decoded once. Re-stored when a member's byte
/// length changes, which is how an avatar swap mid-party shows up without hashing every frame.</summary>
public static class PartyAvatars
{
    private static readonly Dictionary<Guid, (int Stamp, ISharedImmediateTexture? Tex)> Cache = [];

    public static ISharedImmediateTexture? Resolve(OsPartyMember member)
    {
        var bytes = member.AvatarImage;
        if (bytes is not { Length: > 0 })
        {
            return null;
        }
        if (Cache.TryGetValue(member.AccountId, out var cached) && cached.Stamp == bytes.Length)
        {
            return cached.Tex;
        }
        var dir = Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "PartyAvatarCache");
        var tex = AvatarDiskCache.Store(dir, member.AccountId.ToString("N"), bytes);
        Cache[member.AccountId] = (bytes.Length, tex);
        return tex;
    }
}
