using MessagePack;

namespace AetherLove.Shared.News;

/// <summary>Server→client push when an admin publishes (or re-notifies) a news item.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record NewsPublishedPushDto(NewsSummaryDto Summary);

/// <summary>Server→client push of an admin "test push to staff": a live preview of a (possibly unpublished)
/// news item, sent only to connected moderators/admins. Doesn't change the item's status or seen-state.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record NewsTestPushDto(NewsSummaryDto Summary);
