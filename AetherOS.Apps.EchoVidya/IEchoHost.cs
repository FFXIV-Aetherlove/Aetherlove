namespace AetherOS.Apps.EchoVidya;

/// <summary>The parts of Echo only the plugin can do: own the popout window and know whether the playback
/// host is on disk. The host itself arrives through the phone's asset sync, never through the app.</summary>
public interface IEchoHost
{
    bool RuntimeReady { get; }

    /// <summary>The playback host bundle arrived but did not unpack. The phone tries again on its own.</summary>
    bool PlayerFailed { get; }

    bool WindowOpen { get; }

    void OpenSolo(string videoRef);

    void OpenRoom();

    void CloseWindow();
}
