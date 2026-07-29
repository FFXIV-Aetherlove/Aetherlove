using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AetherLove.Services.Market;

/// <summary>Wire models for the Universalis v2 API. Field casing is inconsistent across endpoints
/// (itemID vs itemId), so deserialization runs case-insensitive and the odd ones carry explicit names.
/// Empty objects mean "no data" on the aggregated endpoint, which is why every tier is nullable.</summary>
public sealed class MarketCurrentData
{
    [JsonPropertyName("itemID")] public uint ItemId { get; set; }
    public long LastUploadTime { get; set; }
    public List<MarketListing> Listings { get; set; } = [];
    public List<MarketSale> RecentHistory { get; set; } = [];
    public double CurrentAveragePriceNQ { get; set; }
    public double CurrentAveragePriceHQ { get; set; }
    public double AveragePriceNQ { get; set; }
    public double AveragePriceHQ { get; set; }
    public long MinPriceNQ { get; set; }
    public long MinPriceHQ { get; set; }
    public long MaxPriceNQ { get; set; }
    public long MaxPriceHQ { get; set; }
    public double NqSaleVelocity { get; set; }
    public double HqSaleVelocity { get; set; }
    public string? WorldName { get; set; }
    public string? DcName { get; set; }
    public string? RegionName { get; set; }
    public bool HasData { get; set; }

    public DateTimeOffset? LastUpload =>
        LastUploadTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(LastUploadTime) : null;

    public long MinPrice(bool hq) => hq ? MinPriceHQ : MinPriceNQ;

    public double AverageSalePrice(bool hq) => hq ? AveragePriceHQ : AveragePriceNQ;
}

public sealed class MarketListing
{
    public long LastReviewTime { get; set; }
    public long PricePerUnit { get; set; }
    public int Quantity { get; set; }
    public bool Hq { get; set; }
    public long Total { get; set; }
    public long Tax { get; set; }
    public string RetainerName { get; set; } = "";
    public int RetainerCity { get; set; }
    public bool OnMannequin { get; set; }
    public string? WorldName { get; set; }
}

public sealed class MarketSale
{
    public bool Hq { get; set; }
    public long PricePerUnit { get; set; }
    public int Quantity { get; set; }
    public long Timestamp { get; set; }
    public string? BuyerName { get; set; }
    public bool OnMannequin { get; set; }
    public string? WorldName { get; set; }

    public DateTimeOffset When => DateTimeOffset.FromUnixTimeSeconds(Timestamp);
}

public sealed class MarketMultiResponse
{
    public Dictionary<string, MarketCurrentData> Items { get; set; } = [];
    public List<uint> UnresolvedItems { get; set; } = [];
}

public sealed class AggregatedResponse
{
    public List<AggregatedResult> Results { get; set; } = [];
    public List<uint> FailedItems { get; set; } = [];
}

public sealed class AggregatedResult
{
    public uint ItemId { get; set; }
    public AggregatedQuality Nq { get; set; } = new();
    public AggregatedQuality Hq { get; set; } = new();

    public AggregatedQuality Quality(bool hq) => hq ? Hq : Nq;
}

public sealed class AggregatedQuality
{
    public AggregatedTier<AggregatedPricePoint>? MinListing { get; set; }
    public AggregatedTier<AggregatedPricePoint>? RecentPurchase { get; set; }
    public AggregatedTier<AggregatedAverage>? AverageSalePrice { get; set; }
    public AggregatedTier<AggregatedVelocity>? DailySaleVelocity { get; set; }
}

/// <summary>The world tier is absent when the query scope is a DC or region; dc/region carry the world
/// that holds the value. A tier object that deserialized but has no data has price 0 and must be ignored.</summary>
public sealed class AggregatedTier<T> where T : class
{
    public T? World { get; set; }
    public T? Dc { get; set; }
    public T? Region { get; set; }

    public T? At(MarketScopeKind kind) => kind switch
    {
        MarketScopeKind.World => World,
        MarketScopeKind.DataCenter => Dc,
        _ => Region,
    };
}

public sealed class AggregatedPricePoint
{
    public long Price { get; set; }
    public int? WorldId { get; set; }
    public long? Timestamp { get; set; }

    public DateTimeOffset? When =>
        Timestamp is > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(Timestamp.Value) : null;
}

public sealed class AggregatedAverage
{
    public double Price { get; set; }
}

public sealed class AggregatedVelocity
{
    public double Quantity { get; set; }
}

public sealed class MarketHistory
{
    [JsonPropertyName("itemID")] public uint ItemId { get; set; }
    public List<MarketSale> Entries { get; set; } = [];
    public double NqSaleVelocity { get; set; }
    public double HqSaleVelocity { get; set; }
    public string? WorldName { get; set; }
}
