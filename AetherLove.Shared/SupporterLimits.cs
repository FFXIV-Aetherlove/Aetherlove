namespace AetherLove.Shared;

/// <summary>Supporter perk limits shared by client UI gating and the authoritative server checks. The server
/// always re-validates against these; the client only uses them to size its editors.</summary>
public static class SupporterLimits
{
    /// <summary>Extra portrait slots (Order 2..) on the main profile for a regular user.</summary>
    public const int RegularExtraPhotos = 3;

    /// <summary>Extra portrait slots (Order 2..) on the main profile for a supporter.</summary>
    public const int SupporterExtraPhotos = 5;

    public static int MaxExtraPhotos(bool isSupporter) =>
        isSupporter ? SupporterExtraPhotos : RegularExtraPhotos;

    /// <summary>Images per RP character: the primary plus, for supporters, this many extras.</summary>
    public const int ExtraCharacterImages = 2;

    /// <summary>Total images allowed on one RP character (primary included).</summary>
    public static int MaxCharacterImages(bool isSupporter) =>
        1 + (isSupporter ? ExtraCharacterImages : 0);

    /// <summary>Venue banner slots: one for everyone, five for supporter owners (shown as a carousel).</summary>
    public const int RegularVenueBanners = 1;
    public const int SupporterVenueBanners = 5;

    public static int MaxVenueBanners(bool isSupporter) =>
        isSupporter ? SupporterVenueBanners : RegularVenueBanners;
}
