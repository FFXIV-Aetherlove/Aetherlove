using System;
using AetherLove.Os;
using AetherLove.Services.Localization;
using AetherLove.Windows;
using AetherOS.Apps.Timers;
using AetherOS.Apps.Timers.Schedule;
using AetherOS.Sdk;
using Dalamud.Plugin.Services;

namespace AetherLove.Services;

/// <summary>Publishes the soonest enabled reminder to the server info bar through
/// <see cref="ServerBarService"/> (ADR 21). This service only knows WHAT to say; every gate, the
/// toggles included, belongs to the bar service.</summary>
public sealed class TimersDtrService
{
    private const double PollSeconds = 1.0;
    private const int LabelMaxChars = 18;
    private const string AppId = "timers";

    private readonly ServerBarService _serverBar;
    private readonly TimerScheduleService _host;
    private readonly MainPluginWindow _mainWindow;

    private IServerBarEntry? _entry;
    private double _accum;

    public TimersDtrService(ServerBarService serverBar, TimerScheduleService host, MainPluginWindow mainWindow)
    {
        _serverBar = serverBar;
        _host = host;
        _mainWindow = mainWindow;
    }

    public void Initialize()
    {
        if (_entry is not null)
        {
            return;
        }
        _serverBar.SeedLegacyToggle(AppId, _host.GetReminderConfig().ShowDtr);
        _entry = _serverBar.For(AppId).Entry(
            "reminder", "AetherOS Timers", "os.timers_rem_dtr", _mainWindow.OpenToTimers);
        Plugin.Framework.Update += OnUpdate;
        Refresh();
    }

    public void Shutdown()
    {
        Plugin.Framework.Update -= OnUpdate;
        _entry?.Set(null);
        _entry = null;
    }

    private void OnUpdate(IFramework framework)
    {
        _accum += framework.UpdateDelta.TotalSeconds;
        if (_accum < PollSeconds)
        {
            return;
        }
        _accum = 0;
        Refresh();
    }

    private void Refresh()
    {
        if (_entry is null)
        {
            return;
        }
        var reminders = _host.GetReminderConfig();
        _entry.Set(TryGetSoonest(reminders, out var label, out var remaining)
            ? string.Format(Loc.T("dtr.timers"), Truncate(label), FormatCountdown(remaining))
            : null);
    }

    private bool TryGetSoonest(ReminderConfig config, out string label, out TimeSpan remaining)
    {
        var now = DateTime.UtcNow;
        var best = DateTime.MaxValue;
        var bestLabel = string.Empty;
        void Consider(DateTime when, string candidate)
        {
            if (when > now && when < best)
            {
                best = when;
                bestLabel = candidate;
            }
        }
        if (Enabled(config, ReminderKind.DailyReset))
        {
            Consider(EorzeaSchedule.NextDailyReset(now), Loc.T("notif.timers_daily"));
        }
        if (Enabled(config, ReminderKind.GrandCompanyReset))
        {
            Consider(EorzeaSchedule.NextGrandCompanyReset(now), Loc.T("notif.timers_gc"));
        }
        if (Enabled(config, ReminderKind.WeeklyReset))
        {
            Consider(EorzeaSchedule.NextWeeklyReset(now), Loc.T("notif.timers_weekly"));
        }
        if (Enabled(config, ReminderKind.FashionReportOpen))
        {
            var (isOpen, nextChange) = EorzeaSchedule.FashionReport(now);
            if (!isOpen)
            {
                Consider(nextChange, Loc.T("notif.timers_fr"));
            }
        }
        if (Enabled(config, ReminderKind.CactpotDraw))
        {
            Consider(EorzeaSchedule.NextCactpotDraw(_host.CurrentRegion, now), Loc.T("notif.timers_cactpot"));
        }
        if (Enabled(config, ReminderKind.OceanBoarding))
        {
            Consider(EorzeaSchedule.NextVoyage(now).DepartureUtc, Loc.T("notif.timers_ocean"));
        }
        foreach (var timer in _host.GetCustomTimers())
        {
            Consider(timer.DueUtc, timer.Name);
        }
        label = bestLabel;
        remaining = best == DateTime.MaxValue ? TimeSpan.Zero : best - now;
        return best != DateTime.MaxValue;
    }

    /// <summary>TryGetValue rather than <see cref="ReminderConfig.For"/>, which would grow the shared
    /// dictionary from a poll.</summary>
    private static bool Enabled(ReminderConfig config, ReminderKind kind)
    {
        return config.Kinds.TryGetValue(kind, out var entry) && entry.Enabled;
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 1)
        {
            return $"{(int)remaining.TotalDays}d {remaining.Hours:00}:{remaining.Minutes:00}";
        }
        return $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    private static string Truncate(string label)
    {
        return label.Length > LabelMaxChars ? label[..LabelMaxChars] + "…" : label;
    }
}
