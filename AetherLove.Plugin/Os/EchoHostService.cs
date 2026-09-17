using AetherLove.Config;
using AetherLove.Services.Assets;
using AetherLove.Services.Echo;
using AetherLove.Shared.Assets;
using AetherLove.Windows;
using AetherOS.Apps.EchoVidya;

namespace AetherLove.Os;

/// <summary>The parts of Echo the app cannot reach: the popout window and the state of the playback host,
/// which the asset sync downloads as the <c>echo-host</c> bundle and <see cref="EchoHostInstaller"/> unpacks.</summary>
public sealed class EchoHostService : IEchoHost
{
    private readonly EchoWindow _window;
    private readonly EchoHostInstaller _installer;
    private readonly EchoHostLocator _locator;

    public EchoHostService(EchoWindow window, EchoHostInstaller installer, EchoHostLocator locator,
        Configuration config, AssetSyncService assets)
    {
        _window = window;
        _installer = installer;
        _locator = locator;

        _locator.OverrideExePath = string.IsNullOrWhiteSpace(config.Echo.HostPathOverride)
            ? null
            : config.Echo.HostPathOverride;

        _window.InstallStateProvider = () => _installer.State;
        _window.PlayerProgress = () => assets.Progress(AssetPacks.EchoHost);
    }

    public bool RuntimeReady => _locator.HostExePath is not null;

    public bool PlayerFailed => _installer.State.Phase == EchoInstallPhase.Failed;

    public bool WindowOpen => _window.IsOpen;

    public void OpenSolo(string videoRef) => _window.OpenSolo(videoRef);

    public void OpenRoom() => _window.OpenRoom();

    public void CloseWindow() => _window.IsOpen = false;
}
