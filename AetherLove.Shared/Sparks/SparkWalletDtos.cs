using System;
using MessagePack;

namespace AetherLove.Shared.Sparks;

/// <summary>One earning action from the server catalog, with its exact amount and frequency limits.
/// The client localizes the action label; the server owns every number.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SparkCatalogEntryDto(
    SparkAction Action,
    int Amount,
    SparkPool Pool,
    int? MaxPerDay,
    int? MaxPointsPerWeek);

/// <summary>The Wallet app's wallet snapshot: balance, lifetimes, this week's per-pool counters, the
/// effective caps, and the earning catalog. Weekly counters are already normalized to the current spark
/// week server-side, so a stale wallet never leaks last week's numbers.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SparkWalletDto(
    long Balance,
    long LifetimeEarned,
    long LifetimeSpent,
    DateTimeOffset WeekResetsAtUtc,
    int RoutineEarnedThisWeek,
    int BonusEarnedThisWeek,
    int ExemptEarnedThisWeek,
    int RoutineWeeklyCap,
    int TotalWeeklyCap,
    int BonusWeeklyCap,
    SparkCatalogEntryDto[] Catalog);

/// <summary>One ledger line for the Wallet app's timeline. Context carries the admin reason on
/// adjustment and clawback lines only; it is null on every earn row.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SparkLedgerEntryDto(
    long WalletSequence,
    DateTimeOffset AtUtc,
    SparkAction Action,
    SparkTransactionKind Kind,
    int Amount,
    long BalanceAfter,
    string? Context);

/// <summary>A keyset page of ledger lines, newest first. NextBeforeSequence is null on the last page.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SparkLedgerPageDto(
    SparkLedgerEntryDto[] Lines,
    long? NextBeforeSequence);
