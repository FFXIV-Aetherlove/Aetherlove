namespace AetherLove.Shared.Profile.Enums;

/// <summary>Curated supporter display-name styles. Wire + storage values, append-only, never renumber.
/// The server only stores the choice and omits it for non-supporters; rendering is client-side.</summary>
public enum NameStyle : short
{
    None = 0,

    // Static colors.
    Crimson = 1,
    Gold = 2,
    Emerald = 3,
    Sapphire = 4,
    Violet = 5,
    Rose = 6,

    // Animated.
    RainbowCycle = 7,
    Shimmer = 8,
    Pulse = 9,
}
