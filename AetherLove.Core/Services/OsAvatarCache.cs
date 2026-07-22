using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using Dalamud.Interface.Textures;

namespace AetherLove.Services;

/// <summary>Keeps the account's OS avatar warm for the shell (Settings card, notification shade). The last
/// disk-cached copy shows instantly while <see cref="Refresh"/> swaps in a fresh one; <see cref="SetFromBytes"/>
/// lets OS onboarding cache the just-uploaded image without a round-trip.</summary>
public sealed class OsAvatarCache : IDisposable
{
    private const string Key = "os-avatar";

    private readonly AetherHubContext _hub;

    private ISharedImmediateTexture? _texture;
    private bool _diskProbed;
    private CancellationTokenSource _cts = new();

    public OsAvatarCache(AetherHubContext hub)
    {
        _hub = hub;
    }

    private static string CacheDir => ImageCacheCleaner.MatchOverlayCacheDir;

    /// <summary>The last known OS avatar, or null before the first fetch on a fresh install / for an account with
    /// no avatar set.</summary>
    public ISharedImmediateTexture? Texture
    {
        get
        {
            if (_texture is null && !_diskProbed)
            {
                _diskProbed = true;
                ProbeDisk();
            }
            return _texture;
        }
    }

    public void Refresh(bool onlyIfCold = false)
    {
        if (onlyIfCold && Texture is not null)
        {
            return;
        }

        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var dto = await _hub.GetAccountInfoAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested || dto.OsAvatarWebp is not { Length: > 0 } bytes)
                {
                    return;
                }
                Store(bytes, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[OsAvatarCache] Refresh failed.");
            }
        }, ct);
    }

    /// <summary>Drops the in-memory texture so a cold refresh re-fetches; used when the disk cache was wiped
    /// underneath us (the clearcache flow), where a stale texture pointing at a now-deleted file would otherwise
    /// render as the blank fallback and never reload.</summary>
    public void Invalidate()
    {
        _cts.Cancel();
        _texture = null;
        _diskProbed = false;
    }

    /// <summary>Caches bytes the caller already has (e.g. the WebP returned by the OS-avatar upload).</summary>
    public void SetFromBytes(byte[] bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return;
        }
        Store(bytes, CancellationToken.None);
    }

    private void ProbeDisk()
    {
        try
        {
            if (!Directory.Exists(CacheDir))
            {
                return;
            }
            var newest = Directory.EnumerateFiles(CacheDir, Key + "_*")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (newest is not null)
            {
                _texture = UiHost.TextureProvider.GetFromFile(newest);
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[OsAvatarCache] Could not load the cached avatar.");
        }
    }

    private void Store(byte[] bytes, CancellationToken ct)
    {
        var tex = AvatarDiskCache.Store(CacheDir, Key, bytes);
        if (ct.IsCancellationRequested || tex is null)
        {
            return;
        }
        _texture = tex;
        _diskProbed = true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
