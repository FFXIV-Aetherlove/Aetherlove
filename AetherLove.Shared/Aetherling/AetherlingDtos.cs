using System;
using MessagePack;

namespace AetherLove.Shared.Aetherling;

/// <summary>The rungs of the ladder an Aethercore climbs. APPEND-ONLY, and the value IS the number of
/// charges the core has taken, so a stage can never be renumbered without rewriting live rows.</summary>
public enum AetherlingStage : short
{
    Dormant = 0,
    Stirring = 1,
    Fissured = 2,
    Quickening = 3,

    /// <summary>The last rung a charge can reach. The hatch that follows is free, so it deliberately does
    /// NOT add a rung: the value has to keep meaning "charges taken".</summary>
    Kindling = 4,
}

/// <summary>The six elements a grown Aetherling can lean toward. APPEND-ONLY: stored rows carry the
/// number, so values are never renumbered. Light and dark are deliberately absent until their phase.</summary>
public enum AetherlingElement : short
{
    None = 0,
    Fire = 1,
    Ice = 2,
    Wind = 3,
    Earth = 4,
    Lightning = 5,
    Water = 6,
}

/// <summary>Longest name a player may give. Shorter than the prototype's 24 on purpose.</summary>
public static class AetherlingLimits
{
    public const int NameMaxLength = 14;

    /// <summary>What a freshly hatched one is called until the player says otherwise.</summary>
    public const string DefaultName = "Lumi";

    /// <summary>Most accessories one look may equip at once.</summary>
    public const int MaxEquippedAccessories = 12;
}

/// <summary>One account's Aethercore. Null on the wire means the account never bought one.
/// <para>
/// <see cref="ServerNowUtc"/> rides along on purpose: the gate between stages is wall-clock arithmetic
/// the server owns, and a client that computed the countdown from its own clock would let anyone skip
/// every wait by changing the system time. The client subtracts against this stamp instead.
/// </para></summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingDto(
    short CoreStage,
    DateTimeOffset StageEnteredAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ServerNowUtc,
    int SparksSpent,
    int NextChargeSparks,
    int GateMinutes,
    short MaxStage,
    DateTimeOffset? HatchedAtUtc = null,
    string? PetName = null,
    bool NameChosen = false,
    AetherlingGrowthDto? Growth = null,
    AetherlingAdultDto? Adult = null,
    AetherlingLookDto? Look = null,
    AetherlingScratchCardDto[]? Cards = null,
    DateTimeOffset? OnboardingDoneAtUtc = null);

/// <summary>The growth ladder: 9 fed crystals from hatchling to adult. The client derives the worn
/// form from <see cref="GrowthFed"/> alone (0-2 first form, 3-5 second, 6-8 third, 9 adult) and renders
/// the feed countdown against <c>ServerNowUtc</c>, never its own clock.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingGrowthDto(
    short GrowthFed,
    DateTimeOffset? LastFedAtUtc,
    int FeedGateMinutes,
    short FeedsPerStage);

/// <summary>The grown pet: its rolled element and the lifetime diet ledger the radar and the
/// signature turns read. Counts only ever go up.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingAdultDto(
    DateTimeOffset AdultAtUtc,
    short Element,
    short FeedsToday,
    short FeedsPerDay,
    int DietTurnThreshold,
    AetherlingDietCountDto[] Diet);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingDietCountDto(short Element, int Count);

/// <summary>What the pet is wearing. Sent whole, never patched: a partial write is how two devices
/// dress half a pet each. Item keys are store ItemRefs; the palette is the lowercase slug and
/// "dawn" is the one everyone owns.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingLookDto(
    string Palette,
    string[] Accessories,
    string Reaction,
    bool ArmsFollowJob,
    string[]? DisabledReactions = null);

/// <summary>One scratch card. The prize fields stay at their defaults until the reveal, so an
/// unscratched prize never leaves the server.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingScratchCardDto(
    short Slot,
    DateTimeOffset? RevealedAtUtc,
    short PrizeKind = 0,
    string[]? PrizeRefs = null);
