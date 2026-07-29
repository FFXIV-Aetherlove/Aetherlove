using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AetherLove.Services.Market;

/// <summary>Raw Universalis v2 HTTP client. Every call runs through the shared throttle, returns null
/// (never throws) on any failure, and requests field projections where the endpoint supports them to keep
/// payloads small. Callers should prefer <see cref="MarketDataService"/>, which adds caching.</summary>
public sealed class UniversalisClient
{
    private const string BaseUrl = "https://universalis.app/api/v2";
    public const int MaxIdsPerCall = 100;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly UniversalisThrottle _throttle = new();

    public bool IsPaused => _throttle.IsPaused;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var version = "dev";
        try
        {
            version = UiHost.PluginInterface.Manifest.AssemblyVersion.ToString();
        }
        catch
        {
            // UiHost may not be initialised in tests; the fallback UA is fine there.
        }
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"AetherOS-Market/{version}");
        return client;
    }

    public Task<int[]?> GetMarketableAsync(CancellationToken ct) =>
        GetAsync<int[]>("/marketable", ct);

    public Task<MarketCurrentData?> GetItemAsync(string scope, uint itemId, int listings, int entries, CancellationToken ct) =>
        GetAsync<MarketCurrentData>($"/{Uri.EscapeDataString(scope)}/{itemId}?listings={listings}&entries={entries}", ct);

    public async Task<Dictionary<uint, MarketCurrentData>?> GetItemsAsync(string scope, IReadOnlyList<uint> itemIds,
        int listings, int entries, CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return new Dictionary<uint, MarketCurrentData>();
        }
        if (itemIds.Count == 1)
        {
            var single = await GetItemAsync(scope, itemIds[0], listings, entries, ct).ConfigureAwait(false);
            return single is null ? null : new Dictionary<uint, MarketCurrentData> { [itemIds[0]] = single };
        }

        var ids = string.Join(',', itemIds.Take(MaxIdsPerCall));
        var multi = await GetAsync<MarketMultiResponse>(
            $"/{Uri.EscapeDataString(scope)}/{ids}?listings={listings}&entries={entries}", ct).ConfigureAwait(false);
        if (multi is null)
        {
            return null;
        }
        var result = new Dictionary<uint, MarketCurrentData>(multi.Items.Count);
        foreach (var (key, value) in multi.Items)
        {
            if (uint.TryParse(key, out var id))
            {
                result[id] = value;
            }
        }
        return result;
    }

    public async Task<AggregatedResponse?> GetAggregatedAsync(string scope, IReadOnlyList<uint> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return new AggregatedResponse();
        }
        var ids = string.Join(',', itemIds.Take(MaxIdsPerCall));
        return await GetAsync<AggregatedResponse>($"/aggregated/{Uri.EscapeDataString(scope)}/{ids}", ct).ConfigureAwait(false);
    }

    public Task<MarketHistory?> GetHistoryAsync(string scope, uint itemId, int entriesToReturn, long? entriesWithinSeconds,
        CancellationToken ct)
    {
        var url = $"/history/{Uri.EscapeDataString(scope)}/{itemId}?entriesToReturn={entriesToReturn}";
        if (entriesWithinSeconds is { } within)
        {
            url += $"&entriesWithin={within}";
        }
        return GetAsync<MarketHistory>(url, ct);
    }

    public Task<Dictionary<string, int>?> GetTaxRatesAsync(string world, CancellationToken ct) =>
        GetAsync<Dictionary<string, int>>($"/tax-rates?world={Uri.EscapeDataString(world)}", ct);

    /// <summary>The ~200 most recently updated item ids, most recent first. World or DC scope only.</summary>
    public async Task<int[]?> GetRecentlyUpdatedAsync(string worldOrDc, bool isWorld, CancellationToken ct)
    {
        var param = isWorld ? "world" : "dcName";
        var wrapped = await GetAsync<RecentlyUpdatedResponse>(
            $"/extra/stats/recently-updated?{param}={Uri.EscapeDataString(worldOrDc)}", ct).ConfigureAwait(false);
        return wrapped?.Items;
    }

    private sealed class RecentlyUpdatedResponse
    {
        public int[]? Items { get; set; }
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        bool entered;
        try
        {
            entered = await _throttle.EnterAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        if (!entered)
        {
            return null;
        }
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(RequestTimeout);
            using var response = await Http.GetAsync(BaseUrl + path, cts.Token).ConfigureAwait(false);
            if ((int)response.StatusCode == 429)
            {
                _throttle.ReportRateLimited();
                return null;
            }
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                _throttle.ReportSuccess();
                return null;
            }
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, cts.Token).ConfigureAwait(false);
            _throttle.ReportSuccess();
            return value;
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                _throttle.ReportFailure();
                UiHost.Log.Debug($"[Universalis] GET {path} failed: {ex.Message}");
            }
            return null;
        }
        finally
        {
            _throttle.Exit();
        }
    }
}
