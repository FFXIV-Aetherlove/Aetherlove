namespace AetherOS.PetKit.Rendering;

using System;
using System.Numerics;

using AetherOS.PetKit.Engine;

/// <summary>The strand rig: one chain of tapered capsules with different numbers in it, which is every
/// hanging, waving or jointed part a shell grows. The numbers live in the manifest's
/// <see cref="StrandDef"/>, so a new shell with tendrils, whiskers, antennae or legs costs a record and
/// no code. Strands are anatomy rather than a grant: the only gate is whether the manifest has a record
/// at all. Pure apart from <see cref="Update"/>: it owns the clock and the swing, answers where every
/// knot of every strand is this frame in cell-local pixels, and <see cref="PartsDraw"/> does the drawing.
///
/// <para>The swing drags a FOLLOW POINT after the seat and reads the lean off the gap, never a
/// differentiated velocity: the seat is a per-cell anchor that holds still for seven render frames and
/// then jumps, and differentiating that is an impulse train whose height is the frame rate. Integration
/// is substepped at 120 Hz for the same reason, so the swing is a property of the pet and not of the
/// graphics card.</para>
///
/// <para>Strands are authored OUTBOARD: +x points away from the centre line and is mirrored on the way
/// out, exactly as <see cref="HandFx"/> mirrors the off hand.</para></summary>
public sealed class TentacleFx
{
    /// <summary>Knots a strand may ask for; the cap is what lets the buffers be fixed.</summary>
    public const int MaxSegments = 16;

    /// <summary>Strands in a fan. Six is the widest body plan (the crab's legs).</summary>
    public const int MaxStrands = 8;

    /// <summary>The jointed profile: hip, knee, ankle, as angle deltas accumulated outboard.
    /// Out, then down, then down is what a walking leg does: the first segment reaches away
    /// from the shell, the knee takes it to the floor, and the last segment plants.</summary>
    private static readonly float[] Knee = [0f, 1.00f, 0.25f];

    /// <summary>The drift's per-strand scatter: two irrationals give each strand a phase and a
    /// rate that never line up with its neighbours'. Irrationals rather than an RNG because the
    /// same fan is built several times a frame and must come out identical every time; a hash of
    /// the index needs no state and no ordering guarantee.</summary>
    private const float DriftPhaseStep = 0.61803399f;

    private const float DriftRateStep = 0.75487767f;

    /// <summary>The swing's spring, deliberately NOT manifest fields: a shell says how loosely it
    /// hangs (<see cref="StrandDef.Swing"/>), not what a pendulum is, or every creature would read
    /// as different physics. Under-damped on purpose (ζ ≈ 0.35): one visible overshoot is what
    /// "loose" looks like. <c>LeanPerPixel</c> turns the follow gap, in cell pixels, into radians
    /// of lean.</summary>
    private const float LeanStiffness = 64f;

    private const float LeanDamping = 5.6f;

    private const float LeanPerPixel = 0.022f;

    /// <summary>Half a right angle of trail and no more: the cap stops one absurd delta (a shell
    /// swap, a teleport, a minimised frame) throwing the fan across the cell.</summary>
    private const float LeanMax = 0.78f;

    /// <summary>Fixed integration step. At one step per rendered frame the same spring settles to
    /// a different amplitude at 30 fps than at 144; substepping at 120 Hz makes the swing a
    /// property of the pet rather than of the graphics card. Any new spring here takes the same
    /// substep loop.</summary>
    private const float LeanStep = 1f / 120f;

    /// <summary>How much elapsed time the spring will chase in one frame: a dropped frame or a
    /// minute minimised should land on a settled fan, not replay the swing.</summary>
    private const float LeanMaxCatchUp = 0.1f;

    private readonly Vector2[] _points = new Vector2[MaxStrands * (MaxSegments + 1)];
    private readonly float[] _radii = new float[MaxStrands * (MaxSegments + 1)];

    private float _clock;

    // Integrated once per frame in Update and only READ by Build, so every surface that builds
    // the fan this frame sees one lean.
    private float _follow;

    private float _followVel;

    private bool _following;

    private float _lean;

    /// <summary>Strands in the fan just built.</summary>
    public int Strands { get; private set; }

    /// <summary>Knots per strand, root included, so a strand of N segments has N+1.</summary>
    public int Knots { get; private set; }

    /// <summary>Tip ball radius in cell-local pixels, zero when the record asks for none.</summary>
    public float Bulb { get; private set; }

    /// <summary>How much of its own wave the fan is spending, as a multiplier: the emote's
    /// Ripple. Set every tick from the playing morph, so nothing here knows what an emote is;
    /// 1 is the authored wave and what it sits at when nothing is playing.</summary>
    public float AmpScale { get; set; } = 1f;

    /// <summary>An extra lean on the whole fan, in the same units as the body-motion trail it is
    /// added to: the emote's Tip. Added to the trail rather than per strand, because a lean is
    /// the one motion a fan makes in unison; per strand it would scissor the fan open and
    /// shut.</summary>
    public float LeanBias { get; set; }

    public void Update(float dt, bool reduceMotion) => Update(dt, reduceMotion, null);

    /// <summary>Advances the clock and the swing. <paramref name="seat"/> is where the fan is
    /// sown THIS frame in cell pixels, hop offset included, so a swinging fan is in time with the
    /// sheet by construction; null lets the fan relax to plumb. Reduce-motion parks everything
    /// where it stands: the contract removes motion, not body parts.</summary>
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
            // First seat after a gap: start ON it rather than chasing in from another shell's.
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

    /// <summary>Builds the whole fan into the scratch buffers in cell-local pixels of
    /// <paramref name="def"/>'s own manifest. <paramref name="seat"/> is the seat anchor for this
    /// cell (already per-frame, so it carries the squash), <paramref name="seatDepth"/> the wrap
    /// arc's half-depth that sets the inner strands a little nearer the viewer.</summary>
    /// <param name="scale">Multiplies the LENGTH fields only (Len, Root, Spread). A shell's own fan is
    /// authored in that shell's cell space and passes 1; a WORN fan is authored in 256-space like every
    /// other accessory and passes the cell ratio times the shell's fit, or the Antennae are one size on
    /// every body. Angles, counts and speeds are dimensionless and never scale.</param>
    public void Build(StrandDef def, Vector2 seat, float seatDepth, float scale = 1f)
    {
        var count = Math.Clamp(def.Count, 0, MaxStrands);
        var segs = def.Jointed ? Knee.Length : Math.Clamp(def.Segs, 2, MaxSegments);
        Strands = count;
        Knots = segs + 1;
        Bulb = def.Bulb * def.Root * scale;

        // Turns, not seconds: `waves` counts crests along the strand and `speed` is crests per second.
        var phase = _clock * def.Speed;

        // The body's own drag, plus whatever an emote is leaning on top of it.
        var trail = (_lean + LeanBias) * def.Swing;

        for (var i = 0; i < count; i++)
        {
            BuildOne(def, seat, seatDepth, phase, trail, i, count, segs, scale);
        }
    }

    /// <summary>Knot <paramref name="knot"/> of strand <paramref name="strand"/>, cell-local.</summary>
    public Vector2 PointAt(int strand, int knot) => _points[(strand * (MaxSegments + 1)) + knot];

    /// <summary>Radius at that knot, cell-local pixels.</summary>
    public float RadiusAt(int strand, int knot) => _radii[(strand * (MaxSegments + 1)) + knot];

    /// <summary>One strand. The angle is integrated rather than sampled, which is what conserves
    /// arc length: a strand bends, it never stretches.</summary>
    private void BuildOne(
        StrandDef def, Vector2 seat, float seatDepth, float phase, float trail, int index, int count, int segs,
        float scale)
    {
        var raw = count == 1 ? 0f : ((index / (float)(count - 1)) * 2f) - 1f;
        var side = raw < 0f ? -1f : 1f;
        var fan = MathF.Abs(raw);

        var psi = fan * 1.6f;
        var k = MathF.Max(0.25f, 1f - (def.Stagger * (1f - fan)));
        var outward = MathF.Sin(def.Dir) >= 0f ? -1f : 1f;

        // Scattered by strand INDEX rather than fan position, so mirrored pairs get different water.
        var driftPhase = Frac(index * DriftPhaseStep);
        var driftRate = 1f + (0.35f * Frac(index * DriftRateStep));
        var drift = def.Drift * MathF.Sin(((_clock * def.DriftSpeed * driftRate) + driftPhase) * MathF.Tau);

        var p = new Vector2(seat.X + (side * fan * def.Spread * scale), seat.Y + (seatDepth * (1f - fan)));
        var rootR = def.Root * k * scale;
        var tipR = def.Root * def.Taper * k * scale;
        var segLen = (def.Len * k * scale) / segs;
        var ang = def.Dir + (outward * def.Splay * fan);

        var b = index * (MaxSegments + 1);
        _points[b] = p;
        _radii[b] = rootR;

        for (var s = 1; s <= segs; s++)
        {
            var u = s / (float)segs;

            // The wave fades in over the first sixth of the strand, so the root stays planted.
            var hold = MathF.Min(1f, u / 0.15f);
            var wave = def.Amp * AmpScale * hold * MathF.Sin(((phase - (u * def.Waves)) * MathF.Tau) + psi);

            if (def.Jointed)
            {
                // A jointed limb sways only at the HIP; the knee and ankle hold their profile.
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
