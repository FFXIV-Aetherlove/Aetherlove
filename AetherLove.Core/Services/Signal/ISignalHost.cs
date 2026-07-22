namespace AetherLove.Services.Signal;

/// <summary>Plugin-side surfaces the SignalR push handlers reach: the phone window's open state and the dating
/// screens that react to warnings / moderator messages / (re)connect. Implemented in the plugin so the connection
/// service can live in AetherLove.Core without referencing plugin screens.</summary>
public interface ISignalHost
{
    /// <summary>True while the phone window is open; gates deferred acknowledge screens and notification fallbacks.</summary>
    bool IsPhoneOpen { get; }

    void RequestWarningLiveAcknowledge();

    void RequestModeratorLiveAcknowledge();
}
