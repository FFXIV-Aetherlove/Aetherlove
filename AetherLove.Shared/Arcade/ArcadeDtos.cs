using System;
using MessagePack;

namespace AetherLove.Shared.Arcade;

/// <summary>A finished arcade run. <see cref="Metric1"/>/<see cref="Metric2"/> carry the game's primary
/// progress numbers so the server can sanity-check the score: Snake pellets, Stacker lines cleared,
/// Breaker level reached (+ Metric2 = 1 when the run was won), Meteor/Invaders wave reached,
/// Muncher level reached, Plappy pillars cleared (+ Metric2 = difficulty tier reached).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ArcadeScoreSubmissionDto(
    ArcadeGame Game,
    int Score,
    int DurationMs,
    int Metric1 = 0,
    int Metric2 = 0);

/// <summary>Outcome of a score submission. A rejected score still counts as a play but never enters
/// the leaderboards.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ArcadeScoreResultDto(
    bool Accepted,
    bool NewAllTimeBest,
    bool NewWeeklyBest,
    int AllTimeBest,
    int WeeklyBest);

/// <summary>One leaderboard row: the OS account display name only, never character identity.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ArcadeLeaderboardEntryDto(
    int Rank,
    string DisplayName,
    int Score,
    DateTimeOffset AchievedAtUtc,
    bool IsMe = false);

/// <summary>Top-100 board plus the caller's own placement (also set when outside the top 100).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ArcadeLeaderboardDto(
    ArcadeLeaderboardEntryDto[] Entries,
    int? MyRank,
    int? MyScore,
    DateTimeOffset? MyScoreAtUtc);
