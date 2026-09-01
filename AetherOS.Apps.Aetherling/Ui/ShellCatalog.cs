using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Aetherling.Ui;

/// <summary>The client's shell roster: store ref to asset folder and display name. Names are the
/// creatures' own, literal English like <c>ReactionDef</c> names, identical in every language.
/// Pinch ships in this list and in the assets but nothing grants it this release; ownership is the
/// only gate, so listing it here costs nothing and a future grant needs no client change.</summary>
internal static class ShellCatalog
{
    internal readonly record struct ShellDef(string Ref, string Folder, string Name);

    /// <summary>Every drawn shell, in roster order.</summary>
    public static readonly IReadOnlyList<ShellDef> All =
    [
        new("shell-jellyv1", "jellyv1", "Bellow"),
        new("shell-pufferv1", "pufferv1", "Bloat"),
        new("shell-crabv1", "crabv1", "Pinch"),
        new("shell-serpentv1", "serpentv1", "Rattle"),
        new("shell-nautilusv1", "nautilusv1", "Curl"),
        new("shell-mothv1", "mothv1", "Flit"),
        new("shell-spintopv1", "spintopv1", "Whirl"),
        new("shell-lanternv1", "lanternv1", "Lumen"),
        new("shell-smoulderv1", "smoulderv1", "Smoulder"),
        new("shell-pennantv1", "pennantv1", "Furl"),
        new("shell-grumblev1", "grumblev1", "Grumble"),
        new("shell-mufflev1", "mufflev1", "Muffle"),
        new("shell-chimev1", "chimev1", "Chime"),
    ];

    public static ShellDef? Find(string itemRef)
    {
        foreach (var def in All)
        {
            if (string.Equals(def.Ref, itemRef, StringComparison.OrdinalIgnoreCase))
            {
                return def;
            }
        }
        return null;
    }

    /// <summary>The element's first-milestone shell ref, a deliberate MIRROR of the server's
    /// <c>AetherlingCatalog.ShellRef</c> (the GameScoring precedent): the meters on the pet page
    /// read ownership against these, and a pick changed on one side alone draws wrong meters.</summary>
    public static string FirstFor(string elementKey) => elementKey switch
    {
        "fire" => "shell-lanternv1",
        "ice" => "shell-mufflev1",
        "wind" => "shell-mothv1",
        "earth" => "shell-serpentv1",
        "lightning" => "shell-pennantv1",
        "water" => "shell-jellyv1",
        _ => "",
    };

    /// <summary>And the second milestone's, mirroring <c>AetherlingCatalog.SecondShellRef</c>.</summary>
    public static string SecondFor(string elementKey) => elementKey switch
    {
        "fire" => "shell-smoulderv1",
        "ice" => "shell-chimev1",
        "wind" => "shell-spintopv1",
        "earth" => "shell-nautilusv1",
        "lightning" => "shell-grumblev1",
        "water" => "shell-pufferv1",
        _ => "",
    };

    /// <summary>The element a form attunes the creature to: the one whose diet earned it. Read off the
    /// two tables above rather than a third one, exactly as the server reads it off its own pair. Empty
    /// for the trueform and for a form nothing grants, where the born element stands.</summary>
    public static string ElementOf(string itemRef)
    {
        foreach (var key in ElementKeys)
        {
            if (string.Equals(FirstFor(key), itemRef, StringComparison.OrdinalIgnoreCase)
                || string.Equals(SecondFor(key), itemRef, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }
        return string.Empty;
    }

    private static readonly string[] ElementKeys = ["fire", "ice", "wind", "earth", "lightning", "water"];
}
