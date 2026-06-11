using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using Dalamud.Interface.Textures;

namespace AetherLove.Services;

/// <summary>
/// Keeps the user's own avatar warm for overlays. <see cref="Texture"/> serves the last
/// disk-cached copy instantly; <see cref="Refresh"/> re-fetches in the background and swaps the
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

    private static string CachePath => Path.Combine(
        Plugin.PluginInterface.ConfigDirectory.FullName, "MatchOverlayCache", "self.webp");

    /// <summary>The last known avatar, or null before the first successful fetch on a fresh install.</summary>
    public ISharedImmediateTexture? Texture
    {
        get
        {
            if (_texture is null && !_diskProbed)
            {
                _diskProbed = true;
                try
                {
                    if (File.Exists(CachePath))
                    {
                        _texture = Plugin.TextureProvider.GetFromFile(CachePath);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warning(ex, "[OwnAvatarCache] Could not load the cached avatar.");
                }
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

                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                File.WriteAllBytes(CachePath, avatar);
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                _texture = Plugin.TextureProvider.GetFromFile(CachePath);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[OwnAvatarCache] Refresh failed.");
            }
        }, ct);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
