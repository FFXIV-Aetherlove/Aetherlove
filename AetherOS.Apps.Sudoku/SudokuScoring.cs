using System;

namespace AetherOS.Apps.Sudoku;

/// <summary>What a finished puzzle was worth, kept apart so the numbers can be read and tuned in one place
/// rather than hunted through the game loop.</summary>
public readonly record struct PuzzleScore(int Base, int TimeBonus, int MistakePenalty, float Integrity, int Total);

/// <summary>The score model.
///
/// Three things it deliberately does. It pays mostly for DIFFICULTY, so climbing the ladder beats grinding
/// easy grids. It pays for speed only down to a floor, because past that point nobody is solving, they are
/// typing. And it scales the whole thing by how much of the solve was actually deducible from the board at
/// the moment each digit went in.
///
/// That last term is the honest one. Advanced technique never places digits, it removes candidates until a
/// single appears, so a genuine solve at any difficulty commits almost nothing but singles. Filling cells
/// that still had several candidates is the signature of an answer that came from somewhere other than the
/// grid. It is a multiplier rather than a gate, so an unusual but honest solve loses a little and never a
/// run.</summary>
public static class SudokuScoring
{
    /// <summary>Paid for finishing a grid at each rung. The jumps are steep on purpose: reaching Insane once
    /// should beat clearing Easy five times.</summary>
    private static readonly int[] BasePoints = [100, 250, 600, 1500];

    /// <summary>How long a rung allows before the run ends. It grows in absolute terms, because a harder grid
    /// genuinely takes longer, but far more slowly than the difficulty does, so the clock bites hardest at
    /// the top where the leaderboard is decided.</summary>
    private static readonly int[] LimitSeconds = [360, 480, 660, 900];

    /// <summary>Below this, a solve is not credible: even transcribing a finished grid means entering roughly
    /// fifty digits. Time faster than this earns no more bonus than the floor itself, so there is nothing to
    /// win by racing past the point of plausibility. The server mirrors these in ArcadeScoreChecker.</summary>
    private static readonly int[] FloorSeconds = [40, 75, 140, 240];

    /// <summary>Speed is worth at most this share of the base on top.</summary>
    private const float TimeBonusShare = 0.5f;

    /// <summary>Integrity scales the result across this band. Narrow by design: wide enough that transcribing
    /// costs real points, tight enough that it never decides a run on its own.</summary>
    private const float IntegrityFloor = 0.75f;
    private const float IntegrityCeiling = 1.25f;

    /// <summary>Each strike taken on a grid costs this share of that grid's award.</summary>
    private const float MistakeShare = 0.1f;

    public static int LimitFor(SudokuDifficulty difficulty) => LimitSeconds[(int)difficulty];

    public static int FloorFor(SudokuDifficulty difficulty) => FloorSeconds[(int)difficulty];

    public static int BaseFor(SudokuDifficulty difficulty) => BasePoints[(int)difficulty];

    /// <summary>Grids to clear on a rung before the ladder moves up. Two rather than one so a run gets to
    /// settle into each tier: escalating on every clear put Insane in front of the player by the fourth grid,
    /// which almost nobody survives.</summary>
    public const int GridsPerRung = 2;

    /// <summary>The rung a run is on after clearing <paramref name="solved"/> grids: two Easy, two Medium,
    /// two Difficult, then Insane for as long as the run lasts.</summary>
    public static SudokuDifficulty LadderAt(int solved) =>
        (SudokuDifficulty)Math.Min(solved / GridsPerRung, (int)SudokuDifficulty.Insane);

    public static PuzzleScore Score(SudokuDifficulty difficulty, double seconds, int mistakes, float integrity)
    {
        var tier = (int)difficulty;
        var basePoints = BasePoints[tier];
        var limit = LimitSeconds[tier];

        // Clamping to the floor is what removes the reward for impossible speed: everything quicker scores
        // exactly the same as an honest fast solve.
        var counted = Math.Clamp(seconds, FloorSeconds[tier], limit);
        var remaining = (float)((limit - counted) / (limit - FloorSeconds[tier]));
        var timeBonus = (int)MathF.Round(basePoints * TimeBonusShare * Math.Clamp(remaining, 0f, 1f));

        var penalty = (int)MathF.Round((basePoints + timeBonus) * MistakeShare * Math.Max(0, mistakes));
        var scale = IntegrityFloor + ((IntegrityCeiling - IntegrityFloor) * Math.Clamp(integrity, 0f, 1f));
        var total = Math.Max(0, (int)MathF.Round((basePoints + timeBonus - penalty) * scale));

        return new PuzzleScore(basePoints, timeBonus, penalty, integrity, total);
    }
}
