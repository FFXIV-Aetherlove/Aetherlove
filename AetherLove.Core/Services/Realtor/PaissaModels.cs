using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AetherLove.Services.Realtor;

/// <summary>Bit flags of <see cref="PaissaPlot.PurchaseSystem"/>.</summary>
public static class PaissaPurchase
{
    public const int Lottery = 1;
    public const int FreeCompany = 2;
    public const int Individual = 4;
}

/// <summary>Values of <see cref="PaissaPlot.LottoPhase"/>.</summary>
public static class PaissaLottoPhase
{
    public const int Accepting = 1;
    public const int Results = 2;
    public const int Unavailable = 3;
}

public sealed class PaissaWorldSummary
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("datacenter_id")] public int DatacenterId { get; set; }
    [JsonPropertyName("datacenter_name")] public string DatacenterName { get; set; } = "";
}

public sealed class PaissaWorldDetail
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("districts")] public List<PaissaDistrict> Districts { get; set; } = [];
    [JsonPropertyName("num_open_plots")] public int NumOpenPlots { get; set; }
    [JsonPropertyName("oldest_plot_time")] public double OldestPlotTime { get; set; }
}

public sealed class PaissaDistrict
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("num_open_plots")] public int NumOpenPlots { get; set; }
    [JsonPropertyName("oldest_plot_time")] public double OldestPlotTime { get; set; }
    [JsonPropertyName("open_plots")] public List<PaissaPlot>? OpenPlots { get; set; }
}

/// <summary>One open plot as reported by PaissaDB. Ward and plot numbers are 0-based on the wire;
/// display adds 1. Timestamps are unix epoch seconds.</summary>
public sealed class PaissaPlot
{
    [JsonPropertyName("world_id")] public int WorldId { get; set; }
    [JsonPropertyName("district_id")] public int DistrictId { get; set; }
    [JsonPropertyName("ward_number")] public int WardNumber { get; set; }
    [JsonPropertyName("plot_number")] public int PlotNumber { get; set; }
    [JsonPropertyName("size")] public int Size { get; set; }
    [JsonPropertyName("price")] public long Price { get; set; }
    [JsonPropertyName("last_updated_time")] public double LastUpdatedTime { get; set; }
    [JsonPropertyName("first_seen_time")] public double FirstSeenTime { get; set; }
    [JsonPropertyName("est_time_open_min")] public double EstTimeOpenMin { get; set; }
    [JsonPropertyName("est_time_open_max")] public double EstTimeOpenMax { get; set; }
    [JsonPropertyName("purchase_system")] public int PurchaseSystem { get; set; }
    [JsonPropertyName("lotto_entries")] public int? LottoEntries { get; set; }
    [JsonPropertyName("lotto_phase")] public int? LottoPhase { get; set; }
    [JsonPropertyName("lotto_phase_until")] public double? LottoPhaseUntil { get; set; }

    public bool IsLottery => (PurchaseSystem & PaissaPurchase.Lottery) != 0;
    public bool AllowsFreeCompany => (PurchaseSystem & PaissaPurchase.FreeCompany) != 0;
    public bool AllowsIndividual => (PurchaseSystem & PaissaPurchase.Individual) != 0;

    public DateTimeOffset LastUpdatedAt => DateTimeOffset.FromUnixTimeSeconds((long)LastUpdatedTime);
    public DateTimeOffset? PhaseUntil => LottoPhaseUntil is { } until and > 0
        ? DateTimeOffset.FromUnixTimeSeconds((long)until)
        : null;
}
