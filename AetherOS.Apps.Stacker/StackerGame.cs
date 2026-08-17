using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Stacker;

/// <summary>The pure game model: a well, the seven tetrominoes, gravity, line clears and scoring. No
/// drawing and no ImGui. Time arrives as a delta and the piece falls on a fixed interval that shortens
/// per level, which is the whole difficulty curve.</summary>
internal sealed class StackerGame
{
    public const int Columns = 10;
    public const int Rows = 20;

    /// <summary>Classic line scoring, multiplied by the level.</summary>
    private static readonly int[] LineScores = [0, 100, 300, 500, 800];
    /// <summary>T-spin scoring by lines cleared (0..3), multiplied by the level.</summary>
    private static readonly int[] MiniTSpinScores = [100, 200, 400];
    private static readonly int[] RegularTSpinScores = [400, 800, 1200, 1600];
    /// <summary>Combo bonus: 50 points per combo count, times the current level.</summary>
    private const int ComboPointsPerCombo = 50;
    private const double BackToBackMultiplier = 1.5;
    private const int IKind = 0;
    private const int OKind = 3;
    private const int TKind = 5;
    private const int SoftDropPointsPerCell = 1;
    private const int HardDropPointsPerCell = 2;
    private const int LinesPerLevel = 10;
    /// <summary>Grace period after any lock (hard drop or normal) during which hard drop is ignored, so a
    /// fast double-tap can't insta-place the next piece by accident right after the previous one lands.</summary>
    public const double HardDropLockoutSeconds = 0.15;
    /// <summary>Pieces spawn fully above the visible field, on guideline rows 21 and 22.</summary>
    private const int SpawnRow = -1;

    /// <summary>The I piece's four rotation states as literal cell lists: it has no mino-centred pivot
    /// to rotate true, so (like every SRS implementation) it just uses a fixed row/column per state.</summary>
    private static readonly (int X, int Y)[][] ICells =
    [
        [(0, 1), (1, 1), (2, 1), (3, 1)],
        [(2, 0), (2, 1), (2, 2), (2, 3)],
        [(0, 2), (1, 2), (2, 2), (3, 2)],
        [(1, 0), (1, 1), (1, 2), (1, 3)],
    ];

    /// <summary>The O piece never rotates (per SRS), so it only ever needs one cell list.</summary>
    private static readonly (int X, int Y)[] OCells = [(1, 0), (2, 0), (1, 1), (2, 1)];

    /// <summary>J/L/S/T/Z's other three cells at spawn, as (dx, dy) offsets from their pivot mino at
    /// local (1, 1). True rotation just rotates these offsets around the pivot, which itself never
    /// moves -- unlike rotating the whole 4x4 box, which lets the pivot drift between states.</summary>
    private static readonly (int Dx, int Dy)[][] PivotOffsets = new (int Dx, int Dy)[7][];

    /// <summary>SRS wall kick data (tests 1..5) for J/L/S/T/Z, straight from
    /// https://tetris.wiki/Super_Rotation_System as cited by github.com/SamillWong/PPTetris, in that
    /// table's own (Dy, Dx) form. Indexed by the state rotated FROM (0, R, 2, L) for a clockwise turn.
    /// PPTetris applies these by SUBTRACTING Dy and ADDING Dx to the board position (its row index also
    /// grows downward, like ours) -- <see cref="BuildKicks"/> mirrors that exact convention instead of
    /// re-deriving offsets by hand, since a hand derivation's sign was the source of a real bug.</summary>
    private static readonly (int Dy, int Dx)[][] StandardKicksCw =
    [
        [(0, 0), (0, -1), (1, -1), (-2, 0), (-2, -1)],
        [(0, 0), (0, 1), (-1, 1), (2, 0), (2, 1)],
        [(0, 0), (0, 1), (1, 1), (-2, 0), (-2, 1)],
        [(0, 0), (0, -1), (-1, -1), (2, 0), (2, -1)],
    ];

    /// <summary>SRS wall kick data for the I piece, same source and (Dy, Dx) form as <see cref="StandardKicksCw"/>.</summary>
    private static readonly (int Dy, int Dx)[][] IKicksCw =
    [
        [(0, 0), (0, -2), (0, 1), (-1, -2), (2, 1)],
        [(0, 0), (0, -1), (0, 2), (2, -1), (-1, 2)],
        [(0, 0), (0, 2), (0, -1), (1, 2), (-2, -1)],
        [(0, 0), (0, 1), (0, -2), (-2, 1), (1, -2)],
    ];

    private static readonly (int Dx, int Dy)[] OKick = [(0, 0)];

    /// <summary>The two corners on the side the T's point faces, per rotation (0=up, 1=right, 2=down, 3=left).</summary>
    private static readonly (int Dx, int Dy)[][] TFrontCorners =
    [
        [(-1, -1), (1, -1)],
        [(1, -1), (1, 1)],
        [(-1, 1), (1, 1)],
        [(-1, -1), (-1, 1)],
    ];

    /// <summary>The two corners on the T's flat, opposite side, per rotation.</summary>
    private static readonly (int Dx, int Dy)[][] TBackCorners =
    [
        [(-1, 1), (1, 1)],
        [(-1, -1), (-1, 1)],
        [(-1, -1), (1, -1)],
        [(1, -1), (1, 1)],
    ];

    private readonly int[,] well = new int[Columns, Rows];
    private readonly Random rng = new();
    private readonly List<int> bag = [];

    static StackerGame()
    {
        // Each piece's other three cells minus its pivot at local (1, 1), read off SpawnShapes by hand once.
        PivotOffsets[IKind] = [];
        PivotOffsets[1] = [(-1, -1), (-1, 0), (1, 0)]; // J
        PivotOffsets[2] = [(1, -1), (-1, 0), (1, 0)]; // L
        PivotOffsets[OKind] = [];
        PivotOffsets[4] = [(0, -1), (1, -1), (-1, 0)]; // S
        PivotOffsets[TKind] = [(0, -1), (-1, 0), (1, 0)]; // T
        PivotOffsets[6] = [(-1, -1), (0, -1), (1, 0)]; // Z
    }

    private double fallAccumulator;
    private double lockElapsed;
    private double hardDropLockout;
    private bool lastMoveWasRotation;
    private int combo = -1;
    private bool backToBack;

    public int PieceKind { get; private set; }

    public int PieceRotation { get; private set; }

    public int PieceX { get; private set; }

    public int PieceY { get; private set; }

    public int NextKind { get; private set; }

    public int HeldKind { get; private set; } = -1;

    public bool CanHold { get; private set; }

    public int Score { get; private set; }

    public int Lines { get; private set; }

    public int Level => (this.Lines / LinesPerLevel) + 1;

    public bool Dead { get; private set; }

    /// <summary>Rows cleared by the last lock, for the renderer's flash.</summary>
    public int LastCleared { get; private set; }

    /// <summary>Incremented every time a piece locks, so the UI can notice a new lock even without a clear.</summary>
    public int LockCount { get; private set; }

    /// <summary>Rows cleared by the last lock (pre-collapse row indices), for the renderer's clear flash.</summary>
    public IReadOnlyList<int> LastClearedRows { get; private set; } = [];

    /// <summary>Incremented on every hard drop that actually executes, so the UI can notice a fresh drop
    /// even when it lands on the same row it started (zero-height trail).</summary>
    public int HardDropCount { get; private set; }

    public int LastHardDropKind { get; private set; }

    public int LastHardDropRotation { get; private set; }

    public int LastHardDropX { get; private set; }

    /// <summary>The dropped piece's row before it fell, for the renderer's drop-trail flash.</summary>
    public int LastHardDropStartY { get; private set; }

    /// <summary>The dropped piece's row after it landed, for the renderer's drop-trail flash.</summary>
    public int LastHardDropEndY { get; private set; }

    /// <summary>Facts about the most recent lock, for the UI's score feedback line.</summary>
    public bool LastClearIsTSpin { get; private set; }

    public bool LastClearIsMini { get; private set; }

    /// <summary>True when the back-to-back multiplier was actually applied to the most recent clear.</summary>
    public bool LastClearIsBackToBack { get; private set; }

    /// <summary>The combo count at the most recent clear (0 = first in a streak), or -1 if nothing cleared.</summary>
    public int LastClearCombo { get; private set; } = -1;

    /// <summary>True when a cell of the settled stack is filled.</summary>
    public bool Filled(int x, int y) => this.well[x, y] != 0;

    /// <summary>The piece kind settled at a cell, or -1 if empty, for skinned rendering.</summary>
    public int KindAt(int x, int y) => this.well[x, y] - 1;

    /// <summary>The four cells of a piece at a given position and rotation. J/L/S/T/Z rotate their spawn
    /// offsets true around a fixed pivot mino (so it never drifts); I and O just pick a state's fixed
    /// cell list, since neither has a pivot mino to rotate around.</summary>
    public static IEnumerable<(int X, int Y)> Cells(int kind, int rotation, int originX, int originY)
    {
        rotation = ((rotation % 4) + 4) % 4;
        if (kind == IKind)
        {
            foreach (var (x, y) in ICells[rotation])
            {
                yield return (originX + x, originY + y);
            }
            yield break;
        }
        if (kind == OKind)
        {
            foreach (var (x, y) in OCells)
            {
                yield return (originX + x, originY + y);
            }
            yield break;
        }
        var pivotX = originX + 1;
        var pivotY = originY + 1;
        yield return (pivotX, pivotY);
        foreach (var offset in PivotOffsets[kind])
        {
            var (dx, dy) = offset;
            for (var step = 0; step < rotation; step++)
            {
                (dx, dy) = (-dy, dx);
            }
            yield return (pivotX + dx, pivotY + dy);
        }
    }

    public IEnumerable<(int X, int Y)> CurrentCells() =>
        Cells(this.PieceKind, this.PieceRotation, this.PieceX, this.PieceY);

    /// <summary>Where the current piece would land, for the drop shadow.</summary>
    public IEnumerable<(int X, int Y)> GhostCells()
    {
        var y = this.PieceY;
        while (Fits(this.PieceKind, this.PieceRotation, this.PieceX, y + 1))
        {
            y++;
        }
        return Cells(this.PieceKind, this.PieceRotation, this.PieceX, y);
    }

    public void Reset()
    {
        Array.Clear(this.well);
        this.bag.Clear();
        this.Score = 0;
        this.Lines = 0;
        this.Dead = false;
        this.LastCleared = 0;
        this.HeldKind = -1;
        this.CanHold = true;
        this.fallAccumulator = 0;
        this.hardDropLockout = 0;
        this.lastMoveWasRotation = false;
        this.combo = -1;
        this.backToBack = false;
        this.LockCount = 0;
        this.LastClearedRows = [];
        this.HardDropCount = 0;
        this.LastClearIsTSpin = false;
        this.LastClearIsMini = false;
        this.LastClearIsBackToBack = false;
        this.LastClearCombo = -1;
        this.NextKind = TakeFromBag();
        Spawn();
    }

    public void Tick(double deltaSeconds)
    {
        if (this.Dead)
        {
            return;
        }
        // A long stall must not drop the piece through several rows at once.
        var dt = Math.Min(deltaSeconds, 0.5);
        if (this.hardDropLockout > 0)
        {
            this.hardDropLockout = Math.Max(0, this.hardDropLockout - dt);
        }
        if (!Fits(this.PieceKind, this.PieceRotation, this.PieceX, this.PieceY + 1))
        {
            // Resting on the stack: give the player one fall-interval's worth of grace to slide
            // or rotate it before it locks, rather than locking the instant it touches down.
            this.lockElapsed += dt;
            if (this.lockElapsed >= FallInterval)
            {
                Lock();
                this.fallAccumulator = 0;
                this.lockElapsed = 0;
            }
            return;
        }
        this.lockElapsed = 0;
        this.fallAccumulator += dt;
        var interval = FallInterval;
        while (!this.Dead && this.fallAccumulator >= interval)
        {
            this.fallAccumulator -= interval;
            StepDown(scored: false);
        }
    }

    /// <summary>Gravity interval for the current level, from a leisurely start to a twitchy floor.</summary>
    private double FallInterval => Math.Max(0.08, 0.75 - ((this.Level - 1) * 0.028));

    public void MoveLeft() => TryMove(-1);

    public void MoveRight() => TryMove(1);

    /// <summary>One row of player-driven drop, worth a point.</summary>
    public void SoftDrop()
    {
        if (!this.Dead)
        {
            this.fallAccumulator = 0;
            StepDown(scored: true);
        }
    }

    /// <summary>Slams the piece to its landing spot and locks it immediately. Ignored during the brief
    /// post-lock lockout, so a fast double-tap can't insta-place the next piece by accident.</summary>
    public void HardDrop()
    {
        if (this.Dead || this.hardDropLockout > 0)
        {
            return;
        }
        var startY = this.PieceY;
        var dropped = 0;
        while (Fits(this.PieceKind, this.PieceRotation, this.PieceX, this.PieceY + 1))
        {
            this.PieceY++;
            dropped++;
        }
        if (dropped > 0)
        {
            this.lastMoveWasRotation = false;
        }
        this.Score += dropped * HardDropPointsPerCell;
        this.HardDropCount++;
        this.LastHardDropKind = this.PieceKind;
        this.LastHardDropRotation = this.PieceRotation;
        this.LastHardDropX = this.PieceX;
        this.LastHardDropStartY = startY;
        this.LastHardDropEndY = this.PieceY;
        Lock();
    }

    public void Hold()
    {
        if (this.Dead || !this.CanHold)
        {
            return;
        }

        var current = this.PieceKind;
        if (this.HeldKind < 0)
        {
            this.HeldKind = current;
            Spawn();
        }
        else
        {
            this.PieceKind = this.HeldKind;
            this.HeldKind = current;
            this.PieceRotation = 0;
            this.PieceX = (Columns / 2) - 2;
            this.PieceY = SpawnRow;
            if (!Fits(this.PieceKind, this.PieceRotation, this.PieceX, 0))
            {
                this.Dead = true;
            }
        }
        this.CanHold = false;
    }

    public void Rotate() => RotateBy(1);

    public void RotateLeft() => RotateBy(-1);

    private void RotateBy(int direction)
    {
        if (this.Dead)
        {
            return;
        }
        var next = ((this.PieceRotation + direction) % 4 + 4) % 4;
        foreach (var (dx, dy) in BuildKicks(this.PieceKind, this.PieceRotation, direction))
        {
            if (Fits(this.PieceKind, next, this.PieceX + dx, this.PieceY + dy))
            {
                this.PieceRotation = next;
                this.PieceX += dx;
                this.PieceY += dy;
                this.lastMoveWasRotation = true;
                return;
            }
        }
    }

    /// <summary>The wall kicks for rotating from <paramref name="fromState"/> by <paramref name="direction"/>
    /// (+1 clockwise, -1 counter-clockwise). Clockwise kicks come straight from the reference tables,
    /// converted from their (Dy, Dx) form the same way PPTetris applies them: negate Dy, keep Dx as-is.
    /// Counter-clockwise reuses the clockwise transition that leads INTO fromState, negated again.</summary>
    private static (int Dx, int Dy)[] BuildKicks(int kind, int fromState, int direction)
    {
        if (kind == OKind)
        {
            return OKick;
        }
        var table = kind == IKind ? IKicksCw : StandardKicksCw;
        var cwFrom = direction > 0 ? fromState : ((fromState - 1) % 4 + 4) % 4;
        var raw = table[cwFrom];
        var flip = direction > 0 ? 1 : -1;
        var kicks = new (int Dx, int Dy)[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            kicks[i] = (flip * raw[i].Dx, flip * -raw[i].Dy);
        }
        return kicks;
    }

    private void TryMove(int dx)
    {
        if (!this.Dead && Fits(this.PieceKind, this.PieceRotation, this.PieceX + dx, this.PieceY))
        {
            this.PieceX += dx;
            this.lastMoveWasRotation = false;
        }
    }

    private void StepDown(bool scored)
    {
        if (Fits(this.PieceKind, this.PieceRotation, this.PieceX, this.PieceY + 1))
        {
            this.PieceY++;
            this.lockElapsed = 0;
            this.lastMoveWasRotation = false;
            if (scored)
            {
                this.Score += SoftDropPointsPerCell;
            }
        }
        // Grounded: leave locking to the Tick grace timer, so holding soft drop can't skip it.
    }

    private void Lock()
    {
        this.LockCount++;
        this.hardDropLockout = HardDropLockoutSeconds;
        var isTSpin = false;
        var isMini = false;
        if (this.PieceKind == TKind && this.lastMoveWasRotation)
        {
            DetectTSpin(out isTSpin, out isMini);
        }

        foreach (var (x, y) in CurrentCells())
        {
            if (y >= 0 && y < Rows && x >= 0 && x < Columns)
            {
                this.well[x, y] = this.PieceKind + 1;
            }
        }
        ClearLines(isTSpin, isMini);
        Spawn();
    }

    /// <summary>The classic 3-corner check: a T that just rotated into place counts as a spin once at least
    /// three of its four diagonal corners are walled in, mini unless both corners on its pointed side are.</summary>
    private void DetectTSpin(out bool isTSpin, out bool isMini)
    {
        var cx = this.PieceX + 1;
        var cy = this.PieceY + 1;
        var front = TFrontCorners[this.PieceRotation];
        var back = TBackCorners[this.PieceRotation];
        var frontFilled = CornerFilled(cx, cy, front[0]) + CornerFilled(cx, cy, front[1]);
        var backFilled = CornerFilled(cx, cy, back[0]) + CornerFilled(cx, cy, back[1]);
        isTSpin = frontFilled + backFilled >= 3;
        isMini = isTSpin && frontFilled < 2;
    }

    private int CornerFilled(int cx, int cy, (int Dx, int Dy) offset)
    {
        var x = cx + offset.Dx;
        var y = cy + offset.Dy;
        if (x < 0 || x >= Columns || y >= Rows)
        {
            return 1;
        }
        return y >= 0 && this.well[x, y] != 0 ? 1 : 0;
    }

    private void ClearLines(bool isTSpin, bool isMini)
    {
        // Found in one pass over the untouched board, so every full row keeps its own real position
        // (a shift-and-recheck loop would slide later rows into an already-recorded index, collapsing
        // e.g. a tetris's four rows into just the bottom one for the renderer's flash).
        var clearedRows = new List<int>();
        for (var y = 0; y < Rows; y++)
        {
            var full = true;
            for (var x = 0; x < Columns; x++)
            {
                if (this.well[x, y] == 0)
                {
                    full = false;
                    break;
                }
            }
            if (full)
            {
                clearedRows.Add(y);
            }
        }
        var cleared = clearedRows.Count;
        if (cleared > 0)
        {
            // Compact bottom-up, skipping the cleared rows, then blank whatever's left uncovered at the top.
            var writeY = Rows - 1;
            for (var readY = Rows - 1; readY >= 0; readY--)
            {
                if (clearedRows.Contains(readY))
                {
                    continue;
                }
                if (writeY != readY)
                {
                    for (var x = 0; x < Columns; x++)
                    {
                        this.well[x, writeY] = this.well[x, readY];
                    }
                }
                writeY--;
            }
            for (var y = writeY; y >= 0; y--)
            {
                for (var x = 0; x < Columns; x++)
                {
                    this.well[x, y] = 0;
                }
            }
        }
        this.LastCleared = cleared;
        this.LastClearedRows = clearedRows;
        this.LastClearIsTSpin = isTSpin;
        this.LastClearIsMini = isMini;

        if (cleared == 0)
        {
            this.combo = -1;
            this.LastClearCombo = -1;
            this.LastClearIsBackToBack = false;
            // A T-Spin still scores even without clearing a line, but doesn't touch combo or back-to-back.
            if (isTSpin)
            {
                this.Score += (isMini ? MiniTSpinScores[0] : RegularTSpinScores[0]) * this.Level;
            }
            return;
        }

        this.combo++;
        var comboBonus = this.combo * ComboPointsPerCombo * this.Level;
        var isDifficult = cleared == 4 || isTSpin;
        var appliedBackToBack = isDifficult && this.backToBack;
        var baseScore = isTSpin
            ? (isMini ? MiniTSpinScores[Math.Min(cleared, MiniTSpinScores.Length - 1)] : RegularTSpinScores[Math.Min(cleared, RegularTSpinScores.Length - 1)])
            : LineScores[Math.Min(cleared, 4)];
        var multiplier = appliedBackToBack ? BackToBackMultiplier : 1.0;

        this.Score += (int)(baseScore * this.Level * multiplier) + comboBonus;
        this.Lines += cleared;
        this.backToBack = isDifficult;
        this.LastClearCombo = this.combo;
        this.LastClearIsBackToBack = appliedBackToBack;
    }

    private void Spawn()
    {
        this.PieceKind = this.NextKind;
        this.NextKind = TakeFromBag();
        this.CanHold = true;
        this.PieceRotation = 0;
        this.PieceX = (Columns / 2) - 2;
        this.PieceY = SpawnRow;
        if (!Fits(this.PieceKind, this.PieceRotation, this.PieceX, 0))
        {
            this.Dead = true;
        }
    }

    /// <summary>Seven-bag randomiser, so you never wait forever for the piece you need.</summary>
    private int TakeFromBag()
    {
        if (this.bag.Count == 0)
        {
            for (var i = 0; i < PivotOffsets.Length; i++)
            {
                this.bag.Add(i);
            }
            for (var i = this.bag.Count - 1; i > 0; i--)
            {
                var j = this.rng.Next(i + 1);
                (this.bag[i], this.bag[j]) = (this.bag[j], this.bag[i]);
            }
        }
        var kind = this.bag[^1];
        this.bag.RemoveAt(this.bag.Count - 1);
        return kind;
    }

    private bool Fits(int kind, int rotation, int originX, int originY)
    {
        foreach (var (x, y) in Cells(kind, rotation, originX, originY))
        {
            if (x < 0 || x >= Columns || y >= Rows)
            {
                return false;
            }
            if (y >= 0 && this.well[x, y] != 0)
            {
                return false;
            }
        }
        return true;
    }
}
