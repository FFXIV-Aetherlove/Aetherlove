using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using AetherLove.Shared.Messaging;

namespace AetherLove.Services.Chat;

/// <summary>Pulls the chat delta from the server into <see cref="ChatCacheStore"/>. Single-flight:
/// overlapping callers coalesce into one run.</summary>
public sealed class ChatSyncService
{
    private readonly AetherHubContext _hub;
    private readonly ChatCacheStore _cache;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ChatSyncService(AetherHubContext hub, ChatCacheStore cache)
    {
        _hub = hub;
        _cache = cache;
    }

    public ChatCacheStore Cache => _cache;

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
            UiHost.Log.Debug("[PSW] ChatSync.SyncAsync: cache owner {Owner:N}, cursors msg={Mu:o} match={Ku:o}.", _cache.Owner, mu, ku);
            while (true)
            {
                var delta = await _hub.GetChatDeltaAsync(new ChatDeltaRequest(mu, mc, ku), ct).ConfigureAwait(false);
                UiHost.Log.Debug("[PSW] ChatSync.SyncAsync: server delta ForProfileId={For:N} ({Matches} changed matches, hasMore={More}); cache owner {Owner:N}.", delta.ForProfileId, delta.ChangedMatches.Length, delta.HasMore, _cache.Owner);
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
            UiHost.Log.Warning(ex, "[ChatSync] delta sync failed.");
        }
        finally
        {
            _gate.Release();
        }
    }
}
