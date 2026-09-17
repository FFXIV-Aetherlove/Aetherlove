using System;
using MessagePack;

namespace AetherLove.Shared.Racing;

/// <summary>A racing card the account owns, at one level (1..3). An account owns a card or it does not, so the
/// card id is the whole identity. A pack grants a card the account does not own at level 1 and raises one it owns
/// by a level, stopping at the top level.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRaceCardDto(
    string CardId,
    short Level,
    DateTimeOffset AcquiredAtUtc,
    DateTimeOffset? LevelledAtUtc);

/// <summary>The saved hand as card ids: slot 0 Gold, slots 1 and 2 Silver, "" for an empty slot.
/// <see cref="SilverCardIds"/> is always two entries. <see cref="SavedAtUtc"/> is ignored on the way in.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRaceCardHandDto(
    string GoldCardId,
    string[] SilverCardIds,
    DateTimeOffset SavedAtUtc);

/// <summary>The whole album, replaced wholesale by the client on every reply. <see cref="CatalogueVersion"/> and
/// <see cref="SupportVersion"/> are drift detectors, not gates: an old client uses them to tell it cannot replay a
/// newer race. <see cref="Hand"/> is the saved hand after normalizing against <see cref="Cards"/>.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRaceCardsDto(
    DateTimeOffset ServerNowUtc,
    bool Enabled,
    string CatalogueVersion,
    string SupportVersion,
    LumiRaceCardDto[] Cards,
    LumiRaceCardHandDto Hand);

/// <summary>A card dealt by a pack. <see cref="LevelAfter"/> is the card's level as it was when dealt, so a reveal
/// weeks later still says what the pack did.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRacePackCardDto(
    string CardId,
    short LevelAfter);

/// <summary>A hand resolved to card ids and levels for drawing: the strip before a race and the hand a cup locked.
/// Both arrays are two entries, "" and 0 for an empty slot.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LumiRaceHandViewDto(
    string GoldCardId,
    short GoldLevel,
    string[] SilverCardIds,
    short[] SilverLevels);
