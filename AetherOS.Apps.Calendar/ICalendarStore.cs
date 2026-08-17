using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Calendar;

/// <summary>One personal calendar event. RemindMinutesBefore null means no alert; 0 alerts at the start
/// instant.</summary>
public sealed class CalendarEvent
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public int? RemindMinutesBefore { get; set; }
}

/// <summary>The personal-event store, owned plugin-side so alerts can fire while the Calendar app is closed.
/// Backed by the same on-disk key the app always used, so existing events survive unchanged.</summary>
public interface ICalendarStore
{
    IReadOnlyList<CalendarEvent> Events { get; }

    /// <summary>Bumped on every mutation so the app can invalidate cached agendas.</summary>
    int Version { get; }

    /// <summary>Adds unless an event with the same title and start already exists; returns the stored event.</summary>
    CalendarEvent Add(string title, string note, DateTime startUtc, int? remindMinutesBefore);

    void Update(CalendarEvent ev);

    void Remove(string id);
}
