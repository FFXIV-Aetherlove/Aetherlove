using System;
using AetherLove.Shared.Profile;
using MessagePack;

namespace AetherLove.Shared.Wayfinder;

/// <summary>The player's current challenge. The picture rides inline on both start and state so a relog
/// resumes cleanly; <see cref="RemainingSeconds"/> is server-computed so client clock skew cannot break the
/// countdown. Target coordinates never appear on any DTO.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record WayfinderAssignmentDto(
    Guid AssignmentId,
    Guid ChallengeId,
    string ChallengeName,
    short Expansion,
    byte[] ImageBytes,
    DateTimeOffset ExpiresAtUtc,
    int RemainingSeconds,
    int AttemptCount,
    short? LastVerdict);

/// <summary><see cref="SupporterDailyCap"/> is the cap a supporter would get, so the client can show the
/// locked slots a free account is missing rather than a shorter row.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record WayfinderStateDto(
    WayfinderAssignmentDto? Active,
    int StartsRemainingToday,
    int DailyCap,
    int ChallengesAvailable,
    int TotalFound,
    int SupporterDailyCap = 0,
    bool InGroupRun = false);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record WayfinderStartResultDto(
    WayfinderAssignmentDto Assignment,
    int StartsRemainingToday);

/// <summary>The client-attested position snapshot taken at the selfie moment: territory plus raw world
/// coordinates, exactly as the game reports them.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record WayfinderSubmitDto(
    Guid AssignmentId,
    int TerritoryId,
    float X,
    float Y,
    float Z);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record WayfinderSubmitResultDto(
    short Verdict,
    int AttemptCount,
    bool AssignmentClosed,
    int? SecondsToFind);

/// <summary>A scout's new waypoint, authored in the phone app. The position is the author's own, captured
/// client-side when the photo was taken, and becomes the hidden answer key.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record WayfinderNewChallengeDto(
    string Name,
    short Expansion,
    int TerritoryId,
    float X,
    float Y,
    float Z,
    string? ZoneName,
    PhotoUploadDto Photo);

/// <summary><see cref="PendingReview"/> is always true: every scout submission waits for a moderator
/// rather than going live on the strength of auto-moderation alone. The field stays on the wire because
/// older clients read it, and because the answer is a server policy that may soften again.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record WayfinderNewChallengeResultDto(
    Guid ChallengeId,
    bool PendingReview);

/// <summary>One person's place in a party hunt. <see cref="Joined"/> is true from the gathering join on;
/// spectators (never joined, or dropped at begin) carry false. <see cref="AssignmentId"/> exists once the
/// hunt begins; each client picks its own out of the roster (ids are not secrets).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record WayfinderRunMemberDto(
    Guid AccountId,
    bool Joined,
    Guid? AssignmentId,
    short? BestVerdict,
    bool Found);

/// <summary>A party hunt, full-replace on every change. While Gathering only the roster and
/// <see cref="HostWorldId"/> matter; from Active on the challenge fields are set.
/// <see cref="ImageBytes"/> rides only on the begin push and the explicit get, never on roster/verdict
/// updates.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record WayfinderPartyRunDto(
    Guid RunId,
    Guid PartyId,
    short Status,
    int HostWorldId,
    Guid? ChallengeId,
    string? ChallengeName,
    short Expansion,
    byte[]? ImageBytes,
    DateTimeOffset? ExpiresAtUtc,
    int RemainingSeconds,
    int FoundCount,
    int ParticipantCount,
    WayfinderRunMemberDto[] Members);

/// <summary>The party-hunt position snapshot: the solo shape plus the client-attested current world, the
/// backstop behind the join-time world gate.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record WayfinderGroupSubmitDto(
    Guid AssignmentId,
    int TerritoryId,
    float X,
    float Y,
    float Z,
    int WorldId);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record WayfinderGroupSubmitResultDto(
    short Verdict,
    int AttemptCount,
    bool Found,
    bool WorldOk,
    int? SecondsToFind,
    int FoundCount,
    int ParticipantCount);
