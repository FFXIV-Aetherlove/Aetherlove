namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using static AetherOS.PetKit.Rendering.LineArt.LineShell;

/// <summary>
/// The Chime, drawn. Twelfth shell, second Ice, and the first one the master format had to argue
/// with rather than simply describe.
///
/// <para>A transcription of <c>art-intake/element-shells/chime-master/build_master.py</c>: same
/// names, same numbers, so the two can be diffed. <c>tools/chimecheck.py</c> is what keeps them
/// one thing.</para>
///
/// <para><b>What it costs the engine: nothing.</b> Muffle needed two new channels because a
/// two-ball body has one mass moving relative to another. This is one rigid solid, and everything
/// a rigid solid needs had already been bought: <see cref="Ch.Dy"/> hovers it, <see cref="Ch.Sx"/>
/// and <see cref="Ch.Sy"/> are held inside 0.98-1.05 because it IS a solid, <see cref="Ch.Shake"/>
/// and <see cref="Ch.Blur"/> are the Serpent's rattle, and <see cref="Ch.Glow"/> is the Lantern's
/// flame. The swing is DERIVED from Shake rather than authored beside it, the way Muffle derives
/// head tilt from lean.</para>
///
/// <para><b>It swings from the cut.</b> Ice grows off a ledge and pivots where it grew, so
/// <see cref="Hang"/> is a little below the crown rather than at the centre of area or the tip. A
/// tall thin thing that pivots at the top moves a long way at the bottom and not at all at the
/// top, which is what a struck chime does and what sliding sideways would never have given. The
/// rotation is <see cref="LineShell.Swing"/>, the Lantern's own, applied to FINISHED points so no
/// stroke width is ever derived through it.</para>
///
/// <para><b>Every pin takes the same transform.</b> On a two-mass shell that is a decision with
/// two answers; here there is one body, so a hat, the face, the mouth and the nubs all ride the
/// same solid, and the only thing <see cref="Pin"/> has to be careful about is the hands, which
/// attach to a nub this file draws.</para>
///
/// <para>Two findings shaped this file: <b>the ink is not free at small widths</b> (five points
/// across a 130-wide base are 26 units each and a 12-wide ink takes 12 of that, leaving a dark
/// spike with no body in it), and <b>points under a centred face are read as teeth</b> unless
/// the envelope is a wedge.</para>
/// </summary>
public static class ChimeLineArt
{
    // ---------------------------------------------------------------- geometry --
    // Lifted from build_master.py unchanged. Same names, same numbers: a port that renames things
    // is a port nobody can diff against the generator it came from.
    public const float Cell = 384f;

    public const float CX = 192f;

    /// <summary>Where the longest point meets the ground line. This shell HOVERS: the tip is at
    /// the line, the body is not on it.</summary>
    private const float Sole = 309f;

    /// <summary>The crown apex, and the top of the whole shell.</summary>
    private const float TopY = 26f;

    /// <summary>Half width at the cut, and the widest this shell ever is. 176 across against the
    /// Puffer's 190 and Muffle's 180: a spiky shape has to be no smaller than a round one to read
    /// as the same MASS.
    ///
    /// <para><b>Widest at the TOP</b>, and that is the whole silhouette in one number. The first
    /// pass had a narrow crown over wide shoulders and it read as a shield; worse, it put the hat
    /// seat on the narrowest part of the shell. Ice grows off a ledge and is cut where it grows,
    /// so the widest line in the drawing is the cut and everything under it is growth getting
    /// thinner.</para></summary>
    private const float TopHalf = 97f;

    /// <summary>Where the crown curve hands over to the cut facets. The curve is spent ENTIRELY in
    /// this middle third: only the part a hat brim sits on is round.</summary>
    private const float CrownHalf = 48f;

    private const float CrownRise = 9f;

    /// <summary>The top corners, left and right, at different heights on purpose. Nothing on this
    /// shell is mirrored: an icicle cluster is a thing that grew.</summary>
    private const float ShoulderL = 42f, ShoulderR = 37f;

    /// <summary>Where the mass ends and the points begin: the last latitude at which this shell is
    /// one closed shape, and the seam the fill is decomposed along.</summary>
    private const float SplitY = 185f;

    private const float SplitL = 70f, SplitR = 73f;

    /// <summary>The bottom edge, walked RIGHT TO LEFT the way the outline runs.
    ///
    /// <para><b>Three, not five.</b> Five was the brief and five is what the concept drew, and at
    /// the shipped cell that is a spider with a jaw; see the class note.
    /// Three points at 40-50 wide each carry their fill, carry the facet bands through them, and
    /// read as one solid that broke downward.</para></summary>
    private static readonly Vector2[] Tips =
    [
        new(62f, 246f),
        new(-1f, Sole),
        new(-57f, 260f),
    ];

    private static readonly Vector2[] Valleys =
    [
        new(31f, 195f),
        new(-33f, 192f),
    ];

    /// <summary>Hand roots, on the FLANK rather than at an angle from a centre: this body has no
    /// centre to measure an angle from. Small on purpose: the arm that attaches here is drawn by
    /// the engine, and a big nub competes with it.</summary>
    private const float NubY = 112f, NubR = 17f;

    /// <summary>The facet bands: the left edge of each, the tone, and its opacity. <c>None</c> is
    /// untouched body.
    ///
    /// <para><b>They are all shadow, and that is a finding rather than a preference.</b> The body
    /// is authored at <see cref="LineShell.Base"/> and the rim at <see cref="LineShell.Rim"/>,
    /// which on the Frost palette are five percent apart, so on this shell a highlight cannot be
    /// seen at all and every bit of form has to be cut with shadow. The master's first pass drew a
    /// long white catch down the left flank, exactly as the Nautilus does, and it was invisible in
    /// the render.</para>
    ///
    /// <para><b>On a white body, light is the absence of shadow.</b> So the bands deepen left to
    /// right away from the one light, and the two lightest columns are simply untouched body.</para>
    ///
    /// <para>They run the WHOLE height, mass and points alike. Facets that stop at the top of each
    /// point make every point a separate object hanging off a body, which is a tentacle by
    /// definition; facets that run through make the points slices of one broken solid.</para></summary>
    private static readonly (float X, float Tone, float Op)[] Bands =
    [
        (-97f, Shadow, 0.22f),
        (-73f, 0f, 0f),
        (-48f, Shadow, 0.30f),
        (-24f, 0f, 0f),
        (9f, Shadow, 0.44f),
        (33f, Shadow, 0.26f),
        (57f, Shadow, 0.62f),
        (79f, Shadow, 0.82f),
    ];

    private const float BandEnd = 97f;

    /// <summary>The through-light: the one thing this shell does that nothing else on the roster
    /// can. Not a lamp: a COLUMN OF CLEAN ICE with shadow either side of it, so light comes out
    /// of the body instead of sitting on it.
    ///
    /// <para><see cref="Ch.Glow"/> works both ends of that at once: it lifts the column and it
    /// washes the shadow bands out, which is what being lit from inside actually looks like. Mood
    /// rides the same channel, so care shows on the body rather than in a particle.</para></summary>
    private const float CoreX0 = -24f, CoreX1 = 9f, CoreOp = 0.55f;

    /// <summary>The relief: a shadow line just inside the dark flank, given as (latitude, how far
    /// inside the flank) and solved against the real silhouette at each y rather than drawn at a
    /// fixed x. This body TAPERS, so a straight line is 14 inside the outline at the top and
    /// crossing it at the bottom.
    ///
    /// <para><b>A NAMED TUPLE rather than a Vector2, and that is the bug fix.</b> As a Vector2
    /// this pair reads (X, Y) and means (y, inset), so the two are crossed the moment anybody
    /// writes the obvious thing, which is what happened: the first cut drew the relief at y=13
    /// with x from the inset table and put a ten-wide ink-coloured bar across the air above the
    /// crown. `chimecheck.py` compared the TABLE and passed, because the table was right; only
    /// its use was wrong. A pair whose halves are not interchangeable should not be spelled as a
    /// point.</para></summary>
    private static readonly (float Y, float Inset)[] Relief =
    [
        (55f, 14f),
        (112f, 14f),
        (169f, 14f),
    ];

    private const float ReliefW = 10f;

    /// <summary>The sparkle, and the only job on the accent layer: same as Muffle. Two Ice
    /// shells, one accent idea. All upper-left, because there is one light. (dx, y, size).</summary>
    private static readonly Vector3[] Glints =
    [
        new(64f, 70f, 7.7f),
        new(-40f, 158f, 6f),
        new(7f, 204f, 5f),
    ];

    // ------------------------------------------------------------------- face --

    /// <summary><b>This shell draws no mouth.</b> <c>MouthY</c> is an ANCHOR: the engine draws the
    /// mouth on this point through the animation stack. Muffle's first cut drew one anyway and the
    /// pet wore two, one over the other.</summary>
    private const float EyeY = 112f, MouthY = 160f;

    private const float BlushDx = 55f, BlushY = 139f;

    /// <summary>How far ABOVE the eyes the wardrobe's <c>face</c> anchor sits. 42 is what every
    /// shell needing no glasses correction already uses: Jelly, Crab, Spintop and Muffle all -42,
    /// Wisp -42.5.</summary>
    private const float FaceLift = 42f;

    private static readonly EyeRig Rig = new(
        Dx: 35f, Y: EyeY, Rx: 31f, Ry: 36f,
        PupilRx: 22f, PupilRy: 27f, RingW: 10f, PupilOut: 4.4f,
        BigDx: 9.7f, BigDy: 15.2f, BigR: 8.1f,
        SmallDx: 7.9f, SmallDy: 12.7f, SmallR: 4.2f,
        ShutBow: 18.7f, LashW: 11f);

    public static float InkWidth { get; set; } = 12f;

    /// <summary>Ice, and the most rigid thing on the roster by a distance: below the Crab's 0.05,
    /// where the Jelly is 0.85. A solid that overshoots is a solid made of something else, and the
    /// one thing this silhouette exists to say is that it is not.
    ///
    /// <para>The trim lag is inherited rather than chosen: nothing on this shell rides the lagged
    /// pose. The facet bands are PAINT and take the body's own pose, which is no longer a
    /// judgement call: the Jelly shipped its dome beads on the lagged trim pose and on a boop
    /// they walked out through the top of its dome.</para></summary>
    public static readonly Material Stuff = new(Springiness: 0.06f, TrimLag: 0.30f);

    /// <summary>Where the shell swings FROM, and where its scale pivots.</summary>
    private static readonly Vector2 Hang = new(CX, TopY + 9f);

    /// <summary>Degrees of swing per unit of <see cref="Ch.Shake"/>. Derived, never authored
    /// beside it: shake says how hard it was struck and this says what that does, so a mood
    /// modifier that scales shake gets the swing scaled with it for free.</summary>
    private const float SwingPerUnit = 0.42f;

    // ------------------------------------------------------------------- poses --
    // build_master.py's POSES, verbatim. sx, sy, dy, shake, blur, glow, eye, blush.

    private static Key K(float sx, float sy, float dy, float shake, float blur, float glow, EyeState eye, bool blush = false)
    {
        var c = Neutral();
        c[(int)Ch.Sx] = sx;
        c[(int)Ch.Sy] = sy;
        c[(int)Ch.Dy] = dy;
        c[(int)Ch.Shake] = shake;
        c[(int)Ch.Blur] = blur;
        c[(int)Ch.Glow] = glow;
        return new Key(c, eye, blush);
    }

    private static readonly Key[] Poses =
    [
        // idle 0-8: THE ROSTER'S FIRST HELD POSE. Cells 0-5 do nothing but hover 2 units, then
        // 6-8 are one tremble that decays inside three frames and stops. Every other shell
        // answers "is this alive" by moving continuously; this one answers it by moving RARELY,
        // and a rare motion only reads as rare if the thing was genuinely still first.
        K(1.000f, 1.000f, 0.0f, 0.0f, 0.0f, 1.00f, Open),
        K(1.000f, 1.000f, -0.6f, 0.0f, 0.0f, 1.02f, Open),
        K(1.000f, 1.000f, -1.2f, 0.0f, 0.0f, 1.04f, Open),
        K(1.000f, 1.000f, -1.6f, 0.0f, 0.0f, 1.03f, Open),
        K(1.000f, 1.000f, -1.2f, 0.0f, 0.0f, 1.01f, Open),
        K(1.000f, 1.000f, -0.4f, 0.0f, 0.0f, 1.00f, Open),
        K(0.998f, 1.002f, 0.0f, 4.6f, 0.9f, 1.06f, Open),
        K(1.002f, 0.998f, 0.0f, -3.8f, 0.7f, 1.09f, Open),
        K(1.000f, 1.000f, 0.0f, 1.5f, 0.3f, 1.03f, Open),

        // 9-11: the rest cells the blink clip is built from
        K(1.000f, 1.000f, 0f, 0f, 0f, 1.00f, Open),
        K(1.000f, 1.000f, 0f, 0f, 0f, 1.00f, Shut),
        K(1.000f, 1.000f, 0f, 0f, 0f, 1.00f, HalfShut),

        // boop 12-18: a chime that is touched RINGS. The strike is the one place the solid is
        // allowed to give at all, and everything after it is the ring decaying: alternating
        // swings at falling amplitude with the ghost fading under them. It ends brighter than it
        // started, which is this shell's version of a smile it cannot make with its mouth.
        K(1.010f, 0.994f, 1.0f, -1.0f, 0.2f, 1.05f, Wide),
        K(1.050f, 0.960f, 4.0f, -11.0f, 1.0f, 1.30f, Wide),
        K(0.980f, 1.020f, -2.0f, 9.0f, 1.0f, 1.22f, Squint),
        K(1.010f, 0.994f, 1.0f, -6.5f, 0.8f, 1.16f, Wide),
        K(0.996f, 1.004f, -0.5f, 4.0f, 0.5f, 1.12f, Open),
        K(1.002f, 0.998f, 0.0f, -2.0f, 0.2f, 1.08f, Happy, true),
        K(1.000f, 1.000f, 0.0f, 0.8f, 0.0f, 1.05f, Happy, true),

        // nap 19-24: no slump is available (a solid cannot slump), so the sleep is told entirely
        // in hover height and in the light banking down, which is the honest version for this body.
        K(1.000f, 1.000f, 4.0f, 0f, 0f, 0.72f, Shut, true),
        K(1.000f, 1.000f, 5.2f, 0f, 0f, 0.66f, Shut, true),
        K(1.000f, 1.000f, 6.0f, 0f, 0f, 0.62f, Shut, true),
        K(1.000f, 1.000f, 5.6f, 0f, 0f, 0.65f, Shut, true),
        K(1.000f, 1.000f, 4.6f, 0f, 0f, 0.70f, Shut, true),
        K(1.000f, 1.000f, 4.0f, 0f, 0f, 0.74f, Shut, true),

        // hop 25-33: a rigid thing hops by TILTING and rising, not by crouching. The landing rings
        // exactly the way the boop does, because it is the same event arriving through the floor.
        K(1.006f, 0.996f, 2.0f, -5.0f, 0.2f, 1.02f, Open),
        K(1.012f, 0.990f, 4.0f, -9.0f, 0.5f, 1.10f, Open),
        K(0.994f, 1.008f, -12.0f, 6.0f, 0.4f, 1.16f, Wide),
        K(0.992f, 1.010f, -30.0f, 9.0f, 0.2f, 1.18f, Wide),
        K(0.994f, 1.008f, -37.0f, 7.0f, 0.0f, 1.16f, Open),
        K(0.996f, 1.006f, -28.0f, 2.0f, 0.0f, 1.12f, Open),
        K(0.998f, 1.002f, -11.0f, -4.0f, 0.3f, 1.06f, Wide),
        K(1.040f, 0.968f, 4.0f, -10.0f, 1.0f, 1.24f, Squint),
        K(1.000f, 1.000f, 0.0f, 3.0f, 0.4f, 1.08f, Open),

        // 34-38: the five rest-registered eye cells every shell owes the engine. Leave them out
        // and every drowsy state clamps back to the rest cell and the pet simply stares.
        K(1.000f, 1.000f, 0f, 0f, 0f, 1.00f, ThreeQ),
        K(1.000f, 1.000f, 0f, 0f, 0f, 1.00f, HalfShut),
        K(1.000f, 1.000f, 0f, 0f, 0f, 1.00f, Quarter),
        K(1.000f, 1.000f, 0f, 0f, 0f, 1.00f, Drowsy),
        K(1.000f, 1.000f, 0f, 0f, 0f, 1.00f, Heavy),
    ];

    /// <summary>Lets this shell's ambient channels run through a clip that does not act them. It
    /// has none (no Theta, no Phase, no Spin), so this is honestly a pass-through, and it is here
    /// because the dispatch asks every shell the same question.</summary>
    public static Channels WithAmbient(Channels target, int[] frames, float beat, out bool clipDrives, out float driven)
        => LineShell.WithAmbient(target, Poses, frames, beat, out clipDrives, out driven);

    public static Channels PoseAt(int prev, int cell, int next, int after, float phase)
        => LineShell.PoseAt(Poses, prev, cell, next, after, phase);

    /// <summary>This shell's face at a moment of its own clip: the eye between two cells and the
    /// blush across them. The pose table is this shell's; the resolving is the caller's.</summary>
    public static (EyeState Eye, float Blush) FaceAt(int cell, int next, float phase)
        => (LineShell.EyeAt(Poses, cell, next, phase), LineShell.BlushAt(Poses, cell, next, phase));

    // ---------------------------------------------------------------- the pose --

    /// <summary>The SCALE only, about the hang point. No lift in here: the hover goes on with the
    /// swing, because both belong to the hanging rather than to the shape: the Lantern's
    /// arrangement, and this shell hangs for the same reason it does.</summary>
    public static LinePose Posed(Channels c) =>
        new(c[(int)Ch.Sx], c[(int)Ch.Sy], 0f, CX, Hang.Y);

    private static float Lean(Channels c) => c[(int)Ch.Shake] * SwingPerUnit;

    /// <summary>Scale, then swing about the cut, then lift. Applied to FINISHED points, so no
    /// stroke width is ever derived through the rotation.</summary>
    private static Vector2 Swung(Channels c, Vector2 p) =>
        Swing(p, Hang, Lean(c), c[(int)Ch.Dy]);

    /// <summary>One authoring point under a pose. <c>x</c> is cell space, not an offset.</summary>
    private static Vector2 Pt(Channels c, float x, float y) => Swung(c, Posed(c).Pt(x, y));

    private static Vector2 Off(Channels c, float dx, float y) => Pt(c, CX + dx, y);

    /// <summary>Half width of the mass at a latitude, on one side. The taper is straight, so this
    /// is one lerp, and it is a method rather than a table because three things need it (the
    /// nubs, the eye clearance and the hand anchors) and three copies of a lerp is how two of them
    /// end up disagreeing.</summary>
    private static float Flank(float y, int side)
    {
        var topY = side < 0 ? ShoulderL : ShoulderR;
        var botX = side < 0 ? SplitL : SplitR;
        var u = Math.Clamp((y - topY) / (SplitY - topY), 0f, 1f);
        return TopHalf + ((botX - TopHalf) * u);
    }

    private static Vector2 NubPt(Channels c, int side) =>
        Off(c, side * (Flank(NubY, side) - 2f), NubY);

    // ------------------------------------------------------------- the pieces --
    // The silhouette PINCHES between the points, so it is not star-shaped and LineCanvas cannot
    // fan-fill it in one go. The Jelly has already paid for this exact problem
    // and its answer is the one copied here: split the shape into pieces that ARE star-shaped,
    // fill each from its own centroid, and let them tile: none of them is antialiased, so they
    // meet exactly.
    //
    // Five pieces: the crown cap, the trunk, and one per point. All five are CONVEX, which buys
    // the facets for free: a vertical slab clipped against a convex polygon is a convex polygon,
    // so each band is one more fan rather than a clip path.
    //
    // They are authoring-space constants because the pose is an affine map plus a rotation: the
    // decomposition cannot change with the pose, so it is computed once here and only its
    // vertices are moved each frame.

    private static readonly Vector2[] Cap = BuildCap();

    private static readonly Vector2[] Trunk =
    [
        new(-CrownHalf, TopY + CrownRise),
        new(CrownHalf, TopY + CrownRise),
        new(TopHalf, ShoulderR),
        new(SplitR, SplitY),
        new(-SplitL, SplitY),
        new(-TopHalf, ShoulderL),
    ];

    private static readonly Vector2[][] Points =
    [
        [new(Valleys[0].X, SplitY), new(SplitR, SplitY), Tips[0], Valleys[0]],
        [new(Valleys[1].X, SplitY), new(Valleys[0].X, SplitY), Valleys[0], Tips[1], Valleys[1]],
        [new(-SplitL, SplitY), new(Valleys[1].X, SplitY), Valleys[1], Tips[2]],
    ];

    private static readonly Vector2[][] Pieces = [Cap, Trunk, Points[0], Points[1], Points[2]];

    /// <summary>Every band, pre-clipped to every piece. One entry per (piece, band) pair that
    /// actually overlaps, carrying the polygon and the tone it is filled with.</summary>
    private static readonly List<(Vector2[] Poly, float Tone, float Op, bool Core)> Facets = BuildFacets();

    private static Vector2[] BuildCap()
    {
        // The crown, sampled off the same two cubics the outline strokes: from the left corner to
        // the apex, then the apex to the right corner. Closed across the chord, which is what
        // makes it convex and therefore fillable on its own.
        var pts = new List<Vector2>();
        var l0 = new Vector2(-CrownHalf, TopY + CrownRise);
        var ap = new Vector2(0f, TopY);
        var r0 = new Vector2(CrownHalf, TopY + CrownRise);
        var c1 = new Vector2(-CrownHalf * 0.55f, TopY);
        var c2 = new Vector2(CrownHalf * 0.55f, TopY);
        for (var i = 0; i <= 8; i++)
        {
            pts.Add(Cubic(l0, c1, ap, ap, i / 8f));
        }

        for (var i = 1; i <= 8; i++)
        {
            pts.Add(Cubic(ap, ap, c2, r0, i / 8f));
        }

        return pts.ToArray();
    }

    private static Vector2 Cubic(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        var u = 1f - t;
        return (u * u * u * a) + (3f * u * u * t * b) + (3f * u * t * t * c) + (t * t * t * d);
    }

    private static List<(Vector2[] Poly, float Tone, float Op, bool Core)> BuildFacets()
    {
        var list = new List<(Vector2[], float, float, bool)>();
        foreach (var piece in Pieces)
        {
            for (var i = 0; i < Bands.Length; i++)
            {
                if (Bands[i].Op <= 0f)
                {
                    continue;
                }

                var x1 = i + 1 < Bands.Length ? Bands[i + 1].X : BandEnd;
                var poly = Slab(piece, Bands[i].X, x1);
                if (poly.Length >= 3)
                {
                    list.Add((poly, Bands[i].Tone, Bands[i].Op, false));
                }
            }

            var core = Slab(piece, CoreX0, CoreX1);
            if (core.Length >= 3)
            {
                list.Add((core, Rim, CoreOp, true));
            }
        }

        return list;
    }

    /// <summary>A convex polygon clipped to a vertical slab: two half-plane passes, and the reason
    /// it is done here rather than with <see cref="LineCanvas.FillInPoly"/> is that a facet band
    /// crossing this silhouette is not one blob. FillInPoly pulls a mark radially toward one
    /// centroid, which is exact for a lit inset on a lobe and wrong for a strip that spans a
    /// shape with a bite out of it. Clipping per convex piece is exact for both.</summary>
    private static Vector2[] Slab(Vector2[] poly, float x0, float x1)
    {
        var a = ClipHalf(poly, x0, keepRight: true);
        return a.Length < 3 ? [] : ClipHalf(a, x1, keepRight: false);
    }

    private static Vector2[] ClipHalf(Vector2[] poly, float x, bool keepRight)
    {
        var outp = new List<Vector2>(poly.Length + 2);
        for (var i = 0; i < poly.Length; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Length];
            var pin = keepRight ? p.X >= x : p.X <= x;
            var qin = keepRight ? q.X >= x : q.X <= x;
            if (pin)
            {
                outp.Add(p);
            }

            if (pin != qin && MathF.Abs(q.X - p.X) > 1e-5f)
            {
                var t = (x - p.X) / (q.X - p.X);
                outp.Add(new Vector2(x, p.Y + ((q.Y - p.Y) * t)));
            }
        }

        return outp.ToArray();
    }

    // ------------------------------------------------------------------ anchors --

    /// <summary>The shell's anchors in authoring space, neutral pose: the same table
    /// <c>build_master.py</c>'s <c>anchors_for</c> bakes per cell.</summary>
    public static Vector2 Anchor0(string name) => name switch
    {
        // not the apex: where a hat BRIM has to sit to look right, which on this crown is a little
        // way into the curve
        "head" => new Vector2(CX, TopY + 10f),
        "face" => new Vector2(CX, EyeY - FaceLift),
        "body" => new Vector2(CX, 150f),
        "handL" => new Vector2(CX - (Flank(NubY, -1) - 2f), NubY),
        "handR" => new Vector2(CX + (Flank(NubY, 1) - 2f), NubY),
        "mouth" => new Vector2(CX, MouthY),
        // ears ride the crown corners, the only place on this shell that is both high and wide
        "earL" => new Vector2(CX - (CrownHalf * 0.92f), TopY + 12f),
        "earR" => new Vector2(CX + (CrownHalf * 0.92f), TopY + 12f),
        "tail" => new Vector2(CX, SplitY - 14f),
        _ => new Vector2(CX, 150f),
    };

    /// <summary>A worn pin, moved the way this body moves it.
    ///
    /// <para><b>Every kind takes the same transform, and that is the shell rather than a
    /// shortcut.</b> A two-mass shell has to decide whether a hat rides the head or the body;
    /// this is one rigid solid, so face, head and body are the same thing under the same swing.
    /// The hands are the exception every shell makes: they attach to a nub this file draws, so the
    /// file knows where they are better than any table does.</para></summary>
    public static Vector2 Pin(string name, PinKind kind, Vector2 rest, Channels c)
    {
        if (kind == PinKind.Hand)
        {
            return NubPt(c, name == "handL" ? -1 : 1);
        }

        return Pt(c, rest.X, rest.Y);
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
        c.Begin(dl, bottomCentre, displaySize, Cell, outer, flip);

        var blur = Math.Clamp(ch[(int)Ch.Blur], 0f, 1f);

        // --- the ghost, UNDER everything: the same body swung back the other way, which is what
        // the tremble looked like a frame ago. The Serpent's rattle does exactly this and for
        // exactly this reason: a tremble fast enough to read as a tremble is a blur for most of
        // its cycle, and without the ghost the judder is just three offset frames.
        if (blur > 0.01f)
        {
            var ghost = ch;
            ghost[(int)Ch.Shake] = -ch[(int)Ch.Shake] * 0.55f;
            Solid(c, ghost, body, ink, 0.34f * blur);
        }

        Solid(c, ch, body, ink, 1f);

        // --- the sparkle: the accent layer's only job on this shell, same as Muffle
        foreach (var g in Glints)
        {
            var at = Off(ch, g.X, g.Y);
            c.MoveTo(at - new Vector2(g.Z, 0f));
            c.LineTo(at + new Vector2(g.Z, 0f));
            c.Stroke(Tint(accent, AccRim), 4f, closed: false);
            c.MoveTo(at - new Vector2(0f, g.Z));
            c.LineTo(at + new Vector2(0f, g.Z));
            c.Stroke(Tint(accent, AccRim), 4f, closed: false);
        }

        var q = Posed(ch);
        if (blush > 0f)
        {
            for (var side = -1; side <= 1; side += 2)
            {
                c.Ellipse(Off(ch, side * BlushDx * q.Sx, BlushY), 16.5f * q.Sx, 10f * q.Sy, LineShell.BlushTint(blush));
            }
        }

        DrawEyes(c, Rig, eye, side => Off(ch, side * Rig.Dx * q.Sx, EyeY), q.Sx, q.Sy, eyeTint, ink);

        // NO MOUTH is drawn here. See the note at MouthY: the engine draws it, on the anchor this
        // shell publishes, and a shell that draws its own simply gives the pet two.
    }

    /// <summary>The body itself: nubs, fills, facets, relief, ink. Factored out because the ghost
    /// draws exactly this and nothing else: a motion ghost that is not the same shape as the
    /// thing it is ghosting is a second creature.</summary>
    private static void Solid(LineCanvas c, Channels ch, Vector4 body, Vector4 ink, float alpha)
    {
        var q = Posed(ch);
        var glow = ch[(int)Ch.Glow];
        var nubR = NubR * (q.Sx + q.Sy) * 0.5f;

        // --- hand roots FIRST, so the body covers their inner half: that is what turns a circle
        // into a shoulder rather than a button stuck on. Drawn here rather than through
        // LineShell.DrawNubs because that takes a LinePose and this shell's nubs are on a body
        // that ROTATES: the one thing a LinePose cannot carry.
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            c.Ellipse(NubPt(ch, side), nubR, nubR, Fade(Tint(body, NubFill), alpha));
        }

        // --- the fills, one per convex piece. They tile along their shared seams because none of
        // them is antialiased, which is the same trade the Jelly's scallops make.
        var fill = Fade(Tint(body, Base), alpha);
        foreach (var piece in Pieces)
        {
            Path(c, ch, piece);
            c.Fill(fill);
        }

        // --- the facets, pre-clipped per piece. PAINT, so they take the BODY'S OWN POSE: the
        // Jelly shipped its dome beads on the lagged trim pose, on the reading that a mark sitting
        // on a creature may arrive a beat late, and on a boop they walked out through the top of
        // the dome. An ornament rests on a surface; paint IS the surface.
        foreach (var (poly, tone, op, core) in Facets)
        {
            var o = core
                ? Math.Clamp(op * glow, 0f, 0.95f)
                : Math.Clamp(op * MathF.Max(0.30f, 2f - glow), 0f, 1f);
            if (o <= 0.01f)
            {
                continue;
            }

            Path(c, ch, poly);
            c.Fill(Fade(Tint(body, tone) with { W = o }, alpha));
        }

        // --- the relief, inset from the real flank at each latitude
        var r0 = Off(ch, Flank(Relief[0].Y, 1) - Relief[0].Inset, Relief[0].Y);
        var r1 = Off(ch, Flank(Relief[1].Y, 1) - Relief[1].Inset, Relief[1].Y);
        var r2 = Off(ch, Flank(Relief[2].Y, 1) - Relief[2].Inset, Relief[2].Y);
        c.MoveTo(r0);
        c.QuadTo(r1, r2);
        c.Stroke(Fade(Tint(body, Shadow) with { W = 0.85f }, alpha), ReliefW, closed: false);

        // --- the ink, over everything and in ONE closed stroke: the outline is what closes the
        // shape, so nothing soft may be drawn after it.
        Outline(c, ch);
        c.Stroke(Fade(ink, alpha), InkWidth);

        // The nubs' ink, on their OUTER arc only: a full ring crosses the body outline and the
        // two read as a lens where an arc reads as a shoulder.
        //
        // The sweep is the FLANK's, not the vertical. A half circle taken from twelve o'clock is
        // only right when the nub sits on a vertical edge; this shell's flanks lean nine and ten
        // degrees inward on the way down, so a vertical half circle finishes past the outline at
        // the top of each nub and short of it at the bottom, which is exactly the overhang that
        // was reported. Taking the chord along the flank instead lands both ends ON the
        // silhouette, and it moves correctly through every squash and every swing for free
        // because the flank does. The same argument LineShell.DrawNubs makes for solving the
        // crossings on a curved body, arriving at a straight edge where the answer is closed
        // form.
        var lean = Lean(ch) * MathF.PI / 180f;
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            var top = side < 0 ? ShoulderL : ShoulderR;
            var bot = side < 0 ? -SplitL : SplitR;
            // down the flank, in this side's own direction
            var phi = MathF.Atan2(SplitY - top, bot - (side * TopHalf)) + lean;
            c.Arc(
                NubPt(ch, side), nubR,
                side < 0 ? phi : phi - MathF.PI,
                side < 0 ? phi + MathF.PI : phi,
                Fade(ink, alpha), InkWidth);
        }
    }

    private static Vector4 Fade(Vector4 colour, float alpha) =>
        alpha >= 0.999f ? colour : colour with { W = colour.W * alpha };

    private static void Path(LineCanvas c, Channels ch, Vector2[] poly)
    {
        c.MoveTo(Off(ch, poly[0].X, poly[0].Y));
        for (var i = 1; i < poly.Length; i++)
        {
            c.LineTo(Off(ch, poly[i].X, poly[i].Y));
        }
    }

    /// <summary>The outline, as one closed path: a crown curve spent in the middle third, one cut
    /// facet each side, a straight taper down each flank, and a serrated bottom edge walked right
    /// to left.</summary>
    private static void Outline(LineCanvas c, Channels ch)
    {
        var l0 = Off(ch, -CrownHalf, TopY + CrownRise);
        var ap = Off(ch, 0f, TopY);
        var c1 = Off(ch, -CrownHalf * 0.55f, TopY);
        var c2 = Off(ch, CrownHalf * 0.55f, TopY);
        var r0 = Off(ch, CrownHalf, TopY + CrownRise);
        c.MoveTo(l0);
        c.CubicTo(c1, ap, ap);
        c.CubicTo(ap, c2, r0);
        c.LineTo(Off(ch, TopHalf, ShoulderR));
        c.LineTo(Off(ch, SplitR, SplitY));
        for (var i = 0; i < Tips.Length; i++)
        {
            c.LineTo(Off(ch, Tips[i].X, Tips[i].Y));
            if (i < Valleys.Length)
            {
                c.LineTo(Off(ch, Valleys[i].X, Valleys[i].Y));
            }
        }

        c.LineTo(Off(ch, -SplitL, SplitY));
        c.LineTo(Off(ch, -TopHalf, ShoulderL));
    }
}
