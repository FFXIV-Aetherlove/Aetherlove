using System;
using System.Threading.Tasks;

namespace AetherOS.Apps.Sudoku;

/// <summary>How the current grid ended, so the app knows what to draw next.</summary>
public enum SudokuOutcome
{
    Playing,
    Solved,
    OutOfTime,
    OutOfStrikes,
}

/// <summary>A run: a ladder of grids climbing Easy to Insane, three strikes, and a clock per grid.
///
/// The next grid is generated in the background while the current one is being played, because digging an
/// Insane puzzle means thousands of uniqueness checks and doing that on the draw thread would hitch the
/// game client. The very first grid is the only one anybody waits for.</summary>
public sealed class SudokuGame
{
    public const int MaxStrikes = 3;

    private readonly Random random = new();

    private Task<SudokuPuzzle>? pending;
    private int[] board = new int[SudokuSolver.Cells];
    private bool[] locked = new bool[SudokuSolver.Cells];
    private int[] marks = new int[SudokuSolver.Cells];

    private int placements;
    private int deduced;
    private int puzzleMistakes;

    public SudokuPuzzle? Puzzle { get; private set; }

    public int Solved { get; private set; }

    public int Strikes { get; private set; }

    public int Score { get; private set; }

    public double PuzzleSeconds { get; private set; }

    public double RunSeconds { get; private set; }

    public SudokuOutcome Outcome { get; private set; } = SudokuOutcome.Playing;

    /// <summary>The rung the run is on, which is also the rung the next grid will be dug at.</summary>
    public SudokuDifficulty Difficulty => SudokuScoring.LadderAt(this.Solved);

    /// <summary>Highest rung reached, for the leaderboard's second metric.</summary>
    public SudokuDifficulty Peak { get; private set; }

    public bool Ready => this.Puzzle != null;

    /// <summary>Share of placements that were visibly deducible when made. Starts optimistic so an empty
    /// grid is not scored as suspicious.</summary>
    public float Integrity => this.placements == 0 ? 1f : (float)this.deduced / this.placements;

    public double SecondsLeft =>
        Math.Max(0.0, SudokuScoring.LimitFor(this.Difficulty) - this.PuzzleSeconds);

    public int this[int cell] => this.board[cell];

    public bool IsGiven(int cell) => this.locked[cell];

    public int MarksAt(int cell) => this.marks[cell];

    public void Start()
    {
        this.Solved = 0;
        this.Strikes = 0;
        this.Score = 0;
        this.RunSeconds = 0.0;
        this.Peak = SudokuDifficulty.Easy;
        this.Outcome = SudokuOutcome.Playing;
        this.Puzzle = null;
        this.pending = GenerateAsync(SudokuDifficulty.Easy);
    }

    private Task<SudokuPuzzle> GenerateAsync(SudokuDifficulty difficulty)
    {
        var seed = this.random.Next();
        return Task.Run(() => SudokuGenerator.Generate(difficulty, new Random(seed)));
    }

    /// <summary>Adopts the generated grid once it is ready. Called every frame; cheap until the task finishes.</summary>
    public void Poll()
    {
        if (this.Puzzle != null || this.pending is not { IsCompletedSuccessfully: true } task)
        {
            return;
        }

        this.Puzzle = task.Result;
        this.pending = null;
        this.board = (int[])this.Puzzle.Givens.Clone();
        this.locked = new bool[SudokuSolver.Cells];
        this.marks = new int[SudokuSolver.Cells];
        for (var cell = 0; cell < SudokuSolver.Cells; cell++)
        {
            this.locked[cell] = this.board[cell] != 0;
        }
        this.placements = 0;
        this.deduced = 0;
        this.puzzleMistakes = 0;
        this.PuzzleSeconds = 0.0;
    }

    public void Tick(double delta)
    {
        if (this.Outcome != SudokuOutcome.Playing)
        {
            return;
        }

        Poll();
        if (this.Puzzle == null)
        {
            return;
        }

        this.RunSeconds += delta;
        this.PuzzleSeconds += delta;
        if (this.PuzzleSeconds >= SudokuScoring.LimitFor(this.Difficulty))
        {
            this.Outcome = SudokuOutcome.OutOfTime;
        }
    }

    /// <summary>Writes a digit. Returns false when the digit was wrong, which costs a strike; the cell is
    /// left empty rather than showing a wrong answer, so the board always reads as truth.</summary>
    public bool Place(int cell, int digit)
    {
        if (this.Outcome != SudokuOutcome.Playing || this.Puzzle == null
            || this.locked[cell] || digit is < 1 or > 9)
        {
            return false;
        }

        if (this.Puzzle.Solution[cell] != digit)
        {
            this.Strikes++;
            this.puzzleMistakes++;
            if (this.Strikes >= MaxStrikes)
            {
                this.Outcome = SudokuOutcome.OutOfStrikes;
            }
            return false;
        }

        // Judged BEFORE the digit lands, or the cell it asks about is already filled.
        if (SudokuSolver.WasDeducible(this.board, cell))
        {
            this.deduced++;
        }
        this.placements++;

        this.board[cell] = digit;
        this.marks[cell] = 0;
        ClearPeerMarks(cell, digit);

        if (SudokuSolver.IsComplete(this.board))
        {
            CompletePuzzle();
        }
        return true;
    }

    public void Clear(int cell)
    {
        if (this.Outcome == SudokuOutcome.Playing && !this.locked[cell])
        {
            this.board[cell] = 0;
            this.marks[cell] = 0;
        }
    }

    /// <summary>Toggles a pencil mark. Marks are notes, never judged: they carry no score either way.</summary>
    public void ToggleMark(int cell, int digit)
    {
        if (this.Outcome != SudokuOutcome.Playing || this.locked[cell]
            || this.board[cell] != 0 || digit is < 1 or > 9)
        {
            return;
        }
        this.marks[cell] ^= 1 << (digit - 1);
    }

    private void ClearPeerMarks(int cell, int digit)
    {
        var bit = 1 << (digit - 1);
        foreach (var peer in SudokuSolver.PeersOf(cell))
        {
            this.marks[peer] &= ~bit;
        }
    }

    private void CompletePuzzle()
    {
        // Captured before the count moves on: Difficulty is derived from Solved, so reading it afterwards
        // would record the rung this clear UNLOCKED rather than the one it actually beat.
        var cleared = this.Difficulty;
        var award = SudokuScoring.Score(cleared, this.PuzzleSeconds, this.puzzleMistakes, this.Integrity);
        this.Score += award.Total;
        this.Solved++;
        if (cleared > this.Peak)
        {
            this.Peak = cleared;
        }

        this.Outcome = SudokuOutcome.Solved;
        this.Puzzle = null;
        this.pending = GenerateAsync(this.Difficulty);
    }

    /// <summary>Moves on to the grid that was being dug while the last one was played.</summary>
    public void Continue()
    {
        if (this.Outcome == SudokuOutcome.Solved)
        {
            this.Outcome = SudokuOutcome.Playing;
        }
    }

    /// <summary>The digits still unplaced, so the pad can grey out a digit that is fully used.</summary>
    public int RemainingOf(int digit)
    {
        var used = 0;
        foreach (var value in this.board)
        {
            if (value == digit)
            {
                used++;
            }
        }
        return SudokuSolver.Size - used;
    }
}
