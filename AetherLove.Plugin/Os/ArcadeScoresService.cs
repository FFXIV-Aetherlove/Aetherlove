using System;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using AetherLove.Shared.Arcade;

namespace AetherLove.Os;

/// <summary>Bridges the arcade games to the hub score endpoints; failures are swallowed so a finished
/// run never surfaces an error inside a game.</summary>
public sealed class ArcadeScoresService(AetherHubContext hub) : IArcadeScores
{
    public void SubmitScore(ArcadeScoreSubmissionDto submission, Action<ArcadeScoreResultDto>? onResult = null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await hub.SubmitArcadeScoreAsync(submission).ConfigureAwait(false);
                onResult?.Invoke(result);
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug(ex, "Arcade score submission failed ({Game})", submission.Game);
            }
        });
    }

    public async Task<ArcadeLeaderboardDto?> GetLeaderboardAsync(ArcadeGame game, ArcadeBoard board)
    {
        try
        {
            return await hub.GetArcadeLeaderboardAsync(game, board).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "Arcade leaderboard fetch failed ({Game})", game);
            return null;
        }
    }
}
