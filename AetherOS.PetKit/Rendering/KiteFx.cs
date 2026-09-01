namespace AetherOS.PetKit.Rendering;

using System;
using System.Numerics;

/// <summary>
/// The flown-item rig: the live string-and-tail sim behind the Seabreeze Kite, and any item
/// that someday declares <c>fx: "kite"</c> in its manifest. The sprite carries only the sail;
/// the two lines the old art baked are simulated here and inked by <see cref="PetDraw"/>, along with
/// the flying line from the hand to the sail's mooring point, and the bowed tail hanging
/// below it. The geometry's origin is the generator (tools/arms/draw_summer2.py, kite_fx),
/// which publishes the mooring point through the item's manifest; nothing here re-derives it.
///
/// <para>Split the same way <see cref="TentacleFx"/> is: this class is pure, it owns the
/// clock and answers "where is the sail and every knot of the tail this frame, in the
/// accessory's own 256-space pixels relative to its pin"; <see cref="PetDraw"/> does the
/// drawing, because flip, the fit tilt and the hand's ride live there. Everything is built
/// ONCE per frame in <see cref="Update"/> and merely read at draw, so the three surfaces
/// see one kite, the same guarantee the strand rig makes, kept the cheaper way because a
/// pin-relative frame, unlike a per-cell seat, does not vary by surface.</para>
///
/// <para><b>The tug is the strand rig's follow point, in two axes.</b> The pin is a per-cell
/// anchor plus the hop arc, so it steps rather than glides; a differentiated velocity would
/// be an impulse train whose height is the render framerate. A follow point chasing the pin
/// on an under-damped spring turns the step into a smooth trail, and the sail's lag is the
/// gap between them; the hand yanks the string, the kite is dragged after, overshoots, and
/// is tugged back to place. Same fixed 120 Hz substep, same catch-up cap, for the same
/// reasons TentacleFx documents at length: the swing must be a property of the pet, not of
/// the graphics card.</para>
///
/// <para><b>The breeze is irrationals, not an RNG</b>: pairs of incommensurate sine rates
/// per axis, so the wander never visibly loops, plus a slow gust envelope over the fast
/// terms so the flutter arrives in pushes rather than as a constant shiver. Reproducible
/// across sessions and identical however many times a frame is drawn, which an RNG is not
/// without state this class is better off refusing to own.</para>
///
/// <para><b>Rest is an instance, not a flag.</b> Surfaces that draw a pet going nowhere,
/// such as the wardrobe, the fitting bench, or a challenger, hand PetDraw no rig and get
/// <see cref="Rest"/>: this same geometry built once at clock zero and never updated. The
/// item then draws complete everywhere, and only the surfaces that want the motion pay the
/// plumbing for it.</para>
/// </summary>
public sealed class KiteFx
{
    /// <summary>Knots on the tail, root included. Seven segments matches the reviewed baked
    /// tail's curvature budget at the 96 px read; more is smoothness nobody sees.</summary>
    public const int TailKnots = 8;

    /// <summary>The tail's run in accessory 256-space pixels, root to tip; the same reach
    /// the baked tail had, which is what the manifest's fxReach was priced against.</summary>
    private const float TailLen = 44f;

    /// <summary>Where the bows sit, as fractions of the tail. The baked art's own stations,
    /// so the item reads unchanged at a glance and only moves where it used to be still.</summary>
    public static readonly float[] BowStations = [0.38f, 0.72f, 1.0f];

    // --- the string's spring (see the class doc for why it is a follow point) --------------
    //
    // Slacker than the strand rig's (64/5.6): a flying line is looser than anatomy, and the
    // extra overshoot IS the flutter the item was asked for. ζ ≈ 0.34, settle ~0.8 s.
    private const float TrailStiffness = 42f;

    private const float TrailDamping = 4.4f;

    /// <summary>Sail lag per pixel of follow gap. Near 1: the kite is on a string, so for
    /// small yanks it very nearly keeps its old place while the hand moves under it.</summary>
    private const float TrailPerPixel = 0.85f;

    /// <summary>Lag cap, 256-space px. The string is only so long: one absurd delta (a shell
    /// swap, a minimised phone) must not throw the sail across the cell. Kept tight because
    /// the footprint pays for every pixel of it on all four sides (fxReach); at 7 the tug
    /// still reads plainly and the kite costs the pet barely more room than the baked art
    /// did.</summary>
    private const float TrailMax = 7f;

    private const float TrailStep = 1f / 120f;

    private const float TrailMaxCatchUp = 0.1f;

    private const float Tau = MathF.Tau;

    private readonly Vector2[] tail = new Vector2[TailKnots];

    private readonly Vector2[] bows = new Vector2[3];

    private readonly float[] bowAngles = new float[3];

    private float clock;

    private Vector2 follow;

    private Vector2 followVel;

    private bool following;

    private Vector2 trail;

    /// <summary>The one rig every unplumbed surface reads: built at clock zero, never
    /// updated, so it is the kite standing in fair weather.</summary>
    public static KiteFx Rest { get; } = new();

    public KiteFx()
    {
        this.Build();
    }

    /// <summary>Where the sail sits this frame, relative to its authored place: accessory
    /// 256-space px, y down, unflipped. Breeze wander plus the string's trail.</summary>
    public Vector2 Offset { get; private set; }

    /// <summary>The sail's yaw about the mooring point, radians, unflipped. Small on
    /// purpose: a kite that banks reads as falling.</summary>
    public float Tilt { get; private set; }

    /// <summary>Tail knot <paramref name="i"/>, relative to the MOVED mooring point (the
    /// tail rides the sail), accessory 256-space, unflipped.</summary>
    public Vector2 TailAt(int i) => this.tail[i];

    /// <summary>Bow <paramref name="i"/>'s seat on the tail, same frame as
    /// <see cref="TailAt"/>, with the tail's local direction at that station.</summary>
    public Vector2 BowAt(int i, out float angle)
    {
        angle = this.bowAngles[i];
        return this.bows[i];
    }

    /// <summary>
    /// Advances the sim. <paramref name="pin"/> is where the item's pin (the grip in the
    /// hand) sits this frame in unflipped 256-space; per-cell anchor, hand ride and hop arc
    /// included, so the string feels the body's whole motion. Null parks the trail and lets
    /// the sail come home to plumb; reduce-motion parks everything where it stands, the same
    /// contract every rig here honours; motion removed, never body parts.
    /// </summary>
    public void Update(float dt, bool reduceMotion, Vector2? pin)
    {
        if (reduceMotion || dt <= 0f)
        {
            return;
        }

        this.clock = (this.clock + dt) % 3600f;

        if (pin is not { } now)
        {
            this.following = false;
            now = this.follow;
        }
        else if (!this.following)
        {
            // First sight of a hand: start ON it, exactly as the strand rig does; chasing
            // in from another shell's pin would open the scene with a yank nothing caused.
            this.follow = now;
            this.followVel = Vector2.Zero;
            this.following = true;
        }

        for (var remaining = MathF.Min(dt, TrailMaxCatchUp); remaining > 0f; remaining -= TrailStep)
        {
            var step = MathF.Min(remaining, TrailStep);
            this.followVel += (((now - this.follow) * TrailStiffness) - (this.followVel * TrailDamping)) * step;
            this.follow += this.followVel * step;
        }

        var gap = now - this.follow;
        this.trail = -gap * TrailPerPixel;
        if (this.trail.Length() > TrailMax)
        {
            this.trail *= TrailMax / this.trail.Length();
        }

        this.Build();
    }

    /// <summary>Everything the draw reads, from the clock and the trail. Pure of any input
    /// that varies by surface, which is the multi-surface guarantee.</summary>
    private void Build()
    {
        var t = this.clock;

        // The gust envelope: two very slow incommensurate sines multiplied, lifted to
        // [0.25, 1]. The fast flutter rides it, so the shiver comes and goes in pushes;
        // a light breeze rather than a motor.
        var gust = 0.625f + (0.375f * MathF.Sin(t * 0.041f * Tau) * MathF.Sin((t * 0.023f * Tau) + 1.1f));

        // The wander: a slow soar and a fast flutter per axis, rates chosen never to line
        // up. Sideways moves more than up-and-down, which is what a tethered kite does;
        // the string fixes its radius, so the free direction is the arc.
        var wander = new Vector2(
            (3.0f * MathF.Sin(t * 0.131f * Tau)) + (1.1f * gust * MathF.Sin((t * 0.813f * Tau) + 2.0f)),
            (2.2f * MathF.Sin((t * 0.093f * Tau) + 1.3f)) + (0.8f * gust * MathF.Sin(t * 1.047f * Tau)));

        this.Offset = wander + this.trail;

        // Yaw follows the sideways slide: a kite pushed off its spot noses back towards it.
        // Clamped tight; banking is falling, and this item is having a nice day.
        this.Tilt = Math.Clamp(this.Offset.X * 0.012f, -0.14f, 0.14f);

        this.BuildTail(gust);
    }

    /// <summary>
    /// The tail, transcribed from the strand rig's loop: the angle is integrated rather than
    /// sampled, so the tail bends and never stretches. Root at the mooring point, hanging
    /// down and curling outboard (-x) the way the baked art's did, with the wave held off
    /// the root so the tail grows out of the sail rather than waggling where it joins it.
    /// </summary>
    private void BuildTail(float gust)
    {
        var t = this.clock;
        const int segs = TailKnots - 1;
        const float segLen = TailLen / segs;

        // The tail's own water: a slow lean that never repeats, gusted like the wander, plus
        // the sideways component of the sail's drag so a yanked kite flicks its tail.
        var drift = ((0.42f * MathF.Sin((t * 0.107f * Tau) + 1.7f)) + (0.30f * MathF.Sin(t * 0.049f * Tau))) * gust;
        var sway = Math.Clamp(this.trail.X * 0.05f, -0.55f, 0.55f);

        // Down and a touch outboard; +y is down in sprite space, and the sail flies at -x.
        var ang = (MathF.PI / 2f) + 0.22f;

        var p = Vector2.Zero;
        this.tail[0] = p;
        for (var s = 1; s <= segs; s++)
        {
            var u = s / (float)segs;
            var hold = MathF.Min(1f, u / 0.18f);
            var wave = 0.95f * gust * hold * MathF.Sin((((t * 0.55f) - (u * 1.2f)) * Tau) + 0.4f);
            ang += ((0.9f * u) + wave + ((drift + sway) * hold)) / segs;
            p += new Vector2(MathF.Cos(ang) * segLen, MathF.Sin(ang) * segLen);
            this.tail[s] = p;

            for (var b = 0; b < BowStations.Length; b++)
            {
                if (BowStations[b] > ((s - 1) / (float)segs) && BowStations[b] <= u + 0.0001f)
                {
                    this.bows[b] = Vector2.Lerp(this.tail[s - 1], p, 1f - ((u - BowStations[b]) * segs));
                    this.bowAngles[b] = ang;
                }
            }
        }
    }
}
