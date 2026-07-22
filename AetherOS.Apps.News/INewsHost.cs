using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.News;

namespace AetherOS.Apps.News;

/// <summary>Host bridge into the plugin: the news hub passthroughs plus the server-side seen state read from
/// the connection snapshot (unread badge, instant-paint list) and written on open.</summary>
public interface INewsHost
{
    Task<NewsSummaryDto[]> GetNewsListAsync(CancellationToken ct = default);
    Task<NewsDto?> GetNewsAsync(Guid newsId, CancellationToken ct = default);

    /// <summary>Staff-only draft fetch behind the test push; marks nothing seen.</summary>
    Task<NewsDto?> GetNewsPreviewAsync(Guid newsId, CancellationToken ct = default);

    /// <summary>Marks an entry seen in both the cached connection snapshot and server-side.</summary>
    void MarkSeen(Guid newsId);

    /// <summary>Marks every unseen entry seen (snapshot + server): opening the app counts as reading the news,
    /// so the tile badge clears without tapping each article.</summary>
    void MarkAllSeen();

    /// <summary>The snapshot's unseen entries, for an instant first paint before the full list fetch lands.</summary>
    IReadOnlyList<NewsSummaryDto> KnownNews { get; }

    /// <summary>Server-side unseen count; drives the tile badge.</summary>
    int UnreadCount { get; }
}
