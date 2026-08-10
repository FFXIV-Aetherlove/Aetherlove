using System;
using System.Numerics;

namespace AetherOS.Apps.Aetherling.Engine;

/// <summary>What the newborn looks like this frame.</summary>
public struct PetPose
{
    public int CellIndex;

    /// <summary>Squish and stretch; the sheet holds no scaling of its own.</summary>
    public Vector2 Scale;

    /// <summary>Offset in cell-local pixels, which is where the hop lives.</summary>
    public Vector2 Offset;

    /// <summary>The hop leaves and returns, so the sprite faces the way it is going.</summary>
    public bool FlipX;
}

/// <summary>Keeps the newborn alive on screen without anyone asking it to. It idles, blinks on its own
/// schedule, hops now and then, and drops off to sleep if it is left alone long enough.
///
/// The scheduling is the whole point of the class: a clip player alone would need the caller to decide when
/// to blink, and a blink on a timer the caller owns always ends up either metronomic or forgotten.</summary>
public sealed class AnimationController
{
    private const float BlinkMinSeconds = 3f;
    private const float BlinkMaxSeconds = 7f;
    private const float VariantMinSeconds = 20f;
    private const float VariantMaxSeconds = 90f;

    /// <summary>Left alone this long, it naps. Ten minutes, so it never happens while somebody is watching.</summary>
    private const float NapAfterSeconds = 600f;

    private const float BoopSeconds = 0.5f;
    private const float HopSeconds = 0.85f;
    private const float HopReach = 42f;
    private const float HopHeight = 30f;

    private readonly Random _rng;
    private readonly ClipPlayer _clip;

    private float _sinceInteraction;
    private float _blinkIn;
    private float _variantIn;
    private float _boopT = 1f;
    private float _hopT = 1f;
    private bool _hopLeft;
    private bool _napping;

    public AnimationController(AtlasManifest manifest, Random? rng = null)
    {
        _rng = rng ?? new Random();
        _clip = new ClipPlayer(manifest, "idle");
        _blinkIn = NextBlink();
        _variantIn = NextVariant();
    }

    /// <summary>Nothing moves but the blink: no hop, no idle bob, no boop squish.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Seconds since the last poke, which is what the nap and the mood both watch.</summary>
    public float SinceInteraction => _sinceInteraction;

    /// <summary>It is asleep, so the caller can say so rather than making the player guess.</summary>
    public bool Napping => _napping;

    /// <summary>Plays the hop frames without moving the sprite anywhere. The arrival carries it down from
    /// where the crystal broke under its own arithmetic, and two hop offsets fighting over one sprite reads
    /// as a stumble.</summary>
    public void PlayHopClip()
    {
        _sinceInteraction = 0f;
        _napping = false;
        _hopT = 1f;
        _variantIn = NextVariant();
        _clip.Play("hop");
    }

    /// <summary>A poke. Wakes it, plays the boop, and re-arms the wandering timers.</summary>
    public void Boop()
    {
        _sinceInteraction = 0f;
        _napping = false;
        _boopT = 0f;
        _hopT = 1f;
        _variantIn = NextVariant();
        _clip.Play("boop");
    }

    public void Update(float dt)
    {
        _sinceInteraction += dt;
        _boopT = MathF.Min(1f, _boopT + (dt / BoopSeconds));
        _hopT = MathF.Min(1f, _hopT + (dt / HopSeconds));

        if (!_napping && _sinceInteraction >= NapAfterSeconds)
        {
            _napping = true;
            _clip.Play("nap");
        }

        _clip.Update(ReduceMotion ? 0f : dt);

        if (_napping)
        {
            return;
        }

        // A one-shot that has run its course hands the sprite back to idle; without this it would freeze on
        // the last frame of whatever it just did.
        if (_clip.Finished)
        {
            _clip.Play("idle");
        }

        _blinkIn -= dt;
        if (_blinkIn <= 0f && _clip.ClipName == "idle")
        {
            _blinkIn = NextBlink();
            _clip.Play("blink");
        }

        if (ReduceMotion)
        {
            return;
        }

        _variantIn -= dt;
        if (_variantIn <= 0f && _clip.ClipName == "idle")
        {
            _variantIn = NextVariant();
            _hopT = 0f;
            _hopLeft = _rng.Next(2) == 0;
            _clip.Play("hop");
        }
    }

    public PetPose GetPose()
    {
        var pose = new PetPose
        {
            CellIndex = _clip.CurrentCell,
            Scale = Vector2.One,
            Offset = Vector2.Zero,
            FlipX = false,
        };
        if (ReduceMotion)
        {
            return pose;
        }

        if (_boopT < 1f)
        {
            var squish = MathF.Sin(MathF.PI * _boopT);
            pose.Scale = new Vector2(1f + (0.12f * squish), 1f - (0.16f * squish));
        }

        if (_hopT < 1f)
        {
            // Out and back rather than a jump to somewhere: it never leaves the middle of its own card.
            var arc = MathF.Sin(MathF.PI * _hopT);
            var reach = HopReach * arc * (_hopLeft ? -1f : 1f);
            pose.Offset = new Vector2(reach, -HopHeight * arc);
            pose.FlipX = _hopLeft;
        }

        return pose;
    }

    private float NextBlink() =>
        BlinkMinSeconds + ((float)_rng.NextDouble() * (BlinkMaxSeconds - BlinkMinSeconds));

    private float NextVariant() =>
        VariantMinSeconds + ((float)_rng.NextDouble() * (VariantMaxSeconds - VariantMinSeconds));
}
