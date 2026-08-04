namespace AetherLove.Shared.Arcade;

/// <summary>The arcade games with server-tracked scores. Append-only: values are stored in the database.</summary>
public enum ArcadeGame : short
{
    Snake = 0,
    Stacker = 1,
    Breaker = 2,
    Meteor = 3,
    Invaders = 4,
    Muncher = 5,
}

/// <summary>Which leaderboard window to fetch.</summary>
public enum ArcadeBoard : short
{
    AllTime = 0,
    Weekly = 1,
}
