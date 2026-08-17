using System;
using System.Collections.Generic;
using System.Numerics;

namespace AetherOS.Apps.SkySwarm;

public enum SwarmKind
{
    Drone,
    Raptor,
    Warden,
}

public enum SwarmState
{
    Waiting,
    FlyIn,
    Parked,
    Diving,
    Beam,
    Returning,
    Gone,
}

/// <summary>Sky Swarm: enemies fly in along bezier paths to a formation that breathes in place, then peel
/// off in curved dive runs. A diving Warden can snatch the fighter with a tractor beam; shooting the captor
/// on a later dive wins the ship back and docks it alongside as a dual fighter. Every 3rd stage is a
/// no-fire challenge of ships flying through in trains.</summary>
public sealed class SkySwarmGame
{
    public const float Width = 100f;
    public const float Height = 140f;
    public const float ShipWidth = 8f;
    public const float ShipHeight = 6f;
    public const float PlayerWidth = 9f;
    public const float PlayerHeight = 4.5f;
    public const float PlayerRowY = 126f;
    public const float BeamTopHalfWidth = 2f;
    public const float BeamBottomHalfWidth = 10f;

    public const int DroneParkedPoints = 50;
    public const int DroneDivingPoints = 100;
    public const int RaptorParkedPoints = 80;
    public const int RaptorDivingPoints = 160;
    public const int WardenParkedPoints = 150;
    public const int WardenDivingPoints = 300;
    public const int RescueBonus = 1000;
    public const int ChallengeHitPoints = 100;
    public const int PerfectBonus = 1000;
    public const int StageClearBonus = 200;
    public const int ChallengeShipCount = 20;

    private const int StartLives = 3;
    private const int ChallengeEveryNth = 3;
    private const int BaseDronesPerRow = 5;
    private const int RaptorsPerRow = 4;
    private const int WardenCount = 2;
    private const int ExtraDronesPerStage = 2;
    private const int MaxExtraDrones = 6;

    private const float CenterX = 50f;
    private const float ColumnPitch = 12f;
    private const float WardenRowY = 15f;
    private const float RaptorRowAY = 25f;
    private const float RaptorRowBY = 35f;
    private const float DroneRowAY = 45f;
    private const float DroneRowBY = 55f;
    private const float DroneRowCY = 65f;
    private const float BreatheAmplitude = 0.08f;
    private const float BreatheRate = 0.9f;
    private static readonly Vector2 BreatheCenter = new(CenterX, 38f);

    private const float PlayerSpeed = 62f;
    private const float BulletSpeed = 120f;
    private const float ShotSpeed = 45f;
    private const int SingleShotCap = 2;
    private const int DualShotCap = 3;
    private const float DualSpread = 4.5f;
    private const int MaxEnemyShots = 4;
    private const int MaxAirborneDivers = 3;

    private const float FlyInSeconds = 1.9f;
    private const float FirstWaveDelay = 0.8f;
    private const float WaveInterval = 1.5f;
    private const float ShipStagger = 0.16f;
    private const float ReturnSeconds = 1.6f;
    private const float ChallengeFlySeconds = 3.2f;
    private const float ChallengeWaveInterval = 2.4f;
    private const float ChallengeStagger = 0.24f;

    private const float BeamHoverY = 82f;
    private const float BeamExtendSeconds = 0.5f;
    private const float BeamHoldSeconds = 1.6f;
    private const float BeamRetractSeconds = 0.5f;
    private const float WardenDiveChance = 0.22f;
    private const float BeamChance = 0.65f;

    private const float CaptureSeconds = 1.5f;
    private const float RescueSeconds = 1.6f;
    private const float RespawnSeconds = 1.2f;
    private const float ResultBannerSeconds = 2.2f;
    private const float FrameFlipSeconds = 0.28f;
    private const float FirstDiveDelay = 3.5f;
    private const float DiveRetrySeconds = 0.4f;

    public sealed class Ship
    {
        public SwarmKind Kind;
        public SwarmState State;
        public Vector2 Pos;
        public Vector2 Slot;
        public Vector2 P0;
        public Vector2 P1;
        public Vector2 P2;
        public Vector2 P3;
        public bool TargetsSlot;
        public float FlyAt;
        public float PathTime;
        public float PathDuration;
        public float BeamTime;
        public bool BeamRun;
        public bool HoldsCaptive;
        public float FireAt1 = -1f;
        public float FireAt2 = -1f;
    }

    public sealed class Shot
    {
        public Vector2 Pos;
        public Vector2 Vel;
    }

    private readonly Random random = new();
    private readonly List<Ship> ships = [];
    private readonly List<Vector2> bullets = [];
    private readonly List<Shot> shots = [];

    private float breathePhase;
    private float diveTimer;
    private float respawnTimer;
    private float resultTimer;
    private float captureTimer;
    private float rescueTimer;
    private float frameTimer;
    private Vector2 captureFrom;
    private Vector2 rescueFrom;

    public int Score { get; private set; }

    public int Stage { get; private set; }

    public int Lives { get; private set; }

    public bool Dead { get; private set; }

    public bool Dual { get; private set; }

    public bool DualAchieved { get; private set; }

    public float PlayerX { get; private set; }

    /// <summary>Flips on a timer; the wing-flap frame for every sprite on the field.</summary>
    public bool AnimFrame { get; private set; }

    public bool IsChallenge { get; private set; }

    public float StageTime { get; private set; }

    public int ChallengeHits { get; private set; }

    public int LastChallengeHits { get; private set; }

    public bool LastChallengeWasPerfect { get; private set; }

    public bool CaptureActive { get; private set; }

    public Ship? CaptureWarden { get; private set; }

    public bool RescueActive { get; private set; }

    public float RespawnTimer => this.respawnTimer;

    public float ResultTimer => this.resultTimer;

    public IReadOnlyList<Ship> Ships => this.ships;

    public IReadOnlyList<Vector2> Bullets => this.bullets;

    public IReadOnlyList<Shot> Shots => this.shots;

    public Vector2 CapturePos
    {
        get
        {
            var t = Smooth(Math.Clamp(this.captureTimer / CaptureSeconds, 0f, 1f));
            var target = this.CaptureWarden?.Pos ?? this.captureFrom;
            return Vector2.Lerp(this.captureFrom, target, t);
        }
    }

    public Vector2 RescuePos
    {
        get
        {
            var t = Smooth(Math.Clamp(this.rescueTimer / RescueSeconds, 0f, 1f));
            return Vector2.Lerp(this.rescueFrom, PlayerCenter, t);
        }
    }

    private Vector2 PlayerCenter => new(this.PlayerX, PlayerRowY - (PlayerHeight * 0.5f));

    private static float Smooth(float t) => t * t * (3f - (2f * t));

    /// <summary>How far a Warden's tractor beam has extended, 0 to 1, over its extend/hold/retract phases.</summary>
    public float BeamExtent(Ship ship)
    {
        if (ship.State != SwarmState.Beam)
        {
            return 0f;
        }
        var t = ship.BeamTime;
        if (t < BeamExtendSeconds)
        {
            return t / BeamExtendSeconds;
        }
        if (t < BeamExtendSeconds + BeamHoldSeconds)
        {
            return 1f;
        }
        var retract = (t - BeamExtendSeconds - BeamHoldSeconds) / BeamRetractSeconds;
        return Math.Clamp(1f - retract, 0f, 1f);
    }

    public void Reset()
    {
        this.Score = 0;
        this.Lives = StartLives;
        this.Dead = false;
        this.Dual = false;
        this.DualAchieved = false;
        this.CaptureActive = false;
        this.CaptureWarden = null;
        this.RescueActive = false;
        this.respawnTimer = 0f;
        this.resultTimer = 0f;
        this.PlayerX = CenterX;
        StartStage(1);
    }

    public void MoveLeft(float delta)
    {
        if (this.CaptureActive)
        {
            return;
        }
        this.PlayerX = Math.Max(PlayerWidth * 0.5f, this.PlayerX - (PlayerSpeed * delta));
    }

    public void MoveRight(float delta)
    {
        if (this.CaptureActive)
        {
            return;
        }
        this.PlayerX = Math.Min(Width - (PlayerWidth * 0.5f), this.PlayerX + (PlayerSpeed * delta));
    }

    /// <summary>Two bullets in the air, three while dual; a dual press fires from both barrels.</summary>
    public void Fire()
    {
        if (this.Dead || this.CaptureActive || this.respawnTimer > 0f)
        {
            return;
        }
        var cap = this.Dual ? DualShotCap : SingleShotCap;
        if (this.bullets.Count >= cap)
        {
            return;
        }
        var noseY = PlayerRowY - PlayerHeight;
        if (this.Dual)
        {
            this.bullets.Add(new Vector2(this.PlayerX - DualSpread, noseY));
            if (this.bullets.Count < cap)
            {
                this.bullets.Add(new Vector2(this.PlayerX + DualSpread, noseY));
            }
        }
        else
        {
            this.bullets.Add(new Vector2(this.PlayerX, noseY));
        }
    }

    public void Tick(float delta)
    {
        if (this.Dead)
        {
            return;
        }

        this.frameTimer += delta;
        if (this.frameTimer >= FrameFlipSeconds)
        {
            this.frameTimer -= FrameFlipSeconds;
            this.AnimFrame = !this.AnimFrame;
        }

        if (this.CaptureActive)
        {
            this.captureTimer += delta;
            if (this.captureTimer >= CaptureSeconds)
            {
                CompleteCapture();
            }
            return;
        }
        if (this.respawnTimer > 0f)
        {
            this.respawnTimer -= delta;
            return;
        }
        if (this.resultTimer > 0f)
        {
            this.resultTimer -= delta;
            if (this.resultTimer <= 0f)
            {
                StartStage(this.Stage + 1);
            }
            return;
        }

        this.StageTime += delta;
        this.breathePhase += delta * BreatheRate;

        if (this.RescueActive)
        {
            this.rescueTimer += delta;
            if (this.rescueTimer >= RescueSeconds)
            {
                CompleteRescue();
            }
        }
        if (!this.IsChallenge)
        {
            TickDives(delta);
        }

        // Ships move with the projectiles: a diver crosses the collision band in well under a stuttered
        // frame, so advancing it whole would let it pass through the player untested.
        var steps = Math.Clamp((int)Math.Ceiling(delta * BulletSpeed / (ShipHeight * 0.5f)), 1, 16);
        var step = delta / steps;
        for (var i = 0; i < steps; i++)
        {
            TickShips(step);
            MoveBullets(step);
            MoveShots(step);
            if (this.Dead || this.CaptureActive || this.respawnTimer > 0f)
            {
                return;
            }
        }

        CheckStageEnd();
    }

    private void StartStage(int stage)
    {
        this.Stage = stage;
        this.StageTime = 0f;
        this.IsChallenge = stage % ChallengeEveryNth == 0;
        this.ChallengeHits = 0;
        this.breathePhase = 0f;
        this.diveTimer = FirstDiveDelay;
        this.ships.Clear();
        this.bullets.Clear();
        this.shots.Clear();
        if (this.IsChallenge)
        {
            BuildChallenge();
        }
        else
        {
            BuildFormation();
        }
    }

    private void BuildFormation()
    {
        AddRow(SwarmKind.Drone, BaseDronesPerRow, DroneRowAY);
        AddRow(SwarmKind.Drone, BaseDronesPerRow, DroneRowBY);
        var extra = Math.Min(MaxExtraDrones, (this.Stage - 1) * ExtraDronesPerStage);
        if (extra > 0)
        {
            AddRow(SwarmKind.Drone, extra, DroneRowCY);
        }
        AddRow(SwarmKind.Raptor, RaptorsPerRow, RaptorRowAY);
        AddRow(SwarmKind.Raptor, RaptorsPerRow, RaptorRowBY);
        AddRow(SwarmKind.Warden, WardenCount, WardenRowY);
        AssignWaves();
    }

    private void AddRow(SwarmKind kind, int count, float y)
    {
        var left = CenterX - ((count - 1) * ColumnPitch * 0.5f);
        for (var i = 0; i < count; i++)
        {
            this.ships.Add(new Ship
            {
                Kind = kind,
                State = SwarmState.Waiting,
                Slot = new Vector2(left + (i * ColumnPitch), y),
                Pos = new Vector2(-20f, -20f),
            });
        }
    }

    /// <summary>Chunks the arrival order into near-even waves of at most six ships and hands each ship its
    /// curved entry path, alternating sides per wave.</summary>
    private void AssignWaves()
    {
        var total = this.ships.Count;
        var waves = Math.Clamp((int)Math.Ceiling(total / 6.0), 2, 5);
        var index = 0;
        for (var wave = 0; wave < waves; wave++)
        {
            var size = (total / waves) + (wave < total % waves ? 1 : 0);
            var fromLeft = wave % 2 == 0;
            for (var i = 0; i < size; i++, index++)
            {
                var ship = this.ships[index];
                ship.FlyAt = FirstWaveDelay + (wave * WaveInterval) + (i * ShipStagger);
                ship.P0 = fromLeft ? new Vector2(-10f, 30f) : new Vector2(110f, 30f);
                ship.P1 = fromLeft ? new Vector2(35f, -18f) : new Vector2(65f, -18f);
                ship.P2 = fromLeft ? new Vector2(88f, 58f) : new Vector2(12f, 58f);
                ship.TargetsSlot = true;
                ship.PathDuration = FlyInSeconds;
            }
        }
    }

    private void BuildChallenge()
    {
        const int wavesCount = 4;
        const int perWave = ChallengeShipCount / wavesCount;
        for (var wave = 0; wave < wavesCount; wave++)
        {
            var fromLeft = wave % 2 == 0;
            var entryY = 16f + (wave * 7f);
            for (var i = 0; i < perWave; i++)
            {
                var kind = wave == wavesCount - 1 && i < WardenCount
                    ? SwarmKind.Warden
                    : (i % 2 == 1 ? SwarmKind.Raptor : SwarmKind.Drone);
                this.ships.Add(new Ship
                {
                    Kind = kind,
                    State = SwarmState.Waiting,
                    Pos = new Vector2(-20f, -20f),
                    FlyAt = FirstWaveDelay + (wave * ChallengeWaveInterval) + (i * ChallengeStagger),
                    P0 = fromLeft ? new Vector2(-10f, entryY) : new Vector2(110f, entryY),
                    P1 = fromLeft ? new Vector2(30f, 126f) : new Vector2(70f, 126f),
                    P2 = fromLeft ? new Vector2(74f, -12f) : new Vector2(26f, -12f),
                    P3 = fromLeft ? new Vector2(112f, 88f) : new Vector2(-12f, 88f),
                    TargetsSlot = false,
                    PathDuration = ChallengeFlySeconds,
                });
            }
        }
    }

    private void TickShips(float delta)
    {
        foreach (var ship in this.ships)
        {
            switch (ship.State)
            {
                case SwarmState.Waiting:
                    if (this.StageTime >= ship.FlyAt)
                    {
                        ship.State = SwarmState.FlyIn;
                        ship.PathTime = 0f;
                        ship.Pos = ship.P0;
                    }
                    break;
                case SwarmState.FlyIn:
                    ship.PathTime += delta;
                    if (ship.PathTime >= ship.PathDuration)
                    {
                        if (ship.TargetsSlot)
                        {
                            ship.State = SwarmState.Parked;
                            ship.Pos = SlotPos(ship);
                        }
                        else
                        {
                            ship.State = SwarmState.Gone;
                        }
                    }
                    else
                    {
                        ship.Pos = PathPos(ship);
                    }
                    break;
                case SwarmState.Parked:
                    ship.Pos = SlotPos(ship);
                    break;
                case SwarmState.Diving:
                    ship.PathTime += delta;
                    TickDiveFire(ship);
                    if (ship.PathTime >= ship.PathDuration)
                    {
                        if (ship.BeamRun)
                        {
                            ship.State = SwarmState.Beam;
                            ship.BeamTime = 0f;
                            ship.Pos = ship.P3;
                        }
                        else
                        {
                            BeginReturn(ship);
                        }
                    }
                    else
                    {
                        ship.Pos = PathPos(ship);
                        CheckPlayerCollision(ship);
                    }
                    break;
                case SwarmState.Beam:
                    ship.BeamTime += delta;
                    TryBeamCapture(ship);
                    if (ship.State == SwarmState.Beam
                        && ship.BeamTime >= BeamExtendSeconds + BeamHoldSeconds + BeamRetractSeconds)
                    {
                        BeginReturn(ship);
                    }
                    break;
                case SwarmState.Returning:
                    ship.PathTime += delta;
                    if (ship.PathTime >= ship.PathDuration)
                    {
                        ship.State = SwarmState.Parked;
                        ship.Pos = SlotPos(ship);
                    }
                    else
                    {
                        ship.Pos = PathPos(ship);
                    }
                    break;
            }
        }
    }

    /// <summary>Where a slot sits right now, breathing included, so arrivals land without a snap.</summary>
    private Vector2 SlotPos(Ship ship)
    {
        var scale = 1f + (BreatheAmplitude * MathF.Sin(this.breathePhase));
        return BreatheCenter + ((ship.Slot - BreatheCenter) * scale);
    }

    private Vector2 PathPos(Ship ship)
    {
        var t = Math.Clamp(ship.PathTime / ship.PathDuration, 0f, 1f);
        var target = ship.TargetsSlot ? SlotPos(ship) : ship.P3;
        var u = 1f - t;
        return (u * u * u * ship.P0)
            + (3f * u * u * t * ship.P1)
            + (3f * u * t * t * ship.P2)
            + (t * t * t * target);
    }

    private void TickDives(float delta)
    {
        this.diveTimer -= delta;
        if (this.diveTimer > 0f)
        {
            return;
        }
        this.diveTimer = DiveRetrySeconds;
        if (AirborneCount() >= MaxAirborneDivers)
        {
            return;
        }
        var parkedWardens = CollectParked(onlyWardens: true);
        var parkedEscorts = CollectParked(onlyWardens: false);
        if (parkedWardens.Count == 0 && parkedEscorts.Count == 0)
        {
            return;
        }

        if (parkedWardens.Count > 0 && (parkedEscorts.Count == 0 || this.random.NextDouble() < WardenDiveChance))
        {
            var warden = parkedWardens[this.random.Next(parkedWardens.Count)];
            var beamEligible = !this.Dual && !this.RescueActive && !AnyCaptive();
            StartDive(warden, beamEligible && this.random.NextDouble() < BeamChance);
        }
        else
        {
            var diver = parkedEscorts[this.random.Next(parkedEscorts.Count)];
            StartDive(diver, beamRun: false);
            parkedEscorts.Remove(diver);
            if (parkedEscorts.Count > 0 && this.random.NextDouble() < PairChance())
            {
                StartDive(parkedEscorts[this.random.Next(parkedEscorts.Count)], beamRun: false);
            }
        }
        this.diveTimer = DiveInterval() * (0.8f + ((float)this.random.NextDouble() * 0.5f));
    }

    private float DiveInterval() => Math.Max(0.85f, 2.6f - (0.2f * (this.Stage - 1)));

    private float PairChance() => Math.Min(0.65f, 0.1f + (0.09f * (this.Stage - 1)));

    private float DiveSeconds() => Math.Max(1.7f, 2.5f - (0.07f * (this.Stage - 1)));

    private void StartDive(Ship ship, bool beamRun)
    {
        ship.State = SwarmState.Diving;
        ship.PathTime = 0f;
        ship.BeamRun = beamRun;
        ship.TargetsSlot = false;
        ship.P0 = ship.Pos;
        var side = ship.Pos.X < CenterX ? -1f : 1f;
        if (beamRun)
        {
            // Follows the player's own travel range: a tighter clamp leaves a sliver at each wall the
            // beam can never reach, which is a permanent safe spot against the signature mechanic.
            var hoverX = Math.Clamp(this.PlayerX, PlayerWidth * 0.5f, Width - (PlayerWidth * 0.5f));
            ship.PathDuration = DiveSeconds() * 0.9f;
            ship.P1 = new Vector2(ship.P0.X + (side * 22f), ship.P0.Y + 26f);
            ship.P2 = new Vector2(hoverX - (side * 18f), 60f);
            ship.P3 = new Vector2(hoverX, BeamHoverY);
            ship.FireAt1 = ship.PathDuration * 0.3f;
            ship.FireAt2 = -1f;
        }
        else
        {
            var exitX = Math.Clamp(this.PlayerX + (((float)this.random.NextDouble() * 24f) - 12f), 6f, 94f);
            ship.PathDuration = DiveSeconds();
            ship.P1 = new Vector2(ship.P0.X + (side * 26f), ship.P0.Y + 30f);
            ship.P2 = new Vector2(exitX + (side * 20f), 96f);
            ship.P3 = new Vector2(exitX, Height + 10f);
            ship.FireAt1 = ship.PathDuration * 0.3f;
            ship.FireAt2 = ship.Kind == SwarmKind.Drone ? -1f : ship.PathDuration * 0.55f;
        }
    }

    private void BeginReturn(Ship ship)
    {
        ship.State = SwarmState.Returning;
        ship.PathTime = 0f;
        ship.PathDuration = ReturnSeconds;
        ship.BeamRun = false;
        ship.FireAt1 = -1f;
        ship.FireAt2 = -1f;
        var from = ship.Pos.Y >= Height - 1f
            ? new Vector2(Math.Clamp(ship.Pos.X, 5f, 95f), -10f)
            : ship.Pos;
        ship.P0 = from;
        ship.P1 = new Vector2(from.X, (from.Y + ship.Slot.Y) * 0.5f);
        ship.P2 = new Vector2(ship.Slot.X, ship.Slot.Y - 18f);
        ship.TargetsSlot = true;
    }

    private void TickDiveFire(Ship ship)
    {
        if (ship.FireAt1 >= 0f && ship.PathTime >= ship.FireAt1)
        {
            ship.FireAt1 = -1f;
            FireShot(ship);
        }
        if (ship.FireAt2 >= 0f && ship.PathTime >= ship.FireAt2)
        {
            ship.FireAt2 = -1f;
            FireShot(ship);
        }
    }

    private void FireShot(Ship ship)
    {
        if (this.shots.Count >= MaxEnemyShots)
        {
            return;
        }
        var aim = PlayerCenter - ship.Pos;
        if (aim.Y < 10f)
        {
            aim.Y = 10f;
        }
        this.shots.Add(new Shot { Pos = ship.Pos, Vel = Vector2.Normalize(aim) * ShotSpeed });
    }

    private void TryBeamCapture(Ship ship)
    {
        if (this.Dual || this.CaptureActive || this.RescueActive)
        {
            return;
        }
        var t = ship.BeamTime;
        if (t < BeamExtendSeconds || t > BeamExtendSeconds + BeamHoldSeconds)
        {
            return;
        }
        if (Math.Abs(this.PlayerX - ship.Pos.X) > BeamBottomHalfWidth)
        {
            return;
        }
        this.CaptureActive = true;
        this.CaptureWarden = ship;
        this.captureTimer = 0f;
        this.captureFrom = PlayerCenter;
        this.bullets.Clear();
        this.shots.Clear();
    }

    private void CompleteCapture()
    {
        this.CaptureActive = false;
        if (this.CaptureWarden is { } warden)
        {
            warden.HoldsCaptive = true;
            BeginReturn(warden);
        }
        this.CaptureWarden = null;
        LoseLife();
    }

    private void StartRescue(Vector2 from)
    {
        this.RescueActive = true;
        this.rescueTimer = 0f;
        this.rescueFrom = from;
    }

    private void CompleteRescue()
    {
        this.RescueActive = false;
        this.Dual = true;
        this.DualAchieved = true;
        this.Score += RescueBonus;
    }

    private void CheckPlayerCollision(Ship ship)
    {
        if (this.respawnTimer > 0f)
        {
            return;
        }
        var halfW = (this.Dual ? PlayerWidth : PlayerWidth * 0.5f) + (ShipWidth * 0.5f);
        var dy = Math.Abs(ship.Pos.Y - (PlayerRowY - (PlayerHeight * 0.5f)));
        if (dy > (PlayerHeight + ShipHeight) * 0.5f || Math.Abs(ship.Pos.X - this.PlayerX) > halfW)
        {
            return;
        }
        KillShip(ship);
        PlayerHit();
    }

    private void MoveBullets(float step)
    {
        for (var i = this.bullets.Count - 1; i >= 0; i--)
        {
            var bullet = this.bullets[i];
            bullet.Y -= BulletSpeed * step;
            if (bullet.Y < -4f)
            {
                this.bullets.RemoveAt(i);
                continue;
            }
            this.bullets[i] = bullet;
            if (HitShipAt(bullet) is { } hit)
            {
                this.bullets.RemoveAt(i);
                KillShip(hit);
            }
        }
    }

    private Ship? HitShipAt(Vector2 point)
    {
        foreach (var ship in this.ships)
        {
            if (ship.State is SwarmState.Waiting or SwarmState.Gone)
            {
                continue;
            }
            if (Math.Abs(point.X - ship.Pos.X) <= ShipWidth * 0.5f
                && Math.Abs(point.Y - ship.Pos.Y) <= ShipHeight * 0.5f)
            {
                return ship;
            }
        }
        return null;
    }

    /// <summary>A captor shot mid-flight releases its captive for the rescue; shot while parked, the
    /// captive dies with it and no bonus is paid.</summary>
    private void KillShip(Ship ship)
    {
        var parked = ship.State == SwarmState.Parked;
        if (this.IsChallenge)
        {
            this.Score += ChallengeHitPoints;
            this.ChallengeHits++;
        }
        else
        {
            this.Score += ship.Kind switch
            {
                SwarmKind.Drone => parked ? DroneParkedPoints : DroneDivingPoints,
                SwarmKind.Raptor => parked ? RaptorParkedPoints : RaptorDivingPoints,
                _ => parked ? WardenParkedPoints : WardenDivingPoints,
            };
        }
        if (ship.HoldsCaptive && !parked)
        {
            StartRescue(ship.Pos);
        }
        ship.HoldsCaptive = false;
        ship.State = SwarmState.Gone;
    }

    private void MoveShots(float step)
    {
        for (var i = this.shots.Count - 1; i >= 0; i--)
        {
            var shot = this.shots[i];
            shot.Pos += shot.Vel * step;
            if (shot.Pos.Y > Height + 4f || shot.Pos.X < -4f || shot.Pos.X > Width + 4f)
            {
                this.shots.RemoveAt(i);
                continue;
            }
            var halfW = this.Dual ? PlayerWidth : PlayerWidth * 0.5f;
            if (shot.Pos.Y >= PlayerRowY - PlayerHeight && shot.Pos.Y <= PlayerRowY
                && Math.Abs(shot.Pos.X - this.PlayerX) <= halfW)
            {
                this.shots.RemoveAt(i);
                // Losing a life clears this very list, so stop rather than walk a cursor into the void.
                PlayerHit();
                return;
            }
        }
    }

    /// <summary>A dual hit costs the docked ship, not a life; the rescue absorbed the blow.</summary>
    private void PlayerHit()
    {
        if (this.respawnTimer > 0f || this.CaptureActive)
        {
            return;
        }
        if (this.Dual)
        {
            this.Dual = false;
            return;
        }
        LoseLife();
    }

    private void LoseLife()
    {
        this.Lives--;
        this.bullets.Clear();
        this.shots.Clear();
        if (this.Lives <= 0)
        {
            this.Dead = true;
            return;
        }
        this.PlayerX = CenterX;
        this.respawnTimer = RespawnSeconds;
    }

    private void CheckStageEnd()
    {
        foreach (var ship in this.ships)
        {
            if (ship.State != SwarmState.Gone)
            {
                return;
            }
        }
        if (this.IsChallenge)
        {
            this.LastChallengeHits = this.ChallengeHits;
            this.LastChallengeWasPerfect = this.ChallengeHits >= ChallengeShipCount;
            if (this.LastChallengeWasPerfect)
            {
                this.Score += PerfectBonus;
            }
            this.bullets.Clear();
            this.shots.Clear();
            this.resultTimer = ResultBannerSeconds;
        }
        else
        {
            this.Score += StageClearBonus;
            StartStage(this.Stage + 1);
        }
    }

    private int AirborneCount()
    {
        var count = 0;
        foreach (var ship in this.ships)
        {
            if (ship.State is SwarmState.Diving or SwarmState.Beam or SwarmState.Returning)
            {
                count++;
            }
        }
        return count;
    }

    private List<Ship> CollectParked(bool onlyWardens)
    {
        var list = new List<Ship>();
        foreach (var ship in this.ships)
        {
            if (ship.State == SwarmState.Parked && (ship.Kind == SwarmKind.Warden) == onlyWardens)
            {
                list.Add(ship);
            }
        }
        return list;
    }

    private bool AnyCaptive()
    {
        foreach (var ship in this.ships)
        {
            if (ship.State != SwarmState.Gone && ship.HoldsCaptive)
            {
                return true;
            }
        }
        return false;
    }
}
