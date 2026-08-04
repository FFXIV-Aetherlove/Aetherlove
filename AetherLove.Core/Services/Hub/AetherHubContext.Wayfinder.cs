using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Wayfinder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

/// <summary>Wayfinder location-game passthroughs.</summary>
public sealed partial class AetherHubContext
{
    public async Task<WayfinderStateDto> GetWayfinderStateAsync(short unlockedThrough, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<WayfinderStateDto>("GetWayfinderStateAsync", unlockedThrough, ct).ConfigureAwait(false);

    public async Task<WayfinderStartResultDto> StartWayfinderChallengeAsync(short unlockedThrough, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<WayfinderStartResultDto>("StartWayfinderChallengeAsync", unlockedThrough, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<WayfinderSubmitResultDto> SubmitWayfinderAttemptAsync(WayfinderSubmitDto dto, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<WayfinderSubmitResultDto>("SubmitWayfinderAttemptAsync", dto, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<WayfinderStateDto> AbandonWayfinderChallengeAsync(short unlockedThrough, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<WayfinderStateDto>("AbandonWayfinderChallengeAsync", unlockedThrough, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<WayfinderNewChallengeResultDto> SubmitWayfinderChallengeAsync(WayfinderNewChallengeDto dto, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<WayfinderNewChallengeResultDto>("SubmitWayfinderChallengeAsync", dto, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }
}
