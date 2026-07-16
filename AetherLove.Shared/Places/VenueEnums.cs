using System;

namespace AetherLove.Shared.Places;

/// <summary>Multi-select venue categories. Wire + storage values — append-only, never renumber.</summary>
[Flags]
public enum VenueTag : int
{
    None = 0,
    RoleplayVenue = 1 << 0,
    Barding = 1 << 1,

    /// <summary>18+ venue. Only ever shown to profiles with NsfwEnabled.</summary>
    Nsfw = 1 << 2,

    LiveDj = 1 << 3,
    SyncEnabled = 1 << 4,
    Nightclub = 1 << 5,
    BarTavern = 1 << 6,
    Cafe = 1 << 7,
    Restaurant = 1 << 8,
    Casino = 1 << 9,
    MaidCafe = 1 << 10,
    Bathhouse = 1 << 11,
    GposeStudio = 1 << 12,
    LiveMusic = 1 << 13,
    MarketShop = 1 << 14,
}

/// <summary>Residential district a venue's plot is in. Wire + storage values — append-only.</summary>
public enum HousingDistrict : short
{
    Unknown = 0,
    Mist = 1,
    LavenderBeds = 2,
    Goblet = 3,
    Shirogane = 4,
    Empyreum = 5,
}

/// <summary>Lifecycle/visibility state of a venue. Wire + storage values — append-only, never renumber.
/// Only <see cref="Active"/> venues are shown to browsers.</summary>
public enum VenueStatus : short
{
    /// <summary>Listed and visible in browse.</summary>
    Active = 1,

    /// <summary>Held during initial create because an image is awaiting moderation; hidden from browse until
    /// its images clear, then auto-promoted to <see cref="Active"/>. Not entered again after the venue goes live.</summary>
    PendingModeration = 2,

    /// <summary>Delisted by a moderator/owner; hidden from browse but intact for its owner.</summary>
    Unlisted = 3,
}
