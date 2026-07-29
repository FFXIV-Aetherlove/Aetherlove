using AetherLove.Shared.Profile.Enums;

namespace AetherLove.Services.Market;

public enum MarketScopeKind
{
    World = 0,
    DataCenter = 1,
    Region = 2,
}

/// <summary>A Universalis query scope: a world, data center, or region, carrying the exact API path
/// segment (regions are spelled like "North-America").</summary>
public readonly record struct MarketScope(MarketScopeKind Kind, string ApiName)
{
    public override string ToString() => ApiName;
}

public static class MarketScopes
{
    public static string RegionApiName(Region region) => region switch
    {
        Region.NorthAmerica => "North-America",
        Region.Japan => "Japan",
        Region.Oceania => "Oceania",
        _ => "Europe",
    };

    private static (MarketScope World, MarketScope DataCenter, MarketScope Region)? _cached;

    /// <summary>The player's current world/DC/region as scopes. Live detection needs the framework thread,
    /// so off-thread callers (and brief detection hiccups) fall back to the last successful result.</summary>
    public static (MarketScope World, MarketScope DataCenter, MarketScope Region)? DetectCurrent()
    {
        var loc = VenueLocationDetector.Detect();
        if (loc.World.Length == 0 || loc.DataCenter.Length == 0)
        {
            return _cached;
        }
        var scopes = (new MarketScope(MarketScopeKind.World, loc.World),
            new MarketScope(MarketScopeKind.DataCenter, loc.DataCenter),
            new MarketScope(MarketScopeKind.Region, RegionApiName(loc.Region)));
        _cached = scopes;
        return scopes;
    }
}
