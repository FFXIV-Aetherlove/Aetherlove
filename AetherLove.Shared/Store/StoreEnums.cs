namespace AetherLove.Shared.Store;

/// <summary>What kind of thing a store product unlocks. Together with <c>ItemRef</c> (the catalogue key
/// string) it forms the product's stable identity: the pair is unique per product and is what future
/// effect wiring keys off, so members are append-only forever and never renumbered. First-party refs
/// live in <see cref="StoreItemRefs"/>; Aetherling refs are that catalogue's own keys.</summary>
public enum StoreItemKind : short
{
    Unknown = 0,

    /// <summary>A decorative frame drawn around the user's avatar across apps.</summary>
    AvatarFrame = 1,

    /// <summary>A pack of extra phone themes for the appearance picker.</summary>
    ThemePack = 2,

    /// <summary>A consumable boost (venue listing, yap, weekly spark cap, ...).</summary>
    Powerup = 3,

    /// <summary>A composed product: buying it grants every child product at a custom price.</summary>
    Bundle = 4,

    AetherlingPalette = 10,
    AetherlingAspect = 11,
    AetherlingAccessory = 12,
    AetherlingArms = 13,
    AetherlingConsumable = 14,
    AetherlingIdentity = 15,
    AetherlingReaction = 16,
    AetherlingShell = 17,
}

/// <summary>Browse sort orders.</summary>
public enum StoreSort : short
{
    Featured = 0,
    Newest = 1,
    PriceAscending = 2,
    PriceDescending = 3,
    MostBought = 4,
}

/// <summary>Which home-button renderer a purchased theme wears. APPEND-ONLY: the value is stored.</summary>
public enum StoreThemeHomeShape : short
{
    /// <summary>A neon rounded square, for frames whose art has a bottom cradle.</summary>
    NeonSquare = 0,

    /// <summary>A solid golden bar, wider than tall, for ornate frames with no cradle.</summary>
    GoldenPill = 1,

    /// <summary>The plain white iOS pill; takes no tunables.</summary>
    Pill = 2,
}
