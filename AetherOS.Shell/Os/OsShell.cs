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
    private string[] _externalLive = [];
    private string[] _externalDormant = [];
    private long _externalCheckedAt;

    private const int MaxNotifications = 40;
    private const int ExternalPollMs = 1000;

    /// <summary>Apps whose home tile carries the red "new" badge until first opened. Edit per release when a
    /// new app ships; stale ids in users' <see cref="OsConfig.SeenNewApps"/> drop out harmlessly.</summary>
    internal static readonly string[] NewAppIds =
        ["levemetes", "market", "realtor", "wayfinder", "yapper", "wallet", "snake", "stacker", "breaker",
         "meteor", "invaders", "muncher", "plappy", "doom", "sudoku", "groove", "echo", "store", "aetherling",
         "notes", "calculator", "timers", "racooner", "skyswarm", "eordle", "together", "racer"];

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
            .GroupBy(p => p.InternalName)
            .ToDictionary(g => g.Key, g => g.First());
        _externalLive = wanted
            .Where(name => installed.TryGetValue(name, out var p) && p.IsLoaded)
            .ToArray();
        _externalDormant = wanted
            .Where(name => installed.TryGetValue(name, out var p) && !p.IsLoaded)
            .Select(name => ExternalApp.IdPrefix + name)
            .ToArray();
        _externalCheckedAt = Environment.TickCount64;
        _external = _externalLive
            .Select(name => (IAetherApp)new ExternalApp(name, installed[name].Name))
            .ToArray();
        _all = BuiltInApps().Concat(_external).ToArray();
    }

    /// <summary>Rebuilds the wrappers when the set of loaded plugins behind them has changed since the last
    /// look, so a plugin that finishes loading after AetherOS did (a patch-day update, a manual enable) gets
    /// its tile back in the same session instead of waiting for the next one. Polled from the draw thread
    /// rather than driven by Dalamud's plugin-list event, which raises on its own thread while the draw
    /// thread is walking these arrays.</summary>
    public void SyncExternalApps()
    {
        var now = Environment.TickCount64;
        if (now - _externalCheckedAt < ExternalPollMs)
        {
            return;
        }
        _externalCheckedAt = now;
        var wanted = UiHost.Configuration.Os.ExternalApps;
        if (wanted.Count == 0 && _externalLive.Length == 0)
        {
            return;
        }
        var loaded = new HashSet<string>(
            UiHost.PluginInterface.InstalledPlugins.Where(p => p.IsLoaded).Select(p => p.InternalName),
            StringComparer.Ordinal);
        if (!wanted.Where(loaded.Contains).SequenceEqual(_externalLive, StringComparer.Ordinal))
        {
            RefreshExternalApps();
        }
    }

    /// <summary>Home-screen ids for external apps whose plugin is still installed but is not loaded right now.
    /// They hold their cell rather than reading as an empty one: a plugin waiting to be updated after a game
    /// patch is momentarily unavailable, not gone, and a cell that reads as free is a cell the next new app
    /// lands on. A plugin that is genuinely uninstalled is not listed and does lose its cell.</summary>
    public IReadOnlyList<string> DormantExternalIds() => _externalDormant;

    /// <summary>Whether this plugin is currently wearing a tile, for the add-apps sheet: an entry left in config
    /// by a plugin that was unloaded when AetherOS last looked is not "added" from the player's side, and marking
    /// it so leaves a row that neither adds nor removes.</summary>
    public bool HasExternalApp(string internalName) =>
        _externalLive.Contains(internalName, StringComparer.Ordinal);

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
