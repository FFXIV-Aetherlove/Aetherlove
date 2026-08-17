namespace AetherLove.Shared.Arcade;

/// <summary>The games with server-tracked scores. Append-only: values are stored in the database.
/// Despite the name this is the score domain, not the arcade cabinet: the Aetherling pet games from 11
/// onward share the tables and leaderboards without being arcade apps.</summary>
public enum ArcadeGame : short
{
    Snake = 0,
    Stacker = 1,
    Breaker = 2,
    Meteor = 3,
    Invaders = 4,
    Muncher = 5,
    Plappy = 6,
    Sudoku = 7,
    Racooner = 8,
    SkySwarm = 9,
    Eordle = 10,
    CloudHop = 11,
    CrystalCatch = 12,
    HillRoll = 13,
}

/// <summary>Which leaderboard window to fetch.</summary>
public enum ArcadeBoard : short
{
    AllTime = 0,
    Weekly = 1,
}
