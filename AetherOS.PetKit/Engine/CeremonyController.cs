using System;
using System.Numerics;

namespace AetherOS.PetKit.Engine;

/// <summary>The frame's crystal visuals: frames carry the crystal states, code carries
/// everything that moves.</summary>
public struct CorePose
{
    public int CellIndex;

    /// <summary>The crystal's scale times breathing pulse times tap squish.</summary>
    public Vector2 Scale;

    /// <summary>Jitter offset in cell-local pixels; zero under reduce-motion.</summary>
    public Vector2 Offset;

    /// <summary>Bloom behind the core. 0 = none.</summary>
    public float GlowAlpha;

    /// <summary>An expectant nudge wobble is playing.</summary>
    public bool Wobbling;

    /// <summary>The crystal's own opacity. Falls to nothing as it comes apart.</summary>
    public float CoreAlpha;

    /// <summary>How dark the room behind it is, deepening through the swell and lifting as the shards fly.</summary>
    public float DimAlpha;

    /// <summary>The one white flash, at most 0.85 and gone inside two thirds of a second.</summary>
    public float FlashAlpha;

    /// <summary>Stands in for the flash when motion is reduced: a soft bloom rather than a strike.</summary>
    public float HaloAlpha;

    /// <summary>0 to 1 while the shell pieces fly, negative when they are not.</summary>
    public float ShardProgress;

    /// <summary>0 to 1 while what was inside arrives, negative before it does.</summary>
    public float PetPopProgress;
}

/// <summary>The ceremony crystal's presentation state machine: the breathing pulse, the tap squish, the
/// jitter, the expectant nudge and the birth. It owns no gates, prices or entitlements; it animates the
/// one crystal state there is and the breaking of it.</summary>
public sealed class CeremonyController
{
    /// <summary>The crystal's scale. Surfaces divide their target size by this.</summary>
    public const float KindledScale = 1.75f;

    private const float RestingJitter = 2.6f;
    private const float RestingGlow = 0.55f;
    private const float PulseHz = 1.07f;
    private const string RestingClip = "quicken";

    /// <summary>Seconds of stillness before the core nudges for attention.</summary>
    private const float NudgeAfterSeconds = 8f;

    /// <summary>Where the stillness clock is re-armed to after a nudge, so the next one lands
    /// four seconds later rather than eight.</summary>
    private const float NudgeRearmSeconds = 4f;

    /// <summary>The held breath before the flash. Deliberately the longest stretch of the birth. The
    /// prototype's 1.8 s plus a one-second burst was under three seconds end to end, which for something an
    /// account sees exactly once was over before anyone had registered it was happening.</summary>
    public const float SwellDuration = 4.6f;

    /// <summary>The same breath told only in light, for anyone who asked for less motion.</summary>
    public const float GentleSwellDuration = 2.4f;

    /// <summary>The last fifth of the swell, where the shaking stops and only the light keeps climbing.
    /// The stillness is what makes it suspense; a shake that merely runs longer reads as a stuck sprite.</summary>
    private const float SwellHoldFraction = 0.18f;
    private const float SwellJitterPeak = 7f;
    private const float SwellClipRatePeak = 2.6f;
    private const float SwellScalePeak = 1.06f;
    private const float SwellScaleInhale = 0.97f;
    private const float SwellDimDeepen = 0.2f;

    // The flash itself stays fast, because a slow flash is a glare rather than a strike. Everything the eye
    // is meant to follow after it is not.
    private const float BurstFlashRise = 0.09f;
    private const float BurstFlashDecayEnd = 0.95f;
    private const float BurstShardStart = 0.10f;
    private const float BurstShardEnd = 1.5f;
    private const float BurstPetPopAt = 1.0f;
    private const float BurstPetPopLen = 0.7f;

    /// <summary>The burst measured from the flash; the swell runs before zero. The tail past the pop is a
    /// held beat on the newborn, so the screen does not change the instant it finishes arriving.</summary>
    public const float BurstDuration = 3.4f;

    /// <summary>How dark the ceremony sits normally, before the swell deepens it.</summary>
    private const float RestingDim = 0.55f;

    private readonly Random _rng;
    private readonly ClipPlayer _clip;

    private float _pulseT;
    private float _squishT = 1f;
    private float _jitterTimer;
    private Vector2 _jitterOffset;
    private float _sinceInteraction;
    private float _wobbleT = 1f;
    private bool _birthPlaying;
    private bool _gentleBirth;
    private float _swellDuration = SwellDuration;
    private float _birthT;

    public CeremonyController(AtlasManifest manifest, Random? rng = null)
    {
        _rng = rng ?? new Random();
        _clip = new ClipPlayer(manifest, RestingClip);
    }

    /// <summary>No jitter, no wobble, no breathing, and the sprite parks on its clip's first frame.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Seconds since the last tap.</summary>
    public float SinceInteraction => _sinceInteraction;

    /// <summary>The birth is running, swell included.</summary>
    public bool BirthPlaying => _birthPlaying;

    /// <summary>Still in the anticipation, before the flash.</summary>
    public bool Swelling => _birthPlaying && _birthT < 0f;

    /// <summary>Fires once, the moment the flash lands. The surface uses it to cut the music.</summary>
    public event Action? Flashed;

    /// <summary>Fires once when the birth has played out.</summary>
    public event Action? BirthFinished;

    /// <summary>Starts the birth. Nothing else does: the player breaks the crystal, and only the server's
    /// yes reaches here.</summary>
    public void BeginBirth(bool gentle)
    {
        if (_birthPlaying)
        {
            return;
        }
        _birthPlaying = true;
        _gentleBirth = gentle;
        _swellDuration = gentle ? GentleSwellDuration : SwellDuration;
        _birthT = -_swellDuration;
        _sinceInteraction = 0f;
        _wobbleT = 1f;
    }

    /// <summary>A tap on the core. Never a failure state: every outcome squishes.</summary>
    public void Touch()
    {
        _sinceInteraction = 0f;
        _squishT = 0f;
    }

    public void Update(float dt)
    {
        _pulseT += dt;
        _sinceInteraction += dt;
        _squishT = MathF.Min(1f, _squishT + (dt / 0.06f));
        _wobbleT = MathF.Min(1f, _wobbleT + (dt / 0.55f));

        if (!ReduceMotion && !_birthPlaying && _sinceInteraction >= NudgeAfterSeconds && _wobbleT >= 1f)
        {
            _wobbleT = 0f;
            _sinceInteraction = NudgeRearmSeconds;
        }

        var swell = SwellShape();
        if (_birthPlaying)
        {
            var wasSwelling = _birthT < 0f;
            _birthT += dt;
            if (wasSwelling && _birthT >= 0f)
            {
                _clip.Play("burst");
                Flashed?.Invoke();
            }
            if (_birthT >= BurstDuration)
            {
                _birthPlaying = false;
                _birthT = 0f;
                BirthFinished?.Invoke();
            }
        }

        if (!ReduceMotion)
        {
            _clip.Update(dt * swell.ClipRate);
        }

        var amplitude = ReduceMotion ? 0f
            : Swelling ? swell.Jitter
            : _birthPlaying ? 0f
            : RestingJitter;
        if (amplitude > 0f)
        {
            _jitterTimer -= dt;
            if (_jitterTimer <= 0f)
            {
                _jitterTimer = 1f / 30f;
                _jitterOffset = new Vector2(
                    (((float)_rng.NextDouble() * 2f) - 1f) * amplitude,
                    (((float)_rng.NextDouble() * 2f) - 1f) * amplitude);
            }
        }
        else
        {
            _jitterOffset = Vector2.Zero;
        }
    }

    public CorePose GetPose()
    {
        var pulse = ReduceMotion
            ? 1f
            : 1f + (0.03f * (0.5f + (0.5f * MathF.Sin(_pulseT * PulseHz * MathF.Tau))));

        var squish = MathF.Sin(MathF.PI * MathF.Min(1f, _squishT));
        var squishX = 1f + (0.06f * squish);
        var squishY = 1f - (0.08f * squish);

        var wobble = ReduceMotion ? 0f : MathF.Sin(_wobbleT * MathF.PI * 3f) * (1f - _wobbleT) * 0.05f;

        var pose = new CorePose
        {
            CellIndex = _clip.CurrentCell,
            Scale = new Vector2(
                KindledScale * pulse * squishX * (1f + wobble),
                KindledScale * pulse * squishY * (1f - wobble)),
            Offset = _jitterOffset,
            GlowAlpha = RestingGlow,
            Wobbling = _wobbleT < 1f,
            CoreAlpha = 1f,
            DimAlpha = RestingDim,
            ShardProgress = -1f,
            PetPopProgress = -1f,
        };
        if (!_birthPlaying)
        {
            return pose;
        }

        if (_birthT < 0f)
        {
            var shape = SwellShape();
            pose.Scale *= shape.Scale;
            pose.GlowAlpha = shape.Glow;
            pose.DimAlpha += shape.Dim;
            return pose;
        }

        if (_gentleBirth)
        {
            // No strike and no shards: the crystal simply gives its light up over four hundred milliseconds.
            var fade = Math.Clamp(_birthT / 0.4f, 0f, 1f);
            pose.CoreAlpha = 1f - fade;
            pose.HaloAlpha = MathF.Sin(MathF.PI * fade) * 0.85f;
            pose.DimAlpha = RestingDim * (1f - Math.Clamp((_birthT - 0.3f) / 0.4f, 0f, 1f));
            pose.PetPopProgress = PopProgress(_birthT);
            return pose;
        }

        pose.FlashAlpha = _birthT <= BurstFlashRise
            ? 0.85f * (_birthT / BurstFlashRise)
            : 0.85f * MathF.Max(0f, 1f - ((_birthT - BurstFlashRise) / (BurstFlashDecayEnd - BurstFlashRise)));
        pose.CoreAlpha = _birthT >= BurstShardStart
            ? MathF.Max(0f, 1f - ((_birthT - BurstShardStart) / 0.2f))
            : 1f;
        pose.DimAlpha = RestingDim * (1f - Math.Clamp(
            (_birthT - BurstShardStart) / (BurstShardEnd - BurstShardStart), 0f, 1f));
        if (_birthT is >= BurstShardStart and <= BurstShardEnd)
        {
            pose.ShardProgress = (_birthT - BurstShardStart) / (BurstShardEnd - BurstShardStart);
        }
        pose.PetPopProgress = PopProgress(_birthT);
        return pose;
    }

    private float PopProgress(float t) =>
        t >= BurstPetPopAt ? Math.Clamp((t - BurstPetPopAt) / BurstPetPopLen, 0f, 1f) : -1f;

    /// <summary>The overshoot the newborn arrives on: nothing, past its size, then settling.</summary>
    public static float PetPopScale(float p)
    {
        if (p is < 0f or >= 1f)
        {
            return 1f;
        }
        const float Overshoot = 1.70158f * 1.2f;
        var q = p - 1f;
        return 1f + (q * q * (((Overshoot + 1f) * q) + Overshoot));
    }

    /// <summary>The anticipation curve: a wind-up that speeds the shake and the frames, then a held beat
    /// where the motion draws back to stillness and only the light keeps rising.</summary>
    private (float Jitter, float ClipRate, float Scale, float Glow, float Dim) SwellShape()
    {
        if (!Swelling)
        {
            return (0f, 1f, 1f, 0f, 0f);
        }

        var s = 1f + (_birthT / _swellDuration);
        if (ReduceMotion)
        {
            var gentle = 1f - MathF.Pow(1f - s, 2f);
            return (0f, 1f, 1f, gentle, SwellDimDeepen * gentle);
        }

        if (s < 1f - SwellHoldFraction)
        {
            var accel = MathF.Pow(s / (1f - SwellHoldFraction), 2f);
            return (
                Lerp(RestingJitter, SwellJitterPeak, accel),
                Lerp(1f, SwellClipRatePeak, accel),
                Lerp(1f, SwellScalePeak, accel),
                RestingGlow * accel,
                SwellDimDeepen * accel);
        }

        var q = (s - (1f - SwellHoldFraction)) / SwellHoldFraction;
        var ease = 1f - MathF.Pow(1f - q, 3f);
        return (
            Lerp(SwellJitterPeak, 0.3f, ease),
            Lerp(SwellClipRatePeak, 0.5f, ease),
            Lerp(SwellScalePeak, SwellScaleInhale, ease),
            Lerp(RestingGlow, 1f, ease),
            SwellDimDeepen);
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
