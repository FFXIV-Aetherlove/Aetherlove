namespace AetherOS.Apps.EchoVidya;

/// <summary>The parts of Echo only the plugin can do: own the popout window and the runtime install.</summary>
public interface IEchoHost
{
    bool RuntimeReady { get; }

    AetherLove.Services.Echo.EchoInstallState InstallState { get; }

    void BeginInstall();

    /// <summary>Asks whether a newer playback host is published, and starts fetching it when one is.</summary>
    void CheckForUpdate();

    /// <summary>A newer playback host is published and not yet installed. While true the app blocks on the
    /// update gate: nobody gets to find out mid-video that their player is outdated.</summary>
    bool UpdatePending { get; }

    void CancelInstall();

    bool WindowOpen { get; }

    void OpenSolo(string videoRef);

    void OpenRoom();

    void CloseWindow();
}
