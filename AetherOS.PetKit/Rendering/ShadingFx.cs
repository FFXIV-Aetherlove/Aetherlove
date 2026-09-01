using System;

namespace AetherOS.PetKit.Rendering;

/// <summary>State and switches for the code-side enhanced look: the sliding specular and the clip-rect sheen
/// sweep. Both are drawn by <see cref="CoreDraw"/> from the existing sheets.</summary>
public sealed class ShadingFx
{
    /// <summary>Sweep duration, seconds. Short and readable; never a strobe.</summary>
    private const float SweepSeconds = 0.8f;

    private float _nextSweepIn = 5f;
    private float _sweepElapsed = -1f;

    /// <summary>Master switch; off means <see cref="CoreDraw"/> renders the plain layered quad.</summary>
    public bool Enabled { get; set; }

    /// <summary>The sliding, untinted highlight blob.</summary>
    public bool Specular { get; set; } = true;

    /// <summary>The travelling gloss band, redrawn from the body's own alpha.</summary>
    public bool SheenSweep { get; set; } = true;

    /// <summary>0 to 1 while a sweep is crossing the body; null when idle.</summary>
    public float? SweepT => _sweepElapsed >= 0f ? Math.Min(1f, _sweepElapsed / SweepSeconds) : null;

    /// <summary>Advances the sweep clock. Reduce-motion suppresses sweeps entirely.</summary>
    public void Update(float dt, bool reduceMotion)
    {
        if (!Enabled || !SheenSweep || reduceMotion)
        {
            _sweepElapsed = -1f;
            _nextSweepIn = Math.Max(_nextSweepIn, 2f);
            return;
        }

        if (_sweepElapsed >= 0f)
        {
            _sweepElapsed += dt;
            if (_sweepElapsed >= SweepSeconds)
            {
                _sweepElapsed = -1f;
                _nextSweepIn = 8f + (Random.Shared.NextSingle() * 6f);
            }

            return;
        }

        _nextSweepIn -= dt;
        if (_nextSweepIn <= 0f)
        {
            _sweepElapsed = 0f;
        }
    }

    /// <summary>Starts a sweep now unless one is already crossing. A no-op when the enhanced
    /// look is off, so callers need not check.</summary>
    public void RequestSweep()
    {
        if (Enabled && SheenSweep && _sweepElapsed < 0f)
        {
            _sweepElapsed = 0f;
        }
    }

    /// <summary>Specular material parameters per style key. The Aethercore is a crystal, not a
    /// creature: a small hard glint in a tight halo, so it reads as a faceted surface rather
    /// than a wet one.</summary>
    public static (float CoreAlpha, float HaloAlpha, float SizeMul) SpecFor(string styleKey) => styleKey switch
    {
        "core" => (0.85f, 0.10f, 0.60f),
        _ => (0.48f, 0.11f, 1f),
    };
}
