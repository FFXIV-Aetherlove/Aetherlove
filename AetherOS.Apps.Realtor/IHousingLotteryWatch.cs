using System;

namespace AetherOS.Apps.Realtor;

/// <summary>The player's own lottery entry, as the game's Timers window reported it.</summary>
public sealed record HousingLotteryEntry(
    int Plot,
    int Ward,
    string District,
    string Size,
    int Number,
    string EntryType,
    DateTimeOffset CapturedAt);

/// <summary>The current housing-lottery entry, if one has been seen. There is no passive source for this in
/// the game client, so the host can only report what it read the last time the player opened the Timers
/// window; a null here means "not seen this cycle", never "no entry exists".</summary>
public interface IHousingLotteryWatch
{
    HousingLotteryEntry? Current { get; }

    /// <summary>Forgets the stored entry, for when the player says it is no longer theirs.</summary>
    void Clear();
}
