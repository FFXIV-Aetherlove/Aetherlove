namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using static AetherOS.PetKit.Rendering.LineArt.LineShell;

/// <summary>
/// The Spintop, drawn. Eighth shell, and the second that rotates, but where the Lantern hangs
/// and swings from its ring, this one BALANCES and wobbles about its tip.
///
/// <para><b>Rotating about the tip is what makes the lean a wobble rather than a slide</b>: the
/// point stays kissing the floor. Same rule as the Lantern for how it is applied: the rotation
/// goes on finished points and never into the pose, because a rotation is a similarity and stroke
/// widths must come through it unchanged, while the squash goes into the points, because a
/// non-uniform scale in a group WOULD thin the ink.</para>
///
/// <para><b>The rings are open arcs, never closed ellipses</b>, and the generator is precise
/// about why: "a ring you can only see the front of is a groove cut into a turning solid, where a
/// full ellipse is a hoop sitting on it". Their tilt breathes with <c>theta</c>, which is what
/// sells the turning without animating any rotation of the pattern itself.</para>
///
/// <para><b>Spin is ambient.</b> A top does not stop spinning because it blinked, so
/// <see cref="Ch.Spin"/> joins theta as a channel the creature owns rather than the clip: the
/// nap banks it to 0.45 and the hop drives it to 1.50, and both are acted, but an eye clip has no
/// opinion and does not get one.</para>
/// </summary>
public static class SpintopLineArt
{
    // ---------------------------------------------------------------- geometry --
    // Lifted from the retired spintop-sheet/build_sheet.py unchanged: same names, same numbers.
    public const float Cell = 384f;

    public const float CX = 192f;
    private const float TipX = CX, TipY = 368f;
    private const float YTop = 106f, YEq = 222f, YTip = 368f;
    private const float Hw = 112f;
    private const float Dome = YEq - YTop, Cone = YTip - YEq;

    private const float GrooveK = 0.28f, GrooveTilt = 0.10f;
    /// <summary>Where the rings sit down the cone, and how far each bows.
    ///
    /// <para><b>Moved down from the sheet's own numbers</b> (stripe 0.11, grooves 0.34 and 0.60)
    /// on the owner's eye, and the arithmetic agrees with it: the stripe is 19 wide, so at 0.11
    /// the top of its band came within 2 units of the mouth's row where it meets the flanks. The
    /// centre was never the problem - it dips 50 clear - it was the ENDS riding up either side of
    /// the face. At 0.18 that gap is 12, which is room to breathe without moving the stripe off
    /// the shoulder of the cone where it belongs.</para>
    ///
    /// <para>The bow went up with it, 1.34 to 1.52, which deepens every ring's curve at the
    /// centre. That is the half of the fix that reads as the rings sitting ON a turning solid
    /// rather than being drawn across a flat one.</para></summary>
    private static readonly float[] Grooves = [0.40f, 0.64f];

    private const float StripeT = 0.18f;
    private const float RingBow = 1.52f;

    private const float LeanIdle = 4.5f;
    private const float NubY = YEq, NubR = 17f;
    private const float KnobY = YTop + 3f, KnobRx = 33f, KnobRy = 14f;
    private const float FinialH = 27f, FinialHw = 17f;

    private const float EyeDx = 35f, EyeY = 176f;
    private const float FaceLift = 42f;
    private const float MouthY = EyeY + 49f;
    private const float HeadY = KnobY - KnobRy - 2f;

    private static readonly EyeRig Rig = new(
        Dx: EyeDx, Y: EyeY, Rx: 30f, Ry: 36f,
        PupilRx: 19.5f, PupilRy: 24f, RingW: 9f, PupilOut: 3.5f,
        BigDx: 9f, BigDy: 13f, BigR: 7.5f,
        SmallDx: 7f, SmallDy: 11f, SmallR: 3.6f,
        ShutBow: 18f, LashW: 10f);

    /// <summary>Painted wood. Rigid, and what moves is the wobble - a top that overshot its own
    /// lean would read as being shaken rather than as spinning.</summary>
    public static readonly Material Stuff = new(Springiness: 0.10f, TrimLag: 0.30f);

    public static Vector2 PartOrigin(string part) => new(TipX, TipY);

    public static float InkWidth { get; set; } = 12f;

    /// <summary>A point down the cone's LEFT edge, t = 0 at the equator, 1 at the tip. Every ring,
    /// the wrap seat and the lit face are measured off this rather than from remembered numbers,
    /// so the cone can be re-drawn without any of them sliding off it.</summary>
    private static Vector2 ConePt(float t)
    {
        var m = 1f - t;
        float a = m * m * m, b = 3f * m * m * t, cc = 3f * m * t * t, d = t * t * t;
        return new Vector2(
            (a * -Hw) + (b * -0.86f * Hw) + (cc * -0.42f * Hw),
            (a * YEq) + (b * (YEq + (0.30f * Cone))) + (cc * (YEq + (0.68f * Cone))) + (d * YTip));
    }

    // ------------------------------------------------------------------- poses --

    /// <summary>Matching the generator's <c>P(theta, lean, sx, sy, dy, eye, blush, spin)</c>.
    /// theta drives the idle wobble and the rings' tilt; lean is an EXTRA tilt on top of it.</summary>
    private static Key K(float theta, float lean, float sx, float sy, float dy, float spin, EyeState eye, bool blush = false)
    {
        var c = Neutral();
        c[(int)Ch.Theta] = theta;
        c[(int)Ch.Lean] = lean;
        c[(int)Ch.Sx] = sx;
        c[(int)Ch.Sy] = sy;
        c[(int)Ch.Dy] = dy;
        c[(int)Ch.Spin] = spin;
        return new Key(c, eye, blush);
    }

    private static readonly Key[] Poses =
    [
        // idle 0-7: one wobble. Nothing else moves - a top at speed is steady, and the life is
        // entirely in the lean and the rings' tilt breathing with it.
        K(0.0000f, 0f, 1.000f, 1.000f, 0f, 1.00f, Open),
        K(0.1250f, 0f, 1.000f, 1.000f, 0f, 1.00f, Open),
        K(0.2500f, 0f, 1.000f, 1.000f, 0f, 1.00f, Open),
        K(0.3750f, 0f, 1.000f, 1.000f, 0f, 1.00f, Open),
        K(0.5000f, 0f, 1.000f, 1.000f, 0f, 1.00f, Open),
        K(0.6250f, 0f, 1.000f, 1.000f, 0f, 1.00f, Open),
        K(0.7500f, 0f, 1.000f, 1.000f, 0f, 1.00f, Open),
        K(0.8750f, 0f, 1.000f, 1.000f, 0f, 1.00f, Open),

        // blink 8-10
        K(0.0000f, 0f, 1.000f, 1.000f, 0f, 1.00f, Open),
        K(0.0000f, 0f, 1.000f, 1.000f, 0f, 1.00f, Shut),
        K(0.0000f, 0f, 1.000f, 1.000f, 0f, 1.00f, HalfShut),

        // boop 11-16: knocked off balance and recovering - the lean does the whole clip.
        K(0.1000f, 9f, 1.010f, 0.980f, 0f, 1.00f, Wide),
        K(0.1600f, 15f, 1.050f, 0.920f, 2f, 1.00f, Wide),
        K(0.2400f, 12f, 1.080f, 0.880f, 3f, 1.00f, Squint),
        K(0.3400f, -9f, 0.960f, 1.050f, -2f, 1.00f, Wide),
        K(0.4400f, 4f, 1.010f, 0.990f, 0f, 1.00f, Open),
        K(0.5000f, 0f, 1.000f, 1.000f, 0f, 1.00f, Happy, blush: true),

        // nap 17-22: leaned over and barely turning. Theta is held FLAT on purpose - a clip
        // that holds an ambient channel is a clip with no opinion about it, so the creature's own
        // beat carries the spin through the nap at the rate the pose asks for. Written out as six
        // marching phases it read as the opposite: the clip claimed the channel, the beat was
        // shut out, and the top turned at whatever rate the sleep clip happened to advance at -
        // which on a slow clip with a spline through it is close enough to stopped.
        K(0.0000f, 3.5f, 1.020f, 0.980f, 2f, 0.45f, Shut, blush: true),
        K(0.0000f, 3.5f, 1.020f, 0.980f, 2f, 0.45f, Shut, blush: true),
        K(0.0000f, 3.5f, 1.020f, 0.980f, 2f, 0.45f, Shut, blush: true),
        K(0.0000f, 3.5f, 1.020f, 0.980f, 2f, 0.45f, Shut, blush: true),
        K(0.0000f, 3.5f, 1.020f, 0.980f, 2f, 0.45f, Shut, blush: true),
        K(0.0000f, 3.5f, 1.020f, 0.980f, 2f, 0.45f, Shut, blush: true),

        // hop 23-32: it winds up and LEAPS, spinning harder all the way.
        K(0.0500f, 0f, 1.040f, 0.960f, 3f, 1.00f, Open),
        K(0.1200f, 0f, 1.090f, 0.890f, 7f, 1.30f, Squint),
        K(0.2000f, 0f, 0.920f, 1.120f, -8f, 1.50f, Wide),
        K(0.3000f, 2.5f, 0.940f, 1.080f, -30f, 1.50f, Wide),
        K(0.4200f, 0f, 0.980f, 1.020f, -42f, 1.40f, Open),
        K(0.5400f, -2.5f, 0.960f, 1.060f, -28f, 1.40f, Open),
        K(0.6600f, 0f, 0.930f, 1.100f, -10f, 1.50f, Wide),
        K(0.7400f, 6f, 1.110f, 0.850f, 6f, 1.30f, Squint),
        K(0.8400f, -4f, 1.040f, 0.960f, 2f, 1.00f, Open),
        K(0.0000f, 0f, 1.000f, 1.000f, 0f, 1.00f, Open),

        // 33-37: the five rest-registered eye cells, from this shell's own rest pose - and on
        // THIS shell they are not purely an eye ladder. The lids coming down IS the top winding
        // down, so the spin rate walks with them: a drowsy top is still spinning, just slower.
        // Theta stays flat through all five so the beat owns it.
        K(0.0000f, 0f, 1.000f, 1.000f, 0f, 0.90f, ThreeQ),
        K(0.0000f, 0f, 1.000f, 1.000f, 0f, 0.80f, HalfShut),
        K(0.0000f, 0f, 1.000f, 1.000f, 0f, 0.70f, Quarter),
        K(0.0000f, 0f, 1.000f, 1.000f, 0f, 0.58f, Drowsy),
        K(0.0000f, 0f, 1.000f, 1.000f, 0f, 0.48f, Heavy),
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

    /// <summary>The SCALE only, about the tip. The lean and the lift come after, together.</summary>
    public static LinePose Posed(Channels c) =>
        new(c[(int)Ch.Sx], c[(int)Ch.Sy], 0f, TipX, TipY);

    private static float LeanOf(Channels c) =>
        (LeanIdle * MathF.Sin(MathF.Tau * c[(int)Ch.Theta])) + c[(int)Ch.Lean];

    /// <summary>How open the rings' ellipses are - the tilt breathing with the wobble.</summary>
    private static float Ring(Channels c) => GrooveK + (GrooveTilt * MathF.Cos(MathF.Tau * c[(int)Ch.Theta]));

    private static Vector2 Tilt(Channels c, Vector2 p) =>
        Swing(p, new Vector2(TipX, TipY), LeanOf(c), c[(int)Ch.Dy]);

    private static Vector2 Pt(Channels c, float x, float y) => Tilt(c, Posed(c).Pt(x, y));

    private static Vector2 EyePt(Channels c, float x, float y) => Tilt(c, Posed(c).EyePt(x, y));

    public static Vector2 Anchor0(string name) => name switch
    {
        "head" => new Vector2(CX, HeadY),
        "face" => new Vector2(CX, EyeY - FaceLift),
        "body" => new Vector2(CX, YEq),
        "mouth" => new Vector2(CX, MouthY),
        "handL" => new Vector2(CX - Hw, NubY),
        "handR" => new Vector2(CX + Hw, NubY),
        _ => new Vector2(CX, YEq),
    };

    public static Vector2 Anchor(string name, Channels c)
    {
        var a = Anchor0(name);
        return name is "face" or "head" ? EyePt(c, a.X, a.Y) : Pt(c, a.X, a.Y);
    }

    // -------------------------------------------------------------------- paths --

    /// <summary>The silhouette: a dome over a cone, drawn from the tip round and back.</summary>
    private static void BodyPath(LineCanvas canvas, Channels c)
    {
        canvas.MoveTo(Pt(c, CX, YTip));
        canvas.CubicTo(
            Pt(c, CX - (0.42f * Hw), YEq + (0.68f * Cone)),
            Pt(c, CX - (0.86f * Hw), YEq + (0.30f * Cone)),
            Pt(c, CX - Hw, YEq));
        canvas.CubicTo(
            Pt(c, CX - Hw, YTop + (0.26f * Dome)),
            Pt(c, CX - (0.66f * Hw), YTop),
            Pt(c, CX, YTop));
        canvas.CubicTo(
            Pt(c, CX + (0.66f * Hw), YTop),
            Pt(c, CX + Hw, YTop + (0.26f * Dome)),
            Pt(c, CX + Hw, YEq));
        canvas.CubicTo(
            Pt(c, CX + (0.86f * Hw), YEq + (0.30f * Cone)),
            Pt(c, CX + (0.42f * Hw), YEq + (0.68f * Cone)),
            Pt(c, CX, YTip));
    }

    /// <summary>The three points a ring is built from: its two ends ON the cone's edge, and the
    /// control that bows it toward the viewer.
    ///
    /// <para>A ring is the NEAR half of an ellipse and never a closed one - a ring you can only
    /// see the front of is a groove cut into a turning solid, where a full ellipse is a hoop
    /// sitting on it. The open-arc drawer that used to live here went when every ring was cut to
    /// the silhouette instead; the band version below is the only caller now.</para></summary>
    private static (Vector2 L, Vector2 Ctrl, Vector2 R) RingPts(Channels c, float t)
    {
        var e = ConePt(t);
        var l = Pt(c, CX + e.X, e.Y);
        var r = Pt(c, CX - e.X, e.Y);
        var radY = -e.X * Ring(c) * c[(int)Ch.Sy];
        var mid = (l + r) * 0.5f;
        return (l, mid + new Vector2(0f, radY * RingBow), r);
    }

    /// <summary>A ring drawn as a BAND and cut to the silhouette, for the wide ones.
    ///
    /// <para>Every ring lands with its ends exactly ON the cone's edge, so any stroke hangs half
    /// its weight over the side - and the painted stripe, at 19 wide and sitting on the equator,
    /// hung it straight through the arm nubs. Insetting the ends only shortens the band and
    /// leaves the same overhang somewhere else; the sheet clips, so this clips. The bow is
    /// converted from the quadratic to its cubic twin because a band is walked along a
    /// cubic.</para></summary>
    private static void RingBand(LineCanvas canvas, Channels c, float t, float width, Vector2[] clip, Vector4 colour)
    {
        var (l, ctrl, r) = RingPts(c, t);
        var c1 = l + ((ctrl - l) * (2f / 3f));
        var c2 = r + ((ctrl - r) * (2f / 3f));
        canvas.Band(l, c1, c2, r, width, width);
        canvas.FillBandIn(clip, colour);
    }

    /// <inheritdoc cref="JellyLineArt.Pin"/>
    public static Vector2 Pin(string name, PinKind kind, Vector2 rest, Channels c) => kind switch
    {
        PinKind.Hand => Anchor(name, c),
        PinKind.Face or PinKind.Head => EyePt(c, rest.X, rest.Y),
        _ => Pt(c, rest.X, rest.Y),
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

        // Nubs first, then the body over their inner halves.
        var nubR = NubR * (ch[(int)Ch.Sx] + ch[(int)Ch.Sy]) * 0.5f;
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            c.Ellipse(Pt(ch, CX + (side * Hw), NubY), nubR, nubR, Tint(body, NubFill));
        }

        BodyPath(c, ch);
        c.Fill(Tint(body, Shadow));
        var silhouette = c.Capture();

        // The lit dome, CLIPPED TO THE SILHOUETTE ITSELF. It is deliberately larger than the
        // body - 1.02 of the half-width and 1.05 of the dome - so its edge lands where the light
        // stops rather than where the geometry does, which means it MUST be cut by the real
        // outline. The first cut clipped it to an invented ellipse a good half again too big,
        // and the body colour duly ran past the top and the left of the creature.
        var lit = Pt(ch, CX - 14f, YTop + (Dome * 0.72f));
        c.EllipsePath(lit, Hw * 1.02f * ch[(int)Ch.Sx], Dome * 1.05f * ch[(int)Ch.Sy]);
        c.FillInPoly(silhouette, Tint(body, Base));

        // The cone's lit face: down the left edge and back up an inner line.
        c.MoveTo(Pt(ch, CX + ConePt(0f).X, ConePt(0f).Y));
        for (var i = 1; i <= 8; i++)
        {
            var e = ConePt(i / 8f);
            c.LineTo(Pt(ch, CX + e.X, e.Y));
        }

        for (var i = 8; i >= 0; i--)
        {
            var e = ConePt(i / 8f);
            c.LineTo(Pt(ch, CX + (e.X * 0.46f), e.Y));
        }

        c.FillInPoly(silhouette, Tint(body, Base));

        // THE TRAVELLING HIGHLIGHT, and it is the main thing that says this creature is spinning.
        // Its x rides cos(theta), so the catch of light walks around the dome as the top turns
        // while the dome itself never rotates - which is how you draw a turning solid without
        // animating a pattern going round it. The first cut had no highlight at all, so the top
        // read as a static cone that happened to be leaning.
        // CLIPPED TO THE SILHOUETTE, which is why it is built as a band and filled rather than
        // stroked: a stroke cannot be cut to a curved edge, and this one runs close enough to the
        // flank that at the far end of its travel it crossed the outline and painted over the arm
        // nubs - which belong to the arms, not to the dome.
        var hx = CX - (46f * MathF.Cos(MathF.Tau * ch[(int)Ch.Theta]));
        var hw2 = 9f * ch[(int)Ch.Sx] * 0.5f;
        var hb = Pt(ch, hx - 27f, YTop + (Dome * 0.58f));
        c.Band(
            Pt(ch, hx - 14f, YTop + (Dome * 0.20f)), hb, hb,
            Pt(ch, hx - 30f, YTop + (Dome * 0.92f)),
            hw2 * 2f, hw2 * 2f);
        c.FillBandIn(silhouette, Tint(body, Rim) with { W = 0.85f });

        // The grooves and the stripe take the BODY's pose, not the lagged one. Paint does not
        // lag: a stripe is not an ornament resting on the top, it is on its surface, and the two
        // cannot disagree about where that surface is. Given the lag they did disagree - hardest
        // on a boop, where the body snaps to a squash while the trim is still at the pose before
        // it - and a band positioned by one pose and cut by another walks straight out of the
        // creature at the edges. Trim lag is for things that sit ON a body, not things painted
        // INTO it.
        foreach (var t in Grooves)
        {
            RingBand(c, ch, t, 9f * ch[(int)Ch.Sy], silhouette, Tint(body, Shadow));
        }

        // The painted stripe a top is decorated with - the widest ring, sitting on the equator,
        // which is also the row the arm nubs sit on. Clipped rather than inset: at 19 wide its
        // ends hung straight through the nubs, and pulling them in only moves the overhang.
        RingBand(c, ch, StripeT, 19f * ch[(int)Ch.Sy], silhouette, Tint(accent, AccBase));

        // The hard little foot it balances on.
        var footE = ConePt(0.86f);
        c.MoveTo(Pt(ch, CX + footE.X, footE.Y));
        c.QuadTo(Pt(ch, CX, YTip), Pt(ch, CX - footE.X, footE.Y));
        c.Fill(Tint(accent, AccShadow));

        // The cheeks.
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            c.Ellipse(
                EyePt(ch, CX + (side * 64f), EyeY + 30f),
                16f * Posed(ch).Ex, 10f * Posed(ch).Ey,
                Tint(accent, AccRim) with { W = 0.55f });
        }

        // The knob and finial on the crown. The knob's height rides the ring tilt, so the whole
        // top opens and closes with the wobble rather than sitting flat through it.
        // The knob is CUT to the crown. Left free it stands proud of the dome and reads as a bead
        // balanced on top; clipped, it is a disc set INTO the crown, which is what a top's winding
        // knob is. Its own height rides the ring tilt, so it opens and closes with the wobble.
        var knob = Pt(ch, CX, KnobY);
        var knobRx = KnobRx * ch[(int)Ch.Sx];
        var knobRy = KnobRy * 2.4f * Ring(ch) * ch[(int)Ch.Sy];
        c.EllipsePath(knob, knobRx, knobRy);
        c.FillInPoly(silhouette, Tint(accent, AccBase));

        c.MoveTo(Pt(ch, CX - FinialHw, KnobY - 2f));
        c.QuadTo(Pt(ch, CX, KnobY - FinialH), Pt(ch, CX + FinialHw, KnobY - 2f));
        c.Fill(Tint(accent, AccRim));

        if (blush > 0f)
        {
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? -1 : 1;
                c.Ellipse(EyePt(ch, CX + (side * (EyeDx + 34f)), EyeY + 24f), 14f, 9f, BlushTint(blush));
            }
        }

        // -- the ink.
        BodyPath(c, ch);
        c.Stroke(ink, 12f);

        foreach (var t in Grooves)
        {
            RingBand(c, ch, t, 7f, silhouette, ink);
        }

        // Only the half of the knob that is actually in the crown carries ink. Ringing it whole
        // would draw the very edge the clip just removed.
        c.MoveTo(knob + new Vector2(-knobRx, 0f));
        c.QuadTo(knob + new Vector2(0f, knobRy * RingBow), knob + new Vector2(knobRx, 0f));
        c.Stroke(ink, 9f, closed: false);

        c.MoveTo(Pt(ch, CX - FinialHw, KnobY - 2f));
        c.QuadTo(Pt(ch, CX, KnobY - FinialH), Pt(ch, CX + FinialHw, KnobY - 2f));
        c.Stroke(ink, 9f, closed: false);

        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            var at = Pt(ch, CX + (side * Hw), NubY);
            var quarter = MathF.PI / 2f;
            c.Arc(at, nubR, -quarter, side < 0 ? -MathF.PI - quarter : quarter, ink, 10f);
        }

        DrawEyes(c, Rig, eye, side => EyePt(ch, CX + (side * EyeDx), EyeY), Posed(ch).Ex, Posed(ch).Ey, eyeTint, ink);
    }
}
