using System;
using System.Collections.Generic;
using System.Numerics;

namespace AetherOS.Apps.VoidInvaders;

/// <summary>Void Invaders: seven columns by five rows marching sideways and dropping a step at each edge,
/// four eroding shields, one cannon. The formation speeds up as it thins, so the last invader is the
/// fastest thing in the game.</summary>
public sealed class VoidInvadersGame
{
    public const float Width = 100f;
    public const float Height = 140f;
    public const int Columns = 7;
    public const int Rows = 5;

    public const float InvaderWidth = 8f;
    public const float InvaderHeight = 6f;
    public const float PlayerWidth = 9f;
    public const float PlayerHeight = 4.5f;

    public const int ShieldCount = 4;
    public const int ShieldColumns = 5;
    public const int ShieldRows = 3;
    public const float ShieldCell = 2.2f;

    private const float ColumnPitch = 12f;
    private const float RowPitch = 10f;
    private const float StepX = 2.6f;
    private const float StepY = 5f;
    private const float PlayerY = 126f;
    private const float ShieldY = 104f;
    private const float BulletSpeed = 120f;
    private const float BombSpeed = 38f;
    private const float PlayerSpeed = 62f;
    private const float LandingY = PlayerY - InvaderHeight;
    private const int MaxBombs = 3;
    private const int StartLives = 3;
    private const int WaveClearBonus = 200;

    /// <summary>Points by row, counting from the top: the small ones at the back are worth the most.</summary>
    private static readonly int[] RowPoints = [30, 20, 20, 10, 10];

    /// <summary>Sprite kind per row, so the formation reads as three distinct silhouettes.</summary>
    private static readonly int[] RowKinds = [0, 1, 1, 2, 2];

    private static readonly float[] ShieldX = [14f, 38f, 62f, 86f];

    public sealed class Bomb
    {
        public Vector2 Pos;
    }

    private readonly Random random = new();
    private readonly bool[,] invaders = new bool[Columns, Rows];
    private readonly bool[] shields = new bool[ShieldCount * ShieldColumns * ShieldRows];
    private readonly List<Bomb> bombs = [];

    private float formationX;
    private float formationY;
    private int direction = 1;
    private float stepTimer;
    private float bombTimer;
    private float respawnTimer;

    public int Score { get; private set; }

    public int Wave { get; private set; }

    public int Lives { get; private set; }

    public bool Dead { get; private set; }

    public float PlayerX { get; private set; }

    /// <summary>Flips on every formation step, which is what animates the invaders' legs.</summary>
    public bool AnimFrame { get; private set; }

    public Vector2? Bullet { get; private set; }

    public IReadOnlyList<Bomb> Bombs => this.bombs;

    public bool InvaderAlive(int column, int row) => this.invaders[column, row];

    public static int KindForRow(int row) => RowKinds[row];

    public Vector2 InvaderPos(int column, int row) =>
        new(this.formationX + (column * ColumnPitch), this.formationY + (row * RowPitch));

    public bool ShieldCellAlive(int shield, int column, int row) =>
        this.shields[ShieldIndex(shield, column, row)];

    public static Vector2 ShieldCellPos(int shield, int column, int row) =>
        new(ShieldX[shield] - (ShieldColumns * ShieldCell * 0.5f) + (column * ShieldCell),
            ShieldY + (row * ShieldCell));

    public static float PlayerRowY => PlayerY;

    private static int ShieldIndex(int shield, int column, int row) =>
        (shield * ShieldColumns * ShieldRows) + (row * ShieldColumns) + column;

    public void Reset()
    {
        this.Score = 0;
        this.Wave = 0;
        this.Lives = StartLives;
        this.Dead = false;
        StartWave();
    }

    private void StartWave()
    {
        this.Wave++;
        for (var c = 0; c < Columns; c++)
        {
            for (var r = 0; r < Rows; r++)
            {
                this.invaders[c, r] = true;
            }
        }
        Array.Fill(this.shields, true);
        this.bombs.Clear();
        this.Bullet = null;
        this.formationX = 6f;
        this.formationY = 14f + (Math.Min(this.Wave - 1, 6) * 3f);
        this.direction = 1;
        this.stepTimer = 0f;
        this.bombTimer = 1.2f;
        this.PlayerX = Width * 0.5f;
        this.respawnTimer = 0f;
    }

    private int AliveCount
    {
        get
        {
            var alive = 0;
            for (var c = 0; c < Columns; c++)
            {
                for (var r = 0; r < Rows; r++)
                {
                    if (this.invaders[c, r])
                    {
                        alive++;
                    }
                }
            }
            return alive;
        }
    }

    public void MoveLeft(float delta) => this.PlayerX = Math.Max(PlayerWidth * 0.5f, this.PlayerX - (PlayerSpeed * delta));

    public void MoveRight(float delta) => this.PlayerX = Math.Min(Width - (PlayerWidth * 0.5f), this.PlayerX + (PlayerSpeed * delta));

    /// <summary>One shot in the air at a time, exactly like the cabinet.</summary>
    public void Fire()
    {
        if (this.Dead || this.Bullet != null || this.respawnTimer > 0f)
        {
            return;
        }
        this.Bullet = new Vector2(this.PlayerX, PlayerY - PlayerHeight);
    }

    public void Tick(float delta)
    {
        if (this.Dead)
        {
            return;
        }
        if (this.respawnTimer > 0f)
        {
            this.respawnTimer -= delta;
            return;
        }

        StepFormation(delta);
        DropBombs(delta);

        // The bullet crosses more than a shield cell per frame at any sane frame rate, so it has to move
        // in bites smaller than the thing it is supposed to hit.
        var steps = Math.Clamp((int)Math.Ceiling(delta * BulletSpeed / (ShieldCell * 0.5f)), 1, 16);
        var step = delta / steps;
        for (var i = 0; i < steps; i++)
        {
            MoveBullet(step);
            MoveBombs(step);
            if (this.respawnTimer > 0f || this.Dead)
            {
                return;
            }
        }

        if (this.AliveCount == 0)
        {
            this.Score += WaveClearBonus;
            StartWave();
        }
    }

    private void StepFormation(float delta)
    {
        var alive = this.AliveCount;
        if (alive == 0)
        {
            return;
        }
        var total = Columns * Rows;
        var interval = Math.Max(0.055f, (0.62f - (this.Wave * 0.03f)) * ((float)alive / total));
        this.stepTimer += delta;
        if (this.stepTimer < interval)
        {
            return;
        }
        this.stepTimer = 0f;
        this.AnimFrame = !this.AnimFrame;

        var (minX, maxX) = FormationBounds();
        var nextMin = minX + (StepX * this.direction);
        var nextMax = maxX + (StepX * this.direction);
        if (nextMin < 2f || nextMax > Width - 2f)
        {
            this.direction = -this.direction;
            this.formationY += StepY;
            if (this.formationY + ((Rows - 1) * RowPitch) + InvaderHeight >= LandingY)
            {
                this.Dead = true;
            }
            return;
        }
        this.formationX += StepX * this.direction;
    }

    private (float Min, float Max) FormationBounds()
    {
        var min = Width;
        var max = 0f;
        for (var c = 0; c < Columns; c++)
        {
            for (var r = 0; r < Rows; r++)
            {
                if (!this.invaders[c, r])
                {
                    continue;
                }
                var x = this.formationX + (c * ColumnPitch);
                min = Math.Min(min, x);
                max = Math.Max(max, x + InvaderWidth);
                break;
            }
        }
        return (min, max);
    }

    private void MoveBullet(float delta)
    {
        if (this.Bullet is not { } bullet)
        {
            return;
        }
        bullet.Y -= BulletSpeed * delta;
        if (bullet.Y < 0f)
        {
            this.Bullet = null;
            return;
        }
        this.Bullet = bullet;

        if (HitShield(bullet))
        {
            this.Bullet = null;
            return;
        }
        for (var c = 0; c < Columns; c++)
        {
            for (var r = 0; r < Rows; r++)
            {
                if (!this.invaders[c, r])
                {
                    continue;
                }
                var pos = InvaderPos(c, r);
                if (bullet.X >= pos.X && bullet.X <= pos.X + InvaderWidth
                    && bullet.Y >= pos.Y && bullet.Y <= pos.Y + InvaderHeight)
                {
                    this.invaders[c, r] = false;
                    this.Score += RowPoints[r];
                    this.Bullet = null;
                    return;
                }
            }
        }
    }

    private void DropBombs(float delta)
    {
        this.bombTimer -= delta;
        if (this.bombTimer > 0f || this.bombs.Count >= MaxBombs)
        {
            return;
        }
        this.bombTimer = Math.Max(0.35f, 1.6f - (this.Wave * 0.12f)) * (0.5f + ((float)this.random.NextDouble() * 1f));

        // Only the lowest invader in a column can fire, so the front rank does the shooting.
        var candidates = new List<Vector2>();
        for (var c = 0; c < Columns; c++)
        {
            for (var r = Rows - 1; r >= 0; r--)
            {
                if (this.invaders[c, r])
                {
                    var pos = InvaderPos(c, r);
                    candidates.Add(new Vector2(pos.X + (InvaderWidth * 0.5f), pos.Y + InvaderHeight));
                    break;
                }
            }
        }
        if (candidates.Count > 0)
        {
            this.bombs.Add(new Bomb { Pos = candidates[this.random.Next(candidates.Count)] });
        }
    }

    private void MoveBombs(float delta)
    {
        for (var i = this.bombs.Count - 1; i >= 0; i--)
        {
            var bomb = this.bombs[i];
            bomb.Pos.Y += BombSpeed * delta;

            if (HitShield(bomb.Pos))
            {
                this.bombs.RemoveAt(i);
                continue;
            }
            if (bomb.Pos.Y >= PlayerY - PlayerHeight && bomb.Pos.Y <= PlayerY
                && Math.Abs(bomb.Pos.X - this.PlayerX) <= PlayerWidth * 0.5f)
            {
                this.bombs.RemoveAt(i);
                LoseLife();
                continue;
            }
            if (bomb.Pos.Y > Height)
            {
                this.bombs.RemoveAt(i);
            }
        }
    }

    private bool HitShield(Vector2 point)
    {
        for (var s = 0; s < ShieldCount; s++)
        {
            for (var c = 0; c < ShieldColumns; c++)
            {
                for (var r = 0; r < ShieldRows; r++)
                {
                    var index = ShieldIndex(s, c, r);
                    if (!this.shields[index])
                    {
                        continue;
                    }
                    var pos = ShieldCellPos(s, c, r);
                    if (point.X >= pos.X && point.X <= pos.X + ShieldCell
                        && point.Y >= pos.Y && point.Y <= pos.Y + ShieldCell)
                    {
                        this.shields[index] = false;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private void LoseLife()
    {
        this.Lives--;
        this.Bullet = null;
        this.bombs.Clear();
        if (this.Lives <= 0)
        {
            this.Dead = true;
            return;
        }
        this.PlayerX = Width * 0.5f;
        this.respawnTimer = 1f;
    }
}
