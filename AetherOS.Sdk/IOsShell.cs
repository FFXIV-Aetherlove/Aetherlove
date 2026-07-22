using System;
using System.Collections.Generic;

namespace AetherOS.Sdk;

/// <summary>OS services exposed to apps: navigation, notifications, and badges.</summary>
public interface IOsShell
{
    IReadOnlyList<IAetherApp> Apps { get; }

    void OpenApp(string appId);
    void GoHome();

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
