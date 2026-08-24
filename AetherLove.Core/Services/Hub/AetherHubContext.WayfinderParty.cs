using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Wayfinder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

/// <summary>Party-hunt and activity-binding hub methods (account-scoped, ride the together party).</summary>
public sealed partial class AetherHubContext
{
    public async Task BindEchoRoomToPartyAsync(Guid roomId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("BindEchoRoomToPartyAsync", roomId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task UnbindEchoRoomFromPartyAsync(Guid roomId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("UnbindEchoRoomFromPartyAsync", roomId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<WayfinderPartyRunDto> StartWayfinderPartyGatherAsync(
        int worldId, short unlockedThrough, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<WayfinderPartyRunDto>(
                "StartWayfinderPartyGatherAsync", worldId, unlockedThrough, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<WayfinderPartyRunDto> JoinWayfinderPartyRunAsync(
        Guid runId, int worldId, short unlockedThrough, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<WayfinderPartyRunDto>(
                "JoinWayfinderPartyRunAsync", runId, worldId, unlockedThrough, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<WayfinderPartyRunDto> BeginWayfinderPartyRunAsync(Guid runId, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<WayfinderPartyRunDto>(
                "BeginWayfinderPartyRunAsync", runId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task CancelWayfinderPartyRunAsync(Guid runId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("CancelWayfinderPartyRunAsync", runId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<WayfinderGroupSubmitResultDto> SubmitWayfinderPartyAttemptAsync(
        WayfinderGroupSubmitDto dto, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<WayfinderGroupSubmitResultDto>(
                "SubmitWayfinderPartyAttemptAsync", dto, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<WayfinderPartyRunDto?> GetWayfinderPartyRunAsync(bool withImage, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<WayfinderPartyRunDto?>(
            "GetWayfinderPartyRunAsync", withImage, ct).ConfigureAwait(false);
}
