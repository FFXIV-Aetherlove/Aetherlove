namespace AetherOS.Apps.Love;

/// <summary>Host bridge into the plugin for the native-chat "Pulse" activity ping, which the dating app cannot
/// own itself. Declared here, implemented plugin-side. Selfie capture now comes from the shared app
/// capabilities.</summary>
public interface ILoveHost
{
    /// <summary>Stamps user activity on the plugin-owned Pulse service (schedules the next novelty line).</summary>
    void MarkActivity();

    /// <summary>Sends the phone to the encryption recovery screen, the one place that can mint a profile's
    /// keys with the passphrase. The app reaches for it when a profile has no working E2E (after a create
    /// that could not provision silently, or a chat opened on a keyless profile) instead of telling the
    /// user to contact support for a state that screen exists to heal.</summary>
    void OpenEncryptionRecovery();
}
