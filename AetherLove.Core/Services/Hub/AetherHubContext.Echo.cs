using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.EchoVidya;
using AetherLove.Shared.Hangouts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

/// <summary>Echo watch-together hub methods (account-scoped, independent of the active dating profile).</summary>
public sealed partial class AetherHubContext
{
    public async Task<EchoRoomSnapshotDto> CreateEchoRoomAsync(CreateEchoRoomRequest req, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<EchoRoomSnapshotDto>("CreateEchoRoomAsync", req, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<EchoRoomSnapshotDto> JoinEchoRoomAsync(string code, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<EchoRoomSnapshotDto>("JoinEchoRoomAsync", code, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    /// <summary>Full room state for a first draw or a reconnect; the result replaces the client's room state.</summary>
    public async Task<EchoRoomSnapshotDto> GetEchoRoomSyncAsync(Guid roomId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<EchoRoomSnapshotDto>("GetEchoRoomSyncAsync", roomId, ct).ConfigureAwait(false);

    /// <summary>The caller's live room, or null. Recovers a session the client no longer knows about, e.g.
    /// after a plugin reload dropped the in-memory room state.</summary>
    public async Task<EchoRoomSnapshotDto?> GetMyEchoRoomAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<EchoRoomSnapshotDto?>("GetMyEchoRoomAsync", ct).ConfigureAwait(false);

    public async Task LeaveEchoRoomAsync(Guid roomId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("LeaveEchoRoomAsync", roomId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task EndEchoRoomAsync(Guid roomId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("EndEchoRoomAsync", roomId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task KickEchoMemberAsync(Guid roomId, Guid accountId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("KickEchoMemberAsync", roomId, accountId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task SetEchoHostOnlyAsync(Guid roomId, bool hostOnly, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SetEchoHostOnlyAsync", roomId, hostOnly, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    /// <summary>Accepts a full video URL or a bare id; the server resolves and validates it.</summary>
    public async Task<EchoPlaylistEntryDto> AddEchoPlaylistEntryAsync(Guid roomId, string urlOrId, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<EchoPlaylistEntryDto>("AddEchoPlaylistEntryAsync", roomId, urlOrId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task RemoveEchoPlaylistEntryAsync(Guid roomId, Guid entryId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("RemoveEchoPlaylistEntryAsync", roomId, entryId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task SetEchoPlaybackAsync(Guid roomId, bool isPlaying, double position, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SetEchoPlaybackAsync", roomId, isPlaying, position, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task SetEchoCurrentEntryAsync(Guid roomId, Guid entryId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SetEchoCurrentEntryAsync", roomId, entryId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    /// <summary>Moves to the next entry. The server ignores the call when the ended entry is no longer the
    /// current one, so every client reporting the same end is harmless.</summary>
    public async Task AdvanceEchoPlaylistAsync(Guid roomId, Guid endedEntryId, bool failed, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("AdvanceEchoPlaylistAsync", roomId, endedEntryId, failed, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task SendEchoChatAsync(Guid roomId, string text, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SendEchoChatAsync", roomId, text, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    /// <summary>Null when the room ended or the caller may not see it.</summary>
    public async Task<EchoRoomCardDto?> GetEchoRoomCardAsync(Guid roomId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<EchoRoomCardDto?>("GetEchoRoomCardAsync", roomId, ct).ConfigureAwait(false);

    /// <summary>Publishes the caller's live room as a watch-party hangout. The category is forced server-side,
    /// so the request's own category is ignored.</summary>
    public async Task<HangoutSummaryDto> PublishEchoRoomHangoutAsync(
        Guid roomId, CreateHangoutRequest req, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<HangoutSummaryDto>("PublishEchoRoomHangoutAsync", roomId, req, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    /// <summary>The current playback host build; null when no host is published for this client.</summary>
    public async Task<EchoHostManifestDto?> GetEchoHostManifestAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<EchoHostManifestDto?>("GetEchoHostManifestAsync", ct).ConfigureAwait(false);
}
