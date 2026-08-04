namespace AetherLove.Shared.Yapper;

/// <summary>What a yap is. Append-only: values are wire data and are never renumbered. A repost with
/// text is a quote; there is no separate quote kind.</summary>
public enum YapKind : short
{
    Post = 1,
    Reply = 2,
    Repost = 3,
}

/// <summary>Who may see a yap. Append-only.</summary>
public enum YapVisibility : short
{
    Everyone = 1,
    FollowersOnly = 2,
}

/// <summary>Yap lifecycle. Deleted yaps stay as tombstone rows so replies and reposts keep a stable
/// target to render as "unavailable". Append-only.</summary>
public enum YapStatus : short
{
    Live = 1,
    DeletedByAuthor = 2,
    RemovedByModeration = 3,
}

/// <summary>Auto-moderation state of a yap's text. Flagged yaps stay live pending review. Append-only.</summary>
public enum YapFlagStatus : short
{
    Clean = 1,
    PendingReview = 2,
    Reviewed = 3,
}

/// <summary>Shared-into content embedded in a yap. Append-only; the client renders unknown kinds as the
/// unavailable-fallback card so new embed kinds can ship server-side ahead of the client.</summary>
public enum YapEmbedKind : short
{
    None = 0,
    Venue = 1,
    LevemeteAd = 2,
}

/// <summary>The profile page tabs. Append-only.</summary>
public enum YapperProfileTab : short
{
    Posts = 1,
    Replies = 2,
    Media = 3,
    Liked = 4,
}

/// <summary>Yapper inbox notification kinds. Append-only.</summary>
public enum YapperNotificationKind : short
{
    Like = 1,
    Reply = 2,
    Repost = 3,
    Mention = 4,
    Follow = 5,
    NewPost = 6,
}
