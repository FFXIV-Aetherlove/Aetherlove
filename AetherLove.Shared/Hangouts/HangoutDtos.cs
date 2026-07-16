using System;
using MessagePack;

namespace AetherLove.Shared.Hangouts;

/// <summary>Activity category of a hangout. SFW social activities only by policy.</summary>
public enum HangoutCategory : short
{
    Pve = 0,
    Pvp = 1,
    Rp = 2,
    Social = 3,
    Fishing = 4,
    Gposing = 5,
    GoldSaucer = 8,
    DeepDungeon = 9,
    Mahjong = 10,
    TripleTriad = 11,
    TreasureHunting = 12,
    Fates = 13,
}

/// <summary>How a hangout ended, as exposed over the wire. Internal server reasons (moderation removal,
/// owner disconnect) are mapped onto these so peers never learn why.</summary>
public enum HangoutEndKind : short
{
    Expired = 0,
    EndedEarly = 1,
    Cancelled = 2,
}

/// <summary>The hangout payload shared by every surface (banner, frame, directory, card, manage view).
/// Caller-agnostic: the caller's own RSVP state travels separately (sync ids / RSVP results).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record HangoutSummaryDto(
    Guid Id,
    Guid OwnerProfileId,
    string OwnerDisplayName,
    HangoutCategory Category,
    string Description,
    string DataCenter,
    string World,
    string Location,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int RsvpCount,
    short? MaxAttendees,
    bool UnlistWhenFull,
    bool IsPublic,
    // Drives the little supporter star on the owner's avatar in hangout cards.
    bool OwnerIsSupporter = false);

/// <summary>Creation payload. The server clamps <see cref="StartUtc"/> to now when it lies in the past and
/// validates lead/duration bounds; duration is minutes from the (clamped) start.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record CreateHangoutRequest(
    HangoutCategory Category,
    string Description,
    string DataCenter,
    string World,
    string Location,
    DateTimeOffset StartUtc,
    int DurationMinutes,
    short? MaxAttendees,
    bool UnlistWhenFull,
    bool IsPublic);

/// <summary>Result of an "on my way" toggle. The attendee limit never rejects an RSVP; past it the count
/// simply renders as "at capacity".</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record HangoutRsvpResultDto(
    bool Going,
    int RsvpCount);

/// <summary>One person who clicked "on my way" on the caller's own hangout (the manage view's list).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record HangoutRsvperDto(
    Guid ProfileId,
    string DisplayName);

/// <summary>On-connect reconciliation snapshot: the caller's own live hangout, the live hangouts of their
/// active matches, and the ids of every hangout the caller has an active RSVP on. Always a full replace of
/// the client's in-memory state, never a merge.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record HangoutSyncDto(
    HangoutSummaryDto? MyHangout,
    HangoutSummaryDto[] MatchHangouts,
    Guid[] MyRsvpHangoutIds,
    // Who's coming to the caller's own hangout; empty when MyHangout is null.
    HangoutRsvperDto[]? MyHangoutRsvpers = null);

/// <summary>Lean payload for a hangout rendered outside its owner's profile: chat share cards and
/// directory rows.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record HangoutCardDto(
    HangoutSummaryDto Summary,
    byte[]? OwnerAvatarWebp);

/// <summary>Directory filters sent with every page fetch. <see cref="CategoryMask"/> is a bitmask of
/// <c>1 &lt;&lt; (short)HangoutCategory</c> values; 0 = every category. <see cref="RegionMask"/> maps
/// each hangout's data center to a region server-side; 0 (and any omitted field) = every region.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record HangoutDirectoryFilterDto(
    int CategoryMask,
    bool MatchesOnly,
    Profile.Enums.Region RegionMask = 0);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record HangoutDirectoryPageDto(
    HangoutCardDto[] Items,
    bool HasMore);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record ReportHangoutRequest(
    Guid HangoutId,
    string Reason);

/// <summary>Push to the owner's active matches (and the owner's own other clients) when a hangout goes up.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record HangoutStartedPushDto(
    HangoutSummaryDto Hangout);

/// <summary>Push to the owner, active RSVPers, and the owner's matches when a hangout reaches a terminal
/// state. Natural expiry is not surfaced as a notification client-side.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record HangoutEndedPushDto(
    Guid HangoutId,
    Guid OwnerProfileId,
    HangoutEndKind Kind);

/// <summary>Push to the hangout owner when someone toggles "on my way".</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record HangoutRsvpChangedPushDto(
    Guid HangoutId,
    Guid RsvperProfileId,
    string RsvperDisplayName,
    bool Going,
    int RsvpCount);
