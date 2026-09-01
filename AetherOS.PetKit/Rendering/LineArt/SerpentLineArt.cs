namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;

using static AetherOS.PetKit.Rendering.LineArt.LineShell;

/// <summary>
/// The Serpent, drawn. Fifth shell, and the most mask-dependent on the roster, which after the
/// Crab, the Nautilus and the Puffer is exactly the thing to check first, because on this branch
/// every mask in a generator has turned out to be a draw-order requirement wearing a disguise.
///
/// <para><b>It is drawn as a STAMPED tube.</b> Not an outline with a fill: a run of overlapping
/// discs along a coil, and the ink is the same run at a larger radius laid down first. So each
/// part's fill sits inside its own outline, and the part after it covers the ink of the one
/// behind. The sheet needs a mask per part to get that; walked in path order it simply falls
/// out.</para>
///
/// <para><b>The path is walked back-to-front on purpose</b>, and the generator says why: from the
/// BACK of the ellipse "so that within every turn the far half is laid down before the near half,
/// which is what makes a turn pass in front of itself rather than behind". A coil that overlaps
/// itself twice cannot be drawn in any other order and be right.</para>
///
/// <para><b>No squash.</b> Like the Puffer, and for the same kind of reason: "a coil that
/// squashes is a coil being sat on. This creature BREATHES: a uniform half-percent about the
/// base of the stack, and everything else it does is in the neck." So it poses on <c>k</c> and
/// <c>dy</c>, and spends its beats on <c>neck</c>, <c>sway</c> and <c>shake</c>.</para>
/// </summary>
public static class SerpentLineArt
{
    // ---------------------------------------------------------------- geometry --
    // Lifted from the retired serpent-sheet/build_sheet.py unchanged: same names, same numbers.
    public const float Cell = 384f;

    public const float CX = 192f;
    private const float InkEdge = 10f, InkFine = 5f;

    private const float Turns = 2f;
    private const float R0 = 134f, R1 = 65f;
    private const float Squash = 0.30f;
    private const float BaseY = 302f;
    private const float Rise = 48f;
    private const float W0 = 26f, W1 = 30f;
    private const int Samples = 460;

    private const float NeckLen = 42f;
    private const float HeadR = 60f, HeadDrop = 0.50f;

    private const float TailDeg = 24f, TailRootR = 0.55f, TailH = 112f;
    private const float Rattle = 1.35f;
    private const int Beads = 4;
    private const float EyeDx = 24f, EyeRx = 18f, EyeRy = 22f;
    private const float MouthDy = 30f;
    private const float NubDx = 46f, NubR = 12f;

    /// <summary>The creature in DRAW ORDER, which on this shell is the whole architecture: the
    /// tail first so the coil covers its root, then the low wide outer turn, then the inner turn
    /// over it, then the neck and head stamped as ONE silhouette so the head is the end of the
    /// body rather than a ball on a stick.</summary>
    private const int PartTail = 0, PartOut = 1, PartIn = 2, PartHead = 3;

    private static readonly EyeRig Rig = new(
        Dx: EyeDx, Y: 0f, Rx: EyeRx, Ry: EyeRy,
        PupilRx: 10.5f, PupilRy: 13.5f, RingW: 5.5f, PupilOut: 2f,
        BigDx: 6f, BigDy: 8f, BigR: 4.6f,
        SmallDx: 5f, SmallDy: 8f, SmallR: 2.4f,
        ShutBow: 12f, LashW: 5.5f);

    /// <summary>A snake is muscle. It carries a little follow-through without ever wobbling like
    /// a jelly, and the coil itself barely moves - the life is in the neck.</summary>
    public static readonly Material Stuff = new(Springiness: 0.25f, TrimLag: 0.50f);

    public static Vector2 PartOrigin(string part) => new(CX, BaseY);

    public static float InkWidth { get; set; } = InkEdge;

    // ------------------------------------------------------------------- poses --

    /// <summary>Matching the generator's <c>P(k, dy, neck, sway, shake, blur, eye, blush)</c>.
    /// neck is added to the resting neck length; sway moves where the head sits; shake is the
    /// rattle's whip, in authoring units.</summary>
    private static Key K(float k, float dy, float neck, float sway, float shake, EyeState eye, bool blush = false, bool blur = false)
    {
        var c = Neutral();
        c[(int)Ch.K] = k;
        c[(int)Ch.Dy] = dy;
        c[(int)Ch.Neck] = neck;
        c[(int)Ch.Sway] = sway;
        c[(int)Ch.Shake] = shake;

        // blur is a bool on the sheet and a NUMBER here, which is strictly better: the ghost
        // fades up and down across the blend instead of popping on for two cells.
        c[(int)Ch.Blur] = blur ? 1f : 0f;
        return new Key(c, eye, blush);
    }

    private static readonly Key[] Poses =
    [
        // idle 0-7: the breath, the neck drifting, and the rattle stirring with it.
        K(1.000f, 0f, 0f, 0.0f, 0.0f, Open),
        K(1.004f, -1f, 1f, 1.6f, -1.2f, Open),
        K(1.006f, -1f, 2f, 3.0f, -2.0f, Open),
        K(1.004f, -1f, 1f, 3.8f, -1.4f, Open),
        K(1.000f, 0f, 0f, 3.0f, 0.0f, Open),
        K(0.996f, 1f, -1f, 1.6f, 1.4f, Open),
        K(0.994f, 1f, -2f, 0.0f, 2.0f, Open),
        K(0.996f, 1f, -1f, -1.6f, 1.2f, Open),

        // blink 8-10
        K(1.000f, 0f, 0f, 0.0f, 0.0f, Open),
        K(1.000f, 0f, 0f, 0.0f, 0.0f, Shut),
        K(1.000f, 0f, 0f, 0.0f, 0.0f, HalfShut),

        // boop 11-16: the flinch, the rear, and a rattle fast enough to blur - which is why the
        // two peak cells draw it twice, the ghost at the opposite throw.
        K(1.000f, 1f, -2f, 0.0f, -3.0f, Wide),
        K(1.010f, -2f, 7f, -2.0f, 13.0f, Wide),
        K(1.020f, -3f, 11f, 5.0f, -17.0f, Wide, blur: true),
        K(1.020f, -3f, 10f, -5.0f, 17.0f, Squint, blur: true),
        K(1.010f, -2f, 7f, 2.5f, -9.0f, Squint),
        K(1.000f, 1f, 0f, 0.0f, 4.0f, Happy, blush: true),

        // nap 17-22: the head comes down onto its own coil. A sleeping snake is a coil with a
        // face on it, which is the one silhouette here where the neck almost disappears.
        K(0.996f, 2f, -26f, 0.0f, 0.0f, Shut, blush: true),
        K(0.998f, 2f, -27f, 0.8f, 0.0f, Shut, blush: true),
        K(1.000f, 3f, -28f, 1.4f, 0.0f, Shut, blush: true),
        K(1.000f, 3f, -28f, 0.8f, 0.0f, Shut, blush: true),
        K(0.998f, 2f, -27f, 0.0f, 0.0f, Shut, blush: true),
        K(0.996f, 2f, -26f, -0.8f, 0.0f, Shut, blush: true),

        // hop 23-32: a SURGE. A coiled snake does not hop, it pushes off its own coil - the body
        // gathers, the neck drives up, and the coil spreads under it on the way down.
        K(1.010f, 3f, -10f, 0.0f, 0.0f, Squint),
        K(1.020f, 5f, -16f, 0.0f, 0.0f, Squint),
        K(0.980f, -5f, 11f, 0.0f, 0.0f, Wide),
        K(0.970f, -8f, 12f, 0.0f, 0.0f, Wide),
        K(0.970f, -9f, 11f, 1.5f, 0.0f, Open),
        K(0.980f, -8f, 11f, 2.0f, 0.0f, Open),
        K(0.990f, -4f, 6f, 1.0f, 0.0f, Open),
        K(1.010f, 2f, -6f, 0.0f, 0.0f, Squint),
        K(1.000f, 1f, -2f, -1.0f, 0.0f, Open),
        K(1.000f, 0f, 0f, 0.0f, 0.0f, Open),

        // 33-37: the five rest-registered eye cells, from this shell's own rest pose.
        K(1.000f, 0f, 0f, 0.0f, 0.0f, ThreeQ),
        K(1.000f, 0f, 0f, 0.0f, 0.0f, HalfShut),
        K(1.000f, 0f, 0f, 0.0f, 0.0f, Quarter),
        K(1.000f, 0f, 0f, 0.0f, 0.0f, Drowsy),
        K(1.000f, 0f, 0f, 0.0f, 0.0f, Heavy),
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

    /// <summary>Breath about the coil's own base, plus a bob. Uniform, because a coil that
    /// squashes is a coil being sat on.</summary>
    public static LinePose Posed(Channels c) =>
        new(c[(int)Ch.K], c[(int)Ch.K], c[(int)Ch.Dy], CX, BaseY);

    // ------------------------------------------------------------------- paths --

    /// <summary>Tail-end to neck-top, then the head's own seat. Walked from the BACK of the
    /// ellipse so that within every turn the far half is laid down before the near half, which is
    /// what makes a turn pass in front of itself rather than behind.</summary>
    private static (List<Vector2> Path, List<int> Seams) CoilPath(Channels ch)
    {
        var q = Posed(ch);
        var pts = new List<Vector2>(Samples + 64);
        var seams = new List<int>();
        var total = Turns * MathF.Tau;

        for (var i = 0; i < Samples; i++)
        {
            var t = (float)i / (Samples - 1);
            var th = (-MathF.PI / 2f) + (t * total);
            var r = R0 + ((R1 - R0) * t);
            var cy = BaseY - (Rise * t);
            pts.Add(q.Pt(CX + (r * MathF.Cos(th)), cy + (r * Squash * MathF.Sin(th))));

            if (i > 0 && (int)(t * Turns) != (int)((i - 1) / (float)(Samples - 1) * Turns))
            {
                seams.Add(i);
            }
        }

        // The neck, as a cubic that leaves the coil mostly UPWARD. Weighted along the coil's own
        // tangent it leaves flat - the tangent at the back of the ellipse is horizontal - and its
        // first third lies across the coil as a bar.
        var h = pts[^1];
        var pv = pts[^6];
        var d = h - pv;
        var len = MathF.Max(1f, d.Length());
        var u = d / len;

        var neck = NeckLen + ch[(int)Ch.Neck];
        var tx = CX + ch[(int)Ch.Sway];
        var ty = h.Y - neck;
        var p1 = new Vector2(h.X + (u.X * neck * 0.16f), h.Y - (neck * 0.42f));
        var p2 = new Vector2(tx, ty + (neck * 0.46f));
        var p3 = new Vector2(tx, ty);

        for (var i = 1; i <= 60; i++)
        {
            var t = i / 60f;
            var m = 1f - t;
            pts.Add(
                (h * (m * m * m))
                + (p1 * (3f * m * m * t))
                + (p2 * (3f * m * t * t))
                + (p3 * (t * t * t)));
        }

        seams.Add(Samples);
        return (pts, seams);
    }

    private static float WidthAt(int i, int n) => W0 + ((W1 - W0) * (i / (float)(n - 1)));

    private static float TailW(int i, int n)
    {
        const float Root = W0 * 0.86f;
        return Root + (((Root * 0.44f) - Root) * (i / (float)(n - 1)));
    }

    /// <summary>The tail, with the shake as a TIP-WEIGHTED perpendicular whip. Thrown at the
    /// rattle alone the segments simply leave; the bend has to be in the length of the animal,
    /// ramped from nothing at the root to all of it at the tip, which is both what a hinged thing
    /// does and what keeps the rattle attached to the snake.</summary>
    private static List<Vector2> TailPath(Channels ch, float shake)
    {
        var q = Posed(ch);
        var a = TailDeg * MathF.PI / 180f;
        var root = q.Pt(
            CX + (R0 * TailRootR * MathF.Cos(a)),
            BaseY + (R0 * TailRootR * Squash * MathF.Sin(a)));

        var h = TailH * ch[(int)Ch.K];
        var p1 = root + new Vector2(26f, -h * 0.34f);
        var p2 = root + new Vector2(-12f, -h * 0.82f);
        var p3 = root + new Vector2(8f, -h);

        var pts = new List<Vector2>(40);
        for (var i = 0; i < 40; i++)
        {
            var t = i / 39f;
            var m = 1f - t;
            pts.Add(
                (root * (m * m * m))
                + (p1 * (3f * m * m * t))
                + (p2 * (3f * m * t * t))
                + (p3 * (t * t * t)));
        }

        if (MathF.Abs(shake) <= 0.01f)
        {
            return pts;
        }

        var bent = new List<Vector2>(pts.Count);
        for (var i = 0; i < pts.Count; i++)
        {
            var t = i / (float)(pts.Count - 1);
            var pv = pts[Math.Max(0, i - 1)];
            var nx = pts[Math.Min(pts.Count - 1, i + 1)];
            var d = nx - pv;
            var len = MathF.Max(1f, d.Length());
            var w = shake * MathF.Pow(t, 1.8f);
            bent.Add(pts[i] + new Vector2(-d.Y / len * w, d.X / len * w));
        }

        return bent;
    }

    /// <summary>Where each rattle segment sits, and how far the shake has thrown it. The shake is
    /// applied PERPENDICULAR to the tail and grows along the stack, so the tip swings furthest
    /// and the root barely moves - which is how a thing hinged at one end behaves, and why a
    /// uniform offset reads as the whole rattle sliding sideways rather than whipping.</summary>
    private static List<(Vector2 At, float Rx, float Ry, float Deg)> BeadGeo(List<Vector2> pts)
    {
        var tip = pts[^1];
        var d = pts[^1] - pts[^6];
        var len = MathF.Max(1f, d.Length());
        var u = d / len;
        var ang = (MathF.Atan2(u.Y, u.X) * 180f / MathF.PI) + 90f;
        var rw = TailW(pts.Count - 1, pts.Count) * Rattle * 2.1f;

        var outp = new List<(Vector2, float, float, float)>(Beads);
        for (var i = 0; i < Beads; i++)
        {
            var at = tip + (u * (i * rw * 0.58f));
            var k = 1f - (i * (0.44f / MathF.Max(1, Beads - 1)));
            outp.Add((at, rw * k * 0.94f, rw * k * 0.74f, ang));
        }

        return outp;
    }

    /// <summary>The rattle: four segments, each FILLED AND INKED before the next, so the later
    /// bead's fill wipes the earlier bead's outline and what survives is the lapping arc. The
    /// sheet needs a mask per bead for this, because its layer contract forbids the overlay
    /// painting fills - "stroked without masks all four draw whole and the rattle reads as a
    /// stack of separate rings". Drawn in one pass the wipe is free.</summary>
    private static void Rattle_(LineCanvas c, List<Vector2> pts, Vector4 accent, Vector4 ink, float alpha)
    {
        foreach (var (at, rx, ry, deg) in BeadGeo(pts))
        {
            var xf = new LocalXf(at, 1f, 1f, deg);
            void Ring(float pad)
            {
                const int N = 28;
                for (var i = 0; i <= N; i++)
                {
                    var a = MathF.Tau * i / N;
                    var p = xf.To(MathF.Cos(a) * (rx + pad), MathF.Sin(a) * (ry + pad));
                    if (i == 0)
                    {
                        c.MoveTo(p);
                    }
                    else
                    {
                        c.LineTo(p);
                    }
                }
            }

            // The bead's ink is a CENTRED stroke on the sheet, so it spends half its weight
            // inside the segment and half outside. Padding the whole width outward instead - the
            // same slip the coil ink had - grew every bead by half a stroke on every side, and
            // on four stacked segments that reads as a rattle that has puffed up. Half out, half
            // in, and the silhouette comes back to the size it was drawn at.
            const float Half = (InkFine + 2f) * 0.5f;
            Ring(Half);
            c.Fill(at, ink with { W = ink.W * alpha });
            Ring(-Half);
            c.Fill(at, Tint(accent, AccBase) with { W = alpha });
        }
    }

    private static Vector2 HeadGeo(Channels ch)
    {
        var (path, _) = CoilPath(ch);
        var h = path[^1];
        return new Vector2(h.X, h.Y - (HeadR * HeadDrop));
    }

    public static Vector2 Anchor0(string name) => new(CX, BaseY);

    /// <summary>Every pin on this creature hangs off the HEAD, which is the far end of a coil
    /// whose shape depends on the whole pose - so unlike the other shells there is no useful
    /// neutral table to store. They are solved from the path each time.</summary>
    public static Vector2 Anchor(string name, Channels ch)
    {
        var head = HeadGeo(ch);
        return name switch
        {
            "head" => head + new Vector2(0f, -HeadR * 0.86f),
            "face" => head,
            "mouth" => head + new Vector2(0f, MouthDy),
            "handL" => head + new Vector2(-NubDx, HeadR * 0.62f),
            "handR" => head + new Vector2(NubDx, HeadR * 0.62f),
            "body" => Posed(ch).Pt(CX, BaseY - 40f),
            _ => head,
        };
    }

    /// <summary>The neutral head, cached: <see cref="HeadGeo"/> walks a 460-sample coil and a pin
    /// must not pay for that per frame, let alone per pin.</summary>
    private static Vector2? restHead;

    private static Vector2 RestHead => restHead ??= HeadGeo(LineShell.Neutral());

    /// <inheritdoc cref="JellyLineArt.Pin"/>
    ///
    /// <para>Everything on the face or the crown is CARRIED WITH THE HEAD, which on this shell is
    /// the far end of a coil and the only part that really travels. Held as the pin's own offset
    /// from the neutral head rather than as a stored constant, so it is exactly where the artist
    /// put it at rest and goes wherever the head goes after that. Only the mass - tails, hems,
    /// anything unrecognised - takes the coil's base transform instead.</para>
    public static Vector2 Pin(string name, PinKind kind, Vector2 rest, Channels ch) => kind switch
    {
        PinKind.Hand => Anchor(name, ch),
        PinKind.Body => Posed(ch).Pt2(rest),
        _ => HeadGeo(ch) + (rest - RestHead),
    };

    // -------------------------------------------------------------------- draw --

    /// <summary>One part of the creature, ink then fill, so the fill sits inside its own outline
    /// and the next part along covers both.</summary>
    private static void Part(LineCanvas c, Channels ch, int part, List<Vector2> path, List<int> seams, Vector4 body, Vector4 ink)
    {
        var n = path.Count;

        // The ink is an ANNULUS straddling the body's edge, not a rim hung outside it. The sheet
        // builds it by cutting each part's own fill back by INK_EDGE * 0.45 inside the ink stamp
        // at INK_EDGE * 0.55 outside - 4.5 in, 5.5 out, ten wide in total, the same weight the
        // head's outline carries. Laying ink at +5.5 and then fill at the FULL width, as the
        // first cut did, buries the inner 4.5 and leaves a line a little over half the weight it
        // should be. So the fill is pulled back to meet the ink rather than covering it.
        const float Pad = InkEdge * 0.55f;
        const float Cut = InkEdge * 0.45f;

        if (part == PartTail)
        {
            var tail = TailPath(ch, ch[(int)Ch.Shake]);
            var tn = tail.Count;
            c.Stamps(tail, 0, tn, i => TailW(i, tn), ink, Pad);
            c.Stamps(tail, 0, tn, i => TailW(i, tn), Tint(body, Base), -Cut);
            return;
        }

        int a, b;
        if (part == PartHead)
        {
            a = seams[^1];
            b = n;
        }
        else
        {
            a = part == PartOut ? 0 : seams[0];
            b = part == PartOut ? Math.Min(seams[0] + 3, n) : Math.Min(seams[^1] + 3, n);
        }

        c.Stamps(path, a, b, i => WidthAt(i, n), ink, Pad);
        c.Stamps(path, a, b, i => WidthAt(i, n), Tint(body, Base), -Cut);
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
        c.Begin(dl, bottomCentre, displaySize, Cell, outer, flip);
        var (path, seams) = CoilPath(ch);
        var n = path.Count;

        // The four parts, in the order the architecture demands. Ink and fill together per part,
        // which is what the sheet's per-part masks buy it.
        Part(c, ch, PartTail, path, seams, body, ink);

        // The rattle, on the end of the tail. The blur ghost goes down FIRST and at the opposite
        // throw, so the real rattle lands on top of its own smear rather than under it.
        var blur = Math.Clamp(ch[(int)Ch.Blur], 0f, 1f);
        if (blur > 0.01f)
        {
            Rattle_(c, TailPath(ch, -ch[(int)Ch.Shake] * 1.5f), accent, ink, 0.42f * blur);
        }

        Rattle_(c, TailPath(ch, ch[(int)Ch.Shake]), accent, ink, 1f);

        Part(c, ch, PartOut, path, seams, body, ink);

        // Dorsal cross-BANDS on the outer turn: bands rather than blotches, and DARKER than the
        // body rather than lighter - round spots read as a dalmatian, and a marking paler than
        // what it sits on reads as a highlight. They also give the coil's turns something to be
        // measured against, which is most of what was missing when it read as a smooth mound.
        Markings(c, path, 0, Math.Min(seams[0] + 3, n), n, trimCh, accent);

        Part(c, ch, PartIn, path, seams, body, ink);
        Markings(c, path, seams[0], Math.Min(seams[^1] + 3, n), n, trimCh, accent);

        Part(c, ch, PartHead, path, seams, body, ink);

        // The head: a shadow disc with the lit fill inset up-and-left, clipped to it, plus the
        // rim catch down the lit side.
        var head = HeadGeo(ch);
        var hrx = HeadR * 1.06f;
        c.Ellipse(head, hrx, HeadR, Tint(body, Shadow));
        c.EllipseIn(
            head - new Vector2(HeadR * 0.10f, HeadR * 0.09f),
            hrx - 7f, HeadR - 7f, head, hrx, HeadR, Tint(body, Base));

        c.MoveTo(head + new Vector2(-HeadR * 0.52f, -HeadR * 0.58f));
        c.CubicTo(
            head + new Vector2(-HeadR * 0.76f, -HeadR * 0.38f),
            head + new Vector2(-HeadR * 0.86f, -HeadR * 0.10f),
            head + new Vector2(-HeadR * 0.88f, HeadR * 0.12f));
        c.Stroke(Tint(body, Rim), 6f, closed: false);

        // The crest: the head's one accent mark, fill and outline literally the same path.
        CrestPath(c, head);
        c.Fill(head + new Vector2(0f, -HeadR + 2f), Tint(accent, AccBase));

        foreach (var side in new[] { -1, 1 })
        {
            c.Ellipse(head + new Vector2(side * NubDx, HeadR * 0.62f), NubR, NubR, Tint(body, NubFill));
        }

        if (blush > 0f)
        {
            foreach (var side in new[] { -1, 1 })
            {
                c.Ellipse(head + new Vector2(side * (EyeDx + 24f), HeadR * 0.30f), 11f, 7f, BlushTint(blush));
            }
        }

        // -- the ink the head carries on top of the stamped silhouette.
        c.EllipseStroke(head, hrx, HeadR, ink, InkEdge);
        CrestPath(c, head);
        c.Stroke(ink, InkFine + 1f);

        foreach (var side in new[] { -1, 1 })
        {
            c.EllipseStroke(head + new Vector2(side * NubDx, HeadR * 0.62f), NubR, NubR, ink, InkFine + 1f);
        }

        DrawEyes(c, Rig, eye, side => head + new Vector2(side * EyeDx, HeadR * 0.06f), 1f, 1f, eyeTint, ink);
    }

    private static void Markings(LineCanvas c, List<Vector2> path, int from, int to, int n, Channels trim, Vector4 accent)
    {
        const int Every = 34;
        for (var i = from; i < to; i += Every)
        {
            var at = path[i];
            var pv = path[Math.Max(from, i - 3)];
            var nx = path[Math.Min(to - 1, i + 3)];
            var ang = MathF.Atan2(nx.Y - pv.Y, nx.X - pv.X);
            var w = WidthAt(i, n);
            var mid = at - new Vector2(0f, w * 0.22f);

            var xf = new LocalXf(mid, 1f, 1f, ang * 180f / MathF.PI);
            c.MoveTo(xf.To(-w * 0.30f, 0f));
            c.QuadTo(xf.To(-w * 0.30f, -w * 0.66f), xf.To(0f, -w * 0.66f));
            c.QuadTo(xf.To(w * 0.30f, -w * 0.66f), xf.To(w * 0.30f, 0f));
            c.QuadTo(xf.To(w * 0.30f, w * 0.66f), xf.To(0f, w * 0.66f));
            c.QuadTo(xf.To(-w * 0.30f, w * 0.66f), xf.To(-w * 0.30f, 0f));
            c.Fill(mid, Tint(accent, AccShadow));
        }
    }

    private static void CrestPath(LineCanvas c, Vector2 head)
    {
        c.MoveTo(head + new Vector2(-5f, -HeadR + 3f));
        c.CubicTo(
            head + new Vector2(-10f, -HeadR - 26f),
            head + new Vector2(7f, -HeadR - 30f),
            head + new Vector2(10f, -HeadR - 8f));
        c.CubicTo(
            head + new Vector2(8f, -HeadR - 2f),
            head + new Vector2(0f, -HeadR),
            head + new Vector2(-5f, -HeadR + 3f));
    }
}
