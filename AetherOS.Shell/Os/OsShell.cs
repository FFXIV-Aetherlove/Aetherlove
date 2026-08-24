using System;
using System.Collections.Generic;
using System.Linq;
using AetherLove.Navigation;
using AetherOS.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace AetherLove.Os;

/// <summary>The OS runtime: app registry, notification center, and badges.</summary>
public sealed class OsShell : IOsShell
{
    private readonly ScreenRouter _router;
    private readonly IServiceProvider _services;
    private readonly object _lock = new();
    private readonly List<OsNotification> _notifications = new();
    private readonly Dictionary<string, int> _badges = new();
    private IAetherApp[]? _apps;
    private IAetherApp[] _external = [];
    private IAetherApp[] _all = [];

    private const int MaxNotifications = 40;

    /// <summary>Apps whose home tile carries the red "new" badge until first opened. Edit per release when a
    /// new app ships; stale ids in users' <see cref="OsConfig.SeenNewApps"/> drop out harmlessly.</summary>
    internal static readonly string[] NewAppIds =
        ["levemetes", "market", "realtor", "wayfinder", "yapper", "wallet", "snake", "stacker", "breaker",
         "meteor", "invaders", "muncher", "plappy", "doom", "sudoku", "groove", "echo", "store", "aetherling",
         "notes", "calculator", "timers", "racooner", "skyswarm", "eordle", "together"];

    public bool IsNewApp(string appId) =>
        Array.IndexOf(NewAppIds, appId) >= 0 && !UiHost.Configuration.Os.SeenNewApps.Contains(appId);

    /// <summary>Drops the "new" pill from these apps without opening them, for the home screen's mark-seen
    /// rows. Ids that were never new, or are already seen, cost nothing. True when anything changed.</summary>
    public bool MarkAppsSeen(IEnumerable<string> appIds)
    {
        var seen = UiHost.Configuration.Os.SeenNewApps;
        var changed = false;
        foreach (var appId in appIds)
        {
            if (Array.IndexOf(NewAppIds, appId) >= 0 && !seen.Contains(appId))
            {
                seen.Add(appId);
                changed = true;
            }
        }
        if (changed)
        {
            UiHost.Configuration.Save();
        }
        return changed;
    }

    /// <summary>Every app that still wears the pill, for "mark all as seen".</summary>
    public IEnumerable<string> NewApps() => NewAppIds.Where(IsNewApp);

    // Apps resolve lazily: hosts they depend on may themselves need this shell.
    public OsShell(IServiceProvider services, ScreenRouter router)
    {
        _services = services;
        _router = router;
    }

    /// <summary>Resolved lazily: the tour drives the shell (shade, home screen), so a ctor dependency here
    /// would be a cycle.</summary>
    public void StartTour()
    {
        GoHome();
        _services.GetRequiredService<OsTour>().Start();
    }

    private IAetherApp[] BuiltInApps()
    {
        if (_apps == null)
        {
            _apps = _services.GetServices<IAetherApp>().ToArray();
            foreach (var app in _apps)
            {
                RegisterAppStrings(app);
            }
            RefreshExternalApps();
        }
        return _apps;
    }

    /// <summary>Merges an app's owned localization pack (per ISO language code) into the OS string tables, so its
    /// keys resolve through the normal <c>Localize</c> path with language-then-English fallback.</summary>
    private static void RegisterAppStrings(IAetherApp app)
    {
        if (app.Strings is not { } packs)
        {
            return;
        }
        try
        {
            foreach (var (isoCode, strings) in packs)
            {
                Services.Localization.LanguageProvider.RegisterAppStrings(isoCode, strings);
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, $"[OsShell] Registering string packs for '{app.Id}' failed.");
        }
    }

    public IReadOnlyList<IAetherApp> Apps
    {
        get
        {
            BuiltInApps();
            return _all;
        }
    }

    /// <summary>Rebuilds the external-app wrappers from config; missing or unloaded plugins are skipped.</summary>
    public void RefreshExternalApps()
    {
        var wanted = UiHost.Configuration.Os.ExternalApps;
        var installed = UiHost.PluginInterface.InstalledPlugins
            .Where(p => p.IsLoaded)
            .GroupBy(p => p.InternalName)
            .ToDictionary(g => g.Key, g => g.First());
        _external = wanted
            .Where(installed.ContainsKey)
            .Select(name => (IAetherApp)new ExternalApp(name, installed[name].Name))
            .ToArray();
        _all = BuiltInApps().Concat(_external).ToArray();
    }

    public void AddExternalApp(string internalName)
    {
        var list = UiHost.Configuration.Os.ExternalApps;
        if (!list.Contains(internalName))
        {
            list.Add(internalName);
            UiHost.Configuration.Save();
        }
        RefreshExternalApps();
    }

    public void RemoveExternalApp(string appId)
    {
        if (!appId.StartsWith(ExternalApp.IdPrefix, StringComparison.Ordinal))
        {
            return;
        }
        var internalName = appId[ExternalApp.IdPrefix.Length..];
        UiHost.Configuration.Os.ExternalApps.Remove(internalName);
        HomeLayout.RemoveFromConfig(UiHost.Configuration.Os, appId);
        UiHost.Configuration.Os.DockIds.Remove(appId);
        UiHost.Configuration.Save();
        RefreshExternalApps();
    }

    public bool IsAppRemoved(string appId) => UiHost.Configuration.Os.RemovedApps.Contains(appId);

    /// <summary>Takes a built-in app off the home screen: it loses its cell, its folder membership, its badge and
    /// every notification it has posted, and it stays silent until it is added back from the add-apps sheet. The
    /// app itself keeps running, so a deep link, an intent, or a chat card still opens it.</summary>
    public void RemoveBuiltInApp(string appId)
    {
        if (appId.StartsWith(ExternalApp.IdPrefix, StringComparison.Ordinal) || Find(appId) == null)
        {
            return;
        }
        var os = UiHost.Configuration.Os;
        if (!os.RemovedApps.Contains(appId))
        {
            os.RemovedApps.Add(appId);
        }
        foreach (var folder in os.Folders)
        {
            folder.AppIds.Remove(appId);
        }
        HomeLayout.RemoveFromConfig(os, appId);
        os.DockIds.Remove(appId);
        UiHost.Configuration.Save();

        ClearBadge(appId);
        lock (_lock)
        {
            _notifications.RemoveAll(n => n.AppId == appId);
        }
    }

    /// <summary>Puts a removed built-in app back on the grid, in the first free cell. Nothing adopts it into
    /// a folder afterwards; the player puts it where they want it.</summary>
    public void RestoreBuiltInApp(string appId)
    {
        var os = UiHost.Configuration.Os;
        if (!os.RemovedApps.Remove(appId))
        {
            return;
        }
        HomeLayout.PlaceInConfig(os, appId);
        UiHost.Configuration.Save();
    }

    public IAetherApp? ActiveSurfaceApp { get; private set; }

    /// <summary>Whether the AetherLove session is connected; set by the host window each frame, read by the home widget.</summary>
    public bool Connected { get; set; }

    public IAetherApp? Find(string appId)
    {
        // A deep link (mini tap, notification, DTR) can be the first shell access of the session; resolve
        // the registry or the intent silently no-ops.
        BuiltInApps();
        return _all.FirstOrDefault(a => a.Id == appId);
    }

    public void SendIntent(string targetAppId, OsIntent intent)
    {
        var app = Find(targetAppId);
        if (app == null)
        {
            return;
        }
        app.OnIntent(intent);
        OpenApp(targetAppId);
    }

    public void DeliverIntent(string targetAppId, OsIntent intent)
    {
        Find(targetAppId)?.OnIntent(intent);
    }

    /// <summary>The party join every invite surface routes through: joins by code and lands the user on the
    /// widget page, where the party card lives.</summary>
    public void JoinParty(string code)
    {
        if (code.Length == 0)
        {
            return;
        }
        // Resolved lazily for the same reason the tour is: the party bridge and the home screen both sit
        // above this shell, and a ctor dependency on either is a cycle.
        _services.GetRequiredService<IOsTogether>().Join(code);
        GoHome();
        _services.GetRequiredService<AetherLove.Screens.HomeScreen>().ShowPage(-1);
    }

    public void OpenApp(string appId)
    {
        var app = Find(appId);
        if (app == null)
        {
            return;
        }

        ClearBadge(appId);
        if (Array.IndexOf(NewAppIds, appId) >= 0 && !UiHost.Configuration.Os.SeenNewApps.Contains(appId))
        {
            UiHost.Configuration.Os.SeenNewApps.Add(appId);
            UiHost.Configuration.Save();
        }

        if (app.HasSurface)
        {
            ActiveSurfaceApp = app;
            _router.Navigate(Screen.App);
        }
        else
        {
            app.Open();
        }
    }

    public void GoHome() => _router.Navigate(Screen.Home);

    /// <summary>Resolved lazily like the tour: the home screen depends on this shell.</summary>
    public void GoHomeToFolder(string folderId)
    {
        _services.GetRequiredService<Screens.HomeScreen>().OpenFolder(folderId);
        GoHome();
    }

    public void PostNotification(string appId, string title, string body, Action? onTap = null, string? tag = null)
    {
        if (IsAppRemoved(appId))
        {
            return;
        }
        lock (_lock)
        {
            if (tag != null)
            {
                _notifications.RemoveAll(n => n.Tag == tag);
            }
            _notifications.Insert(0, new OsNotification
            {
                AppId = appId,
                Title = title,
                Body = body,
                OnTap = onTap,
                Tag = tag,
            });
            if (_notifications.Count > MaxNotifications)
            {
                _notifications.RemoveAt(_notifications.Count - 1);
            }
        }
    }

    public void DismissByTag(string tag)
    {
        lock (_lock)
        {
            _notifications.RemoveAll(n => n.Tag == tag);
        }
    }

    public IReadOnlyList<OsNotification> Notifications
    {
        get
        {
            lock (_lock)
            {
                return _notifications.ToArray();
            }
        }
    }

    public void DismissNotification(Guid id)
    {
        lock (_lock)
        {
            _notifications.RemoveAll(n => n.Id == id);
        }
    }

    public void ClearNotifications()
    {
        lock (_lock)
        {
            _notifications.Clear();
        }
    }

    public void AddBadge(string appId, int delta)
    {
        if (IsAppRemoved(appId))
        {
            return;
        }
        lock (_lock)
        {
            _badges[appId] = Math.Max(0, _badges.GetValueOrDefault(appId) + delta);
        }
    }

    public void ClearBadge(string appId)
    {
        lock (_lock)
        {
            _badges.Remove(appId);
        }
    }

    public int OsBadge(string appId)
    {
        lock (_lock)
        {
            return _badges.GetValueOrDefault(appId);
        }
    }

    public int BadgeFor(IAetherApp app) => app.Badge + OsBadge(app.Id);

    /// <summary>Which app a given screen belongs to, for the close animation.</summary>
    public string? AppIdForScreen(Screen screen) => screen switch
    {
        Screen.App => ActiveSurfaceApp?.Id,
        _ => null,
    };
}
