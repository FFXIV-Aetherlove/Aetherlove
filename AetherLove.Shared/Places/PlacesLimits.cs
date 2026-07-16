namespace AetherLove.Shared.Places;

/// <summary>Shared venue-field limits enforced on both the client (UI gating) and the server (authoritative).</summary>
public static class PlacesLimits
{
    public const int VenueNameMinLength = 3;
    public const int VenueNameMaxLength = 60;

    /// <summary>Visible characters; emoji shortcodes count as one (see <see cref="EmojiText.EffectiveLength"/>).</summary>
    public const int VenueDescriptionMaxLength = 4000;

    /// <summary>Hard cap on the raw stored/edited description string; emoji shortcodes make the raw text far
    /// longer than its visible <see cref="VenueDescriptionMaxLength"/> chars.</summary>
    public const int VenueDescriptionRawMaxLength = 16000;

    /// <summary>Raw length of the optional discord.gg invite link.</summary>
    public const int VenueDiscordMaxLength = 200;

    /// <summary>Visible characters per review.</summary>
    public const int ReviewMaxLength = 500;

    public const int MinRating = 1;
    public const int MaxRating = 5;

    public const int MaxOpeningTimesPerVenue = 14;

    public const int MaxWard = 30;
    public const int MaxPlot = 60;
    public const int MaxRoom = 512;
}
