using System;
using System.Threading.Tasks;
using AetherLove.Shared.Arcade;

namespace AetherLove.Os;

/// <summary>Server score tracking for the arcade games. The client only reports what happened; the
/// server owns validation, bests and leaderboards, so a tampered submission gains nothing lasting.</summary>
public interface IArcadeScores
{
    /// <summary>Fire-and-forget submission of a finished run; safe to call offline (the run is simply
    /// not recorded). The optional callback delivers the server verdict for a "new best!" flourish.</summary>
    void SubmitScore(ArcadeScoreSubmissionDto submission, Action<ArcadeScoreResultDto>? onResult = null);

    /// <summary>Top-100 board for a game; null when offline or the request fails.</summary>
    Task<ArcadeLeaderboardDto?> GetLeaderboardAsync(ArcadeGame game, ArcadeBoard board);
}
