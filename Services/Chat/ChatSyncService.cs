using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using AetherLove.Shared.Messaging;

namespace AetherLove.Services.Chat;

/// <summary>Pulls the "everything changed since my cursor" delta from the server into <see cref="ChatCacheStore"/>.
/// Single-flight: overlapping callers (connect, screen opens, reconnects) coalesce into one run. Errors are logged,
/// never surfaced. On a fresh install the first run is the full build; afterwards each run is a cheap increment.</summary>
public sealed class ChatSyncService
{
    private readonly AetherLoveHubClient _hub;
    private readonly ChatCacheStore _cache;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ChatSyncService(AetherLoveHubClient hub, ChatCacheStore cache)
    {
        _hub = hub;
        _cache = cache;
    }

    public ChatCacheStore Cache => _cache;

    /// <summary>Drains the delta (paging until <c>HasMore</c> is false) into the cache. No-op when disconnected or
    /// when a sync is already running.</summary>
    public async Task SyncAsync(CancellationToken ct = default)
    {
        if (!_hub.IsConnected)
        {
            return;
        }
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return;
        }
        try
        {
            var (mu, mc, ku) = _cache.Cursors;
            while (true)
            {
                var delta = await _hub.GetChatDeltaAsync(new ChatDeltaRequest(mu, mc, ku), ct).ConfigureAwait(false);
                _cache.ApplyDelta(delta);
                mu = delta.NextMsgCursorUtc;
                mc = delta.NextMsgCursorCreatedUtc;
                ku = delta.NextMatchCursorUtc;
                if (!delta.HasMore)
                {
                    break;
                }
                ct.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[ChatSync] delta sync failed.");
        }
        finally
        {
            _gate.Release();
        }
    }
}
