using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AetherOS.PetKit.Engine;

/// <summary>What an emote choreography does to this frame's pose, composed over whatever clip is playing.
/// Offsets add in cell-local 256-space pixels; negative Y is a rise, and nothing ever sinks through the
/// ground line, because a squat is a squash and the renderer anchors scale at the bottom-centre. Scales
/// multiply and the flip XORs, so a choreography composes with a hop already in the air.</summary>
public struct EmotePoseDelta
{
    public Vector2 Offset;

    public Vector2 ScaleMul;

    public bool FlipX;

    public static EmotePoseDelta None => new() { ScaleMul = Vector2.One };
}

/// <summary>Where an emote puts the hands this frame: the limb half of a choreography, built in
/// the same shape as <see cref="EmotePoseDelta"/> and composed the same way. Offsets are
/// 256-space and add to each hand's REST pose rather than replacing it, so a track that says
/// nothing about a hand leaves that arm hanging exactly as it hangs while the pet idles.
///
/// <para>Both hands are authored OUTBOARD: +X points away from the body on whichever hand it is,
/// so a symmetric gesture is the same numbers twice and the renderer mirrors the left one. Every
/// raise carries an outboard lean because a hand brought straight up half-merges with the head.
/// Tilt leans whatever the hand holds about its grip point, radians, outboard-positive.</para></summary>
public struct HandsDelta
{
    /// <summary>The clip envelope, 256-space: how far a track may take a hand from its rest
    /// pose, in any direction. Not a guideline: <c>HandFx</c> clamps to it,
    /// because this is the figure the stage and the floating window reserve for hand clips.
    /// Sized to contain the wave (raise 29 up, swing 17 out).</summary>
    public const float MaxReach256 = 30f;

    /// <summary>Tilt ceiling, radians. Capped low on purpose: tilt is the one part of a clip
    /// whose reach grows with the held item, and 8 degrees keeps the worst tip swing inside the
    /// envelope above.</summary>
    public const float MaxTilt = 8f * MathF.PI / 180f;

    public Vector2 Right;

    public Vector2 Left;

    public float RightTilt;

    public float LeftTilt;

    /// <summary>Both arms at rest: what a null track means, frame by frame.</summary>
    public static HandsDelta None => default;

    /// <summary>The symmetric gesture: the same outboard offset (and tilt) on both hands,
    /// which mirrors into a matched pair.</summary>
    public static HandsDelta Mirrored(Vector2 offset, float tilt = 0f) =>
        new() { Right = offset, Left = offset, RightTilt = tilt, LeftTilt = tilt };

    /// <summary>Both hands travelling the same way in WORLD space: a pair swinging left rather
    /// than a pair opening outwards. The sign flips on the left because outboard space points
    /// the other way there.</summary>
    public static HandsDelta Swung(float worldX, float y = 0f) =>
        new() { Right = new Vector2(worldX, y), Left = new Vector2(-worldX, y) };
}

/// <summary>What an emote does to the creature itself, as opposed to <see cref="EmotePoseDelta"/>,
/// which slides and squeezes the finished drawing. A morph is written into the shell's own pose
/// channels before it draws, so a bow folds instead of leaning. Fields are semantic dials:
/// <c>LineShell.WithEmote</c> maps each onto whichever channels a given shell reads, a dial no
/// shell reads costs nothing, and a sheet-drawn shell (the hatchlings, the ceremony core) has no
/// channels at all, so its morph is dropped.</summary>
public struct EmoteMorph
{
    /// <summary>Wider and shorter, the posture dial. Positive squashes, negative stretches.
    /// The one dial every shell answers.</summary>
    public float Squash;

    /// <summary>Vertical travel of the body within its own footprint, authoring pixels.
    /// Positive is DOWN, matching <c>Ch.Dy</c>. Unlike <see cref="EmotePoseDelta.Offset"/>'s Y,
    /// the drawing itself does not move: the creature slumps or draws itself up in place.</summary>
    public float Lift;

    /// <summary>How hard the body was struck, on the shells that ring: the Serpent's rattle,
    /// the Chime's swing.</summary>
    public float Tremble;

    /// <summary>How much brighter than rest, additive on top of the multiplicative 1, on the
    /// shells that are lit at all.</summary>
    public float Glow;

    /// <summary>Motion-ghost strength, for the beats that are too fast to follow.</summary>
    public float Blur;

    /// <summary>Speed multiplier on the ambient beat: 0.5 is half again as fast. Lands on
    /// <c>Ch.Spin</c>, which <c>LineMotion.Advance</c> integrates rather than multiplying into
    /// the phase; scaling a free-running phase does not slow it, it teleports it.</summary>
    public float Rate;

    /// <summary>Tipping sideways, -1 to 1, positive toward the main hand. <c>WithEmote</c>
    /// converts to each shell's own lean units.</summary>
    public float Tip;

    /// <summary>How far the sharp bits stand out, as a multiplier on whatever the clip already
    /// has: the Puffer's spines and the Grumble's bolt. Multiplicative because the Puffer
    /// authors pixels and the Grumble authors a factor; an additive span cannot serve both.</summary>
    public float Bristle;

    /// <summary>How much the soft parts move, as a multiplier: the Pennant's travelling wave and
    /// the Wisp's tuft sway. Multiplicative for the same reason <see cref="Bristle"/> is.</summary>
    public float Ripple;

    /// <summary>Going red, 0 to 1, added to whatever the pose table already has. Not a pose
    /// channel: the runtime adds it to the shell's own blush, so <c>LineShell.WithEmote</c>
    /// never sees it.</summary>
    public float Blush;

    /// <summary>Pulling into yourself: the Nautilus retreating into its shell, the Muffle's
    /// head settling into its base.</summary>
    public float Withdraw;

    /// <summary>Nothing happening, which is what a null track means every frame.</summary>
    public static EmoteMorph None => default;

    /// <summary>Scales every dial, for an envelope applied to a whole track at once.</summary>
    public static EmoteMorph operator *(EmoteMorph m, float k) => new()
    {
        Squash = m.Squash * k,
        Lift = m.Lift * k,
        Blush = m.Blush * k,
        Rate = m.Rate * k,
        Tip = m.Tip * k,
        Bristle = m.Bristle * k,
        Ripple = m.Ripple * k,
        Tremble = m.Tremble * k,
        Glow = m.Glow * k,
        Blur = m.Blur * k,
        Withdraw = m.Withdraw * k,
    };
}

/// <summary>The curve vocabulary every choreography is written in, shared so the launch set and
/// the batches use one copy of each easing.</summary>
public static class EmoteCurves
{
    /// <summary>Nought to one and back, as a half sine: the shape of nearly every beat.</summary>
    public static float Arc(float q) => MathF.Sin(MathF.PI * Math.Clamp(q, 0f, 1f));

    /// <summary>The fractional part, for repeating a beat n times across one clip.</summary>
    public static float Frac(float v) => v - MathF.Floor(v);

    /// <summary>Smoothstep between two edges.</summary>
    public static float SS(float e0, float e1, float t)
    {
        var x = Math.Clamp((t - e0) / (e1 - e0), 0f, 1f);
        return x * x * (3f - (2f * x));
    }

    /// <summary>Ease in over [0,a], hold, ease out over [b,1]: the standard clip envelope.</summary>
    public static float Hold(float a, float b, float p) => SS(0f, a, p) * (1f - SS(b, 1f, p));

    /// <summary>A morph, by name, so a track reads as the dials it spends and not as six
    /// positional floats most of which are zero.</summary>
    public static EmoteMorph M(
        float squash = 0f, float lift = 0f, float tremble = 0f,
        float glow = 0f, float blur = 0f, float withdraw = 0f, float blush = 0f,
        float rate = 0f, float tip = 0f, float bristle = 0f, float ripple = 0f) =>
        new()
        {
            Squash = squash, Lift = lift, Tremble = tremble,
            Glow = glow, Blur = blur, Withdraw = withdraw, Blush = blush,
            Rate = rate, Tip = tip, Bristle = bristle, Ripple = ripple,
        };
}

/// <summary>One learnable emote: a named, fixed-length choreography of code-side motion laid over the
/// running clip, plus its mouth, eye, hand and morph tracks. No sheet, no cells, no VRAM; the entire
/// asset is the function.</summary>
public class EmoteDef
{
    /// <summary>Stable persistence key, and the server catalog's key.</summary>
    public string Key = string.Empty;

    /// <summary>Display name, literal English like <see cref="ReactionDef"/> names.</summary>
    public string Name = string.Empty;

    /// <summary>The in-game emote this answers, purely descriptive.</summary>
    public string GameEmote = string.Empty;

    public float Seconds = 1f;

    /// <summary>Pose delta at progress p in [0,1]. Pure function of p: deterministic and scrub-friendly.</summary>
    public Func<float, EmotePoseDelta> Pose = _ => EmotePoseDelta.None;

    /// <summary>The emote's mouth track, played on the runtime's <see cref="MouthController"/> over the
    /// choreography. Empty = the base mouth carries on.</summary>
    public MouthKey[] Mouth = [];

    /// <summary>The emote's eye track: the mouth track's twin, played on the runtime's
    /// <see cref="EyeController"/> and blended OVER whatever the running clip's eye is doing.
    /// Empty = the eyes carry on exactly as the clip draws them.</summary>
    public EyeKey[] Eyes = [];

    /// <summary>The emote's hand track: the limbs' pose at progress p, the same pure function of
    /// p the body track is. Null = the arms hang at rest through it, which is the honest answer
    /// for the emotes that are not about a limb. Always optional at play time, never at call
    /// time: the hands half is simply ignored when there are no hands to move.</summary>
    public Func<float, HandsDelta>? Hands;

    /// <summary>The emote's morph track: what the creature does, as opposed to what the frame
    /// around it does; the same pure function of p every other track is. Null = the creature
    /// holds whatever shape the running clip has it in. Optional at play time, never at call
    /// time: a sheet-drawn shell has no channels to write, so its morph is dropped and the rest
    /// of the emote plays. The morph is written into the pose the clip asked for, before the
    /// shell's material springs toward it, so it composes with the clip rather than replacing
    /// it.</summary>
    public Func<float, EmoteMorph>? Morph;

    /// <summary>What the ears and the tail are asked to feel for the duration, in
    /// <see cref="PartMoods"/>'s own vocabulary: "happy", "content", "curious", "alert",
    /// "sleepy". Empty = no opinion, and the parts carry on reading what the pet is doing.
    /// Worn rather than grown, so this reaches every shell the moment the player equips ears
    /// or a tail, and nothing at all before that.</summary>
    public string Parts = string.Empty;

    /// <summary>A glyph shown as the emote plays, from <c>Glyphs</c>'s own 28 shapes. Empty = the
    /// creature says nothing above its head. Offered rather than played: <c>ShowGlyph</c> keeps
    /// its own gates (the cooldown, a Reaction owning the moment, a shell with no head pin), and
    /// a declined glyph is a normal, silent outcome.</summary>
    public string Glyph = string.Empty;

    /// <summary>Put the held items down for the performance: nobody breakdances holding a greatsword.
    /// Nothing is unequipped and nothing is saved, so a crash mid-emote never leaves a pet disarmed.</summary>
    public bool StowArms;
}

/// <summary>The launch set the creature can learn by watching (the prototype's Taught-by-Watching pilot):
/// six choreographies whose curves stay inside the hop's established excursions, so every surface's
/// worst-case footprint already contains all of them. Growth is one entry here plus one server catalog
/// key.</summary>
public static class EmoteChoreographies
{
    private static float SS(float e0, float e1, float t) => EmoteCurves.SS(e0, e1, t);

    private static float Hold(float a, float b, float p) => EmoteCurves.Hold(a, b, p);

    private static EmoteMorph M(
        float squash = 0f, float lift = 0f, float tremble = 0f,
        float glow = 0f, float blur = 0f, float withdraw = 0f, float blush = 0f,
        float rate = 0f, float tip = 0f, float bristle = 0f, float ripple = 0f) =>
        EmoteCurves.M(squash, lift, tremble, glow, blur, withdraw, blush, rate, tip, bristle, ripple);

    /// <summary>Every learnable, in display order. MUST cover the server catalog's EmoteKeys; a key
    /// without a choreography learns silently and performs nothing, which reads as a bug.</summary>
    public static readonly IReadOnlyList<EmoteDef> All =
    [
        new EmoteDef
        {
            // The greeting: a lift and a lean into the side that would be waving. The prototype's hand
            // fan is absent by design (no limbs here); the body carries the whole hello.
            Key = "wave", Name = "Wave", GameEmote = "/wave", Seconds = WaveSeconds, Pose = WavePose,
            Hands = WaveHands,
            Mouth = [new MouthKey(0f, "grin", 0.12f), new MouthKey(1.25f, "smile", 0.28f)],

            // The trimmings answer the hand: a wave with the tuft and tassels dead still
            // is an arm moving on a statue.
            Morph = p =>
            {
                var f = Hold(0.15f, 0.85f, p);
                return M(ripple: 0.45f * f, tip: 0.18f * f, glow: 0.10f * f);
            },

            // Arcs for the hello.
            Eyes = [new EyeKey(0.1f, "happy", 0.15f), new EyeKey(1.35f, "open", 0.2f)],

            Parts = "happy",
        },
        new EmoteDef
        {
            // A small hop, then two big hops each carrying a full coin-turn, opposite ways, and a landing
            // squish. The mouth laughs at each apex and grins between them.
            Key = "cheer", Name = "Cheer", GameEmote = "/cheer", Seconds = 3.0f, Pose = CheerPose,
            Hands = CheerHands,
            Mouth =
            [
                new MouthKey(0f, "grin", 0.1f),
                new MouthKey(0.72f, "laugh", 0.14f),
                new MouthKey(1.45f, "grin", 0.12f),
                new MouthKey(1.65f, "laugh", 0.14f),
                new MouthKey(2.55f, "smile", 0.3f),
            ],

            // Everything up: faster, lighter, brighter, the soft parts thrown around too.
            Morph = p =>
            {
                var w = Hold(0.08f, 0.88f, p);
                return M(rate: 0.55f * w, ripple: 0.7f * w, glow: 0.22f * w, lift: -1.4f * w, blush: 0.2f * w);
            },

            // Arcs through the whole routine, open on the last landing.
            Eyes = [new EyeKey(0.1f, "happy", 0.15f), new EyeKey(2.7f, "open", 0.25f)],

            Parts = "happy",
            Glyph = "burst",
        },
        new EmoteDef
        {
            // Respectfully flat through the fold; the smile returns with the rise.
            Key = "bow", Name = "Bow", GameEmote = "/bow", Seconds = 1.7f, Pose = BowPose,
            Hands = BowHands,
            Mouth = [new MouthKey(0f, "flat", 0.2f), new MouthKey(1.35f, "smile", 0.3f)],

            // The fold, on the creature: it gets shorter and slower and comes back up.
            Morph = p =>
            {
                var f = Hold(0.2f, 0.78f, p);
                return M(lift: 2.8f * f, squash: 0.018f * f, rate: -0.25f * f, ripple: 0.2f * f);
            },

            // Lowered through the fold, up with the smile.
            Eyes = [new EyeKey(0.15f, "downcast", 0.2f), new EyeKey(1.35f, "open", 0.25f)],

            Parts = "content",
        },
        new EmoteDef
        {
            // Four "ha" beats, each a snap-squash with a little lift, decaying as the fit passes.
            Key = "laugh", Name = "Laugh", GameEmote = "/laugh", Seconds = 2.2f, Pose = LaughPose,
            Hands = LaughHands,
            Mouth =
            [
                new MouthKey(0f, "laugh", 0.08f),
                new MouthKey(0.48f, "grin", 0.08f), new MouthKey(0.70f, "laugh", 0.08f),
                new MouthKey(1.03f, "grin", 0.08f), new MouthKey(1.25f, "laugh", 0.08f),
                new MouthKey(1.90f, "smile", 0.25f),
            ],

            // Arcs, all the way through. A laughing mouth under open eyes reads as a shout.
            Eyes = [new EyeKey(0f, "happy", 0.08f), new EyeKey(1.9f, "open", 0.2f)],

            // Pulses the creature on the same "ha" beats the body track pulses the frame on.
            Morph = p =>
            {
                var w = Hold(0.1f, 0.88f, p);
                return M(squash: 0.016f * Arc(Frac(p * 6f)) * w, glow: 0.12f * w, blush: 0.35f * w, ripple: 0.55f * w, rate: 0.30f * w);
            },

            Parts = "happy",
        },
        new EmoteDef
        {
            // A slow lean into the pondering side, held long, with a small thinking bob and no
            // resolution beat, because thinking does not have one.
            Key = "think", Name = "Think", GameEmote = "/think", Seconds = 2.4f, Pose = ThinkPose,
            Hands = ThinkHands,
            Mouth =
            [
                new MouthKey(0f, "flat", 0.2f),
                new MouthKey(0.5f, "hmm", 0.25f),
                new MouthKey(2.0f, "smile", 0.3f),
            ],

            // Narrowed while it works, open when it has it.
            Eyes =
            [
                new EyeKey(0.2f, "threeq", 0.2f),
                new EyeKey(0.5f, "quarter", 0.25f),
                new EyeKey(2f, "open", 0.2f),
            ],

            // Nothing moves much. The glow rises as it works and lands with the idea.
            Morph = p =>
            {
                var work = Hold(0.2f, 0.82f, p);
                return M(glow: 0.10f * work, squash: 0.008f * work, rate: -0.15f * work);
            },

            Parts = "curious",
            Glyph = "query",
        },
        new EmoteDef
        {
            // Settles low and breathes, a slow 0.55 Hz swell held through the middle: the one emote whose
            // whole job is to look like nothing is happening, which is why the breath has to be visible.
            Key = "doze", Name = "Doze", GameEmote = "/doze", Seconds = 3.2f, Pose = DozePose,
            Hands = DozeHands,
            Mouth = [new MouthKey(0f, "sleepy", 0.4f)],

            // The slide into sleep and back out; waking is slower than dropping off.
            Eyes =
            [
                new EyeKey(0.45f, "drowsy", 0.35f),
                new EyeKey(1.05f, "heavy", 0.4f),
                new EyeKey(1.6f, "shut", 0.35f),
                new EyeKey(2.85f, "heavy", 0.4f),
                new EyeKey(3.05f, "drowsy", 0.3f),
            ],

            // Settling, and the light going down with it.
            Morph = p =>
            {
                var f = Hold(0.3f, 0.85f, p);
                return M(lift: 3.0f * f, glow: -0.15f * f, withdraw: 0.25f * f, squash: 0.012f * f, rate: -0.45f * f, ripple: -0.3f * f);
            },

            Parts = "sleepy",
        },
        .. EmoteChoreographiesBatch2.All,
        .. EmoteChoreographiesExtra.All,
    ];

    public static EmoteDef? Find(string key) =>
        key.Length == 0 ? null : All.FirstOrDefault(e => e.Key == key);

    private static EmotePoseDelta WavePose(float p)
    {
        var d = EmotePoseDelta.None;
        var lift = Arc(Math.Clamp(p / 0.34f, 0f, 1f));
        d.Offset.Y = -5f * lift;
        d.ScaleMul = new Vector2(1f - (0.02f * lift), 1f + (0.03f * lift));
        d.Offset.X = 3f * SmoothStep(0.12f, 0.34f, p) * (1f - SmoothStep(0.78f, 1f, p));
        return d;
    }

    private static EmotePoseDelta CheerPose(float p)
    {
        var d = EmotePoseDelta.None;
        if (p < 0.18f)
        {
            d.Offset.Y = -14f * Arc(p / 0.18f);
        }
        else if (p is >= 0.24f and < 0.46f)
        {
            var q = (p - 0.24f) / 0.22f;
            d.Offset.Y = -32f * Arc(q);
            var turn = MathF.Cos(2f * MathF.PI * q);
            d.ScaleMul.X = MathF.Max(0.05f, MathF.Abs(turn));
            d.FlipX = turn < 0f;
        }
        else if (p is >= 0.54f and < 0.80f)
        {
            var q = (p - 0.54f) / 0.26f;
            d.Offset.Y = -38f * Arc(q);
            var turn = MathF.Cos(2f * MathF.PI * q);
            d.ScaleMul.X = MathF.Max(0.05f, MathF.Abs(turn));
            // The mirror of hop one: it comes round the other way.
            d.FlipX = turn >= 0f;
        }
        else if (p >= 0.92f)
        {
            var s = Arc((p - 0.92f) / 0.08f);
            d.ScaleMul = new Vector2(1f + (0.12f * s), 1f - (0.14f * s));
        }

        return d;
    }

    private static EmotePoseDelta BowPose(float p)
    {
        var d = EmotePoseDelta.None;
        if (p < 0.14f)
        {
            d.Offset.Y = -5f * Arc(p / 0.14f);
            return d;
        }

        var fold = SmoothStep(0.14f, 0.38f, p) * (1f - SmoothStep(0.72f, 0.95f, p));
        d.ScaleMul = new Vector2(1f + (0.08f * fold), 1f - (0.22f * fold));
        if (p >= 0.95f)
        {
            d.ScaleMul.Y += 0.03f * Arc((p - 0.95f) / 0.05f);
        }

        return d;
    }

    private static EmotePoseDelta LaughPose(float p)
    {
        var d = EmotePoseDelta.None;
        if (p >= 0.88f)
        {
            return d;
        }

        var u = p / 0.88f;
        var r = Arc(Frac(u * 4f));
        var decay = 1f - (0.35f * u);
        d.ScaleMul = new Vector2(1f + (0.10f * r * decay), 1f - (0.14f * r * decay));
        d.Offset.Y = -7f * r * decay;
        return d;
    }

    private static EmotePoseDelta ThinkPose(float p)
    {
        var d = EmotePoseDelta.None;
        var lean = SmoothStep(0f, 0.22f, p) * (1f - SmoothStep(0.82f, 1f, p));
        d.Offset.X = 11f * lean;
        d.Offset.Y = (-4f + (2f * MathF.Sin(2f * MathF.PI * 1.1f * p))) * lean;
        d.ScaleMul = new Vector2(1f - (0.02f * lean), 1f + (0.03f * lean));
        return d;
    }

    private static EmotePoseDelta DozePose(float p)
    {
        var d = EmotePoseDelta.None;
        var settle = SmoothStep(0f, 0.25f, p) * (1f - SmoothStep(0.86f, 1f, p));
        var breath = MathF.Sin(2f * MathF.PI * 0.55f * p);
        d.ScaleMul = new Vector2(
            1f + ((0.07f + (0.02f * breath)) * settle),
            1f - ((0.09f + (0.025f * breath)) * settle));
        return d;
    }

    // ------------------------------------------------------------------- the hand tracks
    // Every track obeys three rules. (1) The envelope: nothing leaves HandsDelta.MaxReach256
    // of rest, because that is what the stage reserves. (2) Outboard bias on every raise: an
    // inboard hand disappears against the body. (3) Nothing is ABOUT the hands except where
    // the emote already was.

    public const float WaveSeconds = 1.7f;

    private const float WaveRaiseSeconds = 0.30f;
    private const float WaveHz = 2.4f;
    private const float WaveFanSeconds = 2.5f / WaveHz;
    private const float WaveReturnSeconds = 0.36f;
    private static readonly Vector2 WaveRaised = new(10f, -29f);
    private const float WaveSwing = 7f;
    private const float WaveArcDip = 2.2f;

    /// <summary>The pilot's clip as an ordinary hand track: raise up-and-outboard, fan through
    /// two and a half swings, ease home. The off hand rests through it; one arm waves, which is
    /// what waving is.</summary>
    private static HandsDelta WaveHands(float p)
    {
        var t = p * WaveSeconds;
        if (t < WaveRaiseSeconds)
        {
            var ease = 1f - MathF.Pow(1f - (t / WaveRaiseSeconds), 3f);
            return new HandsDelta { Right = WaveRaised * ease };
        }

        if (t < WaveRaiseSeconds + WaveFanSeconds)
        {
            var phi = MathF.Sin(MathF.Tau * WaveHz * (t - WaveRaiseSeconds));
            return new HandsDelta
            {
                Right = new Vector2(
                    WaveRaised.X + (WaveSwing * phi),
                    WaveRaised.Y + (WaveArcDip * phi * phi)),
                RightTilt = HandsDelta.MaxTilt * phi,
            };
        }

        // Smoothstep home. The swing ends at phi = 0 (the half-count), so this phase starts
        // exactly at the raised point with zero tilt: no seam between the two.
        var q = Math.Clamp((t - WaveRaiseSeconds - WaveFanSeconds) / WaveReturnSeconds, 0f, 1f);
        return new HandsDelta { Right = WaveRaised * (1f - (q * q * (3f - (2f * q)))) };
    }

    /// <summary>Both arms thrown up at every hop and half-raised for the little one, with the
    /// held item leaning outboard at the top. The turns happen underneath: a hand rides the pin,
    /// the pin rides the flip, so the arms come round with the body for free.</summary>
    private static HandsDelta CheerHands(float p)
    {
        var raise =
            p < 0.20f ? 0.45f * Arc(p / 0.20f)
            : p is >= 0.22f and < 0.48f ? Arc((p - 0.22f) / 0.26f)
            : p is >= 0.52f and < 0.82f ? Arc((p - 0.52f) / 0.30f)
            : p >= 0.88f ? 0.5f * Arc((p - 0.88f) / 0.12f)
            : 0f;

        return HandsDelta.Mirrored(new Vector2(9f * raise, -26f * raise), HandsDelta.MaxTilt * raise);
    }

    /// <summary>The hands fall with the fold and the held item tips down with them: the courtly
    /// half of the bow, where the blade is presented rather than brandished.</summary>
    private static HandsDelta BowHands(float p)
    {
        var fold = SmoothStep(0.14f, 0.38f, p) * (1f - SmoothStep(0.72f, 0.95f, p));
        return HandsDelta.Mirrored(new Vector2(5f * fold, 9f * fold), -0.10f * fold);
    }

    /// <summary>Asymmetric on purpose: the main hand comes up on each "ha" while the off hand
    /// slaps down low. A laugh performed by two matched arms is a jumping jack.</summary>
    private static HandsDelta LaughHands(float p)
    {
        if (p >= 0.88f)
        {
            return HandsDelta.None;
        }

        var u = p / 0.88f;
        var r = Arc(Frac(u * 4f)) * (1f - (0.35f * u));
        return new HandsDelta
        {
            Right = new Vector2(7f * r, -12f * r),
            Left = new Vector2(5f * r, 6f * r),
            RightTilt = 0.10f * r,
        };
    }

    /// <summary>One hand up beside the head, held there, bobbing with the thought: the nearest a
    /// limbless-by-default wisp gets to a hand on the chin, and it has to stay outboard to be
    /// seen at all. No resolution beat: the hand comes down because the emote ends, not because
    /// it decided.</summary>
    private static HandsDelta ThinkHands(float p)
    {
        var raise = SmoothStep(0f, 0.28f, p) * (1f - SmoothStep(0.84f, 1f, p));
        var bob = 1.5f * MathF.Sin(2f * MathF.PI * 1.1f * p);
        return new HandsDelta
        {
            Right = new Vector2(10f * raise, (-21f + bob) * raise),
            RightTilt = -0.06f * raise,
        };
    }

    /// <summary>Hands hanging, drifting with the same 0.55 Hz breath the body swells on. The
    /// smallest track in the set, and the one that stops the doze reading as a paused frame: a
    /// sleeping thing still moves, just barely.</summary>
    private static HandsDelta DozeHands(float p)
    {
        var settle = SmoothStep(0f, 0.25f, p) * (1f - SmoothStep(0.86f, 1f, p));
        var breath = MathF.Sin(2f * MathF.PI * 0.55f * p);
        return HandsDelta.Mirrored(new Vector2(2f * settle, (5f + (1.5f * breath)) * settle));
    }

    // Local aliases so the tracks above read unchanged.
    private static float Arc(float q) => EmoteCurves.Arc(q);

    private static float Frac(float v) => EmoteCurves.Frac(v);

    private static float SmoothStep(float edge0, float edge1, float t) => EmoteCurves.SS(edge0, edge1, t);
}
