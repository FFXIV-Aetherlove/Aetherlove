namespace AetherLove.Shared.Hangouts;

/// <summary>Shared hangout-field limits enforced on both the client (UI gating) and the server (authoritative).</summary>
public static class HangoutLimits
{
    /// <summary>Visible characters; emoji shortcodes count as one (see <see cref="EmojiText.EffectiveLength"/>).</summary>
    public const int DescriptionMaxLength = 200;

    /// <summary>Hard cap on the raw stored description string; emoji shortcodes make the raw text far
    /// longer than its visible <see cref="DescriptionMaxLength"/> chars.</summary>
    public const int DescriptionRawMaxLength = 800;

    public const int LocationMaxLength = 120;

    public const int MinAttendees = 2;
    public const int MaxAttendees = 200;

    /// <summary>How far ahead a hangout may be scheduled to start (broadcast begins immediately either way).</summary>
    public const int MaxLeadHours = 8;

    public const int MaxDurationHours = 8;
}
