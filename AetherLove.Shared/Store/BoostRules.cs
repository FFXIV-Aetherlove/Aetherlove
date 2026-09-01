using System;

namespace AetherLove.Shared.Store;

/// <summary>Which special effect a boosted listing wears. APPEND-ONLY: the value is persisted on the
/// boosted venue or ad and read by every client that draws that listing, so a renumbering would repaint
/// live boosts.</summary>
public enum BoostStyle : short
{
    /// <summary>A slow colour ribbon sweeping the card border.</summary>
    Aurora = 0,

    /// <summary>Sparks rising from the bottom edge.</summary>
    Ember = 1,

    /// <summary>A rotating rainbow rim light.</summary>
    Prism = 2,

    /// <summary>Twinkling motes over a soft halo.</summary>
    Starlight = 3,
}

/// <summary>What a boost is being spent on. Wire-only: the boost itself is stored as a window on the
/// target row, so nothing persists this.</summary>
public enum BoostTarget : short
{
    Venue = 0,
    Levemete = 1,
}

/// <summary>The boost window's arithmetic, shared so the client can show the same end date the server is
/// about to stamp. A boost is never a set of rows: it is one window on the venue or ad, which is what lets
/// an opening added halfway through inherit the boost without anything reconciling it.</summary>
public static class BoostRules
{
    /// <summary>Days one boost adds.</summary>
    public const int Days = 5;

    /// <summary>How far ahead the end may ever sit. Stacking is allowed up to here so a stockpile cannot
    /// buy a permanent slot at the top of the listings.</summary>
    public const int MaxDays = 15;

    /// <summary>Number of styles the picker offers; the enum's members, as a count.</summary>
    public const short StyleCount = 4;

    public static bool IsActive(DateTimeOffset? boostedUntilUtc, DateTimeOffset nowUtc) =>
        boostedUntilUtc is { } until && until > nowUtc;

    public static bool IsKnownStyle(short style) => style >= 0 && style < StyleCount;

    /// <summary>Where the window ends once one more boost is spent on it.</summary>
    public static DateTimeOffset Extend(DateTimeOffset? boostedUntilUtc, DateTimeOffset nowUtc)
    {
        var from = IsActive(boostedUntilUtc, nowUtc) ? boostedUntilUtc!.Value : nowUtc;
        return from.AddDays(Days);
    }

    /// <summary>True when one more boost would push the window past <see cref="MaxDays"/>.</summary>
    public static bool WouldExceedCap(DateTimeOffset? boostedUntilUtc, DateTimeOffset nowUtc) =>
        Extend(boostedUntilUtc, nowUtc) > nowUtc.AddDays(MaxDays);

    /// <summary>The powerup ref a target's boost is spent from.</summary>
    public static string RefFor(BoostTarget target) => target switch
    {
        BoostTarget.Levemete => StoreItemRefs.PowerupLevemeteBoost,
        _ => StoreItemRefs.PowerupVenueBoost,
    };
}
