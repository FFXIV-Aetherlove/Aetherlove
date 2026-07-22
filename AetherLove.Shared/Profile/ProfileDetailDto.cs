using AetherLove.Shared.Hangouts;
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
    string SoundCloudUrl,
    string SoundCloudName,
    string AppleMusicUrl,
    string AppleMusicName,
    string YouTubeMusicUrl,
    string YouTubeMusicName,
    string FavoriteMovie,
    string FavoriteAnime,
    string FavoriteFFCharacter,

    int WeekdayHoursMask,
    int WeekendHoursMask,

    SyncTool SyncTool,

    ProfilePhotoDto[] Photos,
    Guid[] FlairIds,
    ProfileCharacterDto[]? Characters = null,
    // Supporter cosmetics; the server sends None/false unless the profile currently holds the flag.
    NameStyle NameStyle = NameStyle.None,
    bool IsSupporter = false,
    // Retired: hangouts are account-level and never surface on dating profiles. Always null.
    HangoutSummaryDto? ActiveHangout = null);
