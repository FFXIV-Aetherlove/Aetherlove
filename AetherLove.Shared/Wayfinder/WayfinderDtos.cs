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
    int SupporterDailyCap = 0);

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

/// <summary><see cref="PendingReview"/> is true when auto-moderation flagged the submission, so it waits
/// for a moderator instead of going live.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record WayfinderNewChallengeResultDto(
    Guid ChallengeId,
    bool PendingReview);
