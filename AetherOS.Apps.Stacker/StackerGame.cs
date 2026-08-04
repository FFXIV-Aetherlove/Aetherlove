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
    private const int SoftDropPointsPerCell = 1;
    private const int HardDropPointsPerCell = 2;
    private const int LinesPerLevel = 10;

    /// <summary>Rotation states per piece, each a list of (x, y) cells inside a 4x4 box.</summary>
    private static readonly int[][][] Pieces =
    [
        // I
        [[0, 1, 1, 1, 2, 1, 3, 1], [2, 0, 2, 1, 2, 2, 2, 3], [0, 2, 1, 2, 2, 2, 3, 2], [1, 0, 1, 1, 1, 2, 1, 3]],
        // J
        [[0, 0, 0, 1, 1, 1, 2, 1], [1, 0, 2, 0, 1, 1, 1, 2], [0, 1, 1, 1, 2, 1, 2, 2], [1, 0, 1, 1, 0, 2, 1, 2]],
        // L
        [[2, 0, 0, 1, 1, 1, 2, 1], [1, 0, 1, 1, 1, 2, 2, 2], [0, 1, 1, 1, 2, 1, 0, 2], [0, 0, 1, 0, 1, 1, 1, 2]],
        // O
        [[1, 0, 2, 0, 1, 1, 2, 1], [1, 0, 2, 0, 1, 1, 2, 1], [1, 0, 2, 0, 1, 1, 2, 1], [1, 0, 2, 0, 1, 1, 2, 1]],
        // S
        [[1, 0, 2, 0, 0, 1, 1, 1], [1, 0, 1, 1, 2, 1, 2, 2], [1, 1, 2, 1, 0, 2, 1, 2], [0, 0, 0, 1, 1, 1, 1, 2]],
        // T
        [[1, 0, 0, 1, 1, 1, 2, 1], [1, 0, 1, 1, 2, 1, 1, 2], [0, 1, 1, 1, 2, 1, 1, 2], [1, 0, 0, 1, 1, 1, 1, 2]],
        // Z
        [[0, 0, 1, 0, 1, 1, 2, 1], [2, 0, 1, 1, 2, 1, 1, 2], [0, 1, 1, 1, 1, 2, 2, 2], [1, 0, 0, 1, 1, 1, 0, 2]],
    ];

    /// <summary>Kick offsets tried in order when a rotation lands in a wall or a stack.</summary>
    private static readonly int[] KickOffsets = [0, -1, 1, -2, 2];

    private readonly int[,] well = new int[Columns, Rows];
    private readonly Random rng = new();
    private readonly List<int> bag = [];

    private double fallAccumulator;

    public int PieceKind { get; private set; }

    public int PieceRotation { get; private set; }

    public int PieceX { get; private set; }

    public int PieceY { get; private set; }

    public int NextKind { get; private set; }

    public int Score { get; private set; }

    public int Lines { get; private set; }

    public int Level => (this.Lines / LinesPerLevel) + 1;

    public bool Dead { get; private set; }

    /// <summary>Rows cleared by the last lock, for the renderer's flash.</summary>
    public int LastCleared { get; private set; }

    /// <summary>True when a cell of the settled stack is filled.</summary>
    public bool Filled(int x, int y) => this.well[x, y] != 0;

    /// <summary>The four cells of a piece at a given position and rotation.</summary>
    public static IEnumerable<(int X, int Y)> Cells(int kind, int rotation, int originX, int originY)
    {
        var shape = Pieces[kind][((rotation % 4) + 4) % 4];
        for (var i = 0; i < 8; i += 2)
        {
            yield return (originX + shape[i], originY + shape[i + 1]);
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
        this.fallAccumulator = 0;
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
        this.fallAccumulator += Math.Min(deltaSeconds, 0.5);
        var interval = FallInterval;
        while (!this.Dead && this.fallAccumulator >= interval)
        {
            this.fallAccumulator -= interval;
            StepDown(scored: false);
        }
    }

    /// <summary>Gravity interval for the current level, from a leisurely start to a twitchy floor.</summary>
    private double FallInterval => Math.Max(0.08, 0.75 - ((this.Level - 1) * 0.065));

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

    /// <summary>Slams the piece to its landing spot and locks it immediately.</summary>
    public void HardDrop()
    {
        if (this.Dead)
        {
            return;
        }
        var dropped = 0;
        while (Fits(this.PieceKind, this.PieceRotation, this.PieceX, this.PieceY + 1))
        {
            this.PieceY++;
            dropped++;
        }
        this.Score += dropped * HardDropPointsPerCell;
        Lock();
    }

    public void Rotate()
    {
        if (this.Dead)
        {
            return;
        }
        var next = (this.PieceRotation + 1) % 4;
        foreach (var kick in KickOffsets)
        {
            if (Fits(this.PieceKind, next, this.PieceX + kick, this.PieceY))
            {
                this.PieceRotation = next;
                this.PieceX += kick;
                return;
            }
        }
    }

    private void TryMove(int dx)
    {
        if (!this.Dead && Fits(this.PieceKind, this.PieceRotation, this.PieceX + dx, this.PieceY))
        {
            this.PieceX += dx;
        }
    }

    private void StepDown(bool scored)
    {
        if (Fits(this.PieceKind, this.PieceRotation, this.PieceX, this.PieceY + 1))
        {
            this.PieceY++;
            if (scored)
            {
                this.Score += SoftDropPointsPerCell;
            }
            return;
        }
        Lock();
    }

    private void Lock()
    {
        foreach (var (x, y) in CurrentCells())
        {
            if (y >= 0 && y < Rows && x >= 0 && x < Columns)
            {
                this.well[x, y] = this.PieceKind + 1;
            }
        }
        ClearLines();
        Spawn();
    }

    private void ClearLines()
    {
        var cleared = 0;
        for (var y = Rows - 1; y >= 0; y--)
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
            if (!full)
            {
                continue;
            }
            cleared++;
            // Pull everything above down one row, then re-check this row.
            for (var row = y; row > 0; row--)
            {
                for (var x = 0; x < Columns; x++)
                {
                    this.well[x, row] = this.well[x, row - 1];
                }
            }
            for (var x = 0; x < Columns; x++)
            {
                this.well[x, 0] = 0;
            }
            y++;
        }
        this.LastCleared = cleared;
        if (cleared > 0)
        {
            this.Score += LineScores[Math.Min(cleared, 4)] * this.Level;
            this.Lines += cleared;
        }
    }

    private void Spawn()
    {
        this.PieceKind = this.NextKind;
        this.NextKind = TakeFromBag();
        this.PieceRotation = 0;
        this.PieceX = (Columns / 2) - 2;
        this.PieceY = 0;
        if (!Fits(this.PieceKind, this.PieceRotation, this.PieceX, this.PieceY))
        {
            this.Dead = true;
        }
    }

    /// <summary>Seven-bag randomiser, so you never wait forever for the piece you need.</summary>
    private int TakeFromBag()
    {
        if (this.bag.Count == 0)
        {
            for (var i = 0; i < Pieces.Length; i++)
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
