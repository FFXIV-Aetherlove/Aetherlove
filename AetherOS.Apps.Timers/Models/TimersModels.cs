using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Timers;

/// <summary>Every event kind the reminder engine can fire. APPEND-ONLY: persisted stamps key on the value.</summary>
public enum ReminderKind
{
    DailyReset = 0,
    GrandCompanyReset = 1,
    WeeklyReset = 2,
    FashionReportOpen = 3,
    CactpotDraw = 4,
    OceanBoarding = 5,
    VentureComplete = 6,
    FleetReturn = 7,
    CustomTimer = 8,
    CalendarEvent = 9,
}

/// <summary>The datacenter regions the game splits weekly draws across. Values match the Lumina
/// World.DataCenter Region row ids.</summary>
public enum GameRegion
{
    Japan = 1,
    NorthAmerica = 2,
    Europe = 3,
    Oceania = 4,
}

public sealed class ReminderEntry
{
    public bool Enabled { get; set; }

    /// <summary>Minutes before the event to fire; 0 fires at the instant. Sorted descending when persisted.</summary>
    public List<int> LeadMinutes { get; set; } = new();
}

/// <summary>Per-kind reminder settings plus the small knobs the Reminders screen owns.</summary>
public sealed class ReminderConfig
{
    public Dictionary<ReminderKind, ReminderEntry> Kinds { get; set; } = new();

    /// <summary>Manual cactpot region override; null follows the detected region.</summary>
    public GameRegion? CactpotRegionOverride { get; set; }

    public bool ShowDtr { get; set; }

    public ReminderEntry For(ReminderKind kind)
    {
        if (!Kinds.TryGetValue(kind, out var entry))
        {
            entry = new ReminderEntry();
            Kinds[kind] = entry;
        }
        return entry;
    }
}

public sealed class CustomTimer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime DueUtc { get; set; }
    public List<int> LeadMinutes { get; set; } = new();
}

/// <summary>A dated commitment another surface already knows about (venue RSVP today).</summary>
public sealed record TimersCommitment(Guid VenueId, string Name, DateTime WhenUtc);

public enum VesselKind
{
    Submersible = 0,
    Airship = 1,
}

/// <summary>One FC workshop vessel; ReturnUtc is MinValue when it is docked with no voyage.</summary>
public sealed class FleetVessel
{
    public VesselKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime ReturnUtc { get; set; }
}

/// <summary>One retainer's persisted snapshot; VentureName resolved at capture time.</summary>
public sealed class RetainerRow
{
    public ulong RetainerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint VentureId { get; set; }
    public string VentureName { get; set; } = string.Empty;
    public DateTime CompleteUtc { get; set; }
}

/// <summary>One character's persisted book: their retainers and, when they own one, their FC fleet.</summary>
public sealed class TimersCharacter
{
    public ulong ContentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public DateTime CapturedUtc { get; set; }
    public List<RetainerRow> Retainers { get; set; } = new();
    public List<FleetVessel> Fleet { get; set; } = new();
    public string FreeCompany { get; set; } = string.Empty;
    public DateTime FleetCapturedUtc { get; set; }
}
