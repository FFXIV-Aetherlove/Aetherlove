using System;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Profile.Enums;
using MessagePack;

namespace AetherLove.Shared.Places;

/// <summary>
/// One opening-time rule, expressed in the venue's own timezone. Recurring rules set
/// <see cref="DaysMask"/> (bit 0 = Monday .. bit 6 = Sunday); a one-time rule sets it to 0 and carries
/// the venue-local date as <see cref="OneTimeDateDayNumber"/> (<c>DateOnly.DayNumber</c> — DateOnly itself
/// is not wire-safe under the contractless resolver). A span may cross midnight:
/// <c>StartMinute + DurationMinutes</c> past 1440 runs into the next calendar day.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record VenueOpeningTimeDto(
    Guid Id,
    int DaysMask,
    int OneTimeDateDayNumber,
    short StartMinute,
    short DurationMinutes);

/// <summary>Venue card data shared by the browse list and the detail view.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record VenueSummaryDto(
    Guid Id,
    string Name,
    VenueTag Tags,
    Region Region,
    string DataCenter,
    string World,
    HousingDistrict District,
    short Ward,
    short Plot,
    short Room,
    byte[]? LogoWebp,
    int LikeCount,
    bool LikedByMe,
    double AverageRating,
    int ReviewCount);

/// <summary>One concrete dated occurrence expanded from a venue's opening times. <see cref="RsvpAvatars"/>
/// is a small capped clump of attendee avatars; null where the payload would be too heavy (upcoming list).
/// <see cref="BannerWebp"/> rides only on the browse feed's live occurrences, for the hero card.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record VenueOccurrenceDto(
    Guid VenueId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int RsvpCount,
    bool RsvpedByMe,
    byte[][]? RsvpAvatars,
    byte[]? BannerWebp = null);

/// <summary>Client-side Places preferences sent with every browse call. Empty masks = no filtering.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record PlacesFilterDto(
    VenueTag Tags,
    Region RegionMask,
    bool IncludeNsfw);

/// <summary>The Places landing payload: venue cards plus their occurrences over the coming week.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record PlacesBrowseDto(
    VenueSummaryDto[] Venues,
    VenueOccurrenceDto[] HappeningNow,
    VenueOccurrenceDto[] Upcoming);

/// <summary>One published venue review. Reviews are avatar-only by design — no author name crosses the
/// wire. <see cref="PendingModeration"/> is true only on the caller's own flagged review.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record VenueReviewDto(
    Guid Id,
    short Rating,
    string Text,
    DateTimeOffset CreatedAtUtc,
    byte[]? AuthorAvatarWebp,
    bool Mine,
    bool PendingModeration);

/// <summary>One banner slot on a venue (supporter owners can have several; shown as a carousel).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record VenueBannerDto(
    short Slot,
    byte[] Webp);

/// <summary>Lean venue payload for a share card in chat: the summary plus the primary banner, enough to
/// render the browse-style card without the full detail fetch.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record VenueCardDto(
    VenueSummaryDto Summary,
    byte[]? BannerWebp);

/// <summary>Full venue payload for the detail view. <see cref="BannerWebp"/> stays the first banner;
/// <see cref="Banners"/> carries the full supporter carousel when there is more than one.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record VenueDetailDto(
    VenueSummaryDto Summary,
    string Description,
    byte[]? BannerWebp,
    VenueOpeningTimeDto[] OpeningTimes,
    VenueOccurrenceDto[] Occurrences,
    VenueReviewDto[] Reviews,
    VenueReviewDto? MyReview,
    VenueBannerDto[]? Banners = null);

/// <summary>Owner-side venue snapshot for the My Venues editor.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MyVenueDto(
    Guid Id,
    string Name,
    string Description,
    VenueTag Tags,
    Region Region,
    string DataCenter,
    string World,
    HousingDistrict District,
    short Ward,
    short Plot,
    short Room,
    string Timezone,
    VenueStatus Status,
    VenueOpeningTimeDto[] OpeningTimes,
    byte[]? BannerWebp,
    byte[]? LogoWebp,
    int LikeCount,
    double AverageRating,
    int ReviewCount,
    VenueBannerDto[]? Banners = null);

/// <summary>Create (null <see cref="Id"/>) or update a venue definition. Images travel separately via
/// <c>SetVenueImageAsync</c> (a <see cref="PhotoUploadDto"/> per slot).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record VenueEditDto(
    Guid? Id,
    string Name,
    string Description,
    VenueTag Tags,
    Region Region,
    string DataCenter,
    string World,
    HousingDistrict District,
    short Ward,
    short Plot,
    short Room,
    string Timezone,
    VenueOpeningTimeDto[] OpeningTimes);
