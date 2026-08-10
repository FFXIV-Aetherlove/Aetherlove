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

/// <summary>Longest name a player may give. Shorter than the prototype's 24 on purpose.</summary>
public static class AetherlingLimits
{
    public const int NameMaxLength = 14;

    /// <summary>What a freshly hatched one is called until the player says otherwise.</summary>
    public const string DefaultName = "Lumi";
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
    bool NameChosen = false);
