using MessagePack;

namespace AetherLove.Shared.Profile;

/// <summary>The caller's own activity stats for the "My" screen. <see cref="SwipeCount"/> is the caller's own
/// swipes (kept for the client-side match-percentage calc), <see cref="LovesYouCount"/> is how many people
/// have liked the caller.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MyStatsDto(
    int SwipeCount,
    int MatchCount,
    int LovesYouCount);
