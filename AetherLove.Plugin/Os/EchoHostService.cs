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

    /// <summary>The published host version when it differs from the installed one, else null.</summary>
    private volatile string? _updateVersion;

    private int _checking;

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
        _window.UpdateAvailable = () => UpdatePending;
        _window.InstallRequested = BeginInstall;
        _window.InstallCancelRequested = CancelInstall;
    }

    public bool RuntimeReady => _locator.HostExePath is not null;

    public EchoInstallState InstallState => _installer.State;

    public bool WindowOpen => _window.IsOpen;

    /// <summary>Whether a newer playback host is published and not yet installed. While true the app
    /// blocks on the update gate: nobody gets to find out mid-video that their player is outdated.</summary>
    public bool UpdatePending => _updateVersion is not null;

    /// <summary>Compares the published playback host against the installed one and starts fetching it the
    /// moment they differ. Without this a new bundle reaches nobody: the version stamped at install time
    /// was never read back, so only players who re-ran the tour by hand ever moved off their first build.
    /// The install is version-keyed and lands in its own folder, so it runs safely beside a running build,
    /// and a player that was open through the install is restarted onto the new one when it lands.</summary>
    public void CheckForUpdate()
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var manifest = await _hub.GetEchoHostManifestAsync().ConfigureAwait(false);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
                {
                    return;
                }
                if (_locator.IsComplete(manifest.Version))
                {
                    // An old build whose prune was blocked while it was running gets removed on a later
                    // check; only ever the published version's siblings, so a dev override is untouched.
                    _locator.PruneOtherVersions(manifest.Version);
                    _updateVersion = null;
                    return;
                }
                _updateVersion = manifest.Version;
                Plugin.Log.Information(
                    $"[Echo] A newer playback host is published: {manifest.Version} " +
                    $"(installed {_locator.InstalledVersion ?? "none"}).");
                if (!_installer.State.Busy)
                {
                    BeginInstall();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[Echo] Could not check for a newer playback host.");
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
            }
        });
    }

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
                    _updateVersion = null;
                    // A player process that was already running is still the OLD build: nothing about an
                    // install swaps the code a live process runs. Restart it onto the new one.
                    _window.RestartHostAfterUpdate();
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
