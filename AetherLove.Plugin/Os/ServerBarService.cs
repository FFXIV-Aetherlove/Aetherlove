using System;
using System.Collections.Generic;
using System.Linq;
using AetherLove.Config;
using AetherLove.Services;
using AetherLove.Windows;
using AetherOS.Sdk;
using Dalamud.Plugin.Services;

namespace AetherLove.Os;

/// <summary>One registered server-bar entry, for the OS Settings list.</summary>
public sealed record ServerBarEntryInfo(string AppId, string EntryId, string LabelKey);

/// <summary>The one owner of everything AetherLove puts on FFXIV's server info bar (ADR 21). Apps and
/// plugin services register entries and push text; this service owns every gate: the master switch,
/// the phone being powered on, the app still being on the home screen, and the per-app and per-entry
/// toggles stored in <see cref="OsConfig.ServerBarDisabled"/>. An entry that fails any gate is
/// REMOVED from the bar, never hidden: a registered entry reserves a slot, and third-party bars draw
/// reserved slots as blank squares.
///
/// <para>The window arrives late through <see cref="Initialize"/>, because apps receive their
/// capabilities in their constructors and the window depends on the apps; a constructor dependency
/// here would be the OsShell DI cycle all over again.</para></summary>
public sealed class ServerBarService(Configuration config)
{
    private const double PollSeconds = 0.5;

    private sealed class Registration(ServerBarService owner, string appId, string entryId)
    {
        public string Title = string.Empty;
        public string LabelKey = string.Empty;
        public Action? OnOpen;
        public string? Text;
        public DtrTextEntry? Entry;
        public readonly Handle Api = new(owner, appId, entryId);
    }

    private sealed class Handle(ServerBarService owner, string appId, string entryId) : IServerBarEntry
    {
        public void Set(string? text) => owner.SetText(appId, entryId, text);

        public bool Enabled
        {
            get => owner.IsEntryEnabled(appId, entryId);
            set => owner.SetEntryEnabled(appId, entryId, value);
        }
    }

    private sealed class AppHandle(ServerBarService owner, string appId) : IServerBar
    {
        public bool AppEnabled
        {
            get => owner.IsAppEnabled(appId);
            set => owner.SetAppEnabled(appId, value);
        }

        public IServerBarEntry Entry(string entryId, string title, string labelKey, Action? onOpen = null) =>
            owner.Register(appId, entryId, title, labelKey, onOpen);
    }

    private readonly object _lock = new();
    private readonly Dictionary<(string AppId, string EntryId), Registration> _entries = [];
    private readonly Dictionary<string, AppHandle> _apps = new(StringComparer.Ordinal);
    private MainPluginWindow? _window;
    private double _accum;

    public void Initialize(MainPluginWindow window)
    {
        if (_window is not null)
        {
            return;
        }
        _window = window;
        Plugin.Framework.Update += OnUpdate;
    }

    public void Shutdown()
    {
        Plugin.Framework.Update -= OnUpdate;
        lock (_lock)
        {
            foreach (var registration in _entries.Values)
            {
                registration.Entry?.Remove();
                registration.Entry = null;
            }
        }
        _window = null;
    }

    /// <summary>The app-scoped view <see cref="IAppCapabilities.ServerBar"/> hands out.</summary>
    public IServerBar For(string appId)
    {
        lock (_lock)
        {
            if (!_apps.TryGetValue(appId, out var handle))
            {
                handle = new AppHandle(this, appId);
                _apps[appId] = handle;
            }
            return handle;
        }
    }

    /// <summary>Every registered entry, for the OS Settings list, in registration order grouped by app.</summary>
    public IReadOnlyList<ServerBarEntryInfo> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries
                    .OrderBy(e => e.Key.AppId, StringComparer.Ordinal)
                    .Select(e => new ServerBarEntryInfo(e.Key.AppId, e.Key.EntryId, e.Value.LabelKey))];
            }
        }
    }

    /// <summary>Carries an app's pre-capability "show on the bar" switch into the per-app toggle,
    /// exactly once: after that the central store rules, and the legacy setting means nothing.</summary>
    public void SeedLegacyToggle(string appId, bool legacyOn)
    {
        var os = config.Os;
        if (os.ServerBarSeeded.Contains(appId))
        {
            return;
        }
        os.ServerBarSeeded.Add(appId);
        if (!legacyOn && !os.ServerBarDisabled.Contains(appId))
        {
            os.ServerBarDisabled.Add(appId);
        }
        config.Save();
    }

    public bool IsAppEnabled(string appId) => !config.Os.ServerBarDisabled.Contains(appId);

    public bool IsEntryEnabled(string appId, string entryId) =>
        !config.Os.ServerBarDisabled.Contains($"{appId}/{entryId}");

    public void SetAppEnabled(string appId, bool on) => SetKey(appId, on);

    public void SetEntryEnabled(string appId, string entryId, bool on) => SetKey($"{appId}/{entryId}", on);

    private void SetKey(string key, bool on)
    {
        var disabled = config.Os.ServerBarDisabled;
        var changed = on ? disabled.Remove(key) : !disabled.Contains(key) && Add(disabled, key);
        if (changed)
        {
            config.Save();
        }
        static bool Add(List<string> list, string key)
        {
            list.Add(key);
            return true;
        }
    }

    private IServerBarEntry Register(string appId, string entryId, string title, string labelKey, Action? onOpen)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue((appId, entryId), out var registration))
            {
                registration = new Registration(this, appId, entryId);
                _entries[(appId, entryId)] = registration;
            }
            if (title.Length > 0)
            {
                registration.Title = title;
            }
            if (labelKey.Length > 0)
            {
                registration.LabelKey = labelKey;
            }
            if (onOpen is not null)
            {
                registration.OnOpen = onOpen;
            }
            return registration.Api;
        }
    }

    private void SetText(string appId, string entryId, string? text)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue((appId, entryId), out var registration))
            {
                registration.Text = text is { Length: > 0 } ? text : null;
            }
        }
        Apply();
    }

    private void OnUpdate(IFramework framework)
    {
        _accum += framework.UpdateDelta.TotalSeconds;
        if (_accum < PollSeconds)
        {
            return;
        }
        _accum = 0;
        Apply();
    }

    /// <summary>Reconciles every entry against the gates. Runs on each publish and on a slow tick, so a
    /// toggle, a power-off or an app removal takes entries down without any publisher noticing.</summary>
    private void Apply()
    {
        if (_window is null)
        {
            return;
        }
        var master = config.EnableDtrEntry && _window.IsPoweredOn;
        lock (_lock)
        {
            foreach (var ((appId, entryId), registration) in _entries)
            {
                var shown = master
                    && registration.Text is not null
                    && !config.Os.RemovedApps.Contains(appId)
                    && IsAppEnabled(appId)
                    && IsEntryEnabled(appId, entryId);
                if (!shown)
                {
                    registration.Entry?.Remove();
                    continue;
                }
                registration.Entry ??= new DtrTextEntry(
                    Plugin.DtrBar, registration.Title, () => registration.OnOpen?.Invoke());
                registration.Entry.Set(registration.Text);
            }
        }
    }
}
