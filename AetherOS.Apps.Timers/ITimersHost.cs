using System.Collections.Generic;
using System.Threading.Tasks;

namespace AetherOS.Apps.Timers;

/// <summary>The plugin-side reminder engine, ticking on the game framework so reminders fire while the app is
/// closed. The app reads and writes through this so the engine's in-memory caches never go stale.</summary>
public interface ITimersHost
{
    /// <summary>The cactpot draw region: the config override when set, else the last world the engine saw
    /// the player on, persisted so a logged-out phone keeps the right draw time.</summary>
    GameRegion CurrentRegion { get; }

    ReminderConfig GetReminderConfig();

    void SaveReminderConfig(ReminderConfig config);

    IReadOnlyList<CustomTimer> GetCustomTimers();

    void SaveCustomTimers(IReadOnlyList<CustomTimer> timers);

    /// <summary>The signed-in account's upcoming venue RSVPs; empty when offline or logged out.</summary>
    Task<IReadOnlyList<TimersCommitment>> GetCommitmentsAsync();
}
