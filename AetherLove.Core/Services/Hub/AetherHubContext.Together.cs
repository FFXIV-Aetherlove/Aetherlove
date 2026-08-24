using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Together;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

/// <summary>Together-mode party hub methods (account-scoped, independent of the active dating profile).</summary>
public sealed partial class AetherHubContext
{
    public async Task<TogetherPartySnapshotDto> CreateTogetherPartyAsync(CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<TogetherPartySnapshotDto>("CreateTogetherPartyAsync", ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<TogetherPartySnapshotDto> JoinTogetherPartyAsync(string code, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<TogetherPartySnapshotDto>("JoinTogetherPartyAsync", code, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    /// <summary>Full party state for a first draw or a reconnect; the result replaces the client's state.</summary>
    public async Task<TogetherPartySnapshotDto> GetTogetherPartySyncAsync(Guid partyId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<TogetherPartySnapshotDto>("GetTogetherPartySyncAsync", partyId, ct).ConfigureAwait(false);

    /// <summary>The caller's live party, or null. Recovers a session the client no longer knows about, e.g.
    /// after a plugin reload dropped the in-memory party state.</summary>
    public async Task<TogetherPartySnapshotDto?> GetMyTogetherPartyAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<TogetherPartySnapshotDto?>("GetMyTogetherPartyAsync", ct).ConfigureAwait(false);

    public async Task LeaveTogetherPartyAsync(Guid partyId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("LeaveTogetherPartyAsync", partyId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task EndTogetherPartyAsync(Guid partyId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("EndTogetherPartyAsync", partyId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task SendTogetherChatAsync(Guid partyId, string text, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SendTogetherChatAsync", partyId, text, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task KickTogetherMemberAsync(Guid partyId, Guid memberAccountId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("KickTogetherMemberAsync", partyId, memberAccountId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<TogetherPartyCardDto?> GetTogetherPartyCardAsync(Guid partyId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<TogetherPartyCardDto?>("GetTogetherPartyCardAsync", partyId, ct).ConfigureAwait(false);
}
