namespace AetherLove;

/// <summary>Whether the phone is switched on. Powering it off is meant to stop the plugin doing anything at
/// all (no ads, no notifications, no polling, no connection), and the services that tick on their own timers
/// have no reason to know about windows, so they ask here instead.</summary>
public static class PhonePower
{
    /// <summary>True until the player powers the phone off; back to true when they turn it on again.</summary>
    public static bool IsOn { get; private set; } = true;

    public static void Set(bool on) => IsOn = on;
}
