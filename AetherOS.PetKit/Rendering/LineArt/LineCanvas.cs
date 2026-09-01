namespace AetherOS.PetKit.Rendering.LineArt;

using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;

/// <summary>
/// The pose a line-art shell is drawn under: squash about the hem, then lift, which is the
/// same three numbers the foundry's generators take (`sx`, `sy`, `dy`) and the same pivot.
///
/// <para>Kept identical to <c>build_sheet.py</c>'s <c>Q</c> on purpose. The sheets bake this
/// transform into cells; a line-art shell applies it live. If the two ever disagree the shell
/// will read as a different creature at the same moment of the same clip, and the bug will
/// look like an art problem rather than an arithmetic one.</para>
///
/// <para>Eyes take HALF the deform (`ex`/`ey`), which is the foundry's rule and worth keeping
/// for the reason it gives: a hard flatten should read on the body without turning the face
/// into a letterbox.</para>
/// </summary>
public readonly struct LinePose
{
    public LinePose(float sx, float sy, float dy, float cx, float hem)
    {
        this.Sx = sx;
        this.Sy = sy;
        this.Dy = dy;
        this.Cx = cx;
        this.Hem = hem;
        this.Ex = 1f + ((sx - 1f) * 0.5f);
        this.Ey = 1f + ((sy - 1f) * 0.5f);
    }

    public float Sx { get; }

    public float Sy { get; }

    public float Dy { get; }

    public float Cx { get; }

    public float Hem { get; }

    public float Ex { get; }

    public float Ey { get; }

    /// <summary>Linear blend between two poses. This is the whole reason a drawn shell can be
    /// continuous where a sheet cannot: the generator samples the pose curve at 38 cells and
    /// bakes each one, and here the samples are just keys to read between.</summary>
    public static LinePose Lerp(LinePose a, LinePose b, float t)
    {
        var u = Math.Clamp(t, 0f, 1f);
        return new LinePose(
            a.Sx + ((b.Sx - a.Sx) * u),
            a.Sy + ((b.Sy - a.Sy) * u),
            a.Dy + ((b.Dy - a.Dy) * u),
            a.Cx,
            a.Hem);
    }

    public float X(float x) => this.Cx + ((x - this.Cx) * this.Sx);

    public float Y(float y) => this.Hem + ((y - this.Hem) * this.Sy) + this.Dy;

    public Vector2 Pt(float x, float y) => new(this.X(x), this.Y(y));

    /// <summary>The same, for a point already assembled: a shell whose parts come from a master
    /// drawing hands these about rather than pairs of floats.</summary>
    public Vector2 Pt2(Vector2 p) => new(this.X(p.X), this.Y(p.Y));

    public Vector2 EyePt(float x, float y) => new(
        this.Cx + ((x - this.Cx) * this.Ex),
        this.Hem + ((y - this.Hem) * this.Ey) + this.Dy);
}

/// <summary>
/// A nested transform: a part authored in its OWN frame, placed into the shell's.
///
/// <para>The Crab's pincer needs this and the Jelly did not. The jaw is drawn once pointing up,
/// around its own origin, then mirrored per side, scaled and tilted inward, which is exactly
/// what SVG's <c>translate(t) scale(s) rotate(a)</c> does in the generator, and it composes
/// right to left, so a point rotates first, then scales, then moves. Kept as a value rather than
/// as canvas state because a shell may want several at once and a push/pop stack would be one
/// more thing to get wrong.</para>
/// </summary>
public readonly struct LocalXf
{
    private readonly Vector2 at;
    private readonly float sx;
    private readonly float sy;
    private readonly float cos;
    private readonly float sin;

    public LocalXf(Vector2 at, float scaleX, float scaleY, float degrees)
    {
        this.at = at;
        this.sx = scaleX;
        this.sy = scaleY;
        var r = degrees * MathF.PI / 180f;
        this.cos = MathF.Cos(r);
        this.sin = MathF.Sin(r);
    }

    public Vector2 To(float x, float y)
    {
        var rx = (x * this.cos) - (y * this.sin);
        var ry = (x * this.sin) + (y * this.cos);
        return this.at + new Vector2(rx * this.sx, ry * this.sy);
    }
}

/// <summary>
/// A little path builder in a shell's own authoring space, drawing into an ImGui draw list.
///
/// <para><b>Why this exists rather than ImGui's own path calls.</b> Two reasons, and both are
/// about control. The curve sampling is ours, so a shell tessellates the same way at every
/// size and a bezier does not quietly gain or lose segments when the pet is drawn small,
/// which is exactly the sort of thing that makes a drawn creature shimmer when it scales. And
/// <see cref="Fill"/> is a triangle fan from a supplied interior point rather than
/// <c>PathFillConvex</c>, because the shapes here are NOT convex: a jellyfish bell with a
/// scalloped hem dips inward five times, and a convex fill would bridge straight across every
/// scallop and hand back a shape nobody drew. A fan is correct for any shape that is
/// star-shaped about its centre, which every shell body on the roster is.</para>
///
/// <para>Coordinates are the shell's authoring space (the foundry's cell, 384 for the shells
/// built so far), so geometry ports across from <c>build_sheet.py</c> unchanged. The mapping
/// to screen is one multiply, which is what makes this resolution independent: there is no
/// cell, no texel and no size at which the art was authored.</para>
/// </summary>
public sealed class LineCanvas
{
    private readonly List<Vector2> points = new(128);
    private readonly List<Vector2> left = new(32);
    private readonly List<Vector2> right = new(32);

    /// <summary>Scratch for <see cref="EllipseAnd"/>: the shared boundary, with each point's
    /// angle about the centroid beside it so the sort needs no closure.</summary>
    private readonly List<(float A, Vector2 P)> lens = new(96);

    private static readonly Comparison<(float A, Vector2 P)> ByAngle = (p, q) => p.A.CompareTo(q.A);

    private ImDrawListPtr dl;
    private Vector2 origin;
    private float scale;
    private float sx;
    private float sy;
    private float cell;
    private bool flip;

    /// <summary>The live clip rects, mirrored off <see cref="PushClip"/>/<see cref="PopClip"/> so
    /// the selfie recorder can cut recorded geometry the way the GPU cuts the drawn one. Only the
    /// recorder reads it; the draw list carries its own stack.</summary>
    private readonly List<(Vector2 Min, Vector2 Max)> clipStack = new(4);

    /// <summary>Scratch for recorded geometry, so recording allocates only while a selfie is
    /// actually being taken.</summary>
    private readonly List<Vector2> record = new(64);

    /// <summary>How finely a curve is chopped. Twelve is comfortably past the point where a
    /// dome reads as round at pet size and cheap enough that a whole shell is still a handful
    /// of primitives.</summary>
    private const int CurveSteps = 12;

    /// <summary>Begins a shell. <paramref name="bottomCentre"/> is where the authoring space's
    /// bottom-centre lands on screen, and <paramref name="displaySize"/> is how many pixels
    /// the cell spans: the same two arguments <c>PetDraw</c> takes, so a drawn shell drops
    /// into any surface that already draws a sheet one.
    ///
    /// <para><paramref name="outer"/> is the controller's own code-side deform (the boop pop,
    /// the hop stretch) and it is applied HERE, about the cell's bottom centre, because that is
    /// exactly what <c>PetDraw.LocalToScreen</c> does to the sheet quad and to every anchor.
    /// Folding it into the shell's pose instead would squash about the hem, which is a different
    /// pivot, and a drawn body would then sit a few units off the pins meant to be riding it,
    /// which is what made the mouth swim against the face.</para></summary>
    public void Begin(ImDrawListPtr drawList, Vector2 bottomCentre, float displaySize, float cell, Vector2 outer, bool flip = false)
    {
        this.dl = drawList;
        var ds = displaySize / cell;
        this.sx = ds * outer.X;
        this.sy = ds * outer.Y;
        this.scale = ds * (outer.X + outer.Y) * 0.5f;
        this.origin = bottomCentre;
        this.cell = cell;
        this.flip = flip;
        this.points.Clear();
        this.clipStack.Clear();
    }

    public float Scale => this.scale;

    /// <summary>Authoring space to screen. The mirror happens HERE rather than at each call
    /// site, so a shell's geometry is written once facing one way and every derived point
    /// (curve samples, fan origins, clip corners) turns with it for free.</summary>
    public Vector2 To(Vector2 p)
    {
        if (this.flip)
        {
            p.X = this.cell - p.X;
        }

        return this.origin + new Vector2(
            (p.X - (this.cell * 0.5f)) * this.sx,
            (p.Y - this.cell) * this.sy);
    }

    public Vector2 To(float x, float y) => this.To(new Vector2(x, y));

    /// <summary>Adds a screen-space point, skipping one that lands on top of the last.
    ///
    /// <para><b>This guard is the whole reason the skirt used to flicker.</b> A polyline with two
    /// coincident points has a zero-length segment in it, and a stroker has to normalise that
    /// segment to work out which way the joint turns. Normalising a zero vector gives garbage,
    /// and garbage that is recomputed every frame from a pose that is moving gives DIFFERENT
    /// garbage every frame, so the joint sprays a stray spike in a new direction each time,
    /// which reads as a flicker pinned to one spot on the drawing. The bell closes exactly onto
    /// its own start point, so it had one of these every single frame.</para></summary>
    private void Push(Vector2 screen)
    {
        if (this.points.Count > 0 && Vector2.DistanceSquared(this.points[^1], screen) < 0.01f)
        {
            return;
        }

        this.points.Add(screen);
    }

    public void MoveTo(Vector2 p)
    {
        this.points.Clear();
        this.points.Add(this.To(p));
    }

    public void LineTo(Vector2 p) => this.Push(this.To(p));

    /// <summary>Cubic bezier, sampled. Matches SVG's <c>C</c>.</summary>
    public void CubicTo(Vector2 c1, Vector2 c2, Vector2 to)
    {
        var from = this.points.Count > 0 ? this.points[^1] : this.To(c1);
        var a = this.To(c1);
        var b = this.To(c2);
        var d = this.To(to);
        for (var i = 1; i <= CurveSteps; i++)
        {
            var t = (float)i / CurveSteps;
            var s = 1f - t;
            this.Push(
                (from * (s * s * s))
                + (a * (3f * s * s * t))
                + (b * (3f * s * t * t))
                + (d * (t * t * t)));
        }
    }

    /// <summary>Quadratic bezier, sampled. Matches SVG's <c>Q</c>, which is what the scalloped
    /// hems are written in.</summary>
    public void QuadTo(Vector2 c, Vector2 to)
    {
        var from = this.points.Count > 0 ? this.points[^1] : this.To(c);
        var a = this.To(c);
        var b = this.To(to);
        for (var i = 1; i <= CurveSteps; i++)
        {
            var t = (float)i / CurveSteps;
            var s = 1f - t;
            this.Push((from * (s * s)) + (a * (2f * s * t)) + (b * (t * t)));
        }
    }

    /// <summary>Fills the current path as a fan from <paramref name="centre"/> (authoring
    /// space). The centre must see every point on the outline: true for a bell, a shell or a
    /// dome, and the reason this is a fan and not a convex fill.
    ///
    /// <para><b>Antialiasing is turned off for the fan, and that is the fix for the spokes.</b>
    /// ImGui antialiases every triangle INDEPENDENTLY, feathering all three of its edges, so a
    /// fan of triangles that share edges gets a half-transparent seam down every shared edge,
    /// and a filled shape comes out looking like a cut pizza. Tiling the triangles hard makes
    /// them meet exactly. The cost is a jagged outer edge, which costs nothing here because
    /// every filled shape in a line-art shell has its own ink stroke laid over that edge
    /// afterwards, and the stroke IS antialiased.</para></summary>
    public void Fill(Vector2 centre, Vector4 colour)
    {
        if (this.points.Count < 3)
        {
            return;
        }

        var mid = this.To(centre);
        var col = ImGui.ColorConvertFloat4ToU32(colour);

        var flags = this.dl.Flags;
        this.dl.Flags &= ~ImDrawListFlags.AntiAliasedFill;
        for (var i = 0; i < this.points.Count; i++)
        {
            var a = this.points[i];
            var b = this.points[(i + 1) % this.points.Count];
            this.dl.AddTriangleFilled(mid, a, b, col);
        }

        this.dl.Flags = flags;
        this.RecordFill(this.points, colour);
    }

    /// <summary>Fills the current path from its own CENTROID, for a shape whose interior point
    /// is not obvious: a wing lobe, a petal, a collar.
    ///
    /// <para>Picking the fan origin by hand is the trap: a point that looks inside a lobe on
    /// paper is easily outside the actual curve, and a fan from outside a shape sprays triangles
    /// across everything near it. The centroid of the sampled boundary is inside any lobe worth
    /// drawing and costs one pass.</para></summary>
    public void Fill(Vector4 colour)
    {
        if (this.points.Count < 3)
        {
            return;
        }

        var mid = Vector2.Zero;
        foreach (var p in this.points)
        {
            mid += p;
        }

        mid /= this.points.Count;
        this.FillFrom(mid, colour);
    }

    /// <summary>Fills the current path, with every boundary point pulled back to wherever it
    /// leaves an ellipse: a path clipped to a body, which is what an accent painted ON a
    /// creature needs so a squash cannot slide it off the shape it belongs to.</summary>
    public void FillIn(Vector2 clipC, float clipRx, float clipRy, Vector4 colour)
    {
        if (this.points.Count < 3)
        {
            return;
        }

        var cc = this.To(clipC);
        var crx = clipRx * this.sx;
        var cry = clipRy * this.sy;

        var mid = Vector2.Zero;
        foreach (var p in this.points)
        {
            mid += p;
        }

        mid /= this.points.Count;

        for (var i = 0; i < this.points.Count; i++)
        {
            var t = ExitT(mid, this.points[i], cc, crx, cry);
            this.points[i] = mid + ((this.points[i] - mid) * t);
        }

        this.FillFrom(mid, colour);
    }

    /// <summary>A copy of the current path in SCREEN space, to clip a later shape against.</summary>
    public Vector2[] Capture() => this.points.ToArray();

    /// <summary>Fills the current path clipped to a captured one: a lit inset on a shape that is
    /// not an ellipse, which is the case <see cref="EllipseIn"/> cannot serve.
    ///
    /// <para>The Moth needed it and measurement is why: its lit inner wing is the outer wing
    /// shrunk 8% and lifted 6, and lifting a lobe raises its top edge faster than shrinking pulls
    /// it in: 41 of 81 sampled points end up outside the wing they are painted on, overshooting
    /// the top by the full 6. The sheet gets away with a sliver of that under its own outline;
    /// drawn at any size the rest shows.</para></summary>
    public void FillInPoly(Vector2[] clip, Vector4 colour)
    {
        if (this.points.Count < 3 || clip.Length < 3)
        {
            return;
        }

        var mid = Vector2.Zero;
        foreach (var p in this.points)
        {
            mid += p;
        }

        mid /= this.points.Count;

        for (var i = 0; i < this.points.Count; i++)
        {
            this.points[i] = ClampToPoly(mid, this.points[i], clip);
        }

        this.FillFrom(mid, colour);
    }

    /// <summary>Pulls <paramref name="to"/> back to where the segment from <paramref name="from"/>
    /// crosses the polygon, if it crosses at all.</summary>
    private static Vector2 ClampToPoly(Vector2 from, Vector2 to, Vector2[] poly)
    {
        // If the ray STARTS outside the shape there is nothing to clamp toward: the first
        // crossing along it is the far side coming in, and using it would paint a band across
        // open air. Collapse instead, so an extreme pose loses the mark rather than smearing it.
        if (!InPoly(from, poly))
        {
            return from;
        }

        var best = 1f;
        var d = to - from;
        for (var i = 0; i < poly.Length; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Length];
            var e = b - a;
            var den = (d.X * e.Y) - (d.Y * e.X);
            if (MathF.Abs(den) < 1e-6f)
            {
                continue;
            }

            var w = a - from;
            var t = ((w.X * e.Y) - (w.Y * e.X)) / den;
            var u = ((w.X * d.Y) - (w.Y * d.X)) / den;
            if (t > 1e-4f && t < best && u >= 0f && u <= 1f)
            {
                best = t;
            }
        }

        return from + (d * best);
    }

    public static bool InPoly(Vector2 p, Vector2[] poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y)
                && p.X < (((poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y)) + poly[i].X))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private void FillFrom(Vector2 mid, Vector4 colour)
    {
        var col = ImGui.ColorConvertFloat4ToU32(colour);
        var flags = this.dl.Flags;
        this.dl.Flags &= ~ImDrawListFlags.AntiAliasedFill;
        for (var i = 0; i < this.points.Count; i++)
        {
            this.dl.AddTriangleFilled(mid, this.points[i], this.points[(i + 1) % this.points.Count], col);
        }

        this.dl.Flags = flags;
        this.RecordFill(this.points, colour);
    }

    /// <summary>Strokes the current path. Width is in authoring units, so ink keeps its
    /// weight relative to the creature at every size.</summary>
    public void Stroke(Vector4 colour, float width, bool closed = true)
    {
        if (this.points.Count < 2)
        {
            return;
        }

        // A closed stroke must not carry the start point twice: the segment between them is
        // zero-length and the join built on it is the flicker described on Push.
        var count = this.points.Count;
        if (closed && count > 2
            && Vector2.DistanceSquared(this.points[0], this.points[count - 1]) < 0.01f)
        {
            count--;
        }

        for (var i = 0; i < count; i++)
        {
            this.dl.PathLineTo(this.points[i]);
        }

        var px = MathF.Max(1f, width * this.scale);
        this.dl.PathStroke(
            ImGui.ColorConvertFloat4ToU32(colour),
            closed ? ImDrawFlags.Closed : ImDrawFlags.None,
            px);
        this.RecordStroke(this.points, closed, px, colour);

        if (!closed)
        {
            this.Cap(this.points[0], px, colour);
            this.Cap(this.points[count - 1], px, colour);
        }
    }

    /// <summary>A round cap on an open stroke. ImGui strokes with BUTT caps and has no option
    /// for anything else, so an open line ends in a flat edge cut square across it, which on a
    /// 12-unit ink line reads as a little blade sticking out of the drawing, and is why the
    /// foundry's SVG says <c>stroke-linecap="round"</c> on every open path it owns. A disc of
    /// the stroke's own radius at each end is the same thing for one call.</summary>
    private void Cap(Vector2 at, float pxWidth, Vector4 colour)
    {
        if (pxWidth <= 1.5f)
        {
            return;
        }

        this.dl.AddCircleFilled(at, pxWidth * 0.5f, ImGui.ColorConvertFloat4ToU32(colour), 12);
        this.RecordDisc(at, pxWidth * 0.5f, colour);
    }

    /// <summary>Records one disc as a twelve-gon; the recorder speaks polygons, not circles.</summary>
    private void RecordDisc(Vector2 at, float r, Vector4 colour)
    {
        if (!PetFrameRecorder.Recording || r <= 0.3f)
        {
            return;
        }

        var poly = new List<Vector2>(12);
        for (var i = 0; i < 12; i++)
        {
            var a = MathF.Tau * i / 12f;
            poly.Add(at + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r));
        }

        this.RecordFill(poly, colour);
    }

    /// <summary>An ellipse, which the shells use constantly (eyes, markings, beads) and which
    /// ImGui has no primitive for.
    ///
    /// <para>Convex, so this one CAN go through <c>PathFillConvex</c>, which antialiases the
    /// whole outline once instead of feathering every triangle, and is why an eye drawn this way
    /// comes out as a disc rather than as a cut pizza. Anything that is not convex has to use
    /// <see cref="Fill"/> and give up its edge AA to the ink instead.</para></summary>
    public void Ellipse(Vector2 centre, float rx, float ry, Vector4 colour, int segments = 24)
    {
        var mid = this.To(centre);
        var recording = PetFrameRecorder.Recording ? new List<Vector2>(segments) : null;
        for (var i = 0; i < segments; i++)
        {
            var a = MathF.Tau * i / segments;
            var p = mid + new Vector2(
                MathF.Cos(a) * rx * this.sx,
                MathF.Sin(a) * ry * this.sy);
            this.dl.PathLineTo(p);
            recording?.Add(p);
        }

        this.dl.PathFillConvex(ImGui.ColorConvertFloat4ToU32(colour));
        if (recording is not null)
        {
            this.RecordFill(recording, colour);
        }
    }

    /// <summary>Fills an ellipse CLIPPED to another one: the shape a lit inset on a round body
    /// actually is, and the shape a cast shadow from the turn in front actually is.
    ///
    /// <para>Both come up the moment a shell is built from overlapping discs, and both are
    /// clip-paths on the sheet. Without one, the lit inset spills past the silhouette on the side
    /// it is offset toward (the Nautilus showed fill outside its own outline at the top left) and
    /// a cast shadow cannot be drawn at all, because it would land outside the turn it belongs
    /// to. Shrinking the inset instead is not the same picture: the sheet keeps a wide crescent
    /// on the far side AND a hard stop on the near one, and only a clip gives both.</para>
    ///
    /// <para>Built by ray-casting from the inner centre: each boundary point is pulled back to
    /// wherever the ray leaves the clip ellipse, if it leaves it at all. Exact for the case that
    /// matters (an inner shape mostly inside an outer one) and it degrades to the plain ellipse
    /// when nothing is cut.</para></summary>
    public void EllipseIn(Vector2 centre, float rx, float ry, Vector2 clipC, float clipRx, float clipRy, Vector4 colour, int segments = 40)
    {
        var mid = this.To(centre);
        var col = ImGui.ColorConvertFloat4ToU32(colour);
        var flags = this.dl.Flags;
        this.dl.Flags &= ~ImDrawListFlags.AntiAliasedFill;

        var recording = PetFrameRecorder.Recording ? new List<Vector2>(segments) : null;
        var prev = Vector2.Zero;
        for (var i = 0; i <= segments; i++)
        {
            var a = MathF.Tau * i / segments;
            var edge = centre + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
            var t = ExitT(centre, edge, clipC, clipRx, clipRy);
            var p = this.To(centre + ((edge - centre) * t));
            if (i > 0)
            {
                this.dl.AddTriangleFilled(mid, prev, p, col);
            }

            recording?.Add(p);
            prev = p;
        }

        this.dl.Flags = flags;
        if (recording is not null)
        {
            this.RecordFill(recording, colour);
        }
    }

    /// <summary>Fills the region two ellipses SHARE: the shape a tone on a UNION body actually
    /// is, and the first primitive this canvas has needed that is about two shapes rather than
    /// one shape and a mask.
    ///
    /// <para><b>Why <see cref="EllipseIn"/> cannot serve it.</b> That one ray-casts outward from
    /// the inner shape's own centre, so it needs that centre to be inside the clip; where it is
    /// not, <see cref="ExitT"/> hands back 0 and the mark silently disappears. The Grumble's dark
    /// base is one flat band spanning the whole creature, clipped to each low lobe in turn, and
    /// the band's centre is outside five of the six lobes it is painted on. Every one of them
    /// would have come out blank.</para>
    ///
    /// <para>Built from the two boundaries instead: the arc of each ellipse that lies inside the
    /// other, ordered by angle about the centroid of whatever survives. Both ellipses are convex
    /// and so is the region they share, which is what makes the angular sort the right order and
    /// the fan fill afterwards correct.</para></summary>
    public void EllipseAnd(Vector2 a, float aRx, float aRy, Vector2 b, float bRx, float bRy, Vector4 colour, int segments = 40)
    {
        this.lens.Clear();
        for (var i = 0; i < segments; i++)
        {
            var t = MathF.Tau * i / segments;
            var cos = MathF.Cos(t);
            var sin = MathF.Sin(t);
            var pa = a + new Vector2(cos * aRx, sin * aRy);
            if (InEllipse(pa, b, bRx, bRy))
            {
                this.lens.Add((0f, pa));
            }

            var pb = b + new Vector2(cos * bRx, sin * bRy);
            if (InEllipse(pb, a, aRx, aRy))
            {
                this.lens.Add((0f, pb));
            }
        }

        if (this.lens.Count < 3)
        {
            return;
        }

        var mid = Vector2.Zero;
        foreach (var (_, p) in this.lens)
        {
            mid += p;
        }

        mid /= this.lens.Count;
        for (var i = 0; i < this.lens.Count; i++)
        {
            var p = this.lens[i].P;
            this.lens[i] = (MathF.Atan2(p.Y - mid.Y, p.X - mid.X), p);
        }

        this.lens.Sort(ByAngle);

        var screenMid = this.To(mid);
        var col = ImGui.ColorConvertFloat4ToU32(colour);
        var flags = this.dl.Flags;
        this.dl.Flags &= ~ImDrawListFlags.AntiAliasedFill;
        for (var i = 0; i < this.lens.Count; i++)
        {
            this.dl.AddTriangleFilled(
                screenMid,
                this.To(this.lens[i].P),
                this.To(this.lens[(i + 1) % this.lens.Count].P),
                col);
        }

        this.dl.Flags = flags;
        if (PetFrameRecorder.Recording)
        {
            var recording = new List<Vector2>(this.lens.Count);
            foreach (var (_, p) in this.lens)
            {
                recording.Add(this.To(p));
            }

            this.RecordFill(recording, colour);
        }
    }

    private static bool InEllipse(Vector2 p, Vector2 c, float rx, float ry)
    {
        var dx = (p.X - c.X) / MathF.Max(0.0001f, rx);
        var dy = (p.Y - c.Y) / MathF.Max(0.0001f, ry);
        return (dx * dx) + (dy * dy) <= 1f;
    }

    /// <summary>How far along <c>from -> to</c> the ray is still inside the clip ellipse, as a
    /// fraction in [0, 1]. 1 when the whole segment is inside.</summary>
    private static float ExitT(Vector2 from, Vector2 to, Vector2 c, float rx, float ry)
    {
        var ax = (from.X - c.X) / MathF.Max(0.0001f, rx);
        var ay = (from.Y - c.Y) / MathF.Max(0.0001f, ry);
        var bx = (to.X - from.X) / MathF.Max(0.0001f, rx);
        var by = (to.Y - from.Y) / MathF.Max(0.0001f, ry);

        var qa = (bx * bx) + (by * by);
        var qb = 2f * ((ax * bx) + (ay * by));
        var qc = (ax * ax) + (ay * ay) - 1f;
        if (qc >= 0f || qa <= 1e-6f)
        {
            return 0f; // started outside, or nowhere to go
        }

        var disc = (qb * qb) - (4f * qa * qc);
        if (disc <= 0f)
        {
            return 1f;
        }

        var t = (-qb + MathF.Sqrt(disc)) / (2f * qa);
        return Math.Clamp(t, 0f, 1f);
    }

    /// <summary>Builds an ellipse as a PATH, so it can be filled, clipped or stroked like any
    /// other: <see cref="EllipseIn"/> only clips to another ellipse, and a body is often not
    /// one.</summary>
    public void EllipsePath(Vector2 centre, float rx, float ry, int segments = 40)
    {
        for (var i = 0; i < segments; i++)
        {
            var a = MathF.Tau * i / segments;
            var p = centre + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
            if (i == 0)
            {
                this.MoveTo(p);
            }
            else
            {
                this.LineTo(p);
            }
        }
    }

    /// <summary>The same ellipse as an outline: the eye ring, and the one stroke that gives a
    /// drawn face most of its character.</summary>
    public void EllipseStroke(Vector2 centre, float rx, float ry, Vector4 colour, float width, int segments = 24)
    {
        var mid = this.To(centre);
        var recording = PetFrameRecorder.Recording ? new List<Vector2>(segments) : null;
        for (var i = 0; i < segments; i++)
        {
            var a = MathF.Tau * i / segments;
            var p = mid + new Vector2(
                MathF.Cos(a) * rx * this.sx,
                MathF.Sin(a) * ry * this.sy);
            this.dl.PathLineTo(p);
            recording?.Add(p);
        }

        var px = MathF.Max(1f, width * this.scale);
        this.dl.PathStroke(ImGui.ColorConvertFloat4ToU32(colour), ImDrawFlags.Closed, px);
        if (recording is not null)
        {
            this.RecordStroke(recording, closed: true, px, colour);
        }
    }

    /// <summary>An arc of a circle, swept between two angles. What the hand roots want: their
    /// ink closes the silhouette on the OUTER side only, because a full ring would cross the
    /// body outline and the two together read as a lens rather than as a shoulder.</summary>
    public void Arc(Vector2 centre, float r, float from, float to, Vector4 colour, float width, int segments = 16)
    {
        // To() mirrors the CENTRE but cannot mirror a sweep, because the offsets below are
        // built from angles rather than from authoring points. So the x component turns here.
        var mid = this.To(centre);
        var xs = this.flip ? -1f : 1f;
        Vector2 On(float a) => mid + new Vector2(
            MathF.Cos(a) * r * this.sx * xs,
            MathF.Sin(a) * r * this.sy);

        var recording = PetFrameRecorder.Recording ? new List<Vector2>(segments + 1) : null;
        for (var i = 0; i <= segments; i++)
        {
            var p = On(from + ((to - from) * i / segments));
            this.dl.PathLineTo(p);
            recording?.Add(p);
        }

        var apx = MathF.Max(1f, width * this.scale);
        var first = On(from);
        var last = On(to);
        this.dl.PathStroke(ImGui.ColorConvertFloat4ToU32(colour), ImDrawFlags.None, apx);
        if (recording is not null)
        {
            this.RecordStroke(recording, closed: false, apx, colour);
        }

        this.Cap(first, apx, colour);
        this.Cap(last, apx, colour);
    }

    /// <summary>Clips to everything below <paramref name="y"/> in authoring space: the lid. A
    /// lidded eye is a straight horizontal cut across the eye contents, so a rect clip is the
    /// honest shape for it rather than something that squashes the eye and changes what it is
    /// doing.</summary>
    public void PushLidClip(float y, Vector2 centre, float rx, float ry)
    {
        this.PushClip(
            new Vector2(centre.X - (rx * 2f), y),
            new Vector2(centre.X + (rx * 2f), centre.Y + (ry * 3f)));
    }

    /// <summary>The CLOSED outline of a tapered limb along a cubic: the shape a claw, an arm
    /// or a tendril is. Ported from <c>sheetkit.band</c>, which exists for a reason worth
    /// repeating: the obvious way to draw a limb is to stroke its centreline thickly, and a
    /// sheet cannot, because the ink lives on a later layer and stroking the centreline again
    /// there puts a line down the MIDDLE of the limb rather than around it. A closed outline can
    /// be filled by one layer and inked by another.
    ///
    /// <para>Drawn shells inherit that shape rather than the constraint, and it is still the
    /// right one: it is how the ink on a claw comes out the same weight as the ink on the shell
    /// beside it, and it is what <see cref="TentacleFx"/> already builds in C# for the strands.
    /// Fills as a STRIP between the two offset polylines rather than as a fan, because a curved
    /// limb is not star-shaped about any single point.</para></summary>
    public void Band(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float w0, float w1, int steps = 18)
    {
        this.left.Clear();
        this.right.Clear();

        Vector2 At(float t)
        {
            var u = 1f - t;
            return (p0 * (u * u * u)) + (p1 * (3f * u * u * t)) + (p2 * (3f * u * t * t)) + (p3 * (t * t * t));
        }

        for (var i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            var here = At(t);
            var a = At(MathF.Max(0f, t - (1f / steps)));
            var b = At(MathF.Min(1f, t + (1f / steps)));
            var d = b - a;
            var n = d.Length();
            var nrm = n < 1e-4f ? new Vector2(0f, 1f) : new Vector2(-d.Y / n, d.X / n);
            var w = (w0 + ((w1 - w0) * t)) * 0.5f;
            this.left.Add(here + (nrm * w));
            this.right.Add(here - (nrm * w));
        }

        // The closed outline, for stroking: down one side and back the other.
        this.points.Clear();
        foreach (var q in this.left)
        {
            this.Push(this.To(q));
        }

        for (var i = this.right.Count - 1; i >= 0; i--)
        {
            this.Push(this.To(this.right[i]));
        }
    }

    /// <summary>The same band, built along a SAMPLED polyline rather than a cubic.
    ///
    /// <para>The Pennant needs this and no earlier shell did, for the reason its whole body is
    /// authored as samples: every point on that cloth takes the travelling wave at its OWN depth,
    /// so there is no cubic to walk - a control point has no depth to be swayed at. A mark on it
    /// is a run of measured points, and it still has to fill as a strip and clip as one.</para></summary>
    public void BandPath(System.Collections.Generic.IReadOnlyList<Vector2> centre, float w0, float w1)
    {
        this.left.Clear();
        this.right.Clear();
        if (centre.Count < 2)
        {
            return;
        }

        for (var i = 0; i < centre.Count; i++)
        {
            var a = centre[Math.Max(0, i - 1)];
            var b = centre[Math.Min(centre.Count - 1, i + 1)];
            var d = b - a;
            var n = d.Length();
            var nrm = n < 1e-4f ? new Vector2(0f, 1f) : new Vector2(-d.Y / n, d.X / n);
            var w = (w0 + ((w1 - w0) * i / (float)(centre.Count - 1))) * 0.5f;
            this.left.Add(centre[i] + (nrm * w));
            this.right.Add(centre[i] - (nrm * w));
        }

        this.points.Clear();
        foreach (var q in this.left)
        {
            this.Push(this.To(q));
        }

        for (var i = this.right.Count - 1; i >= 0; i--)
        {
            this.Push(this.To(this.right[i]));
        }
    }

    /// <summary>A band given as its two EDGES rather than as a centreline and a width.
    ///
    /// <para>For the region between two curves that are not parallel - the Wisp's pool, which is
    /// bounded above by a horizon and below by the creature's own bottom, and is three times
    /// thicker at the sides than in the middle. Neither a fan nor a constant width strip can
    /// draw that, and it does not need clipping either: built out of the profile the shell is
    /// built from, it cannot leave the body it is on.</para></summary>
    public void BandEdges(System.Collections.Generic.IReadOnlyList<Vector2> a, System.Collections.Generic.IReadOnlyList<Vector2> b)
    {
        this.left.Clear();
        this.right.Clear();
        var n = Math.Min(a.Count, b.Count);
        for (var i = 0; i < n; i++)
        {
            this.left.Add(a[i]);
            this.right.Add(b[i]);
        }

        this.points.Clear();
        foreach (var q in this.left)
        {
            this.Push(this.To(q));
        }

        for (var i = this.right.Count - 1; i >= 0; i--)
        {
            this.Push(this.To(this.right[i]));
        }
    }

    /// <summary>Fills the band built by the last <see cref="Band"/> call.</summary>
    public void FillBand(Vector4 colour)
    {
        if (this.left.Count < 2)
        {
            return;
        }

        var col = ImGui.ColorConvertFloat4ToU32(colour);
        var flags = this.dl.Flags;
        this.dl.Flags &= ~ImDrawListFlags.AntiAliasedFill;
        for (var i = 0; i < this.left.Count - 1; i++)
        {
            Vector2 a = this.To(this.left[i]), b = this.To(this.left[i + 1]);
            Vector2 c = this.To(this.right[i]), d = this.To(this.right[i + 1]);
            this.dl.AddTriangleFilled(a, b, c, col);
            this.dl.AddTriangleFilled(b, d, c, col);
        }

        this.dl.Flags = flags;
        if (PetFrameRecorder.Recording)
        {
            var recording = new List<Vector2>(this.left.Count * 2);
            foreach (var q in this.left)
            {
                recording.Add(this.To(q));
            }
            for (var i = this.right.Count - 1; i >= 0; i--)
            {
                recording.Add(this.To(this.right[i]));
            }

            this.RecordFill(recording, colour);
        }
    }

    /// <summary>A run of overlapping discs along a path: a STAMPED tube, which is how a snake's
    /// body is drawn rather than as an outline with a fill.
    ///
    /// <para>The trick that makes it line art is that the ink is the same run at a larger radius,
    /// laid down FIRST: each part's fill then sits inside its own outline, and the next part
    /// along the path covers the ink of the one behind it. The sheet needs a mask per part to get
    /// that; walked in path order it falls out of the drawing, which is why the generator walks
    /// its coil "from the BACK of the ellipse so that within every turn the far half is laid down
    /// before the near half".</para></summary>
    public void Stamps(System.Collections.Generic.IReadOnlyList<Vector2> path, int from, int to, Func<int, float> radius, Vector4 colour, float pad = 0f, int step = 2)
    {
        var col = ImGui.ColorConvertFloat4ToU32(colour);
        var flags = this.dl.Flags;
        this.dl.Flags &= ~ImDrawListFlags.AntiAliasedFill;

        for (var i = from; i < to; i += step)
        {
            var r = (radius(i) + pad) * this.scale;
            if (r > 0.2f)
            {
                // Segment count follows the RADIUS. At a fixed 16 the discs are visibly
                // polygonal once the pet is drawn large, and because a stamped tube is the union
                // of overlapping discs those flats land at different angles along the run - so
                // the outline reads as a line whose weight wanders rather than as a clean edge.
                var seg = Math.Clamp((int)(r * 0.9f) + 8, 12, 48);
                this.dl.AddCircleFilled(this.To(path[i]), r, col, seg);
                this.RecordDisc(this.To(path[i]), r, colour);
            }
        }

        this.dl.Flags = flags;
    }

    /// <summary>Fills the band built by the last <see cref="Band"/> call, CUT to a captured path.
    ///
    /// <para>The band has to keep its strip fill to be cut correctly, and that is the whole point
    /// of this existing beside <see cref="FillInPoly"/>. A band is a long thin curved thing, and
    /// a fan from its centroid folds it: rays to the far ends cross outside the shape and the
    /// middle collapses into a V - the same fault the Nautilus's accent band had when its outline
    /// walked back on itself. Filled as a strip it cannot fold, whatever the curve does.</para>
    ///
    /// <para>Each pair of edge points is pulled back toward its own point on the CENTRELINE, so
    /// clipping trims the band's WIDTH where it leaves the body rather than shortening its
    /// length.</para></summary>
    public void FillBandIn(Vector2[] clip, Vector4 colour)
    {
        if (this.left.Count < 2 || clip.Length < 3)
        {
            return;
        }

        var col = ImGui.ColorConvertFloat4ToU32(colour);
        var flags = this.dl.Flags;
        this.dl.Flags &= ~ImDrawListFlags.AntiAliasedFill;

        var ls = PetFrameRecorder.Recording ? new List<Vector2>(this.left.Count) : null;
        var rs = ls is null ? null : new List<Vector2>(this.right.Count);
        Vector2 pa = default, pc = default;
        for (var i = 0; i < this.left.Count; i++)
        {
            var l = this.To(this.left[i]);
            var r = this.To(this.right[i]);
            var mid = (l + r) * 0.5f;
            l = ClampToPoly(mid, l, clip);
            r = ClampToPoly(mid, r, clip);
            ls?.Add(l);
            rs?.Add(r);

            if (i > 0)
            {
                this.dl.AddTriangleFilled(pa, l, pc, col);
                this.dl.AddTriangleFilled(l, r, pc, col);
            }

            pa = l;
            pc = r;
        }

        this.dl.Flags = flags;
        if (ls is not null && rs is not null)
        {
            for (var i = rs.Count - 1; i >= 0; i--)
            {
                ls.Add(rs[i]);
            }

            this.RecordFill(ls, colour);
        }
    }

    /// <summary>Clips to a rectangle in authoring space. Normalised after mapping, because a
    /// mirrored transform hands back the corners the other way round and PushClipRect wants a
    /// true min and max: passed inverted it clips everything away.</summary>
    public void PushClip(Vector2 topLeft, Vector2 bottomRight)
    {
        var a = this.To(topLeft);
        var b = this.To(bottomRight);
        var min = new Vector2(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y));
        var max = new Vector2(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
        this.dl.PushClipRect(min, max, true);
        this.clipStack.Add((min, max));
    }

    public void PopClip()
    {
        this.dl.PopClipRect();
        if (this.clipStack.Count > 0)
        {
            this.clipStack.RemoveAt(this.clipStack.Count - 1);
        }
    }

    /// <summary>Records one filled polygon for the selfie compositor, cut to the live clip
    /// rects. The screen already showed this shape; the recorder replays it offline, so the cut
    /// has to be repeated here or a lidded eye records wide open.</summary>
    private void RecordFill(IReadOnlyList<Vector2> poly, Vector4 colour)
    {
        if (!PetFrameRecorder.Recording || poly.Count < 3 || colour.W <= 0f)
        {
            return;
        }

        this.record.Clear();
        this.record.AddRange(poly);
        foreach (var (min, max) in this.clipStack)
        {
            ClipPolyToRect(this.record, min, max);
            if (this.record.Count < 3)
            {
                return;
            }
        }

        PetFrameRecorder.Add(this.record, closed: true, thickness: 0f, ImGui.ColorConvertFloat4ToU32(colour));
    }

    /// <summary>Records one stroked polyline for the selfie compositor. Under a clip only the
    /// runs of points inside every rect survive; a stroke that crosses the lid records as its
    /// visible pieces, endpoints clamped where a segment leaves the rect.</summary>
    private void RecordStroke(IReadOnlyList<Vector2> pts, bool closed, float pxWidth, Vector4 colour)
    {
        if (!PetFrameRecorder.Recording || pts.Count < 2 || colour.W <= 0f)
        {
            return;
        }

        var packed = ImGui.ColorConvertFloat4ToU32(colour);
        if (this.clipStack.Count == 0)
        {
            PetFrameRecorder.Add(pts, closed, pxWidth, packed);
            return;
        }

        var count = closed ? pts.Count + 1 : pts.Count;
        this.record.Clear();
        for (var i = 0; i < count - 1; i++)
        {
            var a = pts[i % pts.Count];
            var b = pts[(i + 1) % pts.Count];
            if (!ClipSegment(ref a, ref b))
            {
                Flush();
                continue;
            }

            if (this.record.Count == 0 || Vector2.DistanceSquared(this.record[^1], a) > 0.01f)
            {
                Flush();
                this.record.Add(a);
            }

            this.record.Add(b);
        }

        Flush();

        void Flush()
        {
            if (this.record.Count >= 2)
            {
                PetFrameRecorder.Add(this.record, closed: false, pxWidth, packed);
            }

            this.record.Clear();
        }
    }

    /// <summary>Liang-Barsky against every live clip rect: clamps the segment to the visible
    /// part, or answers false when nothing of it shows.</summary>
    private bool ClipSegment(ref Vector2 a, ref Vector2 b)
    {
        foreach (var (min, max) in this.clipStack)
        {
            var t0 = 0f;
            var t1 = 1f;
            var d = b - a;
            if (!ClipEdge(-d.X, a.X - min.X, ref t0, ref t1) || !ClipEdge(d.X, max.X - a.X, ref t0, ref t1)
                || !ClipEdge(-d.Y, a.Y - min.Y, ref t0, ref t1) || !ClipEdge(d.Y, max.Y - a.Y, ref t0, ref t1))
            {
                return false;
            }

            var from = a + (d * t0);
            var to = a + (d * t1);
            a = from;
            b = to;
        }

        return true;

        static bool ClipEdge(float p, float q, ref float t0, ref float t1)
        {
            if (p == 0f)
            {
                return q >= 0f;
            }

            var r = q / p;
            if (p < 0f)
            {
                if (r > t1)
                {
                    return false;
                }
                if (r > t0)
                {
                    t0 = r;
                }
            }
            else
            {
                if (r < t0)
                {
                    return false;
                }
                if (r < t1)
                {
                    t1 = r;
                }
            }

            return true;
        }
    }

    /// <summary>Sutherland-Hodgman against one rect, in place.</summary>
    private static void ClipPolyToRect(List<Vector2> poly, Vector2 min, Vector2 max)
    {
        ClipHalf(poly, p => p.X >= min.X, (a, b) => LerpAtX(a, b, min.X));
        ClipHalf(poly, p => p.X <= max.X, (a, b) => LerpAtX(a, b, max.X));
        ClipHalf(poly, p => p.Y >= min.Y, (a, b) => LerpAtY(a, b, min.Y));
        ClipHalf(poly, p => p.Y <= max.Y, (a, b) => LerpAtY(a, b, max.Y));

        static Vector2 LerpAtX(Vector2 a, Vector2 b, float x) =>
            new(x, a.Y + ((b.Y - a.Y) * ((x - a.X) / (b.X - a.X))));

        static Vector2 LerpAtY(Vector2 a, Vector2 b, float y) =>
            new(a.X + ((b.X - a.X) * ((y - a.Y) / (b.Y - a.Y))), y);

        static void ClipHalf(List<Vector2> poly, Func<Vector2, bool> inside, Func<Vector2, Vector2, Vector2> cross)
        {
            if (poly.Count < 3)
            {
                return;
            }

            var src = poly.ToArray();
            poly.Clear();
            for (var i = 0; i < src.Length; i++)
            {
                var a = src[i];
                var b = src[(i + 1) % src.Length];
                var aIn = inside(a);
                var bIn = inside(b);
                if (aIn)
                {
                    poly.Add(a);
                }
                if (aIn != bIn)
                {
                    poly.Add(cross(a, b));
                }
            }
        }
    }
}
