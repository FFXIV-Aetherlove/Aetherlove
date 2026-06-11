using AetherLove.Shared.Profile.Enums;
using MessagePack;

namespace AetherLove.Shared.Profile;

/// <summary>Onboarding step 1: identity, likes, dislikes.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record BasicProfileDto(
    string DisplayName,
    string Bio,
    Race Race,
    Gender Gender,
    Region Region,
    Language LanguageMask,
    ContentInterest ContentInterestMask,
    LookingFor LookingForMask,
    bool NsfwEnabled,
    string Timezone,
    Job FavoriteJob,
    Expansion FavoriteExpansion,
    string SpotifyTrackId,
    string SpotifyTrackName,
    string FavoriteMovie,
    string FavoriteAnime,
    string FavoriteFFCharacter,
    int WeekdayHoursMask,
    int WeekendHoursMask,
    SyncTool SyncTool);
