using System;
using System.Numerics;

namespace AetherOS.Apps.Aetherling.Engine;

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
            _blinkIn = NextBlink();
            _clip.Play("blink");
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

    /// <summary>How close it is to its nap, as a lid state: the last stretch before the inactivity nap
    /// wears drowsy, then heavy, so it visibly gets sleepy rather than dropping off a cliff. Null while
    /// it is properly awake, or already napping where the nap cells close their own eyes.</summary>
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
    }

    public PetPose GetPose()
    {
        var pose = new PetPose
        {
            CellIndex = _clip.CurrentCell,
            Scale = Vector2.One,
            Offset = Vector2.Zero,
            FlipX = false,
        };
        // Before the reduce-motion return on purpose: a lidded eye is a state, not a movement.
        if (DrowsyState() is { } drowsy && _manifest.EyeCellFor(drowsy) is { } drowsyCell)
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
