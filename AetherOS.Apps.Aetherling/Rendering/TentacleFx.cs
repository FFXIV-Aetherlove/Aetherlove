using System;
using System.Numerics;
using AetherOS.Apps.Aetherling.Engine;

namespace AetherOS.Apps.Aetherling.Rendering;

/// <summary>The strand rig: one chain of tapered capsules with different numbers in it, which is every
/// hanging, waving or jointed part the prototype's shells grow. Here it draws the Antennae. Transcribed
/// from the prototype's TentacleFx constant-for-constant; the bench tuned it and the numbers do not
/// drift. Pure apart from <see cref="Update"/>: it owns the clock and the swing, answers where every knot
/// of every strand is this frame in cell-local pixels, and <see cref="PartsDraw"/> does the drawing.
///
/// The swing drags a FOLLOW POINT after the seat and reads the lean off the gap, never a differentiated
/// velocity: the seat is a per-cell anchor that holds still for seven render frames and then jumps, and
/// differentiating that is an impulse train whose height is the frame rate. Integration is substepped at
/// 120 Hz for the same reason, so the swing is a property of the pet and not of the graphics card.</summary>
public sealed class TentacleFx
{
    public const int MaxSegments = 16;
    public const int MaxStrands = 8;

    private static readonly float[] Knee = [0f, 1.00f, 0.25f];

    private const float DriftPhaseStep = 0.61803399f;
    private const float DriftRateStep = 0.75487767f;

    private const float LeanStiffness = 64f;
    private const float LeanDamping = 5.6f;
    private const float LeanPerPixel = 0.022f;
    private const float LeanMax = 0.78f;
    private const float LeanStep = 1f / 120f;
    private const float LeanMaxCatchUp = 0.1f;

    private readonly Vector2[] _points = new Vector2[MaxStrands * (MaxSegments + 1)];
    private readonly float[] _radii = new float[MaxStrands * (MaxSegments + 1)];

    private float _clock;
    private float _follow;
    private float _followVel;
    private bool _following;
    private float _lean;

    public int Strands { get; private set; }

    /// <summary>Knots per strand, root included.</summary>
    public int Knots { get; private set; }

    /// <summary>Tip ball radius in cell-local pixels, zero when the record asks for none.</summary>
    public float Bulb { get; private set; }

    public void Update(float dt, bool reduceMotion) => Update(dt, reduceMotion, null);

    /// <summary>Advances the clock and the swing. <paramref name="seat"/> is where the fan is sown THIS
    /// frame in cell pixels, hop offset included; null lets the fan relax to plumb. Reduce-motion parks
    /// everything where it stands: the contract removes motion, not body parts.</summary>
    public void Update(float dt, bool reduceMotion, Vector2? seat)
    {
        if (reduceMotion || dt <= 0f)
        {
            return;
        }

        _clock = (_clock + dt) % 3600f;

        if (seat is not { } now)
        {
            _following = false;
            now = new Vector2(_follow, 0f);
        }
        else if (!_following)
        {
            _follow = now.X;
            _followVel = 0f;
            _following = true;
        }

        for (var remaining = MathF.Min(dt, LeanMaxCatchUp); remaining > 0f; remaining -= LeanStep)
        {
            var step = MathF.Min(remaining, LeanStep);
            _followVel += (((now.X - _follow) * LeanStiffness) - (_followVel * LeanDamping)) * step;
            _follow += _followVel * step;
        }

        _lean = Math.Clamp((now.X - _follow) * LeanPerPixel, -LeanMax, LeanMax);
    }

    /// <summary>Builds the whole fan into the scratch buffers in cell-local pixels. <paramref name="seat"/>
    /// is the seat anchor for this cell, <paramref name="seatDepth"/> the wrap arc's half-depth that sets
    /// the inner strands a little nearer the viewer.</summary>
    public void Build(StrandDef def, Vector2 seat, float seatDepth)
    {
        var count = Math.Clamp(def.Count, 0, MaxStrands);
        var segs = def.Jointed ? Knee.Length : Math.Clamp(def.Segs, 2, MaxSegments);
        Strands = count;
        Knots = segs + 1;
        Bulb = def.Bulb * def.Root;

        var phase = _clock * def.Speed;
        var trail = _lean * def.Swing;

        for (var i = 0; i < count; i++)
        {
            BuildOne(def, seat, seatDepth, phase, trail, i, count, segs);
        }
    }

    public Vector2 PointAt(int strand, int knot) => _points[(strand * (MaxSegments + 1)) + knot];

    public float RadiusAt(int strand, int knot) => _radii[(strand * (MaxSegments + 1)) + knot];

    /// <summary>One strand. The angle is integrated rather than sampled, which is what conserves arc
    /// length: a strand bends, it never stretches.</summary>
    private void BuildOne(StrandDef def, Vector2 seat, float seatDepth, float phase, float trail, int index,
        int count, int segs)
    {
        var raw = count == 1 ? 0f : ((index / (float)(count - 1)) * 2f) - 1f;
        var side = raw < 0f ? -1f : 1f;
        var fan = MathF.Abs(raw);

        var psi = fan * 1.6f;
        var k = MathF.Max(0.25f, 1f - (def.Stagger * (1f - fan)));
        var outward = MathF.Sin(def.Dir) >= 0f ? -1f : 1f;

        var driftPhase = Frac(index * DriftPhaseStep);
        var driftRate = 1f + (0.35f * Frac(index * DriftRateStep));
        var drift = def.Drift * MathF.Sin(((_clock * def.DriftSpeed * driftRate) + driftPhase) * MathF.Tau);

        var p = new Vector2(seat.X + (side * fan * def.Spread), seat.Y + (seatDepth * (1f - fan)));
        var rootR = def.Root * k;
        var tipR = def.Root * def.Taper * k;
        var segLen = (def.Len * k) / segs;
        var ang = def.Dir + (outward * def.Splay * fan);

        var b = index * (MaxSegments + 1);
        _points[b] = p;
        _radii[b] = rootR;

        for (var s = 1; s <= segs; s++)
        {
            var u = s / (float)segs;
            var hold = MathF.Min(1f, u / 0.15f);
            var wave = def.Amp * hold * MathF.Sin(((phase - (u * def.Waves)) * MathF.Tau) + psi);

            if (def.Jointed)
            {
                ang += (Knee[s - 1] * -outward) + (s == 1 ? (wave + drift) * 0.5f : 0f);
            }
            else
            {
                // The trail is multiplied by `side`, which cancels the outboard mirror everything else is
                // authored in: a body dragged right drags both halves right, and a trail that mirrored
                // would open and shut the fan like scissors.
                ang += ((def.Curl * u * outward) + wave + ((drift + (trail * side)) * hold)) / segs;
            }

            p += new Vector2(MathF.Cos(ang) * side * segLen, MathF.Sin(ang) * segLen);
            _points[b + s] = p;
            _radii[b + s] = rootR + ((tipR - rootR) * u);
        }
    }

    private static float Frac(float v) => v - MathF.Floor(v);
}
