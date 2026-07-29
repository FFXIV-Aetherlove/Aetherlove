using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AetherOS.Apps.Market;

public sealed record MarketRetainerListing(uint ItemId, string ItemName, int Quantity, bool Hq, long UnitPrice);

public sealed class MarketRetainerSnapshot
{
    public ulong RetainerId { get; set; }
    public string Name { get; set; } = "";
    public long Gil { get; set; }
    public int MarketItemCount { get; set; }
    public long MarketExpireUnix { get; set; }
    public DateTimeOffset? LastScanned { get; set; }
    public List<MarketRetainerListing> Listings { get; set; } = [];
}

public sealed record MarketInferredSale(uint ItemId, string ItemName, int Quantity, long UnitPrice,
    DateTimeOffset ObservedAt);

/// <summary>Host bridge to the player's own retainers. The roster fills once the summoning bell has been
/// opened; a retainer's listings and prices are captured while that retainer is summoned. Everything is
/// read-only: the desk never writes prices back to the game.</summary>
public interface IMarketDesk
{
    /// <summary>True once retainer data has been seen this session (the bell was opened).</summary>
    bool CaptureReady { get; }

    IReadOnlyList<MarketRetainerSnapshot> Snapshots { get; }

    IReadOnlyList<MarketInferredSale> RecentSales { get; }

    int UndercutCount { get; }

    /// <summary>Current market minimum for an own-listed item when it undercuts our price.</summary>
    bool TryGetUndercut(uint itemId, out long marketMin);

    event Action? Changed;

    /// <summary>Re-checks every own listing against current home-world minimums (batched, cached).</summary>
    Task RefreshUndercutsAsync();
}
