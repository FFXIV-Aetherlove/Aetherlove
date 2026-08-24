using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AetherOS.Apps.Aetherling.Engine;

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

/// <summary>One learnable emote: a named, fixed-length choreography of code-side motion laid over the
/// running clip, plus its mouth track. No sheet, no cells, no VRAM; the entire asset is the function.
/// The prototype's hand and eye tracks are deliberately absent: this engine has neither drawn limbs nor
/// eye cells, and a track the pet cannot perform is dropped, not refused.</summary>
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
    /// <summary>Every learnable, in display order. MUST cover the server catalog's EmoteKeys; a key
    /// without a choreography learns silently and performs nothing, which reads as a bug.</summary>
    public static readonly IReadOnlyList<EmoteDef> All =
    [
        new EmoteDef
        {
            // The greeting: a lift and a lean into the side that would be waving. The prototype's hand
            // fan is absent by design (no limbs here); the body carries the whole hello.
            Key = "wave", Name = "Wave", GameEmote = "/wave", Seconds = 1.7f, Pose = WavePose,
            Mouth = [new MouthKey(0f, "grin", 0.12f), new MouthKey(1.25f, "smile", 0.28f)],
        },
        new EmoteDef
        {
            // A small hop, then two big hops each carrying a full coin-turn, opposite ways, and a landing
            // squish. The mouth laughs at each apex and grins between them.
            Key = "cheer", Name = "Cheer", GameEmote = "/cheer", Seconds = 3.0f, Pose = CheerPose,
            Mouth =
            [
                new MouthKey(0f, "grin", 0.1f),
                new MouthKey(0.72f, "laugh", 0.14f),
                new MouthKey(1.45f, "grin", 0.12f),
                new MouthKey(1.65f, "laugh", 0.14f),
                new MouthKey(2.55f, "smile", 0.3f),
            ],
        },
        new EmoteDef
        {
            // Respectfully flat through the fold; the smile returns with the rise.
            Key = "bow", Name = "Bow", GameEmote = "/bow", Seconds = 1.7f, Pose = BowPose,
            Mouth = [new MouthKey(0f, "flat", 0.2f), new MouthKey(1.35f, "smile", 0.3f)],
        },
        new EmoteDef
        {
            // Four "ha" beats, each a snap-squash with a little lift, decaying as the fit passes.
            Key = "laugh", Name = "Laugh", GameEmote = "/laugh", Seconds = 2.2f, Pose = LaughPose,
            Mouth =
            [
                new MouthKey(0f, "laugh", 0.08f),
                new MouthKey(0.48f, "grin", 0.08f), new MouthKey(0.70f, "laugh", 0.08f),
                new MouthKey(1.03f, "grin", 0.08f), new MouthKey(1.25f, "laugh", 0.08f),
                new MouthKey(1.90f, "smile", 0.25f),
            ],
        },
        new EmoteDef
        {
            // A slow lean into the pondering side, held long, with a small thinking bob and no
            // resolution beat, because thinking does not have one.
            Key = "think", Name = "Think", GameEmote = "/think", Seconds = 2.4f, Pose = ThinkPose,
            Mouth =
            [
                new MouthKey(0f, "flat", 0.2f),
                new MouthKey(0.5f, "hmm", 0.25f),
                new MouthKey(2.0f, "smile", 0.3f),
            ],
        },
        new EmoteDef
        {
            // Settles low and breathes, a slow 0.55 Hz swell held through the middle: the one emote whose
            // whole job is to look like nothing is happening, which is why the breath has to be visible.
            Key = "doze", Name = "Doze", GameEmote = "/doze", Seconds = 3.2f, Pose = DozePose,
            Mouth = [new MouthKey(0f, "sleepy", 0.4f)],
        },
        .. EmoteChoreographiesBatch2.All,
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

    private static float Arc(float q) => MathF.Sin(MathF.PI * Math.Clamp(q, 0f, 1f));

    private static float Frac(float v) => v - MathF.Floor(v);

    private static float SmoothStep(float edge0, float edge1, float t)
    {
        var x = Math.Clamp((t - edge0) / (edge1 - edge0), 0f, 1f);
        return x * x * (3f - (2f * x));
    }
}
