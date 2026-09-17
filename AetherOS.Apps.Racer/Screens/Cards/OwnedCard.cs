using System;
using System.Collections.Generic;
using AetherLove.Shared.Racing;
using AetherLove.Shared.Racing.Cards;

namespace AetherOS.Apps.Racer.Screens.Cards;

/// <summary>A card the player owns as the card screens draw it: its album row and its catalogue card.</summary>
internal sealed record OwnedCard(LumiRaceCardDto Row, RaceCard Card)
{
    public int Level => RaceCardLevels.Clamp(Row.Level);

    /// <summary>Every owned card the catalogue carries, by card id. A card the catalogue does not carry is left
    /// out.</summary>
    public static Dictionary<string, OwnedCard> Collect(LumiRaceCardsDto cards)
    {
        var byCard = new Dictionary<string, OwnedCard>(StringComparer.Ordinal);
        foreach (var row in cards.Cards)
        {
            if (RaceCardCatalogue.Find(row.CardId) is { } card)
            {
                byCard[card.Id] = new OwnedCard(row, card);
            }
        }

        return byCard;
    }
}
