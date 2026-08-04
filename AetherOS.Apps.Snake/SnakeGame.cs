using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Snake;

internal enum SnakeDirection { Up, Down, Left, Right }

/// <summary>The pure game model: a grid, the snake body, food, and scoring. No drawing and no ImGui, so
/// the rules stay readable and the renderer stays dumb. Time is fed in as a delta, and the board steps on
/// a fixed interval that shortens as you eat, which is what makes the classic ramp.</summary>
internal sealed class SnakeGame
{
    public const int Columns = 20;
    public const int Rows = 24;

    /// <summary>Points per pellet, and per whole second survived.</summary>
    private const int PelletPoints = 10;
    private const int SurvivalPointsPerSecond = 1;

    /// <summary>Seconds per step at the start, the floor it ramps down to, and the cut per pellet.</summary>
    private const double StartStepSeconds = 0.22;
    private const double MinStepSeconds = 0.07;
    private const double StepCutPerPellet = 0.006;

    private readonly List<(int X, int Y)> body = [];
    private readonly Random rng = new();

    private SnakeDirection direction = SnakeDirection.Right;
    private SnakeDirection queuedDirection = SnakeDirection.Right;
    private double stepAccumulator;
    private double survivalAccumulator;
    private double stepSeconds = StartStepSeconds;

    public IReadOnlyList<(int X, int Y)> Body => this.body;

    public (int X, int Y) Food { get; private set; }

    public int Score { get; private set; }

    public int Pellets { get; private set; }

    public double ElapsedSeconds { get; private set; }

    public bool Dead { get; private set; }

    public void Reset()
    {
        this.body.Clear();
        var midX = Columns / 2;
        var midY = Rows / 2;
        // Head first, so growth is a simple tail append.
        this.body.Add((midX, midY));
        this.body.Add((midX - 1, midY));
        this.body.Add((midX - 2, midY));

        this.direction = SnakeDirection.Right;
        this.queuedDirection = SnakeDirection.Right;
        this.stepAccumulator = 0;
        this.survivalAccumulator = 0;
        this.stepSeconds = StartStepSeconds;
        this.Score = 0;
        this.Pellets = 0;
        this.ElapsedSeconds = 0;
        this.Dead = false;
        PlaceFood();
    }

    /// <summary>Queues a turn. A reversal into your own neck is ignored, and only one turn per step is
    /// accepted so a fast double-tap can't fold the snake back on itself.</summary>
    public void Steer(SnakeDirection next)
    {
        if (this.Dead)
        {
            return;
        }
        var reversal = (this.direction, next) switch
        {
            (SnakeDirection.Up, SnakeDirection.Down) => true,
            (SnakeDirection.Down, SnakeDirection.Up) => true,
            (SnakeDirection.Left, SnakeDirection.Right) => true,
            (SnakeDirection.Right, SnakeDirection.Left) => true,
            _ => false,
        };
        if (!reversal)
        {
            this.queuedDirection = next;
        }
    }

    public void Tick(double deltaSeconds)
    {
        if (this.Dead)
        {
            return;
        }
        // A long stall (alt-tab, loading screen) must not fast-forward the board into a wall.
        var delta = Math.Min(deltaSeconds, 0.5);
        this.ElapsedSeconds += delta;

        this.survivalAccumulator += delta;
        while (this.survivalAccumulator >= 1.0)
        {
            this.survivalAccumulator -= 1.0;
            this.Score += SurvivalPointsPerSecond;
        }

        this.stepAccumulator += delta;
        while (!this.Dead && this.stepAccumulator >= this.stepSeconds)
        {
            this.stepAccumulator -= this.stepSeconds;
            Step();
        }
    }

    private void Step()
    {
        this.direction = this.queuedDirection;
        var head = this.body[0];
        var next = this.direction switch
        {
            SnakeDirection.Up => (head.X, Y: head.Y - 1),
            SnakeDirection.Down => (head.X, Y: head.Y + 1),
            SnakeDirection.Left => (X: head.X - 1, head.Y),
            _ => (X: head.X + 1, head.Y),
        };

        if (next.X < 0 || next.Y < 0 || next.X >= Columns || next.Y >= Rows)
        {
            this.Dead = true;
            return;
        }
        // The tail vacates this step, so running into the very last segment is survivable.
        for (var i = 0; i < this.body.Count - 1; i++)
        {
            if (this.body[i] == next)
            {
                this.Dead = true;
                return;
            }
        }

        this.body.Insert(0, next);
        if (next == this.Food)
        {
            this.Pellets++;
            this.Score += PelletPoints;
            this.stepSeconds = Math.Max(MinStepSeconds, this.stepSeconds - StepCutPerPellet);
            PlaceFood();
        }
        else
        {
            this.body.RemoveAt(this.body.Count - 1);
        }
    }

    private void PlaceFood()
    {
        // Cheap rejection sampling; the board is never full enough for this to matter.
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var spot = (X: this.rng.Next(Columns), Y: this.rng.Next(Rows));
            if (!this.body.Contains(spot))
            {
                this.Food = spot;
                return;
            }
        }
        this.Food = (0, 0);
    }
}
