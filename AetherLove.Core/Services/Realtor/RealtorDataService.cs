using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AetherLove.Services.Realtor;

/// <summary>Cached access to PaissaDB: the world list is held for a day, per-world detail for a few
/// minutes, and concurrent requests for the same key share one in-flight fetch, so browsing never
/// hammers the community service.</summary>
public sealed class RealtorDataService
{
    private static readonly TimeSpan WorldsTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan DetailTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FailTtl = TimeSpan.FromSeconds(60);

    private readonly PaissaClient _client;
    private readonly object _gate = new();
    private Task<List<PaissaWorldSummary>?>? _worldsTask;
    private DateTimeOffset _worldsAt = DateTimeOffset.MinValue;
    private readonly Dictionary<int, (Task<PaissaWorldDetail?> Task, DateTimeOffset At)> _details = [];

    public RealtorDataService(PaissaClient client)
    {
        _client = client;
    }

    public bool IsPaused => _client.IsPaused;

    public Task<List<PaissaWorldSummary>?> GetWorldsAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_worldsTask is { } task && !IsStale(task, _worldsAt, WorldsTtl))
            {
                return task;
            }
            _worldsAt = DateTimeOffset.UtcNow;
            _worldsTask = FetchWorldsAsync(ct);
            return _worldsTask;
        }
    }

    public Task<PaissaWorldDetail?> GetWorldAsync(int worldId, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_details.TryGetValue(worldId, out var entry) && !IsStale(entry.Task, entry.At, DetailTtl))
            {
                return entry.Task;
            }
            var task = FetchWorldDetailAsync(worldId, ct);
            _details[worldId] = (task, DateTimeOffset.UtcNow);
            return task;
        }
    }

    /// <summary>A finished-null (failed) task goes stale after <see cref="FailTtl"/> so the next open
    /// retries; successes live for the full TTL and in-flight fetches are always reused.</summary>
    private static bool IsStale<T>(Task<T?> task, DateTimeOffset fetchedAt, TimeSpan ttl) where T : class
    {
        var age = DateTimeOffset.UtcNow - fetchedAt;
        if (!task.IsCompleted)
        {
            return false;
        }
        if (task.IsFaulted || task.IsCanceled || task.Result is null)
        {
            return age > FailTtl;
        }
        return age > ttl;
    }

    private async Task<List<PaissaWorldSummary>?> FetchWorldsAsync(CancellationToken ct)
    {
        return await _client.GetWorldsAsync(ct).ConfigureAwait(false);
    }

    private async Task<PaissaWorldDetail?> FetchWorldDetailAsync(int worldId, CancellationToken ct)
    {
        var detail = await _client.GetWorldAsync(worldId, ct).ConfigureAwait(false);
        if (detail is null)
        {
            return null;
        }
        foreach (var district in detail.Districts)
        {
            if (district.OpenPlots is null && district.NumOpenPlots > 0)
            {
                var full = await _client.GetDistrictAsync(worldId, district.Id, ct).ConfigureAwait(false);
                district.OpenPlots = full?.OpenPlots ?? [];
            }
        }
        return detail;
    }
}
