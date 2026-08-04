using System;
using System.Collections.Generic;
using System.Numerics;

namespace AetherOS.Apps.MeteorCommand;

/// <summary>Meteor Command: six settlements under a meteor shower, one battery, a finite magazine per
/// wave. Everything lives in a fixed 100x140 unit space so the app can scale it to any phone size.</summary>
public sealed class MeteorGame
{
    public const float Width = 100f;
    public const float Height = 140f;
    public const float GroundY = 130f;
    public const int CityCount = 6;

    private const float BatteryX = Width * 0.5f;
    private const float ShotSpeed = 110f;
    private const float BlastGrowth = 30f;
    private const float BlastMaxRadius = 8.5f;
    private const float BlastHold = 0.22f;
    private const float CityHalfWidth = 5f;

    private const int AmmoPerWave = 28;
    private const int MeteorPoints = 25;
    private const int CityBonus = 100;
    private const int AmmoBonus = 5;
    private const int MaxMeteorsPerWave = 26;
    private const float MaxMeteorSpeed = 22f;
    private const int SplitFromWave = 3;

    /// <summary>Where the six settlements stand, clear of the battery in the middle.</summary>
    private static readonly float[] CityX = [9f, 22f, 35f, 65f, 78f, 91f];

    public sealed class Meteor
    {
        public Vector2 Start;
        public Vector2 Pos;
        public Vector2 Dir;
        public float Speed;
        public bool CanSplit;
        public float SplitY;
    }

    public sealed class Shot
    {
        public Vector2 Pos;
        public Vector2 Target;
        public Vector2 Dir;
    }

    public sealed class Blast
    {
        public Vector2 Center;
        public float Radius;
        public bool Growing;
        public float Hold;
    }

    private readonly Random random = new();
    private readonly List<Meteor> meteors = [];
    private readonly List<Shot> shots = [];
    private readonly List<Blast> blasts = [];
    private readonly bool[] cities = new bool[CityCount];

    private int pendingSpawns;
    private float spawnTimer;
    private float waveBreak;

    public int Score { get; private set; }

    public int Wave { get; private set; }

    public int Ammo { get; private set; }

    public bool Dead { get; private set; }

    /// <summary>Set for a few seconds after a wave clears, so the app can show the bonus banner.</summary>
    public string? Banner { get; private set; }

    public IReadOnlyList<Meteor> Meteors => this.meteors;

    public IReadOnlyList<Shot> Shots => this.shots;

    public IReadOnlyList<Blast> Blasts => this.blasts;

    public bool CityAlive(int index) => this.cities[index];

    public static float CityCenter(int index) => CityX[index];

    public int CitiesLeft
    {
        get
        {
            var alive = 0;
            foreach (var city in this.cities)
            {
                if (city)
                {
                    alive++;
                }
            }
            return alive;
        }
    }

    public void Reset()
    {
        this.meteors.Clear();
        this.shots.Clear();
        this.blasts.Clear();
        Array.Fill(this.cities, true);
        this.Score = 0;
        this.Wave = 0;
        this.Dead = false;
        this.Banner = null;
        this.waveBreak = 0f;
        StartWave();
    }

    private void StartWave()
    {
        this.Wave++;
        this.Ammo = AmmoPerWave;
        this.pendingSpawns = Math.Min(MaxMeteorsPerWave, 7 + (this.Wave * 2));
        this.spawnTimer = 0.6f;
    }

    /// <summary>Fires an interceptor at a point. Ignored without ammo, or when aimed at the ground.</summary>
    public void Fire(Vector2 target)
    {
        if (this.Dead || this.Ammo <= 0 || target.Y > GroundY - 3f)
        {
            return;
        }
        var origin = new Vector2(BatteryX, GroundY - 3f);
        var delta = target - origin;
        if (delta.LengthSquared() < 1f)
        {
            return;
        }
        this.Ammo--;
        this.shots.Add(new Shot { Pos = origin, Target = target, Dir = Vector2.Normalize(delta) });
    }

    public void Tick(float delta)
    {
        if (this.Dead)
        {
            return;
        }
        if (this.waveBreak > 0f)
        {
            this.waveBreak -= delta;
            if (this.waveBreak <= 0f)
            {
                this.Banner = null;
                StartWave();
            }
            return;
        }

        Spawn(delta);
        MoveShots(delta);
        MoveBlasts(delta);
        MoveMeteors(delta);

        if (this.pendingSpawns == 0 && this.meteors.Count == 0 && this.shots.Count == 0 && this.blasts.Count == 0)
        {
            CompleteWave();
        }
    }

    private void Spawn(float delta)
    {
        if (this.pendingSpawns <= 0)
        {
            return;
        }
        this.spawnTimer -= delta;
        if (this.spawnTimer > 0f)
        {
            return;
        }
        this.pendingSpawns--;
        this.spawnTimer = Math.Max(0.35f, 1.5f - (this.Wave * 0.08f)) * (0.6f + ((float)this.random.NextDouble() * 0.8f));

        var start = new Vector2((float)this.random.NextDouble() * Width, -4f);
        var target = PickTarget();
        var speed = Math.Min(MaxMeteorSpeed, 7f + (this.Wave * 1.3f)) * (0.85f + ((float)this.random.NextDouble() * 0.3f));
        this.meteors.Add(new Meteor
        {
            Start = start,
            Pos = start,
            Dir = Vector2.Normalize(target - start),
            Speed = speed,
            CanSplit = this.Wave >= SplitFromWave && this.random.NextDouble() < 0.25,
            SplitY = 40f + ((float)this.random.NextDouble() * 30f),
        });
    }

    private Vector2 PickTarget()
    {
        var alive = new List<int>();
        for (var i = 0; i < CityCount; i++)
        {
            if (this.cities[i])
            {
                alive.Add(i);
            }
        }
        if (alive.Count == 0 || this.random.NextDouble() < 0.15)
        {
            return new Vector2(BatteryX, GroundY);
        }
        return new Vector2(CityX[alive[this.random.Next(alive.Count)]], GroundY);
    }

    private void MoveShots(float delta)
    {
        for (var i = this.shots.Count - 1; i >= 0; i--)
        {
            var shot = this.shots[i];
            var step = ShotSpeed * delta;
            var remaining = Vector2.Distance(shot.Pos, shot.Target);
            if (remaining <= step)
            {
                this.blasts.Add(new Blast { Center = shot.Target, Radius = 0.5f, Growing = true });
                this.shots.RemoveAt(i);
                continue;
            }
            shot.Pos += shot.Dir * step;
        }
    }

    private void MoveBlasts(float delta)
    {
        for (var i = this.blasts.Count - 1; i >= 0; i--)
        {
            var blast = this.blasts[i];
            if (blast.Growing)
            {
                blast.Radius += BlastGrowth * delta;
                if (blast.Radius >= BlastMaxRadius)
                {
                    blast.Radius = BlastMaxRadius;
                    blast.Growing = false;
                    blast.Hold = BlastHold;
                }
            }
            else if (blast.Hold > 0f)
            {
                blast.Hold -= delta;
            }
            else
            {
                blast.Radius -= BlastGrowth * 0.75f * delta;
                if (blast.Radius <= 0f)
                {
                    this.blasts.RemoveAt(i);
                    continue;
                }
            }

            // One blast catching several meteors is the whole skill of the game, so this sweeps them all.
            for (var m = this.meteors.Count - 1; m >= 0; m--)
            {
                if (Vector2.Distance(this.meteors[m].Pos, blast.Center) <= blast.Radius)
                {
                    this.meteors.RemoveAt(m);
                    this.Score += MeteorPoints;
                }
            }
        }
    }

    private void MoveMeteors(float delta)
    {
        for (var i = this.meteors.Count - 1; i >= 0; i--)
        {
            var meteor = this.meteors[i];
            meteor.Pos += meteor.Dir * meteor.Speed * delta;

            if (meteor.CanSplit && meteor.Pos.Y >= meteor.SplitY)
            {
                meteor.CanSplit = false;
                var extra = new Meteor
                {
                    Start = meteor.Pos,
                    Pos = meteor.Pos,
                    Dir = Vector2.Normalize(PickTarget() - meteor.Pos),
                    Speed = meteor.Speed,
                };
                this.meteors.Add(extra);
            }

            if (meteor.Pos.Y < GroundY)
            {
                continue;
            }
            this.meteors.RemoveAt(i);
            this.blasts.Add(new Blast { Center = new Vector2(meteor.Pos.X, GroundY), Radius = 0.5f, Growing = true });
            Impact(meteor.Pos.X);
        }
    }

    private void Impact(float x)
    {
        for (var i = 0; i < CityCount; i++)
        {
            if (this.cities[i] && Math.Abs(CityX[i] - x) <= CityHalfWidth)
            {
                this.cities[i] = false;
            }
        }
        if (this.CitiesLeft == 0)
        {
            this.Dead = true;
        }
    }

    private void CompleteWave()
    {
        var bonus = (this.CitiesLeft * CityBonus) + (this.Ammo * AmmoBonus);
        this.Score += bonus;
        this.Banner = bonus.ToString();
        this.waveBreak = 2.2f;
    }
}
