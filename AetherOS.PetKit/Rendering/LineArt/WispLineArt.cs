namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using static AetherOS.PetKit.Rendering.LineArt.LineShell;

/// <summary>
/// The Wisp, drawn - and the only shell on the roster with NO GENERATOR behind it.
///
/// <para>Every other conversion was a transcription: open the foundry script, copy its constants
/// and its pose table across, and the drawing follows because the numbers are the drawing. There
/// is no such script for this one. The sheet is the only artefact, so the sheet had to be read
/// back: a sheet is a cache of a function, taken at its word and run backwards for once.</para>
///
/// <para><b>The pose table was recovered, not authored.</b> <c>tools/lineart/recover_poses.py</c>
/// measures every cell's silhouette against the rest cell and hands back the (sx, sy, dy) the
/// artist was drawing. The result is immediately recognisable as the real animation, which is
/// the evidence it worked: a gentle idle pulse, a boop that squashes to 1.26/0.74 and rebounds
/// past 1.10, a nap settled flat, a hop that crouches to 1.46/0.68 and stretches to 0.80/1.21.
/// That is the widest range of any shell in the set and it is what makes this creature read as
/// weightless.</para>
///
/// <para><b>The GEOMETRY could not be recovered and is measured by hand.</b> The body layer is
/// not one blob - it is hollow through the belly and carries the tuft and the arms - so no radial
/// trace turns it into a single outline. Everything below is measured off cell 8 with a script
/// and written down: the profile table IS the silhouette, sampled every six pixels, because an
/// egg that is pointed at one end and round at the other is not any formula worth guessing
/// at.</para>
/// </summary>
public static class WispLineArt
{
    public const float Cell = 384f;

    public const float CX = 192f;

    /// <summary>The base of the egg: the row the squash pivots on, and what every measurement in
    /// this file is registered against.</summary>
    private const float EggBot = 373f;

    /// <summary>THE SILHOUETTE, measured off the sheet every six pixels and written down.
    ///
    /// <para>Kept as a table rather than fitted to a curve on purpose. An egg pointed at one end
    /// and round at the other is not a superellipse and is not two arcs, and every formula that
    /// nearly fits it is wrong in a different place - at the shoulder, or at the point, or across
    /// the widest row. The table cannot be wrong anywhere: it is what the artist drew, to the
    /// pixel, with the ink stroke's own half width taken back off.</para></summary>
    private static readonly (float Y, float Hw)[] Profile =
    [
        (143f, 0f), (146f, 9.5f), (152f, 24f), (158f, 34f), (164f, 42f),
        (170f, 48f), (176f, 54f), (182f, 59f), (188f, 64f), (194f, 68.5f),
        (200f, 72f), (206f, 75.5f), (212f, 78.5f), (218f, 81f), (224f, 83.5f),
        (230f, 85.5f), (236f, 87f), (242f, 88.5f), (248f, 90f), (254f, 90.8f),
        (260f, 91.3f), (266f, 91.7f), (272f, 92f), (278f, 92f), (284f, 91.8f),
        (290f, 91.4f), (296f, 90.8f), (302f, 90f), (308f, 89f), (314f, 87.8f),
        (320f, 86f), (326f, 82.5f), (332f, 78f), (338f, 74f), (344f, 69.5f),
        (350f, 64.5f), (356f, 57.5f), (362f, 49f), (368f, 36f), (373f, 0f),
    ];

    // The tuft: a flame standing on the crown. It is the one thing on this creature that never
    // stops moving, and it is nearly all CORE - a wide pale flame with a thin darker crescent
    // down its near side, which is what makes it read as burning rather than as a leaf.
    //
    // The outline is traced off the BODY layer and the core off the ACCENT layer, which is the
    // distinction I missed first time round: I traced the accent and drew it as the outline, so
    // the whole tuft came out at the size of its own core and the creature lost its crown.
    private const float TuftTip = 80f, TuftBase = 142f;
    private const float TuftSway = 8f;

    /// <summary>How much bigger than the sheet drew it. The sheet's flame is small enough to read
    /// as a detail; at this size it reads as the creature's head, which is what it is.</summary>
    private const float TuftGrow = 1.40f;

    private static readonly Vector2[] TuftOutline =
    [
        new(194f, 80f), new(187f, 88f), new(182f, 96f), new(178f, 104f), new(177f, 112f),
        new(177f, 120f), new(180f, 128f), new(185f, 136f), new(190f, 142f),
        new(199f, 142f),
        new(204f, 136f), new(208f, 128f), new(207f, 120f), new(200f, 112f), new(196f, 104f),
        new(195f, 96f), new(196f, 88f),
    ];

    /// <summary>The lighter core of the flame, MEASURED off the accent layer rather than traced
    /// by eye: the left and right edge of the accent every three rows, walked down one side and
    /// back up the other.
    ///
    /// <para>The hand trace this replaces was within a pixel or two everywhere, which is exactly
    /// why it was worth replacing - a mark that is nearly right is the kind that never gets
    /// checked again. The hand trace was a first pass; this is a measurement, and the numbers
    /// below are the artist's own.</para></summary>
    private static readonly Vector2[] TuftCore =
    [
        new(194f, 90f),
        new(191f, 93f), new(189f, 96f), new(187f, 99f), new(185f, 102f), new(183f, 105f),
        new(182f, 108f), new(182f, 111f), new(181f, 114f), new(181f, 117f), new(181f, 120f),
        new(182f, 123f), new(183f, 126f), new(184f, 129f), new(186f, 132f), new(187f, 135f),
        new(190f, 138f),
        new(196f, 138f),
        new(199f, 135f), new(202f, 132f), new(204f, 129f), new(205f, 126f), new(205f, 123f),
        new(205f, 120f), new(204f, 117f), new(201f, 114f), new(198f, 111f), new(197f, 108f),
        new(195f, 105f), new(195f, 102f), new(195f, 99f), new(195f, 96f), new(195f, 93f),
    ];

    /// <summary>THE FLAME'S OWN CATCH - the lit streak up the core, as (row, centre, width across
    /// the row) in the same authoring space the core is traced in.
    ///
    /// <para>It used to be a constant-width band authored at x 199-204, and it drew as a small
    /// square low on the near side. The reason is worth keeping: it was clipped to the flame's
    /// OUTLINE, and above y 108 the outline's right edge is at x 195 - so most of the streak was
    /// authored outside the shape it was painted on, and the clip guard correctly collapsed it.
    /// A mark that is nearly outside its host does not look like a smaller mark. It looks like a
    /// mistake, which is what a square in the corner of a flame is.</para>
    ///
    /// <para>Written against the core's own rows instead, it cannot leave: every station below
    /// sits inside <see cref="TuftCore"/> at that row, and it is clipped to the core rather than
    /// to the outline so the ink rim stays unlit. It runs the full length of the flame and is at
    /// its widest two thirds of the way up, which is where a flame is brightest.</para></summary>
    private static readonly (float Y, float Cx, float W)[] TuftLit =
    [
        (95f, 192.5f, 2.0f), (101f, 191.0f, 4.0f), (107f, 191.0f, 5.5f), (113f, 194.0f, 6.5f),
        (119f, 198.0f, 7.0f), (125f, 200.0f, 6.0f), (131f, 199.5f, 4.0f), (135f, 197.0f, 2.0f),
    ];

    private const float NubX = 93.5f, NubY = 304f, NubR = 18f;

    /// <summary>THE POOL - the pale patch low in the body, measured off the accent layer the same
    /// way the silhouette was measured off the body layer: half width every three rows.
    ///
    /// <para>It was a HORIZON before - a waterline bowed across the belly and floored on the
    /// creature's own bottom - and that was wrong about the drawing in the most basic way. The
    /// artist did not paint a waterline. They painted a LENS that floats clear of the ink on
    /// every side: sixty wide at its middle row against a body that is sixty-nine there, and
    /// stopping eight rows short of the base. The horizon reached the flanks and ran to the very
    /// bottom, so it read as the creature standing in something rather than as a marking on
    /// it.</para>
    ///
    /// <para>Measured across all thirty-eight cells it tracks the recovered pose exactly - at the
    /// deepest crouch the lens is 1.46× wide and 0.71× tall against the rest cell, which are the
    /// crouch's own sx and sy to three places. That is the evidence that a fixed table taken
    /// through <see cref="Posed"/> reproduces every cell of the sheet, and it is why this needs
    /// no clip: a mark built in the body's own frame cannot leave it.</para></summary>
    private static readonly (float Y, float Hw)[] Pool =
    [
        (326.5f, 0f), (328f, 20f), (330f, 33f), (333f, 43.5f), (336f, 50.5f), (339f, 55.5f),
        (342f, 58.5f), (345f, 60f), (348f, 58.5f), (351f, 54.5f), (354f, 49.5f), (357f, 44f),
        (360f, 37f), (363f, 26.5f), (365f, 15.5f), (366.5f, 0f),
    ];

    private const float BlushDx = 67f, BlushY = 311f, BlushRx = 15f, BlushRy = 8f;

    private static readonly EyeRig Rig = new(
        Dx: 37f, Y: 268.5f, Rx: 32.5f, Ry: 38.5f,
        PupilRx: 18.5f, PupilRy: 23.5f, RingW: 9f,

        // NEGATIVE, and this shell is the only one that wants it that way. Every other pet's
        // pupils sit outboard, which is what makes a pair look AT you. The Wisp's converge
        // slightly instead - the artist drew them that way and it is most of why this one reads
        // as the youngest thing in the set.
        PupilOut: -5f,
        BigDx: 12.5f, BigDy: 17.5f, BigR: 8f,
        SmallDx: 8f, SmallDy: 11f, SmallR: 4.5f,
        ShutBow: 18f, LashW: 11f, PupilDown: 0.10f);

    /// <summary>Low, and deliberately so on the one shell that looks like it wants the opposite.
    ///
    /// <para>The mouth is not drawn here - PetDraw places it from the manifest's anchor table,
    /// which is read straight off the pose with no spring in it. A springy body therefore walks
    /// away from its own mouth, and on the shell with by far the widest squash in the set that
    /// shows up as the mouth drifting up and down on its own. The Wisp does not need the spring
    /// anyway: its softness is all in the authored deform, which already runs from 1.47/0.67 to
    /// 0.80/1.21 without any help.</para></summary>
    public static readonly Material Stuff = new(Springiness: 0.18f, TrimLag: 0.55f);

    public static float InkWidth { get; set; } = 11f;

    public static Vector2 PartOrigin(string part) => new(CX, EggBot);

    /// <summary>The egg's half width at any row, read off the profile.</summary>
    private static float HalfAt(float y)
    {
        if (y <= Profile[0].Y)
        {
            return 0f;
        }

        for (var i = 1; i < Profile.Length; i++)
        {
            if (y <= Profile[i].Y)
            {
                var (y0, w0) = Profile[i - 1];
                var (y1, w1) = Profile[i];
                var t = (y - y0) / MathF.Max(0.001f, y1 - y0);
                return w0 + ((w1 - w0) * t);
            }
        }

        return 0f;
    }

    // ------------------------------------------------------------------- poses --

    /// <summary>The recovered table. <c>K(sx, sy, dy, phase, amp, eye, blush)</c> - the first
    /// three straight out of the measurement, the last two authored, because a tuft's waver
    /// leaves no trace in a silhouette's bounding box and had to be put back by hand.</summary>
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
        // idle 0-7: the pulse the measurement found - a breath in and a breath out, 5 percent
        // either way, with the flame going round once behind it.
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Open),
        K(1.031f, 0.969f, 5f, 0.1250f, 1.00f, Open),
        K(1.048f, 0.958f, 7f, 0.2500f, 1.00f, Open),
        K(1.031f, 0.969f, 5f, 0.3750f, 1.00f, Open),
        K(1.000f, 1.000f, 0f, 0.5000f, 1.00f, Open),
        K(0.978f, 1.024f, -6f, 0.6250f, 1.00f, Open),
        K(0.961f, 1.038f, -8f, 0.7500f, 1.00f, Open),
        K(0.978f, 1.024f, -6f, 0.8750f, 1.00f, Open),

        // blink 8-10. Phase flat, so the flame keeps burning through it.
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Open),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Shut),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, HalfShut),

        // boop 11-16: squashed to 1.26/0.74 and rebounding past 1.10. The flame is thrown about
        // twice as hard as the body is - it is the lightest thing on the creature.
        K(1.166f, 0.837f, 0f, 0.1200f, 1.80f, Wide),
        K(1.262f, 0.740f, 1f, 0.2600f, 2.30f, Squint),
        K(1.166f, 0.837f, 0f, 0.4200f, 2.00f, Wide),
        K(1.022f, 0.979f, 0f, 0.5800f, 1.50f, Wide),
        K(0.904f, 1.097f, -1f, 0.7400f, 1.30f, Open),
        K(1.000f, 1.000f, 0f, 0.9000f, 1.00f, Happy, blush: true),

        // nap 17-22: settled flat and barely stirring. The flame drops to a third and keeps
        // going, because a wisp that stopped burning would read as dead rather than as asleep.
        K(1.057f, 0.826f, 0f, 0.0000f, 0.32f, Shut, blush: true),
        K(1.070f, 0.809f, 3f, 0.0000f, 0.32f, Shut, blush: true),
        K(1.070f, 0.809f, 3f, 0.0000f, 0.32f, Shut, blush: true),
        K(1.057f, 0.826f, 0f, 0.0000f, 0.32f, Shut, blush: true),
        K(1.044f, 0.840f, -2f, 0.0000f, 0.32f, Shut, blush: true),
        K(1.044f, 0.840f, -2f, 0.0000f, 0.32f, Shut, blush: true),

        // hop 23-32: the crouch goes to 1.46/0.68 and the stretch to 0.80/1.21, which is by some
        // way the widest range on the roster. Nothing else in the set deforms like this, and it
        // is the entire reason the creature reads as weightless.
        K(1.406f, 0.708f, 0f, 0.0500f, 1.40f, Open),
        K(1.459f, 0.677f, 0f, 0.1600f, 1.60f, Squint),
        K(0.843f, 1.188f, -1f, 0.2900f, 2.40f, Wide),
        K(0.803f, 1.212f, -1f, 0.4200f, 2.60f, Wide),
        K(0.852f, 1.170f, -1f, 0.5500f, 2.30f, Open),
        K(1.000f, 1.000f, 0f, 0.6600f, 1.60f, Open),
        K(1.424f, 0.694f, 0f, 0.7600f, 1.50f, Squint),
        K(1.467f, 0.674f, 0f, 0.8500f, 1.60f, Squint),
        K(1.105f, 0.899f, 0f, 0.9300f, 1.30f, Open),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Open),

        // 33-37: the five rest-registered eye cells, from this shell's own rest pose.
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, ThreeQ),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, HalfShut),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Quarter),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Drowsy),
        K(1.000f, 1.000f, 0f, 0.0000f, 1.00f, Heavy),
    ];

    public static Channels WithAmbient(Channels target, int[] frames, float beat, out bool clipDrives, out float driven)
        => LineShell.WithAmbient(target, Poses, frames, beat, out clipDrives, out driven);

    public static Channels PoseAt(int prev, int cell, int next, int after, float phase)
        => LineShell.PoseAt(Poses, prev, cell, next, after, phase);

    /// <summary>This shell's face at a moment of its own clip: the eye between two cells and the
    /// blush across them. The pose table is this shell's; the resolving is the caller's.</summary>
    public static (EyeState Eye, float Blush) FaceAt(int cell, int next, float phase)
        => (LineShell.EyeAt(Poses, cell, next, phase), LineShell.BlushAt(Poses, cell, next, phase));

    /// <summary>Squash about the BASE, which is what the recovery measured against - it took each
    /// cell's bottom row as the registration, so the table only means what it says if the pivot
    /// here is the same one.</summary>
    public static LinePose Posed(Channels c) =>
        new(c[(int)Ch.Sx], c[(int)Ch.Sy], c[(int)Ch.Dy], CX, EggBot);

    /// <summary>The pins, in authoring space. <c>mouth</c> and <c>body</c> are the manifest's own
    /// numbers, brought across when the drawn body started answering for its pins - this was the
    /// one shell on the roster whose table did not carry them, because until then nothing asked.
    /// The mouth sits nine rows above the pool, which is what makes the pool read as the chin
    /// under it rather than as a mark in its own right.</summary>
    public static Vector2 Anchor0(string name) => name switch
    {
        "head" => new Vector2(CX, 150f),
        "face" => new Vector2(CX, Rig.Y - 42f),
        "body" => new Vector2(CX, 256f),
        "mouth" => new Vector2(CX, 318f),
        "handL" => new Vector2(NubX, NubY),
        "handR" => new Vector2((2f * CX) - NubX, NubY),
        _ => new Vector2(CX, 300f),
    };

    public static Vector2 Anchor(string name, Channels c) => Posed(c).Pt2(Anchor0(name));

    /// <inheritdoc cref="JellyLineArt.Pin"/>
    ///
    /// <para>One transform for everything on this shell: the egg squashes about its base and the
    /// face goes with it, which is most of why an egg with a face on it reads as soft.</para>
    public static Vector2 Pin(string name, PinKind kind, Vector2 rest, Channels c) =>
        kind == PinKind.Hand ? Anchor(name, c) : Posed(c).Pt2(rest);

    // -------------------------------------------------------------------- paths --

    private static List<Vector2> EggPoints(LinePose q)
    {
        var pts = new List<Vector2>(Profile.Length * 2);
        for (var i = 0; i < Profile.Length; i++)
        {
            pts.Add(q.Pt(CX + Profile[i].Hw, Profile[i].Y));
        }

        for (var i = Profile.Length - 1; i >= 0; i--)
        {
            pts.Add(q.Pt(CX - Profile[i].Hw, Profile[i].Y));
        }

        return pts;
    }

    /// <summary>How far the flame is leaning at a given row. Zero at its base and full at the
    /// tip, so it bends rather than slides - a flame nailed to the crown that translated whole
    /// would read as a hat coming loose.</summary>
    private static float Lean(Channels c, float y)
    {
        var up = Math.Clamp((TuftBase - y) / (TuftBase - TuftTip), 0f, 1f);
        return TuftSway * c[(int)Ch.Amp] * MathF.Pow(up, 1.4f)
            * MathF.Sin(MathF.Tau * c[(int)Ch.Phase]);
    }

    /// <summary>A traced flame point, grown about the base it stands on and then leaned.</summary>
    private static Vector2 TuftPt(LinePose q, Channels ch, Vector2 src)
    {
        var x = CX + ((src.X - CX) * TuftGrow);
        var y = TuftBase + ((src.Y - TuftBase) * TuftGrow);
        return q.Pt(x + Lean(ch, src.Y), y);
    }

    private static void TuftPath(LineCanvas c, LinePose q, Channels ch, Vector2[] src)
    {
        for (var i = 0; i < src.Length; i++)
        {
            var p = TuftPt(q, ch, src[i]);
            if (i == 0)
            {
                c.MoveTo(p);
            }
            else
            {
                c.LineTo(p);
            }
        }

        c.LineTo(TuftPt(q, ch, src[0]));
    }

    /// <summary>THE CATCH - the single streak of light down the upper right flank, measured off
    /// the sheet's own white: for a row, how far INBOARD OF THE PROFILE EDGE its centre runs and
    /// how wide it is across that row.
    ///
    /// <para>Held as an inset rather than as a position because that is what the mark is about.
    /// It is a shoulder light: it belongs to the edge it runs beside, and written this way it
    /// stays on that shoulder no matter what the profile is re-measured to.</para>
    ///
    /// <para>Two things the first pass had wrong and the measurement settles. The streak
    /// TAPERS - seven pixels across at the shoulder down to one at its tail - where a constant
    /// eleven read as a slug laid on the body rather than as light caught on a curve. And the
    /// inset CLOSES as it descends, thirteen at the top to seven at the bottom, so the streak
    /// converges on the edge instead of running parallel to it. A parallel streak is a stripe; a
    /// converging one is a highlight.</para></summary>
    private static readonly (float Y, float In, float W)[] Catch =
    [
        (173f, 11.0f, 1.0f), (177f, 12.9f, 7.0f), (185f, 11.5f, 7.0f), (191f, 10.8f, 6.0f),
        (197f, 9.8f, 4.5f), (203f, 8.8f, 3.5f), (209f, 8.0f, 3.0f), (215f, 7.3f, 2.0f),
        (223f, 7.1f, 1.0f),
    ];

    /// <summary>The catch as its two EDGES rather than as a centreline and a width, because a
    /// band built the second way can only taper in a straight line and this one does not: it
    /// holds seven across the shoulder and then runs away to nothing, which is the shape of the
    /// thing. The widths in the table are measured across the ROW, so the edges are set across
    /// the row too - taking them along the normal would narrow the streak by the tilt.</summary>
    private static (List<Vector2> L, List<Vector2> R) CatchEdges(LinePose q)
    {
        var left = new List<Vector2>(Catch.Length);
        var right = new List<Vector2>(Catch.Length);
        for (var i = 0; i < Catch.Length; i++)
        {
            var (y, inset, w) = Catch[i];
            var cx = CX + HalfAt(y) - inset;
            left.Add(q.Pt(cx - (w * 0.5f), y));
            right.Add(q.Pt(cx + (w * 0.5f), y));
        }

        return (left, right);
    }

    /// <summary>The pool's outline, walked down one flank and back up the other.</summary>
    private static List<Vector2> PoolPoints(LinePose q)
    {
        var pts = new List<Vector2>(Pool.Length * 2);
        for (var i = 0; i < Pool.Length; i++)
        {
            pts.Add(q.Pt(CX + Pool[i].Hw, Pool[i].Y));
        }

        for (var i = Pool.Length - 1; i >= 0; i--)
        {
            pts.Add(q.Pt(CX - Pool[i].Hw, Pool[i].Y));
        }

        return pts;
    }

    // --------------------------------------------------------------------- draw --

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

        // The flame goes down FIRST and keeps its whole outline, so the egg drawn over it cuts
        // the base off for free. Inking it after the body instead would draw the half that is
        // inside the head straight across the crown.
        TuftPath(c, q, ch, TuftOutline);
        c.Fill(Tint(body, Base));

        // The core is the MAIN colour lifted, not the accent. A flame is the same stuff as the
        // creature burning brighter; painting it in the accent made the crown look like a
        // separate object stuck on top.
        TuftPath(c, q, ch, TuftCore);
        c.Fill(Tint(body, Rim));
        var core = c.Capture();

        // And the catch up the core, so the flame is lit from the same place the body is. Cut to
        // the CORE and not to the outline: the strip between them is the flame's dark near side,
        // and lighting that is what turned the crown into a flat leaf.
        var litL = new List<Vector2>(TuftLit.Length);
        var litR = new List<Vector2>(TuftLit.Length);
        for (var i = 0; i < TuftLit.Length; i++)
        {
            var (y, cx, w) = TuftLit[i];
            litL.Add(TuftPt(q, ch, new Vector2(cx - (w * 0.5f), y)));
            litR.Add(TuftPt(q, ch, new Vector2(cx + (w * 0.5f), y)));
        }

        c.BandEdges(litL, litR);
        c.FillBandIn(core, Vector4.Lerp(Tint(body, Rim), Spark, 0.66f));

        TuftPath(c, q, ch, TuftOutline);
        c.Stroke(ink, InkWidth);

        DrawNubs(c, q, CX, NubX, NubY, NubR, InkWidth, body, ink, fill: true);

        var egg = EggPoints(q);
        c.MoveTo(egg[0]);
        for (var i = 1; i < egg.Count; i++)
        {
            c.LineTo(egg[i]);
        }

        c.LineTo(egg[0]);
        c.Fill(Tint(body, Base));
        var silhouette = c.Capture();

        // THE POOL. A lens of accent floating low in the body, filled straight from the
        // measured table - no clip, and nothing derived from the profile except the frame it is
        // drawn in.
        //
        // It was a horizon-and-floor band before: a waterline bowed across the belly with the
        // creature's own bottom beneath it. That is not what is on the sheet. The artist painted
        // a lens with ink-coloured body showing all the way round it, and the difference is not
        // subtle at any size - a mark that touches the silhouette belongs to the outline, and one
        // that does not belongs to the face. This one belongs to the face: it sits directly under
        // the mouth anchor and it is most of what the creature's chin is.
        //
        // The reason it can be a plain fill is the one the earlier ellipse failed on. A fan
        // clipped to a polygon casts its rays from the shape's own centroid, and the old ellipse
        // reached past the bottom of the body, so its centre sat OUTSIDE the clip and every ray
        // collapsed. A lens measured inside the creature has its centroid inside the creature.
        var pool = PoolPoints(q);
        c.MoveTo(pool[0]);
        for (var i = 1; i < pool.Count; i++)
        {
            c.LineTo(pool[i]);
        }

        c.LineTo(pool[0]);
        c.Fill(Tint(accent, AccBase));

        // THE CATCH, and there is exactly ONE of it. The sheet was checked cell by cell for a
        // second: all thirty-eight carry a single streak on the upper right flank and nothing
        // else. The short lower streak that used to sit beneath this one was authored on the
        // theory that two lengths say curved glass - a fair theory, and not this drawing.
        //
        // Nearly white rather than a body tint. Every other shell's rim light is a lit version of
        // its own colour because every other shell is a solid; this one is lit from inside, and
        // the sheet draws the catch as a specular streak that owes almost nothing to the palette.
        // At Tint(body, Rim) it was technically present and read as absent.
        var (catchL, catchR) = CatchEdges(q);
        c.BandEdges(catchL, catchR);
        c.FillBandIn(silhouette, Vector4.Lerp(Tint(body, Rim), Spark, 0.62f));

        // ALWAYS on, unlike every other shell, where blush is a clip flag that comes up for a
        // boop and a nap. On the Wisp the artist drew the cheeks into the rest cell: they are
        // part of the face rather than a reaction, and switching them off for the idle took the
        // creature's colour with them.
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            c.EllipsePath(
                q.Pt(CX + (side * BlushDx), BlushY),
                BlushRx * q.Sx * (1f + (0.22f * blush)),
                BlushRy * q.Sy * (1f + (0.22f * blush)));
            c.FillInPoly(silhouette, Blush);
        }

        c.MoveTo(egg[0]);
        for (var i = 1; i < egg.Count; i++)
        {
            c.LineTo(egg[i]);
        }

        c.Stroke(ink, InkWidth);

        // The nub arcs are SOLVED against the silhouette rather than fixed at a half circle -
        // this body has no straight flank anywhere, and its widest row moves with every squash,
        // so a fixed arc would end in mid air at one end and cut back across the head at the
        // other. LineCanvas already knows how; it just has to be told what inside means.
        DrawNubs(
            c, q, CX, NubX, NubY, NubR, InkWidth, body, ink, fill: false,
            p => LineCanvas.InPoly(c.To(p), silhouette));

        DrawEyes(c, Rig, eye, side => q.EyePt(CX + (side * Rig.Dx), Rig.Y), q.Ex, q.Ey, eyeTint, ink);
    }
}
