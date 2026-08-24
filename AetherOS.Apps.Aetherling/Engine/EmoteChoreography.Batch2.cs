// Batch 2 emote choreographies — the canvas-approved parity push (Emote Canvas, Aug 2026).
// Same grammar as EmoteChoreographies (EmoteStudy §9.2): pure functions of p, ground line
// sacred, excursions inside the hop envelope, no flips on a dressed pet. Body + mouth only —
// none of these is *about* a limb (§11 gate 2); particle garnish lives with the other
// garnish in AetherlingApp.EmoteGarnish (see repo-drop README-EMOTES.md for the cases).
//
// Skipped on purpose: laugh/shocked/doubt/think/sulk (already shipped in the §11 batch),
// nod-yes and no (shipped as nod and shake). Parked: the ten food/drink emotes and
// lightsticks — they want a held-prop pass on the arms pipeline before they can land.
namespace AetherOS.Apps.Aetherling.Engine;

using System;
using System.Collections.Generic;
using System.Numerics;

public static class EmoteChoreographiesBatch2
{
    private static float Arc(float q) => MathF.Sin(MathF.PI * Math.Clamp(q, 0f, 1f));

    private static float Frac(float v) => v - MathF.Floor(v);

    private static float SS(float e0, float e1, float t)
    {
        var x = Math.Clamp((t - e0) / (e1 - e0), 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    /// <summary>Ease in over [0,a], hold, ease out over [b,1] — the standard clip envelope.</summary>
    private static float Hold(float a, float b, float p) => SS(0f, a, p) * (1f - SS(b, 1f, p));

    private static EmotePoseDelta Delta(float ox = 0f, float oy = 0f, float sx = 1f, float sy = 1f) =>
        new() { Offset = new Vector2(ox, oy), ScaleMul = new Vector2(sx, sy) };

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
            Mouth = [new MouthKey(0f, "grin", 0.12f), new MouthKey(1.5f, "laugh", 0.15f), new MouthKey(2.9f, "smile", 0.25f)],
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
            Mouth = [new MouthKey(0f, "laugh", 0.08f), new MouthKey(1.2f, "grin", 0.2f)],
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
            Mouth = [new MouthKey(0f, "grin", 0.1f)],
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
            Mouth = [new MouthKey(0f, "grin", 0.1f), new MouthKey(1.2f, "laugh", 0.12f), new MouthKey(2.0f, "grin", 0.15f)],
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
            Mouth = [new MouthKey(0f, "beam", 0.2f)],
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
            Mouth = [new MouthKey(0f, "smile", 0.12f), new MouthKey(0.25f, "o", 0.12f), new MouthKey(0.7f, "grin", 0.2f)],
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
            Mouth = [new MouthKey(0f, "pout", 0.15f), new MouthKey(1.4f, "smile", 0.3f)],
        },

        // ------------------------------------------------------------------ distress
        new EmoteDef
        {
            // 7 Hz dither with a slight lift — nothing else in the set uses this register.
            Key = "panic", Name = "Panic", GameEmote = "/panic", Seconds = 1.6f,
            Pose = p =>
            {
                var w = 1f - SS(0.82f, 1f, p);
                return Delta(ox: 6f * MathF.Sin(2f * MathF.PI * 7f * p) * w, oy: -3f * w, sx: 1f + 0.05f * w, sy: 1f - 0.05f * w);
            },
            Mouth = [new MouthKey(0f, "gasp", 0.05f), new MouthKey(1.25f, "frown", 0.2f)],
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
            Mouth = [new MouthKey(0f, "quiver", 0.2f), new MouthKey(0.6f, "sad", 0.3f), new MouthKey(2.5f, "flat", 0.25f)],
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
            Mouth = [new MouthKey(0f, "quiver", 0.25f), new MouthKey(1.0f, "sad", 0.35f)],
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
            Mouth = [new MouthKey(0f, "gasp", 0.08f), new MouthKey(1.0f, "quiver", 0.25f)],
        },
        new EmoteDef
        {
            Key = "disappointed", Name = "Disappointed", GameEmote = "/disappointed", Seconds = 2.4f,
            Pose = p =>
            {
                var f = Hold(0.25f, 0.88f, p);
                return Delta(ox: 4f * MathF.Sin(2f * MathF.PI * 0.5f * p) * f, sx: 1f + 0.07f * f, sy: 1f - 0.11f * f);
            },
            Mouth = [new MouthKey(0f, "flat", 0.2f), new MouthKey(0.5f, "sad", 0.35f)],
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
            Mouth = [new MouthKey(0f, "frown", 0.05f), new MouthKey(2.1f, "frown", 0.2f)],
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
            Mouth = [new MouthKey(0f, "pout", 0.1f), new MouthKey(1.6f, "flat", 0.25f)],
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
            Mouth =
            [
                new MouthKey(0f, "grin", 0.1f), new MouthKey(0.5f, "laugh", 0.1f),
                new MouthKey(0.95f, "grin", 0.12f), new MouthKey(1.35f, "smile", 0.2f),
            ],
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
            Mouth = [new MouthKey(0f, "gasp", 0.05f), new MouthKey(0.9f, "eh", 0.2f), new MouthKey(1.4f, "flat", 0.2f)],
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
            Mouth = [new MouthKey(0f, "pout", 0.08f), new MouthKey(1.0f, "flat", 0.2f)],
        },
        new EmoteDef
        {
            // Lean in and bob, all smirk. (The canvas's stuck-out tongue needs a mouth
            // shape the library doesn't have yet — parked with the props.)
            Key = "deride", Name = "Deride", GameEmote = "/deride", Seconds = 2.0f,
            Pose = p =>
            {
                var f = Hold(0.18f, 0.85f, p);
                var sy = 1f;
                if (p < 0.85f) sy = 1f - 0.045f * Arc(Frac(p / 0.85f * 3f)) * f;
                return Delta(ox: 8f * f, sy: sy);
            },
            Mouth = [new MouthKey(0f, "grin", 0.1f), new MouthKey(1.7f, "smile", 0.2f)],
        },
        new EmoteDef
        {
            // Crouch, then the idea lands: pop + rise. The "!" mark wants a glyph pass.
            Key = "eureka", Name = "Eureka", GameEmote = "/eureka", Seconds = 1.9f,
            Pose = p =>
            {
                var crouch = Hold(0.18f, 0.3f, p);
                var pop = p < 0.45f ? 0f : p < 0.55f ? (p - 0.45f) / 0.1f : 1f - SS(0.85f, 1f, p);
                return Delta(oy: -16f * pop, sx: 1f + 0.08f * crouch - 0.05f * pop, sy: 1f - 0.1f * crouch + 0.1f * pop);
            },
            Mouth = [new MouthKey(0f, "hmm", 0.15f), new MouthKey(0.5f, "o", 0.06f), new MouthKey(1.0f, "beam", 0.2f)],
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
            Mouth = [new MouthKey(0f, "beam", 0.12f)],
        },
        new EmoteDef
        {
            Key = "kneel", Name = "Kneel", GameEmote = "/kneel", Seconds = 2.6f,
            Pose = p =>
            {
                var f = Hold(0.22f, 0.88f, p);
                return Delta(sx: 1f + 0.1f * f, sy: 1f - 0.18f * f);
            },
            Mouth = [new MouthKey(0f, "flat", 0.25f)],
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
            Mouth = [new MouthKey(0f, "sad", 0.25f), new MouthKey(2.4f, "flat", 0.3f)],
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
            Mouth = [new MouthKey(0f, "flat", 0.3f), new MouthKey(2.4f, "smile", 0.3f)],
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
            Mouth = [new MouthKey(0f, "smile", 0.15f), new MouthKey(1.6f, "beam", 0.2f)],
        },
        new EmoteDef
        {
            // Small rise first, then the long formal fold — distinct from bow's single ease.
            Key = "easternbow", Name = "Eastern Bow", GameEmote = "/ebow", Seconds = 2.2f,
            Pose = p =>
            {
                if (p < 0.12f) return Delta(oy: -4f * Arc(p / 0.12f));
                var fold = SS(0.12f, 0.34f, p) * (1f - SS(0.78f, 0.96f, p));
                return Delta(sx: 1f + 0.1f * fold, sy: 1f - 0.26f * fold);
            },
            Mouth = [new MouthKey(0f, "flat", 0.2f), new MouthKey(1.9f, "smile", 0.3f)],
        },

        // ------------------------------------------------------------------ performance
        new EmoteDef
        {
            // Footwork squashes, one coin-turn leap, stuck landing. Coin-turn = the spin's
            // own edge-on squeeze; tools down so nothing teleports between hands (§12).
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
            Mouth = [new MouthKey(0f, "grin", 0.1f), new MouthKey(2.5f, "laugh", 0.2f)],
        },
        new EmoteDef
        {
            // Four corners of a box, one beat each.
            Key = "boxstep", Name = "Box Step", GameEmote = "/boxstep", Seconds = 2.8f,
            Pose = p =>
            {
                var w = Hold(0.1f, 0.9f, p);
                var q = Frac(p * 2f);
                float[] cx = [-10f, 10f, 10f, -10f];
                var i = Math.Min(3, (int)(q * 4f));
                return Delta(ox: cx[i] * w, sy: 1f - 0.05f * Arc(Frac(q * 4f)) * w);
            },
            Mouth = [new MouthKey(0f, "smile", 0.15f)],
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
            Mouth = [new MouthKey(0f, "smile", 0.15f), new MouthKey(2.0f, "grin", 0.2f)],
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
            Mouth =
            [
                new MouthKey(0f, "ah", 0.15f), new MouthKey(0.8f, "o", 0.15f),
                new MouthKey(1.5f, "laugh", 0.15f), new MouthKey(2.2f, "ah", 0.15f),
                new MouthKey(2.8f, "smile", 0.2f),
            ],
        },
        new EmoteDef
        {
            Key = "hum", Name = "Hum", GameEmote = "/hum", Seconds = 2.6f,
            Pose = p =>
            {
                var w = Hold(0.15f, 0.9f, p);
                return Delta(ox: 4f * MathF.Sin(2f * MathF.PI * 0.8f * p) * w);
            },
            Mouth = [new MouthKey(0f, "smile", 0.25f)],
        },
        new EmoteDef
        {
            // Five decaying slams — the headbang is all squash.
            Key = "headbang", Name = "Headbang", GameEmote = "/headbang", Seconds = 2.2f,
            Pose = p =>
            {
                if (p >= 0.88f) return Delta();
                var u = p / 0.88f;
                var r = Arc(Frac(u * 5f));
                var decay = 1f - 0.3f * u;
                return Delta(sx: 1f + 0.1f * r * decay, sy: 1f - 0.18f * r * decay);
            },
            Mouth = [new MouthKey(0f, "grin", 0.08f), new MouthKey(1.9f, "laugh", 0.2f)],
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
            Mouth = [new MouthKey(0f, "grin", 0.1f), new MouthKey(1.7f, "smile", 0.2f)],
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
            Mouth = [new MouthKey(0f, "smile", 0.3f)],
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
            Mouth = [new MouthKey(0f, "flat", 0.25f), new MouthKey(1.5f, "smile", 0.4f)],
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
            Mouth = [new MouthKey(0f, "quiver", 0.15f), new MouthKey(1.8f, "flat", 0.2f)],
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
            Mouth = [new MouthKey(0f, "ah", 0.3f), new MouthKey(2.1f, "flat", 0.3f)],
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
            Mouth =
            [
                new MouthKey(0f, "flat", 0.15f), new MouthKey(0.3f, "ah", 0.2f),
                new MouthKey(0.95f, "o", 0.1f), new MouthKey(1.5f, "flat", 0.25f),
            ],
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
            Mouth =
            [
                new MouthKey(0f, "flat", 0.2f), new MouthKey(0.33f, "o", 0.05f),
                new MouthKey(0.55f, "flat", 0.2f), new MouthKey(1.21f, "o", 0.05f),
                new MouthKey(1.45f, "flat", 0.2f), new MouthKey(2.0f, "eh", 0.2f),
            ],
        },
        new EmoteDef
        {
            // Lurch left, overcorrect right, settle — three eases, no rhythm on purpose.
            Key = "stagger", Name = "Stagger", GameEmote = "/stagger", Seconds = 2.4f,
            Pose = p =>
            {
                var ox = -14f * Hold(0.1f, 0.3f, p)
                    + 11f * SS(0.32f, 0.45f, p) * (1f - SS(0.6f, 0.75f, p))
                    - 7f * SS(0.62f, 0.75f, p) * (1f - SS(0.85f, 1f, p));
                return Delta(ox: ox, sy: 1f - 0.05f * Hold(0.1f, 0.9f, p));
            },
            Mouth = [new MouthKey(0f, "eh", 0.2f), new MouthKey(2.0f, "flat", 0.25f)],
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
            Mouth = [new MouthKey(0f, "eh", 0.3f)],
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
            Mouth = [new MouthKey(0f, "eh", 0.15f), new MouthKey(1.4f, "flat", 0.2f)],
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
            Mouth = [new MouthKey(0f, "quiver", 0.2f), new MouthKey(1.8f, "flat", 0.25f)],
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
            Mouth = [new MouthKey(0f, "o", 0.1f), new MouthKey(1.1f, "smile", 0.2f)],
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
            Mouth = [new MouthKey(0f, "hmm", 0.3f), new MouthKey(1.9f, "smile", 0.3f)],
        },
    ];
}
