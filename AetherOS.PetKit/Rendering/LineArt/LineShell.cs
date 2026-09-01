namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Numerics;

using AetherOS.PetKit.Engine;

/// <summary>
/// What every drawn shell shares, extracted at the SECOND shell, which is the right time, on
/// the foundry's own rule: one copy is a file, two copies is a habit, and there are seven more
/// shells on the roster.
///
/// <para><b>The division this file defends.</b> A shell owes geometry, a pose table and an anchor
/// table, and nothing else. Timing, easing, mood, the eye's whole behaviour and every future bit
/// of expression live HERE, so a feature written once lands on every shell at the moment it is
/// converted rather than nine times afterwards.</para>
/// </summary>
public static class LineShell
{
    /// <summary>What the eyes are doing, as CONTINUOUS quantities wherever they can be.
    ///
    /// <para>The sheet spent a whole cell on each lid position, so a blink was five rungs of a
    /// ladder and the pet could only ever be standing on one of them. Drawn, the lid is a NUMBER:
    /// 0 is wide open, 1 is shut, and every value between is a real position an eye can hold. The
    /// authored cells become keys on that number rather than the only places it may rest.</para>
    ///
    /// <para><see cref="Straight"/> survives as a flag rather than a number because it is not a
    /// position, it is a different lid: a blink is a lid in motion, which curves with the eye,
    /// and drowsiness is a lid at rest, which lies flat across it. <see cref="Happy"/> is
    /// likewise an expression, not a lid height.</para></summary>
    public readonly record struct EyeState(
        float Lid, float Widen, float Squint, bool Straight, bool Happy,
        float GazeX = 0f, float GazeY = 0f)
    {
        public static EyeState Lerp(EyeState a, EyeState b, float t)
        {
            var u = Math.Clamp(t, 0f, 1f);
            return new EyeState(
                a.Lid + ((b.Lid - a.Lid) * u),
                a.Widen + ((b.Widen - a.Widen) * u),
                a.Squint + ((b.Squint - a.Squint) * u),
                u < 0.5f ? a.Straight : b.Straight,
                u < 0.5f ? a.Happy : b.Happy,
                a.GazeX + ((b.GazeX - a.GazeX) * u),
                a.GazeY + ((b.GazeY - a.GazeY) * u));
        }
    }

    public static readonly EyeState Open = new(0f, 0f, 0f, false, false);
    public static readonly EyeState Wide = new(0f, 1f, 0f, false, false);
    public static readonly EyeState HalfShut = new(0.50f, 0f, 0f, false, false);
    public static readonly EyeState Shut = new(1f, 0f, 0f, false, false);
    public static readonly EyeState Happy = new(0f, 0f, 0f, false, true);
    public static readonly EyeState Squint = new(0f, 0f, 1f, false, false);

    // Where the creature is LOOKING, distinct from what its lids are doing: two numbers moving
    // a pupil inside an eye it never leaves.
    public static readonly EyeState Down = new(0f, 0f, 0f, false, false, 0f, 1f);
    public static readonly EyeState Up = new(0f, 0f, 0f, false, false, 0f, -1f);
    public static readonly EyeState Away = new(0f, 0f, 0f, false, false, 1f, 0.25f);
    public static readonly EyeState Downcast = new(0.5f, 0f, 0f, false, false, 0f, 0.8f);

    // The five rest-registered rungs, cells 33-37 on every sheet, and the thing most likely to
    // be forgotten when converting a shell: leave them out and every drowsy state silently
    // clamps back to the rest cell and the pet just stares.
    public static readonly EyeState ThreeQ = new(0.28f, 0f, 0f, false, false);
    public static readonly EyeState Quarter = new(0.74f, 0f, 0f, false, false);
    public static readonly EyeState Drowsy = new(0.50f, 0f, 0f, true, false);
    public static readonly EyeState Heavy = new(0.67f, 0f, 0f, true, false);

    /// <summary>Every pose channel any shell on the roster uses.
    ///
    /// <para><b>Why this is a bag of named floats and not three fields.</b> The Jelly and the Crab
    /// both pose on <c>sx, sy, dy</c>, and after two shells that looked like the contract. It is
    /// not: it is what the two SIMPLEST shells happen to share. The Puffer does not squash at
    /// all: it inflates, uniformly, about the ball's own centre, and its generator says that
    /// difference "is the whole character". The Pennant carries a travelling wave; the Nautilus
    /// retreats into its shell; the Serpent has four channels for its coil. Seven shells, seven
    /// pose signatures, no two alike.</para>
    ///
    /// <para>So the shared layer stopped caring what a channel MEANS. It splines them, springs
    /// them and shades them with mood generically, and each shell reads the handful it uses and
    /// maps those to its own geometry. A feature written here still lands on every shell,
    /// because none of it knows whether it is moving a squash or a puff.</para></summary>
    public enum Ch
    {
        Sx,
        Sy,
        Dy,
        Theta,
        K,
        Glow,
        Spike,
        Fin,
        Phase,
        Amp,
        Retreat,
        Rock,
        Lean,
        Spin,
        Neck,
        Sway,
        Shake,

        /// <summary>How far a stacked mass settles INTO the one under it. The Muffle's own
        /// channel and the first on the roster that moves one part of a body relative to another
        /// rather than deforming the whole of it: a snowman is a thing that was stacked, so the
        /// only motion available to it is the stack giving.
        ///
        /// <para>Additive and in authoring pixels, so it rides <see cref="PoseAt"/>'s spline
        /// unclamped and is free to overshoot, which is where the thud in a landing comes
        /// from.</para></summary>
        Sink,

        /// <summary>How strongly a motion ghost draws. A BOOL on the sheet, where a cell either
        /// carries the doubled rattle or does not; a number here, so it fades up and down across
        /// the blend instead of popping on for two cells.</summary>
        Blur,
        Count,
    }

    /// <summary>The channel values for one pose. An inline array, so it is a value with no
    /// allocation - this is built several times a frame per pet.</summary>
    [System.Runtime.CompilerServices.InlineArray((int)Ch.Count)]
    public struct Channels
    {
        private float first;
    }

    /// <summary>A pose with nothing happening: the multiplicative channels at 1, the additive
    /// ones at 0. Shells build their keys from this and set only what they use, so a channel a
    /// shell never heard of cannot quietly scale its body to nothing.</summary>
    public static Channels Neutral()
    {
        var c = default(Channels);
        c[(int)Ch.Sx] = 1f;
        c[(int)Ch.Sy] = 1f;
        c[(int)Ch.K] = 1f;
        c[(int)Ch.Glow] = 1f;
        c[(int)Ch.Spin] = 1f;
        return c;
    }

    /// <summary>One authored cell: the pose channels, the eye and the blush flag. Each shell
    /// builds these through its own factory matching its generator's <c>P()</c> signature, so a
    /// table still transcribes line for line.</summary>
    public readonly record struct Key(Channels Ch, EyeState Eye, bool Blush);

    /// <summary>A shell's eye, as numbers rather than as drawing code.
    ///
    /// <para>This is the payoff of doing a second shell before generalising. The Jelly's face and
    /// the Crab's are the same DRAWING (a field, a modelling highlight, an outboard pupil, a big
    /// catchlight up-left and a small one down-right, an ink ring, a lash line) at different
    /// sizes. So the drawing belongs here once and each shell supplies fifteen numbers. Seven
    /// more shells is seven more rows, not seven more eye renderers.</para></summary>
    public readonly record struct EyeRig(
        float Dx,
        float Y,
        float Rx,
        float Ry,
        float PupilRx,
        float PupilRy,
        float RingW,
        float PupilOut,
        float BigDx,
        float BigDy,
        float BigR,
        float SmallDx,
        float SmallDy,
        float SmallR,
        float ShutBow,
        float LashW,
        bool ConcentricRim = false,
        float PupilDown = 0.09f);

    /// <summary>How a shell's body answers a change of pose: the material it is made of, as one
    /// number. 0 is rigid (chitin, brass, stone: it arrives at the pose and stays there), 1 is
    /// slack (jelly, cloth, a hanging pennant: it swings past and settles).
    ///
    /// <para>Part of the SHELL CONTRACT rather than of the shared layer, because it is the one
    /// thing about motion only the shell knows. Everything else about how a pet moves is the same
    /// for all of them; whether a body wobbles is what makes a jellyfish not a crab.</para></summary>
    public readonly record struct Material(float Springiness, float TrimLag)
    {
        /// <summary>The default a converted shell gets until somebody has looked at it: rigid
        /// body, a touch of lag on the trimmings. Deliberately dull: a wrong wobble is far more
        /// noticeable than no wobble, so a shell should have to ASK to be slack.</summary>
        public static readonly Material Rigid = new(0f, 0.35f);
    }

    /// <summary>The spring that carries a body toward its posed shape, and the trimmings behind
    /// the body. One per drawn pet, owned by the caller, because it is the only thing in this
    /// whole system that has memory.
    ///
    /// <para><b>Why a spring rather than an ease.</b> An ease arrives and stops; a spring
    /// overshoots and settles, which is what follow-through IS. It is also why this can be shared
    /// across every shell: the shell says how slack it is and the maths is identical.</para>
    ///
    /// <para><b>Trimmings lag the body.</b> Markings, beads and speckles are
    /// ON the creature rather than part of its silhouette, so they can arrive a beat late without
    /// the outline tearing. It is the cheapest thing in the list that makes a drawing feel alive,
    /// and on a sheet it would have meant authoring every marking on every cell.</para></summary>
    public sealed class LineMotion
    {
        private Channels body;
        private Channels trim;
        private Channels vel;
        private bool primed;
        private float beat;

        /// <summary>The tick this state was last advanced on.
        ///
        /// <para>A creature can be DRAWN more than once in a frame and must only MOVE once: the
        /// floating pet and the one in the app window are the same animal seen twice, and the
        /// sheen sweep redraws a body five more times on top of that. Stepping a spring per draw
        /// would run it at six times the clock and stiffen the whole roster - so the tick is
        /// stamped, and the second caller in a tick gets the answer the first one computed rather
        /// than a further step.</para></summary>
        private long stamp = -1;

        /// <summary>Fixed integration substep; one stuttered frame past ~1/16 s flips the damped
        /// velocity, so the spring never takes a frame-sized step. HandFx steps at 1/120 for the
        /// same reason.</summary>
        private const float SpringStep = 1f / 120f;

        /// <summary>The creature's own beat, advanced at its OWN RATE.
        ///
        /// <para>It used to come from the controller, free-running at a fixed hertz, and that was
        /// wrong for the one channel that is a rate rather than a phase. <see cref="Ch.Spin"/>
        /// says how fast the creature is turning, and a top that is asleep is still turning -
        /// slower. Multiplying a free phase by 0.45 does not slow it, it teleports it, so the
        /// beat has to be INTEGRATED per pet with the rate inside the integral.</para>
        ///
        /// <para>Every shell whose neutral leaves Spin at 1 gets exactly the old beat back, so
        /// this costs the other eight nothing.</para></summary>
        public float Beat => this.beat;

        /// <summary>Advances the beat by one frame at the pose's spin rate. Called with the
        /// AUTHORED target, before the ambient substitution that consumes the result - the rate
        /// is a thing the animator says, not a thing the spring settles to.</summary>
        public float Advance(Channels target, float dt, long tick)
        {
            if (tick == this.stamp)
            {
                return this.beat;
            }

            // ONE CLOCK, not two. A clip that ACTS a cyclic channel drives it from the pose
            // table and WithAmbient leaves it alone; a clip that does not gets the free beat.
            // While the table drives, the beat mirrors it, so the handover back is seamless
            // (one frame of lag: RecordDriven runs after this frame's WithAmbient).
            if (this.drivenLive)
            {
                this.beat = this.drivenValue;
            }

            var rate = MathF.Max(0f, target[(int)Ch.Spin]);
            this.beat = Wrap(this.beat + (Math.Clamp(dt, 0f, 0.1f) * rate));
            return this.beat;
        }

        /// <summary>What WithAmbient reported last frame: whether the clip's own table is
        /// driving a cyclic channel, and the value it drove. Refreshed every frame, so a shell
        /// or form swap corrects itself on the next one.</summary>
        private bool drivenLive;

        private float drivenValue;

        public void RecordDriven(bool clipDrives, float value)
        {
            this.drivenLive = clipDrives;
            this.drivenValue = value;
        }

        /// <summary>The body's channels, and the trimmings'. With <see cref="Material.Rigid"/> the
        /// body is the target exactly, so a rigid shell costs nothing and cannot be destabilised
        /// by this at all.</summary>
        public (Channels Body, Channels Trim) Step(Channels target, Material material, float dt, long tick)
        {
            if (tick == this.stamp)
            {
                return (this.body, this.trim);
            }

            this.stamp = tick;
            if (!this.primed || dt <= 0f)
            {
                this.body = target;
                this.trim = target;
                this.vel = default;
                this.primed = true;
                return (this.body, this.trim);
            }

            var step = Math.Clamp(dt, 0f, 0.1f);
            if (material.Springiness <= 0.001f)
            {
                this.body = target;
            }
            else
            {
                // Stiffness falls and damping rises as slackness drops, so 1 swings and settles
                // where 0.2 barely registers. Damped hard enough that no value here can ring.
                var k = 260f - (140f * material.Springiness);
                var d = 2f * MathF.Sqrt(k) * (1.05f - (0.25f * material.Springiness));

                // Integrated in FIXED substeps, never in one frame-sized step: at this damping a
                // step past ~1/16 s flips the velocity and the body detonates across the screen.
                for (var rem = step; rem > 0f; rem -= SpringStep)
                {
                    var h = MathF.Min(rem, SpringStep);
                    for (var i = 0; i < (int)Ch.Count; i++)
                    {
                        // A cyclic channel is passed straight through. Springing a phase would drag
                        // it the long way round every wrap, and a travelling wave has nothing to
                        // overshoot toward anyway.
                        if (IsCyclic((Ch)i))
                        {
                            this.body[i] = target[i];
                            continue;
                        }

                        this.vel[i] += (((target[i] - this.body[i]) * k) - (this.vel[i] * d)) * h;
                        var v = this.body[i] + (this.vel[i] * h);
                        this.body[i] = IsScale((Ch)i) ? MathF.Max(0.30f, v) : v;
                    }
                }
            }

            // The trimmings chase the BODY, not the target: they follow where the creature
            // actually went, which is what makes them read as sitting on it.
            var lag = 1f - MathF.Exp(-(18f - (14f * material.TrimLag)) * step);
            for (var i = 0; i < (int)Ch.Count; i++)
            {
                this.trim[i] = IsCyclic((Ch)i)
                    ? this.body[i]
                    : this.trim[i] + ((this.body[i] - this.trim[i]) * lag);
            }

            return (this.body, this.trim);
        }
    }

    /// <summary>WHICH PART OF THE CREATURE A PIN BELONGS TO, and therefore which of the shell's
    /// transforms it should take.
    ///
    /// <para>A shell does not have one pose transform, it has several - a face that takes half
    /// the squash so the creature stays lookable-at, a roll that takes none because it is the
    /// thing being hung from, a shell that swings where the soul inside it does not. Each shell's
    /// own table names the pins it knows. This is for the ones it does not: a hat pins to
    /// <c>head</c>, which every shell names, but a pair of ears pins to <c>earL</c>/<c>earR</c>,
    /// which almost none do - and ears sitting on a head that takes a half-deform must take the
    /// same half-deform or they will drift off it exactly as far as the deform goes.</para>
    ///
    /// <para>The POSITION never comes from here. It comes from the manifest's own rest-cell
    /// anchor, because that is where the wardrobe was tuned; this decides only how that point
    /// moves. Every shell transform is the identity at neutral, so a pin sits exactly where it
    /// always sat and simply travels with the body from there.</para></summary>
    public enum PinKind
    {
        /// <summary>The mass. Tails, hems, strand seats, and anything unrecognised.</summary>
        Body,

        /// <summary>The face plate: eyes and mouth, and whatever is worn on them.</summary>
        Face,

        /// <summary>The crown: hats, ears, hair.</summary>
        Head,

        /// <summary>The nubs, which are the one pin a shell answers from its own live geometry
        /// rather than from a stored point - see the note on each shell's <c>Pin</c>.</summary>
        Hand,
    }

    /// <summary>Which part a pin name belongs to. Unknown names are body, which is the right
    /// guess: an accessory anchored somewhere a shell has never heard of is hanging off the
    /// mass.</summary>
    public static PinKind KindOf(string name) => name switch
    {
        "handL" or "handR" => PinKind.Hand,
        "face" or "mouth" => PinKind.Face,
        "head" or "earL" or "earR" or "hair" => PinKind.Head,
        _ => PinKind.Body,
    };

    /// <summary>Rotates a finished point about a pivot, then lifts it: what a shell that HANGS
    /// needs, and the first thing on this roster to use a real rotation.
    ///
    /// <para>Applied to points that are already posed, never folded into the pose, and the reason
    /// is ink: a rotation is a similarity, so stroke widths come through it unchanged. Derive a
    /// width through a squash-then-rotate and an outline thins on the swung frames, which would
    /// stop it matching the code-drawn arm and cord beside it - both of which ink at a constant
    /// width from the manifest's own lineColor.</para></summary>
    public static Vector2 Swing(Vector2 p, Vector2 pivot, float degrees, float dy)
    {
        var a = degrees * MathF.PI / 180f;
        var d = p - pivot;
        var cos = MathF.Cos(a);
        var sin = MathF.Sin(a);
        return pivot + new Vector2((d.X * cos) - (d.Y * sin), (d.X * sin) + (d.Y * cos) + dy);
    }

    /// <summary>Mood, as posture. Mood already reaches the pet through how FAST it moves
    /// (<c>AnimationController.IdleRate</c>); this lets it reach how the pet HOLDS itself, which
    /// is the half a sheet could never afford; it would have meant a second set of cells for
    /// every rung of the ladder.
    ///
    /// <para>Small on purpose. These are postures, not expressions: the pet should read as a
    /// little heavier when it is flat and a little lifted when it is bright, and a viewer should
    /// notice the mood without being able to say which pixel moved.</para></summary>
    public static Channels WithMood(Channels q, MoodLevel mood)
    {
        var (squash, lift) = mood switch
        {
            MoodLevel.Napping => (0.030f, 5.0f),
            MoodLevel.Dozy => (0.022f, 3.6f),
            MoodLevel.Mellow => (0.012f, 1.8f),
            MoodLevel.Bright => (-0.008f, -1.4f),
            MoodLevel.Beaming => (-0.016f, -2.6f),
            _ => (0f, 0f),
        };

        // Written to the posture channels only. A shell that does not read one simply never sees
        // it: the Puffer takes the settle through K and ignores the squash entirely.
        q[(int)Ch.Sx] += squash;
        q[(int)Ch.Sy] -= squash;
        q[(int)Ch.K] -= squash * 0.5f;
        q[(int)Ch.Dy] += lift;
        return q;
    }

    /// <summary>An emote, as posture: <see cref="WithMood"/>'s trick again. The morph is written
    /// into the pose the shell is asked to hold, so the geometry answers it, where an
    /// <c>EmotePoseDelta</c> only moves the finished picture. Composed before the material, so a
    /// jelly's emote wobbles into place and a chitin one arrives, from the same authored numbers.
    /// A dial a shell does not read never reaches it, and the unit conversions live here because
    /// this is the only place that knows what a channel measures.</summary>
    public static Channels WithEmote(Channels q, EmoteMorph m)
    {
        // Mood's own mapping, so the two systems never disagree about what posture is.
        q[(int)Ch.Sx] += m.Squash;
        q[(int)Ch.Sy] -= m.Squash;
        q[(int)Ch.K] -= m.Squash * 0.5f;
        q[(int)Ch.Dy] += m.Lift;

        q[(int)Ch.Shake] += m.Tremble;
        q[(int)Ch.Blur] += m.Blur;
        q[(int)Ch.Glow] += m.Glow;

        q[(int)Ch.Retreat] += m.Withdraw;
        q[(int)Ch.Sink] += m.Withdraw * MuffleSettle;

        // Tip: one idea, three channels, because a lean is authored in each shell's own units.
        // Each span is that shell's authored extreme, so a tip of 1 is as far as it ever goes.
        q[(int)Ch.Lean] += m.Tip * LeanSpan;
        q[(int)Ch.Sway] += m.Tip * SwaySpan;
        q[(int)Ch.Rock] += m.Tip * RockSpan;

        // Bristle and Ripple are proportions rather than spans: the Puffer authors Ch.Spike in
        // pixels and the Grumble as a factor, and no additive span serves both.
        q[(int)Ch.Spike] *= MathF.Max(0f, 1f + m.Bristle);
        q[(int)Ch.Amp] *= MathF.Max(0f, 1f + m.Ripple);

        // Rate may be scaled only because LineMotion.Advance integrates the beat with it;
        // multiplying a free-running phase does not slow it, it teleports it.
        q[(int)Ch.Spin] = MathF.Max(0f, q[(int)Ch.Spin] * (1f + m.Rate));

        // Floors the scaling channels against a mistyped authored number, so a bad morph draws
        // a squashed pet rather than an inverted one.
        q[(int)Ch.Sx] = MathF.Max(q[(int)Ch.Sx], ScaleFloor);
        q[(int)Ch.Sy] = MathF.Max(q[(int)Ch.Sy], ScaleFloor);
        q[(int)Ch.K] = MathF.Max(q[(int)Ch.K], ScaleFloor);
        q[(int)Ch.Glow] = MathF.Max(q[(int)Ch.Glow], 0f);
        return q;
    }

    /// <summary>What one full unit of <see cref="EmoteMorph.Tip"/> is worth in each shell's own
    /// lean. Taken off the pose tables rather than chosen: the Spintop authors -9 to 15 degrees,
    /// the Muffle -9 to 7 pixels of head lag, the Serpent plus or minus 5 of body offset, and the
    /// Nautilus -5 to 4 of rock.</summary>
    private const float LeanSpan = 9f;

    private const float SwaySpan = 5f;

    private const float RockSpan = 4.5f;

    /// <summary>How deep the Muffle's head settles on its own hop, and therefore what one whole
    /// unit of <see cref="EmoteMorph.Withdraw"/> is worth in <see cref="Ch.Sink"/>'s pixels.
    /// Taken off that shell's pose table rather than chosen.</summary>
    private const float MuffleSettle = 8f;

    /// <summary>The smallest a morph may leave a scaling channel. Low enough never to clip a real
    /// emote (the deepest authored squash on the roster is a fifth of this away from 1), high
    /// enough that a mistyped one still draws a creature.</summary>
    private const float ScaleFloor = 0.2f;

    /// <summary>Catmull-Rom through four keys, evaluated between the middle two.
    ///
    /// <para>Straight lines between keys are smooth in position but not in speed: the creature
    /// changes direction instantly at every key, and on an eight-key loop that reads as a faint
    /// tick eight times a cycle: the sheet's stepping, quieter but still there.</para></summary>
    public static float Spline(float p0, float p1, float p2, float p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5f * ((2f * p1)
            + ((-p0 + p2) * t)
            + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t2)
            + ((-p0 + (3f * p1) - (3f * p2) + p3) * t3));
    }

    /// <summary>The pose between two keys as a curve through their neighbours, channel by
    /// channel. Clamped after the fact on the SCALING channels only: a Catmull-Rom may overshoot,
    /// which is usually the charm of it (a squash carries a little past the pose it was authored
    /// at) but must never invert a scale. An additive channel (a bob, a sway, an angle) is left
    /// free to overshoot, because that is exactly where the life in it is.</summary>
    public static Channels PoseAt(Key[] poses, int prev, int cell, int next, int after, float phase)
    {
        var p = poses[Math.Clamp(prev, 0, poses.Length - 1)].Ch;
        var a = poses[Math.Clamp(cell, 0, poses.Length - 1)].Ch;
        var b = poses[Math.Clamp(next, 0, poses.Length - 1)].Ch;
        var n = poses[Math.Clamp(after, 0, poses.Length - 1)].Ch;
        var t = Math.Clamp(phase, 0f, 1f);

        var outp = default(Channels);
        for (var i = 0; i < (int)Ch.Count; i++)
        {
            if (IsCyclic((Ch)i))
            {
                // A PHASE is not a number on a line, it is a position on a circle, and a curve
                // through 0.875 and then 0.000 reads that wrap as a long run BACKWARDS - seven
                // eighths of a cycle in reverse, every time the loop comes round. That is the
                // roll that travelled part way down the foot and then snapped back. Unwrapped so
                // each key is the nearest representation of itself to the last, splined, then
                // folded back into [0, 1).
                var u1 = a[i];
                var u0 = Near(p[i], u1);
                var u2 = Near(b[i], u1);
                var u3 = Near(n[i], u2);
                outp[i] = Wrap(Spline(u0, u1, u2, u3, t));
                continue;
            }

            var v = Spline(p[i], a[i], b[i], n[i], t);
            outp[i] = IsScale((Ch)i) ? MathF.Max(0.30f, v) : v;
        }

        return outp;
    }

    /// <summary>Channels that belong to the CREATURE rather than to a clip: a wingbeat, a foot
    /// wave, a ripple, a top's spin. They run because the animal is alive, not because something
    /// is happening to it. <see cref="Ch.Spin"/> joined at the Spintop: a top does not stop
    /// spinning because it blinked.</summary>
    private static bool IsAmbient(Ch c) => c is Ch.Theta or Ch.Phase or Ch.Spin;

    /// <summary>Lets the creature's own beat carry on through any clip that does not act it. A
    /// clip that holds an ambient channel at one value across all its cells has no opinion about
    /// it (a blink is the shell at rest, lids aside), so the free beat takes over; where the clip
    /// DOES act the channel, the authored values win. Asking the TABLE covers every such clip
    /// without naming one.</summary>
    public static Channels WithAmbient(
        Channels target, Key[] poses, System.ReadOnlySpan<int> frames, float beat,
        out bool clipDrives, out float driven)
    {
        clipDrives = false;
        driven = 0f;
        for (var i = 0; i < (int)Ch.Count; i++)
        {
            if (!IsAmbient((Ch)i))
            {
                continue;
            }
            if (ClipActs(poses, frames, i))
            {
                // The table is performing this channel; report a cyclic one so the beat can
                // mirror it and hand over seamlessly when the clip ends. No shell acts both.
                if (IsCyclic((Ch)i) && !clipDrives)
                {
                    clipDrives = true;
                    driven = target[i];
                }
                continue;
            }

            // A cyclic ambient channel takes the beat; a non-cyclic one (a spin RATE rather
            // than a phase) simply keeps whatever it had, which is what "the clip has no
            // opinion" means for a quantity that is not going round.
            if (IsCyclic((Ch)i))
            {
                target[i] = beat;
            }
        }

        return target;
    }

    /// <summary>Does this clip move the channel at all, or only hold it?</summary>
    private static bool ClipActs(Key[] poses, System.ReadOnlySpan<int> frames, int channel)
    {
        if (frames.Length < 2)
        {
            return false;
        }

        var first = poses[Math.Clamp(frames[0], 0, poses.Length - 1)].Ch[channel];
        for (var i = 1; i < frames.Length; i++)
        {
            if (MathF.Abs(poses[Math.Clamp(frames[i], 0, poses.Length - 1)].Ch[channel] - first) > 1e-4f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Channels that live on a circle rather than on a line: the travelling phases that
    /// drive a wave down a foot, a ripple along a pennant, a swing.</summary>
    private static bool IsCyclic(Ch c) => c is Ch.Theta or Ch.Phase;

    /// <summary>The representation of <paramref name="v"/> nearest <paramref name="to"/>, so a
    /// wrap is a short step rather than a long one.</summary>
    private static float Near(float v, float to) => v + MathF.Round(to - v);

    private static float Wrap(float v) => v - MathF.Floor(v);

    /// <summary>Which channels multiply rather than add: the ones a spline must not push through
    /// zero.</summary>
    private static bool IsScale(Ch c) => c is Ch.Sx or Ch.Sy or Ch.K or Ch.Spin or Ch.Glow;

    /// <summary>The eye between two cells. Linear where the body splines, on purpose: a lid is a
    /// travelling edge with hard end stops at open and shut, and a spline's overshoot would pop
    /// the eye past wide or run the lash across the far rim.</summary>
    public static EyeState EyeAt(Key[] poses, int cell, int next, float phase) => EyeState.Lerp(
        poses[Math.Clamp(cell, 0, poses.Length - 1)].Eye,
        poses[Math.Clamp(next, 0, poses.Length - 1)].Eye,
        phase);

    public static bool BlushAt(Key[] poses, int cell) => poses[Math.Clamp(cell, 0, poses.Length - 1)].Blush;

    /// <summary>The blush between two cells, as a number rather than a flag: it fades across the
    /// blend instead of popping on for two cells, and an emote can raise it.</summary>
    public static float BlushAt(Key[] poses, int cell, int next, float phase)
    {
        var a = BlushAt(poses, cell) ? 1f : 0f;
        var b = BlushAt(poses, next) ? 1f : 0f;
        return a + ((b - a) * Math.Clamp(phase, 0f, 1f));
    }

    /// <summary>The cheek colour at a given strength. Alpha only: a paler blush is the same
    /// pigment showing less, never a different one.</summary>
    public static Vector4 BlushTint(float amount) =>
        Blush with { W = Blush.W * Math.Clamp(amount, 0f, 1f) };

    /// <summary>The pair of eyes, for any shell. Ink last so the ring caps every clipped edge.
    /// The shell says WHERE its eyes are and this says what an eye looks like:
    /// <paramref name="seat"/> takes the two centres, <paramref name="ex"/>/<paramref name="ey"/>
    /// the deform the FACE takes, whatever the body is doing.</summary>
    public static void DrawEyes(LineCanvas c, EyeRig rig, EyeState eye, Func<int, Vector2> seat, float ex, float ey, Vector4 eyeTint, Vector4 ink)
    {
        for (var side = -1; side <= 1; side += 2)
        {
            var at = seat(side);

            // Widen and squint are blends now rather than switches, so an eye can be a third of
            // the way into a startle.
            var rx = rig.Rx * ex * (1f + (0.07f * eye.Widen));
            var ry = rig.Ry * ey * (1f + (0.10f * eye.Widen)) * (1f - (0.60f * eye.Squint));

            // Fully shut, or pleased. The lid arrives here by sweeping rather than by being
            // switched on, so the last of a blink's travel is a real closing rather than a cut.
            if (eye.Happy || eye.Lid >= 0.995f)
            {
                // A shut eye is ONE stroke. Sleeping lids sag in the middle, pleased ones peak:
                // same path, opposite bow, which is the whole trick.
                var w = rig.Rx * ex * 0.98f;
                var bow = (eye.Happy ? -rig.ShutBow * 1.1f : rig.ShutBow) * ey;
                c.MoveTo(at + new Vector2(-w, -bow * 0.35f));
                c.QuadTo(at + new Vector2(0f, bow), at + new Vector2(w, -bow * 0.35f));
                c.Stroke(ink, rig.LashW, closed: false);
                continue;
            }

            var lidded = eye.Lid > 0.01f;
            var lidY = at.Y - ry + (2f * ry * eye.Lid);

            if (lidded)
            {
                c.PushLidClip(lidY, at, rx, ry);
            }

            c.Ellipse(at, rx, ry, Tint(eyeTint, EyeFill));

            // Two ways a shell models its eye. Most inset a small highlight up-left; the Puffer
            // instead rims its whole eye concentrically, which is what a wet fish eye does and
            // what makes its face read as an animal's rather than a doll's.
            if (rig.ConcentricRim)
            {
                c.Ellipse(at, rx * 0.86f, ry * 0.88f, Tint(eyeTint, EyeRimV));
            }
            else
            {
                c.Ellipse(
                    at + new Vector2(-rx * 0.30f, -ry * 0.34f),
                    rx * 0.42f, ry * 0.34f,
                    Tint(eyeTint, EyeRimV) with { W = 0.75f });
            }

            // The pupil sits slightly OUTBOARD and low rather than dead centre, which is what
            // makes the pair look at the viewer, and two catchlights of different sizes are what
            // make it wet.
            // The pupil sits outboard by the rig's own amount, and then wherever the creature
            // is looking. Gaze is WORLD-signed rather than outboard: both pupils move the same
            // way, which is the entire difference between looking somewhere and going cross-eyed.
            var px = at.X + (side * rig.PupilOut * ex) + (eye.GazeX * rx * GazeSpan);
            var py = at.Y + (ry * rig.PupilDown) + (eye.GazeY * ry * GazeSpan);
            var shrink = 1f - (0.14f * eye.Widen);
            c.Ellipse(
                new Vector2(px, py),
                rig.PupilRx * ex * shrink,
                rig.PupilRy * ey * shrink * (1f - (0.58f * eye.Squint)),
                Pupil);
            c.Ellipse(new Vector2(px - (rig.BigDx * ex), py - (rig.BigDy * ey)), rig.BigR * ex, rig.BigR * ex, Spark);
            c.Ellipse(new Vector2(px + (rig.SmallDx * ex), py + (rig.SmallDy * ey)), rig.SmallR * ex, rig.SmallR * ex, Spark);

            if (lidded)
            {
                c.PopClip();
            }

            c.EllipseStroke(at, rx, ry, ink, rig.RingW);

            if (lidded)
            {
                // The one thing here that is a KIND rather than a quantity: a blink is a lid in
                // motion and curves with the eye, a heavy lid is at rest and lies flat across it.
                c.MoveTo(new Vector2(at.X - rx, lidY - (eye.Straight ? 0f : ry * 0.16f)));
                if (eye.Straight)
                {
                    c.LineTo(new Vector2(at.X + rx, lidY));
                }
                else
                {
                    c.QuadTo(
                        new Vector2(at.X, lidY + (ry * 0.30f)),
                        new Vector2(at.X + rx, lidY - (ry * 0.16f)));
                }

                c.Stroke(ink, rig.LashW, closed: false);
            }
        }
    }

    /// <summary>An arm nub: the visible shoulder a code-drawn limb grows out of. Filled at the
    /// greys <see cref="HandFx"/> samples (whatever a shell is made of, those two numbers do not
    /// change, or the arm shows a seam at the joint on every palette) and inked on its OUTER arc
    /// only, because a full ring crosses the body outline and the two together read as a lens
    /// rather than as a shoulder.</summary>
    public static void DrawNubs(
        LineCanvas c, LinePose q, float cx, float nubX, float nubY, float nubR, float inkW,
        Vector4 body, Vector4 ink, bool fill, Func<Vector2, bool>? insideBody = null)
    {
        var r = nubR * (q.Sx + q.Sy) * 0.5f;
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            var at = q.Pt(i == 0 ? nubX : (2f * cx) - nubX, nubY);
            if (fill)
            {
                c.Ellipse(at, r, r, Tint(body, NubFill));
                continue;
            }

            // Where the ink starts and stops. A fixed half circle is only right when the nub sits
            // exactly on a vertical edge: true for the Jelly, false for anything with a curved
            // flank, where half a circle ends in mid air at the bottom and cuts back across the
            // body at the top. Solving for the crossings instead means the arc lands ON the
            // silhouette at both ends whatever shape the body is, and it moves correctly through
            // every squash for free.
            var outward = side < 0 ? MathF.PI : 0f;
            var from = outward;
            var to = outward;
            if (insideBody != null)
            {
                const int Steps = 48;
                const float Sweep = MathF.PI; // never reach round the back of the nub
                for (var k = 1; k <= Steps; k++)
                {
                    var d = Sweep * k / Steps;
                    if (from == outward - (Sweep * (k - 1) / Steps)
                        && !insideBody(at + new Vector2(MathF.Cos(outward - d) * r, MathF.Sin(outward - d) * r)))
                    {
                        from = outward - d;
                    }

                    if (to == outward + (Sweep * (k - 1) / Steps)
                        && !insideBody(at + new Vector2(MathF.Cos(outward + d) * r, MathF.Sin(outward + d) * r)))
                    {
                        to = outward + d;
                    }
                }
            }
            else
            {
                var quarter = MathF.PI / 2f;
                from = side < 0 ? -MathF.PI - quarter : -quarter;
                to = side < 0 ? -quarter : quarter;
            }

            c.Arc(at, r, from, to, ink, inkW);
        }
    }

    // The authoring greys, as fractions. The sheets are grey and the palette tints them at
    // draw time; drawing straight into the palette is the same idea with the middle step gone.
    public const float Base = 191f / 255f;
    public const float Shadow = 148f / 255f;
    public const float Rim = 230f / 255f;
    public const float AccBase = 225f / 255f;
    public const float AccShadow = 196f / 255f;
    public const float AccRim = 243f / 255f;
    public const float EyeFill = 205f / 255f;
    public const float EyeRimV = 236f / 255f;
    public const float NubFill = 190f / 255f;
    public const float NubRim = 238f / 255f;

    /// <summary>How far a full gaze moves the pupil, as a fraction of the eye's own radius;
    /// small so a looking pupil never reaches the rim.</summary>
    private const float GazeSpan = 0.34f;

    /// <summary>The pupil and the catchlight are their OWN colours, not tints of the palette: a
    /// pupil that took the body tint would go pale on a pale pet and the face would lose its
    /// anchor. The eye's FIELD is a third tint, <c>Palette.EyeColor</c>, which the manifest
    /// declares as a layer role beside body and accent.</summary>
    public static readonly Vector4 Pupil = new(0x17 / 255f, 0x23 / 255f, 0x2B / 255f, 1f);

    public static readonly Vector4 Spark = new(1f, 1f, 1f, 1f);

    public static readonly Vector4 Blush = new(0xF0 / 255f, 0xA0 / 255f, 0xA0 / 255f, 0.9f);

    /// <summary>A palette colour at one of the authoring values. This one line is what the whole
    /// sheet-tinting path collapses to once the art is drawn rather than sampled.</summary>
    public static Vector4 Tint(Vector4 colour, float value) =>
        new(colour.X * value, colour.Y * value, colour.Z * value, colour.W);
}
