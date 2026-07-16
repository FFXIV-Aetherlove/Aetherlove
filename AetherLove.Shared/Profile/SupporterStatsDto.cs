using MessagePack;

namespace AetherLove.Shared.Profile;

/// <summary>Supporter-only personal analytics, computed server-side and gated on the supporter flag.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SupporterStatsDto(
    int LikesReceived,
    int SuperlikesReceived,
    int LikesGiven,
    int PassesGiven,
    int Matches,
    int Impressions,
    int ProfileViews,
    double LikeRateGivenPct,
    double MatchRatePct);
