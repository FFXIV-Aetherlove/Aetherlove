namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using static AetherOS.PetKit.Rendering.LineArt.LineShell;

/// <summary>
/// The Smoulder, drawn. Fourteenth shell, Fire's second, and the one that pays the conversion's
/// own bill.
///
/// <para>A transcription of <c>art-intake/element-shells/smoulder-master/build_master.py</c>:
/// same names, same numbers. <c>tools/smouldercheck.py</c> is what keeps them one thing.</para>
///
/// <para><b>THE ROSTER'S ONLY INVERTED-VALUE SHELL: a dark body under a lifted ink</b>, where
/// every other shell is a bright body under a dark one. That was flatly impossible on a sheet
/// and the reason is arithmetic rather than effort: a palette tint MULTIPLIES,
/// and the outline was a greyscale layer taking the same multiply, so an outline could never sit
/// above its own body's value. Drawn, <c>lineColor</c> is a literal colour that does not tint at
/// all. This shell is the one that proves the conversion bought something no amount of sheet work
/// could have sold at any price.</para>
///
/// <para><b>What it costs the engine: nothing</b>, and it is the cheapest body on the roster to
/// draw. One closed path, no union, no fan-split, no convex decomposition; <see cref="Ch.Glow"/>
/// is the Lantern's flame and Sx/Sy/Dy are everyone's. Every primitive it wants was already on
/// <see cref="LineCanvas"/>, including the two the Pennant and the Wisp bought
/// (<see cref="LineCanvas.BandEdges"/> for a knocked-off flat, <see cref="LineCanvas.FillBandIn"/>
/// for a fissure that has to stop at the rim).</para>
///
/// <para><b>Its motion signature is WEIGHT.</b> The smallest breath on the roster (two percent of
/// squash and no lift at all), because a heavy thing at rest does almost nothing. What is alive is
/// the CRACK: it swells and banks through the idle, spikes when the pet is booped, and drops to a
/// quarter on the nap. A coal asleep is a coal gone to embers, and it is the clearest reading of
/// care anywhere on the roster because the shell's own light is what dims.</para>
/// </summary>
public static class SmoulderLineArt
{
    // ---------------------------------------------------------------- geometry --
    // Lifted from build_master.py unchanged. Same names, same numbers: a port that renames
    // things is a port nobody can diff against the generator it came from.
    public const float Cell = 384f;

    public const float CX = 192f;

    /// <summary>The ground line, and the pivot everything squashes about. A coal SITS, so there
    /// is no pivot argument to have: the thing is on the floor.</summary>
    private const float Sole = 309f;

    /// <summary>200 across: the Jelly's width exactly, and the roster's middle. Grumble is 260
    /// and Muffle 180.</summary>
    private const float HW = 100f;

    /// <summary>The concept's own 138:118 aspect, to a tenth.</summary>
    private const float HH = 86f;

    private const float EggCy = Sole - HH;

    /// <summary>THE TRUEFORM, PARAMETRIC: <c>silhouette-study/draw_morphs.egg</c>, the curve
    /// every morph in that study starts from, as fractions of (<see cref="HW"/>,
    /// <see cref="HH"/>). Start point then four cubics.
    ///
    /// <para><b>Registered on sole 309 and not on the Wisp's 373, and that is a decision.</b> The
    /// trueform IS the Wisp, so "every landmark stays where it already was" invites taking
    /// wispv2's own numbers, and that would be wrong. wispv2 sits about sixty units lower in its
    /// cell than anything else on the roster (its mouth anchor is y 318 where the Jelly's is 240
    /// and Grumble's is 220) because its sheet was registered that way and the conversion read the
    /// sheet back rather than re-seating it. The claim is about the SHAPE and about the landmarks
    /// being centred; taken literally against a recovered registration it would put this shell on
    /// a different floor from the three it stands beside.</para></summary>
    private static readonly Vector2[] Egg =
    [
        new(0f, -1f),
        new(-0.60f, -0.82f), new(-1f, -0.32f), new(-1f, 0.26f),
        new(-1f, 0.74f), new(-0.55f, 1f), new(0f, 1f),
        new(0.55f, 1f), new(1f, 0.74f), new(1f, 0.26f),
        new(1f, -0.32f), new(0.60f, -0.82f), new(0f, -1f),
    ];

    /// <summary>The silhouette sampled once, in fractions. Forty steps a cubic, which is what the
    /// generator samples, so the two agree point for point rather than approximately.</summary>
    private static readonly Vector2[] Outline = BuildOutline();

    /// <summary>Lighter than the roster's 12, because a LIFTED ink at 13 was the loudest thing on
    /// the creature. See <see cref="Ink"/>.</summary>
    public static float InkWidth { get; set; } = 11f;

    /// <summary>THE FACET: the fix for the first of the concept's three failures. A dark body
    /// with a dark ink has no eye outline, so the first pass put a pale disc behind each eye and
    /// it read, immediately and unarguably, as swimming goggles. The answer a coal was always
    /// going to hand over: a coal is faceted, so give the face its own facet. Not a rounded patch,
    /// an ANGULAR plane knocked flat the way a real lump breaks, so it reads as geology rather
    /// than as makeup.</summary>
    private static readonly Vector2[] Facet =
    [
        new(-0.478f, -0.220f), new(-0.304f, -0.695f), new(0.014f, -0.831f), new(0.391f, -0.661f),
        new(0.522f, -0.220f), new(0.391f, 0.288f), new(0.014f, 0.525f), new(-0.377f, 0.271f),
    ];

    /// <summary>THE FLATS A LUMP BREAKS ALONG, as spans OF THE OUTLINE rather than as free shapes
    /// laid over it: (u0, u1, depth). The outer edge IS the silhouette; the inner edge is that
    /// edge pulled toward the centre by a depth that swells to the middle of the span and returns
    /// to nothing at both ends, which is what a knocked-off face looks like, and why a real one
    /// has no visible boundary at the rim.
    ///
    /// <para><b>The concept authored them as free paths and this could not take them.</b> They
    /// reached past the silhouette by design, on the assumption of a clip: exact in SVG.
    /// <see cref="LineCanvas.FillInPoly"/> clips a fill by pulling its boundary back toward the
    /// fill's own CENTROID, and one plane's centroid is outside the egg, so it collapsed to
    /// nothing rather than being trimmed. Built off the outline it cannot leave the body at all
    /// and needs no clip in either language.</para>
    ///
    /// <para>u runs anticlockwise from the crown: 0–.25 down the left flank, .25–.5 across the
    /// base, .5–.75 up from the base on the right, .75–1 back to the crown.</para></summary>
    private static readonly (float U0, float U1, float Depth)[] Planes =
    [
        (0.030f, 0.250f, 0.30f),
        (0.585f, 0.730f, 0.26f),
        (0.800f, 0.955f, 0.22f),
    ];

    /// <summary>TWO TREES, rooted apart and leaning opposite ways, and both of them CROSS the
    /// form. Every word of that is one of the concept's failures.
    ///
    /// <para><b>The net.</b> Drawn as a lattice they enclosed cells, and a closed cell reads as a
    /// net thrown over a rock rather than as light coming out of one. Cracks in a real thing
    /// BRANCH and never close. <b>The halo.</b> Routed round the rim to keep them off the face,
    /// they came back as an outline (a bright line following an edge IS an edge) and the coal
    /// glowed at its rim like an eclipse. A crack has to cross the form to read as going INTO
    /// it.</para>
    ///
    /// <para><b>Every station sits ON the egg</b>, and seven of them did not until
    /// <c>smouldercheck</c> said so. The concept authored them past the rim on the assumption of
    /// a clip: exact in SVG. <see cref="LineCanvas.FillBandIn"/> trims a band's WIDTH where it
    /// leaves the body, by pulling each edge point back toward its own point on the CENTRELINE,
    /// so a station whose centreline is outside collapses instead of being trimmed. Pulled to
    /// 0.94 of the egg's half-width at their own latitude, which also keeps the hairline clear of
    /// the ink.</para></summary>
    private static readonly (Vector2[] Path, float Width, float Bright)[] Cracks =
    [
        ([new(-0.145f, 0.898f), new(-0.507f, 0.678f), new(-0.710f, 0.288f), new(-0.638f, -0.186f), new(-0.681f, -0.542f)], 12f, 0.95f),
        ([new(-0.710f, 0.288f), new(-0.937f, 0.153f)], 7f, 0.55f),
        ([new(-0.638f, -0.186f), new(-0.435f, -0.458f), new(-0.350f, -0.847f)], 8f, 0.70f),
        ([new(-0.681f, -0.542f), new(-0.314f, -0.880f)], 6f, 0.48f),
        ([new(-0.435f, -0.458f), new(-0.217f, -0.695f)], 5f, 0.40f),
        ([new(0.232f, 0.949f), new(0.536f, 0.712f), new(0.638f, 0.288f), new(0.478f, -0.136f), new(0.667f, -0.542f)], 12f, 0.95f),
        ([new(0.638f, 0.288f), new(0.900f, 0.153f), new(0.872f, -0.186f)], 8f, 0.68f),
        ([new(0.478f, -0.136f), new(0.290f, -0.424f), new(0.362f, -0.797f)], 8f, 0.66f),
        ([new(0.667f, -0.542f), new(0.385f, -0.814f)], 6f, 0.50f),
        ([new(0.362f, -0.797f), new(0.163f, -0.960f)], 5f, 0.40f),
    ];

    /// <summary>How much wider the dim bloom is than its hairline. A wide dim bloom under a thin
    /// bright line is how light out of a gap actually behaves, and it costs one extra strip.
    /// </summary>
    private const float BloomK = 3.6f, BloomOp = 0.075f;

    /// <summary>Thinner than the concept's 0.6, because a HAIRLINE is what says the rock is split
    /// rather than decorated.</summary>
    private const float HairK = 0.5f;

    /// <summary>A short dim seam on the facet, so the face plane is not the one flat area on a
    /// body made of fractures.</summary>
    private static readonly Vector2[] Seam =
    [
        new(-0.304f, -0.407f), new(-0.145f, -0.661f), new(0.043f, -0.712f),
    ];

    private const float SeamW = 6f, SeamOp = 0.22f;

    /// <summary>THE CATCH: a run down the upper-left break, on <c>ElementFx.KeyLight</c>'s own
    /// side. A lump this dark with no lit edge reads as a hole in the picture; one lit edge says
    /// "solid" in a single stroke.</summary>
    private static readonly Vector2[] Catch =
    [
        new(-0.700f, -0.300f), new(-0.560f, -0.640f), new(-0.240f, -0.860f),
    ];

    private const float CatchW = 7f;

    // **THE RIBBON IS PARKED**, on the owner's eye 2026-08-27: a single line standing on a lump
    // is the shape of a stalk however it is shaded, and three passes of paling, tapering and
    // swaying it never stopped it reading as hair. The failure is the FORM rather than the
    // rendering: a ribbon is one-dimensional and smoke is a volume. The generator keeps its
    // numbers behind a `SMOKE_ON` switch because the root position and the sway weighting are the
    // parts that were paid for; whatever replaces it will want the same anchor. Nothing here
    // draws it, and `Ch.Phase` is still authored on every cell so it is wired the day it returns.

    private const float NubDx = 101f, NubY = 251f, NubR = 16f;

    private const float NubX = CX - NubDx;

    /// <summary>The tail seat: at the back of the base and BURIED, the Grumble's rule. Derived
    /// rather than typed, so it follows the egg and the ink if either moves.</summary>
    private const float TailDx = -30f, TailSink = 8f;

    // ------------------------------------------------------------- the surface --

    /// <summary>The lit crust. The roster's <see cref="Base"/> is 0.749; this one number is the
    /// whole shell.
    ///
    /// <para><b>The inversion is a VALUE, not a colour.</b> The app fills with
    /// <c>Tint(body, v)</c> (the palette's own body colour times a number), so a coal is not a
    /// grey hex, it is a low v. That is what keeps a Smoulder on Dawn a cold dark blue-grey lump
    /// and one on Ember a dark ember-brown one, instead of every player's coal being the same
    /// charcoal.</para>
    ///
    /// <para><b>The whole ladder came down when the ink learned to tint</b>, and that is the cost
    /// of tinting rather than a tuning nudge. A literal ink separated from the body on two axes at
    /// once: lighter AND a different hue, a warm grey on a brown lump. A tint keeps the hue by
    /// construction, so VALUE is all that is left, and at the old numbers the creature came out as
    /// one flat blob on Meadow and Lagoon with its eyes very nearly gone. The old ink stood 2.37×
    /// off the crust in luminance where 0.50 against 0.29 is 1.72×. Rather than push the ink up
    /// (the goggles failure waiting), the body went down to buy the ratio back, and
    /// <see cref="FacetV"/> went with it so the eye rings have a darker plane to sit on.</para>
    /// </summary>
    private const float Coal = 0.220f;

    /// <summary>The knocked-off flats.</summary>
    private const float PlaneV = 0.105f;

    /// <summary>The face plane, and the only value on the body ABOVE the crust, which is what
    /// makes a face out of a rock.</summary>
    private const float FacetV = 0.330f;

    private const float CrustLit = 0.620f;

    /// <summary>How far the accent is carried toward white before anything else happens to it.
    ///
    /// <para><b>A crack is LIGHT, not paint.</b> The concept draws its fissures near white because
    /// it is authored in greyscale; ported straight into the accent at full chroma, the same
    /// paths at the same widths came out as GOLD VEINING: a kintsugi bowl rather than a rock with
    /// light in it. That is the "net" failure arriving through the colour instead of through the
    /// routing. Enough pale that the player's palette still decides whether the light is warm or
    /// cold; not enough that a fissure reads as something the rock is made of.</para></summary>
    private const float CrackPale = 0.55f;

    /// <summary>THE LIFTED INK, as a VALUE rather than a hex. Nothing here draws with it (the
    /// app resolves the outline colour and hands it in as <c>ink</c>) but the number lives here
    /// because it is part of this shell's ladder and <c>smouldercheck</c> asserts the direction
    /// against it.
    ///
    /// <para><b>Lifted is not the same as bright.</b> The first build set a literal #8C7F79 to
    /// satisfy the concept's note that the outline must read on the dark planes, and that made the
    /// ink the lightest thing on the creature, so <see cref="DrawEyes"/>, which rings every eye
    /// in <c>ink</c>, drew two pale hoops on a dark face and the goggles came back for a fourth
    /// time, out of a file that had never heard of coal. The number the ink wants is the smallest
    /// that still reads against the PLANES, because that is the only thing it has to be seen
    /// against.</para>
    ///
    /// <para><b>And it is a value because a literal does not take the palette.</b>
    /// <c>lineColor</c> is the same hex whatever the player picked, which passed unnoticed for
    /// thirteen shells because a dark blue-slate outline reads as "dark of the body" on almost
    /// anything. A LIFTED grey does not: on a red pet it is a grey line drawn round a red pet. The
    /// manifest carries <c>lineValue</c> and <c>AtlasManifest.InkFor</c> resolves it against the
    /// body tint, which is what a sheet's baked outline always did. The inversion survives
    /// intact: 0.50 is above the crust's 0.29 whatever the palette does to both.</para>
    ///
    /// <para>It sits between the facet (0.33) and the catch (0.62) on purpose: above the face
    /// plane so the eye rings still read, below the catch so the lit break stays the lightest
    /// thing on the crust.</para></summary>
    private const float InkV = 0.500f;

    // ============================================================= the face =====
    // THE SHELL DRAWS NO MOUTH. The engine draws it, on the anchor this file publishes.
    private const float EyeDx = 37f, EyeY = 213f, MouthY = 258f;

    private const float BlushDx = 68f, BlushY = 242f, BlushRx = 14f, BlushRy = 9f;

    /// <summary>42 above the eyes, because every glasses sprite pins its own top-centre there and
    /// hangs the lenses below. Jelly −42, Crab −42, Spintop −42, Muffle −42, Chime −42,
    /// Grumble −42.</summary>
    private const float FaceLift = 42f;

    /// <summary>The Jelly's rig, and deliberately: it is the shipped shell of exactly this width
    /// with its face on the body itself rather than on a head, so its eye is the one already
    /// judged against a 200-wide egg. The half-span comes out at <c>Dx + Rx = 69</c>, which is the
    /// Jelly's 69, so the wardrobe needed no correction and <c>fit</c> ships empty.</summary>
    private static readonly EyeRig Rig = new(
        Dx: EyeDx, Y: EyeY, Rx: 32f, Ry: 39f,
        PupilRx: 21.5f, PupilRy: 28.5f, RingW: 10f, PupilOut: 4f,
        BigDx: 9.5f, BigDy: 16f, BigR: 8.2f,
        SmallDx: 8f, SmallDy: 13f, SmallR: 4f,
        ShutBow: 20f, LashW: 11f);

    /// <summary>Stone. The stiffest material on the roster, and the reason is the performance
    /// rather than the substance: this shell's boop is a squash it TAKES rather than bounces off,
    /// and a spring is a machine for bouncing. The trimmings lag at the roster default and it is
    /// inert: every mark on this body is paint and takes the body's own pose.</summary>
    public static readonly Material Stuff = new(Springiness: 0.06f, TrimLag: 0.30f);

    // ------------------------------------------------------------------- poses --
    // build_master.py's POSES, verbatim. sx, sy, dy, crack, smoke, eye, blush.

    /// <summary>One cell. <paramref name="crack"/> is how hot the fissures are: multiplicative
    /// and 1 at rest, so it rides <see cref="Ch.Glow"/>, the Lantern's own channel, and it is the
    /// whole performance. <paramref name="smoke"/> is the parked ribbon's travel: cyclic, ambient,
    /// authored and read by nothing until the ribbon comes back.</summary>
    private static Key K(float sx, float sy, float dy, float crack, float smoke, EyeState eye, bool blush = false)
    {
        var c = Neutral();
        c[(int)Ch.Sx] = sx;
        c[(int)Ch.Sy] = sy;
        c[(int)Ch.Dy] = dy;
        c[(int)Ch.Glow] = crack;
        c[(int)Ch.Phase] = smoke;
        return new Key(c, eye, blush);
    }

    private static readonly Key[] Poses =
    [
        // idle 0-7, fps 6, loop. THE SMALLEST BREATH ON THE ROSTER and that is the brief: two
        // percent of squash, no lift at all, and the life is entirely in the crack. Chime answers
        // "is this alive" by trembling once; this one answers by GLOWING, and it is the only
        // shell here that can be completely still.
        K(1.000f, 1.000f, 0.0f, 1.00f, 0.000f, Open),
        K(1.004f, 0.997f, 0.0f, 1.12f, 0.125f, Open),
        K(1.008f, 0.994f, 0.0f, 1.24f, 0.250f, Open),
        K(1.010f, 0.992f, 0.0f, 1.30f, 0.375f, Open),
        K(1.008f, 0.994f, 0.0f, 1.26f, 0.500f, Open),
        K(1.004f, 0.997f, 0.0f, 1.14f, 0.625f, Open),
        K(1.000f, 1.000f, 0.0f, 1.02f, 0.750f, Open),
        K(0.998f, 1.002f, 0.0f, 0.94f, 0.875f, Open),

        // 8-10: the rest cells the blink clip is built from
        K(1.000f, 1.000f, 0.0f, 1.00f, 0.000f, Open),
        K(1.000f, 1.000f, 0.0f, 1.00f, 0.000f, Shut),
        K(1.000f, 1.000f, 0.0f, 1.00f, 0.000f, HalfShut),

        // boop 11-16, fps 14. WEIGHT: it takes the poke, it does not bounce off it. The squash is
        // deep and the rebound barely passes rest (1.02, where the Jelly goes to 1.10), because a
        // solid settles hard. What it DOES do is flare: a poked fire throws light.
        K(1.060f, 0.955f, 1.0f, 1.60f, 0.000f, Wide),
        K(1.160f, 0.880f, 4.0f, 2.40f, 0.050f, Wide),
        K(1.090f, 0.935f, 2.0f, 2.00f, 0.100f, Squint),
        K(1.020f, 0.986f, 0.0f, 1.60f, 0.150f, Wide),
        K(1.002f, 0.999f, 0.0f, 1.30f, 0.200f, Happy, true),
        K(1.000f, 1.000f, 0.0f, 1.14f, 0.250f, Happy, true),

        // nap 17-22, fps 5, loop. BANKED. The crack drops to a quarter and the body settles two
        // percent and stays there. A coal asleep is a coal gone to embers.
        K(1.024f, 0.982f, 2.0f, 0.34f, 0.000f, Shut, true),
        K(1.028f, 0.979f, 2.6f, 0.30f, 0.100f, Shut, true),
        K(1.030f, 0.977f, 3.0f, 0.26f, 0.200f, Shut, true),
        K(1.028f, 0.979f, 2.6f, 0.28f, 0.300f, Shut, true),
        K(1.024f, 0.982f, 2.0f, 0.32f, 0.400f, Shut, true),
        K(1.020f, 0.985f, 1.6f, 0.36f, 0.500f, Shut, true),

        // hop 23-32, fps 11. A HEAVY hop: it gathers to leave the ground at all, does not go far
        // (−26 where the Jelly goes −40), and LANDS: one cell of hard squash with the crack
        // flaring on the impact, the way a struck coal throws sparks. No float at the top.
        K(1.060f, 0.950f, 3.0f, 1.10f, 0.000f, Open),
        K(1.110f, 0.905f, 6.0f, 1.40f, 0.100f, Squint),
        K(0.955f, 1.060f, -12.0f, 1.80f, 0.200f, Wide),
        K(0.940f, 1.080f, -24.0f, 1.60f, 0.300f, Wide),
        K(0.950f, 1.062f, -26.0f, 1.44f, 0.400f, Open),
        K(0.972f, 1.032f, -18.0f, 1.32f, 0.500f, Open),
        K(0.994f, 1.006f, -6.0f, 1.22f, 0.600f, Wide),
        K(1.140f, 0.890f, 5.0f, 2.20f, 0.700f, Squint),
        K(1.030f, 0.975f, 1.0f, 1.40f, 0.800f, Open),
        K(1.000f, 1.000f, 0.0f, 1.10f, 0.900f, Open),

        // 33-37: the five rest-registered eye cells every shell owes the engine. Leave them out
        // and every drowsy state clamps back to the rest cell and the pet simply stares.
        K(1.000f, 1.000f, 0.0f, 1.00f, 0.000f, ThreeQ),
        K(1.000f, 1.000f, 0.0f, 1.00f, 0.000f, HalfShut),
        K(1.000f, 1.000f, 0.0f, 1.00f, 0.000f, Quarter),
        K(1.000f, 1.000f, 0.0f, 1.00f, 0.000f, Drowsy),
        K(1.000f, 1.000f, 0.0f, 1.00f, 0.000f, Heavy),
    ];

    public static Channels WithAmbient(Channels target, int[] frames, float beat, out bool clipDrives, out float driven)
        => LineShell.WithAmbient(target, Poses, frames, beat, out clipDrives, out driven);

    public static Channels PoseAt(int prev, int cell, int next, int after, float phase)
        => LineShell.PoseAt(Poses, prev, cell, next, after, phase);

    /// <summary>This shell's face at a moment of its own clip: the eye between two cells and the
    /// blush across them. The pose table is this shell's; the resolving is the caller's.</summary>
    public static (EyeState Eye, float Blush) FaceAt(int cell, int next, float phase)
        => (LineShell.EyeAt(Poses, cell, next, phase), LineShell.BlushAt(Poses, cell, next, phase));

    /// <summary>Channels to this shell's own transform: squash about the ground line, then lift.
    /// A coal sits, so there is no pivot argument to have.</summary>
    public static LinePose Posed(Channels c) =>
        new(c[(int)Ch.Sx], c[(int)Ch.Sy], c[(int)Ch.Dy], CX, Sole);

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
        // `trimCh` is deliberately unread. The lagged trim pose is for TRIMMINGS: things that sit
        // ON a creature and may arrive a beat after it. Every mark here is PAINT: the planes, the
        // facet, the catch, the fissures. Paint IS the surface, so it takes the body's own pose.
        var q = Posed(ch);
        c.Begin(dl, bottomCentre, displaySize, Cell, outer, flip);

        // A hot coal lifts its whole crust, not just its fissures: a flare that reaches only the
        // cracks reads as a decal on a cold rock.
        var load = MathF.Max(0f, ch[(int)Ch.Glow] - 1f);
        var lift = MathF.Min(0.22f, load * 0.16f);
        var coal = Tint(body, Coal + lift);
        var plane = Tint(body, PlaneV + (lift * 0.6f));
        var facet = Tint(body, FacetV + lift);
        var crust = Tint(body, CrustLit + lift);
        var crackC = Mix(Tint(accent, AccRim), Spark, MathF.Min(0.92f, CrackPale + (load * 0.30f)));

        // --- 1. the nubs' fill, before the body, so a shoulder REPLACES the silhouette where it
        // sits rather than sitting on top of it
        DrawNubs(c, q, CX, NubX, NubY, NubR, InkWidth, body, ink, fill: true);

        // --- 2. the egg. One closed path, filled as a fan from its own centre: an egg is convex,
        // so the centre sees every point on the boundary and there is nothing to decompose.
        EggPath(c, q);
        var hull = c.Capture();
        c.Fill(F(q, 0f, 0f), coal);

        // --- 3. the knocked-off flats, each a strip between the egg's own edge and that edge
        // pulled inward. No clip: built off the outline, a plane cannot leave the body.
        foreach (var (u0, u1, depth) in Planes)
        {
            var (outerEdge, innerEdge) = PlaneEdges(q, u0, u1, depth);
            c.BandEdges(outerEdge, innerEdge);
            c.FillBand(plane);
        }

        // --- 4. the face plane, then the catch
        c.MoveTo(F(q, Facet[0].X, Facet[0].Y));
        for (var i = 1; i < Facet.Length; i++)
        {
            c.LineTo(F(q, Facet[i].X, Facet[i].Y));
        }

        c.Fill(facet);

        c.BandPath(Run(q, Catch), CatchW, CatchW * 0.55f);
        c.FillBand(crust);

        // --- 5. the fissures: a wide dim bloom under a thin bright line, both CUT to the egg.
        // Filled as strips rather than as fans, because a fan from a long thin curved shape's
        // centroid folds it: FillBandIn's own note, which the Nautilus paid for.
        foreach (var (path, w, _) in Cracks)
        {
            c.BandPath(Run(q, path), w * BloomK, w * BloomK * 0.7f);
            c.FillBandIn(hull, crackC with { W = MathF.Min(0.42f, BloomOp * ch[(int)Ch.Glow]) });
        }

        foreach (var (path, w, bright) in Cracks)
        {
            c.BandPath(Run(q, path), w * HairK, w * HairK * 0.55f);
            c.FillBandIn(hull, crackC with { W = MathF.Min(1f, bright * 0.8f * ch[(int)Ch.Glow]) });
        }

        c.BandPath(Run(q, Seam), SeamW, SeamW * 0.6f);
        c.FillBandIn(hull, crackC with { W = MathF.Min(0.5f, SeamOp * ch[(int)Ch.Glow]) });

        // --- 6. THE INK, over everything and in one closed stroke: the outline is what closes the
        // shape, so nothing soft may be drawn after it.
        EggPath(c, q);
        c.Stroke(ink, InkWidth);

        // The nubs' outer arc. A FIXED HALF CIRCLE, the Grumble's rule: this nub sits at 101
        // where the egg is 99.9 wide at its own latitude, so it is PROUD of the flank and the
        // outer half already is the arc that is not the body. Solving the crossings on a proud nub
        // sweeps almost a full ring, and a ring is a button.
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

        // NO MOUTH is drawn here. See the note at MouthY: the engine draws it, on the anchor this
        // shell publishes, and a shell that draws its own simply gives the pet two.
    }

    /// <summary>An egg-fraction point under a pose. Every mark on this shell is authored in these,
    /// so a mark cannot slide off the body it is painted on.</summary>
    private static Vector2 F(LinePose q, float fx, float fy) =>
        q.Pt(CX + (fx * HW), EggCy + (fy * HH));

    private static List<Vector2> Run(LinePose q, Vector2[] path)
    {
        var outp = new List<Vector2>(path.Length);
        foreach (var p in path)
        {
            outp.Add(F(q, p.X, p.Y));
        }

        return outp;
    }

    private static void EggPath(LineCanvas c, LinePose q)
    {
        c.MoveTo(F(q, Outline[0].X, Outline[0].Y));
        for (var i = 1; i < Outline.Length; i++)
        {
            c.LineTo(F(q, Outline[i].X, Outline[i].Y));
        }
    }

    /// <summary>One knocked-off flat, as its two edges: the egg's own edge over a span, and that
    /// edge pulled inward by a depth that swells to the middle and returns to nothing at each
    /// end.</summary>
    private static (List<Vector2> Outer, List<Vector2> Inner) PlaneEdges(LinePose q, float u0, float u1, float depth)
    {
        var n = Outline.Length;
        int i0 = (int)(u0 * n), i1 = (int)(u1 * n);
        var span = Math.Max(1, i1 - i0);
        List<Vector2> outer = new(span + 1), inner = new(span + 1);
        for (var k = i0; k <= i1; k++)
        {
            var p = Outline[((k % n) + n) % n];
            var t = (k - i0) / (float)span;
            // float Sin(PI) is a hair negative, and Pow of a negative base is NaN: clamp before the root.
            var bite = depth * MathF.Pow(MathF.Max(0f, MathF.Sin(MathF.PI * t)), 0.7f);
            outer.Add(F(q, p.X, p.Y));
            inner.Add(F(q, p.X * (1f - bite), p.Y * (1f - bite)));
        }

        return (outer, inner);
    }

    private static Vector2[] BuildOutline()
    {
        const int Steps = 40;
        var outp = new List<Vector2>(Steps * 4);
        for (var seg = 0; seg < 4; seg++)
        {
            var a = Egg[seg * 3];
            Vector2 b = Egg[(seg * 3) + 1], d = Egg[(seg * 3) + 2], e = Egg[(seg * 3) + 3];
            for (var i = 0; i < Steps; i++)
            {
                var t = i / (float)Steps;
                var u = 1f - t;
                outp.Add((a * (u * u * u)) + (b * (3f * u * u * t)) + (d * (3f * u * t * t)) + (e * (t * t * t)));
            }
        }

        return outp.ToArray();
    }

    /// <summary>How far down the egg reaches at one offset from centre. A MAX over the sampled
    /// outline, the same way the generator's <c>bottom_at</c> does it.</summary>
    private static float BottomAt(float dx)
    {
        var fx = dx / HW;
        var best = float.MinValue;
        foreach (var p in Outline)
        {
            if (MathF.Abs(p.X - fx) < 0.04f)
            {
                best = MathF.Max(best, p.Y);
            }
        }

        return EggCy + (best * HH);
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
    /// <c>build_master.py</c>'s <c>anchors_for</c> bakes per cell.</summary>
    public static Vector2 Anchor0(string name) => name switch
    {
        "head" => new Vector2(CX, EggCy - (HH * 0.80f)),
        "face" => new Vector2(CX, EyeY - FaceLift),
        "body" => new Vector2(CX, EggCy + (HH * 0.07f)),
        "handL" => new Vector2(NubX, NubY),
        "handR" => new Vector2((2f * CX) - NubX, NubY),
        "mouth" => new Vector2(CX, MouthY),
        "earL" => new Vector2(CX - (HW * 0.62f), EggCy - (HH * 0.62f)),
        "earR" => new Vector2(CX + (HW * 0.62f), EggCy - (HH * 0.62f)),
        "tail" => new Vector2(CX + TailDx, BottomAt(TailDx) - (InkWidth / 2f) - TailSink),
        _ => new Vector2(CX, EggCy),
    };

    /// <summary>A worn pin, moved the way this body moves it. The position is the caller's (the
    /// manifest's rest-cell anchor, where the wardrobe was tuned) and this decides only the
    /// transform. One mass, so every pin but the hands takes the same one; the hands are the
    /// exception on every shell, because they attach to a nub this file draws.</summary>
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
