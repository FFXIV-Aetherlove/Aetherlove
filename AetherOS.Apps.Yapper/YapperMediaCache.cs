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

    /// <summary>How long a failed fetch waits before the next sighting may try again. Without it a card on
    /// screen asks again every frame, which turns one refused call into a rate-limit storm.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, Visual> _cache = new();
    private readonly ConcurrentDictionary<Guid, byte> _fetches = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _failedAtUtc = new();
    private readonly ConcurrentDictionary<Guid, string> _sources = new();
    private readonly ConcurrentDictionary<Guid, byte> _settled = new();
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
        // The store names the file by a content hash of its own; a session-seeded hash in the key here
        // left every earlier session's copy behind because the stale sweep only matches the key.
        var tex = AvatarDiskCache.Store(cacheDir, key, bytes);
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
            ReportDecode(imageId, visual, context);
            return visual;
        }
        Fetch(imageId, context);
        return null;
    }

    /// <summary>Says once per image whether the texture behind it decoded. Bytes that arrived but cannot be
    /// decoded draw exactly like an image that is still loading, so the log is the only place it shows.</summary>
    private void ReportDecode(Guid imageId, Visual visual, string? context)
    {
        if (visual.Tex is null || _settled.ContainsKey(imageId))
        {
            return;
        }
        if (visual.Tex.TryGetWrap(out var wrap, out var error))
        {
            if (wrap is not null && _settled.TryAdd(imageId, 0))
            {
                UiHost.Log.Debug($"[Yapper/media] {imageId:N} decoded {wrap.Width}x{wrap.Height}");
            }
            return;
        }
        if (error is not null && _settled.TryAdd(imageId, 0))
        {
            _sources.TryGetValue(imageId, out var source);
            UiHost.Log.Warning($"[Yapper/media] {imageId:N} DECODE FAILED for {context ?? "?"} ({source ?? "?"}): {error.GetType().Name}: {error.Message}");
        }
    }

    private void Fetch(Guid imageId, string? context)
    {
        if (_failedAtUtc.TryGetValue(imageId, out var failedAt) && DateTime.UtcNow - failedAt < RetryAfter)
        {
            return;
        }
        if (!_fetches.TryAdd(imageId, 0))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                UiHost.Log.Debug($"[Yapper/media] fetch {imageId:N} for {context ?? "?"}");
                // Media is immutable per id, so any prior download (content-hashed name) is reusable.
                if (Directory.Exists(cacheDir))
                {
                    var cached = Directory.EnumerateFiles(cacheDir, $"{imageId:N}_*").FirstOrDefault();
                    if (cached is not null)
                    {
                        _sources[imageId] = $"disk {Path.GetFileName(cached)}, {new FileInfo(cached).Length} bytes";
                        _cache[imageId] = new Visual(UiHost.TextureProvider.GetFromFile(cached), false);
                        UiHost.Log.Debug($"[Yapper/media] {imageId:N} from {_sources[imageId]}");
                        return;
                    }
                }
                var started = DateTime.UtcNow;
                var bytes = await host.GetYapImageAsync(imageId).ConfigureAwait(false);
                var took = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                if (bytes is { Length: > 0 })
                {
                    _sources[imageId] = $"server {ImageFormat.ExtensionFor(bytes)}, {bytes.Length} bytes";
                    var tex = AvatarDiskCache.Store(cacheDir, $"{imageId:N}", bytes);
                    _cache[imageId] = new Visual(tex, false);
                    if (tex is null)
                    {
                        UiHost.Log.Warning($"[Yapper/media] {imageId:N} arrived ({_sources[imageId]}, {took} ms) but could not be stored for {context ?? "?"}");
                    }
                    else
                    {
                        UiHost.Log.Debug($"[Yapper/media] {imageId:N} ok from {_sources[imageId]}, {took} ms");
                    }
                }
                else
                {
                    _cache[imageId] = new Visual(null, true);
                    UiHost.Log.Warning($"[Yapper/media] {imageId:N} EMPTY from server ({(bytes is null ? "null" : "0 bytes")}, {took} ms) for {context ?? "?"}");
                }
                _failedAtUtc.TryRemove(imageId, out _);
            }
            catch (Exception ex)
            {
                _failedAtUtc[imageId] = DateTime.UtcNow;
                _fetches.TryRemove(imageId, out _);
                UiHost.Log.Warning($"[Yapper/media] {imageId:N} THREW for {context ?? "?"}: {ex.GetType().Name}: {ex.Message}");
            }
        });
    }
}
