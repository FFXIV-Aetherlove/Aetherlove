using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherLove.Shared.Racing.Cards;

/// <summary>A card the account owns, at one level. The hand names the card by its catalogue id.</summary>
public sealed record RaceCardOwned(string CardId, int Level);

/// <summary>A hand slot as the resolver reads it: the card id and the level snapshotted at race entry.</summary>
public readonly record struct RaceCardSlot(string CardId, int Level)
{
    public static readonly RaceCardSlot Empty = new(string.Empty, 0);

    public bool IsEmpty => string.IsNullOrEmpty(CardId);

    public RaceCard? Card => RaceCardCatalogue.Find(CardId);
}

/// <summary>The saved hand as card ids: slot 0 is Gold, slots 1 and 2 are Silver. "" is an empty slot.</summary>
public sealed record RaceCardHandIds(string Gold, string Silver1, string Silver2)
{
    public static readonly RaceCardHandIds Empty = new(string.Empty, string.Empty, string.Empty);

    public bool IsEmpty => Gold.Length == 0 && Silver1.Length == 0 && Silver2.Length == 0;

    public string[] Silvers => [Silver1, Silver2];

    public LumiRaceCardHandDto ToDto(DateTimeOffset savedAtUtc) => new(Gold, Silvers, savedAtUtc);
}

/// <summary>A resolved hand: card ids and levels per slot. This is what the server writes onto the runner rows at
/// entry and what both sides build the resolver from; neither reads the account's live collection for a race.</summary>
public sealed record RaceCardHand(RaceCardSlot Gold, RaceCardSlot Silver1, RaceCardSlot Silver2)
{
    public static readonly RaceCardHand Empty = new(RaceCardSlot.Empty, RaceCardSlot.Empty, RaceCardSlot.Empty);

    public bool IsEmpty => Gold.IsEmpty && Silver1.IsEmpty && Silver2.IsEmpty;

    public RaceCardSlot[] Silvers => [Silver1, Silver2];

    public int SilverCount => (Silver1.IsEmpty ? 0 : 1) + (Silver2.IsEmpty ? 0 : 1);

    /// <summary>The non-empty slots, Gold first.</summary>
    public IEnumerable<RaceCardSlot> Cards
    {
        get
        {
            if (!Gold.IsEmpty)
            {
                yield return Gold;
            }

            if (!Silver1.IsEmpty)
            {
                yield return Silver1;
            }

            if (!Silver2.IsEmpty)
            {
                yield return Silver2;
            }
        }
    }

    /// <summary>Builds a hand from stored ids and levels. A missing Silver entry is an empty slot; a non-empty card
    /// with no level, or a level out of range, is clamped into 1..3.</summary>
    public static RaceCardHand From(string? goldId, int goldLevel, IReadOnlyList<string>? silverIds, IReadOnlyList<int>? silverLevels)
    {
        return new RaceCardHand(
            Slot(goldId, goldLevel),
            Slot(At(silverIds, 0), At(silverLevels, 0)),
            Slot(At(silverIds, 1), At(silverLevels, 1)));
    }

    public static RaceCardHand FromView(LumiRaceHandViewDto? view)
    {
        if (view is null)
        {
            return Empty;
        }

        return From(view.GoldCardId, view.GoldLevel, view.SilverCardIds, view.SilverLevels?.Select(l => (int)l).ToArray());
    }

    public LumiRaceHandViewDto ToView()
    {
        return new LumiRaceHandViewDto(
            Gold.CardId,
            (short)Gold.Level,
            [Silver1.CardId, Silver2.CardId],
            [(short)Silver1.Level, (short)Silver2.Level]);
    }

    private static RaceCardSlot Slot(string? cardId, int level)
    {
        return string.IsNullOrEmpty(cardId) ? RaceCardSlot.Empty : new RaceCardSlot(cardId, RaceCardLevels.Clamp(level));
    }

    private static T? At<T>(IReadOnlyList<T>? list, int index) => list is not null && index < list.Count ? list[index] : default;
}

public enum RaceCardHandFault
{
    None,
    /// <summary>The Silver list is not exactly two entries, or the slot index is not 0..2.</summary>
    Shape,
    /// <summary>A non-empty slot names a card the account does not own.</summary>
    NotOwned,
    /// <summary>An owned card is not in the catalogue.</summary>
    Unknown,
    /// <summary>A Silver in the Gold slot or a Gold in a Silver slot.</summary>
    WrongBand,
    /// <summary>The same card sits in two slots.</summary>
    Duplicate,
}

/// <summary>The hand rules both sides agree on: a card must exist and be owned, its band must match the slot, one
/// card may not fill two slots, and there are exactly two Silver slots. Two different cards of one stack group may
/// both be held; the channel caps clamp the sum.</summary>
public static class RaceCardHandRules
{
    public const int SlotCount = 3;
    public const int GoldSlot = 0;
    public const int SilverSlotCount = 2;

    public static RaceCardBand BandOf(int slot) => slot == GoldSlot ? RaceCardBand.Gold : RaceCardBand.Silver;

    public static IReadOnlyDictionary<string, RaceCardOwned> Index(IEnumerable<RaceCardOwned> cards)
    {
        var index = new Dictionary<string, RaceCardOwned>(StringComparer.Ordinal);
        foreach (var card in cards)
        {
            index[card.CardId] = card;
        }

        return index;
    }

    /// <summary>Checks a whole proposed hand without changing anything; the first failing slot names the fault.</summary>
    public static bool Validate(string? gold, IReadOnlyList<string>? silvers, IReadOnlyDictionary<string, RaceCardOwned> owned, out RaceCardHandFault fault)
    {
        if (silvers is null || silvers.Count != SilverSlotCount)
        {
            fault = RaceCardHandFault.Shape;
            return false;
        }

        string[] slots = [Id(gold), Id(silvers[0]), Id(silvers[1])];
        return ValidateSlots(slots, owned, out fault);
    }

    /// <summary>Whether putting <paramref name="cardId"/> into <paramref name="slot"/> leaves a legal hand.
    /// Clearing a slot is always legal, even while another slot holds a card that has since become invalid.</summary>
    public static bool CanEquip(string? gold, IReadOnlyList<string>? silvers, int slot, string? cardId, IReadOnlyDictionary<string, RaceCardOwned> owned, out RaceCardHandFault fault)
    {
        if (slot < 0 || slot >= SlotCount || silvers is null || silvers.Count != SilverSlotCount)
        {
            fault = RaceCardHandFault.Shape;
            return false;
        }

        if (Id(cardId).Length == 0)
        {
            fault = RaceCardHandFault.None;
            return true;
        }

        string[] slots = [Id(gold), Id(silvers[0]), Id(silvers[1])];
        slots[slot] = Id(cardId);
        return ValidateSlots(slots, owned, out fault);
    }

    /// <summary>Repairs a saved hand silently: a valid slot keeps its place, an unknown, unowned, wrong-band or
    /// repeated card falls out. An earlier slot wins a conflict with a later one.</summary>
    public static RaceCardHandIds Normalize(string? gold, IReadOnlyList<string>? silvers, IReadOnlyDictionary<string, RaceCardOwned> owned)
    {
        string[] saved = [Id(gold), SilverAt(silvers, 0), SilverAt(silvers, 1)];
        string[] result = [string.Empty, string.Empty, string.Empty];
        for (var slot = 0; slot < SlotCount; slot++)
        {
            if (saved[slot].Length == 0)
            {
                continue;
            }

            result[slot] = saved[slot];
            if (SlotFault(result, slot, owned) != RaceCardHandFault.None)
            {
                result[slot] = string.Empty;
            }
        }

        return new RaceCardHandIds(result[0], result[1], result[2]);
    }

    /// <summary>The entry-time snapshot: <see cref="Normalize"/>, then each surviving slot's card id and level.</summary>
    public static RaceCardHand Resolve(string? gold, IReadOnlyList<string>? silvers, IReadOnlyDictionary<string, RaceCardOwned> owned)
    {
        var ids = Normalize(gold, silvers, owned);
        return new RaceCardHand(SlotOf(ids.Gold, owned), SlotOf(ids.Silver1, owned), SlotOf(ids.Silver2, owned));
    }

    /// <summary>The hub code the server answers a fault with; null for <see cref="RaceCardHandFault.None"/>.</summary>
    public static string? HubErrorFor(RaceCardHandFault fault) => fault switch
    {
        RaceCardHandFault.Shape => HubErrors.LumiRaceHandShape,
        RaceCardHandFault.NotOwned => HubErrors.LumiRaceCardNotOwned,
        RaceCardHandFault.Unknown => HubErrors.LumiRaceCardUnknown,
        RaceCardHandFault.WrongBand => HubErrors.LumiRaceCardWrongBand,
        RaceCardHandFault.Duplicate => HubErrors.LumiRaceCardDuplicate,
        _ => null,
    };

    /// <summary>Test helper: how many unordered pairs of owned cards make a legal Silver pair.</summary>
    public static int LegalSilverPairCount(IReadOnlyDictionary<string, RaceCardOwned> owned)
    {
        var ids = owned.Keys.ToArray();
        var count = 0;
        for (var i = 0; i < ids.Length; i++)
        {
            for (var j = i + 1; j < ids.Length; j++)
            {
                if (Validate(string.Empty, [ids[i], ids[j]], owned, out _))
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Test helper: how many complete hands (a Gold and an unordered Silver pair) the owned cards allow.</summary>
    public static int LegalHandCount(IReadOnlyDictionary<string, RaceCardOwned> owned)
    {
        var ids = owned.Keys.ToArray();
        var count = 0;
        foreach (var gold in ids)
        {
            for (var i = 0; i < ids.Length; i++)
            {
                for (var j = i + 1; j < ids.Length; j++)
                {
                    if (Validate(gold, [ids[i], ids[j]], owned, out _))
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    private static bool ValidateSlots(string[] slots, IReadOnlyDictionary<string, RaceCardOwned> owned, out RaceCardHandFault fault)
    {
        for (var slot = 0; slot < SlotCount; slot++)
        {
            if (slots[slot].Length == 0)
            {
                continue;
            }

            fault = SlotFault(slots, slot, owned);
            if (fault != RaceCardHandFault.None)
            {
                return false;
            }
        }

        fault = RaceCardHandFault.None;
        return true;
    }

    private static RaceCardHandFault SlotFault(string[] slots, int slot, IReadOnlyDictionary<string, RaceCardOwned> owned)
    {
        var id = slots[slot];
        if (!owned.ContainsKey(id))
        {
            return RaceCardHandFault.NotOwned;
        }

        var card = RaceCardCatalogue.Find(id);
        if (card is null)
        {
            return RaceCardHandFault.Unknown;
        }

        if (card.Band != BandOf(slot))
        {
            return RaceCardHandFault.WrongBand;
        }

        for (var earlier = 0; earlier < slot; earlier++)
        {
            if (string.Equals(slots[earlier], id, StringComparison.Ordinal))
            {
                return RaceCardHandFault.Duplicate;
            }
        }

        return RaceCardHandFault.None;
    }

    private static string Id(string? cardId) => cardId ?? string.Empty;

    private static string SilverAt(IReadOnlyList<string>? silvers, int index)
    {
        return silvers is not null && index < silvers.Count ? Id(silvers[index]) : string.Empty;
    }

    private static RaceCardSlot SlotOf(string cardId, IReadOnlyDictionary<string, RaceCardOwned> owned)
    {
        if (cardId.Length == 0)
        {
            return RaceCardSlot.Empty;
        }

        return new RaceCardSlot(cardId, RaceCardLevels.Clamp(owned[cardId].Level));
    }
}
