namespace AetherOS.Apps.Realtor;

/// <summary>The host-side phase watcher, which announces lottery phase changes while the app is closed.</summary>
public interface IRealtorAlerts
{
    /// <summary>Drops any outstanding phase announcement, called when the player opens the app.</summary>
    void ClearNotifications();
}
