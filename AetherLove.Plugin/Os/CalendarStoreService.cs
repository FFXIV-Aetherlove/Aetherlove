using System;
using System.Collections.Generic;
using System.Linq;
using AetherOS.Apps.Calendar;
using AetherOS.Sdk;

namespace AetherLove.Os;

/// <summary>The personal calendar events, owned plugin-side so the reminder sweep can fire alerts while
/// the Calendar app is closed. Reads and writes the same on-disk key the app always used, so existing
/// events load unchanged and an old build reading the new shape just ignores the alert field.</summary>
public sealed class CalendarStoreService : ICalendarStore
{
    private const string EventsKey = "events";

    private readonly IAppStorage _store;
    private readonly object _lock = new();
    private List<CalendarEvent>? _events;
    private int _version;

    public CalendarStoreService(AppStorageService storage)
    {
        _store = storage.For("calendar");
    }

    public IReadOnlyList<CalendarEvent> Events
    {
        get
        {
            lock (_lock)
            {
                LoadLocked();
                return _events!.OrderBy(e => e.StartUtc).ToList();
            }
        }
    }

    public int Version
    {
        get
        {
            lock (_lock)
            {
                return _version;
            }
        }
    }

    public CalendarEvent Add(string title, string note, DateTime startUtc, int? remindMinutesBefore)
    {
        lock (_lock)
        {
            LoadLocked();
            var existing = _events!.FirstOrDefault(e => e.Title == title && e.StartUtc == startUtc);
            if (existing is not null)
            {
                return existing;
            }
            var ev = new CalendarEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = title,
                Note = note,
                StartUtc = startUtc,
                RemindMinutesBefore = remindMinutesBefore,
            };
            _events!.Add(ev);
            PersistLocked();
            return ev;
        }
    }

    public void Update(CalendarEvent ev)
    {
        lock (_lock)
        {
            LoadLocked();
            var idx = _events!.FindIndex(e => e.Id == ev.Id);
            if (idx < 0)
            {
                return;
            }
            _events[idx] = ev;
            PersistLocked();
        }
    }

    public void Remove(string id)
    {
        lock (_lock)
        {
            LoadLocked();
            if (_events!.RemoveAll(e => e.Id == id) > 0)
            {
                PersistLocked();
            }
        }
    }

    /// <summary>Events whose alert instant (start minus lead) fell inside (prev, now]; the reminder
    /// sweep's direct feed.</summary>
    internal IReadOnlyList<CalendarEvent> AlertsDue(DateTime prevUtc, DateTime nowUtc)
    {
        lock (_lock)
        {
            LoadLocked();
            var due = new List<CalendarEvent>();
            foreach (var ev in _events!)
            {
                if (ev.RemindMinutesBefore is not { } lead)
                {
                    continue;
                }
                var fireAt = ev.StartUtc.AddMinutes(-lead);
                if (fireAt > prevUtc && fireAt <= nowUtc)
                {
                    due.Add(ev);
                }
            }
            return due;
        }
    }

    private void LoadLocked()
    {
        _events ??= _store.Get<List<CalendarEvent>>(EventsKey) ?? new List<CalendarEvent>();
    }

    private void PersistLocked()
    {
        _store.Set(EventsKey, _events);
        _version++;
    }
}
