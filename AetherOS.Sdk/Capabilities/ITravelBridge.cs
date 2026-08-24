namespace AetherOS.Sdk;

/// <summary>The residential districts a <see cref="TravelAddress"/> can name. The numbers match the client's
/// own housing-district enum, so callers holding one can cast rather than switch.</summary>
public enum TravelDistrict
{
    Unknown = 0,
    Mist = 1,
    LavenderBeds = 2,
    Goblet = 3,
    Shirogane = 4,
    Empyreum = 5,
}

/// <summary>Somewhere in the world a player can be sent. <paramref name="Room"/> is an apartment number and
/// wins over <paramref name="Plot"/> when both are set, matching how the address reads on screen.</summary>
public readonly record struct TravelAddress(string World, TravelDistrict District, int Ward, int Plot, int Room)
{
    /// <summary>Whether this names a place a provider could actually reach.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(World)
        && District != TravelDistrict.Unknown
        && Ward > 0
        && (Plot > 0 || Room > 0);
}

/// <summary>Travelling to a place in the game world, through whichever transport plugin the player happens to
/// have. AetherOS never moves the character itself, so with no provider installed every surface simply hides
/// its travel affordance: check <see cref="IsAvailable"/> before offering one.</summary>
public interface ITravelBridge
{
    /// <summary>The provider's display name for the button label, or null when nothing can travel. Shown to
    /// the player, so it credits whoever is doing the work.</summary>
    string? ProviderName { get; }

    /// <summary>Whether a provider is installed, loaded and reachable.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether the provider is already travelling. Offering a second trip mid-flight would cancel
    /// the first, so surfaces disable rather than hide while this is true.</summary>
    bool IsBusy { get; }

    /// <summary>Asks the provider to travel to <paramref name="address"/>. Returns false when it was not
    /// handed over at all; success only means the request was accepted, never that the trip finished.</summary>
    bool GoTo(TravelAddress address);

    /// <summary>Asks the provider to move the character to <paramref name="world"/> (world travel within the
    /// data center). Same contract as <see cref="GoTo"/>: acceptance, not arrival.</summary>
    bool GoToWorld(string world);
}
