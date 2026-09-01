namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using static AetherOS.PetKit.Rendering.LineArt.LineShell;

/// <summary>
/// The Crab, drawn. Second shell converted, and chosen for the same reason it was chosen for the
/// sheet pipeline: it is the shell most likely to break an assumption. Where the Jelly is a bell
/// and a face, the Crab has a body plan: a domed shell, two chelipeds on curved tapering arms
/// with knuckle joints, and legs that are code rather than art.
///
/// <para><b>What converting it proved.</b> Everything the Jelly needed was already shared and
/// nothing here fought it: the same pose transform, the same eye, the same nubs, the same
/// authoring greys. What it ADDED was two primitives, and both were general rather than crab
/// specific: <see cref="LineCanvas.Band"/> (a tapered limb outline, which the foundry's own
/// comment notes is the shape <c>TentacleFx</c> already builds) and <see cref="LineCanvas.Local"/>
/// (a nested transform, for a part authored in its own frame). Both now belong to every shell
/// that follows. That is the pipeline working.</para>
///
/// <para><b>The one thing that is genuinely this shell's.</b> The pincers are pinned to their own
/// centre (<see cref="ClawCy"/>) rather than to the shell's, so a dome that grew taller did not
/// carry them up with it. The pose still deforms them; they simply hang off a different row. A
/// shell may need a second pivot, and the conversion format has to allow for that.</para>
/// </summary>
public static class CrabLineArt
{
    // ---------------------------------------------------------------- geometry --
    // Lifted from the retired crab-sheet/build_sheet.py unchanged: same names, same numbers.
    public const float Cell = 384f;

    public const float CX = 192f;
    private const float ShellCy = 282f;
    private const float ShellRx = 103f, ShellRy = 80f;
    private const float Fit = 0.75f;

    /// <summary>The pincers do NOT come up with the shell. Pinned to the centre they were
    /// authored against, so a crown that clears the claws stays cleared.</summary>
    private const float ClawCy = 300f;

    private const float ClawTilt = -4f, ClawK = 0.92f;

    /// <summary>What a unit of <see cref="Ch.Lean"/> moves the claws, in the 512-space the claw
    /// geometry is authored in: a full lean walks the knuckles about fourteen units.</summary>
    private const float SwayPerLean = 1.6f;

    public const float HEM = ShellCy + ShellRy;
    private const float SocketsY = HEM - 54f;

    /// <summary>The shoulder. Authored at 18 to match wispv2's, which is the standard the code
    /// arm was measured against; brought in to 15 on the owner's eye once the nub stopped being a
    /// half circle and started being solved against the flank, which made it read larger than the
    /// sheet's did at the same radius.</summary>
    private const float NubY = 317f, NubR = 15f;

    private const float HeadY = ShellCy - 70f;
    private const float MouthY = HEM - 24f;
    private const float FaceLift = 42f;

    private static readonly EyeRig Rig = new(
        Dx: 29f, Y: 286f, Rx: 25f, Ry: 29f,
        PupilRx: 17f, PupilRy: 22f, RingW: 9f, PupilOut: 3f,
        BigDx: 7.5f, BigDy: 12f, BigR: 6.4f,
        SmallDx: 6f, SmallDy: 10f, SmallR: 3.2f,
        ShutBow: 16f, LashW: 10f);

    public static float InkWidth { get; set; } = 12f;

    /// <summary>The shell's own half-width at a row. Solved rather than measured, so every pin
    /// that hangs off it survives the shell moving, which it has now done three times, and each
    /// time an absolute would have quietly slid off the edge.</summary>
    private static float Flank(float y)
    {
        var d = MathF.Min(1f, MathF.Abs(y - ShellCy) / ShellRy);
        return ShellRx * MathF.Sqrt(MathF.Max(0f, 1f - (d * d)));
    }

    private static readonly float NubX = CX - (Flank(NubY) - 4f);

    /// <summary>A point from the 512 master, into cell space.</summary>
    private static Vector2 M(float mx, float my) =>
        new(CX + ((mx - 256f) * Fit), ShellCy + ((my - 320f) * Fit));

    /// <summary>A CLAW point from the master: the same mapping with the shell's centre swapped
    /// for the claws' own, so the pincers keep the height they were authored at.</summary>
    private static Vector2 MC(float mx, float my) =>
        new(CX + ((mx - 256f) * Fit), PartOrigin("claw").Y + ((my - 320f) * Fit));

    // ------------------------------------------------------------------- poses --
    /// <summary>This shell's key factory, matching its generator's <c>P(sx, sy, dy, eye,
    /// blush)</c> exactly, so the table below still transcribes line for line. A shell with other
    /// channels writes its own: the Puffer's takes a puff and a spine length.</summary>
    private static Key K(float sx, float sy, float dy, EyeState eye, bool blush)
    {
        var c = Neutral();
        c[(int)Ch.Sx] = sx;
        c[(int)Ch.Sy] = sy;
        c[(int)Ch.Dy] = dy;

        // The claws rest CLOSED at 1, not 0: Bristle is multiplicative, and a rest value of 0
        // would multiply to 0 forever. The gape is whatever a morph pushes past 1.
        c[(int)Ch.Spike] = 1f;
        return new Key(c, eye, blush);
    }

    private static readonly Key[] Poses =
    [
        // idle 0-7: a crab does not pulse like a jellyfish. A low scuttle-bob, barely changing
        // width: nearly all the life in the idle comes from the legs' own drift, which is free.
        K(1.000f, 1.000f, 0f, Open, false),
        K(1.004f, 0.994f, 1f, Open, false),
        K(1.008f, 0.988f, 2f, Open, false),
        K(1.006f, 0.991f, 2f, Open, false),
        K(1.000f, 1.000f, 0f, Open, false),
        K(0.996f, 1.006f, -2f, Open, false),
        K(0.992f, 1.012f, -3f, Open, false),
        K(0.995f, 1.007f, -1f, Open, false),

        // blink 8-10
        K(1.00f, 1.00f, 0f, Open, false),
        K(1.00f, 1.00f, 0f, Shut, false),
        K(1.00f, 1.00f, 0f, HalfShut, false),

        // boop 11-16: a crab's startle is to hunker, not to recoil upward.
        K(1.02f, 0.97f, 2f, Wide, false),
        K(1.13f, 0.85f, 8f, Wide, false),
        K(1.19f, 0.80f, 11f, Squint, false),
        K(1.04f, 0.95f, 3f, Wide, false),
        K(0.99f, 1.02f, -2f, Open, false),
        K(1.00f, 1.00f, 0f, Happy, true),

        // nap 17-22
        K(1.06f, 0.94f, 7f, Shut, true),
        K(1.08f, 0.92f, 9f, Shut, true),
        K(1.10f, 0.90f, 11f, Shut, true),
        K(1.09f, 0.91f, 10f, Shut, true),
        K(1.07f, 0.93f, 8f, Shut, true),
        K(1.05f, 0.95f, 7f, Shut, true),

        // hop 23-32: a scuttle-jump.
        K(1.10f, 0.90f, 7f, Open, false),
        K(1.20f, 0.80f, 13f, Squint, false),
        K(0.94f, 1.09f, -8f, Wide, false),
        K(0.96f, 1.06f, -28f, Wide, false),
        K(1.00f, 1.00f, -38f, Open, false),
        K(0.97f, 1.04f, -26f, Open, false),
        K(0.95f, 1.07f, -9f, Wide, false),
        K(1.21f, 0.79f, 12f, Squint, false),
        K(1.08f, 0.93f, 4f, Open, false),
        K(1.00f, 1.00f, 0f, Open, false),

        // 33-37: the five rest-registered eye cells, from this shell's own rest pose.
        K(1.00f, 1.00f, 0f, ThreeQ, false),
        K(1.00f, 1.00f, 0f, HalfShut, false),
        K(1.00f, 1.00f, 0f, Quarter, false),
        K(1.00f, 1.00f, 0f, Drowsy, false),
        K(1.00f, 1.00f, 0f, Heavy, false),
    ];

    /// <summary>Chitin. A crab does not wobble, and the contract's whole point is that saying so
    /// costs one number - the Jelly beside it sits at 0.85.</summary>
    public static readonly Material Stuff = new(Springiness: 0.05f, TrimLag: 0.30f);

    /// <summary>Where a named part's authored coordinates hang from, and the reason this is in
    /// the contract at all: the pincers are pinned to their OWN row rather than to the shell's,
    /// so a dome that grew taller did not carry them up with it. The pose still deforms them;
    /// they simply hang from a different origin. Any shell may need this and it was invisible
    /// until a second one was converted.</summary>
    public static Vector2 PartOrigin(string part) => part switch
    {
        "claw" => new Vector2(CX, ClawCy),
        _ => new Vector2(CX, ShellCy),
    };

    /// <summary>Lets this shell's ambient channels run through a clip that does not act them.</summary>
    public static Channels WithAmbient(Channels target, int[] frames, float beat, out bool clipDrives, out float driven)
        => LineShell.WithAmbient(target, Poses, frames, beat, out clipDrives, out driven);

    public static Channels PoseAt(int prev, int cell, int next, int after, float phase)
        => LineShell.PoseAt(Poses, prev, cell, next, after, phase);

    /// <summary>This shell's face at a moment of its own clip: the eye between two cells and the
    /// blush across them. The pose table is this shell's; the resolving is the caller's.</summary>
    public static (EyeState Eye, float Blush) FaceAt(int cell, int next, float phase)
        => (LineShell.EyeAt(Poses, cell, next, phase), LineShell.BlushAt(Poses, cell, next, phase));

    /// <summary>Channels to this shell's own transform. Squash about the hem, then lift - which
    /// is what THIS shell poses on. A shell that inflates or sways reads different channels here
    /// and the shared layer never has to know.</summary>
    public static LinePose Posed(Channels c) =>
        new(c[(int)Ch.Sx], c[(int)Ch.Sy], c[(int)Ch.Dy], CX, HEM);

    /// <summary>The shell's anchors in authoring space, neutral pose. <c>sockets</c> is this
    /// shell's own: where the legs are sown, which is NOT the underside: an ellipse falls away
    /// from its centre, so seating the outer legs at the centre's depth would start them in mid
    /// air.</summary>
    public static Vector2 Anchor0(string name) => name switch
    {
        "head" => new Vector2(CX, HeadY),
        "face" => new Vector2(CX, Rig.Y - FaceLift),
        "body" => new Vector2(CX, ShellCy - ShellRy + (ShellRy * 2f * 0.60f)),
        "handL" => new Vector2(NubX, NubY),
        "handR" => new Vector2((2f * CX) - NubX, NubY),
        "mouth" => new Vector2(CX, MouthY),
        "hem" => new Vector2(CX, HEM),
        "sockets" => new Vector2(CX, SocketsY),
        _ => new Vector2(CX, ShellCy),
    };

    public static Vector2 Anchor(string name, LinePose q)
    {
        var a = Anchor0(name);
        return name is "face" or "head" ? q.EyePt(a.X, a.Y) : q.Pt(a.X, a.Y);
    }

    /// <inheritdoc cref="JellyLineArt.Pin"/>
    public static Vector2 Pin(string name, PinKind kind, Vector2 rest, Channels c)
    {
        var q = Posed(c);
        return kind switch
        {
            PinKind.Hand => Anchor(name, q),
            PinKind.Face or PinKind.Head => q.EyePt(rest.X, rest.Y),
            _ => q.Pt(rest.X, rest.Y),
        };
    }

    // -------------------------------------------------------------------- draw --

    /// <summary>One cheliped's parts, posed. Decoration throughout, never a hand root: the
    /// pincers ride the upper flanks and the drawn arms sprout from nubs on the lower flanks
    /// between them. A crab with claws AND two small mystical arms is the register; a crab whose
    /// claws are its hands is just a crab. <paramref name="sway"/> carries both claws the same
    /// way in WORLD space rather than mirroring (a crab leaning, not a crab opening its arms),
    /// and falls off toward the shoulder, which is buried in the shell.</summary>
    private static (Vector2 Sh, Vector2 C1, Vector2 C2, Vector2 Tip, float Kr) Claw(LinePose q, int side, float sway = 0f)
    {
        var sh = q.Pt2(MC(256f + (side * 104f) + (sway * 0.15f), 284f));
        var c1 = q.Pt2(MC(256f + (side * 148f) + (sway * 0.55f), 280f));
        var c2 = q.Pt2(MC(256f + (side * 172f) + (sway * 0.85f), 274f));
        var tip = q.Pt2(MC(256f + (side * 180f) + sway, 244f));
        var kr = 24f * Fit * ClawK * (q.Sx + q.Sy) * 0.5f;
        return (sh, c1, c2, tip, kr);
    }

    /// <summary>The pincer, authored pointing up with the wedge open at the top, then mirrored
    /// and tilted inward per side. The movable finger's four points swing about
    /// <see cref="Hinge"/> and the fixed finger never moves: opening a claw by scaling the whole
    /// wedge would grow the pincer. <paramref name="gape"/> is 0 at the authored rest and
    /// 1 fully open.</summary>
    private static void JawPath(LineCanvas c, LocalXf xf, float gape)
    {
        var notch = Hinged(-1f, -43f, gape);
        var tip = Hinged(-21f, -61f, gape);
        var k1 = Hinged(-31f, -37f, gape);
        var k2 = Hinged(-28f, -13f, gape);

        c.MoveTo(xf.To(22f, 3f));
        c.CubicTo(xf.To(30f, -21f), xf.To(24f, -46f), xf.To(6f, -61f));
        c.LineTo(xf.To(notch.X, notch.Y));
        c.LineTo(xf.To(tip.X, tip.Y));
        c.CubicTo(xf.To(k1.X, k1.Y), xf.To(k2.X, k2.Y), xf.To(-22f, 3f));
        c.LineTo(xf.To(22f, 3f));
    }

    /// <summary>Where the movable finger pivots: the base of the claw, where the outer edge meets
    /// the knuckle. Authored, not derived, because a hinge is a place on the drawing.</summary>
    private static readonly Vector2 Hinge = new(-22f, 3f);

    /// <summary>How far open a full unit of gape swings the finger. Eighteen degrees: enough to
    /// read at 96 px, and short of the angle where the wedge stops looking like one claw.</summary>
    private const float GapeDegrees = 18f;

    /// <summary>One of the movable finger's points, swung about the hinge.</summary>
    private static Vector2 Hinged(float x, float y, float gape)
    {
        if (MathF.Abs(gape) < 1e-4f)
        {
            return new Vector2(x, y);
        }

        // Negative because the finger opens AWAY from the fixed one, which sits at positive x in
        // this local space.
        var a = -gape * GapeDegrees * MathF.PI / 180f;
        var (sn, cs) = (MathF.Sin(a), MathF.Cos(a));
        var d = new Vector2(x - Hinge.X, y - Hinge.Y);
        return Hinge + new Vector2((d.X * cs) - (d.Y * sn), (d.X * sn) + (d.Y * cs));
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
        // Channels in, this shell's own transform out. Every drawn shell takes the same
        // arguments so the caller needs no special case for any of them.
        var q = Posed(ch);
        var trim = Posed(trimCh);

        // Two borrowed channels: Spike opens the claws (the sharp bits standing out), Lean
        // carries both arms the same way.
        var gape = Math.Clamp(ch[(int)Ch.Spike] - 1f, 0f, 1.2f);
        var clawSway = ch[(int)Ch.Lean] * SwayPerLean;

        c.Begin(dl, bottomCentre, displaySize, Cell, outer, flip);

        var scx = q.X(CX);
        var scy = q.Y(ShellCy);
        var srx = ShellRx * q.Sx;
        var sry = ShellRy * q.Sy;

        // ORDER IS THE CLIP. The sheet's overlay clips its ink to OUTSIDE each occluder - the
        // shell for a buried shoulder, the knuckle ball for the arm's and the jaw's ends - and
        // its own note calls that "the general form of the trick the Jelly's nub does by hand".
        // ImGui has only rectangle clipping, so the same result is bought with painter's order:
        // ink a part, then draw the thing that buries it ON TOP. Ink it all first and unclipped,
        // as the first pass of this shell did, and every buried outline reads as loose lines
        // running across the creature - which is exactly what the overlapping claw art was.
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            var (sh, c1, c2, tip, kr) = Claw(q, side, clawSway);
            var xf = new LocalXf(tip, side * Fit * q.Sx * ClawK, Fit * q.Sy * ClawK, ClawTilt);

            c.Band(sh, c1, c2, tip, 34f * Fit * q.Sy, 26f * Fit * q.Sy);
            c.FillBand(Tint(body, Base));
            c.Stroke(ink, 10f);

            JawPath(c, xf, gape);
            c.Fill(xf.To(0f, -25f), Tint(body, Base));
            c.Stroke(ink, 11f);

            c.MoveTo(xf.To(10f, -6f));
            c.CubicTo(xf.To(15f, -25f), xf.To(10f, -40f), xf.To(0f, -49f));
            c.Stroke(Tint(body, Shadow), 6f * Fit, closed: false);

            // The knuckle: its FILL buries the ends of the arm's and the jaw's outlines, then
            // its ring caps the pair. A joint sells an attachment better than a seam does.
            c.Ellipse(tip, kr, kr, Tint(body, Base));
            c.EllipseStroke(tip, kr, kr, ink, 9f);
        }

        // The shell, over the claws: this is the `nb` clip, and it is what buries each pincer's
        // shoulder and the inboard end of its arm.
        c.Ellipse(new Vector2(scx, scy), srx, sry, Tint(body, Shadow));
        c.Ellipse(
            new Vector2(scx - (10.5f * q.Sx), scy - (9f * q.Sy)),
            srx - (7f * q.Sx), sry - (7f * q.Sy),
            Tint(body, Base));

        // Accent: shell speckles, the front rim lip that gives the body an edge, and the cheeks.
        foreach (var (fx, fy, r) in new[] { (-0.500f, -0.532f, 9f), (0.500f, -0.581f, 9f), (0f, -0.798f, 7f) })
        {
            // Speckles are TRIMMINGS: on the shell rather than part of its outline, so they ride
            // the lagged pose and settle a beat after the body does.
            c.Ellipse(
                trim.Pt(CX + (fx * ShellRx), ShellCy + (fy * ShellRy)),
                r * Fit * trim.Sx, r * Fit * trim.Sy,
                Tint(accent, AccShadow));
        }

        // The rim rides the MOUTH, because the mouth is inked on it: MouthDraw draws one arc on
        // the `mouth` pin and this is the lip it sits in.
        var lipX = 0.83f * ShellRx;
        c.MoveTo(q.Pt(CX - lipX, MouthY - 19f));
        c.CubicTo(
            q.Pt(CX - (lipX * 0.62f), MouthY + 11f),
            q.Pt(CX + (lipX * 0.62f), MouthY + 11f),
            q.Pt(CX + lipX, MouthY - 19f));
        c.Stroke(Tint(accent, AccBase) with { W = 0.85f }, 7f * q.Sy, closed: false);

        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            c.Ellipse(
                q.EyePt(CX + (side * (Rig.Dx + 37f)), Rig.Y + 30f),
                13f * q.Ex, 8f * q.Ey,
                Tint(accent, AccRim) with { W = 0.9f });
        }

        if (blush > 0f)
        {
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? -1 : 1;
                c.Ellipse(
                    q.EyePt(CX + (side * (Rig.Dx + 37f)), Rig.Y + 30f),
                    15f * q.Ex, 9f * q.Ey, BlushTint(blush));
            }
        }

        DrawEyes(c, Rig, eye, side => q.EyePt(CX + (side * Rig.Dx), Rig.Y), q.Ex, q.Ey, eyeTint, ink);

        c.EllipseStroke(new Vector2(scx, scy), srx, sry, ink, 12f);

        // ...and the nubs PART that outline, which is the `ns` clip: the fill breaks the shell's
        // line where the shoulder sits and the arc then completes the silhouette, so the two read
        // as one edge rather than as a ball with a line drawn through it. Order again, not clips.
        bool InsideShell(Vector2 p)
        {
            var dx = (p.X - scx) / MathF.Max(0.001f, srx);
            var dy = (p.Y - scy) / MathF.Max(0.001f, sry);
            return (dx * dx) + (dy * dy) <= 1f;
        }

        DrawNubs(c, q, CX, NubX, NubY, NubR, 11f, body, ink, fill: true);
        DrawNubs(c, q, CX, NubX, NubY, NubR, 11f, body, ink, fill: false, InsideShell);
    }
}
