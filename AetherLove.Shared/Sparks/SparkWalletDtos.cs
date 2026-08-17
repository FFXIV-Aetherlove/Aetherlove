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
    int? MaxPointsPerWeek,

    /// <summary>Times this account has been paid for the action since UTC midnight, which is the same
    /// boundary the daily cap is counted against. Compared with <paramref name="MaxPerDay"/> it is what
    /// lets the page say a thing is done rather than only what it is worth.</summary>
    int UsedToday = 0,

    /// <summary>Sparks this action has paid this spark week, for the entries capped by points rather than
    /// by a count. Trailing with a default so an older server's catalog still deserializes.</summary>
    int EarnedThisWeek = 0)
{
    /// <summary>Nothing more is coming from this action right now: either the day's count is used up or the
    /// week's points are. Both together, because a page that ticks one and not the other is telling half a
    /// truth about the same question, which is "can I still earn this".</summary>
    public bool Exhausted =>
        (MaxPerDay is { } perDay && UsedToday >= perDay)
        || (MaxPointsPerWeek is { } perWeek && EarnedThisWeek >= perWeek);

    /// <summary>The week's points ran out, so this one does not come back at the next daily reset.</summary>
    public bool WeekSpent => MaxPointsPerWeek is { } perWeek && EarnedThisWeek >= perWeek;
}

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
