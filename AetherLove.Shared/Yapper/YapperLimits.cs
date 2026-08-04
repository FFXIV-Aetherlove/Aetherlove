namespace AetherLove.Shared.Yapper;

/// <summary>Shared Yapper limits enforced on both the client (UI gating) and the server (authoritative).</summary>
public static class YapperLimits
{
    /// <summary>Visible characters per yap; emoji shortcodes count as one and links cost
    /// <see cref="LinkCharCost"/> flat (see <c>YapTextParser</c>).</summary>
    public const int TextMaxLength = 300;
    public const int SupporterTextMaxLength = 3000;

    public static int MaxTextLength(bool isSupporter) =>
        isSupporter ? SupporterTextMaxLength : TextMaxLength;

    /// <summary>Every link counts as this many characters regardless of its real length.</summary>
    public const int LinkCharCost = 25;

    /// <summary>Hard cap on the raw stored text; emoji shortcodes make the raw string far longer than
    /// its visible length.</summary>
    public const int TextRawMaxLength = 24000;

    public const int HandleMinLength = 3;
    public const int HandleMaxLength = 20;

    public const int DisplayNameMaxLength = 40;

    /// <summary>Visible characters in the profile bio.</summary>
    public const int BioMaxLength = 300;
    public const int BioRawMaxLength = 2400;

    /// <summary>Parse caps so one yap can never fan out unbounded mention/tag work.</summary>
    public const int MaxMentionsPerYap = 10;
    public const int MaxTagsPerYap = 8;

    /// <summary>Yap images are downscaled to fit within this box and re-encoded to lossy WebP.</summary>
    public const int ImageMaxWidth = 1920;
    public const int ImageMaxHeight = 1080;

    public const long MaxImageUploadBytes = 10L * 1024 * 1024;

    /// <summary>Standard edit window; supporters edit without a window. The server enforces the live
    /// value from config, this const only sizes client copy.</summary>
    public const int EditWindowHours = 12;

    /// <summary>Shared page sizes so the client's load-more and the server's keyset takes never drift.</summary>
    public const int FeedPageSize = 30;
    public const int RepliesPageSize = 30;
    public const int FollowListPageSize = 30;
    public const int NotificationsPageSize = 50;
    public const int SearchPageSize = 20;

    public const int DmPageSize = 40;

    /// <summary>Server-side sanity cap on one DM's ciphertext (the 3,000-char supporter limit encrypts
    /// well under this even in 4-byte UTF-8).</summary>
    public const int DmMaxCiphertextBytes = 16384;
}
