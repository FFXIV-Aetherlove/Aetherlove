using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using AetherLove.Shared.Store;
using Dalamud.Interface.Textures;

namespace AetherLove.Services;

/// <summary>Resolves avatar-ring frame refs to textures, disk-first (RingCache/ under the config dir)
/// with a hub fetch on a cold miss. Ring art is tiny and shared across every surface, so one instance
/// serves the whole client; installed into the AppKit <c>AvatarRings</c> registry at plugin boot.</summary>
public sealed class AvatarRingService : IDisposable
{
    private readonly AetherHubContext _hub;
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly CancellationTokenSource _cts = new();

    private static readonly TimeSpan RetryCooldown = TimeSpan.FromMinutes(5);

    private sealed class Entry
    {
        public ISharedImmediateTexture? Texture;
        public bool Fetching;
        public DateTime FailedAtUtc;
    }

    public AvatarRingService(AetherHubContext hub)
    {
        _hub = hub;
    }

    private static string CacheDir => Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "RingCache");

    /// <summary>The ring texture for a frame ref, or null while unknown/loading. Safe to call per frame.</summary>
    public ISharedImmediateTexture? Texture(string? frameRef)
    {
        if (string.IsNullOrEmpty(frameRef))
        {
            return null;
        }
        var entry = _entries.GetOrAdd(frameRef, static _ => new Entry());
        if (entry.Texture is not null)
        {
            return entry.Texture;
        }
        if (!entry.Fetching && DateTime.UtcNow - entry.FailedAtUtc > RetryCooldown)
        {
            entry.Fetching = true;
            if (!TryProbeDisk(frameRef, entry))
            {
                _ = FetchAsync(frameRef, entry);
            }
        }
        return entry.Texture;
    }

    public Task<AvatarRingDto[]> GetOwnedAsync(CancellationToken ct = default) => _hub.GetMyAvatarRingsAsync(ct);

    public Task EquipAsync(AvatarRingSurface surface, string? frameRef, CancellationToken ct = default) =>
        _hub.SetAvatarRingAsync(surface, frameRef, ct);

    private static string DiskKey(string frameRef)
    {
        var safe = new string(frameRef.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
        return $"ring_{safe}";
    }

    private bool TryProbeDisk(string frameRef, Entry entry)
    {
        try
        {
            if (!Directory.Exists(CacheDir))
            {
                return false;
            }
            var newest = Directory.EnumerateFiles(CacheDir, $"{DiskKey(frameRef)}_*")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (newest is null)
            {
                return false;
            }
            entry.Texture = UiHost.TextureProvider.GetFromFile(newest);
            entry.Fetching = false;
            return true;
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[AvatarRings] Could not probe the ring cache.");
            return false;
        }
    }

    private async Task FetchAsync(string frameRef, Entry entry)
    {
        try
        {
            var bytes = await _hub.GetAvatarFrameImageAsync(frameRef, _cts.Token).ConfigureAwait(false);
            if (bytes is not { Length: > 0 })
            {
                entry.FailedAtUtc = DateTime.UtcNow;
                return;
            }
            entry.Texture = AvatarDiskCache.Store(CacheDir, DiskKey(frameRef), bytes);
            if (entry.Texture is null)
            {
                entry.FailedAtUtc = DateTime.UtcNow;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            entry.FailedAtUtc = DateTime.UtcNow;
            UiHost.Log.Warning(ex, "[AvatarRings] Could not fetch ring art for {Ref}.", frameRef);
        }
        finally
        {
            entry.Fetching = false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
