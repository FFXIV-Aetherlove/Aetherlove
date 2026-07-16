using System;
using MessagePack;

namespace AetherLove.Shared.Profile;

/// <summary>One RP character (OC) on a profile. Presentational metadata only: never matchable and never
/// filtered on. <see cref="ImageBytes"/> is null when the character has no image or the viewer may not see
/// it (peers only ever receive approved images); <see cref="ImageIsNsfw"/> drives the client-side blur
/// exactly like <see cref="ProfilePhotoDto.IsNsfw"/>.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ProfileCharacterDto(
    Guid Id,
    string Name,
    string Bio,
    byte[]? ImageBytes,
    bool ImageIsNsfw,
    // Supporter-only extra images; peers only ever receive approved ones while the owner holds the flag.
    CharacterImageDto[]? ExtraImages = null);

/// <summary>One supporter extra image on an RP character.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record CharacterImageDto(
    short SortOrder,
    byte[] Webp,
    bool IsNsfw);

/// <summary>The caller's own RP characters plus their allowance. <see cref="MaxCharacters"/> is resolved
/// server-side (config, per-role) so a future supporter tier can raise it without client changes.
/// <see cref="ProfileIsNsfw"/> mirrors the caller's own NSFW flag so the editor knows whether the
/// per-image NSFW toggle is available.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MyCharactersDto(
    ProfileCharacterDto[] Characters,
    int MaxCharacters,
    bool ProfileIsNsfw);

/// <summary>One RP character in a save request. Null <see cref="Id"/> creates; a known id updates.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record CharacterSaveDto(
    Guid? Id,
    string Name,
    string Bio);

/// <summary>Full ordered replacement of the caller's RP characters: array index becomes the sort order and
/// characters absent from the list are deleted. Images are managed separately per character.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SaveCharactersRequest(
    CharacterSaveDto[] Characters);
