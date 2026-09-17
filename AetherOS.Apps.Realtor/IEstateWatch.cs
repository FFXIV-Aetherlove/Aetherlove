using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Realtor;

/// <summary>Which house a record is about. Stored as a number, so the order is fixed; a book written before
/// Free Company tracking has no value and reads as Personal.</summary>
public enum EstateKind
{
    Personal = 0,
    FreeCompany = 1,
}

/// <summary>One house a character answers for, and when the clock on it last started over: its own private
/// estate, or the house of its Free Company.
///
/// A Free Company house resets when ANY member enters, and this client only sees its own characters. So a
/// Free Company count is the longest the absence can be, never the exact figure, and the copy says so.
/// Apartments and shared estates are not tracked: a tenant's visit does not reset the owner's clock.</summary>
public sealed class EstateRecord
{
    public ulong ContentId { get; set; }

    public EstateKind Kind { get; set; }

    /// <summary>The game's id for the house. Only filled for Free Company records, where it lets a visit by
    /// one character reset the record of every other character in the same company.</summary>
    public ulong HouseId { get; set; }

    public string Character { get; set; } = string.Empty;

    public string World { get; set; } = string.Empty;

    public int Ward { get; set; }

    public int Plot { get; set; }

    /// <summary>The residential zone the estate sits in, read off the owned house rather than off a visit,
    /// so it is known before the character has ever been seen going home.</summary>
    public uint TerritoryTypeId { get; set; }

    /// <summary>The last visit this install actually watched happen. Only meaningful once
    /// <see cref="VisitObserved"/> is set.</summary>
    public DateTime LastVisitUtc { get; set; }

    /// <summary>False until a visit has been seen. Nothing is counted, warned about or announced before
    /// then: an estate we have never watched somebody enter tells us nothing about how long they have been
    /// away, so the app asks them to go home once rather than inventing an absence.</summary>
    public bool VisitObserved { get; set; }

    public DateTime FirstSeenUtc { get; set; }

    /// <summary>The highest warning already announced for the current absence, so a long absence is not
    /// re-announced on every login. A visit puts it back to zero.</summary>
    public int NotifiedStage { get; set; }
}

/// <summary>How long an absence has to run before it matters. The game demolishes a private estate after 45
/// days without the owner entering it and starts warning at 30, which is when its own Timers entry appears.
/// Both the watcher that announces and the app that draws read these, so the two can never disagree about
/// what counts as a problem.</summary>
public static class EstateRisk
{
    public const int WarnDays = 30;
    public const int UrgentDays = 38;
    public const int CriticalDays = 40;
    public const int LimitDays = 45;

    /// <summary>Row colouring in the owned-realty list. Earlier than the ladder above on purpose: that list
    /// is a thing you go and look at, so it warms up long before anything is announced at you.</summary>
    public const int ListAmberDays = 15;
    public const int ListRedDays = 25;

    /// <summary>Days since the last visit we saw. Zero until one has been seen: with no baseline there is
    /// nothing to count from, and subtracting the default date yields two millennia of nonsense.</summary>
    public static int DaysAway(EstateRecord estate, DateTime nowUtc)
    {
        if (!estate.VisitObserved || estate.LastVisitUtc == default)
        {
            return 0;
        }
        var days = (int)Math.Floor((nowUtc - estate.LastVisitUtc).TotalDays);
        return days < 0 ? 0 : days;
    }

    /// <summary>0 nothing to say, 1 the game is warning by now, 2 getting short, 3 days from demolition.</summary>
    public static int Stage(int daysAway)
    {
        if (daysAway >= CriticalDays)
        {
            return 3;
        }
        if (daysAway >= UrgentDays)
        {
            return 2;
        }
        return daysAway >= WarnDays ? 1 : 0;
    }

    /// <summary>Days left before the game may demolish, floored at zero.</summary>
    public static int DaysLeft(int daysAway)
    {
        var left = LimitDays - daysAway;
        return left < 0 ? 0 : left;
    }

    public static bool AtRisk(EstateRecord estate, DateTime nowUtc)
        => estate.VisitObserved && DaysAway(estate, nowUtc) >= WarnDays;

    /// <summary>The record most worth shouting about, or null when nothing is.</summary>
    public static EstateRecord? Worst(IReadOnlyList<EstateRecord> estates, DateTime nowUtc)
    {
        EstateRecord? worst = null;
        var worstDays = 0;
        foreach (var estate in estates)
        {
            if (!AtRisk(estate, nowUtc))
            {
                continue;
            }
            var days = DaysAway(estate, nowUtc);
            if (worst is null || days > worstDays)
            {
                worst = estate;
                worstDays = days;
            }
        }
        return worst;
    }

    public static int AtRiskCount(IReadOnlyList<EstateRecord> estates, DateTime nowUtc)
    {
        var count = 0;
        foreach (var estate in estates)
        {
            if (AtRisk(estate, nowUtc))
            {
                count++;
            }
        }
        return count;
    }
}

/// <summary>Every house this install tracks, private and Free Company, with how long since it was entered.
/// Populated plugin-side from game memory; usable logged out, and current character first.</summary>
public interface IEstateWatch
{
    IReadOnlyList<EstateRecord> Estates { get; }

    /// <summary>The logged-in character's own private estate, or null when it has none or nobody is logged
    /// in. Never a Free Company house: this is the house the home teleport goes to.</summary>
    EstateRecord? Current { get; }

    /// <summary>Whether a home teleport can be offered, which means Lifestream is installed and has its
    /// command registered. False simply hides the button.</summary>
    bool CanTeleportHome { get; }

    /// <summary>Asks Lifestream to take this character home.</summary>
    void TeleportHome();

    /// <summary>Forgets one house and its day count. The watcher records it again, counting from nothing,
    /// the next time that character is logged in and still has the house.</summary>
    void Remove(ulong contentId, EstateKind kind);

    /// <summary>Bumped on every capture so the app can invalidate per-frame memos.</summary>
    int Version { get; }

    /// <summary>Drops any outstanding demolition warning, called when the player opens the app.</summary>
    void DismissWarnings();
}
