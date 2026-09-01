using System;
using System.Numerics;

namespace AetherOS.PetKit.Engine;

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

    /// <summary>The cell the clip moves to next, and how far the clock has got toward it. Only
    /// meaningful with <see cref="SmoothAnchors"/> set: a sheet body JUMPS from cell to cell, so
    /// its pins are right to jump with it; a drawn body moves between the poses, and pins that
    /// still stepped would leave a hat snapping while the creature under it flowed.</summary>
    public int NextCellIndex;

    /// <summary>The cell before the current one and the one after next. Here because a drawn body
    /// reads its pose along a Catmull-Rom through four keys, and an anchor read as a straight
    /// line between two of them is on a different curve: the two agree at every cell boundary and
    /// part company in between, which is what a mouth drifting on a face looks like.</summary>
    public int PrevCellIndex;

    /// <inheritdoc cref="PrevCellIndex"/>
    public int AfterCellIndex;

    /// <inheritdoc cref="NextCellIndex"/>
    public float FramePhase;

    /// <summary>Blend every anchor toward <see cref="NextCellIndex"/> rather than reading the
    /// current cell's. Off for a sheet body, on for a drawn one.</summary>
    public bool SmoothAnchors;

    /// <summary>Where a DRAWN body says one of its own pins is, in cell space: null for a pin the
    /// shell does not solve, and null altogether for a sheet body. The baked anchor table is the
    /// pose curve quantised; the drawn body is that curve plus mood plus a spring, both applied
    /// after the point an anchor could have followed, so the shell's own answer is the only one
    /// that tracks the body as drawn this frame.</summary>
    public Func<string, Vector2?>? DrawnAnchor;
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
    private const float SpinSeconds = 0.5f;
    private const float HopSeconds = 0.85f;
    private const float HopReach = 42f;
    private const float HopHeight = 30f;

    private readonly Random _rng;
    private readonly ClipPlayer _clip;
    private readonly AtlasManifest _manifest;

    /// <summary>True while the SECOND blink of a pair is pending, so a double never becomes a
    /// stutter of three or four.</summary>
    private bool _doubling;

    /// <summary>How often a blink comes in a pair, and how long after the first the second
    /// follows. A fifth is often enough to read as a habit and rare enough to stay a surprise.</summary>
    private const float DoubleBlinkChance = 0.20f;

    private const float DoubleBlinkGap = 0.13f;

    private float _sinceInteraction;
    private float _blinkIn;
    private float _variantIn;
    private float _boopT = 1f;
    private float _hopT = 1f;
    private float _spinT = 1f;
    private bool _hopLeft;
    private bool _napping;
    private EmoteDef? _emote;
    private float _emoteT;
    private float _emoteAmplitude = 1f;

    public AnimationController(AtlasManifest manifest, Random? rng = null)
    {
        _rng = rng ?? new Random();
        _manifest = manifest;
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

    /// <summary>The clip on screen right now, which is what the mouth's resting shape follows.</summary>
    public string CurrentAnimation => _clip.ClipName;

    /// <summary>The cells the current clip walks, in order.</summary>
    public ReadOnlySpan<int> CurrentFrames => _clip.CurrentFrames;

    /// <summary>The cell <paramref name="delta"/> steps along the clip from the one showing.</summary>
    public int CellAtOffset(int delta) => _clip.CellAtOffset(delta);

    /// <summary>Starts this creature somewhere else in its own idle, from a seed of the caller's
    /// choosing: a pet's name, a race slot, anything stable. Two pets of the same shell breathing
    /// in perfect unison is the single most artificial thing a roster of them can do; one seeded
    /// offset removes the cause. Seeded rather than random so a given pet is the same creature
    /// every session.</summary>
    public void PrimePhase(int seed) => _clip.PrimePhase(seed);

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

    /// <summary>The hop with a turn through it: the creature's "look what I've got", for the moment it
    /// takes up a weapon of its own.
    /// <para>There is no spin sheet and there does not need to be one. A round wisp turns the way a coin
    /// does, by presenting its edge, which the pose already says with a width squeeze and a flip at the
    /// pinch. Touching only those two composes it with whatever clip is running, and every accessory comes
    /// round with the body, weapon included.</para></summary>
    public void PlayTurn()
    {
        PlayHopClip();
        _spinT = 0f;
    }

    /// <summary>The choreography playing right now, or null. One at a time: a new emote through
    /// <see cref="PlayEmote"/> replaces a running one, and everything else waits its turn.</summary>
    public EmoteDef? CurrentEmote => _emote;

    /// <summary>This frame's morph, for a drawn shell to fold into its pose before it draws.
    /// Scaled by the same amplitude the pose track takes, and sampled at the same clamped
    /// progress fraction, so the two halves of one emote never disagree about what time it is.
    /// <see cref="EmoteMorph.None"/> when no emote is playing or it has no morph track.</summary>
    public EmoteMorph CurrentMorph =>
        _emote is { Morph: { } morph } playing
            ? morph(Math.Clamp(_emoteT / playing.Seconds, 0f, 1f)) * _emoteAmplitude
            : EmoteMorph.None;

    /// <summary>Lays a choreography over whatever clip is running. Wakes it and re-arms the wandering
    /// timers, because an emote is attention even when the pet started it. Amplitude below 1 is the
    /// practice attempt: the same curves, visibly not quite having it yet.</summary>
    public void PlayEmote(EmoteDef def, float amplitude = 1f)
    {
        _sinceInteraction = 0f;
        _napping = false;
        _hopT = 1f;
        _boopT = 1f;
        _variantIn = NextVariant();
        _emote = def;
        _emoteT = 0f;
        _emoteAmplitude = amplitude;
        if (_clip.ClipName != "idle")
        {
            _clip.Play("idle");
        }
    }

    /// <summary>A visiting companion follows its owner's creature rather than its own timers: the huddle
    /// feeds it the owner's interaction clock every frame, so naps, drowsy lids and the mood land at the
    /// same moment on every member's screen.</summary>
    public void MimicInteractionClock(float sinceInteraction)
    {
        _sinceInteraction = sinceInteraction;
    }

    /// <summary>Forces the nap state to match the mimicked creature, playing the matching clip on the
    /// transition. The clock mimic above keeps <see cref="Update"/> from immediately fighting it.</summary>
    public void MimicNap(bool napping)
    {
        if (napping == _napping)
        {
            return;
        }
        _napping = napping;
        _clip.Play(napping ? "nap" : "idle");
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
        _spinT = MathF.Min(1f, _spinT + (dt / SpinSeconds));

        if (_emote is { } emote)
        {
            _emoteT += dt;
            if (_emoteT >= emote.Seconds)
            {
                _emote = null;
            }
        }

        if (!_napping && _sinceInteraction >= NapAfterSeconds)
        {
            _napping = true;
            _clip.Play("nap");
        }

        // The emote's Rate, clamped so no clip becomes a different animation.
        _clip.RateScale = _emote is { Morph: { } } && !ReduceMotion
            ? Math.Clamp(1f + CurrentMorph.Rate, 0.4f, 2.2f)
            : 1f;

        if (_blinkLidT >= 0f)
        {
            _blinkLidT += dt;
            if (_blinkLidT >= BlinkSeconds)
            {
                _blinkLidT = -1f;
            }
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
        // Wide-open blink frames flashing through a half-shut face read as waking up, not blinking.
        if (_blinkIn <= 0f && _clip.ClipName == "idle" && DrowsyState() is null)
        {
            // Sometimes twice. A double blink costs nothing to schedule and is the cheapest
            // variety a face can have; the short timer fires once the first blink has handed the
            // clip back to the idle, so the pair reads as one gesture rather than two events.
            _blinkIn = !_doubling && _rng.NextDouble() < DoubleBlinkChance
                ? DoubleBlinkGap
                : NextBlink();
            _doubling = _blinkIn == DoubleBlinkGap;

            if (SuppressEyeCellSwap)
            {
                // A drawn shell blinks with its lid, never the clip: the blink cells pin the
                // pose spline to rest, which snaps the body out of its breath.
                _blinkLidT = 0f;
            }
            else
            {
                _clip.Play("blink");
            }
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

    /// <summary>How close the pet is to its nap, as a lidded eye state, or null while it is
    /// properly awake. Public because on a drawn body the runtime applies it as a lid instead of
    /// swapping the cell: see <see cref="SuppressEyeCellSwap"/>.</summary>
    public string? DrowsyEye => DrowsyState();

    /// <summary>How far the lid is down for a blink, 0 to 1, and 0 when none is playing. Follows
    /// the authored blink clip's shape: down over the first forty percent, back up over the rest.</summary>
    public float BlinkLid
    {
        get
        {
            if (_blinkLidT < 0f)
            {
                return 0f;
            }

            var q = Math.Clamp(_blinkLidT / BlinkSeconds, 0f, 1f);
            return q < 0.4f ? Ease(q / 0.4f) : Ease(1f - ((q - 0.4f) / 0.6f));
        }
    }

    /// <summary>The authored blink's own length: seven frames at 20 fps.</summary>
    private const float BlinkSeconds = 0.35f;

    private float _blinkLidT = -1f;

    private static float Ease(float t) => t * t * (3f - (2f * t));

    /// <summary>Set by the runtime when the body draws from geometry rather than from a sheet.
    /// The eye-cell swap is right for a sheet but wrong for a drawn shell: the pose spline reads
    /// the pinned cell indices, so the swap freezes the creature at rest while the state holds.
    /// The runtime asks <see cref="DrowsyEye"/> and blends the lid into the face instead.</summary>
    public bool SuppressEyeCellSwap { get; set; }

    private string? DrowsyState()
    {
        if (_napping || _clip.ClipName != "idle")
        {
            return null;
        }
        var untilNap = NapAfterSeconds - _sinceInteraction;
        return untilNap <= 60f ? "heavy" : untilNap <= 150f ? "drowsy" : null;
    }

    /// <summary>Swaps the drawn cell for a rest-registered eye cell, compensating the running clip's baked
    /// breath. The breath preserves area and the head anchor measures it at twice the silhouette's squash,
    /// so the square root of the anchors' ratio puts the body back where the clip had it; without it the
    /// swap jumps the silhouette a twentieth of a cell mid-breath.</summary>
    private void SubstituteEyeCell(int cell, ref PetPose pose)
    {
        if (pose.CellIndex == cell)
        {
            return;
        }
        var running = _manifest.Cell - _manifest.AnchorForCell("head", pose.CellIndex).Y;
        var substituted = _manifest.Cell - _manifest.AnchorForCell("head", cell).Y;
        if (running > 0f && substituted > 0f)
        {
            var squash = MathF.Sqrt(running / substituted);
            pose.Scale.X /= squash;
            pose.Scale.Y *= squash;
        }
        pose.CellIndex = cell;

        // A substituted eye cell is a STATE the pet is in, not a step of a clip, so it has no
        // "next" to travel toward. Left pointing at the running clip's next cell, a smoothed
        // anchor would read between a drowsy face and whatever frame the idle was heading for.
        pose.NextCellIndex = cell;
        pose.PrevCellIndex = cell;
        pose.AfterCellIndex = cell;
        pose.FramePhase = 0f;
    }

    public PetPose GetPose()
    {
        var pose = new PetPose
        {
            CellIndex = _clip.CurrentCell,
            Scale = Vector2.One,
            Offset = Vector2.Zero,
            FlipX = false,
            NextCellIndex = _clip.CellAtOffset(1),
            PrevCellIndex = _clip.CellAtOffset(-1),
            AfterCellIndex = _clip.CellAtOffset(2),
            FramePhase = _clip.FramePhase,
        };
        // Before the reduce-motion return on purpose: a lidded eye is a state, not a movement.
        if (!SuppressEyeCellSwap && DrowsyState() is { } drowsy && _manifest.EyeCellFor(drowsy) is { } drowsyCell)
        {
            SubstituteEyeCell(drowsyCell, ref pose);
        }

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

        if (_spinT < 1f)
        {
            // The floor keeps the sprite from vanishing outright at the edge-on frame.
            var turn = MathF.Cos(2f * MathF.PI * _spinT);
            pose.Scale.X *= MathF.Max(0.05f, MathF.Abs(turn));
            pose.FlipX ^= turn < 0f;
        }

        if (_emote is { } emote)
        {
            var delta = emote.Pose(Math.Clamp(_emoteT / emote.Seconds, 0f, 1f));
            pose.Offset += delta.Offset * _emoteAmplitude;
            pose.Scale *= Vector2.One + ((delta.ScaleMul - Vector2.One) * _emoteAmplitude);
            pose.FlipX ^= delta.FlipX;
        }

        return pose;
    }

    private float NextBlink() =>
        BlinkMinSeconds + ((float)_rng.NextDouble() * (BlinkMaxSeconds - BlinkMinSeconds));

    private float NextVariant() =>
        VariantMinSeconds + ((float)_rng.NextDouble() * (VariantMaxSeconds - VariantMinSeconds));
}
