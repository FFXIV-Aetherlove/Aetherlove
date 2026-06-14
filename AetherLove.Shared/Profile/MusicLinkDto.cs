using AetherLove.Shared.Profile.Enums;
using MessagePack;

namespace AetherLove.Shared.Profile;

/// <summary>Result of resolving a pasted music link server-side: the normalised reference (a track id for
/// Spotify, a canonical URL otherwise) and the curated, platform-derived song name (empty when the name
/// couldn't be fetched but the link is still valid). Returned null by the hub when the link is invalid.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MusicLinkDto(
    MusicProvider Provider,
    string Ref,
    string Name);
