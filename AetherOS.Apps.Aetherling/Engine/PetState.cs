using System;
using System.Collections.Generic;
using System.Linq;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Store;
using AetherOS.Apps.Aetherling.Rendering;

namespace AetherOS.Apps.Aetherling.Engine;

/// <summary>Pure reads over the server snapshot and the store inventory: which form is on
/// screen, what the gates say, what is owned. The server owns every fact here; this only
/// translates them for surfaces.</summary>
internal static class PetState
{
    /// <summary>The sheet folder for the pet's current form, derived from the fed count alone.</summary>
    public static string FormFolder(AetherlingDto? dto)
    {
        if (dto?.Adult is not null)
        {
            return CoreAssets.AdultFolder;
        }
        var fed = dto?.Growth?.GrowthFed ?? 0;
        var perStage = Math.Max((short)1, dto?.Growth?.FeedsPerStage ?? 3);
        if (fed >= perStage * 2)
        {
            return CoreAssets.Hatchling3Folder;
        }
        return fed >= perStage ? CoreAssets.Hatchling2Folder : CoreAssets.HatchlingFolder;
    }

    /// <summary>The body for a rung of the growth ladder, for a creature whose snapshot this client will
    /// never see: a party member's. Same ladder <see cref="FormFolder"/> walks, named by the number the
    /// wire carries rather than by anything asset-shaped.</summary>
    public static string FormFolderForStage(short stage) => stage switch
    {
        >= 3 => CoreAssets.AdultFolder,
        2 => CoreAssets.Hatchling3Folder,
        1 => CoreAssets.Hatchling2Folder,
        _ => CoreAssets.HatchlingFolder,
    };

    /// <summary>Time left on the growth feed gate, computed against the server's clock through
    /// the caller's stored offset; zero when feedable or grown.</summary>
    public static TimeSpan FeedGateRemaining(AetherlingDto dto, TimeSpan serverOffset)
    {
        if (dto.Adult is not null || dto.Growth?.LastFedAtUtc is not { } last)
        {
            return TimeSpan.Zero;
        }
        var serverNow = DateTimeOffset.UtcNow + serverOffset;
        var readyAt = last.AddMinutes(dto.Growth.FeedGateMinutes);
        return readyAt > serverNow ? readyAt - serverNow : TimeSpan.Zero;
    }

    /// <summary>Adult meals left today, from the snapshot's own counters.</summary>
    public static int AdultFeedsLeft(AetherlingDto dto) =>
        dto.Adult is { } adult ? Math.Max(0, adult.FeedsPerDay - adult.FeedsToday) : 0;

    public static int CrystalCount(IReadOnlyList<StoreInventoryItemDto>? inventory, Elements.ElementDef element) =>
        inventory?.FirstOrDefault(i =>
                i.ItemKind == StoreItemKind.AetherlingConsumable
                && string.Equals(i.ItemRef, Elements.CrystalRef(element), StringComparison.OrdinalIgnoreCase))
            ?.Quantity ?? 0;

    public static HashSet<string> OwnedRefs(
        IReadOnlyList<StoreInventoryItemDto>? inventory, params StoreItemKind[] kinds)
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (inventory is null)
        {
            return owned;
        }
        foreach (var item in inventory)
        {
            if (item.Quantity > 0 && kinds.Contains(item.ItemKind))
            {
                owned.Add(item.ItemRef);
            }
        }
        return owned;
    }

    /// <summary>The diet count for one element, from the adult ledger.</summary>
    public static int DietCount(AetherlingDto dto, Elements.ElementDef element) =>
        dto.Adult?.Diet.FirstOrDefault(d => d.Element == (short)element.Value)?.Count ?? 0;
}
