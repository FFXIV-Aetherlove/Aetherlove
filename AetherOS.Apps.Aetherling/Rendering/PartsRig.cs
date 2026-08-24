namespace AetherOS.Apps.Aetherling.Rendering;

using System;
using System.Numerics;

using AetherOS.Apps.Aetherling.Engine;

/// <summary>
/// One creature's ears and tail, as far as the renderer is concerned: the two animation stacks
/// and the mood driving them.
///
/// <para>A holder rather than a rig — it owns no motion of its own. The two stacks are
/// deliberately separate classes (a tail is a spine carrying a wave, an ear is a sprung shape
/// that snaps and settles) and this only saves every call site from threading both plus a mood
/// through four layers of drawing.</para>
///
/// <para><b>Rest is an instance, not a flag.</b> Surfaces that draw a pet going nowhere — the
/// wardrobe preview, the fitting bench, a sparring challenger — hand <see cref="PetDraw"/> no
/// rig and get <see cref="Rest"/>: the same stacks at clock zero, never updated. The parts then
/// draw complete everywhere, and only the surfaces that want the motion pay the plumbing for
/// it. The same bargain <see cref="KiteFx.Rest"/> strikes, for the same reason.</para>
///
/// <para><b>The ears' roots are never smoothed; the tail's is, softly.</b> The distinction is
/// visibility. An ear sits ON the crown's outline, so a gliding seat opens a gap anyone can see
/// — smoothing was tried there and read as detachment. The tail's root is buried well inside
/// the silhouette, so its seat can glide without the join ever showing, and gliding is what the
/// tail needs: it is the largest thing on screen, and per-cell anchors step at clip rate, so a
/// raw seat translates the whole tail in visible jerks on every shell. Soft on purpose — the
/// half-second of settle IS the smoothness — and the drag spring drinks from the SMOOTHED
/// signal, so the flex is a gentle trail behind the bob rather than a bounce kicked eight times
/// a second by the steps.</para>
/// </summary>
public sealed class PartsRig
{
    /// <summary>The one rig every unplumbed surface reads: built at clock zero and never
    /// advanced, so it is a creature standing still rather than a creature with no ears.</summary>
    public static PartsRig Rest { get; } = new();

    public TailFx Tail { get; } = new();

    public EarFx Ears { get; } = new();

    /// <summary>What both stacks are being asked for this frame. Set from the pet's own state
    /// and read at draw, so a Reaction someday says "happy" and the tail wags without the
    /// caller knowing a tail from an ear.</summary>
    public (TailMood Tail, EarMood Ears) Mood { get; private set; } = PartMoods.For("idle");

    private float clock;

    private int spell;

    private float nextSpellAt = 4f;

    /// <summary>The tail seat's spring rate. Critically damped; low, because the settle time is
    /// the product being bought — near 9 it glides through a clip's steps like a drawn curve
    /// while never falling more than a few cells behind the body.</summary>
    private const float TailSeatStiffness = 9f;

    private Vector2 tailSeat;

    private Vector2 tailSeatVel;

    private string seatSkin = string.Empty;

    private bool seated;

    /// <summary>The smoothed tail seat, cell-local and pre-flip, or null from a rig that has
    /// never been fed frames — <see cref="Rest"/> and the previews, whose callers fall back to
    /// the raw per-cell anchor.</summary>
    public Vector2? TailSeat => this.seated ? this.tailSeat : null;

    /// <summary>Advances the seat smoother and both stacks. <paramref name="manifest"/> and
    /// <paramref name="cellIndex"/> say where the shell's tail anchor is this frame;
    /// <paramref name="hopY256"/> is the controller's code-side lift in 256-space. Null manifest
    /// parks everything, which is what a preview wants.</summary>
    public void Update(float dt, bool reduceMotion, string mood, AtlasManifest? manifest, int cellIndex, float hopY256)
    {
        float? seat = null;
        if (manifest == null || !manifest.Anchors.ContainsKey("tail"))
        {
            this.seated = false;
        }
        else
        {
            var target = manifest.AnchorForCell("tail", cellIndex);
            if (!this.seated || manifest.Skin != this.seatSkin || reduceMotion || dt <= 0f)
            {
                // First sight and shell swaps must not glide in from a stranger's seat, and
                // reduce-motion gets attachment with nothing added: the glide is motion, and
                // reduce-motion is the promise not to add any.
                this.tailSeat = target;
                this.tailSeatVel = Vector2.Zero;
            }
            else
            {
                // Critically damped, semi-implicit, dt clamped like every spring here: no
                // overshoot to read as bounce, just the clip's steps drawn into a curve.
                var step = MathF.Min(dt, 0.1f);
                this.tailSeatVel += ((target - this.tailSeat) * (TailSeatStiffness * TailSeatStiffness * step))
                                    - (this.tailSeatVel * (2f * TailSeatStiffness * step));
                this.tailSeat += this.tailSeatVel * step;
            }

            this.seated = true;
            this.seatSkin = manifest.Skin;

            // The drag springs drink from the SMOOTHED seat plus the hop: a continuous
            // signal, so the flex is follow-through rather than a per-step flinch. The hop
            // is code-side and already smooth; it joins here so a leap still bends the tail.
            seat = this.tailSeat.Y + (hopY256 * (manifest.Cell / 256f));
        }

        if (!reduceMotion && dt > 0f)
        {
            this.clock = (this.clock + dt) % 3600f;
        }

        var asked = PartMoods.For(mood);

        // An idle creature is not a still one. Where the pet is merely going about its day the
        // tail picks its own spells — a long lazy default, a swish, an occasional slow swoosh,
        // a brief alert stillness with the flicks that come with it. Anything the pet is
        // actually DOING (a hop's wag, a nap's droop) outranks this and is passed through
        // untouched, so the ambient layer never argues with the animation.
        //
        // The ears already have a plan of their own (EarFx's twitches, flicks, perks and
        // swivels), so this exists only for the tail: the two stacks are ambient in their own
        // ways, on their own clocks, and never in step.
        this.Mood = asked.Tail == TailMood.Idle ? (this.AmbientTail(), asked.Ears) : asked;

        this.Tail.Update(dt, reduceMotion, seat, this.Mood.Tail);
        this.Ears.Update(dt, reduceMotion, this.Mood.Ears, seat);
    }

    /// <summary>
    /// The idling tail's own spell, held for several seconds at a time and chosen by the same
    /// irrational hash everything else here is scheduled by — so the sequence never repeats,
    /// never lands on a beat, and is identical however many times a frame is drawn.
    ///
    /// <para>Weighted heavily toward Idle on purpose. A tail doing something interesting most
    /// of the time is a tail nobody notices doing it; the swishes read BECAUSE the long quiet
    /// stretches between them are quiet. <see cref="TailFx"/> cross-fades the change, so a new
    /// spell arrives as the tail picking up rather than as a cut.</para>
    /// </summary>
    private TailMood AmbientTail()
    {
        while (this.clock >= this.nextSpellAt)
        {
            this.spell++;
            this.nextSpellAt += 3.5f + (9f * PartDrag.Hash(this.spell, 23f));
        }

        var roll = PartDrag.Hash(this.spell, 31f);
        return roll switch
        {
            < 0.46f => TailMood.Idle,
            < 0.74f => TailMood.Swish,
            < 0.90f => TailMood.Swoosh,
            _ => TailMood.Alert,
        };
    }
}
