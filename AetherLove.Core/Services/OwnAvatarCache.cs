using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using Dalamud.Interface.Textures;

namespace AetherLove.Services;

/// <summary>Keeps the user's own avatar warm; the last disk-cached copy shows instantly while <see cref="Refresh"/> swaps in a fresh one.</summary>
public sealed class OwnAvatarCache : IDisposable
{
    private readonly AetherHubContext _hub;
    private readonly Config.Configuration _config;

    private ISharedImmediateTexture? _texture;
    private bool _diskProbed;
    private CancellationTokenSource _cts = new();

    public OwnAvatarCache(AetherHubContext hub, Config.Configuration config)
    {
        _hub = hub;
        _config = config;
    }

    private static string CacheDir => ImageCacheCleaner.MatchOverlayCacheDir;

    /// <summary>Disk key scoped to the active profile so siblings never see each other's avatar; the bare
    /// legacy "self" key is only read as a first-boot fallback.</summary>
    private string SelfKey => _config.Auth.ActiveProfileId is { } pid ? $"self_{pid:N}" : "self";

    /// <summary>Drops the in-memory texture so the next read re-probes disk and a cold refresh re-fetches. Used
    /// when the disk cache was wiped underneath us (the clearcache flow) or on a profile switch; without it a
    /// stale texture pointing at a now-deleted file renders as the generic fallback and never reloads.</summary>
    public void Invalidate()
    {
        _cts.Cancel();
        _texture = null;
        _diskProbed = false;
    }

    /// <summary>Drops the in-memory texture after a profile switch so the next read probes the new profile's
    /// disk key.</summary>
    public void OnProfileSwitched()
    {
        Invalidate();
    }

    /// <summary>The last known avatar, or null before the first successful fetch on a fresh install.</summary>
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
                var dto = await _hub.GetMyProfileDetailAsync(ct).ConfigureAwait(false);
                var avatar = dto.Photos.FirstOrDefault(p => p.Order == 0)?.WebpBytes;
                if (ct.IsCancellationRequested || avatar is not { Length: > 0 })
                {
                    return;
                }
                Store(avatar, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[OwnAvatarCache] Refresh failed.");
            }
        }, ct);
    }

    private void ProbeDisk()
    {
        try
        {
            if (!Directory.Exists(CacheDir))
            {
                return;
            }
            var newest = Directory.EnumerateFiles(CacheDir, $"{SelfKey}_*")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            // Legacy fallback (bare "self" with one underscore) prevents stale avatars from showing to other profiles.
            newest ??= Directory.EnumerateFiles(CacheDir, "self_*")
                .Where(f => Path.GetFileNameWithoutExtension(f).Count(c => c == '_') == 1)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (newest is not null)
            {
                _texture = UiHost.TextureProvider.GetFromFile(newest);
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[OwnAvatarCache] Could not load the cached avatar.");
        }
    }

    private void Store(byte[] bytes, CancellationToken ct)
    {
        var tex = AvatarDiskCache.Store(CacheDir, SelfKey, bytes);
        if (ct.IsCancellationRequested || tex is null)
        {
            return;
        }
        _texture = tex;
        _diskProbed = true;
        DeleteLegacySelfFiles();
    }

    /// <summary>Once a profile-scoped copy exists, the pre-multi-profile "self_{hash}" files are retired so
    /// the fallback can never show a stale avatar to a different profile.</summary>
    private static void DeleteLegacySelfFiles()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(CacheDir, "self_*")
                         .Where(f => Path.GetFileNameWithoutExtension(f).Count(c => c == '_') == 1))
            {
                File.Delete(f);
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
