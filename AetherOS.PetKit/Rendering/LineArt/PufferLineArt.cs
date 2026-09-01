namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using static AetherOS.PetKit.Rendering.LineArt.LineShell;

/// <summary>
/// The Puffer, drawn. Third shell, and the one that broke the pose model: deliberately taken
/// third for exactly that reason.
///
/// <para><b>It does not squash.</b> Everything before it posed on <c>sx, sy, dy</c>; this one
/// INFLATES, uniformly, about the ball's own centre, and its generator is emphatic about why: "a
/// ball that squashes is a ball being sat on; this one inflates, and the difference between the
/// two is the whole character". It also carries two channels neither of the first two had: an
/// absolute <c>spike</c> length and a fin <c>flutter</c> in degrees.</para>
///
/// <para><b>The split that IS the creature.</b> The puff scales the ball; the spines' length does
/// NOT scale with it. That is what makes a boop read as bristling rather than as the picture
/// zooming, and it is why the spike length is an authored channel rather than a function of the
/// puff. The fins do not inflate either, for the same reason: a fin is not inflatable, and
/// scaling it with the body was the one thing in the first mockups that read as a zoom.</para>
///
/// <para><b>Two transforms, both LinePose.</b> The ball is <c>LinePose(k, k, dy, CX, CY)</c>:
/// squash-about-a-pivot with both axes equal IS a uniform scale, so no new pose type was needed.
/// The face and the fins ride <c>LinePose(1, 1, dy, ...)</c>, which bobs and does nothing else.
/// That the existing type covered a creature it was not designed for is the generalisation
/// paying off rather than luck: the shell was free to say what its channels MEAN.</para>
/// </summary>
public static class PufferLineArt
{
    // ---------------------------------------------------------------- geometry --
    // Lifted from the retired puffer-sheet/build_sheet.py unchanged: same names, same numbers.
    public const float Cell = 384f;

    public const float CX = 192f;
    private const float CY = 214f, R = 95f;

    private const float InkEdge = 10f, InkSpike = 7f, InkFine = 5f;

    /// <summary>Eight spikes, stepping around three reserved arcs: the crown at 270 where a
    /// hat's brim sits, the belly, and the two fin latitudes.</summary>
    private static readonly float[] SpikeDeg = [25f, 60f, 120f, 155f, 205f, 240f, 300f, 335f];

    private const float SpikeHw = 6.75f;

    private const float EyeDx = 32f, EyeY = 217f;
    private const float MouthY = 268f;

    /// <summary>33 degrees, not 74. Y runs DOWN, so 74 is very nearly straight below the ball and
    /// puts both nubs under the chin where the mouth is. 33 is low on the flanks, clear of the
    /// face, and below the fin line.</summary>
    private const float NubDeg = 33f, NubR = 15f;

    private const float FinY = CY;
    private const float HeadY = CY - R;
    private const float BodyY = CY;
    private const float EarDx = 40f, EarY = 150f;
    private const float TailDx = -58f, TailY = 268f;

    /// <summary>A fish eye: rimmed concentrically rather than given an inset highlight, and its
    /// pupil sits INBOARD (the generator's <c>2.5 * -side</c>) where every other shell's sits
    /// out. Both are why this face reads as an animal's rather than a doll's.</summary>
    private static readonly EyeRig Rig = new(
        Dx: EyeDx, Y: EyeY, Rx: 28f, Ry: 34f,
        PupilRx: 16.24f, PupilRy: 21.08f, RingW: 5.5f, PupilOut: -2.5f,
        BigDx: 9.52f, BigDy: 10.88f, BigR: 6.72f,
        SmallDx: 7.28f, SmallDy: 13.6f, SmallR: 3.36f,
        ShutBow: 18.7f, LashW: 5.5f,
        ConcentricRim: true, PupilDown: 0.06f);

    /// <summary>A puffer is a taut balloon: it arrives at its puff and holds it. Slack enough to
    /// carry a little overshoot out of the boop, nowhere near the Jelly's 0.85.</summary>
    public static readonly Material Stuff = new(Springiness: 0.30f, TrimLag: 0.45f);

    public static Vector2 PartOrigin(string part) => new(CX, CY);

    public static float InkWidth { get; set; } = InkEdge;

    // ------------------------------------------------------------------- poses --

    /// <summary>This shell's key factory, matching its generator's
    /// <c>P(k, spike, dy, eye, blush, fin)</c>. k is how puffed, spike is an ABSOLUTE spine
    /// length, dy is the hover bob, fin is the fins' own flutter in degrees.</summary>
    private static Key K(float k, float spike, float dy, EyeState eye, bool blush = false, float fin = 0f)
    {
        var c = Neutral();
        c[(int)Ch.K] = k;
        c[(int)Ch.Spike] = spike;
        c[(int)Ch.Dy] = dy;
        c[(int)Ch.Fin] = fin;
        return new Key(c, eye, blush);
    }

    private static readonly Key[] Poses =
    [
        // idle 0-7: a hover. The bob and the fin flutter are one cycle so the loop closes, and
        // the body breathes a hair because a fish holding station is never quite still. The
        // spines barely move: they are at rest, and a resting puffer's spines lie down.
        K(0.99f, 18.0f, 0f, Open, fin: 0f),
        K(1.00f, 18.5f, -2f, Open, fin: 7f),
        K(1.00f, 19.0f, -3f, Open, fin: 10f),
        K(0.99f, 18.5f, -2f, Open, fin: 7f),
        K(0.98f, 18.0f, 0f, Open, fin: 0f),
        K(0.98f, 17.5f, 2f, Open, fin: -7f),
        K(0.99f, 17.0f, 3f, Open, fin: -10f),
        K(0.99f, 17.5f, 2f, Open, fin: -7f),

        // blink 8-10: held still.
        K(0.99f, 18.0f, 0f, Open),
        K(0.99f, 18.0f, 0f, Shut),
        K(0.99f, 18.0f, 0f, HalfShut),

        // boop 11-16: THE PUFF. Rest, FLINCH, peak, hold, easing, settled. Note the flinch
        // DEFLATES (0.92) before it blows up - a puffer gathers before it puffs.
        K(0.96f, 20.0f, 1f, Wide),
        K(0.92f, 26.0f, 3f, Wide),
        K(1.06f, 53.0f, -2f, Wide),
        K(1.05f, 47.0f, -2f, Squint),
        K(0.99f, 30.0f, 0f, Squint),
        K(0.96f, 20.0f, 1f, Happy, blush: true),

        // nap 17-22: deflated and drifting.
        K(0.94f, 9.0f, 2f, Shut, blush: true, fin: 4f),
        K(0.94f, 8.5f, 3f, Shut, blush: true, fin: 7f),
        K(0.93f, 8.0f, 4f, Shut, blush: true, fin: 4f),
        K(0.93f, 8.0f, 4f, Shut, blush: true, fin: -4f),
        K(0.94f, 8.5f, 3f, Shut, blush: true, fin: -7f),
        K(0.94f, 9.0f, 2f, Shut, blush: true, fin: -4f),

        // hop 23-32: a DART. A fish does not hop, it kicks and glides.
        K(0.97f, 20.0f, 2f, Squint, fin: -16f),
        K(0.96f, 22.0f, 4f, Squint, fin: -22f),
        K(1.00f, 26.0f, -6f, Wide, fin: 18f),
        K(1.01f, 28.0f, -12f, Wide, fin: 24f),
        K(1.00f, 26.0f, -14f, Open, fin: 16f),
        K(0.99f, 23.0f, -11f, Open, fin: 8f),
        K(0.99f, 21.0f, -6f, Open, fin: 0f),
        K(0.98f, 20.0f, -1f, Squint, fin: -8f),
        K(0.99f, 19.0f, 2f, Open, fin: -4f),
        K(0.99f, 18.0f, 0f, Open),

        // 33-37: the five rest-registered eye cells, from this shell's own rest pose.
        K(0.99f, 18.0f, 0f, ThreeQ),
        K(0.99f, 18.0f, 0f, HalfShut),
        K(0.99f, 18.0f, 0f, Quarter),
        K(0.99f, 18.0f, 0f, Drowsy),
        K(0.99f, 18.0f, 0f, Heavy),
    ];

    /// <summary>Lets this shell's ambient channels run through a clip that does not act them.</summary>
    public static Channels WithAmbient(Channels target, int[] frames, float beat, out bool clipDrives, out float driven)
        => LineShell.WithAmbient(target, Poses, frames, beat, out clipDrives, out driven);

    public static Channels PoseAt(int prev, int cell, int next, int after, float phase)
        => LineShell.PoseAt(Poses, prev, cell, next, after, phase);

    /// <summary>This shell's face at a moment of its own clip: the eye between two cells and the
    /// blush across them. The pose table is this shell's; the resolving is the caller's.</summary>
    public static (EyeState Eye, float Blush) FaceAt(int cell, int next, float phase)
        => (LineShell.EyeAt(Poses, cell, next, phase), LineShell.BlushAt(Poses, cell, next, phase));

    /// <summary>The BALL's transform: a uniform inflate about the ball's centre, then the bob.
    /// Both scale axes equal, which is what makes it an inflate rather than a squash.</summary>
    public static LinePose Posed(Channels c) =>
        new(c[(int)Ch.K], c[(int)Ch.K], c[(int)Ch.Dy], CX, CY);

    /// <summary>The transform for everything that bobs but does NOT inflate: the face, and the
    /// fins. Scale 1 on both axes, so only the bob survives.</summary>
    public static LinePose Flat(Channels c) => new(1f, 1f, c[(int)Ch.Dy], CX, CY);

    /// <summary>Where the outline is at one angle, at this puff.</summary>
    private static Vector2 Edge(Channels c, float deg)
    {
        var a = deg * MathF.PI / 180f;
        var k = c[(int)Ch.K];
        return new Vector2(
            CX + (R * k * MathF.Cos(a)),
            CY + (R * k * MathF.Sin(a)) + c[(int)Ch.Dy]);
    }

    private static Vector2 NubPt(Channels c, int side) => Edge(c, side > 0 ? NubDeg : 180f - NubDeg);

    public static Vector2 Anchor0(string name) => name switch
    {
        "head" => new Vector2(CX, HeadY),
        "face" => new Vector2(CX, EyeY),
        "body" => new Vector2(CX, BodyY),
        "mouth" => new Vector2(CX, MouthY),
        "earL" => new Vector2(CX - EarDx, EarY),
        "earR" => new Vector2(CX + EarDx, EarY),
        "tail" => new Vector2(CX + TailDx, TailY),
        _ => new Vector2(CX, CY),
    };

    /// <summary>An anchor under a pose. <c>face</c> and <c>mouth</c> take the FLAT transform, and
    /// the hands come off the ball's own edge rather than off a stored point: a nub that did not
    /// ride the edge would float when the fish puffed.</summary>
    public static Vector2 Anchor(string name, Channels c) => name switch
    {
        "handL" => NubPt(c, -1),
        "handR" => NubPt(c, 1),
        "face" or "mouth" => Flat(c).Pt2(Anchor0(name)),
        _ => Posed(c).Pt2(Anchor0(name)),
    };

    /// <inheritdoc cref="JellyLineArt.Pin"/>
    ///
    /// <para>The face takes the FLAT transform and the crown does not, which is this shell's
    /// whole character: it inflates about the ball's centre, and a face that inflated with it
    /// would swell rather than puff.</para>
    public static Vector2 Pin(string name, PinKind kind, Vector2 rest, Channels c) => kind switch
    {
        PinKind.Hand => Anchor(name, c),
        PinKind.Face => Flat(c).Pt2(rest),
        _ => Posed(c).Pt2(rest),
    };

    // -------------------------------------------------------------------- draw --

    /// <summary>One spine, rooted ON the ball's edge at this puff. Struck from the edge rather
    /// than from a fixed radius, so a spine never floats off the body when it inflates and never
    /// buries itself when it shrinks.</summary>
    private static void SpikePath(LineCanvas c, Channels ch, float deg)
    {
        var a = deg * MathF.PI / 180f;
        var b = Edge(ch, deg);
        var len = ch[(int)Ch.Spike];
        var tip = b + new Vector2(MathF.Cos(a) * len, MathF.Sin(a) * len);
        var perp = new Vector2(-MathF.Sin(a) * SpikeHw, MathF.Cos(a) * SpikeHw);

        c.MoveTo(b + perp);
        c.LineTo(tip);
        c.LineTo(b - perp);
        c.LineTo(b + perp);
    }

    /// <summary>A mitten-fan at the equator, at a FIXED size and a fixed seat: the one part of
    /// this creature that does not answer to the puff.</summary>
    private static void FinPath(LineCanvas c, Channels ch, int sx)
    {
        var flat = Flat(ch);
        var seat = flat.Pt(CX + (sx * (R - 5f)), FinY);
        var xf = new LocalXf(seat, sx, 1f, ch[(int)Ch.Fin] * sx);

        c.MoveTo(xf.To(0f, -20f));
        c.CubicTo(xf.To(26f, -26f), xf.To(47f, -17f), xf.To(53f, 2f));
        c.CubicTo(xf.To(47f, 20f), xf.To(26f, 27f), xf.To(0f, 21f));
        c.CubicTo(xf.To(8f, 12f), xf.To(9f, -9f), xf.To(0f, -20f));
    }

    public static void Draw(
        LineCanvas c,
        ImDrawListPtr dl,
        Vector2 bottomCentre,
        float displaySize,
        Channels ch,
        Channels trimCh,
        EyeState eye,
        float blush,
        Vector4 body,
        Vector4 accent,
        Vector4 eyeTint,
        Vector4 ink,
        Vector2 outer,
        bool flip = false)
    {
        c.Begin(dl, bottomCentre, displaySize, Cell, outer, flip);

        var k = ch[(int)Ch.K];
        var r = R * k;
        var mid = new Vector2(CX, CY + ch[(int)Ch.Dy]);

        // ORDER IS THE CLIP again: spines and fins go down FIRST, filled AND inked, and the ball
        // is drawn over them so its own edge closes across every root. A spine whose base line
        // shows is stuck ON the fish rather than growing out of it - the generator says exactly
        // that, and on the sheet it needed a mask to achieve.
        foreach (var deg in SpikeDeg)
        {
            SpikePath(c, ch, deg);
            c.Fill(Edge(ch, deg), Tint(body, Base));
            SpikePath(c, ch, deg);
            c.Stroke(ink, InkSpike);
        }

        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            FinPath(c, ch, side);
            c.Fill(Flat(ch).Pt(CX + (side * (R - 5f)), FinY), Tint(accent, AccBase));
            FinPath(c, ch, side);
            c.Stroke(ink, InkSpike);
        }

        // The nubs, then the ball over their inner halves.
        foreach (var side in new[] { -1, 1 })
        {
            c.Ellipse(NubPt(ch, side), NubR, NubR, Tint(body, NubFill));
        }

        // The ball. The lit fill is pushed toward the key light, so what survives of the darker
        // disc is a crescent on the far side - which is the difference between a disc and a ball.
        // Concentric is not lighting, it is what you get when the light comes from your own eye.
        c.Ellipse(mid, r, r, Tint(body, Shadow));
        c.Ellipse(mid - new Vector2(r * 0.10f, r * 0.09f), r - 8f, r - 8f, Tint(body, Base));

        c.MoveTo(mid + new Vector2(-r * 0.55f, -r * 0.62f));
        c.CubicTo(
            mid + new Vector2(-r * 0.78f, -r * 0.42f),
            mid + new Vector2(-r * 0.88f, -r * 0.14f),
            mid + new Vector2(-r * 0.90f, r * 0.08f));
        c.Stroke(Tint(body, Rim), 7f, closed: false);

        // A fish has a pale UNDERSIDE, and one shape reads at 96 px where five scattered freckles
        // do not. Trimmings, so they ride the lagged pose.
        var tr = R * trimCh[(int)Ch.K];
        var tmid = new Vector2(CX, CY + trimCh[(int)Ch.Dy]);
        c.Ellipse(tmid + new Vector2(0f, tr * 0.52f), tr * 0.54f, tr * 0.29f, Tint(accent, AccBase));

        c.MoveTo(tmid + new Vector2(-tr * 0.50f, tr * 0.42f));
        c.QuadTo(tmid + new Vector2(0f, tr * 0.26f), tmid + new Vector2(tr * 0.50f, tr * 0.42f));
        c.Stroke(Tint(accent, AccShadow) with { W = 0.45f }, InkFine, closed: false);

        foreach (var (dx, dy, rr) in new[] { (-0.50f, -0.62f, 5.0f), (0.46f, -0.72f, 4.2f) })
        {
            c.Ellipse(tmid + new Vector2(tr * dx, tr * dy), rr, rr, Tint(accent, AccShadow));
        }

        // The ball's own outline, then the nubs part it and their arcs close the silhouette.
        c.EllipseStroke(mid, r, r, ink, InkEdge);

        bool InsideBall(Vector2 p) => Vector2.DistanceSquared(p, mid) <= r * r;

        foreach (var side in new[] { -1, 1 })
        {
            c.Ellipse(NubPt(ch, side), NubR, NubR, Tint(body, NubFill));
        }

        DrawNubsAt(c, ch, ink, InsideBall);

        // The blush is a COLOUR, so it goes with the ink rather than on the accent layer: a
        // tinted layer carries no hue of its own, and a pink cheek is a hue.
        // A happy or squinting puffer keeps its full cheek even at zero blush.
        var cheek = MathF.Max(blush, eye.Happy || eye.Squint > 0.5f ? 1f : 0f);
        if (cheek > 0f)
        {
            var flat = Flat(ch);
            foreach (var side in new[] { -1, 1 })
            {
                c.Ellipse(flat.Pt(CX + (side * 66f), EyeY + 30f), 14f, 9f, BlushTint(cheek * 0.8f / LineShell.Blush.W));
            }
        }

        // The face rides the FLAT transform: a fish's face does not inflate with the rest of it.
        var face = Flat(ch);
        DrawEyes(c, Rig, eye, side => face.Pt(CX + (side * EyeDx), EyeY), 1f, 1f, eyeTint, ink);
    }

    /// <summary>The nub ink, solved against the ball rather than assumed. The nubs sit ON the
    /// edge at 33 degrees, so a fixed half circle would be wrong at both ends.</summary>
    private static void DrawNubsAt(LineCanvas c, Channels ch, Vector4 ink, Func<Vector2, bool> inside)
    {
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            var at = NubPt(ch, side);
            var outward = MathF.Atan2(at.Y - (CY + ch[(int)Ch.Dy]), at.X - CX);

            var from = outward;
            var to = outward;
            const int Steps = 48;
            for (var s = 1; s <= Steps; s++)
            {
                var d = MathF.PI * s / Steps;
                if (from == outward - (MathF.PI * (s - 1) / Steps)
                    && !inside(at + new Vector2(MathF.Cos(outward - d) * NubR, MathF.Sin(outward - d) * NubR)))
                {
                    from = outward - d;
                }

                if (to == outward + (MathF.PI * (s - 1) / Steps)
                    && !inside(at + new Vector2(MathF.Cos(outward + d) * NubR, MathF.Sin(outward + d) * NubR)))
                {
                    to = outward + d;
                }
            }

            c.Arc(at, NubR, from, to, ink, InkSpike);
        }
    }
}
