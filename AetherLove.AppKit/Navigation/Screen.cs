namespace AetherLove.Navigation;

/// <summary>Top-level shell/gate screens the phone window dispatches. The AetherLove dating surface is a single
/// <see cref="App"/> entry; its own views live inside the AetherOS.Apps.Love app, not here.</summary>
public enum Screen
{
    Splash,
    Home,
    App,
    OsOnboarding,
    Banned,
    WarningsAcknowledge,
    ModeratorMessages,
    PassphraseUnlock,
    EncryptionRecovery,
    Offline,
    Outdated,
    StaffNotice,
}
