using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherOS.PetKit.Engine;

/// <summary>A boop flourish. Everything shipping is drawn procedurally from the particle pools,
/// so a def is a recipe key plus identity; painted overlay sheets can replace a recipe later
/// without touching ownership or the wardrobe.
/// <para><paramref name="ItemRef"/> is the store ref, the one key ownership and the equipped
/// look both use. <paramref name="Signature"/> names the element this is the signature of and
/// is empty for the paid three; signatures are earned grants and never consult a shelf.</para></summary>
public sealed record ReactionDef(string ItemRef, string Name, string Procedural, string Signature)
{
    public const float DurationSeconds = 0.9f;

    /// <summary>The roster: six earned element signatures, plus the paid three kept for the
    /// later shelf (nothing surfaces them this release).</summary>
    public static readonly IReadOnlyList<ReactionDef> All =
    [
        new("reaction-fire", "Cinderburst", "fire", "fire"),
        new("reaction-ice", "Frostglint", "ice", "ice"),
        new("reaction-wind", "Galeswirl", "wind", "wind"),
        new("reaction-earth", "Stonemote", "earth", "earth"),
        new("reaction-lightning", "Crackle", "lightning", "lightning"),
        new("reaction-water", "Ripple", "water", "water"),
        new("reaction-hearts", "Heartburst", "hearts", ""),
        new("reaction-sparkles", "Sparkle Ring", "sparkles", ""),
        new("reaction-shards", "Shard Shimmer", "shards", ""),
    ];

    public static ReactionDef? Find(string itemRef) =>
        string.IsNullOrEmpty(itemRef)
            ? null
            : All.FirstOrDefault(r => string.Equals(r.ItemRef, itemRef, StringComparison.OrdinalIgnoreCase));

    public static ReactionDef? FindSignature(string element) =>
        string.IsNullOrEmpty(element)
            ? null
            : All.FirstOrDefault(r => string.Equals(r.Signature, element, StringComparison.OrdinalIgnoreCase));
}
