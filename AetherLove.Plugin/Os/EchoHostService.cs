using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Services.Echo;
using AetherLove.Services.Hub;
using AetherLove.Windows;
using AetherOS.Apps.EchoVidya;

namespace AetherLove.Os;

/// <summary>The parts of Echo the app cannot reach: the popout window and the browser-runtime install.
///
/// The install runs here rather than in the app so closing the phone mid-download does not abandon it; the
/// setup screen only renders whatever state this reports.</summary>
public sealed class EchoHostService : IEchoHost, IDisposable
{
    private readonly EchoWindow _window;
    private readonly EchoHostInstaller _installer;
    private readonly EchoHostLocator _locator;
    private readonly AetherHubContext _hub;
    private readonly Configuration _config;

    private CancellationTokenSource? _install;

    public EchoHostService(EchoWindow window, EchoHostInstaller installer, EchoHostLocator locator,
        AetherHubContext hub, Configuration config)
    {
        _window = window;
        _installer = installer;
        _locator = locator;
        _hub = hub;
        _config = config;

        _locator.OverrideExePath = string.IsNullOrWhiteSpace(config.Echo.HostPathOverride)
            ? null
            : config.Echo.HostPathOverride;

        _window.InstallStateProvider = () => _installer.State;
        _window.InstallRequested = BeginInstall;
        _window.InstallCancelRequested = CancelInstall;
    }

    public bool RuntimeReady => _locator.HostExePath is not null;

    public EchoInstallState InstallState => _installer.State;

    public bool WindowOpen => _window.IsOpen;

    public void BeginInstall()
    {
        if (_installer.State.Busy)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _install, cts)?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                var manifest = await _hub.GetEchoHostManifestAsync(cts.Token).ConfigureAwait(false);
                if (manifest is null)
                {
                    Plugin.Log.Warning("[Echo] No runtime manifest is published; nothing to install.");
                    return;
                }
                if (await _installer.InstallAsync(manifest, cts.Token).ConfigureAwait(false))
                {
                    _config.Echo.InstalledHostVersion = manifest.Version;
                    _config.Save();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "[Echo] Runtime install failed.");
            }
        });
    }

    public void CancelInstall()
    {
        try
        {
            Interlocked.Exchange(ref _install, null)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void OpenSolo(string videoRef) => _window.OpenSolo(videoRef);

    public void OpenRoom() => _window.OpenRoom();

    public void CloseWindow() => _window.IsOpen = false;

    public void Dispose()
    {
        CancelInstall();
        Interlocked.Exchange(ref _install, null)?.Dispose();
    }
}
