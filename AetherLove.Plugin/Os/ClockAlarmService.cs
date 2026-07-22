using System;
using System.Collections.Generic;
using System.Linq;
using AetherOS.Apps.Clock;
using AetherOS.Sdk;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using Microsoft.Extensions.DependencyInjection;
using AetherLove.Services.Localization;
using AetherLove.Windows;

namespace AetherLove.Os;

/// <summary>The Clock app's timer/alarm engine. Ticks on the game framework so timers and alarms fire even while
/// the app is closed: it owns the persisted list and alarm settings, transitions a due entry to ringing, prints
/// the native chat line (with a clickable link back to the Timers screen) + an OS notification, loops the chosen
/// chat sound effect (re-played on an interval, since the game has no looping SE), and auto-stops after the
/// configured window.</summary>
public sealed class ClockAlarmService : IClockAlarms, IDisposable
{
    private static readonly TimeSpan ElapseGate = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SeReplay = TimeSpan.FromSeconds(2.5);
    private const uint ClockLinkCommandId = 8;
    private const ushort LinkColor = 539;

    private readonly IAppStorage _store;
    private readonly IOsShell _shell;
    private readonly IServiceProvider _services;
    private readonly object _lock = new();
    private readonly List<TimerEntry> _timers = new();
    private readonly DalamudLinkPayload? _chatLink;

    private int _alarmSoundId = 1;
    private bool _notifyInChat = true;
    private int _autoOffMinutes;

    private bool _started;
    private volatile bool _pendingReconcile;
    private DateTime _lastElapseCheckUtc = DateTime.MinValue;
    private DateTime _nextSePlayUtc = DateTime.MinValue;

    public ClockAlarmService(AppStorageService storage, IOsShell shell, IServiceProvider services)
    {
        _store = storage.For("clock");
        _shell = shell;
        _services = services;
        try
        {
            _chatLink = Plugin.ChatGui.AddChatLinkHandler(ClockLinkCommandId, (_, _) => OnChatLink());
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[ClockAlarm] Failed to register chat link handler.");
        }
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }
        Load();
        // Reconcile (which can print chat / notify) must run on the framework thread; Start runs off it, so defer.
        _pendingReconcile = true;
        Plugin.Framework.Update += OnFrameworkUpdate;
        _started = true;
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }
        Plugin.Framework.Update -= OnFrameworkUpdate;
        _started = false;
    }

    public void Dispose()
    {
        Stop();
        try
        {
            Plugin.ChatGui.RemoveChatLinkHandler(ClockLinkCommandId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[ClockAlarm] RemoveChatLinkHandler failed.");
        }
    }

    public IReadOnlyList<ClockTimerView> Timers
    {
        get
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                return _timers
                    .OrderByDescending(t => t.Ringing)
                    .ThenBy(t => t.DueUtc)
                    .Select(t => new ClockTimerView(
                        t.Id, t.Label, t.Duration, t.DueUtc,
                        t.Ringing ? ClockTimerState.Ringing : ClockTimerState.Pending,
                        t.Ringing ? TimeSpan.Zero : Floor(t.DueUtc - now),
                        t.IsAlarm))
                    .ToList();
            }
        }
    }

    public int RingingCount
    {
        get
        {
            lock (_lock)
            {
                return _timers.Count(t => t.Ringing);
            }
        }
    }

    public Guid AddTimer(string label, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return Guid.Empty;
        }
        return Add(new TimerEntry
        {
            Id = Guid.NewGuid(),
            Label = label ?? string.Empty,
            Duration = duration,
            DueUtc = DateTime.UtcNow + duration,
        });
    }

    public Guid AddAlarm(int hour, int minute, string label)
    {
        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);
        var localNow = DateTime.Now;
        var target = new DateTime(localNow.Year, localNow.Month, localNow.Day, hour, minute, 0, DateTimeKind.Local);
        if (target <= localNow)
        {
            target = target.AddDays(1);
        }
        var dueUtc = target.ToUniversalTime();
        return Add(new TimerEntry
        {
            Id = Guid.NewGuid(),
            Label = label ?? string.Empty,
            Duration = Floor(dueUtc - DateTime.UtcNow),
            DueUtc = dueUtc,
            IsAlarm = true,
        });
    }

    private Guid Add(TimerEntry entry)
    {
        lock (_lock)
        {
            _timers.Add(entry);
            SaveTimers();
        }
        return entry.Id;
    }

    public void RemoveTimer(Guid id)
    {
        lock (_lock)
        {
            var idx = _timers.FindIndex(t => t.Id == id);
            if (idx < 0)
            {
                return;
            }
            _shell.DismissByTag(TimerTag(id));
            _timers.RemoveAt(idx);
            SaveTimers();
        }
    }

    public void DismissAllAlarms()
    {
        lock (_lock)
        {
            var changed = false;
            for (var i = _timers.Count - 1; i >= 0; i--)
            {
                if (_timers[i].Ringing)
                {
                    _shell.DismissByTag(TimerTag(_timers[i].Id));
                    _timers.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed)
            {
                SaveTimers();
            }
        }
    }

    public int AlarmSoundId
    {
        get => _alarmSoundId;
        set
        {
            var v = Math.Clamp(value, 1, 16);
            if (v != _alarmSoundId)
            {
                _alarmSoundId = v;
                SaveSettings();
            }
        }
    }

    public bool NotifyInChat
    {
        get => _notifyInChat;
        set
        {
            if (value != _notifyInChat)
            {
                _notifyInChat = value;
                SaveSettings();
            }
        }
    }

    public int AutoOffMinutes
    {
        get => _autoOffMinutes;
        set
        {
            var v = value is 1 or 3 or 5 ? value : 0;
            if (v != _autoOffMinutes)
            {
                _autoOffMinutes = v;
                SaveSettings();
            }
        }
    }

    public void PreviewSound(int seId) => PlaySound(seId);

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_pendingReconcile)
        {
            _pendingReconcile = false;
            Reconcile();
        }

        lock (_lock)
        {
            if (_timers.Count == 0)
            {
                return;
            }
            var now = DateTime.UtcNow;

            if (now - _lastElapseCheckUtc >= ElapseGate)
            {
                _lastElapseCheckUtc = now;
                var changed = false;

                foreach (var e in _timers)
                {
                    if (!e.Ringing && e.DueUtc <= now)
                    {
                        FireFresh(e, now);
                        changed = true;
                    }
                }

                if (_autoOffMinutes > 0)
                {
                    var cutoff = TimeSpan.FromMinutes(_autoOffMinutes);
                    for (var i = _timers.Count - 1; i >= 0; i--)
                    {
                        var e = _timers[i];
                        if (e.Ringing && now - e.RingStartUtc >= cutoff)
                        {
                            _shell.DismissByTag(TimerTag(e.Id));
                            _timers.RemoveAt(i);
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    SaveTimers();
                }
            }

            if (now >= _nextSePlayUtc && _timers.Any(t => t.Ringing) && IsSafeNow())
            {
                PlaySound(_alarmSoundId);
                _nextSePlayUtc = now + SeReplay;
            }
        }
    }

    private void FireFresh(TimerEntry entry, DateTime now)
    {
        entry.Ringing = true;
        entry.RingStartUtc = now;
        entry.Notified = true;
        // Let the next tick sound the alarm immediately rather than wait out a stale replay interval.
        _nextSePlayUtc = DateTime.MinValue;
        PostOsNotification(entry);
        if (_notifyInChat && Plugin.ClientState.IsLoggedIn)
        {
            PrintChatLine(entry);
        }
    }

    private void PostOsNotification(TimerEntry entry)
    {
        var label = DisplayLabel(entry.Label);
        _shell.PostNotification(
            "clock",
            Loc.T("notif.clock_notif_title"),
            string.Format(Loc.T("notif.clock_notif_body"), label),
            () => _shell.OpenApp("clock"),
            TimerTag(entry.Id));
    }

    private void PrintChatLine(TimerEntry entry)
    {
        try
        {
            var sb = new SeStringBuilder()
                .AddText("[AetherLove] ")
                .AddText(string.Format(Loc.T("notif.clock_chat"), DisplayLabel(entry.Label)));
            if (_chatLink is not null)
            {
                sb.AddText(" ")
                  .Add(_chatLink)
                  .AddUiForeground(LinkColor)
                  .AddText($"[{Loc.T("notif.clock_open")}]")
                  .AddUiForegroundOff()
                  .Add(RawPayload.LinkTerminator);
            }
            Plugin.ChatGui.Print(sb.BuiltString);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[ClockAlarm] chat print failed.");
        }
    }

    private void OnChatLink()
    {
        try
        {
            _services.GetService<MainPluginWindow>()?.OpenToClock();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[ClockAlarm] open from chat link failed.");
        }
    }

    /// <summary>Fires one chat sound effect (&lt;se.1&gt;..&lt;se.16&gt;); must run on the game main thread, which
    /// both the framework tick and the app's Draw (preview) already are.</summary>
    private static void PlaySound(int seId)
    {
        try
        {
            UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(seId, 1, 16));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[ClockAlarm] sound play failed.");
        }
    }

    private static bool IsSafeNow()
    {
        if (!Plugin.ClientState.IsLoggedIn || Plugin.ObjectTable.LocalPlayer is null)
        {
            return false;
        }
        var c = Plugin.Condition;
        return !c[ConditionFlag.InCombat]
            && !c[ConditionFlag.BetweenAreas]
            && !c[ConditionFlag.BetweenAreas51]
            && !c[ConditionFlag.WatchingCutscene]
            && !c[ConditionFlag.WatchingCutscene78]
            && !c[ConditionFlag.OccupiedInCutSceneEvent]
            && !c[ConditionFlag.OccupiedInQuestEvent]
            && !c[ConditionFlag.BoundByDuty];
    }

    private void Load()
    {
        lock (_lock)
        {
            var s = _store.Get<AlarmSettings>("settings");
            if (s is not null)
            {
                _alarmSoundId = Math.Clamp(s.AlarmSoundId, 1, 16);
                _notifyInChat = s.NotifyInChat;
                _autoOffMinutes = s.AutoOffMinutes is 1 or 3 or 5 ? s.AutoOffMinutes : 0;
            }

            _timers.Clear();
            var stored = _store.Get<List<PersistedTimer>>("timers");
            if (stored is not null)
            {
                foreach (var p in stored)
                {
                    _timers.Add(new TimerEntry
                    {
                        Id = p.Id,
                        Label = p.Label ?? string.Empty,
                        Duration = new TimeSpan(p.DurationTicks),
                        DueUtc = new DateTime(p.DueUtcTicks, DateTimeKind.Utc),
                        Ringing = p.Ringing,
                        RingStartUtc = new DateTime(p.RingStartUtcTicks, DateTimeKind.Utc),
                        Notified = p.Notified,
                        IsAlarm = p.IsAlarm,
                    });
                }
            }
        }
    }

    /// <summary>On (re)start: a persisted mid-ring entry keeps ringing and re-surfaces its OS notification (the
    /// center is not persisted); a pending entry whose time passed while unloaded fires now.</summary>
    private void Reconcile()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var changed = false;
            foreach (var e in _timers)
            {
                if (e.Ringing)
                {
                    PostOsNotification(e);
                }
                else if (e.DueUtc <= now)
                {
                    FireFresh(e, now);
                    changed = true;
                }
            }
            if (changed)
            {
                SaveTimers();
            }
        }
    }

    private void SaveTimers()
    {
        var list = _timers.Select(t => new PersistedTimer
        {
            Id = t.Id,
            Label = t.Label,
            DurationTicks = t.Duration.Ticks,
            DueUtcTicks = t.DueUtc.Ticks,
            Ringing = t.Ringing,
            RingStartUtcTicks = t.RingStartUtc.Ticks,
            Notified = t.Notified,
            IsAlarm = t.IsAlarm,
        }).ToList();
        _store.Set("timers", list);
    }

    private void SaveSettings() => _store.Set("settings", new AlarmSettings
    {
        AlarmSoundId = _alarmSoundId,
        NotifyInChat = _notifyInChat,
        AutoOffMinutes = _autoOffMinutes,
    });

    private static string DisplayLabel(string label) =>
        string.IsNullOrWhiteSpace(label) ? Loc.T("notif.clock_untitled") : label;

    private static TimeSpan Floor(TimeSpan span) => span < TimeSpan.Zero ? TimeSpan.Zero : span;

    private static string TimerTag(Guid id) => $"clock:timer:{id:N}";

    private sealed class TimerEntry
    {
        public Guid Id;
        public string Label = string.Empty;
        public TimeSpan Duration;
        public DateTime DueUtc;
        public bool Ringing;
        public DateTime RingStartUtc;
        public bool Notified;
        public bool IsAlarm;
    }

    private sealed class PersistedTimer
    {
        public Guid Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public long DurationTicks { get; set; }
        public long DueUtcTicks { get; set; }
        public bool Ringing { get; set; }
        public long RingStartUtcTicks { get; set; }
        public bool Notified { get; set; }
        public bool IsAlarm { get; set; }
    }

    private sealed class AlarmSettings
    {
        public int AlarmSoundId { get; set; }
        public bool NotifyInChat { get; set; }
        public int AutoOffMinutes { get; set; }
    }
}
