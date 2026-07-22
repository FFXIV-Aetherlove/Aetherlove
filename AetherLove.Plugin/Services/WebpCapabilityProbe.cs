using System;
using System.IO;
using System.Threading.Tasks;
using AetherLove.Config;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;

namespace AetherLove.Services;

/// <summary>Probes once per launch whether this machine's decoder can decode WebP, persisting the result to
/// <see cref="Configuration.WebpSupported"/>. <see cref="Tick"/> must run on the draw thread; texture wraps only resolve there.</summary>
public sealed class WebpCapabilityProbe
{
    private const double TimeoutSeconds = 3.0;

    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ISharedImmediateTexture? _tex;
    private bool _started;
    private double _elapsed;

    public WebpCapabilityProbe(Configuration config, IPluginLog log)
    {
        _config = config;
        _log = log;
    }

    /// <summary>Call every frame from the draw thread; resolves once the texture loads or the timeout elapses.</summary>
    public void Tick(float deltaSeconds)
    {
        if (_tcs.Task.IsCompleted)
        {
            return;
        }

        if (!_started)
        {
            _started = true;
            try
            {
                var bytes = AetherLove.Shared.PhotoTransform.CreateProbeWebp();
                var dir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "WebpProbe");
                _tex = AvatarDiskCache.Store(dir, "probe", bytes);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[WebpProbe] Failed to start the WebP capability probe.");
                Resolve(false);
                return;
            }
            if (_tex is null)
            {
                Resolve(false);
                return;
            }
        }

        _elapsed += deltaSeconds;
        if (_tex?.GetWrapOrDefault() is { Width: > 0 })
        {
            Resolve(true);
        }
        else if (_elapsed >= TimeoutSeconds)
        {
            Resolve(false);
        }
    }

    private void Resolve(bool supported)
    {
        if (!_tcs.TrySetResult(supported))
        {
            return;
        }
        if (_config.WebpSupported != supported)
        {
            _config.WebpSupported = supported;
            _config.Save();
        }
        _log.Information($"[WebpProbe] WebP decode is {(supported ? "supported" : "UNSUPPORTED")} on this client; photos served as {(supported ? "WebP" : "JPEG")} from the next connection.");
    }
}
