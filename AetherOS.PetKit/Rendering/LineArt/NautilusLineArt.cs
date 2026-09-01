namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using static AetherOS.PetKit.Rendering.LineArt.LineShell;

/// <summary>
/// The Nautilus, drawn. Fourth shell, and the one with the most parts that move differently from
/// each other.
///
/// <para><b>Three transforms, none of them optional.</b> The body squashes about the sole; the
/// SOUL withdraws by shrinking toward the aperture and dropping; the SHELL takes an extra
/// rotation the other two do not. The generator is precise about that last one ("the sole,
/// outside the rock: the shell rocks ON the foot, so the foot does not go with it") and it is
/// the reason a part-specific transform had to exist rather than a single pose reaching
/// everything.</para>
///
/// <para><b>What retreat actually is.</b> Not conditional geometry: nothing appears or
/// disappears. A snail retreating "is not sinking, it is reversing along the axis of its own
/// opening", so the soul is pulled toward the aperture's centre AND dropped, which makes the face
/// shrink as it goes rather than merely slide.</para>
///
/// <para><b>And the authoring lesson this shell teaches.</b> Its pose table holds
/// <c>sx = sy = 1.00</c> through the entire boop and the entire nap, spending the whole beat on
/// <c>retreat</c> and <c>rock</c> instead; only the hop squashes at all, and never past 1.05.
/// That is how a creature with a rigid part stays rigid: the beats go into channels that are not
/// the squash. Worth looking for on the Spintop and the Lantern, which are the other two with
/// hard bodies and a theta to swing on.</para>
/// </summary>
public static class NautilusLineArt
{
    // ---------------------------------------------------------------- geometry --
    // Lifted from the retired nautilus-sheet/build_sheet.py unchanged: same names, same numbers.
    public const float Cell = 384f;

    public const float CX = 192f;
    private const float InkEdge = 11f, InkSuture = 7f, InkTick = 10f;

    /// <summary>The helicospiral, biggest first, precomputed from the generator's own
    /// <c>whorls(n=4, k=0.68, lean=-118, curl=26, r0=122, base=(196, 202))</c>. The step between
    /// one centre and the next is the DIFFERENCE of the radii times a shade over one, which is
    /// what keeps consecutive turns overlapping: step by more and the coil comes apart into a
    /// stack of separate balls.</summary>
    private static readonly Vector3[] Spec =
    [
        new(196.0000f, 202.0000f, 122.0000f),
        new(169.9740f, 153.0522f, 82.9600f),
        new(168.6584f, 115.3782f, 56.4128f),
        new(179.0847f, 91.9603f, 38.3607f),
    ];

    private static readonly Vector4 Soul = new(CX, 255f, 60.5f, 63.5f);
    private static readonly Vector4 Aperture = new(CX, 251f, 75f, 76f);

    private const float EyeDx = 34f, EyeY = 239f, EyeS = 0.56f;
    private const float MouthY = 275f;
    private const float NubDx = 42f, NubY = 291f, NubR = 12f;
    private const float FanY = 304f, HeadY = 60f, BodyY = 242f;

    private const float FootTop = 294f, FootBot = 352f;
    private const float FootHw = 88f, FootSpread = 1.25f, FootCorner = 27f, FootBulge = 0.10f;
    private const float SoleGain = 1.05f;
    private static readonly float BandY = FootTop + 38f;

    private const float RetreatDrop = 30f, RetreatShrink = 0.30f;

    private static readonly EyeRig Rig = new(
        Dx: EyeDx, Y: EyeY, Rx: 44f * EyeS, Ry: 52f * EyeS,
        PupilRx: 14.3f, PupilRy: 18f, RingW: 7f, PupilOut: 3f,
        BigDx: 8f, BigDy: 10f, BigR: 6f,
        SmallDx: 6f, SmallDy: 11f, SmallR: 3f,
        ShutBow: 14f, LashW: 8f);

    /// <summary>A shell on a soft foot. Rigid where it counts, with a little give in the sole.</summary>
    public static readonly Material Stuff = new(Springiness: 0.20f, TrimLag: 0.40f);

    public static Vector2 PartOrigin(string part) => part switch
    {
        "aperture" => new Vector2(Aperture.X, Aperture.Y),
        "sole" => new Vector2(CX, FootBot),
        _ => new Vector2(CX, FootBot),
    };

    public static float InkWidth { get; set; } = InkEdge;

    // ------------------------------------------------------------------- poses --

    /// <summary>Matching the generator's <c>P(theta, retreat, sx, sy, dy, eye, blush, rock)</c>.
    /// theta drives the foot's travelling wave; retreat is how far in the soul has gone; rock is
    /// the shell's own tilt in degrees.</summary>
    private static Key K(float theta, float retreat, float sx, float sy, float dy, EyeState eye, bool blush = false, float rock = 0f)
    {
        var c = Neutral();
        c[(int)Ch.Theta] = theta;
        c[(int)Ch.Retreat] = retreat;
        c[(int)Ch.Sx] = sx;
        c[(int)Ch.Sy] = sy;
        c[(int)Ch.Dy] = dy;
        c[(int)Ch.Rock] = rock;
        return new Key(c, eye, blush);
    }

    private static readonly Key[] Poses =
    [
        // idle 0-7: the soul breathes out of its shell and back, and a wave runs down the foot.
        // The retreat never reaches zero at rest - a snail at ease is still a snail in a shell.
        K(0f / 8f, 0.10f, 1f, 1f, 0f, Open),
        K(1f / 8f, 0.06f, 1f, 1f, -1f, Open),
        K(2f / 8f, 0.03f, 1f, 1f, -1f, Open),
        K(3f / 8f, 0.02f, 1f, 1f, -1f, Open),
        K(4f / 8f, 0.04f, 1f, 1f, 0f, Open),
        K(5f / 8f, 0.08f, 1f, 1f, 0f, Open),
        K(6f / 8f, 0.12f, 1f, 1f, 1f, Open),
        K(7f / 8f, 0.12f, 1f, 1f, 0f, Open),

        // blink 8-10
        K(0f, 0.08f, 1f, 1f, 0f, Open),
        K(0f, 0.08f, 1f, 1f, 0f, Shut),
        K(0f, 0.08f, 1f, 1f, 0f, HalfShut),

        // boop 11-16: THE RETREAT. Poked, a snail goes in - and note sx and sy never move.
        K(0.20f, 0.55f, 1f, 1f, 0f, Squint, rock: -3.5f),
        K(0.30f, 0.92f, 1f, 1f, 1f, Shut, rock: -5f),
        K(0.40f, 1.00f, 1f, 1f, 1f, Shut, rock: -2f),
        K(0.50f, 0.72f, 1f, 1f, 0f, Shut, rock: 1.5f),
        K(0.60f, 0.30f, 1f, 1f, 0f, Squint, rock: 1f),
        K(0.70f, 0.08f, 1f, 1f, 0f, Happy, blush: true),

        // nap 17-22: most of the way in, lids down, the foot's wave slowed.
        K(0.12f, 0.62f, 1f, 1f, 1f, Shut, blush: true),
        K(0.16f, 0.66f, 1f, 1f, 2f, Shut, blush: true),
        K(0.20f, 0.68f, 1f, 1f, 2f, Shut, blush: true),
        K(0.24f, 0.66f, 1f, 1f, 2f, Shut, blush: true),
        K(0.28f, 0.62f, 1f, 1f, 1f, Shut, blush: true),
        K(0.32f, 0.60f, 1f, 1f, 1f, Shut, blush: true),

        // hop 23-32: a snail does not hop, it SURGES. The only clip that squashes at all.
        K(0.55f, 0.30f, 1.02f, 0.98f, 2f, Squint, rock: 2.5f),
        K(0.65f, 0.42f, 1.05f, 0.95f, 3f, Squint, rock: 4f),
        K(0.75f, 0.10f, 0.98f, 1.03f, -3f, Wide, rock: -2f),
        K(0.85f, 0.00f, 0.96f, 1.05f, -7f, Wide, rock: -5f),
        K(0.95f, 0.00f, 0.97f, 1.04f, -8f, Open, rock: -4f),
        K(0.05f, 0.02f, 0.99f, 1.02f, -5f, Open, rock: -1f),
        K(0.15f, 0.08f, 1.01f, 0.99f, -1f, Open, rock: 1.5f),
        K(0.25f, 0.22f, 1.05f, 0.95f, 3f, Squint, rock: 3.5f),
        K(0.35f, 0.16f, 1.02f, 0.98f, 1f, Open, rock: 1f),
        K(0.00f, 0.08f, 1f, 1f, 0f, Open),

        // 33-37: the five rest-registered eye cells, from this shell's own rest pose.
        K(0f, 0.08f, 1f, 1f, 0f, ThreeQ),
        K(0f, 0.08f, 1f, 1f, 0f, HalfShut),
        K(0f, 0.08f, 1f, 1f, 0f, Quarter),
        K(0f, 0.08f, 1f, 1f, 0f, Drowsy),
        K(0f, 0.08f, 1f, 1f, 0f, Heavy),
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

    /// <summary>The global squash, about the SOLE - this shell's ground relationship.</summary>
    public static LinePose Posed(Channels c) =>
        new(c[(int)Ch.Sx], c[(int)Ch.Sy], c[(int)Ch.Dy], CX, FootBot);

    private static float SoulScale(Channels c) => 1f - (c[(int)Ch.Retreat] * RetreatShrink);

    /// <summary>A point on the SOUL, pulled toward the aperture's own centre as it withdraws.
    /// Toward the aperture rather than straight down, because that is where the animal is going.</summary>
    private static Vector2 SoulPt(Channels c, float x, float y)
    {
        var retreat = c[(int)Ch.Retreat];
        var ax = Aperture.X;
        var ay = Aperture.Y + (RetreatDrop * 0.42f);
        var k = SoulScale(c);
        return Posed(c).Pt(
            ax + ((x - ax) * k),
            ay + ((y - ay) * k) + (retreat * RetreatDrop));
    }

    /// <summary>A point on the SHELL, which takes the rock the foot does not.</summary>
    private static Vector2 ShellPt(Channels c, float x, float y)
    {
        var q = Posed(c);
        var p = q.Pt(x, y);
        var rock = c[(int)Ch.Rock];
        if (MathF.Abs(rock) < 0.01f)
        {
            return p;
        }

        var pivot = q.Pt(CX, FootBot);
        var a = rock * MathF.PI / 180f;
        var d = p - pivot;
        return pivot + new Vector2(
            (d.X * MathF.Cos(a)) - (d.Y * MathF.Sin(a)),
            (d.X * MathF.Sin(a)) + (d.Y * MathF.Cos(a)));
    }

    /// <summary>A snail's foot moves by waves running along its sole. Two of them, low amplitude,
    /// and they are most of what says this creature is alive when the rest of it is a rigid shell
    /// sitting still.</summary>
    private static float FootWave(Channels c, float u) =>
        MathF.Sin(((u * 2f) - c[(int)Ch.Theta]) * MathF.Tau) * 2.6f;

    /// <summary>Half-width of the foot at a row - the flare law, and the single source every part
    /// of the foot measures itself against. Smoothstepped rather than linear so the spread happens
    /// LOW, where the weight is; linear gave a wedge, which is a doorstop.</summary>
    private static float FootHwAt(float y, float inset = 0f)
    {
        var t = Math.Clamp((y - FootTop) / (FootBot - FootTop), 0f, 1f);
        var s = t * t * (3f - (2f * t));
        var hw = FootHw * (1f + ((FootSpread - 1f) * s) + (FootBulge * MathF.Sin(MathF.PI * t)));
        return hw - inset;
    }

    /// <summary>The foot, walked all the way round from one law. The two edges that carry the wave
    /// are sampled; the flanks come off the flare law; the sole's corners are the only two curves
    /// authored by hand, because a corner is the one thing a per-row width cannot express.</summary>
    private static void FootPath(LineCanvas canvas, Channels c, float inset = 0f, float? topY = null, float topGain = 1f)
    {
        var q = Posed(c);
        var top = (topY ?? FootTop) + inset;
        var bot = FootBot - (inset * 0.6f);
        var r = MathF.Max(7f, FootCorner - (inset * 0.6f));
        const int N = 16;

        var hwTop = FootHwAt(top, inset);
        var hwSole = FootHwAt(bot, inset);
        var flankBot = bot - r;

        canvas.MoveTo(q.Pt(CX - hwTop, top + (FootWave(c, 0f) * topGain)));
        for (var i = 1; i <= N; i++)
        {
            var u = (float)i / N;
            canvas.LineTo(q.Pt(CX - hwTop + (2f * hwTop * u), top + (FootWave(c, u) * topGain)));
        }

        // The flank, DOWN the side - and only if there is any side left to walk. Cut high enough
        // (the accent band is cut at BAND_Y) the top sits BELOW the corner's own start, and
        // walking it anyway runs the outline back upward and folds the shape into a bowtie. The
        // generator has the same inversion and never shows it, because SVG's nonzero fill rule
        // paints a self-intersecting path solid; a triangle fan paints the fold, which is the V
        // that appeared across the sole.
        var hasFlank = flankBot > top + 0.5f;
        if (hasFlank)
        {
            for (var i = 1; i <= N; i++)
            {
                var y = top + ((flankBot - top) * i / N);
                canvas.LineTo(q.Pt(CX + FootHwAt(y, inset), y));
            }
        }

        canvas.QuadTo(
            q.Pt(CX + hwSole, bot),
            q.Pt(CX + hwSole - r, bot + (FootWave(c, 1f) * SoleGain)));

        var run = hwSole - r;
        for (var i = 1; i <= N; i++)
        {
            var u = 1f - ((float)i / N);
            canvas.LineTo(q.Pt(CX - run + (2f * run * u), bot + (FootWave(c, u) * SoleGain)));
        }

        // The left corner has to come home to wherever the outline actually STARTED. With a flank
        // it climbs to the flank's foot and walks up; without one (the accent band, cut below the
        // corner's own start) that point is ABOVE the band's top edge, so aiming at it threw the
        // outline up past its own beginning and closed the shape on a diagonal - the stray angle
        // on the left of the sole.
        canvas.QuadTo(
            q.Pt(CX - hwSole, bot),
            hasFlank
                ? q.Pt(CX - FootHwAt(flankBot, inset), flankBot)
                : q.Pt(CX - hwTop, top + (FootWave(c, 0f) * topGain)));

        if (hasFlank)
        {
            for (var i = N - 1; i >= 1; i--)
            {
                var y = top + ((flankBot - top) * i / N);
                canvas.LineTo(q.Pt(CX - FootHwAt(y, inset), y));
            }
        }
    }

    /// <summary>Growth ticks: the two big turns and nothing after them, because the small ones
    /// have no room to carry a mark that reads.
    ///
    /// <para>Drawn in SHADOW on the body rather than in ink, and that is a decision rather than a
    /// convenience: ink is for the EDGE of a creature, and a growth line is a marking ON one. An
    /// inked tick would read as the shell being cracked into segments.</para></summary>
    private static void DrawTicks(LineCanvas c, Channels ch, int whorl, Vector4 body)
    {
        const float Every = 34f, Depth = 0.30f, Span0 = -190f, Span1 = 10f;
        if (whorl >= 2)
        {
            return;
        }

        var w = Spec[whorl];
        var n = Math.Max(3, (int)(((Span1 - Span0) / Every) * MathF.Pow(0.5f, whorl)));
        var rr = w.Z - 10f;

        for (var i = 0; i <= n; i++)
        {
            var a = (Span0 + ((Span1 - Span0) * i / n)) * MathF.PI / 180f;
            var cos = MathF.Cos(a);
            var sin = MathF.Sin(a);
            c.MoveTo(ShellPt(ch, w.X + (cos * rr * (1f - Depth)), w.Y + (sin * rr * (1f - Depth))));
            c.LineTo(ShellPt(ch, w.X + (cos * rr), w.Y + (sin * rr)));
            c.Stroke(Tint(body, Shadow), InkTick, closed: false);
        }
    }

    public static Vector2 Anchor0(string name) => name switch
    {
        "head" => new Vector2(CX, HeadY),
        "face" => new Vector2(CX, EyeY),
        "body" => new Vector2(CX, BodyY),
        "handL" => new Vector2(CX - NubDx, NubY),
        "handR" => new Vector2(CX + NubDx, NubY),
        "mouth" => new Vector2(CX, MouthY),
        "fan" => new Vector2(CX, FanY),
        _ => new Vector2(CX, BodyY),
    };

    /// <summary>An anchor under a pose, and which of the three transforms it takes is the whole
    /// point of this shell: a hat rides the SHELL and rocks with it, a face rides the SOUL and
    /// withdraws with it, and a tail rides the body and does neither.</summary>
    public static Vector2 Anchor(string name, Channels c)
    {
        var a = Anchor0(name);
        return name switch
        {
            "head" or "body" => ShellPt(c, a.X, a.Y),
            "face" or "mouth" or "fan" or "handL" or "handR" => SoulPt(c, a.X, a.Y),
            _ => Posed(c).Pt2(a),
        };
    }

    /// <inheritdoc cref="JellyLineArt.Pin"/>
    ///
    /// <para>Two bodies, and which one a pin rides is the design: the SHELL swings and the SOUL
    /// inside it does not, so a hat rides the shell and a pair of glasses rides the soul. The
    /// named cases are this shell's own table; the kinds carry everything else onto the right one
    /// of the two.</para>
    public static Vector2 Pin(string name, PinKind kind, Vector2 rest, Channels c) => name switch
    {
        "head" or "body" => ShellPt(c, rest.X, rest.Y),
        "face" or "mouth" or "fan" => SoulPt(c, rest.X, rest.Y),
        _ => kind switch
        {
            PinKind.Hand => Anchor(name, c),
            PinKind.Head => ShellPt(c, rest.X, rest.Y),
            PinKind.Face => SoulPt(c, rest.X, rest.Y),
            _ => Posed(c).Pt2(rest),
        },
    };

    // -------------------------------------------------------------------- draw --

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
        var q = Posed(ch);

        // The coil, biggest first so the apex lands on top - and each turn is FILLED, MARKED and
        // INKED before the next one starts.
        //
        // That order is the fix for two faults at once. The sheet masks every whorl's outline to
        // its own visible face, because "stroked whole they would lay arcs straight across the
        // turns in front of them - the overlay draws last, so nothing covers it afterwards and
        // the clip is the only thing that can stop it". Drawing all four fills and then all four
        // rings reproduced exactly that fault: every loop showing through every other. Interleaved,
        // each turn's fill buries the ring of the turn behind it, which is what the mask was for.
        for (var i = 0; i < Spec.Length; i++)
        {
            var w = Spec[i];
            var mid = ShellPt(ch, w.X, w.Y);
            var rx = w.Z * ch[(int)Ch.Sx];
            var ry = w.Z * ch[(int)Ch.Sy];
            var inset = MathF.Max(4f, w.Z * 0.055f);

            // A shadow disc with the lit fill pushed toward the key light: what survives of the
            // darker disc is a crescent on the far side, and that offset is the difference
            // between a disc and a ball. Concentric is not lighting - it is what you get when
            // the light comes from the viewer's own eye.
            c.Ellipse(mid, rx, ry, Tint(body, Shadow));

            // Every inset is CLIPPED to this turn. Each one is offset toward the key light, so
            // unclipped they hang past the silhouette on that side - which is what put fill
            // outside the shell at the top left.
            c.EllipseIn(mid - new Vector2(rx * 0.14f, ry * 0.16f), rx - inset, ry - inset, mid, rx, ry, Tint(body, Base));
            c.EllipseIn(mid - new Vector2(rx * 0.20f, ry * 0.24f), rx * 0.74f, ry * 0.74f, mid, rx, ry, Tint(body, Rim));
            c.EllipseIn(mid - new Vector2(rx * 0.16f, ry * 0.19f), rx * 0.70f, ry * 0.70f, mid, rx, ry, Tint(body, Base));

            DrawTicks(c, ch, i, body);

            // What the NEXT turn casts onto this one. Without it the turns are two balls at the
            // same depth however they are stacked; this is the shadow that says which is in
            // front, and it is the shading the inner edges were missing. Clipped to this turn,
            // which is the only reason it can be drawn at all.
            if (i + 1 < Spec.Length)
            {
                var nw = Spec[i + 1];
                var nmid = ShellPt(ch, nw.X, nw.Y);
                c.EllipseIn(
                    nmid + new Vector2(nw.Z * 0.10f, nw.Z * 0.16f),
                    nw.Z * 1.10f * ch[(int)Ch.Sx], nw.Z * 1.10f * ch[(int)Ch.Sy],
                    mid, rx, ry,
                    Tint(body, Shadow));
            }

            // At INK_EDGE, not the suture weight. Every loop carries the creature's full outline
            // weight and the silhouette is not a mark of its own: it is whatever survives of the
            // loops once the turns in front have covered the rest.
            c.EllipseStroke(mid, rx, ry, ink, InkEdge);
        }

        // The aperture, cut into the coil: the hole the animal lives in.
        var ap = ShellPt(ch, Aperture.X, Aperture.Y);
        c.Ellipse(ap, Aperture.Z * ch[(int)Ch.Sx], Aperture.W * ch[(int)Ch.Sy], Tint(body, Shadow));

        // The sole, OUTSIDE the rock: the shell rocks ON the foot, so the foot does not go with it.
        FootPath(c, ch);
        c.Fill(q.Pt(CX, FootBot - 20f), Tint(body, Shadow));
        FootPath(c, ch, 7f);
        c.Fill(q.Pt(CX, FootBot - 20f), Tint(body, Base));

        // The sole's own material: a BAND along the lower half rather than the whole foot. A
        // snail's foot is different tissue from its shell and reads as one, so it takes the
        // accent role - but the accent is a palette's loudest colour by design, and the first cut
        // gave it the ENTIRE sole, which came out a tan brick the creature stood ON rather than a
        // foot it stood WITH. Cut from the foot's own outline at BAND_Y, so it shares the flare,
        // the sole and the corners; cut as its own rectangle it had straight sides and a straight
        // bottom inside a shape that has neither, and two hard horizontals read as masonry.
        const float BandInset = 8f;
        FootPath(c, ch, BandInset, BandY, 1.35f);
        c.Fill(q.Pt(CX, FootBot - 14f), Tint(accent, AccBase));

        // The leading lip: the light edge along the band's top, which is the fold where the sole
        // turns under. Its own wavy line rather than the band's outline, so it stops at the
        // flanks and never rings the foot.
        {
            const int N = 16;
            var hw = FootHwAt(BandY + BandInset, BandInset) * 0.94f;
            c.MoveTo(q.Pt(CX - hw, BandY + BandInset + (FootWave(c: ch, u: 0f) * 1.35f)));
            for (var i = 1; i <= N; i++)
            {
                var u = (float)i / N;
                c.LineTo(q.Pt(CX - hw + (2f * hw * u), BandY + BandInset + (FootWave(ch, u) * 1.35f)));
            }

            c.Stroke(Tint(accent, AccRim), 7f, closed: false);
        }

        // The soul, sitting in the hole.
        var soulMid = SoulPt(ch, Soul.X, Soul.Y);
        var sk = SoulScale(ch);
        var srx = Soul.Z * sk * ch[(int)Ch.Sx];
        var sry = Soul.W * sk * ch[(int)Ch.Sy];
        c.Ellipse(soulMid, srx, sry, Tint(body, Base));

        // Arm nubs on the soul, so they withdraw with it.
        var nubR = NubR * sk;
        foreach (var side in new[] { -1, 1 })
        {
            c.Ellipse(SoulPt(ch, CX + (NubDx * side), NubY), nubR, nubR, Tint(body, NubFill));
        }

        if (blush > 0f)
        {
            foreach (var side in new[] { -1, 1 })
            {
                c.Ellipse(SoulPt(ch, CX + ((EyeDx + 30f) * side), EyeY + 22f), 13f * sk, 8f * sk, BlushTint(blush));
            }
        }

        // -- the rest of the ink. The whorls inked themselves as they were laid down, above.
        c.EllipseStroke(ap, Aperture.Z * ch[(int)Ch.Sx], Aperture.W * ch[(int)Ch.Sy], ink, InkEdge);

        FootPath(c, ch);
        c.Stroke(ink, InkEdge);

        c.EllipseStroke(soulMid, srx, sry, ink, InkEdge);

        foreach (var side in new[] { -1, 1 })
        {
            c.EllipseStroke(SoulPt(ch, CX + (NubDx * side), NubY), nubR, nubR, ink, InkSuture);
        }

        // The face rides the SOUL: it shrinks and withdraws with the animal, which is what makes
        // a retreat read as going in rather than as the head merely sliding down.
        DrawEyes(c, Rig, eye, side => SoulPt(ch, CX + (side * EyeDx), EyeY), sk, sk, eyeTint, ink);
    }
}
