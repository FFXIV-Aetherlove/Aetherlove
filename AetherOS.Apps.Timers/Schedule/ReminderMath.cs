using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Timers.Schedule;

public sealed record DueReminder(ReminderKind Kind, DateTime OccurrenceUtc, int LeadMinutes, Guid ContextId);

/// <summary>Pure fire-instant arithmetic for the schedule-kind and custom-timer reminders. The plugin
/// service owns venture-complete, fleet-return and calendar-event alerts; they are never emitted here.</summary>
public static class ReminderMath
{
    private static readonly TimeSpan _dayPeriod = TimeSpan.FromDays(1);
    private static readonly TimeSpan _weekPeriod = TimeSpan.FromDays(7);
    private static readonly TimeSpan _voyagePeriod = TimeSpan.FromHours(2);

    /// <summary>Every schedule-kind and custom-timer reminder whose fire instant falls in
    /// (prevUtc, nowUtc], where fire instant = OccurrenceUtc - LeadMinutes. When the interval spans
    /// several occurrences (the game sat closed for hours), at most the LATEST missed fire is emitted
    /// per (kind, lead), and per (timer, lead) for custom timers; the service rebaselines prevUtc to
    /// nowUtc after each call, so the skipped backlog is dropped for good rather than deferred.
    /// The OceanBoarding occurrence is the registration OPEN instant (departure - 15 minutes), the
    /// moment a player can act. ContextId = CustomTimer.Id for custom timers, Guid.Empty otherwise.</summary>
    public static List<DueReminder> DueBetween(DateTime prevUtc, DateTime nowUtc,
        ReminderConfig config, IReadOnlyList<CustomTimer> customTimers, GameRegion region)
    {
        var due = new List<DueReminder>();
        if (nowUtc <= prevUtc)
        {
            return due;
        }
        AddScheduleKind(due, ReminderKind.DailyReset, prevUtc, nowUtc, config,
            EorzeaSchedule.NextDailyReset, _dayPeriod);
        AddScheduleKind(due, ReminderKind.GrandCompanyReset, prevUtc, nowUtc, config,
            EorzeaSchedule.NextGrandCompanyReset, _dayPeriod);
        AddScheduleKind(due, ReminderKind.WeeklyReset, prevUtc, nowUtc, config,
            EorzeaSchedule.NextWeeklyReset, _weekPeriod);
        AddScheduleKind(due, ReminderKind.FashionReportOpen, prevUtc, nowUtc, config,
            EorzeaSchedule.NextFashionReportOpen, _weekPeriod);
        AddScheduleKind(due, ReminderKind.CactpotDraw, prevUtc, nowUtc, config,
            t => EorzeaSchedule.NextCactpotDraw(region, t), _weekPeriod);
        AddScheduleKind(due, ReminderKind.OceanBoarding, prevUtc, nowUtc, config,
            EorzeaSchedule.NextOceanRegistrationOpen, _voyagePeriod);
        foreach (var timer in customTimers)
        {
            foreach (var lead in timer.LeadMinutes)
            {
                var fireUtc = timer.DueUtc.AddMinutes(-lead);
                if (fireUtc > prevUtc && fireUtc <= nowUtc)
                {
                    due.Add(new DueReminder(ReminderKind.CustomTimer, timer.DueUtc, lead, timer.Id));
                }
            }
        }
        return due;
    }

    private static void AddScheduleKind(List<DueReminder> due, ReminderKind kind, DateTime prevUtc,
        DateTime nowUtc, ReminderConfig config, Func<DateTime, DateTime> nextStrictlyAfter, TimeSpan period)
    {
        if (!config.Kinds.TryGetValue(kind, out var entry) || !entry.Enabled)
        {
            return;
        }
        foreach (var lead in entry.LeadMinutes)
        {
            var offset = TimeSpan.FromMinutes(lead);
            // Occurrences are exactly period-spaced, so the latest occurrence at or before an instant
            // is the next one strictly after it, minus one period.
            var occurrence = nextStrictlyAfter(nowUtc + offset) - period;
            var fireUtc = occurrence - offset;
            if (fireUtc > prevUtc && fireUtc <= nowUtc)
            {
                due.Add(new DueReminder(kind, occurrence, lead, Guid.Empty));
            }
        }
    }
}
