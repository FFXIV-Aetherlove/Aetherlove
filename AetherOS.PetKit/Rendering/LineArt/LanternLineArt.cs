namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using static AetherOS.PetKit.Rendering.LineArt.LineShell;

/// <summary>
/// The Lantern, drawn. Seventh shell, and the first that genuinely ROTATES: six shells got here
/// on scale and translation alone.
///
/// <para><b>It hangs, so it swings.</b> The whole creature is scaled about its base, then rotated
/// about the RING it hangs from, then lifted. The generator keeps the swing as a group transform
/// rather than folding it into the points, and says why: "a rotation is a similarity, so stroke
/// widths come through it unchanged. An outline that thinned on a swung frame would stop matching
/// the code-drawn arm and the code-drawn cord beside it, both of which ink from lineColor at a
/// constant width." Drawn here the same way: the rotation is applied to finished points, so no
/// ink weight is derived through it.</para>
///
/// <para><b>The soul takes the full swing but only half the squash.</b> Eyes take half a squash
/// everywhere on this roster; here they take ALL of the rotation, "because the soul is inside the
/// lantern and goes where the lantern goes". A face that lagged the case it lives in would read
/// as a picture sliding behind glass.</para>
///
/// <para><b>Glow is a channel, and the wick barely answers it.</b> A lamp that is startled
/// brightens far more than it grows, so the flame takes only <see cref="WickGain"/> of the glow
/// into its height: a wick scaled straight off the pose reaches through the cap above it.</para>
/// </summary>
public static class LanternLineArt
{
    // ---------------------------------------------------------------- geometry --
    // Lifted from the retired lantern-sheet/build_sheet.py unchanged: same names, same numbers.
    public const float Cell = 384f;

    public const float CX = 192f;
    private const float RingCy = 62f, RingR = 17f;
    private const float BulbCy = 196f, BulbRx = 98f, BulbRy = 112f;

    private const float CapTop = 78f, CapHw = 52f;
    private const float CollarY = 106f, CollarH = 18f, CollarHw = 54f;
    private const float CapBot = CollarY;
    private const float BaseY = 292f, BaseH = 25f, BaseHw = 47f;
    private const float BaseBot = BaseY + BaseH;

    private const float SoulCy = 210f, SoulHw = 55f, SoulHh = 56f;
    private const float NubY = BulbCy + 8f, NubR = 16f;

    private const float EyeDx = 24f, EyeY = SoulCy + 2f;
    private const float FaceLift = 34f;
    private const float MouthY = EyeY + 36f;
    private const float HeadY = CapTop + 2f;

    /// <summary>How far it swings, in degrees, and how weakly the flame answers the glow.</summary>
    private const float Sway = 4f, WickGain = 0.16f;

    // The brass and the glass are their own greys - this shell is made of two materials the
    // others are not, and both sit between the body tones rather than on them.
    private const float Brass = 132f / 255f, BrassRim = 170f / 255f;
    private const float GlassEdge = 150f / 255f, GlassFill = 176f / 255f;

    private static readonly EyeRig Rig = new(
        Dx: EyeDx, Y: EyeY, Rx: 23f, Ry: 27f,
        PupilRx: 15f, PupilRy: 18f, RingW: 8f, PupilOut: 3f,
        BigDx: 7f, BigDy: 10f, BigR: 5.5f,
        SmallDx: 5.5f, SmallDy: 8f, SmallR: 2.8f,
        ShutBow: 14f, LashW: 9f);

    /// <summary>Brass and glass. It is the most rigid thing on the roster - what moves is the
    /// swing, and a swing that overshot would read as the whole lamp being shaken.</summary>
    public static readonly Material Stuff = new(Springiness: 0.08f, TrimLag: 0.30f);

    public static Vector2 PartOrigin(string part) => part switch
    {
        "ring" => new Vector2(CX, RingCy),
        _ => new Vector2(CX, BaseBot),
    };

    public static float InkWidth { get; set; } = 12f;

    // ------------------------------------------------------------------- poses --

    /// <summary>Matching the generator's <c>P(theta, sx, sy, dy, glow, eye, blush)</c>. theta
    /// drives the swing; glow is how hard the lamp is burning.</summary>
    private static Key K(float theta, float sx, float sy, float dy, float glow, EyeState eye, bool blush = false)
    {
        var c = Neutral();
        c[(int)Ch.Theta] = theta;
        c[(int)Ch.Sx] = sx;
        c[(int)Ch.Sy] = sy;
        c[(int)Ch.Dy] = dy;
        c[(int)Ch.Glow] = glow;
        return new Key(c, eye, blush);
    }

    private static readonly Key[] Poses =
    [
        // idle 0-7: one slow swing, and the flame breathing with it.
        K(0.0000f, 1.000f, 1.000f, 0f, 1.00f, Open),
        K(0.1250f, 1.000f, 1.000f, 0f, 1.04f, Open),
        K(0.2500f, 1.000f, 1.000f, 0f, 1.06f, Open),
        K(0.3750f, 1.000f, 1.000f, 0f, 1.04f, Open),
        K(0.5000f, 1.000f, 1.000f, 0f, 1.00f, Open),
        K(0.6250f, 1.000f, 1.000f, 0f, 0.96f, Open),
        K(0.7500f, 1.000f, 1.000f, 0f, 0.94f, Open),
        K(0.8750f, 1.000f, 1.000f, 0f, 0.96f, Open),

        // blink 8-10
        K(0.0000f, 1.000f, 1.000f, 0f, 1.00f, Open),
        K(0.0000f, 1.000f, 1.000f, 0f, 1.00f, Shut),
        K(0.0000f, 1.000f, 1.000f, 0f, 1.00f, HalfShut),

        // boop 11-16: it FLARES. A startled lamp brightens far more than it moves.
        K(0.1200f, 1.010f, 0.980f, 0f, 1.35f, Wide),
        K(0.2000f, 1.040f, 0.940f, 2f, 1.55f, Wide),
        K(0.3000f, 1.050f, 0.930f, 3f, 1.45f, Squint),
        K(0.4200f, 0.980f, 1.030f, -2f, 1.20f, Wide),
        K(0.5400f, 1.010f, 0.990f, 0f, 1.05f, Open),
        K(0.6200f, 1.000f, 1.000f, 0f, 1.00f, Happy, blush: true),

        // nap 17-22: banked down to half, swinging slowly.
        K(0.0000f, 1.000f, 1.000f, 2f, 0.58f, Shut, blush: true),
        K(0.1667f, 1.000f, 1.000f, 2f, 0.54f, Shut, blush: true),
        K(0.3333f, 1.000f, 1.000f, 3f, 0.50f, Shut, blush: true),
        K(0.5000f, 1.000f, 1.000f, 3f, 0.52f, Shut, blush: true),
        K(0.6667f, 1.000f, 1.000f, 2f, 0.56f, Shut, blush: true),
        K(0.8333f, 1.000f, 1.000f, 2f, 0.58f, Shut, blush: true),

        // hop 23-32: a lamp does not hop, it SWINGS UP - and burns harder doing it.
        K(0.0600f, 1.020f, 0.980f, 2f, 1.00f, Open),
        K(0.1400f, 1.050f, 0.940f, 6f, 1.10f, Squint),
        K(0.2400f, 0.960f, 1.060f, -8f, 1.35f, Wide),
        K(0.3600f, 0.970f, 1.050f, -22f, 1.30f, Wide),
        K(0.4800f, 0.990f, 1.020f, -30f, 1.20f, Open),
        K(0.6000f, 0.980f, 1.040f, -20f, 1.25f, Open),
        K(0.7200f, 0.960f, 1.060f, -8f, 1.30f, Wide),
        K(0.8200f, 1.050f, 0.930f, 6f, 1.15f, Squint),
        K(0.9200f, 1.020f, 0.970f, 2f, 1.05f, Open),
        K(0.0000f, 1.000f, 1.000f, 0f, 1.00f, Open),

        // 33-37: the five rest-registered eye cells, from this shell's own rest pose.
        K(0.0000f, 1.000f, 1.000f, 0f, 1.00f, ThreeQ),
        K(0.0000f, 1.000f, 1.000f, 0f, 1.00f, HalfShut),
        K(0.0000f, 1.000f, 1.000f, 0f, 1.00f, Quarter),
        K(0.0000f, 1.000f, 1.000f, 0f, 1.00f, Drowsy),
        K(0.0000f, 1.000f, 1.000f, 0f, 1.00f, Heavy),
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

    /// <summary>The SCALE only - about the base, this shell's ground relationship. Note there is
    /// no lift in here: the bob is applied with the swing, after, because both belong to the
    /// hanging rather than to the shape.</summary>
    public static LinePose Posed(Channels c) =>
        new(c[(int)Ch.Sx], c[(int)Ch.Sy], 0f, CX, BaseBot);

    private static float Lean(Channels c) => Sway * MathF.Sin(MathF.Tau * c[(int)Ch.Theta]);

    /// <summary>Scale, then swing about the ring, then lift. Applied to FINISHED points, so no
    /// stroke width is ever derived through the rotation.</summary>
    private static Vector2 Hang(Channels c, Vector2 p) =>
        Swing(p, new Vector2(CX, RingCy), Lean(c), c[(int)Ch.Dy]);

    private static Vector2 Pt(Channels c, float x, float y) => Hang(c, Posed(c).Pt(x, y));

    private static Vector2 EyePt(Channels c, float x, float y) => Hang(c, Posed(c).EyePt(x, y));

    private static Vector2 NubPt(Channels c, int side)
    {
        var w = BulbRx * MathF.Sqrt(MathF.Max(0f, 1f - MathF.Pow((NubY - BulbCy) / BulbRy, 2f)));
        return Pt(c, CX + (side * w), NubY);
    }

    public static Vector2 Anchor0(string name) => name switch
    {
        "head" => new Vector2(CX, HeadY),
        "face" => new Vector2(CX, EyeY - FaceLift),
        "body" => new Vector2(CX, BulbCy),
        "mouth" => new Vector2(CX, MouthY),
        _ => new Vector2(CX, BulbCy),
    };

    public static Vector2 Anchor(string name, Channels c)
    {
        if (name is "handL" or "handR")
        {
            return NubPt(c, name == "handL" ? -1 : 1);
        }

        var a = Anchor0(name);
        return name is "face" or "head" ? EyePt(c, a.X, a.Y) : Pt(c, a.X, a.Y);
    }

    // -------------------------------------------------------------------- paths --

    /// <summary>The occupant: a soft egg, wider at the bottom than the top, so it reads as
    /// something SITTING in the lantern rather than floating in it.</summary>
    private static void SoulPath(LineCanvas canvas, Channels c, float inset = 0f, Vector2 shift = default)
    {
        var hw = (SoulHw - inset) * c[(int)Ch.Sx];
        var hh = (SoulHh - inset) * c[(int)Ch.Sy];
        var mid = Pt(c, CX, SoulCy) + shift;

        Vector2 P(float fx, float fy) => mid + new Vector2(fx * hw, fy * hh);

        canvas.MoveTo(P(0f, -1f));
        canvas.CubicTo(P(-0.60f, -0.82f), P(-1f, -0.32f), P(-1f, 0.26f));
        canvas.CubicTo(P(-1f, 0.74f), P(-0.55f, 1f), P(0f, 1f));
        canvas.CubicTo(P(0.55f, 1f), P(1f, 0.74f), P(1f, 0.26f));
        canvas.CubicTo(P(1f, -0.32f), P(0.60f, -0.82f), P(0f, -1f));
    }

    /// <summary>The flame, standing on the soul's crown.</summary>
    private static void WickPath(LineCanvas canvas, Channels c, bool inner)
    {
        var hw = SoulHw * c[(int)Ch.Sx];
        var hh = SoulHh * c[(int)Ch.Sy];
        var mid = Pt(c, CX, SoulCy);
        var k = 1f + ((c[(int)Ch.Glow] - 1f) * WickGain);
        var w = inner ? 0.55f : 1f;

        Vector2 P(float fx, float fy) => mid + new Vector2(fx * hw, fy * hh * k);

        canvas.MoveTo(P(-0.30f * w, -0.92f));
        canvas.CubicTo(
            P(-0.40f * w, -1.20f - (0.12f * w)),
            P(-0.10f * w, -1.34f - (0.14f * w)),
            P(0f, -1.52f - (0.16f * w)));
        canvas.CubicTo(
            P(0.14f * w, -1.30f - (0.12f * w)),
            P(0.34f * w, -1.18f - (0.10f * w)),
            P(0.30f * w, -0.92f));
    }

    /// <summary>The brass cap: a shallow lid over the bulb's crown. Left OPEN for the ink, so its
    /// bottom edge - which is exactly the collar's top - is never stroked across it.</summary>
    private static void CapPath(LineCanvas canvas, Channels c)
    {
        canvas.MoveTo(Pt(c, CX - CapHw, CapBot));
        canvas.CubicTo(
            Pt(c, CX - (CapHw * 0.92f), CapTop + 12f),
            Pt(c, CX - (CapHw * 0.46f), CapTop),
            Pt(c, CX, CapTop));
        canvas.CubicTo(
            Pt(c, CX + (CapHw * 0.46f), CapTop),
            Pt(c, CX + (CapHw * 0.92f), CapTop + 12f),
            Pt(c, CX + CapHw, CapBot));
    }

    /// <summary>A rounded bar, posed - the collar and the base, the two pieces of brass that are
    /// simply bars.</summary>
    private static void Bar(LineCanvas canvas, Channels c, float y, float hw, float h, float r)
    {
        // BUILT IN THE POSED FRAME AND ROTATED AFTER. Building it from two already-swung corners
        // and squaring a rectangle off them, as the first cut did, makes the bar's height a
        // function of the swing angle: the two corners rotate apart, the axis-aligned box that
        // fits them grows, and the collar and base visibly fatten and thin as the lamp rocks.
        // A bar that hangs from a swinging lamp TURNS. It does not change size.
        var q = Posed(c);
        var tl = q.Pt(CX - hw, y);
        var br = q.Pt(CX + hw, y + h);
        var rr = MathF.Min(r, MathF.Abs(br.Y - tl.Y) * 0.5f);

        Vector2 S(float px, float py) => Hang(c, new Vector2(px, py));

        canvas.MoveTo(S(tl.X + rr, tl.Y));
        canvas.LineTo(S(br.X - rr, tl.Y));
        canvas.QuadTo(S(br.X, tl.Y), S(br.X, tl.Y + rr));
        canvas.LineTo(S(br.X, br.Y - rr));
        canvas.QuadTo(S(br.X, br.Y), S(br.X - rr, br.Y));
        canvas.LineTo(S(tl.X + rr, br.Y));
        canvas.QuadTo(S(tl.X, br.Y), S(tl.X, br.Y - rr));
        canvas.LineTo(S(tl.X, tl.Y + rr));
        canvas.QuadTo(S(tl.X, tl.Y), S(tl.X + rr, tl.Y));
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

        var bulb = Pt(ch, CX, BulbCy);
        var brx = BulbRx * ch[(int)Ch.Sx];
        var bry = BulbRy * ch[(int)Ch.Sy];
        var glow = ch[(int)Ch.Glow];

        // Nubs first, then the glass over their inner halves.
        var nubR = NubR * (ch[(int)Ch.Sx] + ch[(int)Ch.Sy]) * 0.5f;
        for (var i = 0; i < 2; i++)
        {
            c.Ellipse(NubPt(ch, i == 0 ? -1 : 1), nubR, nubR, Tint(body, NubFill));
        }

        // Glass, occupant and brass, in that order: everything INSIDE the lantern is drawn
        // before the metal that holds it, so the cap and the base close over the bulb rather
        // than being outlined against it.
        c.Ellipse(bulb, brx, bry, Tint(body, GlassEdge));
        c.EllipseIn(
            bulb - new Vector2(8f * ch[(int)Ch.Sx], 8f * ch[(int)Ch.Sy]),
            brx - (9f * ch[(int)Ch.Sx]), bry - (9f * ch[(int)Ch.Sy]),
            bulb, brx, bry, Tint(body, GlassFill));

        // The glass catch, clipped to the bulb.
        c.MoveTo(Pt(ch, CX - (BulbRx * 0.60f), BulbCy - (BulbRy * 0.66f)));
        var catchB = Pt(ch, CX - (BulbRx * 0.80f), BulbCy - (BulbRy * 0.34f));
        c.CubicTo(catchB, catchB, Pt(ch, CX - (BulbRx * 0.84f), BulbCy + (BulbRy * 0.10f)));
        c.Stroke(Tint(body, Rim), 10f * ch[(int)Ch.Sx], closed: false);

        // THE GLOW. Two soft haloes that grow and brighten with the lamp, clipped to the glass
        // and drawn BEHIND the occupant - which is how the sheet's even-odd knockout reads once
        // the soul is painted over the middle of them. Light AROUND the occupant, never a wash
        // over its face. This is most of what the lantern DOES, and the first cut had none of it.
        var haloMid = Pt(ch, CX, SoulCy);
        var haloR = (SoulHw + 30f) * glow;
        foreach (var (scale, op) in new[] { (1.00f, 0.46f), (0.62f, 0.40f) })
        {
            c.EllipseIn(
                haloMid,
                haloR * scale * ch[(int)Ch.Sx],
                haloR * scale * 1.06f * ch[(int)Ch.Sy],
                bulb, brx, bry,
                Tint(accent, AccRim) with { W = MathF.Min(0.80f, op * glow) });
        }

        SoulPath(c, ch);
        c.Fill(Tint(body, Base));
        SoulPath(c, ch, 7f, new Vector2(-4f * ch[(int)Ch.Sx], -3f * ch[(int)Ch.Sy]));
        c.Fill(Tint(body, Rim));

        // The flame stands ON the soul's crown, so it goes over it - and it brightens with the
        // glow rather than growing: a startled lamp brightens far more than it moves.
        WickPath(c, ch, inner: false);
        c.Fill(Tint(accent, AccBase));
        WickPath(c, ch, inner: true);
        c.Fill(Tint(accent, AccRim));

        // The brass: cap, collar, base.
        CapPath(c, ch);
        c.Fill(Tint(body, Brass));
        c.MoveTo(Pt(ch, CX - (CapHw * 0.46f), CapTop + 9f));
        c.LineTo(Pt(ch, CX + (CapHw * 0.46f), CapTop + 9f));
        c.Stroke(Tint(body, BrassRim), 7f * ch[(int)Ch.Sy], closed: false);

        Bar(c, ch, CollarY, CollarHw, CollarH, 9f);
        c.Fill(Tint(body, Brass));
        Bar(c, ch, BaseY, BaseHw, BaseH, 11f);
        c.Fill(Tint(body, Brass));

        if (blush > 0f)
        {
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? -1 : 1;
                c.Ellipse(EyePt(ch, CX + (side * (EyeDx + 26f)), EyeY + 20f), 12f, 8f, BlushTint(blush));
            }
        }

        // -- the ink.
        c.EllipseStroke(bulb, brx, bry, ink, 12f);
        SoulPath(c, ch);
        c.Stroke(ink, 9f);
        CapPath(c, ch);
        c.Stroke(ink, 10f, closed: false);
        Bar(c, ch, CollarY, CollarHw, CollarH, 9f);
        c.Stroke(ink, 10f);
        Bar(c, ch, BaseY, BaseHw, BaseH, 11f);
        c.Stroke(ink, 10f);

        // The ring it hangs from, which is also the point it swings about.
        var ring = Pt(ch, CX, RingCy);
        var rr = RingR * (ch[(int)Ch.Sx] + ch[(int)Ch.Sy]) * 0.5f;
        c.EllipseStroke(ring, rr, rr, ink, 9f);

        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            var at = NubPt(ch, side);
            var quarter = MathF.PI / 2f;
            c.Arc(at, nubR, -quarter, side < 0 ? -MathF.PI - quarter : quarter, ink, 10f);
        }

        DrawEyes(c, Rig, eye, side => EyePt(ch, CX + (side * EyeDx), EyeY), Posed(ch).Ex, Posed(ch).Ey, eyeTint, ink);
    }
}
