using System;

namespace AetherOS.Apps.Timers.Schedule;

/// <summary>Pure UTC arithmetic for every fixed game schedule the Timers app tracks. All inputs and
/// outputs are UTC; occurrence lookups use a strict '&gt;' so the exact instant returns the NEXT one.</summary>
public static class EorzeaSchedule
{
    private const int DailyResetHourUtc = 15;
    private const int GrandCompanyResetHourUtc = 20;
    private const int WeeklyResetHourUtc = 8;
    private const DayOfWeek WeeklyResetDay = DayOfWeek.Tuesday;
    private const int FashionReportOpenHourUtc = 8;
    private const DayOfWeek FashionReportOpenDay = DayOfWeek.Friday;

    private const DayOfWeek CactpotDrawDay = DayOfWeek.Saturday;

    /// <summary>Per-region draw hours await in-game verification; the Reminders screen region override
    /// covers any drift until then.</summary>
    private const int CactpotDrawHourJapanUtc = 8;
    private const int CactpotDrawHourNorthAmericaUtc = 8;
    private const int CactpotDrawHourEuropeUtc = 8;
    private const int CactpotDrawHourOceaniaUtc = 8;

    private const int OceanRegistrationLeadMinutes = 15;
    private const long VoyagePeriodSeconds = 7200;

    /// <summary>Empirically anchored shift between the voyage index and the route table. To re-derive:
    /// take one live in-game Ocean Fishing schedule entry, find its route's position in the valid
    /// IKDRouteTable row list, and set this to (position - VoyageIndex(departure)) mod row count.</summary>
    private const long OceanRouteOffset = 0;

    public static DateTime NextDailyReset(DateTime utcNow)
    {
        return NextDailyAt(utcNow, DailyResetHourUtc);
    }

    public static DateTime NextGrandCompanyReset(DateTime utcNow)
    {
        return NextDailyAt(utcNow, GrandCompanyResetHourUtc);
    }

    public static DateTime NextWeeklyReset(DateTime utcNow)
    {
        return NextWeeklyAt(utcNow, WeeklyResetDay, WeeklyResetHourUtc);
    }

    /// <summary>Open iff the next close (weekly reset) lands before the next Friday opening;
    /// NextChangeUtc is whichever of the two comes first.</summary>
    public static (bool IsOpen, DateTime NextChangeUtc) FashionReport(DateTime utcNow)
    {
        var nextOpen = NextFashionReportOpen(utcNow);
        var nextClose = NextWeeklyReset(utcNow);
        var isOpen = nextClose < nextOpen;
        return (isOpen, isOpen ? nextClose : nextOpen);
    }

    public static DateTime NextCactpotDraw(GameRegion region, DateTime utcNow)
    {
        var hour = region switch
        {
            GameRegion.Japan => CactpotDrawHourJapanUtc,
            GameRegion.NorthAmerica => CactpotDrawHourNorthAmericaUtc,
            GameRegion.Europe => CactpotDrawHourEuropeUtc,
            GameRegion.Oceania => CactpotDrawHourOceaniaUtc,
            _ => CactpotDrawHourNorthAmericaUtc,
        };
        return NextWeeklyAt(utcNow, CactpotDrawDay, hour);
    }

    public static long VoyageIndex(DateTime utc)
    {
        return (utc.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerSecond / VoyagePeriodSeconds;
    }

    /// <summary>The next departure whose registration window or departure is still in the future.
    /// Registration is the 15 minutes BEFORE the even hour: [departure - 15min, departure).</summary>
    public static (DateTime DepartureUtc, DateTime RegistrationOpensUtc, bool RegistrationOpen) NextVoyage(DateTime utcNow)
    {
        var departure = NextEvenHourStrictlyAfter(utcNow);
        var registrationOpens = departure.AddMinutes(-OceanRegistrationLeadMinutes);
        var registrationOpen = utcNow >= registrationOpens;
        return (departure, registrationOpens, registrationOpen);
    }

    public static int RouteTableIndex(long voyageIndex, int tableRowCount)
    {
        if (tableRowCount <= 0)
        {
            return 0;
        }
        var shifted = (voyageIndex + OceanRouteOffset) % tableRowCount;
        return (int)((shifted + tableRowCount) % tableRowCount);
    }

    internal static DateTime NextFashionReportOpen(DateTime utcNow)
    {
        return NextWeeklyAt(utcNow, FashionReportOpenDay, FashionReportOpenHourUtc);
    }

    internal static DateTime NextOceanRegistrationOpen(DateTime utcNow)
    {
        return NextEvenHourStrictlyAfter(utcNow.AddMinutes(OceanRegistrationLeadMinutes))
            .AddMinutes(-OceanRegistrationLeadMinutes);
    }

    private static DateTime NextDailyAt(DateTime utcNow, int hour)
    {
        var candidate = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, hour, 0, 0, DateTimeKind.Utc);
        if (candidate > utcNow)
        {
            return candidate;
        }
        return candidate.AddDays(1);
    }

    private static DateTime NextWeeklyAt(DateTime utcNow, DayOfWeek day, int hour)
    {
        var days = ((int)day - (int)utcNow.DayOfWeek + 7) % 7;
        var candidate = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, hour, 0, 0, DateTimeKind.Utc).AddDays(days);
        if (candidate > utcNow)
        {
            return candidate;
        }
        return candidate.AddDays(7);
    }

    private static DateTime NextEvenHourStrictlyAfter(DateTime utc)
    {
        var hourFloor = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
        var evenFloor = hourFloor.AddHours(-(utc.Hour % 2));
        return evenFloor.AddHours(2);
    }
}
