using System;
using MessagePack;

namespace AetherLove.Shared.Sparks;

/// <summary>One ledger line for the client's diagnostics view.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SparkLedgerLineDto(
    DateTimeOffset AtUtc,
    SparkAction Action,
    SparkTransactionKind Kind,
    int Amount,
    long BalanceAfter);

/// <summary>The caller's wallet snapshot: balance, when the spark week resets, and the newest ledger lines.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SparkStatusDto(
    long Balance,
    long LifetimeEarned,
    DateTimeOffset WeekResetsAtUtc,
    SparkLedgerLineDto[] Lines);
