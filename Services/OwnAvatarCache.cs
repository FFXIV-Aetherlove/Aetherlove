using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using Dalamud.Interface.Textures;

namespace AetherLove.Services;

/// <summary>
/// Keeps the user's own avatar warm for the match overlay and the nav bar. <see cref="Texture"/> serves
/// the last disk-cached copy instantly; <see cref="Refresh"/> re-fetches in the background and swaps the
/// texture when done, so a stale avatar shows briefly instead of a grey placeholder.
/// </summary>
public sealed class OwnAvatarCache : IDisposable
{
    private readonly AetherLoveHubClient _hub;

    private ISharedImmediateTexture? _texture;
    private bool _diskProbed;
    private CancellationTokenSource _cts = new();

    public OwnAvatarCache(AetherLoveHubClient hub)
    {
        _hub = hub;
    }

    private static string CacheDir => Path.Combine(
        Plugin.PluginInterface.ConfigDirectory.FullName, "MatchOverlayCache");

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

    /// <summary>Re-fetches the avatar in the background; the current texture stays visible meanwhile.</summary>
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
                Plugin.Log.Warning(ex, "[OwnAvatarCache] Refresh failed.");
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
            var newest = Directory.EnumerateFiles(CacheDir, "self_*.webp")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (newest is not null)
            {
                _texture = Plugin.TextureProvider.GetFromFile(newest);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[OwnAvatarCache] Could not load the cached avatar.");
        }
    }

    private void Store(byte[] bytes, CancellationToken ct)
    {
        var tex = AvatarDiskCache.Store(CacheDir, "self", bytes);
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
