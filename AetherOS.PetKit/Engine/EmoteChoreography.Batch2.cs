// Batch 2 emote choreographies: the canvas-approved parity push (Emote Canvas, Aug 2026).
// Same grammar as EmoteChoreographies: pure functions of p, ground line sacred, excursions
// inside the hop envelope, no flips on a dressed pet. Particle garnish lives with the other
// garnish in PetRuntime.PlayEmoteGarnish.
//
// Hand tracks: stay inside HandsDelta.MaxReach256, bias every raise outboard (+X points away
// from the body), hands are only about what the emote already was, arrive a quarter cycle
// behind any body rhythm, and asymmetry carries meaning. Tilt leans only what the hand holds.
// Every track ends where it started, so an interrupted clip eases home to the same place.
//
// Parked: the ten food/drink emotes and lightsticks; they want a held-prop pass first.
namespace AetherOS.PetKit.Engine;

using System;
using System.Collections.Generic;
using System.Numerics;

public static class EmoteChoreographiesBatch2
{
    // Local aliases so the tracks below read unchanged.
    private static float Arc(float q) => EmoteCurves.Arc(q);

    private static float Frac(float v) => EmoteCurves.Frac(v);

    private static float SS(float e0, float e1, float t) => EmoteCurves.SS(e0, e1, t);

    private static float Hold(float a, float b, float p) => EmoteCurves.Hold(a, b, p);

    private static EmotePoseDelta Delta(float ox = 0f, float oy = 0f, float sx = 1f, float sy = 1f) =>
        new() { Offset = new Vector2(ox, oy), ScaleMul = new Vector2(sx, sy) };

    private static EmoteMorph M(
        float squash = 0f, float lift = 0f, float tremble = 0f,
        float glow = 0f, float blur = 0f, float withdraw = 0f, float blush = 0f,
        float rate = 0f, float tip = 0f, float bristle = 0f, float ripple = 0f) =>
        EmoteCurves.M(squash, lift, tremble, glow, blur, withdraw, blush, rate, tip, bristle, ripple);

    public static readonly IReadOnlyList<EmoteDef> All =
    [
        // ------------------------------------------------------------------ celebrations
        new EmoteDef
        {
            // Sway on alternating squash, hop finish.
            Key = "dance", Name = "Dance", GameEmote = "/dance", Seconds = 3.2f,
            Pose = p =>
            {
                if (p >= 0.9f) return Delta(oy: -13f * Arc((p - 0.9f) / 0.1f));
                var w = Hold(0.1f, 0.87f, p);
                var s = MathF.Sin(2f * MathF.PI * 2.5f * p);
                return Delta(ox: 15f * s * w, sx: 1f + 0.06f * MathF.Abs(s) * w, sy: 1f - 0.07f * MathF.Abs(s) * w);
            },
            // Trailing the sway by a quarter cycle, which is the whole difference between a
            // dance and a puppet: the arms are carried by the body rather than bolted to it.
            Hands = p =>
            {
                if (p >= 0.9f) return HandsDelta.Mirrored(new Vector2(9f, -20f) * Arc((p - 0.9f) / 0.1f));
                var w = Hold(0.1f, 0.87f, p);
                var lag = MathF.Sin((2f * MathF.PI * 2.5f * p) - (MathF.PI * 0.5f));
                return HandsDelta.Swung(9f * lag * w, -6f * w);
            },
            Mouth = [new MouthKey(0f, "grin", 0.12f), new MouthKey(1.5f, "laugh", 0.15f), new MouthKey(2.9f, "smile", 0.25f)],

            // Faster and looser: the creature's own clock speeds up, the soft parts move
            // more, and it tips into each sway.
            Morph = p =>
            {
                var w = Hold(0.1f, 0.87f, p);
                var s = MathF.Sin(2f * MathF.PI * 2.5f * p);
                return M(rate: 0.45f * w, ripple: 0.6f * w, tip: 0.35f * s * w, glow: 0.10f * w);
            },

            // Arcs, all the way through the routine.
            Eyes = [new EyeKey(0.15f, "happy", 0.2f), new EyeKey(2.95f, "open", 0.25f)],

            Parts = "happy",
            Glyph = "note",
        },
        new EmoteDef
        {
            Key = "huzzah", Name = "Huzzah", GameEmote = "/huzzah", Seconds = 1.6f,
            Pose = p =>
            {
                if (p < 0.55f) return Delta(oy: -30f * Arc(p / 0.55f));
                if (p >= 0.85f) { var s = Arc((p - 0.85f) / 0.15f); return Delta(sx: 1f + 0.11f * s, sy: 1f - 0.13f * s); }
                return EmotePoseDelta.None with { ScaleMul = Vector2.One };
            },
            // Both arms thrown with the leap and still up at the top of it, which is what a
            // huzzah is; they come down through the landing squash rather than before it.
            Hands = p =>
            {
                var raise = p < 0.55f ? Arc(p / 0.55f) : 1f - SS(0.55f, 0.92f, p);
                return HandsDelta.Mirrored(new Vector2(10f * raise, -27f * raise), HandsDelta.MaxTilt * raise);
            },
            Mouth = [new MouthKey(0f, "laugh", 0.08f), new MouthKey(1.2f, "grin", 0.2f)],

            // Lit on the leap.
            Morph = p => M(glow: 0.25f * (p < 0.55f ? Arc(p / 0.55f) : 1f - SS(0.85f, 1f, p)), rate: 0.4f * (p < 0.55f ? Arc(p / 0.55f) : 1f - SS(0.85f, 1f, p)), ripple: 0.5f * (p < 0.55f ? Arc(p / 0.55f) : 1f - SS(0.85f, 1f, p))),

            // Wide on the leap, arcs on the landing: surprise at your own enthusiasm.
            Eyes = [new EyeKey(0f, "wide", 0.06f), new EyeKey(0.9f, "happy", 0.18f), new EyeKey(1.4f, "open", 0.2f)],

            Parts = "happy",
            Glyph = "burst",
        },
        new EmoteDef
        {
            // Snap tall, crisp hold, sparkle garnish sells the pose.
            Key = "vpose", Name = "Victory Pose", GameEmote = "/vpose", Seconds = 2.0f,
            Pose = p =>
            {
                var snap = p < 0.12f ? p / 0.12f : 1f - SS(0.82f, 1f, p);
                return Delta(oy: -6f * snap, sx: 1f - 0.06f * snap, sy: 1f + 0.1f * snap);
            },
            // The pose IS the arms here, so they take the body's own snap envelope exactly: up,
            // out, and held there for as long as the body holds itself tall.
            Hands = p =>
            {
                var snap = p < 0.12f ? p / 0.12f : 1f - SS(0.82f, 1f, p);
                return HandsDelta.Mirrored(new Vector2(13f * snap, -25f * snap), HandsDelta.MaxTilt * snap);
            },
            Mouth = [new MouthKey(0f, "grin", 0.1f)],

            // Arcs on the snap and held for the pose.
            Eyes = [new EyeKey(0.05f, "happy", 0.1f), new EyeKey(1.85f, "open", 0.15f)],

            // Lit for the pose and held.
            Morph = p => M(glow: 0.30f * (p < 0.12f ? p / 0.12f : 1f - SS(0.82f, 1f, p)), ripple: 0.4f * (p < 0.12f ? p / 0.12f : 1f - SS(0.82f, 1f, p))),

            Parts = "happy",
            Glyph = "burst",
        },
        new EmoteDef
        {
            // Fake-out: two innocuous bounces, one huge leap, smug landing.
            Key = "psych", Name = "Psych!", GameEmote = "/psych", Seconds = 2.2f,
            Pose = p =>
            {
                if (p < 0.55f) { var r = Arc(Frac(p / 0.55f * 2f)); return Delta(sx: 1f + 0.06f * r, sy: 1f - 0.08f * r); }
                if (p < 0.9f) { var q = (p - 0.55f) / 0.35f; return Delta(oy: -30f * Arc(q), sy: 1f + 0.06f * Arc(q)); }
                var s = Arc((p - 0.9f) / 0.1f);
                return Delta(sx: 1f + 0.1f * s, sy: 1f - 0.12f * s);
            },
            // Innocent through the fake-out, everything on the leap, and dropped smugly on the
            // landing: the arms are how the joke lands, so they give nothing away early.
            Hands = p =>
            {
                if (p < 0.55f) return HandsDelta.Mirrored(new Vector2(3f, 2f) * Arc(Frac(p / 0.55f * 2f)));
                if (p < 0.9f)
                {
                    var q = Arc((p - 0.55f) / 0.35f);
                    return HandsDelta.Mirrored(new Vector2(12f * q, -26f * q), HandsDelta.MaxTilt * q);
                }
                return HandsDelta.Mirrored(new Vector2(6f, 5f) * Arc((p - 0.9f) / 0.1f));
            },
            Mouth = [new MouthKey(0f, "grin", 0.1f), new MouthKey(1.2f, "laugh", 0.12f), new MouthKey(2.0f, "grin", 0.15f)],

            // Sly through the fake-out, then everything at once on the reveal.
            Morph = p =>
            {
                var sly = Hold(0.1f, 0.5f, p);
                var big = p < 0.55f ? 0f : Arc((p - 0.55f) / 0.45f);
                return M(squash: 0.010f * sly, rate: 0.5f * big, glow: 0.25f * big, ripple: 0.4f * big);
            },

            // Narrowed through the fake-out and wide on the reveal, which is the joke.
            Eyes = [new EyeKey(0.1f, "squint", 0.2f), new EyeKey(0.58f, "wide", 0.05f), new EyeKey(1.95f, "happy", 0.2f)],

            Parts = "happy",
        },

        // ------------------------------------------------------------------ affection
        new EmoteDef
        {
            // The self-squeeze with a rock.
            Key = "hug", Name = "Hug", GameEmote = "/hug", Seconds = 2.4f,
            Pose = p =>
            {
                var f = Hold(0.2f, 0.86f, p);
                return Delta(ox: 3f * MathF.Sin(2f * MathF.PI * 1.2f * p) * f, sx: 1f + 0.10f * f, sy: 1f - 0.08f * f);
            },
            // Inboard, and the one place in the set where that is right: a hug is arms AROUND
            // something, so they close over the middle rather than reaching out for it.
            Hands = p =>
            {
                var f = Hold(0.2f, 0.86f, p);
                var rock = MathF.Sin(2f * MathF.PI * 1.2f * p);
                return HandsDelta.Mirrored(new Vector2(-7f * f, (4f + rock) * f));
            },
            Mouth = [new MouthKey(0f, "beam", 0.2f)],

            // And the creature pulls into itself while it squeezes. On a Nautilus that is the
            // soul going into the shell, on a Muffle the head settling, on everything else
            // nothing at all.
            Morph = p => M(withdraw: 0.35f * Hold(0.2f, 0.86f, p), rate: -0.2f * Hold(0.2f, 0.86f, p)),

            // Eyes shut with the squeeze, which is the whole difference between a hug and a grab.
            Eyes = [new EyeKey(0.25f, "happy", 0.2f), new EyeKey(2.15f, "open", 0.2f)],

            Parts = "happy",
            Glyph = "heart",
        },
        new EmoteDef
        {
            // Lean into the fling; the hearts are garnish (also answers /dote).
            Key = "blowkiss", Name = "Blow Kiss", GameEmote = "/blowkiss", Seconds = 1.8f,
            Pose = p =>
            {
                var f = Hold(0.2f, 0.86f, p);
                return Delta(ox: 3f * SS(0.35f, 0.5f, p) * (1f - SS(0.85f, 1f, p)), oy: -4f * f);
            },
            // One hand only, and it does the whole emote: up to the mouth on the kiss, then
            // flung outboard on the lean the body is already making. The off hand stays out of it.
            Hands = p =>
            {
                var lift = SS(0.05f, 0.3f, p) * (1f - SS(0.35f, 0.55f, p));
                var fling = SS(0.35f, 0.5f, p) * (1f - SS(0.8f, 1f, p));
                return new HandsDelta
                {
                    Right = new Vector2((6f * lift) + (22f * fling), (-17f * lift) - (6f * fling)),
                    RightTilt = HandsDelta.MaxTilt * fling,
                };
            },
            Mouth = [new MouthKey(0f, "smile", 0.12f), new MouthKey(0.25f, "o", 0.12f), new MouthKey(0.7f, "grin", 0.2f)],

            // A lean into the fling and a ripple off it: the soft parts follow the hand out.
            Morph = p =>
            {
                var f = Hold(0.2f, 0.86f, p);
                var fling = SS(0.42f, 0.62f, p) * (1f - SS(0.85f, 1f, p));
                return M(tip: 0.30f * fling, ripple: 0.5f * fling, blush: 0.30f * f, glow: 0.10f * f);
            },

            // Away, then shut on the kiss itself.
            Eyes = [new EyeKey(0.15f, "away", 0.18f), new EyeKey(0.45f, "happy", 0.12f), new EyeKey(1.5f, "open", 0.2f)],

            Parts = "happy",
            Glyph = "twohearts",
        },
        new EmoteDef
        {
            // Shrink, look away; the cheek pulse is garnish.
            Key = "blush", Name = "Blush", GameEmote = "/blush", Seconds = 2.0f,
            Pose = p =>
            {
                var f = Hold(0.22f, 0.86f, p);
                return Delta(ox: -6f * f, sx: 1f - 0.05f * f, sy: 1f - 0.04f * f);
            },
            // A hand up to the cheek while the other tucks away: the asymmetry is the shyness,
            // and two hands to the face would be horror rather than a blush.
            Hands = p =>
            {
                var f = Hold(0.22f, 0.86f, p);
                return new HandsDelta
                {
                    Right = new Vector2(7f * f, -15f * f),
                    Left = new Vector2(-3f * f, 3f * f),
                };
            },
            Mouth = [new MouthKey(0f, "pout", 0.15f), new MouthKey(1.4f, "smile", 0.3f)],

            // Looks away first, then back. The squint is embarrassment and the happy is what
            // it was embarrassed about.
            Eyes =
            [
                new EyeKey(0.1f, "squint", 0.15f),
                new EyeKey(1.35f, "happy", 0.2f),
                new EyeKey(1.85f, "open", 0.15f),
            ],

            // The dial this emote is named after.
            Morph = p => M(blush: 0.85f * Hold(0.15f, 0.85f, p), squash: 0.010f * Hold(0.15f, 0.85f, p), rate: -0.15f * Hold(0.15f, 0.85f, p)),

            Parts = "content",
            Glyph = "heart",
        },

        // ------------------------------------------------------------------ distress
        new EmoteDef
        {
            // 7 Hz dither with a slight lift; nothing else in the set uses this register.
            Key = "panic", Name = "Panic", GameEmote = "/panic", Seconds = 1.6f,
            Pose = p =>
            {
                var w = 1f - SS(0.82f, 1f, p);
                return Delta(ox: 6f * MathF.Sin(2f * MathF.PI * 7f * p) * w, oy: -3f * w, sx: 1f + 0.05f * w, sy: 1f - 0.05f * w);
            },
            // Up fast and shaking on the way down, on the body's own 7 Hz: a startle that
            // reaches its pose late is a comedian explaining a joke.
            Hands = p =>
            {
                var k = p < 0.08f ? p / 0.08f : 1f - SS(0.08f, 0.82f, p);
                var dither = 3f * (1f - SS(0.2f, 0.95f, p)) * MathF.Sin(2f * MathF.PI * 7f * p);
                return HandsDelta.Mirrored(new Vector2((11f * k) + dither, -25f * k), 0.1f * k);
            },
            Mouth = [new MouthKey(0f, "gasp", 0.05f), new MouthKey(1.25f, "frown", 0.2f)],

            // The register the body track cannot reach: a real tremble on the shells that ring
            // and a motion ghost on the ones that blur.
            Morph = p =>
            {
                var w = 1f - SS(0.82f, 1f, p);
                return M(tremble: 0.9f * w, blur: 0.5f * w, rate: 0.6f * w, bristle: 0.5f * w);
            },

            // Wide and stays wide. Nothing else in the set opens this fast.
            Eyes = [new EyeKey(0f, "wide", 0.04f), new EyeKey(1.3f, "open", 0.2f)],

            Parts = "alert",
            Glyph = "bang",
        },
        new EmoteDef
        {
            // Sob pulses decaying over the hold; tears are garnish (Droplet, from the face).
            Key = "cry", Name = "Cry", GameEmote = "/cry", Seconds = 2.8f,
            Pose = p =>
            {
                var f = Hold(0.2f, 0.9f, p);
                var sob = 0.05f * Arc(Frac(p * 5f)) * (1f - 0.4f * p);
                return Delta(sx: 1f + (0.08f + sob * 0.5f) * f, sy: 1f - (0.12f + sob) * f);
            },
            // Both hands up to the eyes and shaking with each sob, which is the one gesture
            // everyone reads as crying without a single tear being drawn.
            Hands = p =>
            {
                var f = Hold(0.2f, 0.9f, p);
                var sob = Arc(Frac(p * 5f)) * (1f - (0.4f * p));
                return HandsDelta.Mirrored(new Vector2(6f * f, (-16f - (3f * sob)) * f));
            },
            Mouth = [new MouthKey(0f, "quiver", 0.2f), new MouthKey(0.6f, "sad", 0.3f), new MouthKey(2.5f, "flat", 0.25f)],

            // Squeezed shut on each sob and never fully open between them.
            Eyes =
            [
                new EyeKey(0.1f, "quarter", 0.15f),
                new EyeKey(0.55f, "shut", 0.1f),
                new EyeKey(0.9f, "quarter", 0.16f),
                new EyeKey(1.5f, "shut", 0.1f),
                new EyeKey(1.9f, "quarter", 0.18f),
                new EyeKey(2.55f, "half", 0.25f),
            ],

            // Curling in and going dim: the one place a negative glow is right.
            Morph = p =>
            {
                var f = Hold(0.2f, 0.9f, p);
                return M(glow: -0.12f * f, withdraw: 0.30f * f, blush: 0.25f * f, rate: -0.2f * f, ripple: -0.2f * f);
            },

            Parts = "sleepy",
            Glyph = "rain",
        },
        new EmoteDef
        {
            Key = "upset", Name = "Upset", GameEmote = "/upset", Seconds = 2.2f,
            Pose = p =>
            {
                var f = Hold(0.2f, 0.88f, p);
                var w = 0.02f * MathF.Sin(2f * MathF.PI * 1.1f * p);
                return Delta(sx: 1f + 0.06f * f, sy: 1f - (0.09f + w) * f);
            },
            // Limp and low, wavering on the body's own slow beat. Almost nothing, which is the
            // point: gloom is the absence of the little lifts everything else here has.
            Hands = p =>
            {
                var f = Hold(0.2f, 0.88f, p);
                var waver = MathF.Sin(2f * MathF.PI * 1.1f * p);
                return HandsDelta.Mirrored(new Vector2((3f + waver) * f, 9f * f));
            },
            Mouth = [new MouthKey(0f, "quiver", 0.25f), new MouthKey(1.0f, "sad", 0.35f)],

            // Half and falling, on the mouth's own turn from quiver to sad.
            Eyes = [new EyeKey(0.15f, "half", 0.25f), new EyeKey(1f, "quarter", 0.3f)],

            // Smaller and heavier where it stands.
            Morph = p =>
            {
                var f = Hold(0.2f, 0.88f, p);
                return M(lift: 2.8f * f, withdraw: 0.35f * f, glow: -0.10f * f, rate: -0.25f * f);
            },

            Parts = "sleepy",
            Glyph = "rain",
        },
        new EmoteDef
        {
            // Shrink + tremble; pale gust garnish.
            Key = "fear", Name = "Fear", GameEmote = "/fear", Seconds = 2.2f,
            Pose = p =>
            {
                var f = Hold(0.12f, 0.88f, p);
                return Delta(ox: (2.5f * MathF.Sin(2f * MathF.PI * 8f * p) - 5f) * f, sx: 1f + 0.06f * f, sy: 1f - 0.1f * f);
            },
            // Drawn in and up as a guard, trembling on the body's 8 Hz. The hands are what turn
            // a shrinking creature into a frightened one.
            Hands = p =>
            {
                var f = Hold(0.12f, 0.88f, p);
                var tremble = MathF.Sin(2f * MathF.PI * 8f * p);
                return HandsDelta.Mirrored(new Vector2((2f + tremble) * f, -11f * f));
            },
            Mouth = [new MouthKey(0f, "gasp", 0.08f), new MouthKey(1.0f, "quiver", 0.25f)],

            // Shrink and tremble, on the creature instead of the frame around it.
            Morph = p =>
            {
                var f = Hold(0.12f, 0.88f, p);
                return M(tremble: 0.6f * f, glow: -0.10f * f, withdraw: 0.45f * f, bristle: 0.45f * f, rate: 0.25f * f);
            },

            // Wide at the gasp, then narrowed and held.
            Eyes = [new EyeKey(0f, "wide", 0.05f), new EyeKey(1f, "threeq", 0.25f)],

            Parts = "alert",
        },
        new EmoteDef
        {
            Key = "disappointed", Name = "Disappointed", GameEmote = "/disappointed", Seconds = 2.4f,
            Pose = p =>
            {
                var f = Hold(0.25f, 0.88f, p);
                return Delta(ox: 4f * MathF.Sin(2f * MathF.PI * 0.5f * p) * f, sx: 1f + 0.07f * f, sy: 1f - 0.11f * f);
            },
            // Hands drop and hang, swinging a beat behind the body's slow sway. Disappointment
            // is a thing that goes out of you, so nothing here rises.
            Hands = p =>
            {
                var f = Hold(0.25f, 0.88f, p);
                var lag = MathF.Sin((2f * MathF.PI * 0.5f * p) - (MathF.PI * 0.5f));
                return HandsDelta.Swung(3f * lag * f, 8f * f);
            },
            Mouth = [new MouthKey(0f, "flat", 0.2f), new MouthKey(0.5f, "sad", 0.35f)],

            // Sinking within its own footprint rather than moving: the creature gets smaller and
            // heavier where it stands, which an offset cannot say.
            Morph = p =>
            {
                var f = Hold(0.25f, 0.88f, p);
                return M(lift: 3.2f * f, glow: -0.08f * f, withdraw: 0.30f * f, rate: -0.30f * f, tip: 0.15f * f);
            },

            // The lids come down with the shoulders and do not go back up.
            Eyes = [new EyeKey(0.3f, "half", 0.3f), new EyeKey(0.9f, "threeq", 0.35f)],

            Parts = "sleepy",
            Glyph = "cloud",
        },

        // ------------------------------------------------------------------ temper
        new EmoteDef
        {
            // Angry's bigger sibling: four stomps with an 11 Hz shudder riding them.
            Key = "furious", Name = "Furious", GameEmote = "/furious", Seconds = 2.4f,
            Pose = p =>
            {
                if (p >= 0.9f) return Delta();
                var u = p / 0.9f;
                var b = Frac(u * 4f);
                var r = b < 0.2f ? b / 0.2f : 1f - SS(0.2f, 0.75f, b);
                var side = (int)MathF.Floor(u * 4f) % 2 == 1 ? -1f : 1f;
                return Delta(
                    ox: 6f * r * side + 2f * MathF.Sin(2f * MathF.PI * 11f * p),
                    sx: 1f + 0.20f * r, sy: 1f - 0.25f * r);
            },
            // Fists driven DOWN on each stomp, sharing the body's attack and release: the arms
            // are what turn four squashes into four blows.
            Hands = p =>
            {
                if (p >= 0.9f) return HandsDelta.None;
                var b = Frac(p / 0.9f * 4f);
                var r = b < 0.2f ? b / 0.2f : 1f - SS(0.2f, 0.75f, b);
                return HandsDelta.Mirrored(new Vector2(8f * r, 12f * r), -HandsDelta.MaxTilt * r);
            },
            Mouth = [new MouthKey(0f, "frown", 0.05f), new MouthKey(2.1f, "frown", 0.2f)],

            // The same as fume with the lid off.
            Morph = p =>
            {
                var f = Hold(0.08f, 0.88f, p);
                return M(tremble: 0.8f * f, glow: 0.45f * f, blush: 0.55f * f, blur: 0.3f * f, bristle: 0.75f * f, rate: 0.45f * f);
            },

            // Squeezed to nothing and held there.
            Eyes = [new EyeKey(0.05f, "squint", 0.08f), new EyeKey(2.1f, "open", 0.25f)],

            Parts = "alert",
            Glyph = "flame",
        },
        new EmoteDef
        {
            // Big inhale, hard exhale; steam gusts are garnish.
            Key = "fume", Name = "Fume", GameEmote = "/fume", Seconds = 2.0f,
            Pose = p =>
            {
                var inh = Hold(0.25f, 0.4f, p);
                var exh = SS(0.45f, 0.6f, p) * (1f - SS(0.85f, 1f, p));
                return Delta(sx: 1f - 0.05f * inh + 0.08f * exh, sy: 1f + 0.07f * inh - 0.10f * exh);
            },
            // Clenched in on the inhale, thrown down on the exhale: the same two beats the body
            // takes, so the whole creature fumes rather than a body with tidy arms beside it.
            Hands = p =>
            {
                var inh = Hold(0.25f, 0.4f, p);
                var exh = SS(0.45f, 0.6f, p) * (1f - SS(0.85f, 1f, p));
                return HandsDelta.Mirrored(new Vector2((3f * inh) + (9f * exh), (-6f * inh) + (10f * exh)));
            },
            Mouth = [new MouthKey(0f, "pout", 0.1f), new MouthKey(1.6f, "flat", 0.25f)],

            // Narrowed, not lowered: anger looks at the thing it is angry about.
            Eyes = [new EyeKey(0.1f, "squint", 0.15f), new EyeKey(1.6f, "open", 0.25f)],

            // Anger is heat. Red, lit, and trembling with the effort of holding it in.
            Morph = p =>
            {
                var f = Hold(0.12f, 0.85f, p);
                return M(tremble: 0.35f * f, glow: 0.30f * f, blush: 0.45f * f, squash: 0.014f * f);
            },

            Parts = "alert",
            Glyph = "flame",
        },

        // ------------------------------------------------------------------ retorts
        new EmoteDef
        {
            Key = "chuckle", Name = "Chuckle", GameEmote = "/chuckle", Seconds = 1.6f,
            Pose = p =>
            {
                if (p >= 0.85f) return Delta();
                var r = Arc(Frac(p / 0.85f * 3f));
                return Delta(sx: 1f + 0.04f * r, sy: 1f - 0.06f * r);
            },
            // One hand up to the mouth, politely: a chuckle is a laugh being covered, and the
            // covering is the only thing separating the two gestures.
            Hands = p =>
            {
                if (p >= 0.85f) return HandsDelta.None;
                var f = Hold(0.12f, 0.8f, p);
                var r = Arc(Frac(p / 0.85f * 3f));
                return new HandsDelta { Right = new Vector2(6f * f, (-13f - (2f * r)) * f) };
            },
            Mouth =
            [
                new MouthKey(0f, "grin", 0.1f), new MouthKey(0.5f, "laugh", 0.1f),
                new MouthKey(0.95f, "grin", 0.12f), new MouthKey(1.35f, "smile", 0.2f),
            ],

            // A jiggle, not a scale. The body track pulses the frame; this pulses the creature,
            // on the same three beats, so the two read as one laugh with weight in it.
            Morph = p =>
            {
                if (p >= 0.85f)
                {
                    return EmoteMorph.None;
                }

                var r = Arc(Frac(p / 0.85f * 3f));
                return M(squash: 0.020f * r, glow: 0.10f * r, ripple: 0.3f * r);
            },

            // Same arcs, gentler exit: a chuckle ends before the face does.
            Eyes = [new EyeKey(0f, "happy", 0.1f), new EyeKey(1.35f, "open", 0.2f)],

            Parts = "happy",
        },
        new EmoteDef
        {
            // Recoil snap, held, eased back.
            Key = "aback", Name = "Aback", GameEmote = "/aback", Seconds = 1.7f,
            Pose = p =>
            {
                var snap = p < 0.09f ? p / 0.09f : 1f - SS(0.6f, 0.95f, p);
                return Delta(ox: -17f * snap, sx: 1f - 0.05f * snap, sy: 1f + 0.05f * snap);
            },
            // Up and open on the recoil, on the body's own snap: hands thrown between the
            // creature and whatever it just heard.
            Hands = p =>
            {
                var snap = p < 0.09f ? p / 0.09f : 1f - SS(0.6f, 0.95f, p);
                return HandsDelta.Mirrored(new Vector2(12f * snap, -18f * snap), 0.09f * snap);
            },
            Mouth = [new MouthKey(0f, "gasp", 0.05f), new MouthKey(0.9f, "eh", 0.2f), new MouthKey(1.4f, "flat", 0.2f)],

            // The snap open, then a settle. Three beats to match the mouth.
            Eyes = [new EyeKey(0f, "wide", 0.04f), new EyeKey(0.9f, "threeq", 0.15f), new EyeKey(1.4f, "open", 0.2f)],

            // The startle, on the creature: it flinches back into itself before it recovers.
            Morph = p =>
            {
                var snap = p < 0.10f ? p / 0.10f : 1f - SS(0.10f, 0.7f, p);
                return M(withdraw: 0.40f * snap, tremble: 0.35f * snap, blur: 0.25f * snap, bristle: 0.4f * snap);
            },

            Parts = "alert",
            Glyph = "bang",
        },
        new EmoteDef
        {
            // Sharper than shake: square-root snap between the swings.
            Key = "deny", Name = "Deny", GameEmote = "/deny", Seconds = 1.3f,
            Pose = p =>
            {
                var s = MathF.Sin(2f * MathF.PI * 4f * p);
                var snap = MathF.Sign(s) * MathF.Sqrt(MathF.Abs(s));
                return Delta(ox: 13f * snap * (1f - SS(0.65f, 1f, p)));
            },
            // The hands LAG the body by a quarter cycle, which is the whole trick: limbs that
            // arrive with the body are welded on, limbs that arrive late have mass. They lift a
            // touch while it lasts, so the refusal reads from the arms too.
            Hands = p =>
            {
                var s = MathF.Sin((2f * MathF.PI * 4f * p) - (MathF.PI * 0.5f));
                var fade = 1f - SS(0.65f, 1f, p);
                var d = HandsDelta.Swung(-7f * s * fade);
                var lift = -6f * (1f - SS(0.72f, 1f, p));
                d.Right.Y = lift;
                d.Left.Y = lift;
                return d;
            },
            Mouth = [new MouthKey(0f, "pout", 0.08f), new MouthKey(1.0f, "flat", 0.2f)],

            // Away, and it stays away: refusing while making eye contact is a different emote.
            Eyes = [new EyeKey(0.1f, "away", 0.12f), new EyeKey(1.1f, "open", 0.2f)],

            // Tipping away from the thing it will not have, and bristling a little at it.
            Morph = p =>
            {
                var s = MathF.Sin(2f * MathF.PI * 4f * p);
                var w = 1f - SS(0.65f, 1f, p);
                return M(tip: -0.35f * s * w, bristle: 0.15f * w);
            },

            Parts = "alert",
        },
        new EmoteDef
        {
            // Lean in and bob, all smirk. (The canvas's stuck-out tongue needs a mouth
            // shape the library doesn't have yet; parked with the props.)
            Key = "deride", Name = "Deride", GameEmote = "/deride", Seconds = 2.0f,
            Pose = p =>
            {
                var f = Hold(0.18f, 0.85f, p);
                var sy = 1f;
                if (p < 0.85f) sy = 1f - 0.045f * Arc(Frac(p / 0.85f * 3f)) * f;
                return Delta(ox: 8f * f, sy: sy);
            },
            // One hand out and levelled at whoever this is aimed at, bobbing with the lean. A
            // matched pair would read as a shrug, which is the opposite of a taunt.
            Hands = p =>
            {
                var f = Hold(0.18f, 0.85f, p);
                var bob = Arc(Frac(p / 0.85f * 3f));
                return new HandsDelta
                {
                    Right = new Vector2(15f * f, (2f - (3f * bob)) * f),
                    RightTilt = HandsDelta.MaxTilt * f,
                };
            },
            Mouth = [new MouthKey(0f, "grin", 0.1f), new MouthKey(1.7f, "smile", 0.2f)],

            // Away and half lidded, which is the whole of a sneer that a mouth cannot carry.
            Eyes = [new EyeKey(0.15f, "away", 0.18f), new EyeKey(0.5f, "half", 0.25f), new EyeKey(1.6f, "open", 0.25f)],

            // Leaning away and slowing down: contempt is unhurried.
            Morph = p =>
            {
                var f = Hold(0.15f, 0.85f, p);
                return M(tip: -0.40f * f, rate: -0.20f * f, squash: 0.008f * f);
            },

            Parts = "curious",
        },
        new EmoteDef
        {
            // Crouch, then the idea lands: pop + rise.
            Key = "eureka", Name = "Eureka", GameEmote = "/eureka", Seconds = 1.9f,
            Pose = p =>
            {
                var crouch = Hold(0.18f, 0.3f, p);
                var pop = p < 0.45f ? 0f : p < 0.55f ? (p - 0.45f) / 0.1f : 1f - SS(0.85f, 1f, p);
                return Delta(oy: -16f * pop, sx: 1f + 0.08f * crouch - 0.05f * pop, sy: 1f - 0.1f * crouch + 0.1f * pop);
            },
            // Tucked through the crouch, then one hand shot up on the pop: the idea arriving is
            // an arm going up, and doing it with both would be a cheer instead.
            Hands = p =>
            {
                var crouch = Hold(0.18f, 0.3f, p);
                var pop = p < 0.45f ? 0f : p < 0.55f ? (p - 0.45f) / 0.1f : 1f - SS(0.85f, 1f, p);
                return new HandsDelta
                {
                    Right = new Vector2((4f * crouch) + (9f * pop), (5f * crouch) - (27f * pop)),
                    Left = new Vector2(2f * crouch, 4f * crouch),
                    RightTilt = HandsDelta.MaxTilt * pop,
                };
            },
            Mouth = [new MouthKey(0f, "hmm", 0.15f), new MouthKey(0.5f, "o", 0.06f), new MouthKey(1.0f, "beam", 0.2f)],

            // The idea lights the creature, on the shells that are lit at all.
            Morph = p =>
            {
                var pop = p < 0.45f ? 0f : p < 0.55f ? (p - 0.45f) / 0.1f : 1f - SS(0.85f, 1f, p);
                return M(glow: 0.55f * pop, rate: 0.35f * pop, bristle: 0.3f * pop);
            },

            // Narrowed through the thinking, wide the instant it lands, happy once it has.
            Eyes =
            [
                new EyeKey(0f, "quarter", 0.15f),
                new EyeKey(0.47f, "wide", 0.05f),
                new EyeKey(1f, "happy", 0.2f),
            ],

            Parts = "alert",
            Glyph = "bang",
        },

        // ------------------------------------------------------------------ court & courtesy
        new EmoteDef
        {
            Key = "greet", Name = "Greet", GameEmote = "/greet", Seconds = 1.5f,
            Pose = p =>
            {
                if (p >= 0.85f) return Delta();
                var r = Arc(Frac(p / 0.85f * 2f));
                return Delta(ox: 4f * SS(0.1f, 0.4f, p), sx: 1f + 0.06f * r, sy: 1f - 0.1f * r);
            },
            // A small "hello" on each of the two hops, Cheer's amplitude halved: the difference
            // between greeting somebody and celebrating is how far the arms go.
            Hands = p =>
            {
                if (p >= 0.85f) return HandsDelta.Mirrored(new Vector2(2f, 4f) * Arc((p - 0.85f) / 0.15f));
                var r = Arc(Frac(p / 0.85f * 2f));
                return HandsDelta.Mirrored(new Vector2(8f * r, -14f * r), 0.08f * r);
            },
            Mouth = [new MouthKey(0f, "beam", 0.12f)],

            // Up and open: a greeting is aimed at somebody.
            Eyes = [new EyeKey(0.1f, "up", 0.18f), new EyeKey(0.6f, "happy", 0.2f), new EyeKey(1.4f, "open", 0.2f)],

            // Lifted and lit, and the trimmings answer: a greeting is the whole creature
            // arriving, not just an arm going up.
            Morph = p =>
            {
                var f = Hold(0.12f, 0.8f, p);
                return M(lift: -1.6f * f, glow: 0.16f * f, ripple: 0.45f * f, rate: 0.20f * f);
            },

            Parts = "happy",
            Glyph = "burst",
        },
        new EmoteDef
        {
            Key = "kneel", Name = "Kneel", GameEmote = "/kneel", Seconds = 2.6f,
            Pose = p =>
            {
                var f = Hold(0.22f, 0.88f, p);
                return Delta(sx: 1f + 0.1f * f, sy: 1f - 0.18f * f);
            },
            // Low and forward, where hands rest on a knee. Nothing moves once it is down: the
            // stillness is what makes it kneeling rather than crouching.
            Hands = p =>
            {
                var f = Hold(0.22f, 0.88f, p);
                return HandsDelta.Mirrored(new Vector2(4f * f, 10f * f));
            },
            Mouth = [new MouthKey(0f, "flat", 0.25f)],

            // Down through the kneel, up at the end.
            Eyes = [new EyeKey(0.25f, "down", 0.3f), new EyeKey(2.0f, "open", 0.3f)],

            // Down and still. The rate drop is what separates kneeling from crouching.
            Morph = p =>
            {
                var f = Hold(0.2f, 0.9f, p);
                return M(lift: 3.2f * f, rate: -0.35f * f, withdraw: 0.25f * f, squash: 0.012f * f);
            },

            Parts = "content",
        },
        new EmoteDef
        {
            // The deepest fold in the set, with a pleading pulse riding the hold.
            Key = "grovel", Name = "Grovel", GameEmote = "/grovel", Seconds = 2.8f,
            Pose = p =>
            {
                var f = Hold(0.2f, 0.9f, p);
                var pulse = 0.03f * Arc(Frac(p * 3f));
                return Delta(sx: 1f + (0.26f + pulse) * f, sy: 1f - (0.30f + pulse) * f);
            },
            // Stretched out along the ground and pleading on the body's own pulse. The deepest
            // reach in the set, and the only one that spends all of it downward.
            Hands = p =>
            {
                var f = Hold(0.2f, 0.9f, p);
                var plead = Arc(Frac(p * 3f));
                return HandsDelta.Mirrored(new Vector2((6f + (2f * plead)) * f, (13f + (3f * plead)) * f));
            },
            Mouth = [new MouthKey(0f, "sad", 0.25f), new MouthKey(2.4f, "flat", 0.3f)],

            // Shut for the whole grovel. Looking up would undo it.
            Eyes = [new EyeKey(0.2f, "shut", 0.3f), new EyeKey(2.4f, "half", 0.3f)],

            // All the way in. The deepest withdraw in the set, and the Nautilus is nearly gone.
            Morph = p => M(withdraw: 0.75f * Hold(0.2f, 0.9f, p), lift: 4.0f * Hold(0.2f, 0.9f, p), rate: -0.4f * Hold(0.2f, 0.9f, p)),

            Parts = "sleepy",
        },
        new EmoteDef
        {
            // Slow devotional sway; rising sparkles are garnish.
            Key = "pray", Name = "Pray", GameEmote = "/pray", Seconds = 2.8f,
            Pose = p =>
            {
                var f = Hold(0.25f, 0.9f, p);
                return Delta(sx: 1f + 0.06f * f, sy: 1f - 0.09f * f + 0.015f * MathF.Sin(2f * MathF.PI * 0.6f * p) * f);
            },
            // Inboard like the hug, and for the same reason: hands brought TOGETHER in front is
            // the gesture, and a pair reaching outboard would be a shrug at the heavens.
            Hands = p =>
            {
                var f = Hold(0.25f, 0.9f, p);
                var sway = MathF.Sin(2f * MathF.PI * 0.6f * p);
                return HandsDelta.Mirrored(new Vector2(-5f * f, (-8f + sway) * f));
            },
            Mouth = [new MouthKey(0f, "flat", 0.3f), new MouthKey(2.4f, "smile", 0.3f)],

            // Shut through the devotion and open on the release, with the mouth.
            Eyes = [new EyeKey(0.3f, "shut", 0.35f), new EyeKey(2.4f, "open", 0.35f)],

            // A quiet light rather than a bright one.
            Morph = p => M(glow: 0.14f * Hold(0.25f, 0.9f, p), rate: -0.25f * Hold(0.25f, 0.9f, p)),

            Parts = "content",
            Glyph = "radiance",
        },
        new EmoteDef
        {
            // Two quick grateful dips.
            Key = "thankyou", Name = "Thank You", GameEmote = "/thankyou", Seconds = 1.9f,
            Pose = p =>
            {
                var q = Frac(MathF.Min(0.999f, p) * 2f);
                var f = SS(0f, 0.25f, q) * (1f - SS(0.5f, 0.8f, q));
                return Delta(sx: 1f + 0.09f * f, sy: 1f - 0.16f * f);
            },
            // The hands dip with each of the two folds and a held item tips with them: the
            // courtly half of a thank you, where a thing is presented rather than brandished.
            Hands = p =>
            {
                var q = Frac(MathF.Min(0.999f, p) * 2f);
                var f = SS(0f, 0.25f, q) * (1f - SS(0.5f, 0.8f, q));
                return HandsDelta.Mirrored(new Vector2(4f * f, 9f * f), -0.08f * f);
            },
            Mouth = [new MouthKey(0f, "smile", 0.15f), new MouthKey(1.6f, "beam", 0.2f)],

            // Down on each dip and up between them.
            Eyes = [new EyeKey(0.1f, "down", 0.15f), new EyeKey(0.55f, "open", 0.15f),
                new EyeKey(1.05f, "down", 0.15f), new EyeKey(1.6f, "happy", 0.2f)],

            // A small bow of the whole creature on each dip.
            Morph = p =>
            {
                var q = Frac(MathF.Min(0.999f, p) * 2f);
                var f = SS(0f, 0.25f, q) * (1f - SS(0.5f, 0.8f, q));
                return M(lift: 2.4f * f, squash: 0.014f * f, ripple: 0.3f * f);
            },

            Parts = "happy",
            Glyph = "heart",
        },
        new EmoteDef
        {
            // Small rise first, then the long formal fold; distinct from bow's single ease.
            Key = "easternbow", Name = "Eastern Bow", GameEmote = "/ebow", Seconds = 2.2f,
            Pose = p =>
            {
                if (p < 0.12f) return Delta(oy: -4f * Arc(p / 0.12f));
                var fold = SS(0.12f, 0.34f, p) * (1f - SS(0.78f, 0.96f, p));
                return Delta(sx: 1f + 0.1f * fold, sy: 1f - 0.26f * fold);
            },
            // Folded in front and held there for the whole bow. Formality is stillness, so this
            // is the one track in the set with no oscillation of any kind in it.
            Hands = p =>
            {
                if (p < 0.12f) return HandsDelta.Mirrored(new Vector2(2f, -3f) * Arc(p / 0.12f));
                var fold = SS(0.12f, 0.34f, p) * (1f - SS(0.78f, 0.96f, p));
                return HandsDelta.Mirrored(new Vector2(-3f * fold, 11f * fold), -HandsDelta.MaxTilt * fold);
            },
            Mouth = [new MouthKey(0f, "flat", 0.2f), new MouthKey(1.9f, "smile", 0.3f)],

            // Lowered for the whole bow. Looking up mid-bow is what makes one insolent.
            Eyes = [new EyeKey(0.2f, "downcast", 0.25f), new EyeKey(1.7f, "open", 0.3f)],

            // Deeper and slower than the western one, and it holds.
            Morph = p =>
            {
                var f = Hold(0.18f, 0.82f, p);
                return M(lift: 3.6f * f, rate: -0.30f * f, squash: 0.016f * f, withdraw: 0.15f * f);
            },

            Parts = "content",
        },

        // ------------------------------------------------------------------ performance
        new EmoteDef
        {
            // Footwork squashes, one coin-turn leap, stuck landing. Coin-turn = the spin's
            // own edge-on squeeze; tools down so nothing teleports between hands.
            Key = "breaking", Name = "Breaking", GameEmote = "/breakdance", Seconds = 2.8f, StowArms = true,
            Pose = p =>
            {
                if (p < 0.55f)
                {
                    var r = Arc(Frac(p / 0.55f * 3f));
                    return Delta(ox: 8f * MathF.Sin(2f * MathF.PI * 3f * p), sx: 1f + 0.16f * r, sy: 1f - 0.2f * r);
                }
                if (p < 0.9f)
                {
                    var q = (p - 0.55f) / 0.35f;
                    var turn = MathF.Cos(2f * MathF.PI * q);
                    var sx = MathF.Max(0.07f, MathF.Abs(turn));
                    return Delta(oy: -20f * Arc(q), sx: sx);
                }
                var s = Arc((p - 0.9f) / 0.1f);
                return Delta(sx: 1f + 0.12f * s, sy: 1f - 0.14f * s);
            },
            // Counterweights through the footwork, spread wide for the turn, and struck on the
            // landing. The tools are away for this one, so these are bare hands.
            Hands = p =>
            {
                if (p < 0.55f)
                {
                    var s = MathF.Sin((2f * MathF.PI * 3f * p) - (MathF.PI * 0.5f));
                    return HandsDelta.Swung(-10f * s, -4f);
                }
                if (p < 0.9f)
                {
                    var q = Arc((p - 0.55f) / 0.35f);
                    return HandsDelta.Mirrored(new Vector2(16f * q, -12f * q));
                }
                var stuck = Arc((p - 0.9f) / 0.1f);
                return HandsDelta.Mirrored(new Vector2(13f * stuck, -6f * stuck), HandsDelta.MaxTilt * stuck);
            },
            Mouth = [new MouthKey(0f, "grin", 0.1f), new MouthKey(2.5f, "laugh", 0.2f)],

            // Smeared through the footwork.
            Morph = p => M(blur: 0.5f * Hold(0.1f, 0.9f, p)),

            // Focused: half lidded and not looking at you.
            Eyes = [new EyeKey(0.2f, "half", 0.2f), new EyeKey(0.9f, "away", 0.3f), new EyeKey(2.5f, "happy", 0.25f)],

            Parts = "happy",
            Glyph = "note",
        },
        new EmoteDef
        {
            // Four corners of a box, one beat each: every leg is an eased travel to the next
            // corner with a small lift over it, never a direct table index, which teleports.
            Key = "boxstep", Name = "Box Step", GameEmote = "/boxstep", Seconds = 2.8f,
            Pose = p =>
            {
                var w = Hold(0.1f, 0.9f, p);
                var leg = Frac(p * 2f) * 4f;
                var i = Math.Min(3, (int)leg);
                var t = leg - i;

                // The box is in X and in depth; depth reads as height on a side-on creature.
                float[] cx = [-10f, 10f, 10f, -10f];
                float[] cy = [0f, 0f, -7f, -7f];
                var j = (i + 1) & 3;
                var x = cx[i] + ((cx[j] - cx[i]) * SS(0f, 1f, t));
                var y = cy[i] + ((cy[j] - cy[i]) * SS(0f, 1f, t));

                var lift = Arc(t);
                return Delta(ox: x * w, oy: (y - (4f * lift)) * w, sy: 1f + (0.05f * lift * w));
            },
            // Held out in a frame, lifting a touch on each corner: a box step is danced WITH
            // somebody, and the arms are where that shows even when nobody is there.
            Hands = p =>
            {
                var w = Hold(0.1f, 0.9f, p);
                var beat = Arc(Frac(Frac(p * 2f) * 4f));
                return HandsDelta.Mirrored(new Vector2((9f + (2f * beat)) * w, (-5f - (3f * beat)) * w));
            },
            Mouth = [new MouthKey(0f, "smile", 0.15f)],

            // Tipping into each corner of the box, on the step's own beat.
            Morph = p =>
            {
                var w = Hold(0.12f, 0.88f, p);
                return M(tip: 0.40f * MathF.Sin(2f * MathF.PI * 2f * p) * w, ripple: 0.35f * w, rate: 0.20f * w);
            },

            // Pleased with itself for the whole box.
            Eyes = [new EyeKey(0.2f, "happy", 0.2f), new EyeKey(2.55f, "open", 0.25f)],

            Parts = "happy",
            Glyph = "note",
        },
        new EmoteDef
        {
            // Snappy lateral hops (square-rooted sine = quick cut, slow return).
            Key = "sidestep", Name = "Side Step", GameEmote = "/sidestep", Seconds = 2.4f,
            Pose = p =>
            {
                var w = Hold(0.1f, 0.9f, p);
                var s = MathF.Sin(2f * MathF.PI * 1.5f * p);
                return Delta(ox: 14f * MathF.Sign(s) * MathF.Sqrt(MathF.Abs(s)) * w, sy: 1f - 0.04f * MathF.Abs(s) * w);
            },
            // Thrown the other way to the hop, in world space: arms are the counterweight a
            // quick lateral cut needs, and matching the body would read as sliding rather than
            // stepping.
            Hands = p =>
            {
                var w = Hold(0.1f, 0.9f, p);
                var s = MathF.Sin(2f * MathF.PI * 1.5f * p);
                var snap = MathF.Sign(s) * MathF.Sqrt(MathF.Abs(s));
                return HandsDelta.Swung(-9f * snap * w, -3f * w);
            },
            Mouth = [new MouthKey(0f, "smile", 0.15f), new MouthKey(2.0f, "grin", 0.2f)],

            // The same idea at half the width and twice the wit.
            Morph = p =>
            {
                var w = Hold(0.12f, 0.88f, p);
                return M(tip: 0.30f * MathF.Sin(2f * MathF.PI * 1.5f * p) * w, ripple: 0.25f * w);
            },

            // Arcs, and a glance the way it is going.
            Eyes = [new EyeKey(0.15f, "away", 0.2f), new EyeKey(0.8f, "happy", 0.2f), new EyeKey(2.15f, "open", 0.25f)],

            Parts = "happy",
        },
        new EmoteDef
        {
            // The mouth does the singing; drifting motes are the closest note the pool has.
            Key = "singalong", Name = "Sing Along", GameEmote = "/sing", Seconds = 3.0f,
            Pose = p =>
            {
                var w = Hold(0.12f, 0.9f, p);
                return Delta(ox: 8f * MathF.Sin(2f * MathF.PI * 1.1f * p) * w, oy: -3f * w);
            },
            // One hand up and open while the other keeps time low: singing is a thing done at
            // somebody, and a matched pair would turn a performance into a stretch.
            Hands = p =>
            {
                var w = Hold(0.12f, 0.9f, p);
                var sway = MathF.Sin(2f * MathF.PI * 1.1f * p);
                return new HandsDelta
                {
                    Right = new Vector2((11f + (2f * sway)) * w, -19f * w),
                    Left = new Vector2(5f * w, (2f + sway) * w),
                    RightTilt = 0.07f * w,
                };
            },
            Mouth =
            [
                new MouthKey(0f, "ah", 0.15f), new MouthKey(0.8f, "o", 0.15f),
                new MouthKey(1.5f, "laugh", 0.15f), new MouthKey(2.2f, "ah", 0.15f),
                new MouthKey(2.8f, "smile", 0.2f),
            ],

            // Arcs, and a look up on the long note.
            Eyes = [new EyeKey(0.2f, "happy", 0.25f), new EyeKey(1.4f, "up", 0.3f), new EyeKey(2.6f, "happy", 0.3f)],

            // Swaying with the tune and lit by it; the long note is where the ripple opens out.
            Morph = p =>
            {
                var w = Hold(0.15f, 0.85f, p);
                return M(tip: 0.30f * MathF.Sin(2f * MathF.PI * 0.8f * p) * w,
                    ripple: 0.5f * w, glow: 0.14f * w, rate: 0.15f * w);
            },

            Parts = "happy",
            Glyph = "note",
        },
        new EmoteDef
        {
            Key = "hum", Name = "Hum", GameEmote = "/hum", Seconds = 2.6f,
            Pose = p =>
            {
                var w = Hold(0.15f, 0.9f, p);
                return Delta(ox: 4f * MathF.Sin(2f * MathF.PI * 0.8f * p) * w);
            },
            // Barely anything, trailing the body: a hum is the quietest thing in the set and
            // the arms have to stay quieter than it.
            Hands = p =>
            {
                var w = Hold(0.15f, 0.9f, p);
                var drift = MathF.Sin((2f * MathF.PI * 0.8f * p) - (MathF.PI * 0.5f));
                return HandsDelta.Swung(3f * drift * w, 3f * w);
            },
            Mouth = [new MouthKey(0f, "smile", 0.25f)],

            // Shut and content: humming is a thing done with the eyes closed.
            Eyes = [new EyeKey(0.3f, "happy", 0.3f), new EyeKey(2.4f, "open", 0.3f)],

            // Calm: slower than rest, tipping gently, content rather than performing.
            Morph = p =>
            {
                var w = Hold(0.2f, 0.85f, p);
                return M(tip: 0.22f * MathF.Sin(2f * MathF.PI * 0.5f * p) * w,
                    rate: -0.18f * w, glow: 0.08f * w, ripple: 0.25f * w);
            },

            Parts = "content",
            Glyph = "note",
        },
        new EmoteDef
        {
            // Five decaying slams; the headbang is all squash.
            Key = "headbang", Name = "Headbang", GameEmote = "/headbang", Seconds = 2.2f,
            Pose = p =>
            {
                if (p >= 0.88f) return Delta();
                var u = p / 0.88f;
                var r = Arc(Frac(u * 5f));
                var decay = 1f - 0.3f * u;
                return Delta(sx: 1f + 0.1f * r * decay, sy: 1f - 0.18f * r * decay);
            },
            // Held up and driven down on every slam. Faded in and out rather than cut, because
            // a raised pair dropped in one frame pops where a fist at rest would not.
            Hands = p =>
            {
                var w = SS(0f, 0.08f, p) * (1f - SS(0.82f, 1f, p));
                var r = Arc(Frac(MathF.Min(1f, p / 0.88f) * 5f)) * (1f - (0.3f * p));
                return HandsDelta.Mirrored(new Vector2(11f * w, (-7f + (16f * r)) * w));
            },
            Mouth = [new MouthKey(0f, "grin", 0.08f), new MouthKey(1.9f, "laugh", 0.2f)],

            // Fast enough to smear, which is most of what makes it read as headbanging.
            Morph = p => M(blur: 0.55f * Hold(0.1f, 0.88f, p), tremble: 0.25f * Hold(0.1f, 0.88f, p)),

            // Shut. Nobody headbangs with their eyes open.
            Eyes = [new EyeKey(0.12f, "shut", 0.1f), new EyeKey(1.95f, "open", 0.2f)],

            Parts = "happy",
            Glyph = "note",
        },
        new EmoteDef
        {
            // Two coin-turns in one arc; tools down for the same reason as breaking.
            Key = "spin", Name = "Spin", GameEmote = "/spin", Seconds = 2.0f, StowArms = true,
            Pose = p =>
            {
                if (p < 0.85f)
                {
                    var q = p / 0.85f;
                    var turn = MathF.Cos(2f * MathF.PI * 2f * q);
                    return Delta(oy: -22f * Arc(q), sx: MathF.Max(0.07f, MathF.Abs(turn)));
                }
                var s = Arc((p - 0.85f) / 0.15f);
                return Delta(sx: 1f + 0.1f * s, sy: 1f - 0.12f * s);
            },
            // Flung wide through the turns and tucked on the landing, which is what spinning
            // does to arms and also what stops them being flung when it ends. Tools are away.
            Hands = p =>
            {
                if (p < 0.85f)
                {
                    var q = Arc(p / 0.85f);
                    return HandsDelta.Mirrored(new Vector2(18f * q, -8f * q));
                }
                var land = Arc((p - 0.85f) / 0.15f);
                return HandsDelta.Mirrored(new Vector2(6f * land, 4f * land));
            },
            Mouth = [new MouthKey(0f, "grin", 0.1f), new MouthKey(1.7f, "smile", 0.2f)],

            // A whole creature turning is the best case the ghost has.
            Morph = p => M(blur: 0.6f * Hold(0.08f, 0.9f, p)),

            // Shut through the turn and a little unfocused coming out of it.
            Eyes = [new EyeKey(0.1f, "shut", 0.08f), new EyeKey(1.5f, "half", 0.18f), new EyeKey(1.8f, "open", 0.2f)],

            Parts = "happy",
        },
        new EmoteDef
        {
            // The idle's opposite number: a long, contented metronome.
            Key = "sway", Name = "Sway", GameEmote = "/sway", Seconds = 3.2f,
            Pose = p =>
            {
                var w = Hold(0.15f, 0.9f, p);
                return Delta(ox: 9f * MathF.Sin(2f * MathF.PI * 0.55f * p) * w, sy: 1f - 0.02f * w);
            },
            // Trailing the metronome by a quarter cycle. The whole emote is contentment, so the
            // arms are carried rather than swung.
            Hands = p =>
            {
                var w = Hold(0.15f, 0.9f, p);
                var lag = MathF.Sin((2f * MathF.PI * 0.55f * p) - (MathF.PI * 0.5f));
                return HandsDelta.Swung(7f * lag * w, -2f * w);
            },
            Mouth = [new MouthKey(0f, "smile", 0.3f)],

            // The dial this emote is named after: it sways the creature rather than sliding
            // the picture of it.
            Morph = p =>
            {
                var w = Hold(0.15f, 0.85f, p);
                return M(tip: 0.55f * MathF.Sin(2f * MathF.PI * 0.6f * p) * w,
                    ripple: 0.4f * w, rate: -0.10f * w);
            },

            // Half lidded and drifting: swaying is a thing done with the eyes nearly shut.
            Eyes = [new EyeKey(0.3f, "half", 0.35f), new EyeKey(2.9f, "open", 0.3f)],

            Parts = "content",
            Glyph = "note",
        },

        // ------------------------------------------------------------------ states
        new EmoteDef
        {
            // Settle low and breathe there.
            Key = "sit", Name = "Sit", GameEmote = "/sit", Seconds = 3.0f,
            Pose = p =>
            {
                var f = Hold(0.2f, 0.92f, p);
                return Delta(sx: 1f + 0.12f * f, sy: 1f - 0.16f * f + 0.01f * MathF.Sin(2f * MathF.PI * 0.5f * p) * f);
            },
            // Settled into the lap and breathing with the body. A sitting thing still moves,
            // just barely, and this is the whole of it.
            Hands = p =>
            {
                var f = Hold(0.2f, 0.92f, p);
                var breath = MathF.Sin(2f * MathF.PI * 0.5f * p);
                return HandsDelta.Mirrored(new Vector2(5f * f, (8f + breath) * f));
            },
            Mouth = [new MouthKey(0f, "flat", 0.25f), new MouthKey(1.5f, "smile", 0.4f)],

            // Half lidded and looking down, which is a creature settling rather than posing.
            Eyes = [new EyeKey(0.4f, "downcast", 0.4f), new EyeKey(2.2f, "half", 0.35f)],

            // Settling, and the whole creature slowing down with it.
            Morph = p =>
            {
                var f = Hold(0.25f, 0.9f, p);
                return M(lift: 3.0f * f, rate: -0.30f * f, squash: 0.016f * f, withdraw: 0.20f * f);
            },

            Parts = "content",
        },
        new EmoteDef
        {
            // 9 Hz tremble, hunched; falling flakes are garnish (Flake).
            Key = "shiver", Name = "Shiver", GameEmote = "/shiver", Seconds = 2.2f,
            Pose = p =>
            {
                var w = Hold(0.1f, 0.9f, p);
                return Delta(ox: 2.5f * MathF.Sin(2f * MathF.PI * 9f * p) * w, sx: 1f + 0.03f * w, sy: 1f - 0.04f * w);
            },
            // Clamped in against itself for warmth and trembling on the body's own 9 Hz. The
            // third and last inboard track in the set, and the reason is the same as the hug's.
            Hands = p =>
            {
                var w = Hold(0.1f, 0.9f, p);
                var tremble = MathF.Sin(2f * MathF.PI * 9f * p);
                return HandsDelta.Mirrored(new Vector2((-4f + tremble) * w, (2f + tremble) * w));
            },
            Mouth = [new MouthKey(0f, "quiver", 0.15f), new MouthKey(1.8f, "flat", 0.2f)],

            // The body track moves the creature; tremble is the creature itself shaking.
            Morph = p =>
            {
                var w = Hold(0.1f, 0.9f, p);
                return M(tremble: 0.75f * w, withdraw: 0.15f * w, bristle: 0.30f * w, rate: 0.25f * w);
            },

            // Squeezed against the cold.
            Eyes = [new EyeKey(0.1f, "squint", 0.2f), new EyeKey(1.8f, "open", 0.25f)],

            Parts = "alert",
            Glyph = "snowflake",
        },
        new EmoteDef
        {
            // Wilted pant; brow droplets are garnish (Droplet).
            Key = "swelter", Name = "Swelter", GameEmote = "/swelter", Seconds = 2.6f,
            Pose = p =>
            {
                var f = Hold(0.2f, 0.9f, p);
                var pant = 0.03f * MathF.Abs(MathF.Sin(2f * MathF.PI * 1.6f * p));
                return Delta(sx: 1f + 0.07f * f, sy: 1f - (0.1f + pant) * f);
            },
            // One hand fanning weakly on the pant while the other hangs: a wilted creature does
            // not have the energy for two, and the asymmetry is what makes it look spent.
            Hands = p =>
            {
                var f = Hold(0.2f, 0.9f, p);
                var pant = MathF.Abs(MathF.Sin(2f * MathF.PI * 1.6f * p));
                return new HandsDelta
                {
                    Right = new Vector2((7f + (3f * pant)) * f, (6f - (4f * pant)) * f),
                    Left = new Vector2(3f * f, 10f * f),
                };
            },
            Mouth = [new MouthKey(0f, "ah", 0.3f), new MouthKey(2.1f, "flat", 0.3f)],

            // Heavy rather than shut: too hot to keep them open, not tired enough to close.
            Eyes =
            [
                new EyeKey(0.2f, "half", 0.3f),
                new EyeKey(1.2f, "heavy", 0.4f),
                new EyeKey(2.1f, "threeq", 0.3f),
            ],

            // Too hot: sinking where it stands, going red, and the lit shells glowing with it.
            Morph = p =>
            {
                var f = Hold(0.2f, 0.85f, p);
                return M(lift: 3.6f * f, glow: 0.22f * f, blush: 0.55f * f, withdraw: 0.20f * f, rate: -0.35f * f, ripple: -0.25f * f);
            },

            Parts = "sleepy",
            Glyph = "flame",
        },
        new EmoteDef
        {
            // Inhale-inhale-SNAP: the whole clip is the buildup and release.
            Key = "sneeze", Name = "Sneeze", GameEmote = "/sneeze", Seconds = 1.9f,
            Pose = p =>
            {
                var inh = Hold(0.3f, 0.42f, p);
                var snap = p < 0.48f ? 0f : p < 0.56f ? (p - 0.48f) / 0.08f : 1f - SS(0.75f, 1f, p);
                return Delta(ox: 7f * snap, sx: 1f - 0.05f * inh + 0.12f * snap, sy: 1f + 0.09f * inh - 0.16f * snap);
            },
            // Up to the face on the build, thrown down and out on the release: the covering is
            // the manners, and the throw is the sneeze.
            Hands = p =>
            {
                var inh = Hold(0.3f, 0.42f, p);
                var snap = p < 0.48f ? 0f : p < 0.56f ? (p - 0.48f) / 0.08f : 1f - SS(0.75f, 1f, p);
                return HandsDelta.Mirrored(new Vector2((5f * inh) + (10f * snap), (-15f * inh) + (9f * snap)));
            },
            Mouth =
            [
                new MouthKey(0f, "flat", 0.15f), new MouthKey(0.3f, "ah", 0.2f),
                new MouthKey(0.95f, "o", 0.1f), new MouthKey(1.5f, "flat", 0.25f),
            ],

            // Lids fall through the inhale and slam on the snap.
            Eyes =
            [
                new EyeKey(0.3f, "threeq", 0.15f),
                new EyeKey(0.46f, "half", 0.06f),
                new EyeKey(0.5f, "shut", 0.04f),
                new EyeKey(0.72f, "threeq", 0.12f),
                new EyeKey(1f, "open", 0.15f),
            ],

            // The snap is one frame too fast to follow, which is exactly what Blur is for.
            Morph = p =>
            {
                var snap = p < 0.48f ? 0f : p < 0.56f ? (p - 0.48f) / 0.08f : 1f - SS(0.75f, 1f, p);
                return M(blur: 0.7f * snap, tremble: 0.4f * snap, bristle: 0.5f * snap);
            },

            Parts = "alert",
        },
        new EmoteDef
        {
            // Three involuntary pops on an otherwise still body.
            Key = "hiccup", Name = "Hiccup", GameEmote = "/hiccup", Seconds = 2.2f,
            Pose = p =>
            {
                foreach (var b in (ReadOnlySpan<float>)[0.068f, 0.25f, 0.4545f])
                {
                    var q = (p - b) / 0.09f;
                    if (q is >= 0f and <= 1f) return Delta(oy: -8f * Arc(q), sy: 1f + 0.06f * Arc(q));
                }
                return Delta();
            },
            // A small jolt on each pop and nothing at all between them. Involuntary means the
            // arms are surprised too, so they never anticipate the next one.
            Hands = p =>
            {
                foreach (var b in (ReadOnlySpan<float>)[0.068f, 0.25f, 0.4545f])
                {
                    var q = (p - b) / 0.09f;
                    if (q is >= 0f and <= 1f) return HandsDelta.Mirrored(new Vector2(4f, -9f) * Arc(q));
                }
                return HandsDelta.None;
            },
            Mouth =
            [
                new MouthKey(0f, "flat", 0.2f), new MouthKey(0.33f, "o", 0.05f),
                new MouthKey(0.55f, "flat", 0.2f), new MouthKey(1.21f, "o", 0.05f),
                new MouthKey(1.45f, "flat", 0.2f), new MouthKey(2.0f, "eh", 0.2f),
            ],

            // One jolt, and everything that can jolt does.
            Morph = p =>
            {
                var jolt = p < 0.30f ? 0f : p < 0.36f ? (p - 0.30f) / 0.06f : 1f - SS(0.36f, 0.6f, p);
                return M(tremble: 0.8f * jolt, blur: 0.45f * jolt, squash: 0.020f * jolt, bristle: 0.35f * jolt, rate: 0.3f * jolt);
            },

            // Wide on the jolt and only slowly convinced it is over.
            Eyes = [new EyeKey(0.3f, "wide", 0.04f), new EyeKey(0.6f, "threeq", 0.2f), new EyeKey(1.6f, "open", 0.25f)],

            Parts = "alert",
        },
        new EmoteDef
        {
            // Lurch left, overcorrect right, settle; three eases, no rhythm on purpose.
            Key = "stagger", Name = "Stagger", GameEmote = "/stagger", Seconds = 2.4f,
            Pose = p =>
            {
                var ox = -14f * Hold(0.1f, 0.3f, p)
                    + 11f * SS(0.32f, 0.45f, p) * (1f - SS(0.6f, 0.75f, p))
                    - 7f * SS(0.62f, 0.75f, p) * (1f - SS(0.85f, 1f, p));
                return Delta(ox: ox, sy: 1f - 0.05f * Hold(0.1f, 0.9f, p));
            },
            // Thrown the opposite way to every lurch, off the body's own curve: arms flung out
            // for balance are the difference between staggering and being dragged sideways.
            Hands = p =>
            {
                var ox = (-14f * Hold(0.1f, 0.3f, p))
                    + (11f * SS(0.32f, 0.45f, p) * (1f - SS(0.6f, 0.75f, p)))
                    - (7f * SS(0.62f, 0.75f, p) * (1f - SS(0.85f, 1f, p)));
                return HandsDelta.Swung(-0.6f * ox, -7f * Hold(0.1f, 0.9f, p));
            },
            Mouth = [new MouthKey(0f, "eh", 0.2f), new MouthKey(2.0f, "flat", 0.25f)],

            // Unfocused and drifting, one beat behind the body.
            Eyes = [new EyeKey(0.2f, "half", 0.25f), new EyeKey(0.9f, "away", 0.35f), new EyeKey(1.8f, "threeq", 0.3f)],

            // Blurred and sagging: it is not in control of where it is going.
            Morph = p =>
            {
                var f = Hold(0.12f, 0.88f, p);
                return M(blur: 0.35f * f, lift: 2.2f * f, withdraw: 0.15f * f, tip: 0.5f * MathF.Sin(2f * MathF.PI * 0.9f * p) * f, rate: -0.2f * f);
            },

            Parts = "sleepy",
            Glyph = "swirl",
        },
        new EmoteDef
        {
            // Slow figure-eight wobble; orbiting sparkles are garnish.
            Key = "dizzy", Name = "Dizzy", GameEmote = "/dizzy", Seconds = 2.8f,
            Pose = p =>
            {
                var w = Hold(0.12f, 0.9f, p);
                return Delta(
                    ox: 11f * MathF.Sin(2f * MathF.PI * 1.2f * p) * w,
                    sy: 1f + 0.04f * MathF.Sin(2f * MathF.PI * 2.4f * p) * w);
            },
            // Swimming a quarter cycle behind the wobble, which is what makes the creature look
            // like it is being moved rather than moving.
            Hands = p =>
            {
                var w = Hold(0.12f, 0.9f, p);
                var lag = MathF.Sin((2f * MathF.PI * 1.2f * p) - (MathF.PI * 0.5f));
                return HandsDelta.Swung(8f * lag * w, -4f * w);
            },
            Mouth = [new MouthKey(0f, "eh", 0.3f)],

            // The figure-eight is fast enough to smear on the shells that can.
            Morph = p => M(blur: 0.45f * Hold(0.12f, 0.9f, p), tip: 0.45f * MathF.Sin(2f * MathF.PI * 1.2f * p) * Hold(0.12f, 0.9f, p)),

            // Unfocused rather than closed, and it never quite recovers inside the clip.
            Eyes = [new EyeKey(0.2f, "half", 0.3f), new EyeKey(2.4f, "threeq", 0.3f)],

            Parts = "sleepy",
            Glyph = "swirl",
        },

        // ------------------------------------------------------------------ reactions II
        new EmoteDef
        {
            // The "dunno" lift-and-wobble.
            Key = "shrug", Name = "Shrug", GameEmote = "/shrug", Seconds = 1.8f,
            Pose = p =>
            {
                var f = Hold(0.15f, 0.85f, p);
                return Delta(ox: 7f * MathF.Sin(2f * MathF.PI * 1.3f * p) * f, sx: 1f - 0.04f * f, sy: 1f + 0.07f * f);
            },
            // Palms out, matched, held: everywhere else in the set a symmetric pair is the
            // failure mode that reads as a shrug. This is the one emote where that IS the read.
            Hands = p =>
            {
                var f = Hold(0.15f, 0.85f, p);
                var wobble = MathF.Sin(2f * MathF.PI * 1.3f * p);
                return HandsDelta.Mirrored(new Vector2((14f + wobble) * f, -7f * f), HandsDelta.MaxTilt * f);
            },
            Mouth = [new MouthKey(0f, "eh", 0.15f), new MouthKey(1.4f, "flat", 0.2f)],

            // Up and away, which is what "I have no idea" looks like on a face.
            Eyes = [new EyeKey(0.2f, "away", 0.2f), new EyeKey(0.7f, "up", 0.25f), new EyeKey(1.5f, "open", 0.25f)],

            // The shoulders go up and the creature goes with them, tipping into the
            // "who knows" rather than only lifting.
            Morph = p =>
            {
                var f = Hold(0.15f, 0.85f, p);
                return M(squash: -0.012f * f, tip: 0.25f * MathF.Sin(2f * MathF.PI * 1.3f * p) * f);
            },

            Parts = "curious",
            Glyph = "query",
        },
        new EmoteDef
        {
            // Clenched, quivering effort held for the duration.
            Key = "endure", Name = "Endure", GameEmote = "/endure", Seconds = 2.2f,
            Pose = p =>
            {
                var f = Hold(0.15f, 0.9f, p);
                var cl = 0.02f * MathF.Sin(2f * MathF.PI * 6f * p);
                return Delta(sx: 1f + (0.06f + cl) * f, sy: 1f - (0.08f + cl) * f);
            },
            // Fists in tight and shaking on the body's own clench rate. Effort is a thing held,
            // so nothing here travels: it only vibrates.
            Hands = p =>
            {
                var f = Hold(0.15f, 0.9f, p);
                var clench = MathF.Sin(2f * MathF.PI * 6f * p);
                return HandsDelta.Mirrored(new Vector2((5f + clench) * f, (3f + clench) * f));
            },
            Mouth = [new MouthKey(0f, "quiver", 0.2f), new MouthKey(1.8f, "flat", 0.25f)],

            // Clenched with the rest of it, and released on the same beat the mouth is.
            Eyes = [new EyeKey(0.15f, "squint", 0.15f), new EyeKey(1.8f, "open", 0.25f)],

            // The clench, on the body itself. Held effort is a shape a creature holds.
            Morph = p =>
            {
                var f = Hold(0.15f, 0.9f, p);
                return M(squash: (0.018f + (0.006f * MathF.Sin(2f * MathF.PI * 6f * p))) * f,
                    blush: 0.30f * f, tremble: 0.20f * f, bristle: 0.25f * f);
            },

            Parts = "alert",
        },
        new EmoteDef
        {
            // Two forward jabs.
            Key = "poke", Name = "Poke", GameEmote = "/poke", Seconds = 1.5f,
            Pose = p =>
            {
                foreach (var b in (ReadOnlySpan<float>)[0.12f, 0.5f])
                {
                    var q = (p - b) / 0.22f;
                    if (q is >= 0f and <= 1f) return Delta(ox: 22f * Arc(q), sx: 1f + 0.06f * Arc(q));
                }
                return Delta();
            },
            // The most hand-shaped emote in the set: one arm does the jabbing and the body
            // merely goes with it, which is the right way round for a poke.
            Hands = p =>
            {
                foreach (var b in (ReadOnlySpan<float>)[0.12f, 0.5f])
                {
                    var q = (p - b) / 0.22f;
                    if (q is >= 0f and <= 1f)
                    {
                        var jab = Arc(q);
                        return new HandsDelta
                        {
                            Right = new Vector2(24f * jab, -4f * jab),
                            RightTilt = HandsDelta.MaxTilt * jab,
                        };
                    }
                }
                return HandsDelta.None;
            },
            Mouth = [new MouthKey(0f, "o", 0.1f), new MouthKey(1.1f, "smile", 0.2f)],

            // At the thing it is poking. A poke with the eyes front is a jab at nothing.
            Eyes = [new EyeKey(0.1f, "down", 0.14f), new EyeKey(1.0f, "open", 0.2f)],

            // A lean toward the thing being poked, and a beat of stillness before it.
            Morph = p =>
            {
                var reach = SS(0.1f, 0.35f, p) * (1f - SS(0.6f, 0.9f, p));
                return M(tip: 0.35f * reach, rate: -0.15f * reach, squash: 0.008f * reach);
            },

            Parts = "curious",
        },
        new EmoteDef
        {
            // The look-down tilt, swaying to check both sides.
            Key = "examineself", Name = "Examine Self", GameEmote = "/examineself", Seconds = 2.4f,
            Pose = p =>
            {
                var f = Hold(0.2f, 0.88f, p);
                return Delta(ox: 5f * MathF.Sin(2f * MathF.PI * 0.8f * p) * f, sx: 1f + 0.05f * f, sy: 1f - 0.09f * f);
            },
            // One hand held out and turned over with the sway, the other resting: looking at
            // yourself is done with a hand, and doing it with both would be a plea.
            Hands = p =>
            {
                var f = Hold(0.2f, 0.88f, p);
                var turn = MathF.Sin(2f * MathF.PI * 0.8f * p);
                return new HandsDelta
                {
                    Right = new Vector2((9f + (3f * turn)) * f, (4f + (2f * turn)) * f),
                    Left = new Vector2(4f * f, 6f * f),
                    RightTilt = 0.06f * turn * f,
                };
            },
            Mouth = [new MouthKey(0f, "hmm", 0.3f), new MouthKey(1.9f, "smile", 0.3f)],

            // Looks at itself, which is the emote.
            Eyes = [new EyeKey(0.25f, "down", 0.3f), new EyeKey(2.05f, "open", 0.25f)],

            // Turning to check each side, slowly.
            Morph = p =>
            {
                var f = Hold(0.2f, 0.88f, p);
                return M(tip: 0.35f * MathF.Sin(2f * MathF.PI * 0.8f * p) * f, rate: -0.20f * f);
            },

            Parts = "curious",
            Glyph = "query",
        },
    ];
}
