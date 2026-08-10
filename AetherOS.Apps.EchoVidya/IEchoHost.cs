namespace AetherOS.Apps.EchoVidya;

/// <summary>The parts of Echo only the plugin can do: own the popout window and the runtime install.</summary>
public interface IEchoHost
{
    bool RuntimeReady { get; }

    AetherLove.Services.Echo.EchoInstallState InstallState { get; }

    void BeginInstall();

    void CancelInstall();

    bool WindowOpen { get; }

    void OpenSolo(string videoRef);

    void OpenRoom();

    void CloseWindow();
}
