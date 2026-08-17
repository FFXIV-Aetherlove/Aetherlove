using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherLove.Services.Hub;
using AetherLove.Services.Localization;
using AetherLove.Windows;
using AetherOS.Apps.Calendar;
using AetherOS.Apps.Timers;
using AetherOS.Apps.Timers.Schedule;
using AetherOS.Sdk;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using Microsoft.Extensions.DependencyInjection;

namespace AetherLove.Os;

/// <summary>The Timers app's reminder engine, and the one place any timers or calendar alert actually
/// fires. Ticks on the game framework so reminders land while the app is closed: it owns the persisted
/// reminder settings, custom timers and per-reminder fire stamps, sweeps the schedule math each second,
/// watches the retainer/fleet books for completions, asks the calendar store for due alerts, and delivers
/// each fire as an OS notification plus a native chat line with a clickable link back to the app.</summary>
public sealed class TimerScheduleService : ITimersHost, IDisposable
{
    private static readonly TimeSpan ElapseGate = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RebaselineGap = TimeSpan.FromMinutes(5);
    /// <summary>Covers the longest reminder cycle (weekly) with margin, so a rebaseline always finds the
    /// latest past occurrence of every kind without walking an unbounded history.</summary>
    private static readonly TimeSpan RebaselineLookback = TimeSpan.FromDays(8);
    private static readonly TimeSpan StatePersistEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CommitmentsMemo = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RegionRecheck = TimeSpan.FromSeconds(30);
    private const uint TimersLinkCommandId = 9;
    private const uint CalendarLinkCommandId = 11;
    private const ushort LinkColor = 539;
    private const uint ReminderSeId = 9;
    private const int MaxPendingSounds = 3;
    private const string TimersAppId = "timers";
    private const string CalendarAppId = "calendar";
    private const string RemindersKey = "reminders";
    private const string CustomTimersKey = "customTimers";
    private const string StateKey = "reminderState";
    private const GameRegion DefaultRegion = GameRegion.NorthAmerica;

    private readonly IAppStorage _store;
    private readonly IOsShell _shell;
    private readonly IServiceProvider _services;
    private readonly RetainerFleetService _retainers;
    private readonly CalendarStoreService _calendar;
    private readonly AetherHubContext _hub;
    private readonly object _lock = new();
    private readonly DalamudLinkPayload? _timersLink;
    private readonly DalamudLinkPayload? _calendarLink;
    private readonly Dictionary<string, long> _lastFired = new();
    private readonly HashSet<CompletionKey> _pendingCompletions = new();
    private readonly HashSet<CompletionKey> _notifiedCompletions = new();

    private ReminderConfig _config = new();
    private List<CustomTimer> _custom = new();
    private DateTime _lastSweepUtc = DateTime.MinValue;
    private DateTime _lastStateSaveUtc = DateTime.MinValue;
    private DateTime _lastElapseCheckUtc = DateTime.MinValue;
    private bool _stateDirty;
    private GameRegion _region = DefaultRegion;
    private DateTime _nextRegionCheckUtc = DateTime.MinValue;
    private int _pendingSounds;
    private bool _started;
    private volatile bool _pendingReconcile;

    private IReadOnlyList<TimersCommitment> _commitments = Array.Empty<TimersCommitment>();
    private DateTime _commitmentsFetchedUtc = DateTime.MinValue;

    private readonly record struct CompletionKey(ulong ContentId, bool Vessel, ulong ItemKey, long DueTicks);

    private sealed class PersistedReminderState
    {
        public long LastSweepUtcTicks { get; set; }
        public Dictionary<string, long> LastFired { get; set; } = new();
    }

    public TimerScheduleService(AppStorageService storage, IOsShell shell, IServiceProvider services,
        RetainerFleetService retainers, CalendarStoreService calendar, AetherHubContext hub)
    {
        _store = storage.For(TimersAppId);
        _shell = shell;
        _services = services;
        _retainers = retainers;
        _calendar = calendar;
        _hub = hub;
        try
        {
            _timersLink = Plugin.ChatGui.AddChatLinkHandler(TimersLinkCommandId, (_, _) => OpenFromChat(toCalendar: false));
            _calendarLink = Plugin.ChatGui.AddChatLinkHandler(CalendarLinkCommandId, (_, _) => OpenFromChat(toCalendar: true));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Timers] Failed to register chat link handlers.");
        }
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }
        Load();
        // Reconcile (which can notify and print chat) must run on the framework thread; Start runs off it.
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
            Plugin.ChatGui.RemoveChatLinkHandler(TimersLinkCommandId);
            Plugin.ChatGui.RemoveChatLinkHandler(CalendarLinkCommandId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Timers] RemoveChatLinkHandler failed.");
        }
    }

    public ReminderConfig GetReminderConfig()
    {
        lock (_lock)
        {
            return _config;
        }
    }

    public void SaveReminderConfig(ReminderConfig config)
    {
        lock (_lock)
        {
            _config = config;
            _store.Set(RemindersKey, config);
        }
    }

    public IReadOnlyList<CustomTimer> GetCustomTimers()
    {
        lock (_lock)
        {
            return _custom.ToList();
        }
    }

    public void SaveCustomTimers(IReadOnlyList<CustomTimer> timers)
    {
        lock (_lock)
        {
            _custom = timers.ToList();
            _store.Set(CustomTimersKey, _custom);
            PruneCustomStampsLocked();
        }
    }

    public async Task<IReadOnlyList<TimersCommitment>> GetCommitmentsAsync()
    {
        lock (_lock)
        {
            if (DateTime.UtcNow - _commitmentsFetchedUtc < CommitmentsMemo)
            {
                return _commitments;
            }
        }
        try
        {
            var rsvps = await _hub.GetMyVenueRsvpsAsync().ConfigureAwait(false);
            var mapped = rsvps
                .Select(r => new TimersCommitment(r.VenueId, r.VenueName, r.StartUtc.UtcDateTime))
                .ToArray();
            lock (_lock)
            {
                _commitments = mapped;
                _commitmentsFetchedUtc = DateTime.UtcNow;
            }
            return mapped;
        }
        catch (Exception)
        {
            return Array.Empty<TimersCommitment>();
        }
    }

    public GameRegion CurrentRegion
    {
        get
        {
            lock (_lock)
            {
                return _config.CactpotRegionOverride ?? _region;
            }
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!PhonePower.IsOn)
        {
            return;
        }
        if (_pendingReconcile)
        {
            _pendingReconcile = false;
            lock (_lock)
            {
                RebaselineLocked(DateTime.UtcNow);
            }
        }
        var now = DateTime.UtcNow;
        if (now - _lastElapseCheckUtc < ElapseGate)
        {
            return;
        }
        _lastElapseCheckUtc = now;
        lock (_lock)
        {
            if (_lastSweepUtc == DateTime.MinValue || now - _lastSweepUtc > RebaselineGap)
            {
                RebaselineLocked(now);
            }
            else
            {
                SweepLocked(_lastSweepUtc, now);
            }
            DrainPendingSoundLocked();
        }
    }

    private void SweepLocked(DateTime prev, DateTime now)
    {
        MaybeDetectRegionLocked(now);
        var due = ReminderMath.DueBetween(prev, now, _config, _custom, _region);
        foreach (var reminder in due.OrderBy(d => d.OccurrenceUtc.AddMinutes(-d.LeadMinutes)))
        {
            var key = StampKey(reminder);
            if (_lastFired.TryGetValue(key, out var stamp) && reminder.OccurrenceUtc.Ticks <= stamp)
            {
                continue;
            }
            _lastFired[key] = reminder.OccurrenceUtc.Ticks;
            _stateDirty = true;
            FireReminderLocked(reminder);
        }
        SweepCompletionsLocked(prev, now, rebaseline: false);
        foreach (var ev in _calendar.AlertsDue(prev, now))
        {
            FireCalendarLocked(ev);
        }
        _lastSweepUtc = now;
        MaybePersistStateLocked(now, force: false);
    }

    /// <summary>Advances every schedule stamp to the latest past occurrence without firing; only custom
    /// timers that elapsed in the gap get one catch-up each, and venture/fleet ready state is re-posted
    /// as a current-state notification.</summary>
    private void RebaselineLocked(DateTime now)
    {
        MaybeDetectRegionLocked(now);
        var prev = _lastSweepUtc;
        var past = ReminderMath.DueBetween(now - RebaselineLookback, now, _config, Array.Empty<CustomTimer>(), _region);
        foreach (var reminder in past)
        {
            var key = StampKey(reminder);
            if (!_lastFired.TryGetValue(key, out var stamp) || reminder.OccurrenceUtc.Ticks > stamp)
            {
                _lastFired[key] = reminder.OccurrenceUtc.Ticks;
                _stateDirty = true;
            }
        }
        if (prev != DateTime.MinValue)
        {
            foreach (var timer in _custom)
            {
                CatchUpCustomTimerLocked(timer, prev, now);
            }
        }
        SweepCompletionsLocked(prev, now, rebaseline: true);
        _lastSweepUtc = now;
        MaybePersistStateLocked(now, force: true);
    }

    private void CatchUpCustomTimerLocked(CustomTimer timer, DateTime prev, DateTime now)
    {
        var crossedLead = int.MaxValue;
        foreach (var lead in timer.LeadMinutes)
        {
            var fireAt = timer.DueUtc.AddMinutes(-lead);
            if (fireAt > now)
            {
                continue;
            }
            var key = CustomStampKey(lead, timer.Id);
            if (_lastFired.TryGetValue(key, out var stamp) && timer.DueUtc.Ticks <= stamp)
            {
                continue;
            }
            _lastFired[key] = timer.DueUtc.Ticks;
            _stateDirty = true;
            if (fireAt > prev)
            {
                crossedLead = Math.Min(crossedLead, lead);
            }
        }
        if (crossedLead != int.MaxValue)
        {
            FireReminderLocked(new DueReminder(ReminderKind.CustomTimer, timer.DueUtc, crossedLead, timer.Id));
        }
    }

    /// <summary>Venture and fleet completions, handled outside ReminderMath because they need the books.
    /// Two maps: only a completion previously seen pending this session fires as a fresh reminder (chat and
    /// sound included); anything already done when first seen (login over a pile of finished ventures, a
    /// rebaseline) collapses into a tag-replaced current-state notification per character instead.</summary>
    private void SweepCompletionsLocked(DateTime prev, DateTime now, bool rebaseline)
    {
        var ventureEnabled = KindEnabledLocked(ReminderKind.VentureComplete);
        var fleetEnabled = KindEnabledLocked(ReminderKind.FleetReturn);
        var live = new HashSet<CompletionKey>();
        foreach (var character in _retainers.Characters)
        {
            foreach (var retainer in character.Retainers)
            {
                if (retainer.VentureId == 0 || retainer.CompleteUtc == default)
                {
                    continue;
                }
                var key = new CompletionKey(character.ContentId, false, retainer.RetainerId, retainer.CompleteUtc.Ticks);
                live.Add(key);
                HandleCompletionLocked(key, retainer.CompleteUtc, prev, now, rebaseline, ventureEnabled,
                    () => string.Format(Loc.T("notif.timers_venture"), retainer.Name, retainer.VentureName),
                    AetherOS.Apps.Timers.TimersTags.ForVenture(character.ContentId));
            }
            for (var i = 0; i < character.Fleet.Count; i++)
            {
                var vessel = character.Fleet[i];
                if (vessel.ReturnUtc == DateTime.MinValue)
                {
                    continue;
                }
                var key = new CompletionKey(character.ContentId, true, (ulong)i, vessel.ReturnUtc.Ticks);
                live.Add(key);
                HandleCompletionLocked(key, vessel.ReturnUtc, prev, now, rebaseline, fleetEnabled,
                    () => string.Format(Loc.T("notif.timers_fleet"), vessel.Name),
                    AetherOS.Apps.Timers.TimersTags.ForFleet(character.ContentId));
            }
        }
        _pendingCompletions.RemoveWhere(k => !live.Contains(k));
        _notifiedCompletions.RemoveWhere(k => !live.Contains(k));
    }

    private void HandleCompletionLocked(CompletionKey key, DateTime doneUtc, DateTime prev, DateTime now,
        bool rebaseline, bool enabled, Func<string> bodyFactory, string tag)
    {
        if (doneUtc > now)
        {
            _pendingCompletions.Add(key);
            return;
        }
        if (!_notifiedCompletions.Add(key))
        {
            return;
        }
        if (!enabled)
        {
            return;
        }
        var fresh = !rebaseline && doneUtc > prev && _pendingCompletions.Contains(key);
        var body = bodyFactory();
        PostTimersNotification(body, tag);
        if (fresh)
        {
            AnnounceLocked(Loc.T("notif.timers_chat"), body, _timersLink);
        }
    }

    private void FireReminderLocked(DueReminder due)
    {
        if (DescribeLocked(due) is not { } info)
        {
            return;
        }
        var body = due.LeadMinutes > 0
            ? info.Body + " " + string.Format(Loc.T("notif.timers_lead"), due.LeadMinutes)
            : info.Body;
        PostTimersNotification(body, info.Tag);
        AnnounceLocked(Loc.T("notif.timers_chat"), body, _timersLink);
    }

    private void FireCalendarLocked(CalendarEvent ev)
    {
        var body = string.Format(Loc.T("notif.calendar_alert_body"), ev.Title);
        _shell.PostNotification(CalendarAppId, Loc.T("notif.calendar_alert_title"), body,
            OpenCalendarFromNotification, $"calendar:event:{ev.Id}");
        AnnounceLocked(null, body, _calendarLink);
    }

    private (string Body, string Tag)? DescribeLocked(DueReminder due)
    {
        switch (due.Kind)
        {
            case ReminderKind.DailyReset:
                return (Loc.T("notif.timers_daily"), AetherOS.Apps.Timers.TimersTags.ForKind(due.Kind));
            case ReminderKind.GrandCompanyReset:
                return (Loc.T("notif.timers_gc"), AetherOS.Apps.Timers.TimersTags.ForKind(due.Kind));
            case ReminderKind.WeeklyReset:
                return (Loc.T("notif.timers_weekly"), AetherOS.Apps.Timers.TimersTags.ForKind(due.Kind));
            case ReminderKind.FashionReportOpen:
                return (Loc.T("notif.timers_fr"), AetherOS.Apps.Timers.TimersTags.ForKind(due.Kind));
            case ReminderKind.CactpotDraw:
                return (Loc.T("notif.timers_cactpot"), AetherOS.Apps.Timers.TimersTags.ForKind(due.Kind));
            case ReminderKind.OceanBoarding:
                return (Loc.T("notif.timers_ocean"), AetherOS.Apps.Timers.TimersTags.ForKind(due.Kind));
            case ReminderKind.CustomTimer:
                var timer = _custom.FirstOrDefault(t => t.Id == due.ContextId);
                if (timer is null)
                {
                    return null;
                }
                return (string.Format(Loc.T("notif.timers_custom"), timer.Name), AetherOS.Apps.Timers.TimersTags.ForCustom(due.ContextId));
            default:
                return null;
        }
    }

    private void PostTimersNotification(string body, string tag)
    {
        _shell.PostNotification(TimersAppId, Loc.T("notif.timers_title"), body, OpenTimersFromNotification, tag);
    }

    private void AnnounceLocked(string? sentence, string body, DalamudLinkPayload? link)
    {
        if (!Plugin.ClientState.IsLoggedIn)
        {
            return;
        }
        PrintChatLine(sentence, body, link);
        _pendingSounds = Math.Min(_pendingSounds + 1, MaxPendingSounds);
    }

    private void DrainPendingSoundLocked()
    {
        if (_pendingSounds == 0 || !IsSafeNow())
        {
            return;
        }
        _pendingSounds--;
        PlaySound();
    }

    /// <summary>One native chat line per fire: an optional lead-in sentence, then the reminder body as
    /// the clickable link back to the app.</summary>
    private void PrintChatLine(string? sentence, string body, DalamudLinkPayload? link)
    {
        try
        {
            var sb = new SeStringBuilder().AddText("[AetherOS] ");
            if (sentence is not null)
            {
                sb.AddText(sentence).AddText(" ");
            }
            if (link is not null)
            {
                sb.Add(link)
                  .AddUiForeground(LinkColor)
                  .AddText($"[{body}]")
                  .AddUiForegroundOff()
                  .Add(RawPayload.LinkTerminator);
            }
            else
            {
                sb.AddText(body);
            }
            Plugin.ChatGui.Print(sb.BuiltString);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Timers] chat print failed.");
        }
    }

    private void OpenTimersFromNotification()
    {
        OpenFromChat(toCalendar: false);
    }

    private void OpenCalendarFromNotification()
    {
        OpenFromChat(toCalendar: true);
    }

    private void OpenFromChat(bool toCalendar)
    {
        try
        {
            var window = _services.GetService<MainPluginWindow>();
            if (window is null)
            {
                return;
            }
            if (toCalendar)
            {
                window.OpenToCalendar();
            }
            else
            {
                window.OpenToTimers();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Timers] open from chat link failed.");
        }
    }

    private static void PlaySound()
    {
        try
        {
            UIGlobals.PlayChatSoundEffect(ReminderSeId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Timers] sound play failed.");
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

    private void MaybeDetectRegionLocked(DateTime now)
    {
        if (_config.CactpotRegionOverride is { } manual)
        {
            _region = manual;
            return;
        }
        if (now < _nextRegionCheckUtc)
        {
            return;
        }
        _nextRegionCheckUtc = now + RegionRecheck;
        try
        {
            var worldId = Plugin.ObjectTable.LocalPlayer?.CurrentWorld.RowId ?? 0u;
            if (worldId == 0)
            {
                return;
            }
            var world = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>().GetRowOrDefault(worldId);
            var regionId = world?.DataCenter.ValueNullable?.Region.RowId ?? 0u;
            if (regionId is >= (uint)GameRegion.Japan and <= (uint)GameRegion.Oceania)
            {
                _region = (GameRegion)regionId;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[Timers] Region detection failed: {ex.Message}");
        }
    }

    private bool KindEnabledLocked(ReminderKind kind)
    {
        return _config.Kinds.TryGetValue(kind, out var entry) && entry.Enabled;
    }

    private static string StampKey(DueReminder due)
    {
        return due.ContextId == Guid.Empty
            ? $"{(int)due.Kind}:{due.LeadMinutes}"
            : CustomStampKey(due.LeadMinutes, due.ContextId);
    }

    private static string CustomStampKey(int lead, Guid id)
    {
        return $"{(int)ReminderKind.CustomTimer}:{lead}:{id:N}";
    }

    private void PruneCustomStampsLocked()
    {
        var prefix = $"{(int)ReminderKind.CustomTimer}:";
        var liveIds = _custom.Select(t => t.Id.ToString("N")).ToHashSet();
        var stale = _lastFired.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal)
                && k.Split(':') is { Length: 3 } parts
                && !liveIds.Contains(parts[2]))
            .ToList();
        foreach (var key in stale)
        {
            _lastFired.Remove(key);
        }
        if (stale.Count > 0)
        {
            _stateDirty = true;
        }
    }

    private void Load()
    {
        lock (_lock)
        {
            _config = _store.Get<ReminderConfig>(RemindersKey) ?? new ReminderConfig();
            _custom = _store.Get<List<CustomTimer>>(CustomTimersKey) ?? new List<CustomTimer>();
            var state = _store.Get<PersistedReminderState>(StateKey);
            if (state is not null)
            {
                _lastSweepUtc = new DateTime(state.LastSweepUtcTicks, DateTimeKind.Utc);
                foreach (var (key, ticks) in state.LastFired)
                {
                    _lastFired[key] = ticks;
                }
            }
        }
    }

    private void MaybePersistStateLocked(DateTime now, bool force)
    {
        if (!force && !_stateDirty && now - _lastStateSaveUtc < StatePersistEvery)
        {
            return;
        }
        _lastStateSaveUtc = now;
        _stateDirty = false;
        _store.Set(StateKey, new PersistedReminderState
        {
            LastSweepUtcTicks = _lastSweepUtc.Ticks,
            LastFired = new Dictionary<string, long>(_lastFired),
        });
    }
}
