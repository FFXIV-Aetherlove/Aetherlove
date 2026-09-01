namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using static AetherOS.PetKit.Rendering.LineArt.LineShell;

/// <summary>
/// The Moth, drawn. Sixth shell, and the first in a while that fought nothing: the pose is the
/// Jelly's squash-about-an-underside with one channel added, and the ink is ordinary centred
/// strokes rather than the Serpent's stamped annuli.
///
/// <para><b>Everything that moves, moves in the wings.</b> Its generator is explicit that this
/// shell does NOT rotate: "the Spintop leans and the Lantern swings because both hang or balance;
/// a moth in level flight does neither, and a tilted moth reads as a falling moth". So `theta`
/// does not tilt anything: it opens and closes the wings, through <see cref="Spread"/>.</para>
///
/// <para><b>A wing hinged at the thorax sweeps DOWN as it spreads.</b> The span and the tip drop
/// are one motion: holding the tip height while the span moved "read as a picture being stretched"
/// rather than as a creature beating its wings. Every wing coordinate scales on the spread and
/// carries a share of the drop with it.</para>
///
/// <para><b>Both wing roots sit inside the thorax on purpose</b>, and that is a draw-order note
/// rather than a shape one: the wings go down first, the body covers their roots, and the ink
/// then only has to skip the closing edge. The generator's own words: "no clip is needed
/// anywhere", which after five shells of masks-as-draw-order is a pleasant thing to read.</para>
/// </summary>
public static class MothLineArt
{
    // ---------------------------------------------------------------- geometry --
    // Lifted from the retired moth-sheet/build_sheet.py unchanged: same names, same numbers.
    public const float Cell = 384f;

    public const float CX = 192f;
    private const float WingHw = 150f;
    private const float BodyCy = 232f, BodyRx = 58f, BodyRy = 96f;
    private const float BodyBot = BodyCy + BodyRy;

    private const float RuffY = 158f, RuffH = 40f, RuffHw = 62f;
    private const float EyeDx = 24f, EyeY = 206f;
    private const float FaceLift = 36f;
    private const float MouthY = EyeY + 40f;
    private const float NubY = 262f, NubR = 15f;

    private const float WingAmp = 0.055f, WingDrop = 7f;

    /// <summary>The wing markings, in the same right-hand offsets as the wings, so they ride the
    /// spread with the wing they are painted on rather than sliding across it.</summary>
    private static readonly Vector3 EyeSpot = new(92f, 158f, 15f);

    private static readonly Vector3 HindSpot = new(72f, 278f, 9f);

    /// <summary>The abdomen's banding, low, so the body has some length to it.</summary>
    private static readonly float[] Bands = [282f, 304f];

    private static readonly EyeRig Rig = new(
        Dx: EyeDx, Y: EyeY, Rx: 25f, Ry: 30f,
        PupilRx: 16f, PupilRy: 20f, RingW: 8f, PupilOut: 3f,
        BigDx: 7.5f, BigDy: 11f, BigR: 6f,
        SmallDx: 6f, SmallDy: 9f, SmallR: 3f,
        ShutBow: 15f, LashW: 9f);

    /// <summary>Wings and a soft body. Slacker than the Crab, nowhere near the Jelly - what
    /// wobbles on a moth is already being driven by the wingbeat.</summary>
    public static readonly Material Stuff = new(Springiness: 0.35f, TrimLag: 0.55f);

    public static Vector2 PartOrigin(string part) => new(CX, BodyBot);

    public static float InkWidth { get; set; } = 12f;

    // ------------------------------------------------------------------- poses --

    /// <summary>Matching the generator's <c>P(theta, sx, sy, dy, eye, blush)</c>. theta opens and
    /// closes the wings and bobs the body; sx/sy scale about the body's own bottom.</summary>
    private static Key K(float theta, float sx, float sy, float dy, EyeState eye, bool blush = false)
    {
        var c = Neutral();
        c[(int)Ch.Theta] = theta;
        c[(int)Ch.Sx] = sx;
        c[(int)Ch.Sy] = sy;
        c[(int)Ch.Dy] = dy;
        return new Key(c, eye, blush);
    }

    private static readonly Key[] Poses =
    [
        // idle 0-7: one full wingbeat, and the body bobbing on it.
        K(0.0000f, 1.000f, 1.000f, 0f, Open),
        K(0.1250f, 1.000f, 1.000f, -1f, Open),
        K(0.2500f, 1.000f, 1.000f, -2f, Open),
        K(0.3750f, 1.000f, 1.000f, -1f, Open),
        K(0.5000f, 1.000f, 1.000f, 0f, Open),
        K(0.6250f, 1.000f, 1.000f, 1f, Open),
        K(0.7500f, 1.000f, 1.000f, 2f, Open),
        K(0.8750f, 1.000f, 1.000f, 1f, Open),

        // blink 8-10
        K(0.0000f, 1.000f, 1.000f, 0f, Open),
        K(0.0000f, 1.000f, 1.000f, 0f, Shut),
        K(0.0000f, 1.000f, 1.000f, 0f, HalfShut),

        // boop 11-16: the wings snap through most of a beat while the body flinches.
        K(0.2500f, 1.020f, 0.980f, -3f, Wide),
        K(0.3000f, 1.050f, 0.950f, 1f, Wide),
        K(0.6800f, 1.030f, 0.960f, 3f, Squint),
        K(0.8000f, 0.980f, 1.030f, -2f, Wide),
        K(0.1000f, 1.010f, 0.990f, 0f, Open),
        K(0.2500f, 1.000f, 1.000f, 0f, Happy, blush: true),

        // nap 17-22: the wings settle almost shut and barely stir.
        K(0.6200f, 1.000f, 1.000f, 2f, Shut, blush: true),
        K(0.6600f, 1.000f, 1.000f, 3f, Shut, blush: true),
        K(0.7000f, 1.000f, 1.000f, 3f, Shut, blush: true),
        K(0.7400f, 1.000f, 1.000f, 3f, Shut, blush: true),
        K(0.7000f, 1.000f, 1.000f, 2f, Shut, blush: true),
        K(0.6600f, 1.000f, 1.000f, 2f, Shut, blush: true),

        // hop 23-32: a moth does not hop, it FLITS - the crouch is a wing gather.
        K(0.6000f, 1.030f, 0.970f, 3f, Open),
        K(0.7000f, 1.060f, 0.930f, 7f, Squint),
        K(0.2500f, 0.950f, 1.080f, -10f, Wide),
        K(0.1500f, 0.960f, 1.060f, -32f, Wide),
        K(0.2500f, 0.990f, 1.020f, -44f, Open),
        K(0.3500f, 0.970f, 1.050f, -30f, Open),
        K(0.2000f, 0.950f, 1.070f, -11f, Wide),
        K(0.7000f, 1.070f, 0.910f, 7f, Squint),
        K(0.5500f, 1.020f, 0.970f, 3f, Open),
        K(0.0000f, 1.000f, 1.000f, 0f, Open),

        // 33-37: the five rest-registered eye cells, from this shell's own rest pose.
        K(0.0000f, 1.000f, 1.000f, 0f, ThreeQ),
        K(0.0000f, 1.000f, 1.000f, 0f, HalfShut),
        K(0.0000f, 1.000f, 1.000f, 0f, Quarter),
        K(0.0000f, 1.000f, 1.000f, 0f, Drowsy),
        K(0.0000f, 1.000f, 1.000f, 0f, Heavy),
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

    /// <summary>Squash about the body's own underside - the hover line, this shell's ground
    /// relationship - then lift. No rotation: a tilted moth reads as a falling moth.</summary>
    public static LinePose Posed(Channels c) =>
        new(c[(int)Ch.Sx], c[(int)Ch.Sy], c[(int)Ch.Dy], CX, BodyBot);

    /// <summary>How far the wings are open, from the beat's own phase.</summary>
    private static float Spread(Channels c) => 1f + (WingAmp * MathF.Sin(MathF.Tau * c[(int)Ch.Theta]));

    // ------------------------------------------------------------------- wings --

    /// <summary>Forewing and hindwing outlines, as right-hand offsets from the centre line. The
    /// spread scales the span and DROPS the tips with it, because a wing hinged at the thorax
    /// sweeps down as it spreads.</summary>
    private static (Vector2[] Fore, Vector2[] Hind) WingPoints(float open)
    {
        var drop = WingAmp <= 0f ? 0f : WingDrop * (open - 1f) / WingAmp;

        Vector2[] fore =
        [
            new(34f, 192f),
            new(86f * open, 134f + (drop * 0.5f)),
            new(128f * open, 112f + drop),
            new(WingHw * open, 132f + drop),
            new(168f * open, 158f + drop),
            new(120f * open, 210f + (drop * 0.6f)),
            new(46f, 240f),
        ];

        Vector2[] hind =
        [
            new(36f, 236f),
            new(74f * open, 244f + (drop * 0.4f)),
            new(112f * open, 258f + (drop * 0.7f)),
            new(116f * open, 288f + drop),
            new(118f * open, 316f + drop),
            new(66f * open, 322f + (drop * 0.5f)),
            new(26f, 304f),
        ];

        return (fore, hind);
    }

    /// <summary>One wing from a seven-point list: root, three cubic controls to the tip, two
    /// back, and a second root. Left OPEN for the ink, so the closing edge - which runs through
    /// the thorax that covers it - is never stroked across it.</summary>
    private static void WingPath(LineCanvas c, LinePose q, Vector2[] pts, int side, float shrink = 1f, float lift = 0f)
    {
        Vector2 P(int i) => q.Pt(CX + (side * pts[i].X * shrink), pts[i].Y - lift);

        c.MoveTo(P(0));
        c.CubicTo(P(1), P(2), P(3));
        c.CubicTo(P(4), P(5), P(6));
    }

    private static Vector2 NubPt(LinePose q, int side)
    {
        var w = BodyRx * MathF.Sqrt(MathF.Max(0f, 1f - MathF.Pow((NubY - BodyCy) / BodyRy, 2f)));
        return q.Pt(CX + (side * w), NubY);
    }

    public static Vector2 Anchor0(string name) => name switch
    {
        "head" => new Vector2(CX, 142f),
        "face" => new Vector2(CX, EyeY - FaceLift),
        "body" => new Vector2(CX, BodyCy),
        "mouth" => new Vector2(CX, MouthY),
        "hem" => new Vector2(CX, BodyBot),
        _ => new Vector2(CX, BodyCy),
    };

    public static Vector2 Anchor(string name, Channels c)
    {
        var q = Posed(c);
        if (name is "handL" or "handR")
        {
            return NubPt(q, name == "handL" ? -1 : 1);
        }

        var a = Anchor0(name);
        return name is "face" or "head" ? q.EyePt(a.X, a.Y) : q.Pt2(a);
    }

    /// <inheritdoc cref="JellyLineArt.Pin"/>
    public static Vector2 Pin(string name, PinKind kind, Vector2 rest, Channels c)
    {
        var q = Posed(c);
        return kind switch
        {
            PinKind.Hand => Anchor(name, c),
            PinKind.Face or PinKind.Head => q.EyePt(rest.X, rest.Y),
            _ => q.Pt2(rest),
        };
    }

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
        var open = Spread(ch);
        var (fore, hind) = WingPoints(open);

        var bodyMid = q.Pt(CX, BodyCy);
        var brx = BodyRx * ch[(int)Ch.Sx];
        var bry = BodyRy * ch[(int)Ch.Sy];

        // WINGS: every fill, then the markings, then every outline - in that order and not
        // interleaved per wing. The sheet puts wing fills on the body layer, markings on the
        // accent and outlines on the overlay, so an outline sits above ALL the fills and above
        // the markings. Inking each wing as it was drawn, which is what the first cut did, let
        // the next wing's lit inset paint straight over the outline of the one before it.
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            foreach (var pts in new[] { hind, fore })
            {
                WingPath(c, q, pts, side);
                c.Fill(Tint(body, Shadow));
            }
        }

        // The lit inner wing, inset and lifted - and CLIPPED to the wing it is painted on.
        // Measured: lifting a lobe raises its top edge faster than shrinking pulls it in, so 41
        // of 81 sampled points of the inner forewing fall outside the outer one, overshooting the
        // top by the full 6. The sheet hides a sliver of that under its own outline and the rest
        // shows, which is the colour that was escaping past the line.
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            foreach (var (pts, k, lift) in new[] { (hind, 0.90f, 5f), (fore, 0.92f, 6f) })
            {
                WingPath(c, q, pts, side);
                var outline = c.Capture();
                WingPath(c, q, pts, side, k, lift);
                c.FillInPoly(outline, Tint(body, Base));
            }
        }

        DrawWingMarks(c, q, ch, trimCh, open, accent);

        // The outlines last, as OPEN paths: both wings close through the thorax, so a closed
        // stroke would lay a line straight across the body they tuck into. Drawn BEFORE the body
        // so the thorax buries the roots and the arc that crosses it - which is the `nb` clip the
        // sheet needs because its overlay draws after everything.
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            foreach (var pts in new[] { hind, fore })
            {
                WingPath(c, q, pts, side);
                c.Stroke(ink, 10f, closed: false);
            }
        }

        // The nubs, then the thorax over their inner halves.
        var nubR = NubR * (ch[(int)Ch.Sx] + ch[(int)Ch.Sy]) * 0.5f;
        for (var i = 0; i < 2; i++)
        {
            c.Ellipse(NubPt(q, i == 0 ? -1 : 1), nubR, nubR, Tint(body, NubFill));
        }

        c.Ellipse(bodyMid, brx, bry, Tint(body, Shadow));
        c.EllipseIn(
            bodyMid - new Vector2(7f * ch[(int)Ch.Sx], 7f * ch[(int)Ch.Sy]),
            brx - (7f * ch[(int)Ch.Sx]), bry - (7f * ch[(int)Ch.Sy]),
            bodyMid, brx, bry, Tint(body, Base));

        // The abdomen's banding, clipped to the body.
        // The abdomen's banding. Its half-width is SOLVED against the body's own ellipse at that
        // row rather than typed: the bands sit low, where an ellipse has fallen well in from its
        // widest, so a fixed 46 runs past the silhouette and the ends poke out below the outline.
        // A rectangle clip cannot help - the edge being crossed is a curve.
        foreach (var y in Bands)
        {
            var hw = (BodyRx * MathF.Sqrt(MathF.Max(0f, 1f - MathF.Pow((y - BodyCy) / BodyRy, 2f)))) - 7f;
            if (hw <= 2f)
            {
                continue;
            }

            c.MoveTo(q.Pt(CX - hw, y));
            c.QuadTo(q.Pt(CX, y + 9f), q.Pt(CX + hw, y));
            c.Stroke(Tint(body, Shadow), 8f * ch[(int)Ch.Sy], closed: false);
        }

        // The ruff: a CRESCENT, an arch whose underside curves back up, never a bowl - and
        // CLIPPED to the thorax, which is the whole difference between a collar and a hat. It
        // also carries NO INK: the sheet draws it once, filled, on the accent layer and never
        // strokes it. Outlined and unclipped, as the first cut had it, its two curves arc over
        // the crown and read as eyebrows on a face that already has plenty going on - which is
        // the bonnet the generator warns about, arrived at by a different route.
        RuffPath(c, q);
        c.FillIn(new Vector2(CX, BodyCy), BodyRx, BodyRy, Tint(accent, AccBase));

        if (blush > 0f)
        {
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? -1 : 1;
                c.Ellipse(q.EyePt(CX + (side * (EyeDx + 34f)), EyeY + 20f), 14f, 9f, BlushTint(blush));
            }
        }

        // -- the ink.
        c.EllipseStroke(bodyMid, brx, bry, ink, 12f);

        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            var at = NubPt(q, side);
            var quarter = MathF.PI / 2f;
            c.Arc(at, nubR, -quarter, side < 0 ? -MathF.PI - quarter : quarter, ink, 10f);
        }

        DrawEyes(c, Rig, eye, side => q.EyePt(CX + (side * EyeDx), EyeY), q.Ex, q.Ey, eyeTint, ink);
    }

    /// <summary>The wings' own markings, riding the spread with the wing they are painted on.
    /// They are TRIMMINGS, so they take the lagged pose.</summary>
    private static void DrawWingMarks(LineCanvas c, LinePose q, Channels ch, Channels trimCh, float open, Vector4 accent)
    {
        var t = Posed(trimCh);
        var topen = Spread(trimCh);

        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;

            var at = t.Pt(CX + (side * EyeSpot.X * topen), EyeSpot.Y);
            c.Ellipse(at, EyeSpot.Z * trimCh[(int)Ch.Sx], EyeSpot.Z * trimCh[(int)Ch.Sx], Tint(accent, AccBase));
            c.Ellipse(at, EyeSpot.Z * 0.38f * trimCh[(int)Ch.Sx], EyeSpot.Z * 0.38f * trimCh[(int)Ch.Sx], Tint(accent, AccRim));

            // The vein, sweeping out toward the tip.
            var va = t.Pt(CX + (side * 44f), 190f);
            var vb = t.Pt(CX + (side * 96f * topen), 150f);
            var vc = t.Pt(CX + (side * 132f * topen), 146f);
            c.MoveTo(va);
            c.CubicTo(vb, vb, vc);
            c.Stroke(Tint(accent, AccShadow) with { W = 0.85f }, 6f * trimCh[(int)Ch.Sx], closed: false);

            var hat = t.Pt(CX + (side * HindSpot.X * topen), HindSpot.Y);
            c.Ellipse(hat, HindSpot.Z * trimCh[(int)Ch.Sx], HindSpot.Z * trimCh[(int)Ch.Sx], Tint(accent, AccBase) with { W = 0.9f });
        }
    }

    private static void RuffPath(LineCanvas c, LinePose q)
    {
        const float Top = RuffY;
        const float Bot = RuffY + RuffH;
        const float Inner = RuffY + (RuffH * 0.60f);

        c.MoveTo(q.Pt(CX - RuffHw, Bot));
        c.CubicTo(
            q.Pt(CX - (RuffHw * 0.86f), Top + 14f),
            q.Pt(CX - (RuffHw * 0.42f), Top),
            q.Pt(CX, Top));
        c.CubicTo(
            q.Pt(CX + (RuffHw * 0.42f), Top),
            q.Pt(CX + (RuffHw * 0.86f), Top + 14f),
            q.Pt(CX + RuffHw, Bot));
        c.CubicTo(
            q.Pt(CX + (RuffHw * 0.62f), Inner + 2f),
            q.Pt(CX + (RuffHw * 0.30f), Inner),
            q.Pt(CX, Inner));
        c.CubicTo(
            q.Pt(CX - (RuffHw * 0.30f), Inner),
            q.Pt(CX - (RuffHw * 0.62f), Inner + 2f),
            q.Pt(CX - RuffHw, Bot));
    }
}
