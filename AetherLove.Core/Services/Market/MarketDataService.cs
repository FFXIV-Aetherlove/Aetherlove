using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherLove.Services.Market;

/// <summary>The cached gateway every Market feature reads prices through. TTL caches per scope+item keep
/// repeat views free, failed lookups are negative-cached so broken ids can't cause retry loops, and batch
/// requests only fetch the stale slice in chunks of 100. All Universalis traffic in the plugin flows
/// through this single instance and its throttled client.</summary>
public sealed class MarketDataService
{
    private static readonly TimeSpan ItemTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AggregatedTtl = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan AggregatedMissTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FetchFailTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HistoryTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TaxTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan RecentTtl = TimeSpan.FromMinutes(5);

    private sealed record Cached<T>(T? Value, long FreshUntil) where T : class
    {
        public bool Fresh => Environment.TickCount64 < FreshUntil;
    }

    private readonly UniversalisClient _client;
    private readonly ConcurrentDictionary<string, Cached<MarketCurrentData>> _items = new();
    private readonly ConcurrentDictionary<string, Cached<AggregatedResult>> _aggregated = new();
    private readonly ConcurrentDictionary<string, Cached<MarketHistory>> _history = new();
    private readonly ConcurrentDictionary<string, Cached<Dictionary<string, int>>> _tax = new();
    private readonly ConcurrentDictionary<string, Cached<int[]>> _recent = new();
    private readonly ConcurrentDictionary<string, Task<MarketCurrentData?>> _itemFetches = new();
    private readonly ConcurrentDictionary<string, Task<MarketHistory?>> _historyFetches = new();

    public UniversalisClient Client => _client;

    public MarketDataService(UniversalisClient client)
    {
        _client = client;
    }

    private static Cached<T> Store<T>(T? value, TimeSpan ttl) where T : class =>
        new(value, Environment.TickCount64 + (long)ttl.TotalMilliseconds);

    public Task<MarketCurrentData?> GetItemAsync(MarketScope scope, uint itemId, CancellationToken ct)
    {
        var key = $"{scope.ApiName}:{itemId}";
        if (_items.TryGetValue(key, out var cached) && cached.Fresh)
        {
            return Task.FromResult(cached.Value);
        }
        return _itemFetches.GetOrAdd(key, async _ =>
        {
            try
            {
                var data = await _client.GetItemAsync(scope.ApiName, itemId, 20, 20, ct).ConfigureAwait(false);
                _items[key] = Store(data, data is null ? FetchFailTtl : ItemTtl);
                return data;
            }
            finally
            {
                _itemFetches.TryRemove(key, out Task<MarketCurrentData?>? _);
            }
        });
    }

    /// <summary>Aggregated stats for a set of ids, served from cache where fresh and fetched in chunks
    /// otherwise. Ids Universalis reports as failed are absent from the result and negative-cached.</summary>
    public async Task<IReadOnlyDictionary<uint, AggregatedResult>> GetAggregatedAsync(MarketScope scope,
        IReadOnlyList<uint> itemIds, CancellationToken ct)
    {
        var result = new Dictionary<uint, AggregatedResult>();
        var missing = new List<uint>();
        foreach (var id in itemIds.Distinct())
        {
            var key = $"{scope.ApiName}:{id}";
            if (_aggregated.TryGetValue(key, out var cached) && cached.Fresh)
            {
                if (cached.Value is not null)
                {
                    result[id] = cached.Value;
                }
            }
            else
            {
                missing.Add(id);
            }
        }

        foreach (var chunk in missing.Chunk(UniversalisClient.MaxIdsPerCall))
        {
            ct.ThrowIfCancellationRequested();
            var response = await _client.GetAggregatedAsync(scope.ApiName, chunk, ct).ConfigureAwait(false);
            if (response is null)
            {
                foreach (var id in chunk)
                {
                    _aggregated[$"{scope.ApiName}:{id}"] = Store<AggregatedResult>(null, FetchFailTtl);
                }
                continue;
            }
            foreach (var item in response.Results)
            {
                _aggregated[$"{scope.ApiName}:{item.ItemId}"] = Store(item, AggregatedTtl);
                result[item.ItemId] = item;
            }
            foreach (var failed in response.FailedItems)
            {
                _aggregated[$"{scope.ApiName}:{failed}"] = Store<AggregatedResult>(null, AggregatedMissTtl);
            }
        }
        return result;
    }

    public Task<MarketHistory?> GetHistoryAsync(MarketScope scope, uint itemId, TimeSpan window, CancellationToken ct)
    {
        var key = $"{scope.ApiName}:{itemId}:{(long)window.TotalDays}";
        if (_history.TryGetValue(key, out var cached) && cached.Fresh)
        {
            return Task.FromResult(cached.Value);
        }
        return _historyFetches.GetOrAdd(key, async _ =>
        {
            try
            {
                var data = await _client.GetHistoryAsync(scope.ApiName, itemId, 1800,
                    (long)window.TotalSeconds, ct).ConfigureAwait(false);
                _history[key] = Store(data, data is null ? FetchFailTtl : HistoryTtl);
                return data;
            }
            finally
            {
                _historyFetches.TryRemove(key, out Task<MarketHistory?>? _);
            }
        });
    }

    public async Task<Dictionary<string, int>?> GetTaxRatesAsync(string world, CancellationToken ct)
    {
        if (_tax.TryGetValue(world, out var cached) && cached.Fresh)
        {
            return cached.Value;
        }
        var data = await _client.GetTaxRatesAsync(world, ct).ConfigureAwait(false);
        _tax[world] = Store(data, data is null ? FetchFailTtl : TaxTtl);
        return data;
    }

    public async Task<int[]?> GetRecentlyUpdatedAsync(MarketScope scope, CancellationToken ct)
    {
        if (scope.Kind == MarketScopeKind.Region)
        {
            return null;
        }
        if (_recent.TryGetValue(scope.ApiName, out var cached) && cached.Fresh)
        {
            return cached.Value;
        }
        var data = await _client.GetRecentlyUpdatedAsync(scope.ApiName, scope.Kind == MarketScopeKind.World, ct)
            .ConfigureAwait(false);
        _recent[scope.ApiName] = Store(data, data is null ? FetchFailTtl : RecentTtl);
        return data;
    }
}
