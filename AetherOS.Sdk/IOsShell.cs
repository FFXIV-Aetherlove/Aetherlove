using System;
using System.Collections.Generic;

namespace AetherOS.Sdk;

/// <summary>OS services exposed to apps: navigation, notifications, and badges.</summary>
public interface IOsShell
{
    IReadOnlyList<IAetherApp> Apps { get; }

    /// <summary>The built-in Arcade folder's id, for games navigating back into it.</summary>
    const string ArcadeFolderId = "folder:arcade";

    void OpenApp(string appId);
    void GoHome();

    /// <summary>Whether the user removed this app from the home screen. It is still registered and still
    /// reachable through a deep link, but it must not appear in any list the user browses. Hosts without a
    /// removal concept report false.</summary>
    bool IsAppRemoved(string appId) => false;

    /// <summary>Returns to the home screen with the given folder overlay open; hosts without folders
    /// fall back to a plain home.</summary>
    void GoHomeToFolder(string folderId) => GoHome();

    void PostNotification(string appId, string title, string body, Action? onTap = null, string? tag = null);
    IReadOnlyList<OsNotification> Notifications { get; }
    void DismissNotification(Guid id);

    /// <summary>Removes every notification carrying <paramref name="tag"/>; call it when the underlying
    /// content is read so the center, widget, and any tag-scoped badge clear together.</summary>
    void DismissByTag(string tag);
    void ClearNotifications();

    /// <summary>OS-side badge counts, added on top of each app's own <see cref="IAetherApp.Badge"/>.</summary>
    void AddBadge(string appId, int delta);
    void ClearBadge(string appId);
    int OsBadge(string appId);

    /// <summary>Delivers the intent to the target app and opens it.</summary>
    void SendIntent(string targetAppId, OsIntent intent);

    /// <summary>Replays the guided OS tour from the home screen. Default no-op for hosts without one.</summary>
    void StartTour()
    {
    }
}
