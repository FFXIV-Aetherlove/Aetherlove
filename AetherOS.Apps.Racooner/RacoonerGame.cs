using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Racooner;

/// <summary>The pure game model: a raccoon crossing five road lanes, a median, and five stream lanes to
/// bank itself in one of five bays. Vehicles and pads glide continuously on wrapping lanes; the raccoon
/// hops on the grid and rides pads with a fractional offset. No drawing and no ImGui; time is fed in as a
/// delta and the world advances on a fixed sub-step.</summary>
internal sealed class RacoonerGame
{
    public const int Columns = 13;
    public const int Rows = 15;

    public const int StartRow = 0;
    public const int RoadFirstRow = 1;
    public const int RoadLastRow = 5;
    public const int MedianRow = 6;
    public const int StreamFirstRow = 7;
    public const int StreamLastRow = 11;
    public const int BankRow = 12;
    public const int BayCount = 5;

    public static readonly int[] BayColumns = [0, 3, 6, 9, 12];

    /// <summary>Scoring and pacing, mirrored exactly by the server-side score checker.</summary>
    private const int HopPoints = 10;
    private const int BayPoints = 200;
    private const int BayTimeBonusMax = 90;
    private const int BayTimeBonusPerSecond = 2;
    private const int LevelClearBonus = 500;
    private const float LifeTimerSeconds = 45f;
    private const int StartLives = 3;

    private const float FixedStepSeconds = 1f / 60f;
    private const float SpeedRampPerLevel = 1.12f;
    private const int MaxVehiclesPerLane = 3;
    private const int DensityRampLevels = 2;
    private const int VehicleMinLength = 1;
    private const int VehicleMaxLength = 2;
    private const float CollisionMargin = 0.2f;
    private const float PadGripSlack = 0.3f;
    /// <summary>How far off a den's centre still counts as going in. A full cell, because the raccoon is
    /// drawn a cell wide: at 0.7 it could be plainly overlapping a den on screen and still be turned away,
    /// which reads as the den not working rather than as a miss. The dens are three columns apart, so a
    /// window this wide can never be ambiguous between two of them.</summary>
    private const float BayCatchHalfWidth = 1f;
    private const float HopFlashSeconds = 0.18f;
    private const float DeathPauseSeconds = 1.2f;
    private const float LevelClearPauseSeconds = 1.5f;
    private const float BankFlashSeconds = 0.9f;
    private const float BumpFlashSeconds = 0.4f;
    private const int StartColumn = 6;

    private static readonly float[] RoadSpeeds = [1.5f, 2.1f, 1.7f, 2.4f, 1.9f];
    private static readonly int[] RoadBaseCounts = [2, 3, 2, 3, 2];
    private static readonly float[] StreamSpeeds = [1.3f, 1.8f, 1.5f, 2.0f, 1.6f];
    private static readonly int[] PadCounts = [2, 3, 2, 3, 2];
    private static readonly int[] PadLengths = [3, 2, 3, 2, 3];

    public sealed class Entity
    {
        public float X;
        public int Length;
    }

    public sealed class Lane
    {
        public int Row;
        public float Speed;
        public readonly List<Entity> Entities = [];
    }

    private readonly Random random = new();
    private readonly List<Lane> roadLanes = [];
    private readonly List<Lane> streamLanes = [];
    private readonly bool[] bays = new bool[BayCount];

    private float stepAccumulator;
    private int maxRowReached;
    private float deathTimer;
    private float clearTimer;

    public int Score { get; private set; }

    public int Level { get; private set; }

    public int Lives { get; private set; }

    public bool Dead { get; private set; }

    public int BankedTotal { get; private set; }

    public float TimerRemaining { get; private set; }

    public float TimerFraction => this.TimerRemaining / LifeTimerSeconds;

    public float X { get; private set; }

    public int Row { get; private set; }

    public float HopFlash { get; private set; }

    public int LastBankedBay { get; private set; } = -1;

    public float BankFlash { get; private set; }

    /// <summary>A refused bank attempt, so a hop into the bank wall or an occupied den reads as
    /// refused rather than as a dropped input. Without it the game looks broken at exactly the
    /// moment a new player is working out what the top row is for.</summary>
    public float BumpFlash { get; private set; }

    public int BumpColumn { get; private set; } = -1;

    public bool Dying => this.deathTimer > 0f;

    public bool ClearingLevel => this.clearTimer > 0f;

    public bool Frozen => this.Dying || this.ClearingLevel;

    public IReadOnlyList<bool> Bays => this.bays;

    public IReadOnlyList<Lane> RoadLanes => this.roadLanes;

    public IReadOnlyList<Lane> StreamLanes => this.streamLanes;

    private bool OnStream => this.Row is >= StreamFirstRow and <= StreamLastRow;

    /// <summary>The empty den a hop up would enter right now, or -1. The board lights it, so the timing
    /// is something the player can see coming instead of something they guess at.</summary>
    public int AlignedBay
    {
        get
        {
            if (this.Dead || this.Frozen || this.Row != StreamLastRow)
            {
                return -1;
            }
            var bay = BayAt(this.X);
            return bay >= 0 && !this.bays[bay] ? bay : -1;
        }
    }

    /// <summary>Which den, if any, a raccoon at this position is lined up with. The one place the catch
    /// window is measured, so the highlight and the hop can never disagree about it.</summary>
    private static int BayAt(float x)
    {
        for (var i = 0; i < BayCount; i++)
        {
            if (MathF.Abs(x - BayColumns[i]) <= BayCatchHalfWidth)
            {
                return i;
            }
        }
        return -1;
    }

    public void Reset()
    {
        this.Score = 0;
        this.Level = 1;
        this.Lives = StartLives;
        this.Dead = false;
        this.BankedTotal = 0;
        this.LastBankedBay = -1;
        this.BankFlash = 0f;
        this.BumpFlash = 0f;
        this.BumpColumn = -1;
        this.deathTimer = 0f;
        this.clearTimer = 0f;
        this.stepAccumulator = 0f;
        Array.Clear(this.bays);
        BuildLanes();
        Respawn();
    }

    public void Tick(double deltaSeconds)
    {
        if (this.Dead)
        {
            return;
        }
        // A long stall (alt-tab, loading screen) must not fast-forward the traffic over the raccoon.
        this.stepAccumulator += (float)Math.Min(deltaSeconds, 0.5);
        while (!this.Dead && this.stepAccumulator >= FixedStepSeconds)
        {
            this.stepAccumulator -= FixedStepSeconds;
            Step(FixedStepSeconds);
        }
    }

    /// <summary>A discrete hop. Up from the top stream lane is a bay attempt: an empty bay banks the
    /// raccoon, a filled bay or the bank wall bounces it back without harm.</summary>
    public void Hop(int dx, int dy)
    {
        if (this.Dead || this.Frozen)
        {
            return;
        }
        this.HopFlash = HopFlashSeconds;
        if (dy > 0)
        {
            HopUp();
            return;
        }
        if (dy < 0)
        {
            if (this.Row > StartRow)
            {
                this.Row--;
                SnapOffStream();
            }
            return;
        }
        if (dx == 0)
        {
            return;
        }
        if (this.OnStream)
        {
            this.X += dx;
            KillIfOffEdge();
        }
        else
        {
            this.X = Math.Clamp(MathF.Floor(this.X + 0.5f) + dx, 0f, Columns - 1);
        }
    }

    private void HopUp()
    {
        if (this.Row == StreamLastRow)
        {
            TryBank();
            return;
        }
        this.Row++;
        SnapOffStream();
        if (this.Row > this.maxRowReached)
        {
            this.Score += HopPoints * (this.Row - this.maxRowReached);
            this.maxRowReached = this.Row;
        }
    }

    private void TryBank()
    {
        var bay = BayAt(this.X);
        if (bay < 0)
        {
            // Nothing lined up: the hop met the bank wall.
            Bump((int)MathF.Round(this.X));
            return;
        }
        if (this.bays[bay])
        {
            Bump(BayColumns[bay]);
            return;
        }

        this.bays[bay] = true;
        this.BankedTotal++;
        this.LastBankedBay = bay;
        this.BankFlash = BankFlashSeconds;
        this.Score += HopPoints + BayPoints
            + Math.Min(BayTimeBonusMax, (int)this.TimerRemaining * BayTimeBonusPerSecond);

        var cleared = true;
        foreach (var filled in this.bays)
        {
            cleared &= filled;
        }
        if (cleared)
        {
            this.Score += LevelClearBonus;
            this.clearTimer = LevelClearPauseSeconds;
        }
        else
        {
            Respawn();
        }
    }

    private void Bump(int column)
    {
        this.BumpFlash = BumpFlashSeconds;
        this.BumpColumn = Math.Clamp(column, 0, Columns - 1);
    }

    private void Step(float dt)
    {
        this.HopFlash = MathF.Max(0f, this.HopFlash - dt);
        this.BankFlash = MathF.Max(0f, this.BankFlash - dt);
        this.BumpFlash = MathF.Max(0f, this.BumpFlash - dt);

        if (this.clearTimer > 0f)
        {
            this.clearTimer -= dt;
            if (this.clearTimer <= 0f)
            {
                this.Level++;
                Array.Clear(this.bays);
                BuildLanes();
                Respawn();
            }
            return;
        }

        MoveLanes(dt);

        if (this.deathTimer > 0f)
        {
            this.deathTimer -= dt;
            if (this.deathTimer <= 0f)
            {
                if (this.Lives <= 0)
                {
                    this.Dead = true;
                }
                else
                {
                    Respawn();
                }
            }
            return;
        }

        this.TimerRemaining -= dt;
        if (this.TimerRemaining <= 0f)
        {
            this.TimerRemaining = 0f;
            StartDeath();
            return;
        }

        if (this.Row is >= RoadFirstRow and <= RoadLastRow)
        {
            var lane = this.roadLanes[this.Row - RoadFirstRow];
            foreach (var vehicle in lane.Entities)
            {
                if (SpansOverlap(this.X + CollisionMargin, 1f - (2f * CollisionMargin), vehicle.X, vehicle.Length))
                {
                    StartDeath();
                    return;
                }
            }
        }
        else if (this.OnStream)
        {
            var lane = this.streamLanes[this.Row - StreamFirstRow];
            if (!IsSupported(lane))
            {
                StartDeath();
                return;
            }
            this.X += lane.Speed * dt;
            KillIfOffEdge();
        }
    }

    private void MoveLanes(float dt)
    {
        foreach (var lane in this.roadLanes)
        {
            foreach (var entity in lane.Entities)
            {
                entity.X = Wrap(entity.X + (lane.Speed * dt));
            }
        }
        foreach (var lane in this.streamLanes)
        {
            foreach (var entity in lane.Entities)
            {
                entity.X = Wrap(entity.X + (lane.Speed * dt));
            }
        }
    }

    private bool IsSupported(Lane lane)
    {
        var center = this.X + 0.5f;
        foreach (var pad in lane.Entities)
        {
            var d = Wrap(center - pad.X);
            if (d <= pad.Length + PadGripSlack || d >= Columns - PadGripSlack)
            {
                return true;
            }
        }
        return false;
    }

    private void KillIfOffEdge()
    {
        var center = this.X + 0.5f;
        if (center < 0f || center > Columns)
        {
            StartDeath();
        }
    }

    private void StartDeath()
    {
        this.Lives--;
        this.deathTimer = DeathPauseSeconds;
        this.HopFlash = 0f;
    }

    private void Respawn()
    {
        this.Row = StartRow;
        this.X = StartColumn;
        this.TimerRemaining = LifeTimerSeconds;
        this.maxRowReached = StartRow;
        this.HopFlash = 0f;
    }

    private void SnapOffStream()
    {
        if (!this.OnStream)
        {
            this.X = Math.Clamp(MathF.Floor(this.X + 0.5f), 0f, Columns - 1);
        }
    }

    private void BuildLanes()
    {
        this.roadLanes.Clear();
        this.streamLanes.Clear();
        var ramp = MathF.Pow(SpeedRampPerLevel, this.Level - 1);
        for (var i = 0; i < RoadSpeeds.Length; i++)
        {
            var lane = new Lane
            {
                Row = RoadFirstRow + i,
                Speed = RoadSpeeds[i] * ramp * (i % 2 == 0 ? 1f : -1f),
            };
            var count = Math.Min(MaxVehiclesPerLane, RoadBaseCounts[i] + ((this.Level - 1) / DensityRampLevels));
            var spacing = (float)Columns / count;
            var phase = (float)this.random.NextDouble() * Columns;
            for (var v = 0; v < count; v++)
            {
                lane.Entities.Add(new Entity
                {
                    X = Wrap(phase + (v * spacing)),
                    Length = this.random.Next(VehicleMinLength, VehicleMaxLength + 1),
                });
            }
            this.roadLanes.Add(lane);
        }
        for (var i = 0; i < StreamSpeeds.Length; i++)
        {
            var lane = new Lane
            {
                Row = StreamFirstRow + i,
                Speed = StreamSpeeds[i] * ramp * (i % 2 == 0 ? -1f : 1f),
            };
            var spacing = (float)Columns / PadCounts[i];
            var phase = (float)this.random.NextDouble() * Columns;
            for (var p = 0; p < PadCounts[i]; p++)
            {
                lane.Entities.Add(new Entity
                {
                    X = Wrap(phase + (p * spacing)),
                    Length = PadLengths[i],
                });
            }
            this.streamLanes.Add(lane);
        }
    }

    private static float Wrap(float v) => ((v % Columns) + Columns) % Columns;

    /// <summary>Interval intersection on the wrapping lane circle.</summary>
    private static bool SpansOverlap(float aStart, float aLength, float bStart, float bLength)
    {
        var d = Wrap(bStart - aStart);
        return d < aLength || d > Columns - bLength;
    }
}
