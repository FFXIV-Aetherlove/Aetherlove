using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Shared.News;
using AetherOS.Apps.News;

namespace AetherLove.Os;

/// <summary>Daily Eorzean's bridge: the news hub passthroughs, plus the server-side seen state read from the
/// connection snapshot (unread badge and instant-paint list) and written on open.</summary>
public sealed class NewsHostService : INewsHost
{
    private readonly AetherHubContext _hub;
    private readonly SessionBootstrapper _bootstrap;

    public NewsHostService(AetherHubContext hub, SessionBootstrapper bootstrap)
    {
        _hub = hub;
        _bootstrap = bootstrap;
    }

    public Task<NewsSummaryDto[]> GetNewsListAsync(CancellationToken ct = default) =>
        _hub.GetNewsListAsync(ct);

    public Task<NewsDto?> GetNewsAsync(Guid newsId, CancellationToken ct = default) =>
        _hub.GetNewsAsync(newsId, ct);

    public Task<NewsDto?> GetNewsPreviewAsync(Guid newsId, CancellationToken ct = default) =>
        _hub.GetNewsPreviewAsync(newsId, ct);

    public void MarkSeen(Guid newsId)
    {
        _bootstrap.MarkNewsSeenInSnapshot(new[] { newsId });
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.MarkNewsSeenAsync(new[] { newsId }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[NewsHostService] MarkNewsSeenAsync failed.");
            }
        });
    }

    public void MarkAllSeen()
    {
        var ids = _bootstrap.LastConnection?.UnseenNews.Select(n => n.Id).ToArray() ?? [];
        if (ids.Length == 0)
        {
            return;
        }
        _bootstrap.MarkNewsSeenInSnapshot(ids);
        _ = Task.Run(async () =>
        {
            try
            {
                await _hub.MarkNewsSeenAsync(ids, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[NewsHostService] MarkNewsSeenAsync (all) failed.");
            }
        });
    }

    public IReadOnlyList<NewsSummaryDto> KnownNews => _bootstrap.LastConnection?.UnseenNews ?? [];

    public int UnreadCount => _bootstrap.LastConnection?.UnseenNews.Length ?? 0;
}
