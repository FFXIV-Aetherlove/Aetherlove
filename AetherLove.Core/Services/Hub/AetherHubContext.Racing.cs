using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Racing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

/// <summary>Lumi racing passthroughs.</summary>
public sealed partial class AetherHubContext
{
    public async Task<LumiRaceStateDto> GetLumiRaceStateAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<LumiRaceStateDto>("GetLumiRaceStateAsync", ct).ConfigureAwait(false);

    public async Task<LumiRaceLogEntryDto[]> GetLumiRaceLogAsync(int limit, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<LumiRaceLogEntryDto[]>("GetLumiRaceLogAsync", limit, ct).ConfigureAwait(false);

    public async Task<LumiRaceStartResultDto> StartLumiRaceAsync(short difficulty, string? courseKey = null,
        CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<LumiRaceStartResultDto>("StartLumiRaceAsync", difficulty, courseKey, ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task<LumiRacePrizeDto[]> GetLumiRacePackPrizesAsync(Guid packId, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<LumiRacePrizeDto[]>("GetLumiRacePackPrizesAsync", packId, ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task<LumiRacePackDto> RevealLumiRacePackAsync(Guid packId, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<LumiRacePackDto>("RevealLumiRacePackAsync", packId, ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task<LumiRacePartyRunDto> StartLumiRacePartyGatherAsync(CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<LumiRacePartyRunDto>("StartLumiRacePartyGatherAsync", ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task<LumiRacePartyRunDto> JoinLumiRacePartyRunAsync(Guid runId, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<LumiRacePartyRunDto>("JoinLumiRacePartyRunAsync", runId, ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task<LumiRacePartyRunDto> BeginLumiRacePartyRunAsync(Guid runId, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<LumiRacePartyRunDto>("BeginLumiRacePartyRunAsync", runId, ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task CancelLumiRacePartyRunAsync(Guid runId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct))
                .InvokeAsync("CancelLumiRacePartyRunAsync", runId, ct)
                .ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }

    public async Task<LumiRacePartyRunDto?> GetLumiRacePartyRunAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<LumiRacePartyRunDto?>("GetLumiRacePartyRunAsync", ct).ConfigureAwait(false);
}
