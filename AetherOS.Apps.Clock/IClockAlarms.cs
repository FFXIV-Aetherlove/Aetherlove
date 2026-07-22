using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Clock;

public enum ClockTimerState
{
    Pending,
    Ringing,
}

/// <summary>A read-only snapshot of one timer for the Clock UI; <see cref="Remaining"/> is engine-computed
/// against the current time and floored at zero.</summary>
public sealed record ClockTimerView(
    Guid Id,
    string Label,
    TimeSpan Duration,
    DateTime DueUtc,
    ClockTimerState State,
    TimeSpan Remaining,
    bool IsAlarm);

/// <summary>The Clock app's countdown timers plus the shared alarm settings, owned by a plugin-side engine that
/// ticks on the game framework so timers fire even while the app is closed. The pure-SDK Clock app drives it all
/// through this interface and never touches the game, storage, or the sound API directly.</summary>
public interface IClockAlarms
{
    /// <summary>Ringing timers first, then pending ordered by soonest due.</summary>
    IReadOnlyList<ClockTimerView> Timers { get; }

    /// <summary>Number of timers currently ringing; drives the Clock tile badge.</summary>
    int RingingCount { get; }

    Guid AddTimer(string label, TimeSpan duration);

    /// <summary>Adds a one-shot alarm that fires at the next local occurrence of hour:minute.</summary>
    Guid AddAlarm(int hour, int minute, string label);

    /// <summary>Cancels a pending timer or dismisses a ringing one.</summary>
    void RemoveTimer(Guid id);

    /// <summary>Silences every timer that is currently ringing.</summary>
    void DismissAllAlarms();

    /// <summary>Chosen alarm sound, 1..16 == the game's chat sound effects &lt;se.1&gt;..&lt;se.16&gt;; clamped.</summary>
    int AlarmSoundId { get; set; }

    /// <summary>Print a line in the native game chat log when a timer fires.</summary>
    bool NotifyInChat { get; set; }

    /// <summary>Auto-stop a ringing alarm after this many minutes; 0 = ring until dismissed. Only 0/1/3/5.</summary>
    int AutoOffMinutes { get; set; }

    /// <summary>Play the given sound once so the user can hear their pick.</summary>
    void PreviewSound(int seId);
}
