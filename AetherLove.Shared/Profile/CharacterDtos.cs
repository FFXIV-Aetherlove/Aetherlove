using System;
using MessagePack;

namespace AetherLove.Shared.Profile;

/// <summary>Presentational RP character; never matchable or filtered. ImageBytes null when not visible to peer; ImageIsNsfw mirrors ProfilePhotoDto blur.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ProfileCharacterDto(
    Guid Id,
    string Name,
    string Bio,
    byte[]? ImageBytes,
    bool ImageIsNsfw,
    // Supporter-only extra images; peers see only approved ones while owner holds flag.
    CharacterImageDto[]? ExtraImages = null);

/// <summary>One supporter extra image on an RP character.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record CharacterImageDto(
    short SortOrder,
    byte[] Webp,
    bool IsNsfw);

/// <summary>Caller's RP characters with allowance. MaxCharacters is server-resolved; ProfileIsNsfw gates the per-image NSFW toggle.</summary>
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
