namespace AetherOS.PetKit.Rendering;

using System;
using System.Numerics;

using AetherOS.PetKit.Engine;

/// <summary>
/// The tail's animation stack: a clock, a mood's program, a drag spring, and nothing else.
/// Transcribed from the tuning bench (art-intake/ears-tails-rig, <c>rig_motion.py</c>) without
/// a line of drift.
///
/// <para><b>It has never seen a radius, a colour, a length or a jag.</b> This class answers one
/// question; "how far off its rest line is each knot of the arc this frame"; in radians, and
/// <see cref="PetDraw"/> adds those deltas to the model's own rest direction and curl and
/// integrates. That seam is the whole design of the two slots: improve a swish here and every
/// tail in the game swishes better, while a new animal is a <see cref="TailPartDef"/> and no
/// code at all.</para>
///
/// <para>Split the same way <see cref="TentacleFx"/> is: this class is pure and owns the clock,
/// <see cref="PetDraw"/> does the drawing because flip, squash and the hop offset live there.
/// Advanced once per frame in <see cref="Update"/> and merely READ by <see cref="Deltas"/>, so
/// the three surfaces and the footprint measurement all see one tail.</para>
///
/// <para><b>Angles and turns, never pixels.</b> A wave of N degrees is a wave of N degrees
/// whether it is running down a fox's brush or a rabbit's puff, which is what lets one program
/// drive every model without any of them looking wrong.</para>
/// </summary>
public sealed class TailFx
{
    /// <summary>Knots a tail may ask for, root included. The bench's longest model uses 29;
    /// the cap is what lets the buffer be fixed.</summary>
    public const int MaxKnots = 33;

    /// <summary>What a mood does to a spine. Degrees and turns; see the class doc for why
    /// there is not a pixel among them.</summary>
    private readonly record struct Program(
        float Root,      // wave amplitude at the root...
        float Tip,       // ...and at the tip; a tail waves more the further out
        float Hz,        // wave cycles per second
        float Waves,     // crests along the arc: under 1 a lazy S, over 1 a ripple
        float Pivot,     // whole-tail rotation at the base; the wag's real driver
        float PivotHz,
        float Flick,     // gated tip-snap burst amplitude, 0 for none
        float Drag);     // how much of the body's motion this mood lets through

    private static Program ProgramFor(TailMood mood) => mood switch
    {
        TailMood.Swish => new Program(6f, 24f, 0.40f, 0.70f, 8f, 0.40f, 0f, 1.0f),
        TailMood.Wag => new Program(4f, 16f, 2.00f, 0.35f, 27f, 2.00f, 0f, 0.6f),
        TailMood.Swoosh => new Program(9f, 40f, 0.16f, 1.05f, 13f, 0.16f, 0f, 1.2f),
        TailMood.Alert => new Program(0.6f, 2.5f, 0.90f, 0.40f, 1.2f, 0.90f, 26f, 0.35f),
        TailMood.Sleepy => new Program(1f, 4f, 0.10f, 0.40f, 1.5f, 0.10f, 0f, 0.5f),
        _ => new Program(3f, 13f, 0.22f, 0.55f, 4f, 0.22f, 0f, 1.0f),
    };

    /// <summary>How long a mood takes to become the next one. Programs differ in amplitude and
    /// rate, so switching between them on a single frame steps the whole tail at once; a snap
    /// exactly where the point was to look unhurried. Half a second of crossfade turns "the
    /// mood changed" into "the tail picked up", which is what an animal does.</summary>
    private const float BlendSeconds = 0.5f;

    private readonly float[] deltas = new float[MaxKnots];
    private readonly PartDrag drag = new();

    private float clock;

    private TailMood mood = TailMood.Idle;

    private TailMood previous = TailMood.Idle;

    private float blend = 1f;

    /// <summary>
    /// Advances the clock and the drag. <paramref name="seatX"/> is where the tail is sown
    /// THIS frame in the shell's own cell pixels, hop included and pre-flip; null parks the
    /// trail, which is what a wardrobe preview wants; it draws a pet going nowhere.
    ///
    /// <para>Reduce-motion (§10) parks the stack where it stands rather than clearing it: the
    /// contract removes motion, not body parts, and a tail snapped back to rest would be a
    /// lurch delivered by the setting that exists to prevent lurches.</para>
    /// </summary>
    public void Update(float dt, bool reduceMotion, float? seatX, TailMood mood)
    {
        if (reduceMotion || dt <= 0f)
        {
            return;
        }

        if (mood != this.mood)
        {
            // Cross-fade from wherever the last blend had got to, not from the mood that
            // started it: three mood changes inside half a second must not snap on the second.
            this.previous = this.blend >= 1f ? this.mood : this.previous;
            this.mood = mood;
            this.blend = 0f;
        }

        this.blend = MathF.Min(1f, this.blend + (dt / BlendSeconds));
        this.clock = (this.clock + dt) % 3600f;
        this.drag.Update(dt, seatX);
    }

    /// <summary>
    /// Angle deltas at each knot, radians, root first; add to the model's base angle.
    ///
    /// <para><paramref name="response"/> is the model's own looseness (a skinny whip answers
    /// harder than a heavy brush). It scales the whole deviation rather than any one term, so
    /// a stiff tail is a quieter version of the same performance rather than a different
    /// one.</para>
    /// </summary>
    public ReadOnlySpan<float> Deltas(int knots, float response)
    {
        knots = Math.Clamp(knots, 2, MaxKnots);
        if (this.blend >= 1f)
        {
            this.Fill(ProgramFor(this.mood), knots, response, 1f);
            return this.deltas.AsSpan(0, knots);
        }

        // Mid-change: the outgoing program laid down first, the incoming one mixed over it.
        // Blending the ANGLES rather than the parameters is what keeps this honest; two
        // sine waves at different rates have no meaningful average frequency, but their
        // outputs cross-fade perfectly well.
        this.Fill(ProgramFor(this.previous), knots, response, 1f - this.blend);
        this.Fill(ProgramFor(this.mood), knots, response, this.blend, accumulate: true);
        return this.deltas.AsSpan(0, knots);
    }

    private void Fill(Program p, int knots, float response, float weight, bool accumulate = false)
    {
        var t = this.clock;

        // The whole-tail pivot: what a wag actually is. Faded in over the first third of the
        // arc below, so the tail turns from its base rather than hinging at a point.
        var pivot = (p.Pivot * (MathF.PI / 180f)) * MathF.Sin(MathF.Tau * p.PivotHz * t);

        // The flick: a burst gated by two slow incommensurate sines, so an alert tail is
        // STILL most of the time and snaps when nobody ordered it to. A tail that flicked on
        // a beat would read as a metronome, which is the opposite of alive.
        var flick = 0f;
        if (p.Flick > 0f)
        {
            var gate = MathF.Sin(MathF.Tau * 0.11f * t) * MathF.Sin((MathF.Tau * 0.073f * t) + 1.2f);
            if (gate > 0.55f)
            {
                flick = MathF.Sin(MathF.Tau * 2.6f * t) * (p.Flick * (MathF.PI / 180f))
                        * ((gate - 0.55f) / 0.45f);
            }
        }

        // The body's yank, let through by however much this mood allows. A frightened tail is
        // clamped to the body; a lazy one swings with it.
        var trail = this.drag.Lean * p.Drag;

        for (var i = 0; i < knots; i++)
        {
            var u = i / (float)(knots - 1);
            var amp = (p.Root + ((p.Tip - p.Root) * u)) * (MathF.PI / 180f);
            var wave = amp * MathF.Sin((MathF.Tau * p.Hz * t) - (MathF.Tau * p.Waves * u));
            var hold = MathF.Min(1f, u / 0.35f);          // the root stays planted
            var snap = MathF.Max(0f, (u - 0.55f) / 0.45f); // flicks live in the last half
            var v = (wave + (pivot * hold) + (flick * snap) + (trail * hold)) * response * weight;
            this.deltas[i] = accumulate ? this.deltas[i] + v : v;
        }
    }
}
