using System.Collections.Generic;

namespace AetherOS.Apps.Timers;

/// <summary>Persisted retainer and FC fleet books for every character seen on this install, captured
/// plugin-side from game memory. Current character first; usable logged out.</summary>
public interface ITimersRetainers
{
    IReadOnlyList<TimersCharacter> Characters { get; }

    /// <summary>Bumped on every capture so the app can invalidate per-second memos.</summary>
    int Version { get; }
}
