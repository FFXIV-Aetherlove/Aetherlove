using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using Dalamud.Interface.Textures;

namespace AetherOS.Apps.Store;

/// <summary>Lazy per-product art fetch + disk-backed texture cache, the Yapper media cache shape with
/// one addition: a small fetch semaphore, because a storefront can surface thirty images in one frame
/// and the hub does not deserve thirty parallel calls. Misses render as shimmer placeholders.</summary>
internal sealed class StoreMediaCache(IStoreHost host, string cacheDir)
{
    internal sealed record Visual(ISharedImmediateTexture? Tex, bool Gone);

    private const int MaxConcurrentFetches = 4;

    private readonly ConcurrentDictionary<string, Visual> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _fetches = new();
    private readonly SemaphoreSlim _gate = new(MaxConcurrentFetches, MaxConcurrentFetches);

    /// <summary>The texture for a product's art, kicking off the fetch on first sight. Null while
    /// loading; <see cref="Visual.Gone"/> when the product has none.
    ///
    /// The version is the server's stamp for WHICH image this is. It is the entire mechanism by which
    /// replaced art reaches an install that already fetched the old one: without it the disk hit below
    /// answers forever and the server is never asked again.</summary>
    public Visual? Get(Guid productId, int version = 0) =>
        Get(productId, version, host.GetStoreProductImageAsync);

    /// <summary>A collection card's banner. Ids are version-7 guids, so all three art kinds share one
    /// cache without any chance of collision.</summary>
    public Visual? GetCollection(Guid collectionId, int version = 0) =>
        Get(collectionId, version, host.GetStoreCollectionImageAsync);

    /// <summary>A category tile's banner.</summary>
    public Visual? GetCategory(Guid categoryId, int version = 0) =>
        Get(categoryId, version, host.GetStoreCategoryImageAsync);

    /// <summary>A theme's wallpaper, watermarked by the server. Keyed by the same product id as the shelf
    /// art, so this only ever runs on an instance with its own cache directory.</summary>
    public Visual? GetThemeBackground(Guid productId, int version = 0) =>
        Get(productId, version, host.GetStoreThemeBackgroundPreviewAsync);

    private Visual? Get(Guid id, int version, Func<Guid, CancellationToken, Task<byte[]?>> fetch)
    {
        var key = CacheKey(id, version);
        if (_cache.TryGetValue(key, out var visual))
        {
            return visual;
        }
        Fetch(id, key, fetch);
        return null;
    }

    /// <summary>Forgets every version of one subject's art so the next sighting fetches again; for what the
    /// version stamp cannot cover, such as retrying a fetch that failed.</summary>
    public void Evict(Guid id)
    {
        var prefix = $"{id:N}v";
        foreach (var key in _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _cache.TryRemove(key, out _);
            _fetches.TryRemove(key, out _);
        }
    }

    private static string CacheKey(Guid id, int version) => $"{id:N}v{version:X8}";

    private void Fetch(Guid id, string key, Func<Guid, CancellationToken, Task<byte[]?>> fetch)
    {
        if (!_fetches.TryAdd(key, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Directory.Exists(cacheDir))
                {
                    var cached = Directory.EnumerateFiles(cacheDir, $"{key}_*").FirstOrDefault();
                    if (cached is not null)
                    {
                        _cache[key] = new Visual(UiHost.TextureProvider.GetFromFile(cached), false);
                        return;
                    }
                }
                var bytes = await fetch(id, default).ConfigureAwait(false);
                _cache[key] = bytes is { Length: > 0 }
                    ? new Visual(AvatarDiskCache.Store(cacheDir, key, bytes), false)
                    : new Visual(null, true);
                SweepOtherVersions(id, key);
            }
            catch (Exception ex)
            {
                // Transient failure: allow a retry on the next sighting.
                _fetches.TryRemove(key, out _);
                UiHost.Log.Verbose($"[Store/media] {key} fetch threw: {ex.GetType().Name}");
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    /// <summary>Deletes what an earlier version of this subject's art left on disk. AvatarDiskCache only
    /// sweeps siblings of the SAME key, so without this every re-upload leaks its predecessor.</summary>
    private void SweepOtherVersions(Guid id, string keep)
    {
        try
        {
            if (!Directory.Exists(cacheDir))
            {
                return;
            }
            foreach (var path in Directory.EnumerateFiles(cacheDir, $"{id:N}v*"))
            {
                if (!Path.GetFileName(path).StartsWith($"{keep}_", StringComparison.Ordinal))
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Verbose($"[Store/media] sweeping old art for {id:N} threw: {ex.GetType().Name}");
        }
    }
}
