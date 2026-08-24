namespace AetherLove.Shared.Wayfinder;

/// <summary>Outcome band of one position submission. Only the band ever crosses the wire; the server keeps
/// target coordinates and numeric distances to itself.</summary>
public enum WayfinderVerdict : short
{
    WrongZone = 0,
    Far = 1,
    Close = 2,
    VeryClose = 3,
    Found = 4,
}

public enum WayfinderAssignmentStatus : short
{
    Active = 0,
    Found = 1,
    Expired = 2,
    /// <summary>The player gave up before the timer ran out; the start is still spent.</summary>
    Abandoned = 3,
}

/// <summary>A party hunt's lifecycle. Append-only: values are stored in the database.</summary>
public enum WayfinderRunStatus : short
{
    Gathering = 0,
    Active = 1,
    Completed = 2,
    Expired = 3,
    Cancelled = 4,
}
