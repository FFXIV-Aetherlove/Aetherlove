using System;
using System.Linq;
using MessagePack;

namespace AetherLove.Shared.Racing;

/// <summary>The Starlight Cup's scoring, shared so the standings the phone animates are the standings the
/// server pays on. Amounts of sparks are NOT here: the server prices the prize from config.</summary>
public static class LumiCupRules
{
    public const int RaceCount = 4;
    public const string Finale = "grandstand-dash";

    public static int Points(int place) => place switch
    {
        0 => 12,
        1 => 10,
        2 => 9,
        3 => 8,
        4 => 7,
        5 => 6,
        _ => 0,
    };

    public static int Packs(int place) => place is >= 0 and < 3 ? 3 - place : 0;

    public static short[] Standings(LumiRaceDto[] races)
    {
        return Enumerable.Range(0, LumiRaceLimits.FieldSize)
            .OrderByDescending(slot => races.Sum(r => Points(Array.IndexOf(r.Placements, (short)slot))))
            .ThenByDescending(slot => races.Count(r => r.Placements[0] == slot))
            .ThenByDescending(slot => races.Count(r => r.Placements[1] == slot))
            .ThenByDescending(slot => races.Count(r => r.Placements[2] == slot))
            .ThenBy(slot => races.Length == 0 ? slot : Array.IndexOf(races[^1].Placements, (short)slot))
            .Select(slot => (short)slot).ToArray();
    }
}

/// <summary>The cup page's state. <paramref name="CompletionSparks"/> is what finishing pays this week,
/// priced by the server, so the entry copy never promises a number the ledger will not book.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiCupStateDto(
    DateTimeOffset ServerNowUtc,
    DateTimeOffset WeekResetAtUtc,
    bool CanEnter,
    int CompletionSparks,
    LumiCupDto? Cup);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiCupDto(
    Guid Id,
    DateTimeOffset WeekStartUtc,
    string[] Courses,
    LumiRaceFieldEntryDto[] Field,
    LumiRaceDto[] FinishedRaces,
    LumiRaceDto? ActiveRace,
    int SparksAwarded,
    int PacksAwarded,
    /// <summary>The hand locked at entry, applied to every leg. Null on a cup entered before cards.</summary>
    LumiRaceHandViewDto? Hand = null);
