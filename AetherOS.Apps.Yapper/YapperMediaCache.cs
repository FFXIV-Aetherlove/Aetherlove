using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AetherLove;
using AetherLove.Services;
using Dalamud.Interface.Textures;

namespace AetherOS.Apps.Yapper;

/// <summary>Lazy per-image fetch + disk-backed texture cache for yap media and card imagery. One
/// in-flight fetch per id; misses render as shimmer placeholders until the bytes land.</summary>
internal sealed class YapperMediaCache(IYapperHost host, string cacheDir)
{
    internal sealed record Visual(ISharedImmediateTexture? Tex, bool Gone);

    private readonly ConcurrentDictionary<Guid, Visual> _cache = new();
    private readonly ConcurrentDictionary<Guid, byte> _fetches = new();
    private readonly ConcurrentDictionary<string, (int Hash, ISharedImmediateTexture? Tex)> _inline = new();

    /// <summary>Texture for inline avatar bytes; re-resolves when the bytes change (avatar edits).</summary>
    public ISharedImmediateTexture? GetAvatar(Guid profileId, byte[] bytes) =>
        GetInline($"av_{profileId:N}", bytes);

    /// <summary>Texture for any inline image bytes (banners, avatars), memoized per key + content.</summary>
    public ISharedImmediateTexture? GetInline(string key, byte[] bytes)
    {
        var hash = System.HashCode.Combine(bytes.Length,
            bytes.Length >= 4 ? BitConverter.ToInt32(bytes, 0) : 0,
            bytes.Length >= 8 ? BitConverter.ToInt32(bytes, bytes.Length - 4) : 0);
        if (_inline.TryGetValue(key, out var entry) && entry.Hash == hash)
        {
            return entry.Tex;
        }
        var tex = AvatarDiskCache.Store(cacheDir, $"{key}_{hash:x8}", bytes);
        _inline[key] = (hash, tex);
        return tex;
    }

    /// <summary>The texture for an image id, kicking off the fetch on first sight. Null while loading;
    /// <see cref="Visual.Gone"/> when the server no longer has it. <paramref name="context"/> is diagnostic
    /// only: it names the yap that asked, so a trace can tell two surfaces apart.</summary>
    public Visual? Get(Guid imageId, string? context = null)
    {
        if (_cache.TryGetValue(imageId, out var visual))
        {
            return visual;
        }
        Fetch(imageId, context);
        return null;
    }

    private void Fetch(Guid imageId, string? context)
    {
        if (!_fetches.TryAdd(imageId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                UiHost.Log.Verbose($"[Yapper/media] fetch {imageId:N} for {context ?? "?"}");
                // Media is immutable per id, so any prior download (content-hashed name) is reusable.
                if (Directory.Exists(cacheDir))
                {
                    var cached = Directory.EnumerateFiles(cacheDir, $"{imageId:N}_*").FirstOrDefault();
                    if (cached is not null)
                    {
                        _cache[imageId] = new Visual(UiHost.TextureProvider.GetFromFile(cached), false);
                        UiHost.Log.Verbose($"[Yapper/media] {imageId:N} from disk cache");
                        return;
                    }
                }
                var bytes = await host.GetYapImageAsync(imageId).ConfigureAwait(false);
                if (bytes is { Length: > 0 })
                {
                    _cache[imageId] = new Visual(AvatarDiskCache.Store(cacheDir, $"{imageId:N}", bytes), false);
                    UiHost.Log.Verbose($"[Yapper/media] {imageId:N} ok, {bytes.Length} bytes");
                }
                else
                {
                    _cache[imageId] = new Visual(null, true);
                    UiHost.Log.Verbose($"[Yapper/media] {imageId:N} EMPTY ({(bytes is null ? "null" : "0 bytes")}) for {context ?? "?"}");
                }
            }
            catch (Exception ex)
            {
                // Transient failure: allow a retry on the next sighting.
                _fetches.TryRemove(imageId, out _);
                UiHost.Log.Verbose($"[Yapper/media] {imageId:N} THREW for {context ?? "?"}: {ex.GetType().Name}: {ex.Message}");
            }
        });
    }
}
