namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using static AetherOS.PetKit.Rendering.LineArt.LineShell;

/// <summary>
/// The Jelly, drawn. No sheet, no texture, no cell: the same geometry its retired sheet
/// generator emitted as SVG, evaluated live instead of baked. The sheets it produced are the
/// design of record: <c>art-intake/retired-sheets/jellyv1/</c>.
///
/// <para><b>This is the line-art prototype</b>, and the Jelly is the honest shell to try it on:
/// its tendrils are already code-drawn (<see cref="TentacleFx"/>), so the sheet was only ever
/// carrying a bell and a face. If a drawn shell cannot beat a baked one here it will not beat
/// one anywhere.</para>
///
/// <para><b>What it is meant to prove, in the order the owner asked for it.</b>
/// <list type="number">
/// <item><b>A drawn look.</b> Ink is a stroke rather than a baked overlay, so line weight is a
/// number rather than a pixel and can be tuned live.</item>
/// <item><b>Resolution independence.</b> Nothing here knows the cell was 384. The only size in
/// the file is the caller's <c>displaySize</c>, so the same call draws a 24 px rail pip and a
/// full-screen pet with no second asset and no filtering.</item>
/// <item><b>Continuous animation.</b> The sheet samples the pose curve at 38 cells and bakes
/// each one; <see cref="PoseAt"/> reads BETWEEN those samples, so the swim pulse is a curve
/// rather than eight steps. The pose table is the authored intent, kept verbatim: the aim was
/// to stop quantising it, not to redesign it.</item>
/// <item><b>No pipeline.</b> There is no Chromium, no rasterise step and no PNG. Shipping a
/// shell becomes shipping a file like this one.</item>
/// </list></para>
///
/// <para><b>Known gaps in this cut</b>, none of them load-bearing on the question being asked:
/// accessories, hands and the mouth still come from the existing rigs and are not drawn here;
/// the drowsy pair's straight lid is not built, so the five rest-registered eye cells fall back
/// to the bowed lash line; and the shading is the foundry's tones rather than anything
/// reconsidered for line art. The art direction is exactly the argument to have next, and this
/// is the thing to have it over.</para>
/// </summary>
public static class JellyLineArt
{
    // ---------------------------------------------------------------- geometry --
    // Lifted from the retired build_sheet.py unchanged. Same names, same numbers: a port that renames
    // things is a port nobody can diff against the generator it came from.
    public const float Cell = 384f;

    private const float CX = 192f;
    private const float TOP = 100f;
    private const float HEM = 254f;
    private const float HALF = 100f;
    private const float BellH = HEM - TOP;

    private const int Scallops = 5;
    private const float ScallopDrop = 26f;

    private const float EyeDx = 37f, EyeY = 188f;
    private const float NubX = 92f, NubY = 246f, NubR = 16f;

    /// <summary>This shell's face, as fifteen numbers. The DRAWING is shared
    /// (<see cref="LineShell.DrawEyes"/>) because the Jelly's face and the Crab's turned out to
    /// be the same drawing at different sizes, which is exactly what doing a second shell before
    /// generalising was for.</summary>
    private static readonly EyeRig Rig = new(
        Dx: EyeDx, Y: EyeY, Rx: 32f, Ry: 39f,
        PupilRx: 21.5f, PupilRy: 28.5f, RingW: 10f, PupilOut: 4f,
        BigDx: 9.5f, BigDy: 16f, BigR: 8.2f,
        SmallDx: 8f, SmallDy: 13f, SmallR: 4f,
        ShutBow: 20f, LashW: 11f);


    /// <summary>The ink weight, in authoring units. The sheet bakes 13; a stroke can be
    /// anything, and this being a dial rather than a pixel is half the point of the exercise.</summary>
    public static float InkWidth { get; set; } = 13f;

    // ------------------------------------------------------------------- poses --
    // build_sheet.py's POSES, verbatim. sx, sy, dy, eye, blush.

    /// <summary>This shell's key factory, matching its generator's <c>P(sx, sy, dy, eye,
    /// blush)</c> exactly, so the table below still transcribes line for line. A shell with other
    /// channels writes its own: the Puffer's takes a puff and a spine length.</summary>
    private static Key K(float sx, float sy, float dy, EyeState eye, bool blush)
    {
        var c = Neutral();
        c[(int)Ch.Sx] = sx;
        c[(int)Ch.Sy] = sy;
        c[(int)Ch.Dy] = dy;
        return new Key(c, eye, blush);
    }

    private static readonly Key[] Poses =
    [
        // idle 0-7: the swim pulse. Squeezes narrow and tall, rising; relaxes wide and flat,
        // sinking. This is the clip that most wants to stop being eight steps.
        K(1.00f, 1.000f, 0f, Open, false),
        K(0.97f, 1.045f, -3f, Open, false),
        K(0.94f, 1.080f, -6f, Open, false),
        K(0.96f, 1.050f, -4f, Open, false),
        K(1.00f, 1.000f, 0f, Open, false),
        K(1.04f, 0.965f, 2f, Open, false),
        K(1.06f, 0.950f, 3f, Open, false),
        K(1.03f, 0.975f, 1f, Open, false),

        // blink 8-10
        K(1.00f, 1.00f, 0f, Open, false),
        K(1.00f, 1.00f, 0f, Shut, false),
        K(1.00f, 1.00f, 0f, HalfShut, false),

        // boop 11-16
        K(0.98f, 1.04f, -3f, Wide, false),
        K(1.14f, 0.86f, 7f, Wide, false),
        K(1.21f, 0.79f, 10f, Squint, false),
        K(0.89f, 1.15f, -10f, Wide, false),
        K(1.05f, 0.96f, 2f, Open, false),
        K(1.00f, 1.00f, 0f, Happy, true),

        // nap 17-22
        K(1.05f, 0.95f, 8f, Shut, true),
        K(1.07f, 0.93f, 10f, Shut, true),
        K(1.09f, 0.91f, 12f, Shut, true),
        K(1.08f, 0.92f, 11f, Shut, true),
        K(1.06f, 0.94f, 9f, Shut, true),
        K(1.04f, 0.96f, 8f, Shut, true),

        // hop 23-32
        K(1.08f, 0.92f, 6f, Open, false),
        K(1.17f, 0.83f, 12f, Squint, false),
        K(0.87f, 1.19f, -15f, Wide, false),
        K(0.91f, 1.13f, -36f, Wide, false),
        K(0.98f, 1.02f, -48f, Open, false),
        K(0.94f, 1.08f, -34f, Open, false),
        K(0.90f, 1.14f, -12f, Wide, false),
        K(1.19f, 0.81f, 11f, Squint, false),
        K(1.07f, 0.94f, 4f, Open, false),
        K(1.00f, 1.00f, 0f, Open, false),

        // 33-37: the five rest-registered eye cells every sheet owes the engine. Built from this
        // shell's OWN rest pose (cell 32) with nothing changed but the lids, which is what
        // sheetkit's eye_poses does and why they cannot drift from it. They were MISSING from
        // the first cut of this table, so every drowsy and half-lidded state clamped back to
        // cell 32 and the pet simply stared.
        K(1.00f, 1.00f, 0f, ThreeQ, false),
        K(1.00f, 1.00f, 0f, HalfShut, false),
        K(1.00f, 1.00f, 0f, Quarter, false),
        K(1.00f, 1.00f, 0f, Drowsy, false),
        K(1.00f, 1.00f, 0f, Heavy, false),
    ];

    /// <summary>A jellyfish is the slackest thing on the roster and should read as it: the bell
    /// swings past its pose and settles rather than arriving at it. This is the number the whole
    /// Material contract exists for - the Crab beside it sits at 0.05.</summary>
    public static readonly Material Stuff = new(Springiness: 0.85f, TrimLag: 0.70f);

    /// <summary>Where a named part's authored coordinates hang from. The Jelly has only one, and
    /// declares it anyway: a shell that needs a second (the Crab's pincers) should not be the
    /// only one that says where its parts live.</summary>
    public static Vector2 PartOrigin(string part) => new(CX, HEM);

    /// <summary>Lets this shell's ambient channels run through a clip that does not act them.</summary>
    public static Channels WithAmbient(Channels target, int[] frames, float beat, out bool clipDrives, out float driven)
        => LineShell.WithAmbient(target, Poses, frames, beat, out clipDrives, out driven);

    /// <summary>The pose for a cell, blended toward the next one by <paramref name="phase"/>.
    /// <paramref name="next"/> is passed rather than assumed to be <c>cell + 1</c> because a
    /// clip's frames are a list and the cell after the last one is the clip's first.</summary>
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

    /// <summary>The shell's anchors in authoring space, neutral pose: the same <c>ANCHORS0</c>
    /// the generator bakes per cell. Kept here so a drawn body can answer for its own pins
    /// rather than reading 38 rounded samples of them out of a manifest.</summary>
    public static Vector2 Anchor0(string name) => name switch
    {
        "head" => new Vector2(CX, 115f),
        "face" => new Vector2(CX, EyeY - 42f),
        "body" => new Vector2(CX, 214f),
        "handL" => new Vector2(NubX, NubY),
        "handR" => new Vector2((2f * CX) - NubX, NubY),
        "mouth" => new Vector2(CX, 240f),
        "hem" => new Vector2(CX, HEM),
        _ => new Vector2(CX, CX),
    };

    /// <summary>An anchor under a pose, in authoring space. <c>face</c> and <c>head</c> take the
    /// HALF deform the eyes do, which is the generator's own rule and matters: a hat pinned to
    /// the full squash dives into the skull on a hard boop.</summary>
    public static Vector2 Anchor(string name, LinePose q)
    {
        var a = Anchor0(name);
        return name is "face" or "head" ? q.EyePt(a.X, a.Y) : q.Pt(a.X, a.Y);
    }


    /// <summary>A worn pin, moved the way this body moves it.
    ///
    /// <para>The point comes from the caller - the manifest's rest-cell anchor, where the
    /// wardrobe was tuned - and this decides only the transform. The hands are the one exception
    /// and always will be: they attach to a nub this file draws, so the file knows where it is
    /// better than any table does.</para></summary>
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
        // Channels in, this shell's own transform out. Every drawn shell takes the same
        // arguments so the caller needs no special case for any of them.
        var q = Posed(ch);

        c.Begin(dl, bottomCentre, displaySize, Cell, outer, flip);

        // Bioluminescence: Ch.Glow lights the BELL, because the dome is the creature and the
        // crown beads are too small to read. Signed, since half the emote set dims; the two
        // directions are normalised separately because the authored set is not symmetric.
        var lit = ch[(int)Ch.Glow] - 1f;
        var litUp = Math.Clamp(lit / 0.5f, 0f, 1f);
        var litDown = Math.Clamp(-lit / 0.16f, 0f, 1f);
        var bellV = Base + ((Rim - Base) * litUp) - ((Base - Shadow) * litDown);

        // The hand roots go down FIRST and the bell covers their inner half, which is what
        // turns a circle into a shoulder. Drawn over the bell they read as buttons stuck on it.
        var nubR = NubR * (q.Sx + q.Sy) * 0.5f;
        foreach (var nx in new[] { NubX, (2f * CX) - NubX })
        {
            c.Ellipse(q.Pt(nx, NubY), nubR, nubR, Tint(body, NubFill));
        }

        FillBell(c, q, Tint(body, bellV));

        // The underside tone, a soft band below the eye line, and it MUST be clipped to the
        // bell. The generator draws it inside a clip-path against the bell outline; drawn free
        // it hangs 30 units under the hem and shows as a faint disc sitting below the creature,
        // which is exactly what it did in the first cut. Clipped at the hem line rather than
        // against the true outline: the tone is well inside the bell horizontally, so the hem is
        // the only edge it can cross, and a rect is a great deal cheaper than a path clip.
        c.PushClip(
            new Vector2(q.X(CX - HALF) - 2f, q.Y(TOP) - 2f),
            new Vector2(q.X(CX + HALF) + 2f, q.Y(HEM)));
        c.Ellipse(q.Pt(CX, HEM - 10f), HALF * 0.95f * q.Sx, 40f * q.Sy, Tint(body, Shadow) with { W = 0.30f });
        c.PopClip();

        // The bell margin: the accent's own scallop echo, riding above the hem.
        FrillPath(c, q, 22f * q.Sy, 26f * q.Sy);
        c.Stroke(Tint(accent, AccShadow), 13f * q.Sy, closed: false);

        // Two dome markings and a crown bead. Anatomy, not scatter - and PAINT, so they take the
        // body's own pose. They used to ride the lagged trim pose, on the reading that a mark
        // sitting on a creature may arrive a beat late; that is true of an ornament resting on a
        // body and false of pigment in it. Given the lag they disagreed with the bell about
        // where its surface was, and a boop - which snaps the body to a squash while the trim is
        // still a pose behind - is exactly where the disagreement is largest, so the highest
        // mark walked out through the top of the dome. Same finding as the Spintop's stripes,
        // and the same fix: the surface and the paint on it cannot hold two opinions about where
        // it is. Under one pose they cannot, because the dome is an affine image of itself and a
        // mark inside it at rest is inside it at every pose.
        //
        // Which leaves the second half of the report, which the lag only made visible: the crown
        // bead was authored TANGENT to the dome curve - 15 wide at CX-45, where the outline runs
        // through 147 at that latitude - so half of it lay across the ink even at rest. It comes
        // inboard to CX-22, where a 12x7 lens keeps 8 units of clearance and still sits above and
        // left of everything else, which is the whole of what it was drawn to do.
        // The crown beads: brighter and a touch larger when lit, fading when it dims.
        var beadA = (0.85f + (0.15f * litUp)) * (1f - (0.45f * litDown));
        var beadK = 1f + (0.22f * litUp);

        c.Ellipse(q.Pt(CX - 30f, TOP + (BellH * 0.30f)), 11f * q.Sx * beadK, 11f * q.Sy * beadK, Tint(accent, AccBase) with { W = beadA });
        c.Ellipse(q.Pt(CX + 23f, TOP + (BellH * 0.18f)), 8f * q.Sx * beadK, 8f * q.Sy * beadK, Tint(accent, AccBase) with { W = beadA });
        c.Ellipse(q.Pt(CX - 22f, TOP + (BellH * 0.13f)), 12f * q.Sx * beadK, 7f * q.Sy * beadK, Tint(accent, AccRim) with { W = 0.70f * (1f - (0.45f * litDown)) + (0.30f * litUp) });

        // The bloom under the dome reads as light from INSIDE; nothing at rest, so an unlit
        // Jelly is the drawing it always was.
        if (litUp > 0.01f)
        {
            c.Ellipse(
                q.Pt(CX, TOP + (BellH * 0.36f)),
                HALF * 0.80f * q.Sx,
                BellH * 0.34f * q.Sy,
                Tint(accent, AccRim) with { W = 0.30f * litUp });
        }

        if (blush > 0f)
        {
            for (var side = -1; side <= 1; side += 2)
            {
                c.Ellipse(
                    q.EyePt(CX + (side * (EyeDx + 39f)), EyeY + 33f),
                    18f * q.Ex, 11f * q.Ey, BlushTint(blush));
            }
        }

        DrawEyes(c, Rig, eye, side => q.EyePt(CX + (side * Rig.Dx), Rig.Y), q.Ex, q.Ey, eyeTint, ink);

        // The ink last and over everything, which is what makes it read as line art rather
        // than as an outline that happens to be there. It also covers the fills' hard edges,
        // which is what pays for LineCanvas.Fill giving up its antialiasing.
        BellPath(c, q);
        c.Stroke(ink, InkWidth);

        // The hand roots take the body's ink weight, but only on their OUTER arc: a full ring
        // crosses the bell outline and the two read as a lens, where an arc closes the
        // silhouette, which is what a shoulder is.
        //
        // The sweep STOPS where the nub crosses the hem rather than running a flat half circle.
        // A half circle ends at the bottom of the nub, which is below the hem and therefore off
        // the body altogether - the ink finished in mid air and left the visible gap between the
        // nub and the creature. The crossing is worth solving for rather than nudging, because
        // the nub sits exactly on the bell's edge (NubX and CX-HALF are the same 92) and both
        // move with the pose: sin of the angle is the hem's drop over the nub's radius, which
        // reduces to sy/(sx+sy) and so stays correct through every squash.
        var cross = MathF.Asin(Math.Clamp(q.Sy / MathF.Max(0.001f, q.Sx + q.Sy), -1f, 1f));
        var quarter = MathF.PI / 2f;
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            var at = q.Pt(i == 0 ? NubX : (2f * CX) - NubX, NubY);
            c.Arc(
                at, nubR,
                -quarter,
                side < 0 ? -MathF.PI - cross : cross,
                ink, 12f);
        }
    }

    /// <summary>Fills the bell in two parts, and the split is a correctness fix rather than an
    /// optimisation.
    ///
    /// <para>The obvious thing (one fan from a point inside the dome) is WRONG here, and it
    /// showed up as a malformed corner along the bottom of the skirt. Each scallop returns to
    /// the hem line exactly, so the outline pinches down to touch it at every junction, and a
    /// shape that pinches is not star-shaped: a ray from up inside the dome toward the far side
    /// of a neighbouring scallop leaves the shape at the junction between them, and the fan
    /// triangle that follows that ray spills outside the creature. It went unnoticed on the
    /// right and not the left only because the fan closes its last wedge there.</para>
    ///
    /// <para>So: the dome and its flat hem are filled as one region, which IS star-shaped from
    /// the middle of the dome; then each scallop's bulge is filled on its own, from the midpoint
    /// of the chord it hangs off. Every piece is then honestly star-shaped about the point it is
    /// fanned from, and they tile along the hem line because none of them is
    /// antialiased.</para></summary>
    private static void FillBell(LineCanvas c, LinePose q, Vector4 colour)
    {
        float lx = q.X(CX - HALF), rx = q.X(CX + HALF);
        var hemy = q.Y(HEM);
        var topy = q.Y(TOP);
        var cc = (rx - lx) * 0.285f;

        // The dome, closed straight across the hem rather than around the scallops.
        c.MoveTo(new Vector2(lx, hemy));
        c.CubicTo(
            new Vector2(lx, topy + ((hemy - topy) * 0.36f)),
            new Vector2(CX - cc, topy),
            new Vector2(CX, topy));
        c.CubicTo(
            new Vector2(CX + cc, topy),
            new Vector2(rx, topy + ((hemy - topy) * 0.36f)),
            new Vector2(rx, hemy));
        c.Fill(q.Pt(CX, TOP + (BellH * 0.55f)), colour);

        // Then the five bulges hanging below it.
        var drop = ScallopDrop * (1f + ((q.Sx - 1f) * 1.6f)) * q.Sy;
        var step = (rx - lx) / Scallops;
        var x = rx;
        for (var i = 0; i < Scallops; i++)
        {
            var x2 = x - step;
            c.MoveTo(new Vector2(x, hemy));
            c.QuadTo(new Vector2(x - (step / 2f), hemy + drop), new Vector2(x2, hemy));
            c.Fill(new Vector2(x - (step / 2f), hemy), colour);
            x = x2;
        }
    }

    /// <summary>Dome plus an even scalloped hem, exactly as the generator writes it: two cubics
    /// for the dome, then one quadratic per scallop back along the hem. The skirt flares as the
    /// bell squashes, which is the deformation doing anatomy rather than just scaling.</summary>
    private static void BellPath(LineCanvas c, LinePose q)
    {
        float lx = q.X(CX - HALF), rx = q.X(CX + HALF);
        var hemy = q.Y(HEM);
        var topy = q.Y(TOP);
        var drop = ScallopDrop * (1f + ((q.Sx - 1f) * 1.6f)) * q.Sy;
        var cc = (rx - lx) * 0.285f;

        c.MoveTo(new Vector2(lx, hemy));
        c.CubicTo(
            new Vector2(lx, topy + ((hemy - topy) * 0.36f)),
            new Vector2(CX - cc, topy),
            new Vector2(CX, topy));
        c.CubicTo(
            new Vector2(CX + cc, topy),
            new Vector2(rx, topy + ((hemy - topy) * 0.36f)),
            new Vector2(rx, hemy));

        var step = (rx - lx) / Scallops;
        var x = rx;
        for (var i = 0; i < Scallops; i++)
        {
            var x2 = x - step;
            c.QuadTo(new Vector2(x - (step / 2f), hemy + drop), new Vector2(x2, hemy));
            x = x2;
        }
    }

    /// <summary>The inner frill: a scallop echo riding above the hem.</summary>
    private static void FrillPath(LineCanvas c, LinePose q, float lift, float drop)
    {
        float lx = q.X(CX - HALF), rx = q.X(CX + HALF);
        var hemy = q.Y(HEM);
        var step = (rx - lx) / Scallops;

        c.MoveTo(new Vector2(lx, hemy - lift));
        var x = lx;
        for (var i = 0; i < Scallops; i++)
        {
            var x2 = x + step;
            c.QuadTo(new Vector2(x + (step / 2f), hemy - lift + drop), new Vector2(x2, hemy - lift));
            x = x2;
        }
    }

}
