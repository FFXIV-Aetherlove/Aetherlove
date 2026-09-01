using System;
using System.Collections.Generic;
using System.Linq;
using AetherLove.Shared.Aetherling;
using AetherLove.Shared.Store;
using AetherOS.PetKit.Rendering;

namespace AetherOS.PetKit.Engine;

/// <summary>Pure reads over the server snapshot and the store inventory: which form is on
/// screen, what the gates say, what is owned. The server owns every fact here; this only
/// translates them for surfaces.</summary>
public static class PetState
{
    /// <summary>A dev-only shell override for the OWN adult pet, session-local and never on the
    /// wire, written only by the debug window. Names an asset folder under the pet tree
    /// ("jellyv1"); null or empty defers to the worn look.</summary>
    public static string? ShellOverride { get; set; }

    /// <summary>The asset folders a shell ref may resolve to. An unknown ref (a future shell this
    /// build has never heard of) heals to the trueform on screen rather than to a missing body;
    /// the look itself is left alone, exactly the retired-shell rule the prototype recorded.</summary>
    private static readonly HashSet<string> ShellFolders = new(StringComparer.Ordinal)
    {
        "jellyv1", "pufferv1", "crabv1",
        "serpentv1", "nautilusv1",
        "mothv1", "spintopv1",
        "lanternv1", "smoulderv1",
        "pennantv1", "grumblev1",
        "mufflev1", "chimev1",
    };

    /// <summary>An adult's folder for a worn shell ref ("shell-jellyv1"), the trueform for none
    /// or an unknown one.</summary>
    public static string ShellFolderFor(string? shellRef)
    {
        if (string.IsNullOrEmpty(shellRef))
        {
            return CoreAssets.AdultFolder;
        }
        const string Prefix = "shell-";
        var folder = shellRef.StartsWith(Prefix, StringComparison.Ordinal)
            ? shellRef[Prefix.Length..]
            : shellRef;
        return ShellFolders.Contains(folder) ? folder : CoreAssets.AdultFolder;
    }

    /// <summary>The sheet folder for the pet's current form: the growth ladder below adulthood,
    /// then the worn shell (the dev override outranks it).</summary>
    public static string FormFolder(AetherlingDto? dto)
    {
        if (dto?.Adult is not null)
        {
            return string.IsNullOrEmpty(ShellOverride)
                ? ShellFolderFor(dto.Look?.Shell)
                : ShellOverride;
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
    /// wire carries rather than by anything asset-shaped; the shell ref rides the same wire.</summary>
    public static string FormFolderForStage(short stage, string? shell = null) => stage switch
    {
        >= 3 => ShellFolderFor(shell),
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

    /// <summary>The element the creature answers to now: the worn form's, or the one it was born with
    /// while it wears none. The server decides it and sends it; one that predates the field sends zero,
    /// and the born element stands. Anything asking "which element is this pet" in play means this one.
    /// </summary>
    public static short AttunedElement(AetherlingDto? core)
    {
        if (core?.Adult is not { } adult)
        {
            return 0;
        }
        return adult.AttunedElement > 0 ? adult.AttunedElement : adult.Element;
    }

    /// <summary>The diet count for one element, from the adult ledger.</summary>
    public static int DietCount(AetherlingDto dto, Elements.ElementDef element) =>
        dto.Adult?.Diet.FirstOrDefault(d => d.Element == (short)element.Value)?.Count ?? 0;
}
