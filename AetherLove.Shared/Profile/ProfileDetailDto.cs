using AetherLove.Shared.Profile.Enums;
using MessagePack;

namespace AetherLove.Shared.Profile;

/// <summary>One photo attached to a profile detail view.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ProfilePhotoDto(
    int Order,
    bool IsNsfw,
    byte[] WebpBytes);

/// <summary>Full read-only profile snapshot for the expanded profile view.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ProfileDetailDto(
    Guid ProfileId,

    string DisplayName,
    string Bio,

    Race Race,
    Gender Gender,
    Region Region,

    Language LanguageMask,
    ContentInterest ContentInterestMask,
    LookingFor LookingForMask,

    bool NsfwEnabled,
    bool IsNsfw,

    int TimezoneOffsetMinutes,

    Job FavoriteJob,
    Expansion FavoriteExpansion,

    string FavoriteLocationName,
    string SpotifyTrackId,
    string SpotifyTrackName,
    string FavoriteMovie,
    string FavoriteAnime,
    string FavoriteFFCharacter,

    int WeekdayHoursMask,
    int WeekendHoursMask,

    SyncTool SyncTool,

    ProfilePhotoDto[] Photos);
