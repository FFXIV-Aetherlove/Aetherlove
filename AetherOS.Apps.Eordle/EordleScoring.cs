using System;

namespace AetherOS.Apps.Eordle;

/// <summary>The score model: a solved word pays by how few guesses it took, plus a small linear speed
/// bonus. The server's ArcadeScoreChecker mirrors every number here; the two must track together.</summary>
public static class EordleScoring
{
    /// <summary>Paid for a solve, indexed by guesses used minus one. Nailing it on the first row is rare
    /// enough to be worth five times the last-gasp sixth.</summary>
    public static readonly int[] GuessPoints = [500, 400, 300, 200, 150, 100];

    /// <summary>Speed pays at most this much, falling linearly from an instant solve to nothing at
    /// <see cref="FastSeconds"/> per word.</summary>
    public const int SpeedBonusMax = 50;
    public const double FastSeconds = 20.0;

    /// <summary>First-guess points plus a full speed bonus.</summary>
    public const int MaxPointsPerWord = 550;

    public static int SpeedBonus(double wordSeconds) =>
        (int)Math.Round(SpeedBonusMax * Math.Clamp(1.0 - (wordSeconds / FastSeconds), 0.0, 1.0));

    public static int WordPoints(int guessesUsed, double wordSeconds)
    {
        var index = Math.Clamp(guessesUsed - 1, 0, GuessPoints.Length - 1);
        return Math.Min(MaxPointsPerWord, GuessPoints[index] + SpeedBonus(wordSeconds));
    }
}
