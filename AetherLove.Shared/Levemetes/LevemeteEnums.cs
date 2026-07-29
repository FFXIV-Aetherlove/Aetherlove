namespace AetherLove.Shared.Levemetes;

/// <summary>Classified categories. Append-only: values are wire data and are never renumbered. A client
/// renders unknown values through a fallback label rather than hiding them, so new categories can ship
/// server-side ahead of the client.</summary>
public enum LevemeteCategory : short
{
    HouseDecoration = 1,
    Gposing = 2,
    Commissions = 3,
    Dj = 4,
    VenueStaff = 5,
    BardsAndBands = 6,
    Mercenary = 7,
    CraftingGathering = 8,

    /// <summary>Excluded from browse unless the caller's filter opts in.</summary>
    Adult = 9,
}

/// <summary>Whether the poster is looking for a service or offering one.</summary>
public enum LevemeteKind : short
{
    LookingFor = 1,
    Offering = 2,
}

/// <summary>Ad lifecycle. Expired ads are delisted but never deleted, so an account's full ad history
/// stays available to moderation.</summary>
public enum LevemeteAdStatus : short
{
    Active = 1,
    PendingModeration = 2,
    Unlisted = 3,
    Expired = 4,
}
