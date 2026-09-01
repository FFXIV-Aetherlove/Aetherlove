using System;
using MessagePack;

namespace AetherLove.Shared.Racing;

/// <summary>Shared limits both sides agree on.</summary>
public static class LumiRaceLimits
{
    /// <summary>Runners in every race. Party members fill real slots first; the rest are ghosts.</summary>
    public const int FieldSize = 6;

    /// <summary>Shards on a crystal card. A full card deals the prize pack.</summary>
    public const int StampsPerCard = 5;
}

/// <summary>One runner in a resolved race. Stats are the block the race was resolved under, stored and
/// sent "as it was" so a later stat system never rewrites history. The client must never display them.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRaceFieldEntryDto(
    short Slot,
    bool IsGhost,
    bool IsPartyMember,
    string Name,
    short Element,
    string Palette,
    string[] Accessories,
    short Stage,
    short Speed,
    short Power,
    short Stamina,
    short Focus,
    short Heart,
    string Shell = "");

/// <summary>A resolved race, whole. The client re-derives the running of it from the inputs (the sim is
/// deterministic), while <see cref="Placements"/> is the server's authoritative record; on any
/// disagreement the server's order wins. <see cref="StartAtUtc"/> is the shared gun: every client counts
/// down against <see cref="ServerNowUtc"/> and starts playback at the same instant.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRaceDto(
    Guid RaceId,
    string CourseKey,
    string WeatherKey,
    int Seed,
    string DialsVersion,
    LumiRaceFieldEntryDto[] Field,
    short PlayerSlot,
    DateTimeOffset StartAtUtc,
    DateTimeOffset ServerNowUtc,
    short[] Placements,
    short WinnerSlot,
    Guid? PartyRunId = null);

/// <summary>What resolving a race earned the caller. Amounts stay server-owned; this only reports what
/// was credited so the prize scene can say it.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRaceRewardDto(
    short Place,
    int SparksAwarded,
    bool StampAwarded,
    short StampsOnCard,
    bool CardCompleted,
    LumiRacePackDto? Pack = null);

/// <summary>A dealt prize pack: two items, already granted, waiting to be ripped open. The rip is
/// bookkeeping, never the grant, so an unopened pack can never strand a prize.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRacePackDto(
    Guid PackId,
    short PrizeKind1,
    string PrizeRef1,
    short PrizeKind2,
    string PrizeRef2,
    DateTimeOffset? RevealedAtUtc);

/// <summary>How one dealt prize presents itself. Resolved from the catalog WITHOUT the sellable gate,
/// because the racing shelves are shut on purpose and a prize already granted is not a purchase. Copy
/// ships in all six columns the way store copy does; the client picks its own.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRacePrizeDto(
    short Kind,
    string Ref,
    Guid ProductId,
    string NameEnglish,
    string? NameSpanish,
    string? NameFrench,
    string? NameRussian,
    string? NameGerman,
    string? NamePortuguese,
    uint AccentColor,
    bool HasImage);

/// <summary>What starting a race returns: the race to play back and what it earned.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRaceStartResultDto(
    LumiRaceDto Race,
    LumiRaceRewardDto Reward);

/// <summary>The racer app's home state.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRaceStateDto(
    DateTimeOffset ServerNowUtc,
    bool Enabled,
    short RacesToday,
    short RacesPerDay,
    DateTimeOffset? NextRaceAtUtc,
    short Stamps,
    int RacesRun,
    int Wins,
    int Seconds,
    int Thirds,
    int GhostAppearances,
    int CardsCompleted,
    LumiRacePackDto[] PendingPacks,
    LumiRaceDto? ActiveRace = null,
    LumiRacePartyRunDto? PartyRun = null,
    /// <summary>Whether the caller's Aetherling has grown up. Racing is adults only, and the home
    /// screen says which of the two things is missing rather than greying a button silently.</summary>
    bool PetAdult = false,
    bool PetHatched = false,
    LumiRaceOfferDto[]? Offers = null,
    int PartyRaces = 0,
    int PartyWins = 0,
    int PartySeconds = 0,
    int PartyThirds = 0,
    LumiRaceElementCountDto[]? ElementCounts = null,
    /// <summary>The tournament ledger: stamps earned against each cap. Past either cap races run in
    /// practice, which pays no stamp and no sparks; the pages say so instead of hiding the race button.</summary>
    short StampsToday = 0,
    short StampsPerDay = 0,
    short StampsThisWeek = 0,
    short StampsPerWeek = 0,
    /// <summary>Minutes between races. Carried so the pages that state the rule read the server's own
    /// number rather than a copy of it that can drift.</summary>
    short GateMinutes = 0,
    /// <summary>When the current sparks week rolls, for the practice popup's countdown.</summary>
    DateTimeOffset? WeekResetAtUtc = null,
    int PracticeRaces = 0,
    int PracticeWins = 0,
    int PracticeSeconds = 0,
    int PracticeThirds = 0,
    /// <summary>The caller's own Lumi, enough of it to name it and to draw it: the onboarding speaks to
    /// the creature by name and stands it among the others rather than describing a stranger.</summary>
    string PetName = "",
    short PetElement = 0,
    string PetPalette = "",
    string PetAccessories = "",
    short PetStage = 3,
    string PetShell = "");

/// <summary>One course on offer, already graded for the caller's racer. <see cref="WeatherKey"/> is the
/// sky dealt with the offer and the one the race will run, so a card can name it up front.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRaceOfferDto(
    short Difficulty,
    string CourseKey,
    short Element,
    short Category,
    string WeatherKey = AetherRaceLive.ClearWeather);

/// <summary>How many races the caller has run at one difficulty on one element's ground.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRaceElementCountDto(
    short Element,
    short Difficulty,
    int Count,
    bool IsPractice = false);

/// <summary>One finished race as the history list shows it.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRaceLogEntryDto(
    string CourseKey,
    string WeatherKey,
    short Element,
    short Difficulty,
    short Place,
    bool IsParty,
    DateTimeOffset ResolvedAtUtc,
    bool IsPractice = false);

/// <summary>A party race gathering or running, the Wayfinder party-run shape. The race itself rides on
/// <see cref="Race"/> once the host begins; every member replays the same bits.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRacePartyRunDto(
    Guid RunId,
    Guid PartyId,
    Guid HostAccountId,
    short Status,
    LumiRacePartyMemberDto[] Members,
    DateTimeOffset ServerNowUtc,
    LumiRaceDto? Race = null,
    /// <summary>The VIEWER's own outcome, filled only on a per-viewer read once the race exists; the
    /// pushed copy has no viewer and carries null. The stage was inventing this before, twice, in two
    /// different ways.</summary>
    LumiRaceRewardDto? Reward = null);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRacePartyMemberDto(
    Guid AccountId,
    string Name,
    bool Joined);

/// <summary>Party run lifecycle. Append-only, stored as short.</summary>
public enum LumiRacePartyRunStatus : short
{
    Gathering = 0,
    Active = 1,
    Resolved = 2,
    Cancelled = 3,
    Expired = 4,
}
