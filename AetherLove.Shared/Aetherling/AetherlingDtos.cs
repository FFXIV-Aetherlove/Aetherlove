using System;
using MessagePack;

namespace AetherLove.Shared.Aetherling;

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

    /// <summary>What a newborn is called until the player names it.</summary>
    public const string DefaultName = "Lumi";

    /// <summary>Most accessories one look may equip at once. Raised from 12 to 25 (owner, 2026-08-28):
    /// ears, tails, nooks, banners and both arms all count against it, so a well-dressed creature was
    /// reaching the old cap in ordinary play. The stored column holds far more than this.</summary>
    public const int MaxEquippedAccessories = 25;

    /// <summary>The most meals a day the client ever shows: the slots, the tooltip and the tour clamp the
    /// server's FeedsPerDay to this, so a test fixture with an unlimited appetite still draws five slots.</summary>
    public const int ShownFeedsPerDay = 5;

    /// <summary>The store ref of the consumable that buys a rename. Both sides name it: the server spends
    /// it, the client checks for it before offering the pill.</summary>
    public const string NameChangeRef = "name-change";
}

/// <summary>One account's Aethercore. Null on the wire means the account never bought one. A core
/// with a null <see cref="Adult"/> is a crystal waiting to be broken, whatever else it carries: the
/// hatch and the adulting are one moment since 2.7, and a pet hatched under the old ladder but never
/// grown up goes back into its crystal until its owner breaks it.
/// <para>
/// <see cref="ServerNowUtc"/> rides along on purpose: the daily appetite and the wheel are wall-clock
/// arithmetic the server owns, and a client counting down from its own clock would let anyone skip a
/// wait by changing the system time. The client subtracts against this stamp instead.
/// </para></summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingDto(
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ServerNowUtc,
    int SparksSpent,
    DateTimeOffset? HatchedAtUtc = null,
    string? PetName = null,
    bool NameChosen = false,
    AetherlingAdultDto? Adult = null,
    AetherlingLookDto? Look = null,
    AetherlingScratchCardDto[]? Cards = null,
    DateTimeOffset? OnboardingDoneAtUtc = null,
    AetherlingEmotesDto? Emotes = null,
    bool SharesWithParty = true,
    AetherlingWheelStateDto? Wheel = null,
    DateTimeOffset? PromotedAtUtc = null,
    DateTimeOffset? LastFedAtUtc = null);

/// <summary>The grown pet: the element it was born with, the element it is attuned to now, and the
/// lifetime diet ledger the radar and the signature turns read. Counts only ever go up.
///
/// <para><see cref="Element"/> is the one rolled when the crystal broke and never changes.
/// <see cref="AttunedElement"/> is what the creature currently answers to: the worn form's element,
/// or the born one while it wears none. Everything that asks "which element is this pet" (races, the
/// games' powers) means the attuned one; only "what did it hatch as" means the born one. It is zero
/// on a server that predates it, so a reader falls back to <see cref="Element"/>.</para></summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingAdultDto(
    DateTimeOffset AdultAtUtc,
    short Element,
    short FeedsToday,
    short FeedsPerDay,
    int DietTurnThreshold,
    AetherlingDietCountDto[] Diet,
    int ShellFeedThreshold = 0,
    int ShellFeedThreshold2 = 0,
    short AttunedElement = 0);

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
    string[]? DisabledReactions = null,
    string Shell = "");

/// <summary>One scratch card. The prize fields stay at their defaults until the reveal, so an
/// unscratched prize never leaves the server.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingScratchCardDto(
    short Slot,
    DateTimeOffset? RevealedAtUtc,
    short PrizeKind = 0,
    string[]? PrizeRefs = null);

/// <summary>The emote ledger: which of its person's emotes the creature has picked up by watching, and
/// how far along the ones it is still puzzling over are. Thresholds and the learn gate ride along because
/// the server owns every number; the client renders meters against them and decides nothing.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingEmotesDto(
    AetherlingEmoteProgressDto[] Emotes,
    DateTimeOffset? LastLearnedAtUtc,
    int SightingsToLearn,
    int LearnGateHours);

/// <summary>One watchable emote: its stable key, how often the creature has seen it, and when it was
/// learned. Sightings keep counting past the threshold while the daily learn gate holds the unlock.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AetherlingEmoteProgressDto(
    string Key,
    int Sightings,
    DateTimeOffset? LearnedAtUtc);
