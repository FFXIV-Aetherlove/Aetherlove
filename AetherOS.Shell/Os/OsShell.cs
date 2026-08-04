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
        ["levemetes", "market", "realtor", "wayfinder", "yapper", "snake", "stacker", "breaker",
         "meteor", "invaders", "muncher"];

    public bool IsNewApp(string appId) =>
        Array.IndexOf(NewAppIds, appId) >= 0 && !UiHost.Configuration.Os.SeenNewApps.Contains(appId);

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
