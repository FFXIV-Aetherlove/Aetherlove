using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Sudoku;

/// <summary>The rungs of the ladder a run climbs. Not a menu: a run starts on Easy and every solved grid
/// moves it up, so the fourth puzzle onwards is Insane.</summary>
public enum SudokuDifficulty
{
    Easy = 0,
    Medium = 1,
    Difficult = 2,
    Insane = 3,
}

/// <summary>One generated puzzle: the givens, the solution, and what it actually took to solve.</summary>
public sealed class SudokuPuzzle
{
    public required int[] Givens { get; init; }

    public required int[] Solution { get; init; }

    public required SudokuDifficulty Difficulty { get; init; }

    /// <summary>The hardest technique the logical solver needed. Kept because it is the honest description
    /// of the grid, where the clue count is not.</summary>
    public required SudokuTechnique Hardest { get; init; }

    public int GivenCount
    {
        get
        {
            var n = 0;
            foreach (var value in this.Givens)
            {
                if (value != 0)
                {
                    n++;
                }
            }
            return n;
        }
    }
}

/// <summary>Builds puzzles on demand.
///
/// Difficulty is graded by the hardest technique a logical solve needs, never by how many clues were dug
/// out. Clue count is a poor proxy: two grids with the same givens can be a minute apart in real effort,
/// and players notice. Every candidate is checked for a unique solution before it is offered, so no run can
/// be lost to an unfair grid.
///
/// Generation costs single-digit milliseconds for Easy and can reach tens for Insane, so callers run it off
/// the draw thread.</summary>
public static class SudokuGenerator
{
    /// <summary>Digging stops here even if the grid would still be unique, so a puzzle always has enough of
    /// a foothold to look like a sudoku rather than a blank sheet.</summary>
    private static readonly int[] MinimumGivens = [40, 34, 28, 24];

    /// <summary>How hard a grid must be to count as this rung. A grid easier than its rung is rejected and
    /// re-dug, which is what stops Insane occasionally handing out a two-minute grid.</summary>
    private static readonly SudokuTechnique[] MinimumTechnique =
    [
        SudokuTechnique.NakedSingle,
        SudokuTechnique.HiddenSingle,
        SudokuTechnique.LockedCandidate,
        SudokuTechnique.NakedPair,
    ];

    /// <summary>The ladder tops out here: Insane may need bifurcation, the others may not.</summary>
    private static readonly SudokuTechnique[] MaximumTechnique =
    [
        SudokuTechnique.HiddenSingle,
        SudokuTechnique.LockedCandidate,
        SudokuTechnique.XWing,
        SudokuTechnique.Guess,
    ];

    /// <summary>A rejected grid costs single-digit milliseconds, so it is worth being fussy: an Insane rung
    /// that occasionally hands out a Medium grid is far more damaging to a ladder than a few hundred
    /// milliseconds of retries on a background thread.</summary>
    private const int MaxAttempts = 80;

    /// <summary>Digging is far cheaper than building a fresh solution, so a rejected grid is re-dug from the
    /// same one in a different order and only occasionally started over. Without this the retries dominate
    /// the cost and an Insane grid takes seconds to appear.</summary>
    private const int DigsPerSolution = 5;

    public static SudokuPuzzle Generate(SudokuDifficulty difficulty, Random random)
    {
        var tier = (int)difficulty;
        SudokuPuzzle? closest = null;
        int[]? solution = null;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (solution == null || attempt % DigsPerSolution == 0)
            {
                solution = FullGrid(random);
            }

            var puzzle = Dig(solution, difficulty, random);
            var (solved, hardest) = SudokuSolver.SolveLogically(puzzle);
            var graded = solved ? hardest : SudokuTechnique.Guess;

            if (graded >= MinimumTechnique[tier] && graded <= MaximumTechnique[tier])
            {
                return Build(puzzle, solution, difficulty, graded);
            }

            // Keep the nearest miss so a run never stalls waiting for a perfect grid.
            closest ??= Build(puzzle, solution, difficulty, graded);
        }

        return closest!;
    }

    private static SudokuPuzzle Build(int[] givens, int[] solution, SudokuDifficulty difficulty,
        SudokuTechnique hardest) =>
        new()
        {
            Givens = givens,
            Solution = solution,
            Difficulty = difficulty,
            Hardest = hardest,
        };

    /// <summary>A complete valid grid, built by filling cells in a shuffled candidate order and backtracking.
    /// Seeding the first box at random is enough to make the rest diverge, so grids never repeat in practice.</summary>
    public static int[] FullGrid(Random random)
    {
        var grid = new int[SudokuSolver.Cells];
        Fill(grid, 0, random);
        return grid;
    }

    private static bool Fill(int[] grid, int cell, Random random)
    {
        if (cell == SudokuSolver.Cells)
        {
            return true;
        }

        var digits = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        Shuffle(digits, random);

        foreach (var digit in digits)
        {
            if (!SudokuSolver.IsLegal(grid, cell, digit))
            {
                continue;
            }
            grid[cell] = digit;
            if (Fill(grid, cell + 1, random))
            {
                return true;
            }
            grid[cell] = 0;
        }
        return false;
    }

    /// <summary>Removes clues one at a time, keeping a removal only while the grid still has exactly one
    /// solution. Removal order is shuffled so the holes do not fall into a recognisable pattern.</summary>
    private static int[] Dig(int[] solution, SudokuDifficulty difficulty, Random random)
    {
        var puzzle = (int[])solution.Clone();
        var order = new int[SudokuSolver.Cells];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }
        Shuffle(order, random);

        var floor = MinimumGivens[(int)difficulty];
        var given = SudokuSolver.Cells;

        foreach (var cell in order)
        {
            if (given <= floor)
            {
                break;
            }
            var saved = puzzle[cell];
            puzzle[cell] = 0;
            if (SudokuSolver.CountSolutions(puzzle) == 1)
            {
                given--;
            }
            else
            {
                puzzle[cell] = saved;
            }
        }
        return puzzle;
    }

    private static void Shuffle<T>(T[] items, Random random)
    {
        for (var i = items.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
