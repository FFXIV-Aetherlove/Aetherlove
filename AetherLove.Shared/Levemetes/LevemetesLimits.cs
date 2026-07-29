namespace AetherLove.Shared.Levemetes;

/// <summary>Shared classified-ad limits enforced on both the client (UI gating) and the server (authoritative).</summary>
public static class LevemetesLimits
{
    public const int TitleMinLength = 3;
    public const int TitleMaxLength = 60;

    /// <summary>Visible characters; emoji shortcodes count as one (see <see cref="EmojiText.EffectiveLength"/>).</summary>
    public const int DescriptionMaxLength = 2000;

    /// <summary>Hard cap on the raw stored/edited description string; emoji shortcodes make the raw text far
    /// longer than its visible <see cref="DescriptionMaxLength"/> chars.</summary>
    public const int DescriptionRawMaxLength = 16000;

    /// <summary>Raw length of the optional free-text compensation line.</summary>
    public const int PriceMaxLength = 60;

    /// <summary>Raw length of the optional discord.gg invite link.</summary>
    public const int DiscordMaxLength = 200;

    /// <summary>Visible characters per review.</summary>
    public const int ReviewMaxLength = 500;

    public const int MinRating = 1;
    public const int MaxRating = 5;

    public const int MinPhotos = 1;
    public const int MaxPhotos = 3;

    /// <summary>Shared page size so the client's load-more and the server's skip/take can never drift.</summary>
    public const int ReviewsPageSize = 20;
}
