using System;
using AetherLove.Config;
using AetherLove.Os;
using AetherLove.Services.Localization;
using AetherLove.Windows;
using AetherOS.Apps.Timers;
using AetherOS.Apps.Timers.Schedule;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace AetherLove.Services;

/// <summary>Publishes the soonest enabled Timers countdown to the DTR bar. A sibling of
/// <see cref="GrooveDtrService"/>: same text-entry shape, polled once a second because the countdown
/// itself ticks.</summary>
public sealed class TimersDtrService
{
    private const double PollSeconds = 1.0;
    private const int LabelMaxChars = 24;
    private const string AppId = "timers";

    private readonly IDtrBar _dtrBar;
    private readonly TimerScheduleService _host;
    private readonly Configuration _config;
    private readonly MainPluginWindow _mainWindow;

    private IDtrBarEntry? _entry;
    private double _accum;
    private string _lastText = string.Empty;
    private bool _lastShown;

    public TimersDtrService(IDtrBar dtrBar, TimerScheduleService host, Configuration config,
        MainPluginWindow mainWindow)
    {
        _dtrBar = dtrBar;
        _host = host;
        _config = config;
        _mainWindow = mainWindow;
    }

    public void Initialize()
    {
        if (_entry is not null)
        {
            return;
        }
        _entry = _dtrBar.Get("AetherOS Timers");
        _entry.OnClick = _ => _mainWindow.OpenToTimers();
        Plugin.Framework.Update += OnUpdate;
        Refresh();
    }

    public void Shutdown()
    {
        Plugin.Framework.Update -= OnUpdate;
        _entry?.Remove();
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
        // Removing the Timers app is an opt-out of the whole feature, so the server-bar line goes with it.
        var eligible = _config.EnableDtrEntry && reminders.ShowDtr && _mainWindow.IsPoweredOn
            && !_config.Os.RemovedApps.Contains(AppId);
        var shown = false;
        var text = string.Empty;
        if (eligible && TryGetSoonest(reminders, out var label, out var remaining))
        {
            shown = true;
            text = string.Format(Loc.T("dtr.timers"), Truncate(label), FormatCountdown(remaining));
        }
        if (shown == _lastShown && text == _lastText)
        {
            return;
        }
        _lastShown = shown;
        _lastText = text;
        _entry.Shown = shown;
        if (shown)
        {
            _entry.Text = new SeStringBuilder().AddText(text).Build();
        }
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
