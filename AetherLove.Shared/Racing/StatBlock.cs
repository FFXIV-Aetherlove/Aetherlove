namespace AetherLove.Shared.Racing;

/// <summary>A racer's stat block, in the fixed stat order: Speed, Power, Stamina, Focus, Heart.
/// <paramref name="Element"/> is a <see cref="RacingElements.WheelOrder"/> string, or empty for
/// none. The order is the order the UI shows and the replay writes; it never varies.</summary>
public sealed record StatBlock(string Name, string Element, int Speed, int Power, int Stamina, int Focus, int Heart);

/// <summary>One entry in a race field: the stat block, unadorned.</summary>
public readonly record struct RaceRunner(string Name, StatBlock Stats, bool IsPlayer);
