namespace AetherOS.PetKit.Rendering;

using System;
using System.Numerics;

using AetherOS.PetKit.Engine;

/// <summary>
/// The code-side limbs: clip state and kinematics for the Reaching (EvolutionSpec §4b — the
/// arms evolution, grown out of the ArtDirection §7.6 wave pilot). This class is pure — it
/// owns the rest pose and the clip clock and answers "where is each hand, and how far is
/// whatever it holds tilted, this frame"; <see cref="PetDraw"/> does the drawing, because the
/// screen-space transform (flip, squash, hop) lives there. The sheet-based
/// <c>AnimationController</c> is untouched: the body still animates from its cells, the
/// per-cell hand pins already ride that animation, and these limbs simply hang off the pins —
/// which is what keeps the two systems composable rather than competing.
///
/// <para><b>The clips are the emotes' now</b> (EmoteStudy §12). This class no longer knows a
/// single choreography: it plays whatever <see cref="HandsDelta"/> track it is handed, and
/// every track in the app comes from an <see cref="EmoteDef"/> — the wave included, which was
/// promoted from a hard-coded clip here into an ordinary emote. So an emote is one call that
/// moves body, mouth and hands together, and <see cref="PlayEmote"/> is simply ignored when
/// there are no hands to move.</para>
///
/// <para><see cref="Enabled"/> is that gate, set by the app each tick from the grant and the
/// player's own toggle (the Reaching earned, arms not tucked, pet fledged). Off means nothing
/// is drawn and nothing is followed — the render is byte-identical to the classic path, which
/// is both the pre-evolution look and the revert story.</para>
///
/// <para>On, both hands rest a few units outboard of their pins, so a sliver of tapered limb
/// shows past the silhouette — the arms are drawn BEHIND the body layers, so no code shape
/// ever paints over the sheet art and the baked nubs read as the shoulder joints (see the
/// §7.6 pilot notes for the lesson that bought this). Anything anchored to a hand pin rides
/// the limb's current offset.</para>
///
/// <para><b>The limb is a row now</b> (ArtDirection §7.6d, from art-intake/arm-lab). What
/// used to be a straight rod between the pin and the hand is a limited-length pseudopod under
/// the strand rig's own two motions, and every number in it comes off the manifest's
/// <see cref="HandStyleDef"/> — so this class gained exactly two jobs: it integrates the
/// spring the hand chases its track through, and it stirs the water the pair sits in. The
/// SHAPE between pin and hand stays <see cref="PetDraw"/>'s, like every other drawn thing.</para>
///
/// <para><b>Room.</b> Tracks are clamped to <see cref="HandsDelta.MaxReach256"/>, and that
/// same figure is charged by the stage and the floating window for every hand-anchored item
/// (<c>AccessoryFootprint</c>). It has to be: since §12 a clip plays *through* an emote's own
/// body excursion rather than only while the pet is still, so the two reaches genuinely add
/// and pretending otherwise would be the window lying about its size. The clamp is what makes
/// the reservation a promise rather than a hope — a future track cannot quietly overrun
/// it.</para>
///
/// <para><b>And limited length is a second, tighter clamp.</b> A curved row also caps the hand
/// at <see cref="HandStyleDef.Len"/> from the PIN, which at the shipped 26 is strictly inside
/// the ±30-from-rest box the stage reserves — so the spring may overshoot its follow point
/// freely and the drawn hand still cannot leave the envelope. That is the arm-lab's one open
/// question ("clamp the follow point, or charge the window a lag allowance") answered by the
/// anatomy instead of by either: a short limb cannot reach the edge of a box it does not
/// span. <see cref="TryGet"/> reports the post-limit hand, so a held item rides where the
/// hand actually IS rather than where the track asked it to be.</para>
/// </summary>
public sealed class HandFx
{
    /// <summary>The main hand's pin (ArmsSpec §1). Single-handed clips play here, so a held
    /// weapon demonstrates the lock by riding along.</summary>
    public const string RightAnchor = "handR";

    /// <summary>The off hand's pin.</summary>
    public const string LeftAnchor = "handL";

    /// <summary>Hand radius in 256-space, measured off the wispv2 sheet rather than eyeballed:
    /// the pins sit one nub-radius inside the outermost silhouette pixel (ArmsSpec §1), and
    /// walking the pin's row outward gives 13–14 px in the 384 cell ≈ 9 in 256-space. The hand
    /// must be the same circle the nub is, or the arm reads as growing a bigger paw.</summary>
    public const float HandRadius256 = 9f;

    /// <summary>The arm tapers, shoulder to wrist, and the proportions were judged from the
    /// render (tools/wave): a uniform thin capsule left the nub reading as a separate bump,
    /// while a root a shade wider than the nub slides behind it and makes it the joint.</summary>
    public const float ArmRootRadius256 = 10f;

    /// <summary>See <see cref="ArmRootRadius256"/> — the wrist end of the taper, slimmer than
    /// the hand ball so the hand still reads as a hand and not a rounded bar.</summary>
    public const float ArmWristRadius256 = 7f;

    /// <summary>The DEFAULT rest pose, and the one a row that says nothing inherits
    /// (<see cref="HandStyleDef.RestX"/> carries the live value, because where a hand rests is
    /// a judgement about a particular silhouette — see that field for why it stopped being a
    /// constant).
    ///
    /// <para>Where the right hand rests, 256-space, unflipped: outboard and down of its pin,
    /// far enough that there is a visible ARM between the shoulder and the hand rather than a
    /// hand sitting on a shoulder — which was the old (6, 3), fine while the limbs drew behind
    /// the silhouette and fatal once they drew in front of it (§7.6d). The left hand mirrors it in local space, which the flip then mirrors
    /// again for free. Every track is authored as a delta from here.
    ///
    /// <para>Public because this is the arms-out <b>pin root</b>: a hand-anchored item hangs
    /// from the pin plus this while the limbs are active, and from the bare pin while they
    /// are not, so the stage's reach arithmetic (<c>AccessoryFootprint</c>) has to know the
    /// same two roots the renderer uses.</para></summary>
    public static readonly Vector2 Rest256 = new(12f, 6f);

    /// <summary>
    /// The house pendulum, shared with the strand rig by value and not by accident: ζ ≈ 0.35,
    /// under-damped, one visible overshoot. A shell says how loosely its hand hangs
    /// (<see cref="HandStyleDef.Lag"/>); it does not get to say what a pendulum is, or the
    /// creatures would read as different physics — <see cref="TentacleFx"/>'s own argument,
    /// and it applies with more force here, where the pair is bilateral.
    /// </summary>
    private const float Zeta = 0.35f;

    /// <summary>
    /// The water's per-hand scatter, the same two irrationals <see cref="TentacleFx"/> hashes
    /// strand indices with — the golden ratio's fractional part and the plastic number's. A
    /// hash of the index rather than an RNG for the same reason it is there: the pose is
    /// resolved more than once a frame (three surfaces, plus the footprint measurement) and
    /// has to come out identical every time, with no state and no ordering guarantee.
    /// </summary>
    private const float DriftPhaseStep = 0.61803399f;

    private const float DriftRateStep = 0.75487767f;

    /// <summary>Fixed integration step for the follow spring, and the reason it is stepped
    /// rather than handed the frame's delta: an explicit integrator's error goes with the step,
    /// so at one step per rendered frame the same spring settles to a different amplitude on a
    /// 30 fps machine than on a 144 fps one. <see cref="TentacleFx"/> measured that and paid
    /// the same dozen float operations to make the motion a property of the pet.</summary>
    private const float SpringStep = 1f / 120f;

    /// <summary>How much elapsed time the spring will chase in one frame. A phone that spent a
    /// minute minimised hands us a delta measured in seconds; chasing it would either cost the
    /// frame or fling the hands.</summary>
    private const float SpringMaxChase = 0.1f;

    /// <summary>The ease-home when a clip is cut short: fast enough to be clear of the way
    /// before a hop's first airborne frame, slow enough not to pop.</summary>
    private const float RecoverSeconds = 0.12f;

    /// <summary>The ease-IN when a clip starts from hands that are not at rest (a second
    /// emote landing on top of the first). Same length as the recovery, for the same reason:
    /// it is the same motion, run the other way.</summary>
    private const float BlendSeconds = 0.12f;

    private readonly Limb right = new(Rest256);
    private readonly Limb left = new(new Vector2(-Rest256.X, Rest256.Y));

    private Func<float, HandsDelta>? track;
    private float trackSeconds;
    private float elapsed;
    private float clock;

    /// <summary>How much of the running track the hands actually spend. A practice attempt is the
    /// same curves at reduced excursion, and arms waving at full reach over a body that is only
    /// half committing is the one place the two halves of a performance can visibly disagree.</summary>
    private float amplitude = 1f;

    /// <summary>
    /// The arm this rig is drawing, straight off the manifest of whatever shell is on screen.
    /// Set by the app each tick beside <see cref="Enabled"/>, for the same reason: a shell swap
    /// must take effect on the frame it happens, and the very first drawn frame has no delta
    /// time to hang an update off.
    ///
    /// <para>Defaulted rather than nullable, and the default is the shipped row, so a caller
    /// that never sets it (the race field builds two dozen of these) draws the same arm as
    /// everything else instead of nothing at all.</para>
    /// </summary>
    public HandStyleDef Style { get; set; } = new();

    /// <summary>The master gate, set by the app each tick: the Reaching earned, the arms not
    /// tucked by the player, and the pet fledged (a hatchling has no hand pins yet — its arms
    /// arrive last, which is the growth line the Reaching continues). Off = nothing drawn,
    /// nothing followed, clips cleared.</summary>
    public bool Enabled { get; set; }

    /// <summary>A track is running. Read by the app only to explain itself (the dev panel);
    /// nothing in the render path branches on it.</summary>
    public bool Playing => this.track != null;

    /// <summary>
    /// Plays an emote's hand track, if it has one and if there are hands to play it with.
    /// Silently does nothing otherwise, which is the contract the whole §12 pass rests on:
    /// every emote calls this, and an emote is never rewritten, gated or split because a
    /// particular pet has not reached yet.
    /// </summary>
    public void PlayEmote(EmoteDef def, float amplitude = 1f)
    {
        if (!this.Enabled || def.Hands is null || def.Seconds <= 0f)
        {
            return;
        }

        this.track = def.Hands;
        this.trackSeconds = def.Seconds;
        this.amplitude = Math.Clamp(amplitude, 0f, 1f);
        this.elapsed = 0f;

        // Ease in from wherever the hands actually are. Usually that is rest and the blend is
        // a no-op; it earns itself the moment a second emote lands on a raised arm.
        this.right.Ease(BlendSeconds);
        this.left.Ease(BlendSeconds);
    }

    /// <summary>Takes both hands home: a short eased return from wherever they are. Called
    /// when a real body clip starts with no emote behind it, so a stray track can never ride
    /// a hop.</summary>
    public void Cancel()
    {
        if (this.track == null)
        {
            return;
        }

        this.track = null;
        this.right.Ease(RecoverSeconds);
        this.left.Ease(RecoverSeconds);
    }

    /// <summary>Advances the limbs. Reduce-motion (§10) kills the clip trajectories outright —
    /// the arms themselves stay, resting; the contract removes motion, not body parts.</summary>
    public void Update(float dt, bool reduceMotion)
    {
        if (!this.Enabled || reduceMotion)
        {
            this.track = null;
            this.right.Reset();
            this.left.Reset();
            return;
        }

        this.right.Advance(dt);
        this.left.Advance(dt);

        if (this.track is { } clip)
        {
            this.elapsed += dt;
            if (this.elapsed >= this.trackSeconds)
            {
                // Tracks are authored to end where they started, so this recovery is normally
                // a formality — it is here so that one which does not still lands softly.
                this.Cancel();
            }
            else
            {
                var d = Scale(Clamp(clip(this.elapsed / this.trackSeconds)), this.amplitude);
                this.right.Set(new Vector2(d.Right.X, d.Right.Y), d.RightTilt);

                // Outboard space mirrors into cell-local on the off hand — and so does the
                // tilt, or a symmetric pair would lean the same way instead of matching.
                this.left.Set(new Vector2(-d.Left.X, d.Left.Y), -d.LeftTilt);
                this.Settle(dt);
                return;
            }
        }

        this.right.Set(Vector2.Zero, 0f);
        this.left.Set(Vector2.Zero, 0f);
        this.Settle(dt);
    }

    /// <summary>Drives the limbs directly from a caller-owned delta, bypassing the internal
    /// clip clock entirely — for a rig with its own notion of time (a runner's stride phase,
    /// synced to distance rather than wall-clock seconds) rather than a fixed-length emote
    /// track. Mirrors exactly what <see cref="Update"/> does with a clip's own delta each
    /// frame, so a caller may freely alternate this with <see cref="PlayEmote"/>/<see
    /// cref="Update"/> on the same instance (the parade calling a reaction between running
    /// beats, say) without either path fighting the other — this one simply never touches
    /// <see cref="Playing"/>'s track.
    ///
    /// <para><b>Gets the limb, does not get the water.</b> The row's shape and its length limit
    /// apply here exactly as they do to an emote, but the follow spring and the drift do not:
    /// they are seconds-based motions and this path exists precisely for a caller whose clock
    /// is not seconds. Lagging a stride that is synced to DISTANCE would put the arms out of
    /// step with the legs at every change of pace, which is the one thing a gait must not
    /// do.</para></summary>
    public void DriveExternal(HandsDelta d)
    {
        if (!this.Enabled)
        {
            this.right.Reset();
            this.left.Reset();
            return;
        }

        var c = Clamp(d);
        this.right.Rest = this.Style.Rest;
        this.left.Rest = new Vector2(-this.Style.Rest.X, this.Style.Rest.Y);
        this.right.Set(new Vector2(c.Right.X, c.Right.Y), c.RightTilt);

        // Outboard space mirrors into cell-local on the off hand — and so does the tilt, or a
        // symmetric pair would lean the same way instead of matching (Update's own comment).
        this.left.Set(new Vector2(-c.Left.X, c.Left.Y), -c.LeftTilt);

        this.right.Place(Limit(this.right.Offset, this.Style));
        this.left.Place(Limit(this.left.Offset, this.Style));
    }

    /// <summary>
    /// Turns each hand's TRACK point into the point it is actually drawn at: the follow spring
    /// integrated, the water stirred, and the whole thing pulled back inside the limb's reach.
    /// The three run in that order for a reason — the spring may overshoot and the drift may
    /// push further still, and the length limit is what makes both of those free, because a
    /// limb that cannot reach the edge of the reserved box cannot overspend it.
    /// </summary>
    private void Settle(float dt)
    {
        var style = this.Style;
        var flowing = style.IsFlowing;

        // The row's rest pose, re-read every tick beside everything else it carries: a shell
        // swap must not leave the hands resting where the previous shell's arms hung.
        this.right.Rest = style.Rest;
        this.left.Rest = new Vector2(-style.Rest.X, style.Rest.Y);

        // One clock for the pair, wrapped well short of the precision cliff a float sine
        // walks off after an hour or so of uptime.
        this.clock = (this.clock + dt) % 3600f;

        // Stiffness from the row's own looseness: lag 0 is the hand nailed to its track, and
        // the shipped 0.24 settles in about a third of a second with one overshoot.
        var stiff = 400f / (0.25f + (style.Lag * 1.5f));
        var damp = 2f * Zeta * MathF.Sqrt(stiff);
        var chase = MathF.Min(dt, SpringMaxChase);

        for (var idx = 0; idx < 2; idx++)
        {
            var limb = idx == 0 ? this.right : this.left;
            var side = idx == 0 ? 1f : -1f;
            var point = limb.Offset;

            if (flowing && style.Lag > 0f)
            {
                point = limb.Follow(point, stiff, damp, chase);
            }
            else
            {
                limb.DropFollow();
            }

            if (flowing && style.Drift > 0f)
            {
                // Authored outboard, like everything else the rig builds, so the pair wanders
                // apart rather than both sliding the same way across the cell.
                var phase = Frac(idx * DriftPhaseStep);
                var rate = 1f + (0.35f * Frac(idx * DriftRateStep));
                var dx = style.Drift * Sin01((this.clock * style.DriftSpeed * rate) + phase);
                var dy = style.Drift * 0.6f * Sin01((this.clock * style.DriftSpeed * 0.8f * rate) + phase + 0.3f);
                point += new Vector2(side * dx, dy);
            }

            limb.Place(Limit(point, style));
        }
    }

    /// <summary>The limb's reach, enforced on the drawn hand: a curved row holds a maximum arc
    /// length, so the hand goes no further from the pin than the limb can carry it and STRAINS
    /// at the stop rather than clipping. The shipped rod is exempt — it has no arc to run out
    /// of, and a capsule row exists to render byte-identically to the pre-§7.6d path.</summary>
    private static Vector2 Limit(Vector2 v, HandStyleDef style)
    {
        if (!style.IsCurved || style.Len <= 0f)
        {
            return v;
        }

        // The same 0.97 the bench draws at: the last few per cent of a real pseudopod is a
        // straight line, and stopping just short is what keeps the curve a curve at full reach.
        var max = style.Len * 0.97f;
        var d = v.Length();
        return d > max && d > 0.0001f ? v * (max / d) : v;
    }

    private static float Frac(float v) => v - MathF.Floor(v);

    /// <summary>Turns per second in, radians handled here — the drift is authored in turns
    /// like <see cref="StrandDef.DriftSpeed"/>, because "one wander every twelve seconds" is a
    /// number a person can tune and 0.5027 rad/s is not.</summary>
    private static float Sin01(float turns) => MathF.Sin(turns * MathF.Tau);

    /// <summary>The limb riding <paramref name="anchor"/>, if there is one: the offset it is
    /// actually DRAWN at this frame — track, spring, water and length limit all applied, so a
    /// held item rides the hand rather than the track — from the pin (256-space, unflipped),
    /// and the tilt for whatever it holds (radians,
    /// unflipped sense — positive leans the top outboard on the right hand; PetDraw negates
    /// under FlipX). False while disabled or for anchors that are not hands, in which case
    /// items stay on their static pins exactly as before the evolution.</summary>
    public bool TryGet(string anchor, out Vector2 offset256, out float tilt)
    {
        var limb = this.Enabled
            ? anchor switch
            {
                RightAnchor => this.right,
                LeftAnchor => this.left,
                _ => null,
            }
            : null;

        offset256 = limb?.Drawn ?? Vector2.Zero;
        tilt = limb?.Tilt ?? 0f;
        return limb != null;
    }

    /// <summary>The envelope, enforced (see the class note): a track may ask for anything, and
    /// gets what the stage reserved.</summary>
    private static HandsDelta Clamp(HandsDelta d) => new()
    {
        Right = ClampReach(d.Right),
        Left = ClampReach(d.Left),
        RightTilt = Math.Clamp(d.RightTilt, -HandsDelta.MaxTilt, HandsDelta.MaxTilt),
        LeftTilt = Math.Clamp(d.LeftTilt, -HandsDelta.MaxTilt, HandsDelta.MaxTilt),
    };

    private static Vector2 ClampReach(Vector2 v) => new(
        Math.Clamp(v.X, -HandsDelta.MaxReach256, HandsDelta.MaxReach256),
        Math.Clamp(v.Y, -HandsDelta.MaxReach256, HandsDelta.MaxReach256));

    /// <summary>The whole delta at a fraction of its excursion; see <see cref="amplitude"/>.</summary>
    private static HandsDelta Scale(HandsDelta d, float k) => k >= 1f ? d : new HandsDelta
    {
        Right = d.Right * k,
        Left = d.Left * k,
        RightTilt = d.RightTilt * k,
        LeftTilt = d.LeftTilt * k,
    };

    /// <summary>One hand: its rest pose, its current pose, and the ease that carries it
    /// between a clip and rest in either direction. Tracks are authored against rest, so
    /// "no clip" and "clip finished" are the same state and an interrupted clip recovers to
    /// the same place it would have ended.</summary>
    private sealed class Limb
    {
        private Vector2 easeFrom;
        private float easeFromTilt;
        private float easeT = -1f;      // -1 = not easing
        private float easeSeconds;

        private Vector2 follow;
        private Vector2 followVel;
        private bool followLive;

        public Limb(Vector2 rest)
        {
            this.Rest = rest;
            this.Offset = rest;
            this.Drawn = rest;
        }

        /// <summary>Where this hand hangs with no track playing, cell-local (the off hand's is
        /// already mirrored by the caller). Settable because it comes off the manifest row now,
        /// and the manifest can change under a shell swap.</summary>
        public Vector2 Rest { get; set; }

        /// <summary>Where the TRACK wants this hand: rest plus the clip's delta, eased. The
        /// input to the settle, and never what gets drawn on a flowing row.</summary>
        public Vector2 Offset { get; private set; }

        /// <summary>Where the hand actually is this frame — what the renderer draws to and
        /// what a held item hangs off.</summary>
        public Vector2 Drawn { get; private set; }

        public float Tilt { get; private set; }

        /// <summary>The settle's answer, stored. Separate from <see cref="Offset"/> on purpose:
        /// feeding a spring its own output is how a follow point drifts away from the track it
        /// is supposed to be chasing.</summary>
        public void Place(Vector2 at) => this.Drawn = at;

        /// <summary>
        /// Advances the follow point toward <paramref name="target"/> and returns it. What the
        /// spring drags is a POINT chasing the track, never a differentiated velocity — the
        /// track is sampled from a clip whose own source is a per-cell anchor, so on an 8 fps
        /// sheet it holds still for several render frames and then jumps. Differentiate that
        /// and the hand gets an impulse train whose height is the render framerate; a point
        /// chasing a step just moves smoothly to it. (<see cref="TentacleFx"/> paid for this
        /// lesson first.)
        /// </summary>
        public Vector2 Follow(Vector2 target, float stiff, float damp, float chase)
        {
            if (!this.followLive)
            {
                // Born at the track, not at the origin: a spring that starts at rest and has
                // to catch up would fling both arms on the frame the grant lands.
                this.follow = target;
                this.followVel = Vector2.Zero;
                this.followLive = true;
                return this.follow;
            }

            for (var rem = chase; rem > 0f; rem -= SpringStep)
            {
                var h = MathF.Min(rem, SpringStep);
                this.followVel += (((target - this.follow) * stiff) - (this.followVel * damp)) * h;
                this.follow += this.followVel * h;
            }

            return this.follow;
        }

        /// <summary>Forgets the follow point, so a row that switches its lag off — or a pet
        /// that comes back from reduce-motion — restarts the spring at the track instead of
        /// resuming a chase from wherever it was parked.</summary>
        public void DropFollow() => this.followLive = false;

        /// <summary>Start easing FROM the current pose over <paramref name="seconds"/>. Used
        /// both to blend into a starting clip and to recover home from a cut one — it is the
        /// same motion, and one mechanism cannot disagree with itself.</summary>
        public void Ease(float seconds)
        {
            this.easeFrom = this.Offset;
            this.easeFromTilt = this.Tilt;
            this.easeSeconds = MathF.Max(0.01f, seconds);
            this.easeT = 0f;
        }

        public void Advance(float dt)
        {
            if (this.easeT >= 0f)
            {
                this.easeT += dt;
                if (this.easeT >= this.easeSeconds)
                {
                    this.easeT = -1f;
                }
            }
        }

        /// <summary>Places the hand at <paramref name="delta"/> from rest (cell-local, already
        /// mirrored by the caller), blended through any ease still running.</summary>
        public void Set(Vector2 delta, float tilt)
        {
            var offset = this.Rest + delta;
            if (this.easeT >= 0f)
            {
                var p = Math.Clamp(this.easeT / this.easeSeconds, 0f, 1f);
                var ease = p * p * (3f - (2f * p));
                offset = Vector2.Lerp(this.easeFrom, offset, ease);
                tilt = this.easeFromTilt + ((tilt - this.easeFromTilt) * ease);
            }

            this.Offset = offset;
            this.Tilt = tilt;
        }

        public void Reset()
        {
            this.easeT = -1f;
            this.Offset = this.Rest;
            this.Drawn = this.Rest;
            this.Tilt = 0f;
            this.DropFollow();
        }
    }
}
