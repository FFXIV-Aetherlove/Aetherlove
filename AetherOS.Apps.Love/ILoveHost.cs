namespace AetherOS.Apps.Love;

/// <summary>Host bridge into the plugin for the native-chat "Pulse" activity ping, which the dating app cannot
/// own itself. Declared here, implemented plugin-side. Selfie capture now comes from the shared app
/// capabilities.</summary>
public interface ILoveHost
{
    /// <summary>Stamps user activity on the plugin-owned Pulse service (schedules the next novelty line).</summary>
    void MarkActivity();
}
