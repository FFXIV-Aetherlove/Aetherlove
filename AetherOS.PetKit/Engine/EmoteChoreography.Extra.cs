// Nine more choreographies: angry, doubt, happy, nod, shake, shocked, squats, sulk and yawn.
//
// OPTIONAL. EmoteChoreographies.All must cover the server catalog's EmoteKeys, so these nine only
// belong here once the catalog has rows for them; deleting this file and its one line in All
// leaves the other fifty exactly as they were. They are kept in a file of their own rather than
// merged into the two beside them so that either decision is a one-line change.
namespace AetherOS.PetKit.Engine;

using System;
using System.Collections.Generic;
using System.Numerics;

public static class EmoteChoreographiesExtra
{
    private static float Arc(float q) => EmoteCurves.Arc(q);

    private static float Frac(float v) => EmoteCurves.Frac(v);

    private static float SmoothStep(float e0, float e1, float t) => EmoteCurves.SS(e0, e1, t);

    private static float Hold(float a, float b, float p) => EmoteCurves.Hold(a, b, p);

    private static EmoteMorph M(
        float squash = 0f, float lift = 0f, float tremble = 0f,
        float glow = 0f, float blur = 0f, float withdraw = 0f, float blush = 0f,
        float rate = 0f, float tip = 0f, float bristle = 0f, float ripple = 0f) =>
        EmoteCurves.M(squash, lift, tremble, glow, blur, withdraw, blush, rate, tip, bristle, ripple);

    public static readonly IReadOnlyList<EmoteDef> All =
    [
        new EmoteDef
        {
            // A beaming yes: the ω smile through both dips. No hand track on purpose; a nod
            // is the whole body agreeing.
            Key = "nod", Name = "Nod", GameEmote = "/yes", Seconds = 1.3f, Pose = NodPose,
            Mouth = [new MouthKey(0f, "beam", 0.12f)],

            // Arcs for the whole yes. A nod under open eyes is a machine agreeing.
            Eyes = [new EyeKey(0f, "happy", 0.12f), new EyeKey(1.1f, "open", 0.2f)],

            // A small squash on each dip, on the pose track's own beats.
            Morph = p => M(squash: 0.014f * Arc(Frac(p * 2f)), ripple: 0.25f * Arc(Frac(p * 2f))),

            Parts = "happy",
        },
        new EmoteDef
        {
            // A firm no that relaxes as the swings die out.
            Key = "shake", Name = "Shake", GameEmote = "/no", Seconds = 1.1f, Pose = ShakePose,
            Hands = ShakeHands,
            Mouth = [new MouthKey(0f, "pout", 0.08f), new MouthKey(0.85f, "flat", 0.15f)],

            // Away and narrowed.
            Eyes = [new EyeKey(0.08f, "away", 0.12f), new EyeKey(0.9f, "open", 0.2f)],

            // A little tremble through the swings, dying with them.
            Morph = p => M(tremble: 0.30f * (1f - SmoothStep(0.55f, 1f, p)), tip: -0.3f * MathF.Sin(2f * MathF.PI * 4f * p) * (1f - SmoothStep(0.55f, 1f, p))),

            Parts = "alert",
        },
        new EmoteDef
        {
            // The mouth exhales an "o" at the bottom of every rep, and the open-joy finish
            // sells the "phew". Tools down: nobody does five squats holding a frypan.
            Key = "squats", Name = "Squats", GameEmote = "/squats", Seconds = 3.4f, Pose = SquatsPose,
            Hands = SquatsHands, StowArms = true,
            Mouth =
            [
                new MouthKey(0f, "flat", 0.1f),
                new MouthKey(0.31f, "o", 0.12f), new MouthKey(0.63f, "flat", 0.12f),
                new MouthKey(0.94f, "o", 0.12f), new MouthKey(1.25f, "flat", 0.12f),
                new MouthKey(1.56f, "o", 0.12f), new MouthKey(1.88f, "flat", 0.12f),
                new MouthKey(2.19f, "o", 0.12f), new MouthKey(2.50f, "flat", 0.12f),
                new MouthKey(2.81f, "o", 0.12f), new MouthKey(3.13f, "open joy", 0.15f),
            ],

            // Screwed up with the effort and open on the finish.
            Eyes = [new EyeKey(0.15f, "squint", 0.2f), new EyeKey(3.05f, "happy", 0.25f)],

            // Squashing into each rep and going red over the set.
            Morph = p =>
            {
                var f = Hold(0.1f, 0.92f, p);
                return M(squash: 0.020f * Arc(Frac(p * 5f)) * f, blush: 0.5f * f, tremble: 0.2f * f, rate: 0.25f * f, bristle: 0.15f * f);
            },

            Parts = "happy",
        },
        new EmoteDef
        {
            // Three soft hops and a landing squish, Cheer's gentler cousin: no turns, no
            // escalation, just uncomplicated bounce. The ω beam holds throughout.
            Key = "happy", Name = "Happy", GameEmote = "/happy", Seconds = 1.9f, Pose = HappyPose,
            Hands = HappyHands,
            Mouth =
            [
                new MouthKey(0f, "beam", 0.12f),
                new MouthKey(0.95f, "laugh", 0.15f),
                new MouthKey(1.55f, "beam", 0.2f),
            ],

            // Arcs throughout, which is what separates happy from merely bouncing.
            Eyes = [new EyeKey(0f, "happy", 0.12f), new EyeKey(1.62f, "open", 0.2f)],

            // Lit and light: a small glow and a lift off its own weight.
            Morph = p =>
            {
                var f = Hold(0.1f, 0.85f, p);
                return M(glow: 0.18f * f, lift: -1.2f * f, blush: 0.2f * f, rate: 0.35f * f, ripple: 0.5f * f);
            },

            Parts = "happy",
            Glyph = "burst",
        },
        new EmoteDef
        {
            // The snap: stretch tall and narrow in three frames flat, hang there, then
            // shudder back down. The gasp lands on frame one; a delayed gasp is a joke.
            Key = "shocked", Name = "Shocked", GameEmote = "/shocked", Seconds = 1.5f, Pose = ShockedPose,
            Hands = ShockedHands,
            Mouth =
            [
                new MouthKey(0f, "gasp", 0.05f),
                new MouthKey(0.85f, "eh", 0.18f),
                new MouthKey(1.2f, "flat", 0.2f),
            ],

            // Wide on the snap and nowhere near closed after it.
            Eyes = [new EyeKey(0f, "wide", 0.04f), new EyeKey(0.85f, "threeq", 0.18f), new EyeKey(1.2f, "open", 0.2f)],

            // The flinch: everything that can jolt does, and all of it decays fast.
            Morph = p =>
            {
                var snap = p < 0.08f ? p / 0.08f : 1f - SmoothStep(0.08f, 0.6f, p);
                return M(tremble: 0.7f * snap, blur: 0.4f * snap, withdraw: 0.35f * snap, bristle: 0.6f * snap, rate: 0.3f * snap);
            },

            Parts = "alert",
            Glyph = "bang",
        },
        new EmoteDef
        {
            // Three hard stomps: sharp attack, slower release, a lateral jolt alternating
            // with each. The frown never lets up until it is over.
            Key = "angry", Name = "Angry", GameEmote = "/angry", Seconds = 2.0f, Pose = AngryPose,
            Hands = AngryHands,
            Mouth = [new MouthKey(0f, "frown", 0.08f), new MouthKey(1.75f, "pout", 0.2f)],

            // Narrowed, not lowered: anger looks at the thing it is angry about.
            Eyes = [new EyeKey(0.06f, "squint", 0.12f), new EyeKey(1.75f, "open", 0.25f)],

            // Heat: red, lit, and shaking with the stomps rather than only moving with them.
            Morph = p =>
            {
                var f = Hold(0.08f, 0.88f, p);
                return M(tremble: 0.55f * f, glow: 0.35f * f, blush: 0.5f * f, squash: 0.014f * f, bristle: 0.5f * f, tip: 0.15f * f);
            },

            Parts = "alert",
            Glyph = "flame",
        },
        new EmoteDef
        {
            // Leans away and stays leaning, swaying once while it decides it isn't
            // convinced. The smirk is the whole argument.
            Key = "doubt", Name = "Doubt", GameEmote = "/doubt", Seconds = 1.8f, Pose = DoubtPose,
            Hands = DoubtHands,
            Mouth =
            [
                new MouthKey(0f, "flat", 0.15f),
                new MouthKey(0.32f, "hmm", 0.22f),
                new MouthKey(1.25f, "pout", 0.22f),
            ],

            // Half lidded and looking away, which is the whole of an unconvinced face.
            Eyes = [new EyeKey(0.2f, "away", 0.18f), new EyeKey(0.5f, "half", 0.25f), new EyeKey(1.45f, "open", 0.25f)],

            // Leaning back into itself a little, unconvinced in the body too.
            Morph = p => M(withdraw: 0.20f * Hold(0.2f, 0.85f, p), squash: 0.008f * Hold(0.2f, 0.85f, p)),

            Parts = "curious",
            Glyph = "query",
        },
        new EmoteDef
        {
            // The sigh lift, the collapse, the slow waver riding the hold, the reluctant rise.
            Key = "sulk", Name = "Sulk", GameEmote = "/sulk", Seconds = 3.0f, Pose = SulkPose,
            Hands = SulkHands,
            Mouth =
            [
                new MouthKey(0f, "smile", 0.2f),
                new MouthKey(0.32f, "quiver", 0.25f),
                new MouthKey(1.0f, "sad", 0.3f),
                new MouthKey(2.55f, "smile", 0.35f),
            ],

            // Down and staying down for most of it, up only as the smile returns.
            Eyes = [new EyeKey(0.3f, "downcast", 0.3f), new EyeKey(2.55f, "half", 0.35f), new EyeKey(2.85f, "open", 0.3f)],

            // The collapse, on the creature: smaller, heavier, the light going out of it.
            Morph = p =>
            {
                var f = Hold(0.15f, 0.85f, p);
                return M(lift: 3.4f * f, withdraw: 0.40f * f, glow: -0.16f * f, rate: -0.35f * f, tip: -0.2f * f);
            },

            Parts = "sleepy",
            Glyph = "cloud",
        },
        new EmoteDef
        {
            // Stretches tall and thin on the inhale, hangs at the top of the yawn, then
            // settles heavier than it started. The "ah" is held long enough to be a real
            // yawn rather than a gasp, and it lands in sleepy, not back at rest.
            Key = "yawn", Name = "Yawn", GameEmote = "/yawn", Seconds = 2.7f, Pose = YawnPose,
            Hands = YawnHands,
            Mouth =
            [
                new MouthKey(0f, "smile", 0.15f),
                new MouthKey(0.35f, "ah", 0.38f),
                new MouthKey(1.6f, "sleepy", 0.45f),
            ],

            // The eyes lead: shut a beat before the mouth reaches its "ah", held through the
            // hang, and the reopen climbs back slower than it fell, ending on one slow blink.
            Eyes =
            [
                new EyeKey(0.16f, "half", 0.08f),
                new EyeKey(0.30f, "shut", 0.06f),
                new EyeKey(1.86f, "quarter", 0.18f),
                new EyeKey(2.10f, "threeq", 0.16f),
                new EyeKey(2.32f, "shut", 0.07f),
                new EyeKey(2.50f, "open", 0.16f),
            ],

            // A yawn pulls UP and long before it drops, so the squash goes negative first.
            Morph = p =>
            {
                var stretch = Arc(SmoothStep(0f, 0.55f, p));
                var settle = Hold(0.6f, 0.9f, p);
                return M(squash: (-0.022f * stretch) + (0.014f * settle), lift: 2.6f * settle,
                    glow: -0.08f * settle, withdraw: 0.18f * settle, rate: -0.3f * settle, ripple: 0.35f * stretch);
            },

            Parts = "sleepy",
        },
    ];

    private static EmotePoseDelta NodPose(float p)
    {
        var d = EmotePoseDelta.None;
        var q = Frac(p * 2f); // two nods
        var f = SmoothStep(0f, 0.22f, q) * (1f - SmoothStep(0.42f, 0.72f, q));
        d.ScaleMul = new Vector2(1f + (0.11f * f), 1f - (0.20f * f));
        return d;
    }

    private static EmotePoseDelta ShakePose(float p)
    {
        var d = EmotePoseDelta.None;
        var s = MathF.Sin(2f * MathF.PI * 3f * p);
        var snap = MathF.Sign(s) * MathF.Sqrt(MathF.Abs(s));
        d.Offset.X = 17f * snap * (1f - SmoothStep(0.6f, 1f, p));
        return d;
    }

    private static HandsDelta ShakeHands(float p)
    {
        var s = MathF.Sin((2f * MathF.PI * 3f * p) - (MathF.PI * 0.5f));
        var fade = 1f - SmoothStep(0.6f, 1f, p);
        var d = HandsDelta.Swung(-7f * s * fade);
        var lift = -6f * (1f - SmoothStep(0.72f, 1f, p));
        d.Right.Y = lift;
        d.Left.Y = lift;
        return d;
    }

    private static EmotePoseDelta SquatsPose(float p)
    {
        var d = EmotePoseDelta.None;
        if (p < 0.92f)
        {
            var r = MathF.Min(1f, Arc(Frac(p / 0.92f * 5f)) * 1.45f);
            d.ScaleMul = new Vector2(1f + (0.20f * r), 1f - (0.28f * r));
        }
        else
        {
            d.Offset.Y = -9f * Arc((p - 0.92f) / 0.08f);
        }

        return d;
    }

    private static HandsDelta SquatsHands(float p)
    {
        if (p >= 0.92f)
        {
            // The "phew": the arms drop as the little hop lands.
            return HandsDelta.Mirrored(new Vector2(3f, 6f) * Arc((p - 0.92f) / 0.08f));
        }

        var r = MathF.Min(1f, Arc(Frac(p / 0.92f * 5f)) * 1.45f);
        return HandsDelta.Mirrored(new Vector2(8f * r, -13f * r));
    }

    private static EmotePoseDelta HappyPose(float p)
    {
        var d = EmotePoseDelta.None;
        if (p < 0.92f)
        {
            var r = Arc(Frac(p / 0.92f * 3f));
            d.Offset.Y = -16f * r;
            d.ScaleMul = new Vector2(1f - (0.03f * r), 1f + (0.05f * r));
        }
        else
        {
            var s = Arc((p - 0.92f) / 0.08f);
            d.ScaleMul = new Vector2(1f + (0.08f * s), 1f - (0.10f * s));
        }

        return d;
    }

    private static HandsDelta HappyHands(float p)
    {
        if (p >= 0.92f)
        {
            return HandsDelta.Mirrored(new Vector2(2f, 4f) * Arc((p - 0.92f) / 0.08f));
        }

        var r = Arc(Frac(p / 0.92f * 3f));
        return HandsDelta.Mirrored(new Vector2(8f * r, -15f * r), 0.09f * r);
    }

    private static EmotePoseDelta ShockedPose(float p)
    {
        var d = EmotePoseDelta.None;
        var k = p < 0.10f
            ? p / 0.10f
            : 1f - SmoothStep(0.10f, 0.62f, p);
        d.ScaleMul = new Vector2(1f - (0.10f * k), 1f + (0.17f * k));
        d.Offset.Y = -13f * k;

        // A shiver on the way down, dying with the recovery.
        var shake = 1f - SmoothStep(0.25f, 0.95f, p);
        d.Offset.X = 5f * shake * MathF.Sin(2f * MathF.PI * 6f * p);
        return d;
    }

    private static HandsDelta ShockedHands(float p)
    {
        var k = p < 0.10f
            ? p / 0.10f
            : 1f - SmoothStep(0.10f, 0.62f, p);
        var shiver = 3f * (1f - SmoothStep(0.25f, 0.95f, p)) * MathF.Sin(2f * MathF.PI * 6f * p);
        return HandsDelta.Mirrored(new Vector2((11f * k) + shiver, -27f * k), 0.12f * k);
    }

    private static EmotePoseDelta AngryPose(float p)
    {
        var d = EmotePoseDelta.None;
        if (p >= 0.9f)
        {
            return d;
        }

        var u = p / 0.9f;
        var beat = Frac(u * 3f);
        var r = beat < 0.22f
            ? beat / 0.22f
            : 1f - SmoothStep(0.22f, 0.8f, beat);
        d.ScaleMul = new Vector2(1f + (0.17f * r), 1f - (0.21f * r));
        d.Offset.X = 5f * r * ((int)(u * 3f) % 2 == 0 ? 1f : -1f);
        return d;
    }

    private static HandsDelta AngryHands(float p)
    {
        if (p >= 0.9f)
        {
            return HandsDelta.None;
        }

        var beat = Frac(p / 0.9f * 3f);
        var r = beat < 0.22f ? beat / 0.22f : 1f - SmoothStep(0.22f, 0.8f, beat);
        return HandsDelta.Mirrored(new Vector2(7f * r, 11f * r), -0.12f * r);
    }

    private static EmotePoseDelta DoubtPose(float p)
    {
        var d = EmotePoseDelta.None;
        var lean = SmoothStep(0f, 0.25f, p) * (1f - SmoothStep(0.78f, 1f, p));
        d.Offset.X = (-13f + (5f * MathF.Sin(2f * MathF.PI * 0.8f * p))) * lean;
        d.Offset.Y = -3f * lean;
        d.ScaleMul = new Vector2(1f + (0.04f * lean), 1f - (0.03f * lean));
        return d;
    }

    private static HandsDelta DoubtHands(float p)
    {
        var lean = SmoothStep(0f, 0.25f, p) * (1f - SmoothStep(0.78f, 1f, p));
        var sway = MathF.Sin(2f * MathF.PI * 0.8f * p);
        return new HandsDelta
        {
            Left = new Vector2(9f + (3f * sway), -16f) * lean,
            LeftTilt = 0.10f * lean,
            Right = new Vector2(0f, 3f * lean),
        };
    }

    private static EmotePoseDelta SulkPose(float p)
    {
        var d = EmotePoseDelta.None;
        if (p < 0.12f)
        {
            d.Offset.Y = -6f * Arc(p / 0.12f);
            return d;
        }

        var f = SmoothStep(0.12f, 0.34f, p) * (1f - SmoothStep(0.84f, 1f, p));
        var sag = 0.025f * MathF.Sin(2f * MathF.PI * 1.0f * p);
        d.ScaleMul = new Vector2(1f + ((0.10f + (sag * 0.5f)) * f), 1f - ((0.18f + sag) * f));
        return d;
    }

    private static HandsDelta SulkHands(float p)
    {
        var f = SmoothStep(0.12f, 0.34f, p) * (1f - SmoothStep(0.84f, 1f, p));
        var waver = MathF.Sin(2f * MathF.PI * 1.0f * p);
        return HandsDelta.Mirrored(new Vector2((4f + waver) * f, 9f * f));
    }

    private static EmotePoseDelta YawnPose(float p)
    {
        var d = EmotePoseDelta.None;
        var rise = SmoothStep(0f, 0.32f, p) * (1f - SmoothStep(0.60f, 0.86f, p));
        d.ScaleMul = new Vector2(1f - (0.07f * rise), 1f + (0.14f * rise));
        d.Offset.Y = -11f * rise;

        var settle = SmoothStep(0.78f, 1f, p);
        d.ScaleMul.X += 0.05f * settle;
        d.ScaleMul.Y -= 0.07f * settle;
        return d;
    }

    private static HandsDelta YawnHands(float p)
    {
        var rise = SmoothStep(0f, 0.32f, p) * (1f - SmoothStep(0.60f, 0.86f, p));
        var settle = SmoothStep(0.78f, 1f, p) * (1f - SmoothStep(0.94f, 1f, p));
        return HandsDelta.Mirrored(
            new Vector2((12f * rise) + (3f * settle), (-24f * rise) + (7f * settle)),
            HandsDelta.MaxTilt * rise);
    }
}
