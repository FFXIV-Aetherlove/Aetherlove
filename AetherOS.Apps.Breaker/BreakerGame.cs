using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherOS.Apps.Breaker;

internal enum PowerKind { Wide, Multi, Slow, Life, Points }

/// <summary>A ball in flight. Positions are in board units (columns wide, rows tall), not pixels, so the
/// model stays resolution independent.</summary>
internal sealed class Ball
{
    public float X;
    public float Y;
    public float VelocityX;
    public float VelocityY;
}

/// <summary>A falling capsule dropped by a broken brick.</summary>
internal sealed class Capsule
{
    public float X;
    public float Y;
    public PowerKind Kind;
}

/// <summary>Breaker's rules: a paddle, one or more balls, a brick grid with multi-hit and solid bricks,
/// dropped power-ups, lives, and a level table. No drawing and no ImGui; the board is a unit space the
/// renderer scales into pixels.</summary>
internal sealed class BreakerGame
{
    public const int Columns = 13;
    public const int Rows = 22;
    public const int BrickRows = 9;

    private const float PaddleY = Rows - 1.5f;
    private const float BallRadius = 0.28f;
    private const float BaseSpeed = 9.0f;
    private const float MaxSpeed = 15.0f;
    private const float CapsuleFallSpeed = 4.0f;
    private const float PaddleSpeed = 14.0f;

    private const float NarrowPaddle = 2.2f;
    private const float WidePaddle = 3.6f;
    private const float PowerSeconds = 12f;
    private const float DropChance = 0.24f;

    private const int PointsPerHit = 50;
    private const int PointsPerBreak = 100;
    private const int PointsPerCapsule = 250;
    private const int LevelClearBonus = 1000;
    private const int StartingLives = 3;

    /// <summary>Level layouts. '.' empty, '1'-'3' hit counts, '#' indestructible.</summary>
    private static readonly string[][] Levels =
    [
        [
            ".............",
            "..111111111..",
            "..111111111..",
            "..222222222..",
            ".............",
        ],
        [
            "..1111111111.",
            "..1..........",
            "..1.22222222.",
            "..1.2........",
            "..1.2.333333.",
            "..1.2........",
        ],
        [
            "......1......",
            ".....121.....",
            "....12321....",
            "...1233321...",
            "..123###321..",
            "...1233321...",
        ],
        [
            "2.2.2.2.2.2.2",
            ".1.1.1.1.1.1.",
            "2.2.2.2.2.2.2",
            ".1.1.1.1.1.1.",
            "3.3.3.3.3.3.3",
        ],
        [
            "#...........#",
            ".33333333333.",
            ".3222222222.3",
            ".32111111123.",
            ".3222222222.3",
            ".33333333333.",
            "#...........#",
        ],
        [
            "1111111111111",
            "22.........22",
            "2.3.......3.2",
            "2..#######..2",
            "2.3.......3.2",
            "22.........22",
            "1111111111111",
        ],
    ];

    private readonly int[,] bricks = new int[Columns, BrickRows];
    private readonly List<Ball> balls = [];
    private readonly List<Capsule> capsules = [];
    private readonly Random rng = new();

    private float widePowerRemaining;
    private float slowPowerRemaining;

    public IReadOnlyList<Ball> Balls => this.balls;

    public IReadOnlyList<Capsule> Capsules => this.capsules;

    public float PaddleX { get; private set; } = Columns / 2f;

    public float PaddleWidth => this.widePowerRemaining > 0 ? WidePaddle : NarrowPaddle;

    public float PaddleTop => PaddleY;

    public int Score { get; private set; }

    public int Lives { get; private set; }

    public int LevelIndex { get; private set; }

    public int LevelNumber => this.LevelIndex + 1;

    public static int LevelCount => Levels.Length;

    public bool Dead { get; private set; }

    /// <summary>True once every level has been cleared; the run ends in triumph rather than defeat.</summary>
    /// <summary>Set for one frame when a level was just cleared, so the renderer can celebrate.</summary>
    public bool LevelJustCleared { get; private set; }

    /// <summary>True while the ball waits on the paddle for the player to launch it.</summary>
    public bool AwaitingLaunch { get; private set; }

    public bool WideActive => this.widePowerRemaining > 0;

    public bool SlowActive => this.slowPowerRemaining > 0;

    /// <summary>Hit count of a brick cell; 0 empty, -1 indestructible.</summary>
    public int Brick(int x, int y) => this.bricks[x, y];

    public void Reset()
    {
        this.Score = 0;
        this.Lives = StartingLives;
        this.LevelIndex = 0;
        this.Dead = false;
        LoadLevel();
    }

    public void MovePaddle(float delta)
    {
        var half = this.PaddleWidth * 0.5f;
        this.PaddleX = Math.Clamp(this.PaddleX + delta, half, Columns - half);
        if (this.AwaitingLaunch)
        {
            var ball = this.balls[0];
            ball.X = this.PaddleX;
            ball.Y = PaddleY - BallRadius - 0.05f;
        }
    }

    /// <summary>Launch speed climbs with the level, so a lost life deep into a run relaunches fast instead
    /// of resetting the difficulty; from level ~13 every ball starts at the cap.</summary>
    private float LaunchSpeed => MathF.Min(MaxSpeed, BaseSpeed + ((this.LevelNumber - 1) * 0.5f));

    /// <summary>Sends the waiting ball on its way, angled slightly by nothing more than tradition.</summary>
    public void Launch()
    {
        if (!this.AwaitingLaunch)
        {
            return;
        }
        this.AwaitingLaunch = false;
        var ball = this.balls[0];
        ball.VelocityX = LaunchSpeed * 0.45f * (this.rng.Next(2) == 0 ? -1f : 1f);
        ball.VelocityY = -LaunchSpeed * 0.9f;
    }

    public void Tick(double deltaSeconds)
    {
        if (this.Dead)
        {
            return;
        }
        this.LevelJustCleared = false;
        var delta = (float)Math.Min(deltaSeconds, 0.5);

        this.widePowerRemaining = Math.Max(0f, this.widePowerRemaining - delta);
        this.slowPowerRemaining = Math.Max(0f, this.slowPowerRemaining - delta);

        MoveCapsules(delta);
        if (!this.AwaitingLaunch)
        {
            // Substep so a fast ball can't tunnel straight through a brick row.
            var steps = Math.Clamp((int)MathF.Ceiling(delta * 120f), 1, 12);
            var slice = delta / steps;
            for (var i = 0; i < steps && !this.Dead; i++)
            {
                MoveBalls(slice);
            }
        }
    }

    private void MoveCapsules(float delta)
    {
        for (var i = this.capsules.Count - 1; i >= 0; i--)
        {
            var capsule = this.capsules[i];
            capsule.Y += CapsuleFallSpeed * delta;
            var caught = capsule.Y >= PaddleY - 0.4f && capsule.Y <= PaddleY + 0.6f
                && MathF.Abs(capsule.X - this.PaddleX) <= (this.PaddleWidth * 0.5f) + 0.4f;
            if (caught)
            {
                Apply(capsule.Kind);
                this.capsules.RemoveAt(i);
            }
            else if (capsule.Y > Rows)
            {
                this.capsules.RemoveAt(i);
            }
        }
    }

    private void Apply(PowerKind kind)
    {
        this.Score += PointsPerCapsule;
        switch (kind)
        {
            case PowerKind.Wide:
                this.widePowerRemaining = PowerSeconds;
                break;
            case PowerKind.Slow:
                this.slowPowerRemaining = PowerSeconds;
                break;
            case PowerKind.Life:
                this.Lives++;
                break;
            case PowerKind.Multi:
                SplitBalls();
                break;
            default:
                this.Score += PointsPerCapsule;
                break;
        }
    }

    /// <summary>Two extra balls fan out from each ball in play, capped so the board stays readable.</summary>
    private void SplitBalls()
    {
        foreach (var ball in this.balls.ToList())
        {
            if (this.balls.Count >= 6)
            {
                break;
            }
            for (var i = 0; i < 2; i++)
            {
                var angle = (i == 0 ? 0.4f : -0.4f);
                var cos = MathF.Cos(angle);
                var sin = MathF.Sin(angle);
                this.balls.Add(new Ball
                {
                    X = ball.X,
                    Y = ball.Y,
                    VelocityX = (ball.VelocityX * cos) - (ball.VelocityY * sin),
                    VelocityY = (ball.VelocityX * sin) + (ball.VelocityY * cos),
                });
            }
        }
    }

    private void MoveBalls(float delta)
    {
        var speedScale = this.slowPowerRemaining > 0 ? 0.65f : 1f;
        for (var i = this.balls.Count - 1; i >= 0; i--)
        {
            var ball = this.balls[i];
            ball.X += ball.VelocityX * delta * speedScale;
            ball.Y += ball.VelocityY * delta * speedScale;

            if (ball.X - BallRadius <= 0f)
            {
                ball.X = BallRadius;
                ball.VelocityX = MathF.Abs(ball.VelocityX);
            }
            else if (ball.X + BallRadius >= Columns)
            {
                ball.X = Columns - BallRadius;
                ball.VelocityX = -MathF.Abs(ball.VelocityX);
            }
            if (ball.Y - BallRadius <= 0f)
            {
                ball.Y = BallRadius;
                ball.VelocityY = MathF.Abs(ball.VelocityY);
            }

            BouncePaddle(ball);
            BounceBricks(ball);

            if (ball.Y - BallRadius > Rows)
            {
                this.balls.RemoveAt(i);
            }
        }

        if (this.balls.Count == 0)
        {
            LoseLife();
        }
    }

    /// <summary>Paddle bounce: where you hit decides the outgoing angle, which is the whole skill of the
    /// game. Speed creeps up slightly on every paddle hit, up to a ceiling.</summary>
    private void BouncePaddle(Ball ball)
    {
        var half = this.PaddleWidth * 0.5f;
        var withinX = ball.X >= this.PaddleX - half - BallRadius && ball.X <= this.PaddleX + half + BallRadius;
        var crossing = ball.Y + BallRadius >= PaddleY && ball.Y + BallRadius <= PaddleY + 0.6f;
        if (!withinX || !crossing || ball.VelocityY <= 0f)
        {
            return;
        }
        var offset = Math.Clamp((ball.X - this.PaddleX) / half, -1f, 1f);
        var speed = MathF.Min(MaxSpeed, MathF.Sqrt((ball.VelocityX * ball.VelocityX)
            + (ball.VelocityY * ball.VelocityY)) * 1.02f);
        var angle = offset * 1.05f;
        ball.VelocityX = MathF.Sin(angle) * speed;
        ball.VelocityY = -MathF.Cos(angle) * speed;
        ball.Y = PaddleY - BallRadius - 0.01f;
    }

    private void BounceBricks(Ball ball)
    {
        var col = (int)MathF.Floor(ball.X);
        var row = (int)MathF.Floor(ball.Y);
        if (col < 0 || col >= Columns || row < 0 || row >= BrickRows || this.bricks[col, row] == 0)
        {
            return;
        }

        // Bounce off whichever face the ball crossed, judged by how deep it is in the cell.
        var cellCenterX = col + 0.5f;
        var cellCenterY = row + 0.5f;
        var overlapX = 0.5f + BallRadius - MathF.Abs(ball.X - cellCenterX);
        var overlapY = 0.5f + BallRadius - MathF.Abs(ball.Y - cellCenterY);
        if (overlapX < overlapY)
        {
            ball.VelocityX = -ball.VelocityX;
            ball.X += ball.VelocityX > 0 ? overlapX : -overlapX;
        }
        else
        {
            ball.VelocityY = -ball.VelocityY;
            ball.Y += ball.VelocityY > 0 ? overlapY : -overlapY;
        }

        if (this.bricks[col, row] < 0)
        {
            return;
        }
        this.bricks[col, row]--;
        this.Score += PointsPerHit;
        if (this.bricks[col, row] == 0)
        {
            this.Score += PointsPerBreak;
            MaybeDropCapsule(col, row);
            if (!AnyBreakableLeft())
            {
                AdvanceLevel();
            }
        }
    }

    private void MaybeDropCapsule(int col, int row)
    {
        if (this.rng.NextDouble() > DropChance)
        {
            return;
        }
        // Life capsules are the rare one; the rest come up evenly.
        var roll = this.rng.Next(100);
        var kind = roll switch
        {
            < 28 => PowerKind.Wide,
            < 52 => PowerKind.Multi,
            < 74 => PowerKind.Slow,
            < 84 => PowerKind.Life,
            _ => PowerKind.Points,
        };
        this.capsules.Add(new Capsule { X = col + 0.5f, Y = row + 0.5f, Kind = kind });
    }

    private bool AnyBreakableLeft()
    {
        for (var x = 0; x < Columns; x++)
        {
            for (var y = 0; y < BrickRows; y++)
            {
                if (this.bricks[x, y] > 0)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Levels loop forever so the score has no ceiling; the clear bonus keeps scaling with the
    /// ever-growing level number while the ball speed stays capped.</summary>
    private void AdvanceLevel()
    {
        this.Score += LevelClearBonus * this.LevelNumber;
        this.LevelJustCleared = true;
        this.LevelIndex++;
        LoadLevel();
    }

    private void LoseLife()
    {
        this.Lives--;
        this.capsules.Clear();
        this.widePowerRemaining = 0;
        this.slowPowerRemaining = 0;
        if (this.Lives <= 0)
        {
            this.Dead = true;
            return;
        }
        PlaceBallOnPaddle();
    }

    private void LoadLevel()
    {
        Array.Clear(this.bricks);
        var layout = Levels[this.LevelIndex % Levels.Length];
        for (var y = 0; y < layout.Length && y < BrickRows; y++)
        {
            var line = layout[y];
            for (var x = 0; x < Columns && x < line.Length; x++)
            {
                this.bricks[x, y] = line[x] switch
                {
                    '1' => 1,
                    '2' => 2,
                    '3' => 3,
                    '#' => -1,
                    _ => 0,
                };
            }
        }
        this.capsules.Clear();
        this.widePowerRemaining = 0;
        this.slowPowerRemaining = 0;
        PlaceBallOnPaddle();
    }

    private void PlaceBallOnPaddle()
    {
        this.balls.Clear();
        this.PaddleX = Columns / 2f;
        this.balls.Add(new Ball { X = this.PaddleX, Y = PaddleY - BallRadius - 0.05f });
        this.AwaitingLaunch = true;
    }

    /// <summary>Board-unit radius, so the renderer draws the ball at the model's scale.</summary>
    public static float Radius => BallRadius;

    public static float PaddleGlideSpeed => PaddleSpeed;
}
