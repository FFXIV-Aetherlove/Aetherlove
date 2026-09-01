namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using static AetherOS.PetKit.Rendering.LineArt.LineShell;

/// <summary>
/// The Muffle, drawn. Eleventh shell, first that was never a sheet.
///
/// <para><b>Every shell before this one was a conversion.</b> Ten sheet generators existed and
/// each C# file is a transcription of one: same names, same numbers. The generators went with
/// the foundry on 2026-08-28; their sheets are the design of record in
/// <c>art-intake/retired-sheets/</c>. This shell has no sheet to convert and
/// never will; its generator is
/// <c>art-intake/element-shells/muffle-master/build_master.py</c>, written after the sheet
/// pipeline was retired, and the relationship is identical. That is the point: the master
/// format had to prove it could brief a shell into existence rather than only re-describe
/// one.</para>
///
/// <para><b>Why it exists.</b> Ice was the only element on the wheel with nothing at all in it
/// (<c>ElementRosterStudy.md</c>), and of the two Ice candidates this is the one built from two
/// circles: the cheapest possible shape to debug a new authoring format on.</para>
///
/// <para><b>Two channels are new</b> and both live on the head, because on a two-ball body the
/// head is the only part that CAN carry secondary motion. <see cref="Ch.Sink"/> is how far it
/// settles into the base; <see cref="Ch.Lean"/> (already on the roster, from the Spintop) is its
/// lateral lag. Between them they do what a jointed rig would otherwise be needed for: on the
/// hop the head is still going down when the body has started up, and still coming up when the
/// body lands.</para>
///
/// <para><b>What it deliberately does not have.</b> No scarf, no carrot, no twig arms, no drift
/// at its feet. Every one of those is something the wardrobe already sells or the arm code
/// already draws, and the drift was a second silhouette that would have travelled to the race
/// rail and the 24 px tile where the ground is somewhere else. What is left is two circles,
/// three coals and the way the snow is packed, which turned out to be enough, once the head
/// was big enough to carry the eyes. Two similar circles are a doll; a small head on a big base is a
/// snowman, and no amount of marking substitutes for that one ratio.</para>
/// </summary>
public static class MuffleLineArt
{
    // ---------------------------------------------------------------- geometry --
    // Lifted from build_master.py unchanged. Same names, same numbers: a port that renames
    // things is a port nobody can diff against the generator it came from.
    public const float Cell = 384f;

    public const float CX = 192f;

    /// <summary>Where the shell meets the ground line, and the pivot everything squashes about.
    /// A ball that squashes about its centre sinks through the floor; one that squashes about
    /// its top hovers.</summary>
    private const float Sole = 309f;

    /// <summary>Scaled up 1.15 from the first cut, on the owner's eye: next to the rest of the
    /// roster he read SMALL. The cause was width rather than height: he was already the tallest
    /// thing drawn, but a 156-wide body against the Puffer's 190 and the Jelly's 200 is less
    /// MASS, and mass is what the eye sizes a creature by. The ratio below is untouched, so the
    /// proportions that were signed off are exactly the proportions here.</summary>
    private const float BodyR = 90f;

    /// <summary>0.78 of the base, and the number that carries the whole read.
    ///
    /// <para><b>Grown from 63 because the eyes were touching the sides</b>, and the amount was
    /// measured rather than nudged: the eye ring's outer edge sits at
    /// <c>Dx + Rx + RingW/2 = 31 + 27.5 + 4.5 = 63.0</c>, which was exactly the old radius, so
    /// at the eyes' own latitude the ring was crossing the outline by half a pixel. At 70 there
    /// is 6.5 px of head outside the ring.</para></summary>
    private const float HeadR = 70f;

    /// <summary>How far apart the two centres sit, as a fraction of the summed radii.
    ///
    /// <para>Came down 0.71 → 0.68 in the same move as the head grew, so the creature does not
    /// get TALLER as well as wider: a bigger ball set deeper keeps the silhouette's total height
    /// within 7 px of what it was, and a head set further into the base is the more snowman-like
    /// reading anyway.</para></summary>
    private const float Seat = 0.68f;

    private const float SeatD = (BodyR + HeadR) * Seat;

    private static readonly float BodyCY = Sole - BodyR;
    private static readonly float HeadCY = BodyCY - SeatD;

    /// <summary>Hand roots, in degrees from twelve o'clock on the base ball. 70 read as ears and
    /// 84 sat too low to be shoulders.</summary>
    private const float NubDeg = 74f;

    private const float NubR = 17f;

    /// <summary>The LEFT root's authoring x, because that is the one
    /// <see cref="DrawNubs"/> takes: it mirrors the other about <paramref name="cx"/> itself,
    /// and handing it the right-hand root instead simply swaps which side gets which arc.</summary>
    private static readonly float NubX = CX - (BodyR * MathF.Sin(NubDeg * MathF.PI / 180f));

    private static readonly float NubY = BodyCY - (BodyR * MathF.Cos(NubDeg * MathF.PI / 180f));

    /// <summary>The coals: offset from the base-ball centre, and radius. Drawn in the INK rather
    /// than the accent: ArtDirection's own words for the outline are "dark-grey, which becomes
    /// dark of body colour automatically after tinting", which is exactly what a coal is. So they
    /// tint correctly under every palette for free and cost the accent layer nothing.</summary>
    private static readonly (float Dy, float R)[] Coals =
    [
        (-23f, 12.7f),
        (14f, 11f),
        (51f, 9.2f),
    ];

    /// <summary>The sparkle, and the ONLY job left on the accent layer once the scarf and the
    /// carrot went back to the wardrobe, which makes this the cheapest four-layer shell on the
    /// roster. All upper-left, because there is one light and a sparkle scattered round a body is
    /// a sparkle that says the light is everywhere. (Ball, degrees, radius fraction.)</summary>
    private static readonly (bool Head, float Deg, float R)[] Glints =
    [
        (false, 318f, 0.62f),
        (false, 288f, 0.34f),
        (true, 322f, 0.58f),
    ];

    /// <summary>The two soft areas, as fractions of the ball each sits on.
    ///
    /// <para><b>Both used to be strokes drawn after the ink</b>, and both read as a BAND laid
    /// across the creature rather than as light on it: a stroke has two edges and a constant
    /// width, which is what a band <i>is</i>. They are filled lenses now, drawn BEFORE the ink
    /// and clipped to their ball, so the outline closes over them and the shape is bounded by
    /// the drawing instead of crossing it.</para>
    ///
    /// <para><b>Chin</b>: settled snow where the head was set down. Placed on the HEAD's numbers
    /// but clipped to the BASE ball, with the head drawn over it afterwards: the head's own arc
    /// cuts the top, so the visible remainder is a crescent under the chin that cannot fall out
    /// of register with the head however far <see cref="Ch.Sink"/> moves it. The old stroke had
    /// to be given a share of Sink by hand to keep up.</para>
    ///
    /// <para><b>Moon</b>: bounce light along the base: a flat lens sunk past the bottom of the
    /// ball, so the ball's own edge cuts it into a crescent.</para></summary>
    private const float ChinCy = 0.80f, ChinRx = 0.92f, ChinRy = 0.52f;

    private const float MoonCy = 0.88f, MoonRx = 0.72f, MoonRy = 0.26f;

    /// <summary>The lit inset, offset up-left and CLIPPED to the ball (<see
    /// cref="LineCanvas.EllipseIn"/>). It reaches 1.08 of the radius on purpose: a real highlight
    /// keeps a wide crescent on the far side and a hard stop on the near one, and shrinking it to
    /// fit gives neither.</summary>
    private const float LightD = 0.20f, LightK = 0.88f;

    /// <summary><b>This shell draws no mouth.</b> <c>MouthY</c> is an ANCHOR, not a mark: the
    /// engine draws the mouth itself, on this point, through the animation stack, which is why
    /// every drawn shell on the roster stops at the eyes and says so. The first cut of this one
    /// drew a mouth as well and the pet wore TWO, one over the other. A shell owes the mouth a
    /// seat and nothing more.</summary>
    private const float EyeY = 119f, MouthY = 161f;

    private const float BlushDx = 44f, BlushY = 140f;

    /// <summary>How far ABOVE the eyes the wardrobe's <c>face</c> anchor sits.
    ///
    /// <para>Every glasses sprite pins its own top-centre to that anchor and hangs the lenses
    /// below it: Round Glasses is 126×62 with origin (63, 1), so the lens centres land about +30
    /// in 256-space, which is +45 here. Put the anchor on the eyes and the glasses sit on the
    /// chin.</para>
    ///
    /// <para>42 is not invented: it is what every shell needing NO glasses correction already
    /// uses: Jelly −42, Crab −42, Spintop −42, Wisp −42.5. The two at 0 (Puffer, Nautilus) buy
    /// the same fix a second time with a per-item <c>dy</c> instead.</para></summary>
    private const float FaceLift = 42f;

    private static readonly float EyeOffY = EyeY - HeadCY;
    private static readonly float MouthOffY = MouthY - HeadCY;
    private static readonly float BlushOffY = BlushY - HeadCY;

    /// <summary>This shell's face, as fifteen numbers, in the record's own field order so it
    /// copies straight out of the master's <c>RIG</c>.</summary>
    private static readonly EyeRig Rig = new(
        Dx: 31f, Y: EyeY, Rx: 27.5f, Ry: 34.5f,
        PupilRx: 19f, PupilRy: 25f, RingW: 9f, PupilOut: 4f,
        BigDx: 8.6f, BigDy: 14f, BigR: 7.4f,
        SmallDx: 7f, SmallDy: 11.5f, SmallR: 3.7f,
        ShutBow: 17f, LashW: 10f);

    /// <summary>The ink weight, in authoring units: a number rather than a pixel, which is half
    /// the point of drawing rather than baking.</summary>
    public static float InkWidth { get; set; } = 12f;

    /// <summary>Packed snow: nearly rigid, with just enough give that the head settles rather
    /// than arrives. Well below the Jelly's 0.85 and a little above the Crab's 0.05, and the
    /// give matters more here than on most shells, because it lands on
    /// <see cref="Ch.Sink"/> and a settling head is the thing this creature does.</summary>
    public static readonly Material Stuff = new(Springiness: 0.22f, TrimLag: 0.45f);

    // ------------------------------------------------------------------- poses --
    // build_master.py's POSES, verbatim. sx, sy, dy, sink, lean, eye, blush.

    private static Key K(float sx, float sy, float dy, float sink, float lean, EyeState eye, bool blush = false)
    {
        var c = Neutral();
        c[(int)Ch.Sx] = sx;
        c[(int)Ch.Sy] = sy;
        c[(int)Ch.Dy] = dy;
        c[(int)Ch.Sink] = sink;
        c[(int)Ch.Lean] = lean;
        return new Key(c, eye, blush);
    }

    private static readonly Key[] Poses =
    [
        // idle 0-7: the smallest breath on the roster, and almost all of it is Sink: the head
        // settling and un-settling. Ice's motion signature is stillness, and a shell whose idle
        // is a SETTLE rather than a bob is the only one here that can be still without looking
        // frozen.
        K(1.000f, 1.000f, 0f, 0.0f, 0.0f, Open),
        K(0.995f, 1.006f, -1f, -0.4f, 0.4f, Open),
        K(0.992f, 1.010f, -2f, -0.8f, 0.6f, Open),
        K(0.995f, 1.006f, -1f, -0.4f, 0.3f, Open),
        K(1.000f, 1.000f, 0f, 0.0f, 0.0f, Open),
        K(1.005f, 0.995f, 1f, 0.5f, -0.4f, Open),
        K(1.008f, 0.992f, 1f, 0.8f, -0.6f, Open),
        K(1.004f, 0.996f, 0f, 0.4f, -0.3f, Open),

        // blink 8-10
        K(1.000f, 1.000f, 0f, 0f, 0f, Open),
        K(1.000f, 1.000f, 0f, 0f, 0f, Shut),
        K(1.000f, 1.000f, 0f, 0f, 0f, HalfShut),

        // boop 11-16: the head lags, sinks, and re-seats. Lean goes one way under the squash and
        // OVERSHOOTS the other way on the rebound, which is the whole of follow-through in two
        // numbers.
        K(0.980f, 1.030f, -2f, -1.5f, 0.0f, Wide),
        K(1.120f, 0.880f, 5f, 5.0f, -6.0f, Wide),
        K(1.170f, 0.830f, 7f, 7.5f, -9.0f, Squint),
        K(0.920f, 1.110f, -6f, -4.0f, 7.0f, Wide),
        K(1.040f, 0.970f, 2f, 1.5f, -3.0f, Open),
        K(1.000f, 1.000f, 0f, 0.0f, 0.0f, Happy, true),

        // nap 17-22: sunk deep and squat. A snowman asleep is a snowman slumping, which is the
        // one drowsy read this shape gives away free.
        K(1.040f, 0.960f, 3.0f, 5.0f, 0.0f, Shut, true),
        K(1.050f, 0.950f, 4.0f, 5.6f, 0.5f, Shut, true),
        K(1.060f, 0.940f, 5.0f, 6.2f, 0.8f, Shut, true),
        K(1.055f, 0.945f, 4.5f, 5.9f, 0.5f, Shut, true),
        K(1.050f, 0.950f, 4.0f, 5.5f, 0.0f, Shut, true),
        K(1.045f, 0.955f, 3.5f, 5.2f, -0.3f, Shut, true),

        // hop 23-32: Sink lags the head through the whole clip: still going down when the body
        // has started up, still coming up when the body lands.
        K(1.060f, 0.940f, 3f, 3.0f, 0f, Open),
        K(1.120f, 0.880f, 6f, 6.0f, 0f, Open),
        K(0.940f, 1.090f, -8f, -5.0f, 0f, Wide),
        K(0.920f, 1.120f, -26f, -7.0f, 0f, Wide),
        K(0.940f, 1.080f, -34f, -6.0f, 0f, Open),
        K(0.960f, 1.050f, -26f, -4.0f, 0f, Open),
        K(0.980f, 1.020f, -10f, -2.0f, 0f, Wide),
        K(1.160f, 0.850f, 6f, 8.0f, 0f, Squint),
        K(0.970f, 1.050f, -4f, -3.0f, 0f, Open),
        K(1.000f, 1.000f, 0f, 0.0f, 0f, Open),

        // 33-37: the five rest-registered eye cells every shell owes the engine. Leave them out
        // and every drowsy state clamps back to the rest cell and the pet simply stares.
        K(1.000f, 1.000f, 0f, 0f, 0f, ThreeQ),
        K(1.000f, 1.000f, 0f, 0f, 0f, HalfShut),
        K(1.000f, 1.000f, 0f, 0f, 0f, Quarter),
        K(1.000f, 1.000f, 0f, 0f, 0f, Drowsy),
        K(1.000f, 1.000f, 0f, 0f, 0f, Heavy),
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

    /// <summary>Channels to this shell's own transform: squash about the SOLE, then lift.
    ///
    /// <para><see cref="Ch.Sink"/> and <see cref="Ch.Lean"/> are deliberately not folded in here.
    /// A <see cref="LinePose"/> is one affine map and those two move one mass relative to
    /// another, which is not one, so the base ball reads this pose and the head reads it plus
    /// the two offsets, in <see cref="Head"/>. Folding them in would have moved the coals and the
    /// hand roots with the head, which are on the other ball entirely.</para></summary>
    public static LinePose Posed(Channels c) =>
        new(c[(int)Ch.Sx], c[(int)Ch.Sy], c[(int)Ch.Dy], CX, Sole);

    /// <summary>The head ball under a pose: centre, and its two radii.
    ///
    /// <para>It takes HALF the body's squash through <see cref="LinePose.Ex"/>/<see
    /// cref="LinePose.Ey"/>: the roster-wide rule that the soul is inside the vessel and does
    /// not deform with it: then <see cref="Ch.Sink"/> drops it into the base and
    /// <see cref="Ch.Lean"/> lags it sideways.</para></summary>
    private static (Vector2 At, float Rx, float Ry) Head(LinePose q, Channels c) => (
        new Vector2(q.X(CX) + c[(int)Ch.Lean], q.Y(HeadCY) + c[(int)Ch.Sink]),
        HeadR * q.Ex,
        HeadR * q.Ey);

    private static (Vector2 At, float Rx, float Ry) Body(LinePose q) => (
        q.Pt(CX, BodyCY), BodyR * q.Sx, BodyR * q.Sy);

    private static Vector2 On((Vector2 At, float Rx, float Ry) ball, float deg, float r = 1f)
    {
        var a = deg * MathF.PI / 180f;
        return ball.At + new Vector2(ball.Rx * r * MathF.Sin(a), -ball.Ry * r * MathF.Cos(a));
    }

    /// <summary>The shell's anchors in authoring space, neutral pose: the same table
    /// <c>build_master.py</c>'s <c>anchors_for</c> bakes per cell.
    ///
    /// <para><c>head</c> is deliberately not the crown of the drawing: it is where a hat brim has
    /// to sit to look right, which on a ball is a little way down the dome.</para></summary>
    public static Vector2 Anchor0(string name) => name switch
    {
        "head" => new Vector2(CX, HeadCY - (HeadR * 0.78f)),
        "face" => new Vector2(CX, EyeY - FaceLift),
        "body" => new Vector2(CX, BodyCY - (BodyR * 0.10f)),
        "handL" => new Vector2(NubX, NubY),
        "handR" => new Vector2((2f * CX) - NubX, NubY),
        "mouth" => new Vector2(CX, MouthY),
        "earL" => new Vector2(CX - (HeadR * 0.86f), HeadCY - (HeadR * 0.46f)),
        "earR" => new Vector2(CX + (HeadR * 0.86f), HeadCY - (HeadR * 0.46f)),
        "tail" => new Vector2(CX, BodyCY + (BodyR * 0.52f)),
        _ => new Vector2(CX, BodyCY),
    };

    /// <summary>A worn pin, moved the way this body moves it.
    ///
    /// <para>The position is the caller's (the manifest's rest-cell anchor, where the wardrobe
    /// was tuned) and this decides only the transform. The hands are the exception and always
    /// will be: they attach to a nub this file draws, so the file knows where it is better than
    /// any table does.</para>
    ///
    /// <para><b>Face and head take the head ball's whole motion here, not a half-squash.</b> On
    /// every other shell those two are the same body taking a gentler deform; on this one they
    /// are a DIFFERENT MASS that sinks and lags, so a hat left on the base's transform would
    /// stay put while the head it is sitting on settled out from under it.</para></summary>
    public static Vector2 Pin(string name, PinKind kind, Vector2 rest, Channels c)
    {
        var q = Posed(c);
        if (kind == PinKind.Hand)
        {
            return q.Pt(Anchor0(name).X, Anchor0(name).Y);
        }

        if (kind is PinKind.Face or PinKind.Head)
        {
            // The rest point expressed relative to the head's rest centre, then carried by
            // wherever the head actually went. Identity at neutral, like every shell transform.
            var head = Head(q, c);
            return new Vector2(
                head.At.X + ((rest.X - CX) * q.Ex),
                head.At.Y + ((rest.Y - HeadCY) * q.Ey));
        }

        return q.Pt(rest.X, rest.Y);
    }

    // -------------------------------------------------------------------- draw --

    /// <summary>Draws the whole creature. Takes the same bottom-centre and display size
    /// <c>PetDraw.Draw</c> does, so this can stand in anywhere a sheet pet is drawn.</summary>
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
        var q = Posed(ch);
        var trim = Posed(trimCh);
        var head = Head(q, ch);
        var baseBall = Body(q);

        c.Begin(dl, bottomCentre, displaySize, Cell, outer, flip);

        var inside = InsideOf(baseBall);

        // The hand roots go down FIRST and the ball covers their inner half, which is what turns
        // a circle into a shoulder. Drawn over the body they read as buttons stuck on it.
        DrawNubs(c, q, CX, NubX, NubY, NubR, InkWidth, body, ink, fill: true);

        // The base ball: fill, then EVERYTHING SOFT, then its ink. The outline is what closes
        // each shape, so nothing soft may be drawn after it or it lies across the lines instead
        // of under them.
        c.Ellipse(baseBall.At, baseBall.Rx, baseBall.Ry, Tint(body, Base));
        Lit(c, baseBall, body);
        Soft(c, baseBall,
            new Vector2(baseBall.At.X, baseBall.At.Y + (baseBall.Ry * MoonCy)),
            baseBall.Rx * MoonRx, baseBall.Ry * MoonRy, Tint(body, Rim) with { W = 0.55f });
        Soft(c, baseBall,
            new Vector2(head.At.X, head.At.Y + (head.Ry * ChinCy)),
            head.Rx * ChinRx, head.Ry * ChinRy, Tint(body, Rim) with { W = 0.60f });
        c.EllipseStroke(baseBall.At, baseBall.Rx, baseBall.Ry, ink, InkWidth, 40);

        // --- the coals. TRIMMINGS: they sit on the ball rather than being part of its outline,
        // so they may arrive a beat after it without anything tearing.
        foreach (var (dy, r) in Coals)
        {
            var at = trim.Pt(CX, BodyCY + dy);
            var rr = r * (trim.Sx + trim.Sy) * 0.5f;
            c.Ellipse(at, rr, rr, ink);

            // one catch each, so a coal reads as wet stone against matte snow rather than a hole
            c.Ellipse(
                at - new Vector2(rr * 0.34f, rr * 0.34f),
                rr * 0.26f, rr * 0.26f, Tint(body, Rim) with { W = 0.55f });
        }

        // --- the head ball, OVER the chin lens: its arc is what cuts the crescent, and its ink
        // is what closes the cut. Filled after the base's ink so it covers it where they overlap
        // and the pair reads as one silhouette with a head sitting on it.
        c.Ellipse(head.At, head.Rx, head.Ry, Tint(body, Base));
        Lit(c, head, body);
        c.EllipseStroke(head.At, head.Rx, head.Ry, ink, InkWidth, 40);

        // --- the sparkle: the accent layer's only job on this shell
        foreach (var (onHead, deg, r) in Glints)
        {
            var at = On(onHead ? head : baseBall, deg, r);
            c.MoveTo(at - new Vector2(7f, 0f));
            c.LineTo(at + new Vector2(7f, 0f));
            c.Stroke(Tint(accent, AccRim), 4f, closed: false);
            c.MoveTo(at - new Vector2(0f, 7f));
            c.LineTo(at + new Vector2(0f, 7f));
            c.Stroke(Tint(accent, AccRim), 4f, closed: false);
        }

        if (blush > 0f)
        {
            for (var side = -1; side <= 1; side += 2)
            {
                c.Ellipse(
                    new Vector2(head.At.X + (side * BlushDx * q.Ex), head.At.Y + (BlushOffY * q.Ey)),
                    15f * q.Ex, 9f * q.Ey, LineShell.BlushTint(blush));
            }
        }

        DrawEyes(
            c, Rig, eye,
            side => new Vector2(head.At.X + (side * Rig.Dx * q.Ex), head.At.Y + (EyeOffY * q.Ey)),
            q.Ex, q.Ey, eyeTint, ink);

        // NO MOUTH is drawn here. See the note at MouthY: the engine draws it, on the anchor
        // this shell publishes, and a shell that draws its own simply gives the pet two.

        // The hand roots' ink, on their OUTER arc only and solved against the real silhouette:
        // a full ring crosses the body outline and the two read as a lens rather than a shoulder.
        DrawNubs(c, q, CX, NubX, NubY, NubR, InkWidth, body, ink, fill: false, insideBody: inside);
    }

    /// <summary>One soft area, clipped to the ball it belongs to: the same
    /// <see cref="LineCanvas.EllipseIn"/> the lit inset uses, and for the same reason: a highlight
    /// that can leave its own body is a highlight lying on top of the drawing.</summary>
    private static void Soft(LineCanvas c, (Vector2 At, float Rx, float Ry) ball, Vector2 at, float rx, float ry, Vector4 colour) =>
        c.EllipseIn(at, rx, ry, ball.At, ball.Rx, ball.Ry, colour);

    /// <summary>The lit inset, clipped to the ball it sits on: <see cref="LineCanvas.EllipseIn"/>,
    /// which exists for exactly this and was added when the Nautilus showed fill outside its own
    /// outline. Inscribing a smaller ellipse instead was tried here and is a worse picture for
    /// the reason that method's own note gives: a real highlight keeps a wide crescent on the far
    /// side AND a hard stop on the near one, and only a clip gives both.</summary>
    private static void Lit(LineCanvas c, (Vector2 At, float Rx, float Ry) ball, Vector4 body) =>
        c.EllipseIn(
            ball.At - new Vector2(ball.Rx * LightD, ball.Ry * LightD),
            ball.Rx * LightK, ball.Ry * LightK,
            ball.At, ball.Rx, ball.Ry,
            Tint(body, Rim) with { W = 0.34f });

    /// <summary>Is a point inside the base ball? What <see cref="DrawNubs"/> needs to stop each
    /// nub's ink exactly where it crosses the silhouette: the flank is curved here, so a fixed
    /// half circle would end in mid air at one end and cut back across the body at the
    /// other.</summary>
    private static Func<Vector2, bool> InsideOf((Vector2 At, float Rx, float Ry) ball) => p =>
    {
        var dx = (p.X - ball.At.X) / MathF.Max(0.001f, ball.Rx);
        var dy = (p.Y - ball.At.Y) / MathF.Max(0.001f, ball.Ry);
        return (dx * dx) + (dy * dy) <= 1f;
    };
}
