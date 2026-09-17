namespace AetherOS.Apps.Racer.Rendering;

using System;
using System.Numerics;

/// <summary>Bounded cosmetic ground events: the fire courses' meteors. Two fixed slots over a fixed
/// table of candidate sites, stepped on the stage's frame clock, with a seeded stream of its own
/// that never touches the simulation's. No renderer dependency: the stage supplies visibility, road
/// clearance and the flight's start through callbacks, and draws from <see cref="Meteors"/>.
///
/// <para>A flight and its impact are world-anchored, so a turning camera moves the picture and not
/// the event. Every trajectory is sampled against the road with a swept-width allowance before it
/// is accepted; an event with no safe visible site is skipped, never placed on the road.</para></summary>
internal sealed class RaceSceneryFx
{
    public const int MaxSites = 96;
    public const int MaxMeteors = 2;
    public const int FragmentsPerImpact = 6;
    public const float ImpactRadius = 0.90f;
    public const float FlightSeconds = 0.62f;
    public const float ImpactSeconds = 0.72f;

    /// <summary>The longest flight, in bounds, and the disc each flight sample must keep clear of
    /// the road: half the sample step plus the trail's own width.</summary>
    private const float MaxFlight = 2.2f;
    private const float SweptRadius = 0.32f;
    private const int FlightSamples = 12;
    private const int SiteTries = 16;

    private const float FirstWait = 2.5f;
    private const float WaitBase = 4.5f;
    private const float WaitSpan = 3f;
    private const float RetryWait = 0.8f;
    private const float MinIntensity = 0.25f;
    private const float LightReachSquared = 12f;

    public struct Meteor
    {
        public Vector2 Start;
        public Vector2 Target;
        public float Age;
        public float Phase;
        public bool Active;
    }

    private readonly Vector2[] sites = new Vector2[MaxSites];
    private readonly Meteor[] meteors = new Meteor[MaxMeteors];
    private int siteCount;
    private uint rng;
    private float wait;

    /// <summary>Is a world site on the stage with room for its impact? Read at spawn only.</summary>
    public Func<Vector2, bool>? SiteVisible { get; set; }

    /// <summary>Is a world disc clear of every road branch? Asked for the impact footprint and for
    /// every flight sample.</summary>
    public Func<Vector2, float, bool>? SiteClear { get; set; }

    /// <summary>Where a flight to this target begins, in world bounds. The stage answers with a
    /// screen-up-left offset resolved through the live camera, so meteors fall the same way on the
    /// stage whatever the heading.</summary>
    public Func<Vector2, Vector2>? FlightStart { get; set; }

    public int SiteCount => this.siteCount;

    public ReadOnlySpan<Meteor> Meteors => this.meteors;

    public int ActiveCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < this.meteors.Length; i++)
            {
                if (this.meteors[i].Active)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public void Begin(int seed)
    {
        this.siteCount = 0;
        this.rng = unchecked((uint)seed ^ 0x41a5fa11u);
        this.wait = FirstWait;
        Array.Clear(this.meteors);
    }

    /// <summary>Offers one candidate impact site. Kept only while there is room and the impact
    /// footprint clears the road.</summary>
    public void AddSite(Vector2 world)
    {
        if (this.siteCount < MaxSites && this.SiteClear is { } clear && clear(world, ImpactRadius))
        {
            this.sites[this.siteCount++] = world;
        }
    }

    /// <summary>Ages the live events and rolls the next one when the wait runs out. A hold cancels
    /// every live event and starts none; reduced motion or a disabled terrain clears them.</summary>
    public void Update(float dt, bool enabled, bool hold, bool reducedMotion, float intensity)
    {
        dt = float.IsFinite(dt) ? Math.Clamp(dt, 0f, 1f) : 0f;
        if (!enabled || reducedMotion || !float.IsFinite(intensity) || intensity <= 0f)
        {
            Array.Clear(this.meteors);
            return;
        }

        for (var i = 0; i < MaxMeteors; i++)
        {
            ref var meteor = ref this.meteors[i];
            if (!meteor.Active)
            {
                continue;
            }

            meteor.Age += dt;
            if (meteor.Age >= FlightSeconds + ImpactSeconds || hold)
            {
                meteor.Active = false;
            }
        }

        if (hold || this.siteCount == 0 || dt <= 0f)
        {
            return;
        }

        this.wait -= dt;
        if (this.wait > 0f)
        {
            return;
        }

        this.wait = (WaitBase + (this.Next() * WaitSpan)) / Math.Clamp(intensity, MinIntensity, 1f);
        for (var slot = 0; slot < MaxMeteors; slot++)
        {
            if (this.meteors[slot].Active)
            {
                continue;
            }

            var first = (int)(this.Next() * this.siteCount);
            for (var attempt = 0; attempt < Math.Min(this.siteCount, SiteTries); attempt++)
            {
                var target = this.sites[(first + attempt) % this.siteCount];
                if (this.SiteVisible is not { } visible || !visible(target))
                {
                    continue;
                }

                var angle = this.Next() * MathF.Tau;
                var start = this.FlightStart is { } flightStart
                    ? flightStart(target)
                    : target + (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * MaxFlight);
                var flight = start - target;
                if (!float.IsFinite(flight.X) || !float.IsFinite(flight.Y))
                {
                    continue;
                }

                if (flight.LengthSquared() > MaxFlight * MaxFlight)
                {
                    start = target + (Vector2.Normalize(flight) * MaxFlight);
                }

                if (!this.ClearTrajectory(start, target))
                {
                    continue;
                }

                this.meteors[slot] = new Meteor
                {
                    Start = start,
                    Target = target,
                    Phase = this.Next() * MathF.Tau,
                    Active = true,
                };
                return;
            }

            // A close camera may expose no usable verge: wait briefly rather than place on road.
            this.wait = RetryWait;
            return;
        }
    }

    private bool ClearTrajectory(Vector2 start, Vector2 target)
    {
        if (this.SiteClear is not { } clear || !clear(target, ImpactRadius))
        {
            return false;
        }

        for (var i = 0; i <= FlightSamples; i++)
        {
            if (!clear(Vector2.Lerp(start, target, i / (float)FlightSamples), SweptRadius))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The impact light falling on a world point, 0..1: nothing during a flight, a sharp
    /// falloff from the strike that fades out over the impact.</summary>
    public float LightAt(Vector2 world)
    {
        var light = 0f;
        foreach (ref readonly var meteor in this.meteors.AsSpan())
        {
            if (!meteor.Active || meteor.Age < FlightSeconds)
            {
                continue;
            }

            var age = (meteor.Age - FlightSeconds) / ImpactSeconds;
            var falloff = MathF.Max(0f, 1f - (Vector2.DistanceSquared(world, meteor.Target) / LightReachSquared));
            light = MathF.Max(light, falloff * (1f - age) * (1f - age));
        }

        return light;
    }

    private float Next()
    {
        this.rng += 0x6d2b79f5u;
        var t = this.rng;
        t = (t ^ (t >> 15)) * (t | 1u);
        t ^= t + ((t ^ (t >> 7)) * (t | 61u));
        return ((t ^ (t >> 14)) >> 8) * (1f / 16777216f);
    }
}
