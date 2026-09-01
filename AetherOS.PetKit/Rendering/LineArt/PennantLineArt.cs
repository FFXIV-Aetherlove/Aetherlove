namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using static AetherOS.PetKit.Rendering.LineArt.LineShell;

/// <summary>
/// The Pennant, drawn. Ninth and last shell with a generator to port, and the only one whose
/// whole body is in motion at once.
///
/// <para><b>It is authored in CLOTH SPACE.</b> Every point is a pair (<c>u</c>, an offset from
/// the centre line, and <c>d</c>, depth below the hang line) and one travelling wave moves all
/// of them coherently. That is what makes the shell cheap: the edges, the folds, the braid and
/// the face are not separately animated, they are the same wave sampled at different depths.
/// Sampled rather than expressed as curves, and the generator says why: "every point takes the
/// wave at its OWN depth: a Bezier control point has no depth to be swayed at".</para>
///
/// <para><b>The wave is held at zero above the hang line</b>, because the cloth cannot move where
/// it is nailed to the bar. That single clamp is what keeps the roll and the cloth agreeing.</para>
///
/// <para><b>Three transforms, and the roll takes none of the wave.</b> The cloth takes all of it,
/// the FACE takes only <see cref="FaceRide"/> of it so it stays lookable-at while the body
/// travels, and the roll (the bar it hangs from) takes none: it is the thing being hung
/// FROM.</para>
///
/// <para><b>The folds are what carry the turn.</b> With a purely lateral wave the silhouette
/// sways but never foreshortens; two shadows travelling at a different phase lag from the edges
/// are what make it read as a surface rotating rather than a ribbon sliding.</para>
/// </summary>
public static class PennantLineArt
{
    // ---------------------------------------------------------------- geometry --
    // Lifted from the retired pennant-sheet/build_sheet.py unchanged: same names, same numbers.
    public const float Cell = 384f;

    public const float CX = 192f;
    private const float YFit = 0.76f;
    private const float TailsY = 306f;

    private static float MY(float v) => TailsY + ((v - 432f) * YFit);

    private static readonly float Hang = MY(150f);
    private static readonly float Span = TailsY - Hang;

    private const float HwTop = 88f, HwMid1 = 101f, HwMid2 = 82f, HwHem = 94f;
    private const float TailDx = 70f;
    private static readonly float DShoulder = (MY(372f) - Hang) / Span;
    private const float DNotch = 0.78f;
    private const float Tuck = 24f;
    private static readonly float DTuck = -Tuck / Span;

    /// <summary>The wave: how far, how many, and how sharply it grows with depth.</summary>
    private const float Amp = 11f, Waves = 0.55f, DepthPow = 1.15f;

    /// <summary>How much of the wave the FACE takes. Less than all of it, so the creature stays
    /// lookable-at while its body travels.</summary>
    private const float FaceRide = 0.35f;

    private static readonly float RollCy = MY(132f);
    private const float RollHl = 106f, RollHh = 17f, CapR = 26f;

    private const float EyeDx = 32f, EyeY = 196f;
    private const float NubR = 16f, KnotR = 13f;
    private const float FaceLift = 42f;

    /// <summary>How far the whole face sits above where the sheet put it.
    ///
    /// <para>A departure from the generator's numbers, on the owner's eye, and worth recording
    /// since the next person to read this file will find a face that does not line up with the
    /// PNGs. The mouth sat at 243, and the hem's notch climbs to meet it as the swallowtail
    /// closes - so on a shell that is a triangle pointing up, the lowest feature is the one with
    /// the least room. Raising the pair together keeps the face's own spacing exactly as
    /// authored and simply gives the mouth its clearance.</para></summary>
    private const float FaceRaise = 22f;

    private static readonly float DNub = (208f - Hang) / Span;
    private static readonly float DFace = (EyeY - FaceRaise - Hang) / Span;
    private static readonly float DMouth = (243f - FaceRaise - Hang) / Span;
    private static readonly float DBody = (205f - Hang) / Span;
    private static readonly float HeadY = RollCy - RollHh + 8f;

    private const int Steps = 26;

    private static readonly EyeRig Rig = new(
        Dx: EyeDx, Y: EyeY, Rx: 27f, Ry: 33f,
        PupilRx: 17f, PupilRy: 21f, RingW: 9f, PupilOut: 3f,
        BigDx: 8f, BigDy: 11f, BigR: 6.5f,
        SmallDx: 6.5f, SmallDy: 9f, SmallR: 3.2f,
        ShutBow: 16f, LashW: 10f);

    /// <summary>Cloth. The slackest thing on the roster after the Jelly - but its own wave is
    /// already doing the moving, so the spring only has to carry the body under it.</summary>
    public static readonly Material Stuff = new(Springiness: 0.55f, TrimLag: 0.60f);

    public static Vector2 PartOrigin(string part) => part switch
    {
        "roll" => new Vector2(CX, RollCy),
        _ => new Vector2(CX, Hang),
    };

    public static float InkWidth { get; set; } = 12f;

    /// <summary>Half-width of the cloth at parameter t along its edge, 0 at the hang line and 1
    /// at the shoulder where the swallowtail starts.</summary>
    private static float HalfWidth(float t)
    {
        var m = 1f - t;
        return (m * m * m * HwTop) + (3f * m * m * t * HwMid1) + (3f * m * t * t * HwMid2) + (t * t * t * HwHem);
    }

    // ------------------------------------------------------------------- poses --

    /// <summary>Matching the generator's <c>P(sx, sy, dy, phase, amp, eye, blush)</c>. phase is
    /// where the travelling wave has got to; amp is how hard it is blowing.</summary>
    private static Key K(float sx, float sy, float dy, float phase, float amp, EyeState eye, bool blush = false)
    {
        var c = Neutral();
        c[(int)Ch.Sx] = sx;
        c[(int)Ch.Sy] = sy;
        c[(int)Ch.Dy] = dy;
        c[(int)Ch.Phase] = phase;
        c[(int)Ch.Amp] = amp;
        return new Key(c, eye, blush);
    }

    private static readonly Key[] Poses =
    [
        // idle 0-7: one full pass of the wave, and the body riding it.
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Open),
        K(1.000f, 1.000f, -1f, 0.1250f, 1.00f, Open),
        K(1.000f, 1.000f, -2f, 0.2500f, 1.00f, Open),
        K(1.000f, 1.000f, -1f, 0.3750f, 1.00f, Open),
        K(1.000f, 1.000f, 0f, 0.5000f, 1.00f, Open),
        K(1.000f, 1.000f, 1f, 0.6250f, 1.00f, Open),
        K(1.000f, 1.000f, 2f, 0.7500f, 1.00f, Open),
        K(1.000f, 1.000f, 1f, 0.8750f, 1.00f, Open),

        // blink 8-10
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Open),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Shut),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, HalfShut),

        // boop 11-16: the wind gets up. Amp more than doubles and the wave races.
        K(1.010f, 0.980f, -2f, 0.1000f, 1.60f, Wide),
        K(1.060f, 0.910f, 3f, 0.2800f, 2.10f, Wide),
        K(1.080f, 0.870f, 5f, 0.4600f, 2.30f, Squint),
        K(0.950f, 1.070f, -5f, 0.6600f, 1.70f, Wide),
        K(1.020f, 0.980f, 1f, 0.8400f, 1.20f, Open),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Happy, blush: true),

        // nap 17-22: the air goes still. Amp drops to a third and the cloth hangs.
        K(1.000f, 1.020f, 3f, 0.0000f, 0.35f, Shut, blush: true),
        K(1.000f, 1.030f, 4f, 0.1667f, 0.35f, Shut, blush: true),
        K(1.000f, 1.040f, 6f, 0.3333f, 0.35f, Shut, blush: true),
        K(1.000f, 1.050f, 6f, 0.5000f, 0.35f, Shut, blush: true),
        K(1.000f, 1.040f, 5f, 0.6667f, 0.35f, Shut, blush: true),
        K(1.000f, 1.030f, 4f, 0.8333f, 0.35f, Shut, blush: true),

        // hop 23-32: a banner does not hop, it BILLOWS - the wind takes it up and drops it.
        K(1.030f, 0.960f, 3f, 0.0600f, 1.20f, Open),
        K(1.080f, 0.890f, 8f, 0.2000f, 1.50f, Squint),
        K(0.940f, 1.100f, -10f, 0.3400f, 2.20f, Wide),
        K(0.950f, 1.090f, -30f, 0.4800f, 2.40f, Wide),
        K(0.980f, 1.030f, -40f, 0.6200f, 2.00f, Open),
        K(0.960f, 1.060f, -28f, 0.7600f, 2.20f, Open),
        K(0.940f, 1.090f, -10f, 0.9000f, 2.40f, Wide),
        K(1.080f, 0.870f, 10f, 0.0400f, 1.60f, Squint),
        K(1.040f, 0.960f, 4f, 0.1800f, 1.20f, Open),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Open),

        // 33-37: the five rest-registered eye cells, from this shell's own rest pose.
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, ThreeQ),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, HalfShut),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Quarter),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Drowsy),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Heavy),
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

    /// <summary>Unused by this shell's own drawing - it poses in cloth space rather than through
    /// a LinePose - but kept so the caller needs no special case.</summary>
    public static LinePose Posed(Channels c) =>
        new(c[(int)Ch.Sx], c[(int)Ch.Sy], c[(int)Ch.Dy], CX, TailsY);

    // ------------------------------------------------------------- cloth space --

    /// <summary>The travelling wave. HELD AT ZERO above the hang line, because the cloth cannot
    /// move where it is nailed to the bar.</summary>
    private static float Sway(Channels c, float d)
    {
        var dd = MathF.Max(0f, d);
        return Amp * c[(int)Ch.Amp] * MathF.Pow(dd, DepthPow)
            * MathF.Sin(MathF.Tau * (c[(int)Ch.Phase] - (dd * Waves)));
    }

    private static Vector2 Pt(Channels c, float u, float d) => new(
        CX + (u * c[(int)Ch.Sx]) + Sway(c, d),
        Hang + (d * Span * c[(int)Ch.Sy]) + c[(int)Ch.Dy]);

    /// <summary>The face rides only part of the wave, and half the scale deform.</summary>
    private static Vector2 FacePt(Channels c, float u, float d)
    {
        var ex = 1f + ((c[(int)Ch.Sx] - 1f) * 0.5f);
        var ey = 1f + ((c[(int)Ch.Sy] - 1f) * 0.5f);
        return new Vector2(
            CX + (u * ex) + (Sway(c, d) * FaceRide),
            Hang + (d * Span * ey) + c[(int)Ch.Dy]);
    }

    /// <summary>The bar it hangs from: a quarter of the scale deform and NONE of the wave. It is
    /// the thing being hung from, so it cannot ride what it is holding up.</summary>
    private static Vector2 RollPt(Channels c, float u, float y)
    {
        var rx = 1f + ((c[(int)Ch.Sx] - 1f) * 0.25f);
        var ry = 1f + ((c[(int)Ch.Sy] - 1f) * 0.25f);
        return new Vector2(CX + (u * rx), Hang + ((y - Hang) * ry) + c[(int)Ch.Dy]);
    }

    private static Vector2 Edge(Channels c, int side, float t) => Pt(c, side * HalfWidth(t), DShoulder * t);

    private static Vector2 Nub(Channels c, int side) =>
        Pt(c, side * (HalfWidth(DNub / DShoulder) - (NubR * 0.45f)), DNub);

    private static Vector2 Tail(Channels c, int side) => Pt(c, side * TailDx, 1f);

    public static Vector2 Anchor0(string name) => new(CX, Hang);

    public static Vector2 Anchor(string name, Channels c) => name switch
    {
        "head" => RollPt(c, 0f, HeadY),
        "face" => FacePt(c, 0f, DFace - (FaceLift / Span)),
        "body" => Pt(c, 0f, DBody),
        "mouth" => FacePt(c, 0f, DMouth),
        "handL" => Nub(c, -1),
        "handR" => Nub(c, 1),
        _ => Pt(c, 0f, DBody),
    };


    // -------------------------------------------------------------------- paths --

    /// <summary>The cloth outline: from the tuck behind the roll, down one edge, round the
    /// swallowtail and back up the other. SAMPLED, because every point takes the wave at its own
    /// depth and a Bezier control point has no depth to be swayed at.</summary>
    private static List<Vector2> ClothPoints(Channels c)
    {
        var pts = new List<Vector2>((Steps * 2) + 8)
        {
            new(CX - (HwTop * c[(int)Ch.Sx]) + Sway(c, DTuck), Hang + (DTuck * Span * c[(int)Ch.Sy]) + c[(int)Ch.Dy]),
        };

        for (var i = 0; i <= Steps; i++)
        {
            pts.Add(Edge(c, -1, (float)i / Steps));
        }

        pts.Add(Tail(c, -1));
        pts.Add(Pt(c, 0f, DNotch));
        pts.Add(Tail(c, 1));

        for (var i = Steps; i >= 0; i--)
        {
            pts.Add(Edge(c, 1, (float)i / Steps));
        }

        pts.Add(new Vector2(CX + (HwTop * c[(int)Ch.Sx]) + Sway(c, DTuck), Hang + (DTuck * Span * c[(int)Ch.Sy]) + c[(int)Ch.Dy]));
        return pts;
    }

    private static float RollSx(Channels c) => 1f + ((c[(int)Ch.Sx] - 1f) * 0.25f);

    private static float RollSy(Channels c) => 1f + ((c[(int)Ch.Sy] - 1f) * 0.25f);

    /// <summary>The roll as ONE closed silhouette: bar plus a disc at each end. Drawn as a single
    /// path on purpose - three overlapping shapes would each need their own outline, and every
    /// one of those would draw its hidden half straight across the shape in front of it. A union
    /// outline has no inside to draw.</summary>
    private static List<Vector2> RollPoints(Channels c)
    {
        var dx = MathF.Sqrt(MathF.Max(1f, (CapR * CapR) - (RollHh * RollHh)));
        var ax = RollHl - dx;
        float top = RollCy - RollHh, bot = RollCy + RollHh;
        var r = CapR * (RollSx(c) + RollSy(c)) * 0.5f;

        var pts = new List<Vector2>(48) { RollPt(c, -ax, top), RollPt(c, ax, top) };

        var right = RollPt(c, RollHl, RollCy);
        for (var i = 1; i < 18; i++)
        {
            var a = (-MathF.PI / 2f) + (MathF.PI * i / 18f);
            pts.Add(right + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r));
        }

        pts.Add(RollPt(c, ax, bot));
        pts.Add(RollPt(c, -ax, bot));

        var left = RollPt(c, -RollHl, RollCy);
        for (var i = 1; i < 18; i++)
        {
            var a = (MathF.PI / 2f) + (MathF.PI * i / 18f);
            pts.Add(left + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r));
        }

        return pts;
    }

    /// <summary>A fold, running the length of the cloth on its own phase LAG. The folds are what
    /// carry the turn: with a purely lateral wave the silhouette sways but never foreshortens,
    /// and two shadows travelling at a different rate from the edges are what make it read as a
    /// surface rotating rather than a ribbon sliding.</summary>
    private static List<Vector2> FoldPoints(Channels c, float u0, float lag)
    {
        var pts = new List<Vector2>(13);
        for (var i = 0; i <= 12; i++)
        {
            var d = 0.10f + ((i / 12f) * (DShoulder - 0.10f));
            pts.Add(new Vector2(
                CX + (u0 * c[(int)Ch.Sx]) + Sway(c, d + lag),
                Hang + (d * Span * c[(int)Ch.Sy]) + c[(int)Ch.Dy]));
        }

        return pts;
    }

    /// <summary>The light down the lit edge, DERIVED from the edge - offset inboard from the same
    /// samples at the same depths - rather than drawn as its own curve near it. The Jelly cut a
    /// rim light for being a stroke inside a shape instead of a light on its boundary; this one
    /// cannot drift, because it has no geometry of its own.</summary>
    private static List<Vector2> RimPoints(Channels c)
    {
        var pts = new List<Vector2>(11);
        for (var i = 0; i <= 10; i++)
        {
            var t = 0.10f + ((i / 10f) * 0.62f);
            pts.Add(Pt(c, -(HalfWidth(t) - 9f), DShoulder * t));
        }

        return pts;
    }

    /// <summary>The unlit side: a band run down INSIDE the right edge. Light is upper-left on
    /// every shell in the set, so the far edge is the one that loses it.</summary>
    private static List<Vector2> ShadePoints(Channels c)
    {
        var pts = new List<Vector2>(13);
        for (var i = 0; i <= 12; i++)
        {
            var t = 0.06f + ((i / 12f) * 0.86f);
            pts.Add(Pt(c, HalfWidth(t) - 20f, DShoulder * t));
        }

        return pts;
    }

    /// <summary>The braid: the swallowtail again, pulled up, so the hem's two committed angles
    /// read twice - once in silhouette, once in colour. Subdivided, because a band walked along a
    /// five point polyline pinches at every corner.
    ///
    /// <para>Its ends sit at the SHOULDERS - the corner where the flank stops and the swallowtail
    /// begins - because that is the one point on the hem that is a hard corner on both paths, so
    /// it is the only place the two can be made to agree by construction. Placed anywhere up the
    /// flank instead, the braid started cutting inward while the edge below it was still running
    /// down to the shoulder, and the wedge that opened widened all the way to the tails.</para>
    ///
    /// <para>The offsets are then chosen to keep the whole W PARALLEL to the hem rather than
    /// welded to it: the ends move inboard in u and the tails and notch move up, and measured
    /// perpendicular to each leg those come to about five units all the way along. Welded flat
    /// onto the outline it read as too much; short of it at one end and not the other it read as
    /// separated. A constant offset is the only version that reads as a braid.</para></summary>
    private static List<Vector2> HemPoints(Channels c, float inset, float lift)
    {
        Vector2[] corners =
        [
            Pt(c, -(HwHem - (inset * 0.5f)), DShoulder),
            Pt(c, -(TailDx - (inset * 0.2f)), 1f - lift),
            Pt(c, 0f, DNotch - lift),
            Pt(c, TailDx - (inset * 0.2f), 1f - lift),
            Pt(c, HwHem - (inset * 0.5f), DShoulder),
        ];

        var pts = new List<Vector2>(33) { corners[0] };
        for (var s = 0; s < 4; s++)
        {
            for (var i = 1; i <= 8; i++)
            {
                pts.Add(Vector2.Lerp(corners[s], corners[s + 1], i / 8f));
            }
        }

        // Rounded at the corners, which is what the two breaks at the bottom of the W were.
        // A strip takes its width along the NORMAL, and at a hard vertex there is no one normal
        // to take it along: the point sits between two steep legs while the chord across it runs
        // almost flat, so the band waists there and the two legs read as separate marks that stop
        // short of each other. Filleting the turn is the fix rather than widening or overlapping
        // it - a band with no sharp vertex has nothing to pinch at.
        return Smooth(pts, 2);
    }

    /// <summary>Chaikin, with the ends pinned so the braid still lands on the flanks. Two passes
    /// only, so it rounds the turn without softening the swallowtail into a curve.</summary>
    private static List<Vector2> Smooth(List<Vector2> pts, int passes)
    {
        for (var k = 0; k < passes; k++)
        {
            var next = new List<Vector2>((pts.Count * 2) + 2) { pts[0] };
            for (var i = 0; i < pts.Count - 1; i++)
            {
                next.Add(Vector2.Lerp(pts[i], pts[i + 1], 0.25f));
                next.Add(Vector2.Lerp(pts[i], pts[i + 1], 0.75f));
            }

            next.Add(pts[pts.Count - 1]);
            pts = next;
        }

        return pts;
    }

    /// <summary>The header stripe, sitting just under the roll across the full width.</summary>
    private static List<Vector2> HeaderPoints(Channels c)
    {
        var pts = new List<Vector2>(9);
        for (var i = 0; i <= 8; i++)
        {
            pts.Add(Pt(c, -HwTop + 6f + ((i / 8f) * ((HwTop - 6f) * 2f)), 0.075f));
        }

        return pts;
    }

    /// <summary>A band run the length of the bar at a given row, sampled so the clip has
    /// something to cut. Reaches a little into the caps at both ends on purpose - the light runs
    /// the whole pole, and where it leaves the silhouette the clip takes it off.</summary>
    private static void RollBand(LineCanvas c, Channels ch, float y, float w)
    {
        var end = RollHl + (CapR * 0.55f);
        var line = new List<Vector2>(33);
        for (var i = 0; i <= 32; i++)
        {
            line.Add(RollPt(ch, -end + ((i / 32f) * end * 2f), y));
        }

        c.BandPath(line, w, w);
    }

    private static void Path(LineCanvas c, List<Vector2> pts, bool closed)
    {
        c.MoveTo(pts[0]);
        for (var i = 1; i < pts.Count; i++)
        {
            c.LineTo(pts[i]);
        }

        if (closed)
        {
            c.LineTo(pts[0]);
        }
    }

    /// <inheritdoc cref="JellyLineArt.Pin"/>
    ///
    /// <para>The rest point arrives in CELL space and this shell thinks in cloth space, so it is
    /// inverted on the way in: an offset from the centre line, and a depth below the hang line as
    /// a fraction of the span. That depth is the whole reason a pin on this creature cannot be a
    /// stored point - it is what decides how much of the travelling wave the pin takes.</para>
    public static Vector2 Pin(string name, PinKind kind, Vector2 rest, Channels c)
    {
        var u = rest.X - CX;
        var d = (rest.Y - Hang) / Span;

        // The baked point already carries the REST pose's wave, and Pt adds the live one, so the
        // rest wave has to come back out or every body pin sits a few pixels off its own body on
        // every frame including rest. At c == RestCh both arms below reduce to rest.X exactly.
        return kind switch
        {
            PinKind.Hand => Anchor(name, c),
            PinKind.Head => RollPt(c, u, rest.Y),
            PinKind.Face => FacePt(c, u - (Sway(RestCh, d) * FaceRide), d),
            _ => Pt(c, u - Sway(RestCh, d), d),
        };
    }

    /// <summary>The rest pose's channels, which the baked anchor table was measured against.</summary>
    private static readonly Channels RestCh = Poses[0].Ch;

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
        float sx = ch[(int)Ch.Sx], sy = ch[(int)Ch.Sy];

        // Nubs first, then the cloth over their inner halves.
        var nubR = NubR * (sx + sy) * 0.5f;
        for (var i = 0; i < 2; i++)
        {
            c.Ellipse(Nub(ch, i == 0 ? -1 : 1), nubR, nubR, Tint(body, NubFill));
        }

        Path(c, ClothPoints(ch), closed: true);
        c.Fill(Tint(body, Base));
        var cloth = c.Capture();

        // Everything painted ON the cloth is cut to it, and every one of them is a BAND - long,
        // thin and curved - so they fill as strips. A fan from a band's centroid folds it into a
        // V, which is the fault the Nautilus and the Spintop each had once.
        //
        // They also take the CLOTH'S OWN pose, not the lagged one. Paint does not lag: a mark is
        // not on a body, it is on its SURFACE, and on a shell whose whole surface is a travelling
        // wave a beat of disagreement would show everywhere at once.
        c.BandPath(ShadePoints(ch), 40f * sx, 40f * sx);
        c.FillBandIn(cloth, Tint(body, Shadow) with { W = 0.40f });

        c.BandPath(FoldPoints(ch, -32f, 0.10f), 8f * sx, 8f * sx);
        c.FillBandIn(cloth, Tint(body, Shadow) with { W = 0.55f });
        c.BandPath(FoldPoints(ch, 28f, -0.13f), 8f * sx, 8f * sx);
        c.FillBandIn(cloth, Tint(body, Shadow) with { W = 0.55f });

        c.BandPath(RimPoints(ch), 8f * sx, 8f * sx);
        c.FillBandIn(cloth, Tint(body, Rim) with { W = 0.85f });

        c.BandPath(HeaderPoints(ch), 15f * sy, 15f * sy);
        c.FillBandIn(cloth, Tint(accent, AccBase));

        c.BandPath(HemPoints(ch, 12f, 0.030f), 12f * sy, 12f * sy);
        c.FillBandIn(cloth, Tint(accent, AccBase));

        // The tail knots, over the hem they finish.
        var knotR = KnotR * (sx + sy) * 0.5f;
        for (var i = 0; i < 2; i++)
        {
            c.Ellipse(Tail(ch, i == 0 ? -1 : 1), knotR, knotR, Tint(body, Base));
        }

        // The roll, over the cloth's top edge - which is why that edge is never inked.
        Path(c, RollPoints(ch), closed: true);
        c.Fill(Tint(body, Base));
        var roll = c.Capture();

        // The shading on the bar is two BANDS, SAMPLED along their length.
        //
        // Both halves of that matter and the second is the one I got wrong twice. A clipped band
        // is only as accurate as its stations: a two point band has nothing between its ends, so
        // when the clip pulls one end in and collapses the other the whole strip becomes a single
        // pair of triangles - which is exactly the wedge that kept appearing on the rail. A band
        // that runs off its host has to be sampled ACROSS the crossing, or the clip has no
        // geometry to cut and simply reshapes the quad.
        //
        // (The first half, for the record: FillInPoly fans from a centroid, and a long thin strip
        // stops being star-shaped about its own centre once its ends are trimmed. Neither a fan
        // nor a two point strip can draw a line of light down a rail.)
        var ry = RollSy(ch);
        RollBand(c, ch, RollCy + (RollHh * 0.62f), RollHh * 0.72f);
        c.FillBandIn(roll, Tint(body, Shadow) with { W = 0.55f });

        RollBand(c, ch, RollCy - (RollHh * 0.44f), 7f * ry);
        c.FillBandIn(roll, Tint(body, Rim) with { W = 0.85f });

        // The curls at the ends of the pole catch the light like everything else on the shell:
        // a crescent on the outer turn of each scroll and a shorter one on the inner turn, both
        // to the upper left, which is where the light is on every shell in the set. Without them
        // the caps read as flat discs with a spiral scratched onto them, and the pole lost the
        // only modelling it has.
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            var mid = RollPt(ch, side * RollHl, RollCy);
            var rr = CapR * (RollSx(ch) + ry) * 0.5f;
            // Brighter than the shell rim: the curls are the smallest marks on the pet and the
            // only modelling the pole has, so at the sizes this draws at they need to carry
            // further than a body tint does.
            var curl = Vector4.Lerp(Tint(body, Rim), Spark, 0.45f);
            c.Arc(mid, rr * 0.72f, -2.62f, -1.34f, curl, 8f);
            c.Arc(mid, rr * 0.34f, -2.50f, -1.60f, curl, 5.5f);
        }

        // The band on the bar, which takes the ROLL'S pose because that is the surface it is on.
        // Sampled down its length and stopped just short of both rails: this was the last two
        // point band on the shell and it had the same fault as the rail lights - two stations,
        // both sitting exactly ON the outline, so the clip collapsed one end and kept the other
        // and the strip came out as a wedge. It is the small V that kept showing on the bar.
        var bandTop = RollCy - RollHh + 3f;
        var bandBot = RollCy + RollHh - 3f;
        var barBand = new List<Vector2>(13);
        for (var i = 0; i <= 12; i++)
        {
            barBand.Add(RollPt(ch, -14f, bandTop + ((i / 12f) * (bandBot - bandTop))));
        }

        c.BandPath(barBand, 11f * RollSx(ch), 11f * RollSx(ch));
        c.FillBandIn(roll, Tint(accent, AccShadow));

        if (blush > 0f)
        {
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? -1 : 1;
                c.Ellipse(FacePt(ch, side * (EyeDx + 30f), DFace + (22f / Span)), 14f, 9f, BlushTint(blush));
            }
        }

        // -- the ink. The cloth inks OPEN: its top edge is behind the roll, and closing the
        // stroke would lay a line straight across the bar.
        Path(c, ClothPoints(ch), closed: false);
        c.Stroke(ink, InkWidth, closed: false);

        Path(c, RollPoints(ch), closed: true);
        c.Stroke(ink, 12f);

        // The rolled ends: a spiral off centre, stopping well inside the disc so it can never
        // reach the outline and thicken it.
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            var mid = RollPt(ch, side * RollHl, RollCy);
            var rr = CapR * (RollSx(ch) + ry) * 0.5f;
            c.MoveTo(mid + new Vector2(rr * 0.60f * side, 0f));
            for (var s = 1; s <= 30; s++)
            {
                var a = (s / 30f) * (MathF.Tau * 0.78f);
                var r2 = rr * (0.60f - (0.44f * (s / 30f)));
                c.LineTo(mid + new Vector2(MathF.Cos(a) * r2 * side, MathF.Sin(a) * r2));
            }

            c.Stroke(ink, 8f, closed: false);
        }

        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            c.EllipseStroke(Tail(ch, side), knotR, knotR, ink, 8f);

            // The nub arc never reaches round the back: the half that would sit inside the cloth
            // is the half the cloth is drawn over - so each nub shows its OUTBOARD half, the left
            // one sweeping left and the right one sweeping right. Sweeping by side * PI mirrored
            // the DIRECTION rather than the half, which is not the same thing: both ends came out
            // walking through the same side, so one nub pointed out of the body and the other
            // pointed into it.
            var at = Nub(ch, side);
            var from = side < 0 ? MathF.PI / 2f : -MathF.PI / 2f;
            c.Arc(at, nubR, from, from + MathF.PI, ink, 11f);
        }

        var fex = 1f + ((sx - 1f) * 0.5f);
        var fey = 1f + ((sy - 1f) * 0.5f);
        DrawEyes(c, Rig, eye, side => FacePt(ch, side * EyeDx, DFace), fex, fey, eyeTint, ink);
    }
}
