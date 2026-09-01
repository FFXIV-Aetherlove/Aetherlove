namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using static AetherOS.PetKit.Rendering.LineArt.LineShell;

/// <summary>
/// The Grumble, drawn. Thirteenth shell, first Lightning after the Pennant, and the first body
/// on the roster that is a UNION rather than a set of parts.
///
/// <para>A transcription of <c>art-intake/element-shells/grumble-master/build_master.py</c>:
/// same names, same numbers, so the two can be diffed. <c>tools/grumblecheck.py</c> is what
/// keeps them one thing.</para>
///
/// <para><b>What it costs the engine: nothing.</b> Muffle needed two new channels because a
/// two-ball body has one mass moving relative to another; Chime needed none because a rigid
/// solid was already paid for. This one is assembled entirely out of borrowed channels:
/// <see cref="Ch.Glow"/> is the charge (the Lantern's flame), <see cref="Ch.Spike"/> is how far
/// the bolt reaches (the Puffer's spines), <see cref="Ch.Blur"/> is the flash (the Serpent's
/// rattle), <see cref="Ch.Phase"/> is the boil (the Nautilus's growth), and Sx/Sy/Dy are
/// everyone's. The contract clause the plan reserved for this shell (<c>slotScales.head =
/// 1.3</c>, never once exercised) turned out to be wrong about the design and is still
/// unexercised.</para>
///
/// <para><b>Its motion signature is the roster's oddest and the whole point of it.</b> Every
/// other shell answers <i>is this thing alive</i> by moving continuously. Chime answers by being
/// still and trembling once. This one answers by LOADING: idle gathers for six cells, holds one,
/// and spends itself in a single frame. The only reason cell 7 reads as lightning is that cell 6
/// is still.</para>
///
/// <para><b>A cumulus is its outline, and its outline is made of circles.</b> The mass is eleven
/// overlapping lobes and nothing else: no seams, no tiers, no decomposition. That is not a
/// shortcut, it is the finding the master paid two failed builds for: all the dark outlines
/// first, then all the fills, and every arc that lies inside another lobe is buried by that
/// lobe's fill. The union never needs its outline computed, which makes this the cheapest body
/// on the roster to draw and the only one with no fan-split in it at all.</para>
/// </summary>
public static class GrumbleLineArt
{
    // ---------------------------------------------------------------- geometry --
    // Lifted from build_master.py unchanged. Same names, same numbers: a port that renames
    // things is a port nobody can diff against the generator it came from.
    public const float Cell = 384f;

    public const float CX = 192f;

    /// <summary>The ground line. This shell does not touch it: its BOLT does, which is the whole
    /// of its ground relationship.</summary>
    private const float Sole = 309f;

    /// <summary>The air under the mass, <c>Sole - 266</c>. Nothing in <see cref="Draw"/> reads it;
    /// it is the number that says this creature hovers, and the check compares it.</summary>
    private const float Hover = 43f;

    /// <summary>ELEVEN OVERLAPPING LOBES, in draw order, as (offset from centre, latitude, two
    /// radii). The belly lobes are last so their fills bury everything they overlap.
    ///
    /// <para>Two are new against the concept: bumps on the anvil's WINGS (6 and 7), without
    /// which the shelf is a flying saucer, and the belly lobes are pulled in, because at the
    /// concept's spacing the shallow overlaps left the union veined with its own construction
    /// arcs.</para></summary>
    private static readonly (float Dx, float Y, float Rx, float Ry)[] Lobes =
    [
        (0f, 137f, 53.3f, 33.5f),      // 0  the crown: the brim seat, and the only part of this
                                       //    shell a hat can sit on
        (0f, 158f, 130f, 26f),         // 1  THE ANVIL: see below
        (-84f, 162f, 26f, 20f),        // 2  wing puffs: what stops the anvil being a disc, by
        (84f, 159f, 27f, 21f),         // 3  giving its tips somewhere to end
        (-43f, 135f, 21f, 12.5f),      // 4  boil bumps on the crown's shoulders
        (43f, 132f, 18.5f, 11f),       // 5
        (-104f, 146f, 20f, 12f),       // 6  boil bumps on the WINGS
        (104f, 144f, 18f, 11f),        // 7
        (0f, 204f, 64.5f, 62f),        // 8  the puff: the mass the face lives on
        (-51f, 214f, 44.5f, 40f),      // 9  belly lobes
        (51f, 211f, 46f, 42f),         // 10
    ];

    private const int Crown = 0, Anvil = 1, Puff = 8;

    /// <summary>The lobes the dark base is clipped to.</summary>
    private static readonly int[] Low = [8, 9, 10];

    /// <summary>The lobes the one light falls on.</summary>
    private static readonly int[] Lit = [0, 1, 4, 5, 6, 7];

    /// <summary>The small bumps that drift with the boil.</summary>
    private static readonly int[] Boilers = [4, 5, 6, 7];

    // THE ANVIL is lobe 1 and nothing else: no tier, no separate stroke, no seam. An added mass
    // must share the body's outline rather than sit on it, and in a union that is not a rule to
    // obey, it is the only thing that CAN happen. It is 260 across where the mass under
    // it is 205, so it SHELVES, and that overhang, flat dark thing over round bright thing, is
    // the whole thunderhead read.

    private const float NubDx = 104f, NubY = 216f, NubR = 17f;

    /// <summary>The tail seat: at the back of the belly, left of the bolt, and BURIED.
    ///
    /// <para>The latitude is derived rather than typed, because typing it got it wrong. At 250 the
    /// anchor sat 6.7 above the outline, which is 0.7 <i>inside</i> the inner edge of a 12-wide
    /// ink (effectively on the line), so a tail sprite whose origin is not exactly its own top
    /// edge showed its base hanging past the silhouette on the frames that squash. Half the ink
    /// plus <see cref="TailSink"/> now, which is 14 clear of the outline, and it moves on its own
    /// if the belly lobes or the ink ever do.</para></summary>
    private const float TailDx = -34f, TailSink = 8f;

    /// <summary>The LEFT root's authoring x, because that is the one <see cref="DrawNubs"/>
    /// takes: it mirrors the other about <see cref="CX"/> itself.</summary>
    private const float NubX = CX - NubDx;

    /// <summary>The bolt. PART OF THE SHELL rather than an effect: it is this creature's only
    /// contact with the ground line, the way Chime's longest point is. Its top is buried in the
    /// belly so it grows out of the body instead of being stuck to it.
    ///
    /// <para>Where it hangs from cost the concept two passes: beside the right-hand nub it read
    /// as a pen the cloud was holding (anything at a hand pin is HELD), and under the chin
    /// it read as a cigarette. Off the underside, right of centre, it reads as weather.</para>
    /// </summary>
    private static readonly Vector2[] Bolt =
    [
        new(-14f, 240f),
        new(50f, 234f),
        new(14f, 274f),
        new(54f, 268f),
        new(10f, Sole),
        new(30f, 282f),
        new(-10f, 288f),
    ];

    /// <summary>Above this the bolt is inside the body and must not move, or a long reach unplugs
    /// it from the mass it grows out of.</summary>
    private const float BoltRoot = 240f;

    /// <summary>Thinner than the body's, and it has to be: a shape narrower
    /// than about three ink widths is drawn entirely in outline, and at 12 this was a dark spike
    /// with no strike in it. The first mark on the roster to take a lighter ink than its body, so
    /// nobody restore it to <see cref="InkWidth"/> for consistency's sake.</summary>
    private const float BoltInk = 6f;

    /// <summary>The bolt is a zigzag, which is not star-shaped from any single point, so it fills
    /// as three CONVEX pieces that tile it: the same trade the Jelly's scallops and Chime's
    /// points make. Indices into <see cref="Bolt"/>; the two cuts are 2–6 and 3–5, which are the
    /// only diagonals that leave every piece convex through the whole reach of
    /// <see cref="Ch.Spike"/>.</summary>
    private static readonly int[][] BoltPieces =
    [
        [6, 0, 1, 2],
        [2, 3, 5, 6],
        [3, 4, 5],
    ];

    // ------------------------------------------------------------- the surface --
    // THE TONES ARE OPAQUE, and that is a finding rather than a preference. A union has no single
    // path to clip against, so every soft mark has to be clipped to the LOBE it sits on, and a
    // translucent mark clipped to two overlapping lobes is painted TWICE over the overlap. Done
    // in ink at 0.32 the way every other shell shades, the creature came out veined with the
    // circles it was built from, which is exactly the fault the union rule warns about arriving
    // through the back door.
    //
    //     On a union, tones are colours and never opacities.

    /// <summary>The dark base, as (latitude, rx and ry as fractions of the puff's). A storm is lit
    /// from above and its base is flat dark; the value split IS the element here, the way the
    /// facet bands are on Chime.
    ///
    /// <para>It sits BELOW the face and only just: the first cut had it crossing the eyes' lower
    /// half, so the pet read as standing in its own shadow. On a shell whose element is a value
    /// split, the split and the face compete for the same body and the face wins.</para></summary>
    private static readonly (float Y, float Rk, float Yk)[] Under =
    [
        (262f, 2.40f, 0.62f),
        (280f, 1.90f, 0.44f),
    ];

    /// <summary>The one light, upper left, per lit lobe.</summary>
    private const float LightD = 0.14f, LightK = 0.94f;

    /// <summary>How far the small bumps drift. The creases a real cumulus boils along, said by
    /// MOVING lobes rather than by drawing seams on them: a seam drawn on a union is a line
    /// across a shape that has no line there.</summary>
    private const float BoilAmp = 3f;

    /// <summary>The crown and the anvil's upper surface, as an authoring grey rather than as the
    /// master's hex. It is 0.72 of the way from <see cref="Base"/> to <see cref="Rim"/>, which is
    /// where <c>BASE_LIT</c> sits between <c>CLOUD</c> and <c>RIM</c> in the Levin palette.
    /// </summary>
    private const float LitTone = 219f / 255f;

    /// <summary>Step two of the dark base, one step below <see cref="Shadow"/>. The master's
    /// <c>BASE_DARKER</c> is 0.84 of its <c>BASE_DARK</c>, and 148 × 0.84 is this.</summary>
    private const float Deep = 124f / 255f;

    /// <summary>Where the charge is heading, as a value.
    ///
    /// <para><b>This is the one deliberate departure from the master and it is a palette fix.</b>
    /// The generator darkens the base by mixing toward <c>INK</c>, which is fine in a mockup
    /// where the ink and the body are the same hue by construction. In the app <c>lineColor</c>
    /// is one fixed colour in the manifest and the body is whatever palette the player picked, so
    /// mixing the two would send a red Grumble's underside muddy violet. Mixing VALUES instead
    /// keeps every tone a tint of the body, which is what <see cref="Tint"/> exists for, and it
    /// costs nothing on Levin because there the two agree.</para></summary>
    private const float InkTone = 57f / 255f;

    /// <summary>The ink weight, in authoring units: a number rather than a pixel, which is half
    /// the point of drawing rather than baking.</summary>
    public static float InkWidth { get; set; } = 12f;

    // ============================================================= the face =====
    // THE SHELL DRAWS NO MOUTH, and NO BROWS. The mouth is the engine's, on the anchor this file
    // publishes. The brows came off on the owner's note ("we don't want him to always look
    // angry"), and it is a contract point rather than a taste one: a brow painted onto the body
    // is A MOOD BAKED INTO THE VESSEL. The pet cannot stop wearing it, so a beaming Grumble
    // scowls and a napping one scowls, and the mood system quietly stops reaching the face on
    // this one shell. The grumble is in the name, the dark base and the charge.
    private const float EyeDx = 36f, EyeY = 186f, MouthY = 220f;

    private const float BlushDx = 74f, BlushY = 212f, BlushRx = 17f, BlushRy = 10f;

    /// <summary>How far ABOVE the eyes the wardrobe's <c>face</c> anchor sits, because every
    /// glasses sprite pins its own top-centre to it and hangs the lenses below. 42 on the Jelly,
    /// the Crab, the Spintop, Muffle and Chime, and 42 here.</summary>
    private const float FaceLift = 42f;

    /// <summary>This shell's face, as sixteen numbers, in the record's own field order so it
    /// copies straight out of the master's <c>RIG</c>.</summary>
    private static readonly EyeRig Rig = new(
        Dx: EyeDx, Y: EyeY, Rx: 32f, Ry: 37f,
        PupilRx: 22f, PupilRy: 27f, RingW: 10f, PupilOut: 4f,
        BigDx: 10f, BigDy: 16f, BigR: 8.6f,
        SmallDx: 8f, SmallDy: 13f, SmallR: 4.4f,
        ShutBow: 19f, LashW: 11f);

    /// <summary>Vapour, and nearly rigid anyway, which looks like a contradiction and is not.
    ///
    /// <para>The softness of this creature is in the DRAWING: eleven lobes, a boil that never
    /// stops, and an outline made of circles. The one thing its motion must not do is spread the
    /// discharge, and a spring is exactly a machine for spreading things. Cell 7 is one frame
    /// with everything in it; a slack body would carry it into cell 8 and turn the only loud
    /// moment this shell has into a wobble. Nothing here rides the trim pose either (every tone
    /// is paint and takes the body's own pose), so <c>TrimLag</c> is inert and set to the roster
    /// default.</para></summary>
    public static readonly Material Stuff = new(Springiness: 0.10f, TrimLag: 0.30f);

    // ------------------------------------------------------------------- poses --
    // build_master.py's POSES, verbatim. sx, sy, dy, charge, spike, blur, boil, eye, blush.

    /// <summary>One cell. Every channel here is one another shell already bought.
    ///
    /// <para><paramref name="charge"/> is how loaded it is, and it drives the underside darkening
    /// and the bolt together because they are one thing: a cloud about to let go is a cloud whose
    /// base has gone black. Multiplicative and 1 at rest, so it rides <see cref="Ch.Glow"/>.
    /// <paramref name="spike"/> is how far the bolt reaches about its buried root.
    /// <paramref name="blur"/> is the flash: one frame, and it LIFTS THE FILL rather than
    /// drawing a wash, which on a union is the only way to flash without seams.
    /// <paramref name="boil"/> is the cumulus creasing, cyclic and ambient, so it keeps turning
    /// through a clip that does not act it.</para></summary>
    private static Key K(float sx, float sy, float dy, float charge, float spike, float blur, float boil, EyeState eye, bool blush = false)
    {
        var c = Neutral();
        c[(int)Ch.Sx] = sx;
        c[(int)Ch.Sy] = sy;
        c[(int)Ch.Dy] = dy;
        c[(int)Ch.Glow] = charge;
        c[(int)Ch.Spike] = spike;
        c[(int)Ch.Blur] = blur;
        c[(int)Ch.Phase] = boil;
        return new Key(c, eye, blush);
    }

    private static readonly Key[] Poses =
    [
        // idle 0-8, fps 8, loop. BUILD AND DISCHARGE, and the shell's whole brief. Cells 0-5
        // gather: the charge climbs, the mass draws in and rises, the bolt pulls up into the
        // body. Cell 6 HOLDS at the top of it. Cell 7 is the snap: one frame, everything at
        // once, the only cell on this shell that is loud. Cell 8 is spent.
        K(1.000f, 1.000f, 0.0f, 1.00f, 1.00f, 0.0f, 0.00f, Open),
        K(0.994f, 1.004f, -1.5f, 1.18f, 0.94f, 0.0f, 0.11f, Open),
        K(0.988f, 1.008f, -3.0f, 1.38f, 0.88f, 0.0f, 0.22f, Open),
        K(0.983f, 1.012f, -4.2f, 1.58f, 0.82f, 0.0f, 0.33f, Open),
        K(0.979f, 1.015f, -5.0f, 1.76f, 0.78f, 0.0f, 0.44f, Squint),
        K(0.977f, 1.016f, -5.4f, 1.90f, 0.76f, 0.0f, 0.55f, Squint),
        K(0.977f, 1.016f, -5.4f, 1.96f, 0.75f, 0.0f, 0.66f, Squint),
        K(1.055f, 0.958f, 2.0f, 2.60f, 1.42f, 1.0f, 0.77f, Wide),
        K(1.014f, 0.990f, 0.4f, 1.30f, 1.08f, 0.3f, 0.88f, Open),

        // 9-11: the rest cells the blink clip is built from
        K(1.000f, 1.000f, 0.0f, 1.00f, 1.00f, 0.0f, 0.0f, Open),
        K(1.000f, 1.000f, 0.0f, 1.00f, 1.00f, 0.0f, 0.0f, Shut),
        K(1.000f, 1.000f, 0.0f, 1.00f, 1.00f, 0.0f, 0.0f, HalfShut),

        // boop 12-18, fps 14. Startled, so it goes off EARLY: the poke is a TRIGGER rather than a
        // squash, which is the one thing this creature can do that no other shell can answer a
        // touch with.
        K(1.030f, 0.972f, 1.5f, 1.20f, 1.02f, 0.1f, 0.0f, Wide),
        K(1.070f, 0.944f, 3.5f, 2.80f, 1.50f, 1.0f, 0.1f, Wide),
        K(0.960f, 1.034f, -3.0f, 2.10f, 1.20f, 0.7f, 0.2f, Squint),
        K(1.022f, 0.984f, 1.0f, 1.55f, 1.05f, 0.3f, 0.3f, Wide),
        K(0.992f, 1.006f, -1.0f, 1.28f, 0.98f, 0.1f, 0.4f, Open),
        K(1.006f, 0.996f, 0.4f, 1.12f, 1.00f, 0.0f, 0.5f, Happy, true),
        K(1.000f, 1.000f, 0.0f, 1.05f, 1.00f, 0.0f, 0.6f, Happy, true),

        // nap 19-24, fps 6, loop. It settles and goes flat: the charge drops below rest, the bolt
        // pulls almost all the way in, and the base lightens. A storm asleep is a storm that has
        // rained itself out.
        K(1.020f, 0.986f, 3.0f, 0.70f, 0.42f, 0.0f, 0.0f, Shut, true),
        K(1.026f, 0.982f, 4.0f, 0.64f, 0.36f, 0.0f, 0.1f, Shut, true),
        K(1.030f, 0.979f, 4.6f, 0.60f, 0.32f, 0.0f, 0.2f, Shut, true),
        K(1.028f, 0.980f, 4.2f, 0.62f, 0.34f, 0.0f, 0.3f, Shut, true),
        K(1.024f, 0.983f, 3.6f, 0.66f, 0.38f, 0.0f, 0.4f, Shut, true),
        K(1.020f, 0.986f, 3.0f, 0.70f, 0.42f, 0.0f, 0.5f, Shut, true),

        // hop 25-33, fps 12. It is already flying, so the hop is a SURGE rather than a jump: it
        // gathers, throws itself up, and the bolt trails behind and snaps back under it on the
        // landing.
        K(0.986f, 1.010f, 3.0f, 1.20f, 0.86f, 0.0f, 0.0f, Open),
        K(0.978f, 1.016f, 6.0f, 1.50f, 0.72f, 0.0f, 0.1f, Open),
        K(1.030f, 0.976f, -14.0f, 1.90f, 1.24f, 0.5f, 0.2f, Wide),
        K(1.040f, 0.968f, -34.0f, 1.70f, 1.36f, 0.3f, 0.3f, Wide),
        K(1.030f, 0.976f, -40.0f, 1.50f, 1.30f, 0.0f, 0.4f, Open),
        K(1.014f, 0.990f, -30.0f, 1.36f, 1.18f, 0.0f, 0.5f, Open),
        K(0.996f, 1.004f, -12.0f, 1.24f, 1.02f, 0.0f, 0.6f, Wide),
        K(1.060f, 0.952f, 3.0f, 2.20f, 1.44f, 1.0f, 0.7f, Squint),
        K(1.000f, 1.000f, 0.0f, 1.10f, 1.00f, 0.2f, 0.8f, Open),

        // 34-38: the five rest-registered eye cells every shell owes the engine. Leave them out
        // and every drowsy state clamps back to the rest cell and the pet simply stares.
        K(1.000f, 1.000f, 0.0f, 1.00f, 1.00f, 0.0f, 0.0f, ThreeQ),
        K(1.000f, 1.000f, 0.0f, 1.00f, 1.00f, 0.0f, 0.0f, HalfShut),
        K(1.000f, 1.000f, 0.0f, 1.00f, 1.00f, 0.0f, 0.0f, Quarter),
        K(1.000f, 1.000f, 0.0f, 1.00f, 1.00f, 0.0f, 0.0f, Drowsy),
        K(1.000f, 1.000f, 0.0f, 1.00f, 1.00f, 0.0f, 0.0f, Heavy),
    ];

    /// <summary>Lets this shell's ambient channels run through a clip that does not act them:
    /// here that is the boil, which has no business stopping because the pet blinked.</summary>
    public static Channels WithAmbient(Channels target, int[] frames, float beat, out bool clipDrives, out float driven)
        => LineShell.WithAmbient(target, Poses, frames, beat, out clipDrives, out driven);

    public static Channels PoseAt(int prev, int cell, int next, int after, float phase)
        => LineShell.PoseAt(Poses, prev, cell, next, after, phase);

    /// <summary>This shell's face at a moment of its own clip: the eye between two cells and the
    /// blush across them. The pose table is this shell's; the resolving is the caller's.</summary>
    public static (EyeState Eye, float Blush) FaceAt(int cell, int next, float phase)
        => (LineShell.EyeAt(Poses, cell, next, phase), LineShell.BlushAt(Poses, cell, next, phase));

    /// <summary>Channels to this shell's own transform: squash about the ANVIL'S OWN LINE, then
    /// lift.
    ///
    /// <para>Not about the ground line, and that is the difference between a thing that stands and
    /// a thing that flies. A hovering mass compressed about a floor it is not touching slides up
    /// and down the screen as it breathes; compressed about its own widest line it billows.</para>
    /// </summary>
    public static LinePose Posed(Channels c) =>
        new(c[(int)Ch.Sx], c[(int)Ch.Sy], c[(int)Ch.Dy], CX, Lobes[Anvil].Y);

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
        // `trimCh` is deliberately unread, and it is the only shell on the roster that ignores it.
        // The lagged trim pose is for TRIMMINGS: beads, coals, speckles, things that sit on a
        // creature and may arrive a beat after it. Every mark on this one is PAINT: the lit crown,
        // the dark base, the boil. Paint IS the surface, so it takes the body's own pose, which is
        // 6663b4d's finding and the Jelly's second helping of it.
        var q = Posed(ch);
        c.Begin(dl, bottomCentre, displaySize, Cell, outer, flip);

        var blur = Math.Clamp(ch[(int)Ch.Blur], 0f, 1f);
        var load = MathF.Max(0f, ch[(int)Ch.Glow] - 1f);

        // The flash LIFTS the fill rather than washing over it: on a union a translucent wash
        // doubles wherever two lobes overlap, which is the same fault the tones themselves ran
        // into. And a loaded cloud is a cloud whose base has gone black.
        var fill = Tint(body, Base + ((Rim - Base) * 0.55f * blur));
        var lit = Tint(body, LitTone + ((Rim - LitTone) * 0.55f * blur));
        var dark = Tint(body, Shadow + ((InkTone - Shadow) * MathF.Min(0.62f, load * 0.62f)));
        var darker = Tint(body, Deep + ((InkTone - Deep) * MathF.Min(0.68f, load * 0.68f)));

        // --- 1. the bolt, under everything, so the belly buries its root
        Span<Vector2> bolt = stackalloc Vector2[Bolt.Length];
        BoltAt(q, ch, bolt);
        var boltFill = Mix(Tint(accent, AccBase), Spark, 0.5f * blur);

        foreach (var piece in BoltPieces)
        {
            c.MoveTo(bolt[piece[0]]);
            for (var i = 1; i < piece.Length; i++)
            {
                c.LineTo(bolt[piece[i]]);
            }

            c.Fill(boltFill);
        }

        c.MoveTo(bolt[0]);
        for (var i = 1; i < bolt.Length; i++)
        {
            c.LineTo(bolt[i]);
        }

        c.Stroke(ink, BoltInk);

        // --- 2. ALL the outlines, and the fills that go under them. An arc that lies inside
        // another lobe is buried by that lobe's fill in step 4; an arc on the true outer boundary
        // is inside nothing and survives. That is the whole decomposition, and there isn't one.
        var drift = MathF.Sin(MathF.Tau * ch[(int)Ch.Phase]) * BoilAmp;
        for (var i = 0; i < Lobes.Length; i++)
        {
            var (at, rx, ry) = LobeAt(q, drift, i);
            c.Ellipse(at, rx, ry, fill, 40);
            c.EllipseStroke(at, rx, ry, ink, InkWidth, 40);
        }

        // --- 3. the nubs' fill, BEFORE the lobe fills, so a shoulder replaces the silhouette
        // where it sits rather than sitting on top of it
        DrawNubs(c, q, CX, NubX, NubY, NubR, InkWidth, body, ink, fill: true);

        // --- 4. ALL the fills. Every arc that is inside another lobe dies here.
        for (var i = 0; i < Lobes.Length; i++)
        {
            var (at, rx, ry) = LobeAt(q, drift, i);
            c.Ellipse(at, rx - 1f, ry - 1f, fill, 40);
        }

        // --- 5. the tones, per lobe, in OPAQUE colour. The dark base is one band spanning the
        // whole creature and shared out across the three low lobes, which is why it needs
        // LineCanvas.EllipseAnd rather than EllipseIn: its centre is outside five of the six
        // lobes it is painted on, and a ray-cast from a centre that is outside paints nothing.
        var puff = LobeAt(q, drift, Puff);
        foreach (var i in Low)
        {
            var (at, rx, ry) = LobeAt(q, drift, i);
            for (var b = 0; b < Under.Length; b++)
            {
                var (y, rk, yk) = Under[b];
                c.EllipseAnd(
                    new Vector2(puff.At.X, q.Y(y)), puff.Rx * rk, puff.Ry * yk,
                    at, rx - 1f, ry - 1f,
                    b == 0 ? dark : darker);
            }
        }

        foreach (var i in Lit)
        {
            var (at, rx, ry) = LobeAt(q, drift, i);
            c.EllipseIn(
                at - new Vector2(rx * LightD, ry * LightD), rx * LightK, ry * LightK,
                at, rx - 1f, ry - 1f, lit);
        }

        // --- 6. the nubs' outer arc, then the face. A full ring crosses the body outline and the
        // two read as a lens where an arc reads as a shoulder.
        //
        // A FIXED HALF CIRCLE, and this is the one place the master turned out to be right for a
        // reason it never wrote down. Muffle and Chime both SOLVE where their nub ink crosses the
        // silhouette, because their nubs sit on the body and a fixed sweep would end in mid air.
        // Solving it here was tried and is worse: this nub sits 104 out where the union is only
        // 97 wide, so it is mostly OUTSIDE the creature, and the solver dutifully swept nearly
        // the whole circle. Which is a ring, and a ring is a button. **The rule the two shells
        // share is not "solve the crossings", it is "draw only the arc that is not the body" -
        // and on a nub that is proud of the flank, the outer half IS that arc.**
        DrawNubs(c, q, CX, NubX, NubY, NubR, InkWidth, body, ink, fill: false);

        if (blush > 0f)
        {
            for (var side = -1; side <= 1; side += 2)
            {
                c.Ellipse(
                    q.Pt(CX + (side * BlushDx), BlushY),
                    BlushRx * q.Ex, BlushRy * q.Ey, LineShell.BlushTint(blush));
            }
        }

        DrawEyes(
            c, Rig, eye,
            side => q.Pt(CX + (side * Rig.Dx), EyeY),
            q.Ex, q.Ey, eyeTint, ink);

        // NO MOUTH and NO BROWS are drawn here. See the note at MouthY: the engine draws the
        // mouth, on the anchor this shell publishes, and a shell that draws its own gives the pet
        // two.
    }

    /// <summary>One lobe under a pose. The boilers ride the drift, which is how a cumulus creases
    /// without anything being drawn on it.</summary>
    private static (Vector2 At, float Rx, float Ry) LobeAt(LinePose q, float drift, int i)
    {
        var (dx, y, rx, ry) = Lobes[i];
        if (Array.IndexOf(Boilers, i) >= 0)
        {
            y += drift;
        }

        return (q.Pt(CX + dx, y), rx * q.Sx, ry * q.Sy);
    }

    /// <summary>The bolt under a pose. <see cref="Ch.Spike"/> scales its reach about the buried
    /// root, so the part inside the body never moves and it cannot unplug, and a longer bolt is
    /// a wider one, or a big reach draws a wire.</summary>
    private static void BoltAt(LinePose q, Channels ch, Span<Vector2> into)
    {
        // Clamped at zero because Spike is additive and free to overshoot on the spline; a
        // negative reach would fold the strike back up through the belly.
        var spike = MathF.Max(0f, ch[(int)Ch.Spike]);
        var k = 1f + ((spike - 1f) * 0.35f);
        for (var i = 0; i < Bolt.Length; i++)
        {
            var y = Bolt[i].Y > BoltRoot
                ? BoltRoot + ((Bolt[i].Y - BoltRoot) * spike)
                : Bolt[i].Y;
            into[i] = q.Pt(CX + (Bolt[i].X * k), y);
        }
    }

    /// <summary>How far down the union reaches at one offset from centre: the mirror of the
    /// generator's <c>half_width</c>, and a MAX over the lobes for the same reason: on a union no
    /// single circle knows where the creature stops.</summary>
    private static float BottomAt(float dx)
    {
        var y = float.MinValue;
        foreach (var (lx, cy, rx, ry) in Lobes)
        {
            var d = MathF.Abs(dx - lx) / rx;
            if (d < 1f)
            {
                y = MathF.Max(y, cy + (ry * MathF.Sqrt(1f - (d * d))));
            }
        }

        return y;
    }

    private static Vector4 Mix(Vector4 a, Vector4 b, float t)
    {
        var u = Math.Clamp(t, 0f, 1f);
        return new Vector4(
            a.X + ((b.X - a.X) * u),
            a.Y + ((b.Y - a.Y) * u),
            a.Z + ((b.Z - a.Z) * u),
            a.W);
    }

    /// <summary>The shell's anchors in authoring space, neutral pose: the same table
    /// <c>build_master.py</c>'s <c>anchors_for</c> bakes per cell.
    ///
    /// <para><c>head</c> is deliberately not the apex: it is where a hat BRIM has to sit, which on
    /// this crown is a little way into the dome. The ears ride the crown's own boil bumps, the
    /// only place both high and wide, and the tail seats at the back of the belly clear of the
    /// bolt.</para></summary>
    public static Vector2 Anchor0(string name) => name switch
    {
        "head" => new Vector2(CX, Lobes[Crown].Y - (Lobes[Crown].Ry * 0.42f)),
        "face" => new Vector2(CX, EyeY - FaceLift),
        "body" => new Vector2(CX, Lobes[Puff].Y),
        "handL" => new Vector2(NubX, NubY),
        "handR" => new Vector2((2f * CX) - NubX, NubY),
        "mouth" => new Vector2(CX, MouthY),
        "earL" => new Vector2(CX + Lobes[4].Dx, Lobes[4].Y + 6f),
        "earR" => new Vector2(CX + Lobes[5].Dx, Lobes[5].Y + 6f),
        "tail" => new Vector2(CX + TailDx, BottomAt(TailDx) - (InkWidth / 2f) - TailSink),
        _ => new Vector2(CX, Lobes[Puff].Y),
    };

    /// <summary>A worn pin, moved the way this body moves it.
    ///
    /// <para>The position is the caller's (the manifest's rest-cell anchor, where the wardrobe
    /// was tuned) and this decides only the transform. <b>Every pin but the hands takes the same
    /// one</b>, and on this shell that is not laziness: a union is a single mass, the crown and
    /// the puff are the same body, and the generator's own <c>anchors_for</c> asks <c>pt</c> for
    /// all nine. Muffle needs a second transform because its head is a different ball; there is
    /// no second ball here.</para>
    ///
    /// <para>The hands stay the exception, as on every shell: they attach to a nub this file
    /// draws, so the file knows where it is better than any table does.</para></summary>
    public static Vector2 Pin(string name, PinKind kind, Vector2 rest, Channels c)
    {
        var q = Posed(c);
        if (kind == PinKind.Hand)
        {
            var a = Anchor0(name);
            return q.Pt(a.X, a.Y);
        }

        return q.Pt(rest.X, rest.Y);
    }
}
