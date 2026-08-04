using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Arcade;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

/// <summary>Arcade score passthroughs.</summary>
public sealed partial class AetherHubContext
{
    public async Task<ArcadeScoreResultDto> SubmitArcadeScoreAsync(ArcadeScoreSubmissionDto submission, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<ArcadeScoreResultDto>("SubmitArcadeScoreAsync", submission, ct).ConfigureAwait(false);

    public async Task<ArcadeLeaderboardDto> GetArcadeLeaderboardAsync(ArcadeGame game, ArcadeBoard board, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<ArcadeLeaderboardDto>("GetArcadeLeaderboardAsync", (short)game, (short)board, ct).ConfigureAwait(false);
}
