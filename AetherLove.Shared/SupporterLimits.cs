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

    /// <summary>Hours a sent messenger image survives before the server deletes it. The sender picks the expiry
    /// from <see cref="ImageTtlHourOptions"/>; free accounts cap at <see cref="RegularImageTtlHours"/> (3 days),
    /// supporters at <see cref="SupporterImageTtlHours"/> (7 days).</summary>
    public const int RegularImageTtlHours = 72;
    public const int SupporterImageTtlHours = 168;

    /// <summary>Selectable expiry options in hours: 24h / 48h / 72h for everyone, then 4-7 days for supporters.</summary>
    public static readonly int[] ImageTtlHourOptions = { 24, 48, 72, 96, 120, 144, 168 };

    public static int MaxImageTtlHours(bool isSupporter) =>
        isSupporter ? SupporterImageTtlHours : RegularImageTtlHours;

    /// <summary>An expiry option is a supporter perk once it exceeds the free cap.</summary>
    public static bool ImageTtlRequiresSupporter(int hours) => hours > RegularImageTtlHours;

    /// <summary>Snaps a requested expiry to the largest valid option within the caller's tier cap, so a tampered
    /// client can never buy a longer lifetime than its tier allows.</summary>
    public static int ClampImageTtlHours(int requestedHours, bool isSupporter)
    {
        var cap = MaxImageTtlHours(isSupporter);
        var chosen = ImageTtlHourOptions[0];
        foreach (var opt in ImageTtlHourOptions)
        {
            if (opt <= cap && opt <= requestedHours)
            {
                chosen = opt;
            }
        }
        return chosen;
    }

    /// <summary>Concurrent messenger-image storage per account; freed as images expire or are deleted.</summary>
    public const long RegularImageStorageBytes = 50L * 1024 * 1024;
    public const long SupporterImageStorageBytes = 250L * 1024 * 1024;

    public static long ImageStorageBytes(bool isSupporter) =>
        isSupporter ? SupporterImageStorageBytes : RegularImageStorageBytes;

    /// <summary>Images per yap. Yapper media is a public service and counts against no storage quota.</summary>
    public const int RegularYapImages = 4;
    public const int SupporterYapImages = 8;

    public static int MaxYapImages(bool isSupporter) =>
        isSupporter ? SupporterYapImages : RegularYapImages;
}
