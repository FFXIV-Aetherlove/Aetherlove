namespace AetherLove.Services.Signal;

/// <summary>Plugin-side surfaces the SignalR push handlers reach: the phone window's open state and the dating
/// screens that react to warnings / moderator messages / (re)connect. Implemented in the plugin so the connection
/// service can live in AetherLove.Core without referencing plugin screens.</summary>
public interface ISignalHost
{
    /// <summary>True while the phone window is open; gates deferred acknowledge screens and notification fallbacks.</summary>
    bool IsPhoneOpen { get; }

    /// <summary>True only while the given app is the surface the phone is actually showing. Notification
    /// suppression gates on this rather than on an app's own "which chat is open" stamp alone, because that
    /// stamp deliberately survives a trip to the home screen (warm resume) and a message arriving there must
    /// not be swallowed as if the chat were on screen.</summary>
    bool IsAppInForeground(string appId);

    void RequestWarningLiveAcknowledge();

    void RequestModeratorLiveAcknowledge();

    /// <summary>Arms the OS staff-notice gate for a live mid-session account-level notice, so acknowledging it
    /// returns the user to where they were instead of re-running the startup ladder.</summary>
    void RequestStaffNoticeLiveAcknowledge();

    /// <summary>Re-reads the account snapshot into the staff-notice gate while it is already showing. Navigating
    /// to the screen it is already on never re-runs its OnShow, so a notice arriving mid-gate needs this to join
    /// the displayed batch.</summary>
    void RefreshStaffNoticeGate();
}
