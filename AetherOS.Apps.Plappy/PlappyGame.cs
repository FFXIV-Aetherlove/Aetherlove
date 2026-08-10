using System;
using System.Collections.Generic;

namespace AetherOS.Apps.Plappy;

/// <summary>Plappy Birb: gravity, one flap impulse, and a corridor of pillars that never stops narrowing.
/// Everything lives in a fixed 100x140 unit space so the app can scale it to any phone size. No drawing and
/// no ImGui, so the rules stay readable and the renderer stays dumb.</summary>
public sealed class PlappyGame
{
    public const float Width = 100f;
    public const float Height = 140f;
    public const float GroundY = 130f;

    /// <summary>The bird holds this column; the world moves past it instead.</summary>
    public const float BirdX = 30f;
    public const float BirdRadius = 3.4f;
    public const float PillarWidth = 14f;

    private const float Gravity = 210f;
    private const float FlapImpulse = -66f;
    private const float MaxFallSpeed = 128f;

    /// <summary>How the run tightens. Every <see cref="PillarsPerTier"/> pillars the world moves one step
    /// from the "start" figures toward the "hard" ones, and stops at <see cref="MaxTier"/>.</summary>
    public const int PillarsPerTier = 5;
    public const int MaxTier = 8;

    private const float StartSpeed = 27f;
    private const float HardSpeed = 47f;
    private const float StartGap = 37f;
    private const float HardGap = 21f;
    private const float StartSpacing = 60f;
    private const float HardSpacing = 44f;

    /// <summary>From this tier on the gaps stop holding still, drifting a little further each tier.</summary>
    private const int DriftFromTier = 5;
    private const float DriftPerTier = 2.6f;
    private const float DriftSpeed = 1.15f;

    /// <summary>Points for clearing a pillar, plus a bonus for threading the middle of its gap.</summary>
    public const int PillarPoints = 10;
    public const int ThreadBonus = 5;
    private const float ThreadBand = 0.18f;

    /// <summary>Room above and below every gap, so a gap centre never hugs the ceiling or the ground.</summary>
    private const float GapMargin = 16f;

    /// <summary>The empty runway ahead of the first pillar, so a run never opens with a wall in your face.</summary>
    private const float LeadIn = 78f;

    public sealed class Pillar
    {
        public float X;
        public float GapCenter;
        public float GapHeight;
        public float DriftAmplitude;
        public float DriftPhase;
        public bool Cleared;

        /// <summary>Where the gap sits right now; the drift is a function of the pillar's own phase so the
        /// renderer and the collision test can never disagree about it.</summary>
        public float LiveGapCenter { get; internal set; }

        public float GapTop => this.LiveGapCenter - (this.GapHeight * 0.5f);

        public float GapBottom => this.LiveGapCenter + (this.GapHeight * 0.5f);
    }

    private readonly Random random = new();
    private readonly List<Pillar> pillars = [];

    private float spawnX;
    private float driftClock;

    public IReadOnlyList<Pillar> Pillars => this.pillars;

    public float BirdY { get; private set; }

    public float BirdVelocity { get; private set; }

    public int Score { get; private set; }

    public int PillarsCleared { get; private set; }

    /// <summary>Seconds of actual play, which is the clamped tick time rather than wall clock: a frame
    /// hitch slows the world down instead of teleporting the bird, and the run must be measured the same
    /// way the pillars were.</summary>
    public float ElapsedSeconds { get; private set; }

    public bool Dead { get; private set; }

    /// <summary>True until the first flap: the bird hovers and the world holds still, so opening the app
    /// mid-conversation does not cost you a run.</summary>
    public bool Waiting { get; private set; }

    /// <summary>Set for a moment after a threaded gap, so the app can flash the bonus.</summary>
    public float ThreadFlash { get; private set; }

    public int Tier => Math.Min(MaxTier, this.PillarsCleared / PillarsPerTier);

    public float Speed => Lerp(StartSpeed, HardSpeed, TierFraction);

    private float TierFraction => this.Tier / (float)MaxTier;

    private float CurrentGap => Lerp(StartGap, HardGap, TierFraction);

    private float CurrentSpacing => Lerp(StartSpacing, HardSpacing, TierFraction);

    public void Reset()
    {
        this.pillars.Clear();
        this.BirdY = Height * 0.38f;
        this.BirdVelocity = 0f;
        this.Score = 0;
        this.PillarsCleared = 0;
        this.ElapsedSeconds = 0f;
        this.Dead = false;
        this.Waiting = true;
        this.ThreadFlash = 0f;
        this.spawnX = LeadIn;
        this.driftClock = 0f;
        SpawnPillar();
    }

    public void Flap()
    {
        if (this.Dead)
        {
            return;
        }
        this.Waiting = false;
        this.BirdVelocity = FlapImpulse;
    }

    public void Tick(float deltaSeconds)
    {
        if (this.Dead)
        {
            return;
        }
        // A long stall (alt-tab, loading screen) must not fast-forward the bird into a pillar.
        var delta = Math.Min(deltaSeconds, 0.05f);
        this.ThreadFlash = Math.Max(0f, this.ThreadFlash - delta);

        if (this.Waiting)
        {
            this.driftClock += delta;
            UpdateGapCenters();
            return;
        }

        this.driftClock += delta;
        this.ElapsedSeconds += delta;
        this.BirdVelocity = Math.Min(MaxFallSpeed, this.BirdVelocity + (Gravity * delta));
        this.BirdY += this.BirdVelocity * delta;

        // The ceiling stops the bird rather than killing it; only the ground and the pillars do that.
        if (this.BirdY < BirdRadius)
        {
            this.BirdY = BirdRadius;
            this.BirdVelocity = 0f;
        }
        if (this.BirdY + BirdRadius >= GroundY)
        {
            this.BirdY = GroundY - BirdRadius;
            this.Dead = true;
            return;
        }

        Advance(delta);
        UpdateGapCenters();
        ScoreCleared();
        if (HitsAnyPillar())
        {
            this.Dead = true;
        }
    }

    private void Advance(float delta)
    {
        var travel = this.Speed * delta;
        this.spawnX -= travel;
        foreach (var pillar in this.pillars)
        {
            pillar.X -= travel;
        }
        this.pillars.RemoveAll(p => p.X + PillarWidth < -PillarWidth);
        while (this.spawnX <= Width)
        {
            SpawnPillar();
        }
    }

    private void SpawnPillar()
    {
        var gap = CurrentGap;
        var drift = this.Tier < DriftFromTier ? 0f : (this.Tier - DriftFromTier + 1) * DriftPerTier;
        var lowest = GapMargin + (gap * 0.5f) + drift;
        var highest = GroundY - GapMargin - (gap * 0.5f) - drift;
        var center = highest <= lowest
            ? (lowest + highest) * 0.5f
            : lowest + ((float)this.random.NextDouble() * (highest - lowest));

        var pillar = new Pillar
        {
            X = this.spawnX,
            GapCenter = center,
            GapHeight = gap,
            DriftAmplitude = drift,
            DriftPhase = (float)this.random.NextDouble() * MathF.Tau,
        };
        pillar.LiveGapCenter = center;
        this.pillars.Add(pillar);
        this.spawnX += CurrentSpacing;
    }

    private void UpdateGapCenters()
    {
        foreach (var pillar in this.pillars)
        {
            pillar.LiveGapCenter = pillar.DriftAmplitude <= 0f
                ? pillar.GapCenter
                : pillar.GapCenter + (MathF.Sin((this.driftClock * DriftSpeed) + pillar.DriftPhase) * pillar.DriftAmplitude);
        }
    }

    private void ScoreCleared()
    {
        foreach (var pillar in this.pillars)
        {
            if (pillar.Cleared || pillar.X + PillarWidth > BirdX)
            {
                continue;
            }
            pillar.Cleared = true;
            this.PillarsCleared++;
            this.Score += PillarPoints;
            if (Math.Abs(this.BirdY - pillar.LiveGapCenter) <= pillar.GapHeight * ThreadBand)
            {
                this.Score += ThreadBonus;
                this.ThreadFlash = 0.6f;
            }
        }
    }

    private bool HitsAnyPillar()
    {
        foreach (var pillar in this.pillars)
        {
            if (pillar.X > BirdX + BirdRadius || pillar.X + PillarWidth < BirdX - BirdRadius)
            {
                continue;
            }
            // The bird is a circle, but its nearest point to a full-height column is always the plain
            // vertical distance, so the gap edges are the only test that matters.
            var nearestX = Math.Clamp(BirdX, pillar.X, pillar.X + PillarWidth);
            var dx = BirdX - nearestX;
            var overlap = MathF.Sqrt(Math.Max(0f, (BirdRadius * BirdRadius) - (dx * dx)));
            if (this.BirdY - overlap < pillar.GapTop || this.BirdY + overlap > pillar.GapBottom)
            {
                return true;
            }
        }
        return false;
    }

    private static float Lerp(float from, float to, float t) => from + ((to - from) * t);
}
