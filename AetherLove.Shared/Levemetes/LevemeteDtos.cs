using System;
using MessagePack;

namespace AetherLove.Shared.Levemetes;

/// <summary>Client browse preferences sent with every browse call. An empty <see cref="Categories"/> array
/// means no restriction, which is what lets an old client keep seeing ads in categories it does not know.
/// <see cref="Kind"/> 0 means both kinds.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LevemetesFilterDto(
    short[] Categories,
    int RegionMask,
    short Kind,
    bool IncludeNsfw);

/// <summary>Ad card data for the browse feed. The cover is slot 1's bytes, which the pipeline guarantees is
/// SFW, so the feed never needs blurring.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LevemeteSummaryDto(
    Guid Id,
    short Kind,
    short Category,
    string Title,
    int RegionMask,
    byte[]? CoverWebp,
    double AverageRating,
    int ReviewCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset BumpedAtUtc,
    string? Price);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record LevemetesBrowseDto(LevemeteSummaryDto[] Ads);

/// <summary>One carousel photo. NSFW slots ship with the flag so the client blurs until revealed.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LevemetePhotoDto(
    short Order,
    bool IsNsfw,
    byte[] WebpBytes);

/// <summary>One published review of the poster. Avatar-only by design: no author name and no account id
/// cross the wire. <see cref="PendingModeration"/> is true only on the caller's own flagged review.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LevemeteReviewDto(
    Guid Id,
    short Rating,
    string Text,
    DateTimeOffset CreatedAtUtc,
    byte[]? AuthorAvatarWebp,
    bool Mine,
    bool PendingModeration);

/// <summary>The full ad detail. Availability masks are poster-LOCAL; the viewer shifts them with the
/// denormalized <see cref="TimezoneOffsetMinutes"/> (the timezone id string itself never ships, it reads as
/// a quasi-location). <see cref="PosterAcceptsContact"/> mirrors the poster's messenger adds toggle; the
/// friend code itself never rides an ad, contact goes through the server.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LevemeteDetailDto(
    Guid Id,
    short Kind,
    short Category,
    string Title,
    string Description,
    int RegionMask,
    int WeekdayHoursMask,
    int WeekendHoursMask,
    int TimezoneOffsetMinutes,
    LevemetePhotoDto[] Photos,
    byte[]? PosterAvatarWebp,
    bool PosterAcceptsContact,
    bool ReviewsEnabled,
    double AverageRating,
    int ReviewCount,
    LevemeteReviewDto[] Reviews,
    LevemeteReviewDto? MyReview,
    bool IsMine,
    DateTimeOffset? ExpiresAtUtc,
    string? Price,
    string? Discord,
    string? PosterName = null);

/// <summary>One of the caller's own ads, with the moderation state the public detail hides.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MyLevemeteDto(
    Guid Id,
    short Kind,
    short Category,
    string Title,
    string Description,
    int RegionMask,
    int WeekdayHoursMask,
    int WeekendHoursMask,
    string? Timezone,
    bool ReviewsEnabled,
    short Status,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset BumpedAtUtc,
    string? TextFlagReason,
    MyLevemetePhotoDto[] Photos,
    string? Price,
    string? Discord);

/// <summary>Per-slot moderation mirror for the owner's editor.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MyLevemetePhotoDto(
    short Order,
    bool IsNsfw,
    bool Approved,
    bool InReview,
    byte[] WebpBytes);

/// <summary>The editable ad fields, client to server.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LevemeteEditDto(
    Guid? Id,
    short Kind,
    short Category,
    string Title,
    string Description,
    int RegionMask,
    int WeekdayHoursMask,
    int WeekendHoursMask,
    string? Timezone,
    bool ReviewsEnabled,
    string? Price,
    string? Discord);

/// <summary>Lean payload for the chat share card. A null fetch result is the tombstone for a delisted,
/// expired or removed ad.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LevemeteCardDto(
    string Title,
    short Category,
    short Kind,
    byte[]? CoverWebp);
