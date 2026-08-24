using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AetherOS.Apps.Aetherling.Engine;

/// <summary>Which family a glyph belongs to (GlyphSpec §4.3). The register decides nothing
/// mechanical; it groups the library for the Wardrobe and the dev sheet, and it is the honest
/// answer to "what is this glyph about".</summary>
public enum GlyphRegister
{
    Feeling,
    Element,
    World,
    Thing,
    Social,
}

/// <summary>Where a glyph's fill colour comes from (GlyphSpec §5.3). Colour is the second
/// axis: one drawn glyph yields every colour meaning free, exactly as one drawn body yields
/// every palette.</summary>
public enum GlyphTint
{
    /// <summary>Ink only, no fill. The two permitted marks (§4.2) and the pure-stroke
    /// shapes — a mark is not an object, so it is never coloured.</summary>
    None,

    /// <summary>The pet's own accent colour, so its feelings are literally in its colours
    /// and every palette ever sold makes this surface newly personal.</summary>
    Accent,

    /// <summary>The element's own colour, from the game's colour language (Affinities).</summary>
    Element,

    /// <summary>A muted pale neutral: the pet is reporting, not feeling.</summary>
    Neutral,
}

/// <summary>
/// One path inside a glyph. Fills are CONVEX ONLY (<c>PathFillConvex</c>, the ParticleFx and
/// MouthDraw idiom), so a concave symbol is built as a stroked outline over convex fill
/// pieces: the outline carries the ink and the pieces carry the colour, and every seam
/// between pieces is interior and therefore invisible. Points are in a 100-unit box, y down.
///
/// <para><b><see cref="Layer"/> is the occlusion rule, and line art does not work without
/// it.</b> Within a layer the renderer draws every fill and then every stroke, which is what
/// keeps a multi-piece fill seamless under one outline. But a glyph assembled from several
/// *objects* needs the opposite: a palm drawn over the roots of its own fingers, a notehead
/// over the foot of its stem, a hat brim over the base of its crown. Flatten those into one
/// layer and each object's outline saws straight through the one behind it — which is exactly
/// the fault the first Hand shipped with. Higher layers draw later, each as its own
/// fill-then-stroke pass, so a shape in front genuinely hides what it covers.</para>
/// </summary>
public readonly record struct GlyphPath(Vector2[] Points, bool Closed, bool Fill, bool Stroke, float Weight = 1f, int Layer = 0);

/// <summary>
/// A glyph as geometry (GlyphSpec §7.1): one bold shape plus at most two internal strokes,
/// drawn to survive 30 px rather than to look good at 200. No sheet, no cells, no VRAM — the
/// entire asset is the numbers below, which is why a new glyph is a preset rather than an
/// art-intake round.
/// </summary>
public readonly record struct GlyphShape(
    string Name,
    GlyphRegister Register,
    GlyphTint Tint,

    /// <summary>Element key when <see cref="Tint"/> is <see cref="GlyphTint.Element"/>;
    /// empty otherwise. A Show may override it (the crystal wears whatever was eaten).</summary>
    string Element,

    GlyphPath[] Paths);

/// <summary>
/// The canonical glyph library (GlyphSpec §4.3): thirty entries in five registers, every one
/// owned by a system that already exists — no new state is invented anywhere in this file.
///
/// <para>The rules that keep it honest, restated where they are enforced: <b>no letters, no
/// digits, no words, no punctuation</b>, with exactly two exceptions — <c>!</c> and <c>?</c>,
/// permitted because FFXIV's own quest markers use those two marks over NPC heads, so they
/// read here as marks rather than as text. The cap is the load-bearing half: two is a
/// register, three is a slippery slope to a text box.</para>
///
/// <para>Shaped after <see cref="MouthShapes"/> deliberately: presets plus an alias map, call
/// by meaning, unknown names fall back rather than break, and the library owns no clock.</para>
/// </summary>
public static class GlyphShapes
{
    // ------------------------------------------------------------------ builders

    private static Vector2[] Poly(params float[] xy)
    {
        var pts = new Vector2[xy.Length / 2];
        for (var i = 0; i < pts.Length; i++)
        {
            pts[i] = new Vector2(xy[i * 2], xy[(i * 2) + 1]);
        }

        return pts;
    }

    private static Vector2[] Circle(float cx, float cy, float r, int segments = 28)
    {
        var pts = new Vector2[segments];
        for (var i = 0; i < segments; i++)
        {
            var a = MathF.Tau * i / segments;
            pts[i] = new Vector2(cx + (MathF.Cos(a) * r), cy + (MathF.Sin(a) * r));
        }

        return pts;
    }

    private static Vector2[] Ellipse(float cx, float cy, float rx, float ry, int segments = 28)
    {
        var pts = new Vector2[segments];
        for (var i = 0; i < segments; i++)
        {
            var a = MathF.Tau * i / segments;
            pts[i] = new Vector2(cx + (MathF.Cos(a) * rx), cy + (MathF.Sin(a) * ry));
        }

        return pts;
    }

    /// <summary>Arc samples, degrees, clockwise in screen space (y down).</summary>
    private static Vector2[] Arc(float cx, float cy, float r, float fromDeg, float toDeg, int segments = 18)
    {
        var pts = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var a = (fromDeg + ((toDeg - fromDeg) * i / segments)) * MathF.PI / 180f;
            pts[i] = new Vector2(cx + (MathF.Cos(a) * r), cy + (MathF.Sin(a) * r));
        }

        return pts;
    }

    /// <summary>A sine ribbon — the mellow waves, and cheap enough to sample rather than
    /// author.</summary>
    private static Vector2[] Wave(float y, float amp, float x0, float x1, float cycles, int segments = 20)
    {
        var pts = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var u = (float)i / segments;
            pts[i] = new Vector2(x0 + ((x1 - x0) * u), y + (MathF.Sin(u * MathF.Tau * cycles) * amp));
        }

        return pts;
    }

    private static Vector2[] Spiral(float cx, float cy, float r0, float r1, float turns, int segments = 40)
    {
        var pts = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var u = (float)i / segments;
            var a = u * MathF.Tau * turns;
            var r = r0 + ((r1 - r0) * u);
            pts[i] = new Vector2(cx + (MathF.Cos(a) * r), cy + (MathF.Sin(a) * r));
        }

        return pts;
    }

    private static Vector2[] Move(Vector2[] pts, float dx, float dy)
    {
        var copy = new Vector2[pts.Length];
        for (var i = 0; i < pts.Length; i++)
        {
            copy[i] = new Vector2(pts[i].X + dx, pts[i].Y + dy);
        }

        return copy;
    }

    private static Vector2[] Sized(Vector2[] pts, float scale, float dx, float dy)
    {
        var copy = new Vector2[pts.Length];
        for (var i = 0; i < pts.Length; i++)
        {
            copy[i] = new Vector2((pts[i].X * scale) + dx, (pts[i].Y * scale) + dy);
        }

        return copy;
    }

    private static GlyphPath Ink(Vector2[] pts, bool closed = true, float weight = 1f)
        => new(pts, closed, false, true, weight);

    private static GlyphPath Solid(Vector2[] pts)
        => new(pts, true, true, false, 1f);

    /// <summary>Moves a group of paths onto a drawing layer — the occlusion rule of
    /// <see cref="GlyphPath.Layer"/>, applied to a whole object at once so a hand's palm can
    /// be put in front of its own fingers in one line.</summary>
    private static GlyphPath[] Layered(int layer, params GlyphPath[] paths)
    {
        var copy = new GlyphPath[paths.Length];
        for (var i = 0; i < paths.Length; i++)
        {
            copy[i] = paths[i] with { Layer = layer };
        }

        return copy;
    }

    /// <summary>A stadium: a thick line with round ends, as a closed convex polygon. What
    /// turns a finger from an ink STROKE into a drawn object with its own outline, which is
    /// the difference between line art and a felt-tip drawing.</summary>
    private static Vector2[] Capsule(float x0, float y0, float x1, float y1, float r, int segments = 10)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        if ((dx * dx) + (dy * dy) < 0.0001f)
        {
            return Circle(x0, y0, r);
        }

        var deg = MathF.Atan2(dy, dx) * 180f / MathF.PI;
        var far = Arc(x1, y1, r, deg - 90f, deg + 90f, segments);
        var near = Arc(x0, y0, r, deg + 90f, deg + 270f, segments);
        var pts = new Vector2[far.Length + near.Length];
        Array.Copy(far, pts, far.Length);
        Array.Copy(near, 0, pts, far.Length, near.Length);
        return pts;
    }

    private static Vector2[] Reverse(Vector2[] pts)
    {
        var copy = new Vector2[pts.Length];
        for (var i = 0; i < pts.Length; i++)
        {
            copy[i] = pts[pts.Length - 1 - i];
        }

        return copy;
    }

    private static Vector2[] Concat(Vector2[] a, Vector2[] b)
    {
        var pts = new Vector2[a.Length + b.Length];
        Array.Copy(a, pts, a.Length);
        Array.Copy(b, 0, pts, a.Length, b.Length);
        return pts;
    }

    /// <summary>Outline and fill from one convex path — the common case, and the only one
    /// where the fill can safely be the outline itself.</summary>
    private static GlyphPath[] Convex(Vector2[] pts, params GlyphPath[] extra)
    {
        var paths = new GlyphPath[2 + extra.Length];
        paths[0] = Solid(pts);
        paths[1] = Ink(pts);
        Array.Copy(extra, 0, paths, 2, extra.Length);
        return paths;
    }

    /// <summary>
    /// Fills a star-shaped outline exactly, as a triangle fan from its centroid. Every
    /// triangle is convex by construction, and unlike a covering circle-and-wedge
    /// approximation it can never bulge past the outline and swallow a notch — which is what
    /// a flame's shoulder is, and the whole reason a flame is not just a droplet.
    /// </summary>
    private static GlyphPath[] Fan(Vector2[] outline)
    {
        var centre = Vector2.Zero;
        foreach (var p in outline)
        {
            centre += p;
        }

        centre /= outline.Length;

        var tris = new GlyphPath[outline.Length];
        for (var i = 0; i < outline.Length; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Length];
            var along = (b - a) * SeamOverlap;
            tris[i] = Solid([centre, a - along, b + along]);
        }

        return tris;
    }

    /// <summary>
    /// How far each piece of a multi-part fill reaches past its neighbour, as a fraction of
    /// the edge it shares with them.
    ///
    /// <para>Without it, adjacent fills that share an exact edge leave a visible <b>seam</b>:
    /// both <c>PathFillConvex</c> and GDI+ antialias each polygon independently, so the two
    /// half-covered fringes along the join composite to about three quarters of the colour
    /// and the shared edge shows as a pale line. That is what turned the cloud into a
    /// pinwheel the moment its fill became exact — which is also why the sloppy old
    /// circles-and-a-rectangle version never showed one: it overlapped everywhere.</para>
    ///
    /// <para>The overshoot at the OUTER boundary is what buys this, and it is affordable
    /// because the outline stroke is ~8.5 units wide in the same 100-unit box and centred on
    /// the path: it hides ±4 units. A few hundredths of a short edge is nowhere near that.</para>
    /// </summary>
    private const float SeamOverlap = 0.035f;

    /// <summary>An outline filled by <see cref="Fan"/> and then inked — the concave sibling of
    /// <see cref="Convex"/>, and the right builder for anything with a notch in it.</summary>
    private static GlyphPath[] Notched(Vector2[] outline, params GlyphPath[] extra)
        => Join(Fan(outline), [Ink(outline)], extra);

    /// <summary>A filled band between two sampled arcs, as small convex quads. What makes the
    /// crescents fillable at all: a crescent is concave, but every quad across its width is
    /// not.</summary>
    private static GlyphPath[] Band(Vector2[] outer, Vector2[] inner)
    {
        var n = Math.Min(outer.Length, inner.Length) - 1;
        var quads = new GlyphPath[n];
        for (var i = 0; i < n; i++)
        {
            // Same overlap as Fan, and for the same reason: the radial joins between quads
            // are exact shared edges, and exact shared edges antialias into visible seams.
            var outAlong = (outer[i + 1] - outer[i]) * SeamOverlap;
            var inAlong = (inner[i + 1] - inner[i]) * SeamOverlap;
            quads[i] = Solid([
                outer[i] - outAlong,
                outer[i + 1] + outAlong,
                inner[i + 1] + inAlong,
                inner[i] - inAlong,
            ]);
        }

        return quads;
    }

    private static GlyphPath[] Join(params GlyphPath[][] groups)
    {
        var total = 0;
        foreach (var g in groups)
        {
            total += g.Length;
        }

        var all = new GlyphPath[total];
        var at = 0;
        foreach (var g in groups)
        {
            Array.Copy(g, 0, all, at, g.Length);
            at += g.Length;
        }

        return all;
    }

    // A four-point star: a concave outline over four convex kites (centre, inner, tip, inner).
    private static GlyphPath[] Star(float cx, float cy, float outer, float inner)
    {
        Span<Vector2> tips =
        [
            new(cx, cy - outer), new(cx + outer, cy), new(cx, cy + outer), new(cx - outer, cy),
        ];
        var d = inner * 0.7071f;
        Span<Vector2> mids =
        [
            new(cx + d, cy - d), new(cx + d, cy + d), new(cx - d, cy + d), new(cx - d, cy - d),
        ];

        var outline = new Vector2[8];
        for (var i = 0; i < 4; i++)
        {
            outline[i * 2] = tips[i];
            outline[(i * 2) + 1] = mids[i];
        }

        var paths = new GlyphPath[5];
        for (var i = 0; i < 4; i++)
        {
            var prev = mids[(i + 3) % 4];
            paths[i] = Solid([new Vector2(cx, cy), prev, tips[i], mids[i]]);
        }

        paths[4] = Ink(outline);
        return paths;
    }

    /// <summary>The snowflake's side branches, generated rather than hand-placed: the first
    /// draft's six hand-typed V ticks each landed at a slightly wrong angle and the whole
    /// mark read as a tangle. By construction they cannot.</summary>
    private static GlyphPath[] Branches(float cx, float cy, float at, float length, int count, float spreadDeg)
    {
        var paths = new GlyphPath[count];
        var spread = spreadDeg * MathF.PI / 180f;
        for (var i = 0; i < count; i++)
        {
            var a = MathF.Tau * i / count;
            var root = new Vector2(cx + (MathF.Cos(a) * at), cy + (MathF.Sin(a) * at));
            var left = new Vector2(root.X + (MathF.Cos(a - spread) * length), root.Y + (MathF.Sin(a - spread) * length));
            var right = new Vector2(root.X + (MathF.Cos(a + spread) * length), root.Y + (MathF.Sin(a + spread) * length));
            paths[i] = Ink([left, root, right], false, 1.1f);
        }

        return paths;
    }

    /// <summary>Radiating spokes — the sun's rays and the snowflake's arms.</summary>
    private static GlyphPath[] Rays(float cx, float cy, float r0, float r1, int count, float weight = 1f)
    {
        var rays = new GlyphPath[count];
        for (var i = 0; i < count; i++)
        {
            var a = MathF.Tau * i / count;
            var dx = MathF.Cos(a);
            var dy = MathF.Sin(a);
            rays[i] = Ink([new Vector2(cx + (dx * r0), cy + (dy * r0)), new Vector2(cx + (dx * r1), cy + (dy * r1))], false, weight);
        }

        return rays;
    }

    // ------------------------------------------------------------------ the shapes

    private static readonly Vector2[] HeartOutline = Poly(
        50, 90, 74, 70, 88, 54, 92, 40, 88, 27, 78, 21, 66, 23, 57, 31, 50, 41,
        43, 31, 34, 23, 22, 21, 12, 27, 8, 40, 12, 54, 26, 70);

    private static readonly Vector2[] CrescentOuter = Arc(50, 50, 40, 56.85f, 303.15f, 24);

    // Through 180, not through 0: the inner boundary is the bite the small circle takes out of
    // the big one, so it is the small circle's NEAR side. Sampled the other way round the two
    // arcs run in opposite directions, index i of one sits opposite index i of the other, and
    // Band cheerfully fills the whole disc — which is precisely what the first pass drew.
    private static readonly Vector2[] CrescentInner = Arc(66, 50, 34, 80.05f, 279.95f, 24);

    private static readonly Vector2[] CloudOutline = Poly(
        22, 74, 13, 68, 11, 58, 18, 49, 28, 47, 32, 35, 44, 27, 58, 28, 68, 37, 71, 48,
        81, 49, 88, 57, 86, 68, 78, 74);

    /// <summary>The cloud, lifted to make room for the rain beneath it.</summary>
    private static readonly Vector2[] RainCloud = Move(CloudOutline, 0f, -14f);

    /// <summary>The library, in register order. Index is position here; call by name or alias.</summary>
    public static readonly GlyphShape[] All =
    [
        // ---------------------------------------------------------------- feeling (8)
        new("burst", GlyphRegister.Feeling, GlyphTint.Accent, "", Star(50, 50, 46, 24)),

        new("heart", GlyphRegister.Feeling, GlyphTint.Accent, "", Join(
            [Solid(Circle(29, 36, 21)), Solid(Circle(71, 36, 21)), Solid(Poly(10, 44, 90, 44, 50, 90))],
            [Ink(HeartOutline)])),

        new("ring", GlyphRegister.Feeling, GlyphTint.Accent, "", Convex(Circle(50, 50, 32))),

        new("waves", GlyphRegister.Feeling, GlyphTint.None, "", [
            Ink(Wave(38, 9, 12, 88, 1f), false),
            Ink(Wave(66, 9, 12, 88, 1f), false),
        ]),

        // ONE closed outline, not two open arcs. The angles are where the two circles actually
        // intersect (r 40 at x 50, r 34 at x 66 → ±56.85° on the outer, ±80.05° on the inner),
        // so the horns come to a point. Sampled at the same count in the same direction, the
        // two arcs also pair up index-wise for Band, which is what fills a concave shape.
        new("crescent", GlyphRegister.Feeling, GlyphTint.Accent, "", Join(
            Band(CrescentOuter, CrescentInner),
            [Ink(Concat(CrescentOuter, Reverse(CrescentInner)))])),

        // The two permitted marks (§4.2). Ink only, never coloured, never an alert.
        new("bang", GlyphRegister.Feeling, GlyphTint.None, "", [
            Ink(Poly(50, 12, 50, 60), false, 1.7f),
            Ink(Circle(50, 82, 6, 14), true, 1.3f),
        ]),

        new("query", GlyphRegister.Feeling, GlyphTint.None, "", [
            Ink(Poly(28, 33, 31, 21, 39, 13, 51, 11, 63, 15, 70, 25, 69, 35, 62, 44, 53, 50, 51, 58, 51, 64), false, 1.7f),
            Ink(Circle(51, 82, 6, 14), true, 1.3f),
        ]),

        new("swirl", GlyphRegister.Feeling, GlyphTint.None, "", [
            Ink(Spiral(50, 50, 5, 40, 1.9f), false, 1.3f),
        ]),

        // ---------------------------------------------------------------- element (8)
        // The shoulder notch on the left is the whole difference between a flame and a
        // droplet: the first draft was a teardrop with a lean, and it read as Water in a
        // warm colour. A flame licks.
        new("flame", GlyphRegister.Element, GlyphTint.Element, "fire", Notched(Poly(
            58, 4, 70, 26, 79, 46, 80, 64, 72, 81, 56, 92, 40, 91, 27, 81, 21, 64, 25, 47,
            37, 38, 45, 45, 46, 29, 50, 16))),

        new("snowflake", GlyphRegister.Element, GlyphTint.None, "ice", Join(
            Rays(50, 50, 0, 42, 6, 1.2f),
            Branches(50, 50, 26, 13, 6, 52f))),

        new("leaf", GlyphRegister.Element, GlyphTint.Element, "wind", Convex(
            Poly(84, 15, 79, 39, 66, 60, 47, 76, 25, 85, 20, 66, 28, 45, 46, 28, 66, 18),
            Ink(Poly(80, 20, 60, 43, 40, 62, 25, 84), false))),

        // Eight vertices rather than six: at six it was a tidy hexagon and read as a gem,
        // which is the crystal's job. A rock is lumpy.
        new("stone", GlyphRegister.Element, GlyphTint.Element, "earth", Convex(
            Poly(19, 70, 26, 44, 41, 27, 63, 22, 82, 38, 85, 62, 71, 84, 42, 88))),

        new("bolt", GlyphRegister.Element, GlyphTint.Element, "lightning", Join(
            [
                Solid(Poly(58, 4, 26, 52, 52, 40)),
                Solid(Poly(26, 52, 46, 52, 52, 40)),
                Solid(Poly(46, 52, 38, 96, 76, 40, 52, 40)),
            ],
            [Ink(Poly(58, 4, 26, 52, 46, 52, 38, 96, 76, 40, 52, 40))])),

        new("drop", GlyphRegister.Element, GlyphTint.Element, "water", Convex(Poly(
            50, 6, 64, 26, 76, 46, 80, 62, 74, 78, 60, 90, 40, 90, 26, 78, 20, 62, 24, 46, 36, 26))),

        // Light and dark are one shape twice, unfilled and filled — the pair reads as a pair,
        // which is exactly what the Alignment is (EvolutionSpec §2.3b).
        new("radiance", GlyphRegister.Element, GlyphTint.Element, "light", Join(
            [Solid(Circle(50, 50, 19)), Ink(Circle(50, 50, 19))],
            Rays(50, 50, 27, 44, 8, 1.2f))),

        new("umbra", GlyphRegister.Element, GlyphTint.Element, "dark", Join(
            [Solid(Circle(50, 50, 29)), Ink(Circle(50, 50, 29))],
            Rays(50, 50, 34, 45, 8, 1.2f))),

        // ---------------------------------------------------------------- world (4 + sun)
        // A chevron, not a cross: the first draft's crossed bars read as a crucifix on a
        // shield, which is a whole religion this app did not mean to import. A rank mark
        // says "the job you are playing" and says nothing else.
        new("jobmark", GlyphRegister.World, GlyphTint.Neutral, "", Convex(
            Poly(50, 9, 83, 22, 83, 52, 71, 77, 50, 93, 29, 77, 17, 52, 17, 22),
            Ink(Poly(36, 57, 50, 38, 64, 57), false),
            Ink(Poly(36, 71, 50, 52, 64, 71), false))),

        // Filled by a fan off its own outline, not by three circles and a rectangle standing in
        // for it. The stand-in was reported as "the colour clips outside the lines" and it was:
        // the rectangle ran x 12-88 where the outline's flat bottom runs 22-78, so ten units of
        // fill sat outside the ink at each corner, and the left circle overhung by four more.
        // A covering approximation is only ever as good as its worst edge; a fan is exact.
        new("cloud", GlyphRegister.World, GlyphTint.Neutral, "", Notched(CloudOutline)),

        new("rain", GlyphRegister.World, GlyphTint.Element, "water", Join(
            Notched(RainCloud),
            [
                Ink(Poly(32, 68, 26, 88), false, 1.2f),
                Ink(Poly(52, 68, 46, 88), false, 1.2f),
                Ink(Poly(72, 68, 66, 88), false, 1.2f),
            ])),

        new("gale", GlyphRegister.World, GlyphTint.None, "", [
            Ink(Poly(10, 31, 60, 31, 70, 29, 76, 22, 74, 14, 66, 10, 57, 13, 53, 20), false, 1.2f),
            Ink(Poly(10, 54, 72, 54, 83, 57, 89, 64, 86, 73, 78, 78, 69, 74, 65, 67), false, 1.2f),
            Ink(Poly(20, 76, 52, 76), false, 1.2f),
        ]),

        // ---------------------------------------------------------------- things (6)
        // A hexagonal bipyramid — pointed at BOTH ends, parallel sides between. The first
        // draft was a pentagon: wide across the shoulders, tapering to a narrow flat foot,
        // with a cross ruled over it. That is a coffin, reported as one, and it was: a shape
        // that narrows downward to a flat base reads as a casket in any silhouette, whatever
        // it is coloured. Points at both ends is what makes it a crystal instead.
        //
        // The two facet strokes run vertex to vertex — the pyramid's own converging edges,
        // then the central edge down to the foot. Terminating ON a vertex is what an edge
        // does; it is the strokes that stop in mid-air that read as unfinished (see core).
        new("crystal", GlyphRegister.Thing, GlyphTint.Element, "", Convex(
            Poly(50, 3, 68, 26, 68, 74, 50, 97, 32, 74, 32, 26),
            Ink(Poly(32, 26, 50, 42, 68, 26), false),
            Ink(Poly(50, 42, 50, 97), false))),

        // (Runestone was cut here on the fourth pass. It was a rounded lump with an inner ring,
        // and Stone is a rounded lump — two glyphs separated by one internal stroke, which the
        // legibility strip says is the first thing to die below ~37 px. A stone on the dock is
        // said with `core` now; the two were never far enough apart to be worth both.)

        // Crown behind, brim in FRONT of it: the brim hides the cone's base entirely, so the
        // crown rises out of the brim instead of sitting on a line drawn across it. The base
        // is narrowed to 32..68 so the ellipse's shallow shoulders still cover it — a wider
        // cone pokes out either side around y=55, where the brim has barely any width yet.
        // (Two drafts back this was an arc bowed off the cone's base and read as a traffic
        // cone with a smile.)
        new("hat", GlyphRegister.Thing, GlyphTint.Accent, "", Join(
            Layered(0, Convex(Poly(50, 8, 64, 40, 68, 60, 32, 60, 36, 40))),
            Layered(1, Solid(Ellipse(50, 64, 40, 10)), Ink(Ellipse(50, 64, 40, 10))))),

        // The currency's own mark. Not a letter, so it stays in.
        new("spark", GlyphRegister.Thing, GlyphTint.Accent, "", Star(50, 48, 46, 17)),

        // One band, not the crystal's cross: with both internal strokes the core and the
        // crystal were the same drawing at two sizes, and they mean very different things.
        // Proper gem anatomy rather than one floating bar: a girdle across the shoulders and a
        // pavilion converging below it, every stroke terminating on a vertex of the outline.
        // The previous single band was inset off both ends to keep it clear of the border,
        // which is how it ended up hanging in mid-air with nothing to meet — an unfinished
        // line, and reported as one. The rule the two passes together settle: an internal
        // stroke either meets a vertex or it should not be there.
        //
        // Rounder and wider than the crystal on purpose, and neutral rather than element-
        // tinted, so the food and the Aethercore never read as each other.
        new("core", GlyphRegister.Thing, GlyphTint.Neutral, "", Convex(
            Poly(50, 4, 74, 32, 67, 74, 50, 96, 33, 74, 26, 32),
            Ink(Poly(26, 32, 74, 32), false),
            Ink(Poly(33, 74, 50, 86, 67, 74), false))),

        // Stem behind, noteheads in front: the stem's two feet land on the heads' right edges
        // and are covered by them, the way a drawn note joins. On one layer each head's own
        // outline cut the stem off short of its own foot.
        new("note", GlyphRegister.Thing, GlyphTint.Accent, "", Join(
            Layered(0, Ink(Poly(40, 79, 40, 17, 83, 9, 83, 69), false, 1.3f)),
            Layered(1,
                Solid(Circle(27, 77, 13)), Ink(Circle(27, 77, 13)),
                Solid(Circle(70, 67, 13)), Ink(Circle(70, 67, 13))))),

        // ---------------------------------------------------------------- social (3)
        // Four fingers and a thumb as DRAWN OBJECTS — capsules with their own outlines — with
        // the palm on the layer above, covering every root. Two earlier drafts got this wrong
        // in opposite directions: the first ran the fingers as 2.4x ink strokes (a 20-unit
        // finger in a 100-unit box: a dark blob), and the second thinned them but left
        // everything on one layer, so the palm's own outline sawed a line straight across all
        // five. Fingers touch at the tips and overlap at the roots, which is what puts a line
        // BETWEEN them and no line THROUGH them.
        new("hand", GlyphRegister.Social, GlyphTint.Accent, "", Join(
            Layered(0,
                Solid(Capsule(34, 78, 16, 64, 7f)), Ink(Capsule(34, 78, 16, 64, 7f)),
                Solid(Capsule(33, 62, 28, 30, 6.5f)), Ink(Capsule(33, 62, 28, 30, 6.5f)),
                Solid(Capsule(44, 58, 42, 20, 6.5f)), Ink(Capsule(44, 58, 42, 20, 6.5f)),
                Solid(Capsule(56, 58, 59, 20, 6.5f)), Ink(Capsule(56, 58, 59, 20, 6.5f)),
                Solid(Capsule(67, 62, 72, 31, 6.5f)), Ink(Capsule(67, 62, 72, 31, 6.5f))),
            Layered(1, Solid(Circle(50, 70, 25)), Ink(Circle(50, 70, 25))))),

        // The near heart occludes the far one, which is the only thing that makes two of them
        // read as two rather than as one tangled outline.
        new("twohearts", GlyphRegister.Social, GlyphTint.Accent, "", Join(
            Layered(0,
                Solid(Sized(Circle(29, 36, 21), 0.62f, 4, 6)),
                Solid(Sized(Circle(71, 36, 21), 0.62f, 4, 6)),
                Solid(Sized(Poly(10, 44, 90, 44, 50, 90), 0.62f, 4, 6)),
                Ink(Sized(HeartOutline, 0.62f, 4, 6))),
            Layered(1,
                Solid(Sized(Circle(29, 36, 21), 0.5f, 45, 42)),
                Solid(Sized(Circle(71, 36, 21), 0.5f, 45, 42)),
                Solid(Sized(Poly(10, 44, 90, 44, 50, 90), 0.5f, 45, 42)),
                Ink(Sized(HeartOutline, 0.5f, 45, 42))))),

        new("links", GlyphRegister.Social, GlyphTint.None, "", [
            Ink(Circle(36, 50, 24)),
            Ink(Circle(66, 50, 24)),
        ]),
    ];

    /// <summary>
    /// The lookup half (§4.3): every trigger can say what it MEANS ("delight", "sleepy",
    /// "thanks") and the library stays twenty-nine shapes. Matched case-insensitively.
    /// Add an alias freely; add a shape only when no alias is honest.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // feeling
            ["delight"] = "burst", ["joy"] = "burst", ["sparkle"] = "burst", ["yes"] = "burst",
            ["love"] = "heart", ["affection"] = "heart", ["thanks"] = "heart",
            ["content"] = "ring", ["settled"] = "ring", ["calm"] = "ring",
            ["mellow"] = "waves", ["drift"] = "waves",
            ["sleepy"] = "crescent", ["dozy"] = "crescent", ["night"] = "crescent", ["moon"] = "crescent",
            ["surprise"] = "bang", ["notice"] = "bang", ["oh"] = "bang",
            ["puzzled"] = "query", ["curious"] = "query", ["unknown"] = "query",
            ["dizzy"] = "swirl", ["overwhelmed"] = "swirl",

            // element — the affinity keys resolve straight to their own glyph
            ["fire"] = "flame",
            ["ice"] = "snowflake", ["snow"] = "snowflake", ["frost"] = "snowflake",
            ["wind"] = "leaf", ["gust"] = "leaf",
            ["earth"] = "stone", ["rock"] = "stone",
            ["lightning"] = "bolt", ["levin"] = "bolt",
            ["water"] = "drop", ["droplet"] = "drop",
            ["light"] = "radiance", ["sun"] = "radiance", ["day"] = "radiance", ["clear"] = "radiance",
            ["dark"] = "umbra", ["shadow"] = "umbra",

            // world
            ["job"] = "jobmark", ["class"] = "jobmark",
            ["overcast"] = "cloud",
            ["rainy"] = "rain",
            ["wind-weather"] = "gale", ["breeze"] = "gale",

            // things
            ["food"] = "crystal", ["meal"] = "crystal",
            ["stone-item"] = "core", ["dock"] = "core",
            ["wardrobe"] = "hat", ["dressed"] = "hat",
            ["currency"] = "spark",
            ["aethercore"] = "core", ["egg"] = "core",
            ["music"] = "note", ["hum"] = "note", ["song"] = "note",

            // social
            ["hello"] = "hand", ["greet"] = "hand", ["wave"] = "hand", ["reached"] = "hand",
            ["friend"] = "twohearts",
            ["paired"] = "links", ["together"] = "links",
        };

    /// <summary>Does the library have this, by name or by alias? The strict half of
    /// <see cref="Find"/>, for the one caller that must not have the forgiving one: a name
    /// arriving from ANOTHER app (the prompt handle, <c>IAetherlingPrompts</c>) is refused
    /// rather than shown as a puzzled mark — our own typo is charming, someone else's is a
    /// bug they should see.</summary>
    public static bool Knows(string name) =>
        name.Length > 0
        && (Aliases.ContainsKey(name)
            || All.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Resolves a name or alias to its shape; an unknown name falls back to the
    /// query mark — a pet that visibly wonders what it was asked to say is charming, and a
    /// pet that shows nothing is a bug report.</summary>
    public static GlyphShape Find(string name)
    {
        if (Aliases.TryGetValue(name, out var canonical))
        {
            name = canonical;
        }

        foreach (var shape in All)
        {
            if (string.Equals(shape.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return shape;
            }
        }

        return All[6]; // "query"
    }

    /// <summary>The mood layer's own voice (KindlingSpec §1.5 → GlyphSpec §6.2). Only
    /// TRANSITIONS ever reach this: an ambient glyph marks a change, never a condition, or
    /// the mood quietly becomes the meter this app has promised never to draw.</summary>
    public static string ForMood(MoodLevel mood) => mood switch
    {
        MoodLevel.Beaming => "burst",
        MoodLevel.Bright => "heart",
        MoodLevel.Mellow => "waves",
        MoodLevel.Dozy or MoodLevel.Napping => "crescent",
        _ => string.Empty, // Content is the resting state, and rest says nothing.
    };
}

/// <summary>
/// The one glyph channel (GlyphSpec §7.2): a rise/hold/fall envelope over one shape, or over
/// two when it is a <b>saying</b>. One glyph at a time, ever — a second trigger REPLACES
/// rather than queues, so a mashed boop produces one delighted pet and not a backlog.
///
/// <para>Owned by the app beside the <see cref="MouthController"/> and handed to the renderer
/// each frame, so every surface shows the same glyph the way every surface reads the same
/// pose. Like the mouth library, this owns only its own envelope: WHEN a glyph is shown, and
/// how often, is the caller's business.</para>
/// </summary>
public sealed class GlyphController
{
    public const float RiseSeconds = 0.25f;
    public const float FallSeconds = 0.45f;

    /// <summary>How long a glyph is held. Generous on purpose: a symbol read at 37 px on a
    /// busy game screen needs longer than a UI designer's instinct (§6.5).</summary>
    public const float HoldSeconds = 2.2f;

    /// <summary>The first half of a saying holds shorter — it is a clause, not a sentence.</summary>
    public const float SayHoldSeconds = 1.05f;

    private const float SwapSeconds = 0.16f;

    private string[] names = [];
    private string[] tints = [];
    private int index;
    private int phase; // 0 rise, 1 hold, 2 fall
    private float clock;

    public bool Playing => this.names.Length > 0;

    /// <summary>This frame's glyph. Only meaningful while <see cref="Playing"/>.</summary>
    public GlyphShape Current { get; private set; }

    /// <summary>Element key overriding the shape's own, or empty. The crystal wears whatever
    /// was actually eaten, which is the whole reason this exists.</summary>
    public string CurrentElement { get; private set; } = string.Empty;

    public float Alpha { get; private set; }

    /// <summary>0→1 across the rise: the strokes draw themselves on, which is what makes a
    /// being of aether look like it is condensing a shape rather than opening a dialog.
    /// Pinned to 1 under reduce-motion.</summary>
    public float Reveal { get; private set; } = 1f;

    /// <summary>Settle offset in 256-space px, positive = lower. Suppressed under
    /// reduce-motion; the glyph still appears, because a shown symbol is a state and not a
    /// motion (the same reasoning that keeps the resting mouth working, EmoteStudy §10).</summary>
    public float Lift { get; private set; }

    /// <summary>
    /// Shows one glyph, or a two-glyph <b>saying</b> when <paramref name="then"/> is given.
    ///
    /// <para>Three rules govern a saying, none of them enforceable in code and none of them
    /// pretended to be — they are rules for whoever writes the trigger, checked in review like
    /// every other piece of copy:</para>
    /// <list type="number">
    /// <item><b>Tense.</b> A saying may narrate the present or the past. It may never point at
    /// the future. <c>crystal → burst</c> is thanks; <c>heart → crystal</c> is a demand, and
    /// there is no version of this app that ships it.</item>
    /// <item><b>Order: subject first, reaction second.</b> The thing that happened, then how
    /// the pet took it. Reversed it still parses, but it reads as commentary rather than as
    /// narration, and narration is the only register the tense rule leaves open.</item>
    /// <item><b>An element glyph is a SUBJECT, never a modifier of feeling.</b> Fire means the
    /// element, not anger; lightning means the element, not shock. The moment an affinity
    /// starts doubling as a mood, the leaning is a stat — which EvolutionSpec §2.4's
    /// never-touches column exists to prevent.</item>
    /// </list>
    /// </summary>
    public void Show(string name, string? then = null, string element = "")
    {
        this.names = then == null ? [name] : [name, then];
        this.tints = then == null ? [element] : [element, string.Empty];
        this.index = 0;
        this.phase = 0;
        this.clock = 0f;
        this.Apply();
    }

    public void Clear()
    {
        this.names = [];
        this.tints = [];
        this.Alpha = 0f;
        this.Lift = 0f;
    }

    public void Update(float dt, bool reduceMotion)
    {
        if (!this.Playing)
        {
            return;
        }

        this.clock += dt;
        var last = this.index >= this.names.Length - 1;
        var riseLen = this.index == 0 ? RiseSeconds : SwapSeconds;
        var holdLen = last ? HoldSeconds : SayHoldSeconds;
        var fallLen = last ? FallSeconds : SwapSeconds;

        switch (this.phase)
        {
            case 0 when this.clock >= riseLen:
                this.phase = 1;
                this.clock = 0f;
                break;
            case 1 when this.clock >= holdLen:
                this.phase = 2;
                this.clock = 0f;
                break;
            case 2 when this.clock >= fallLen:
                if (last)
                {
                    this.Clear();
                    return;
                }

                this.index++;
                this.phase = 0;
                this.clock = 0f;
                this.Apply();
                break;
        }

        // Re-read after a transition: the index may have moved to the saying's second glyph
        // this very frame, and its envelope is a different length from the first's.
        last = this.index >= this.names.Length - 1;
        riseLen = this.index == 0 ? RiseSeconds : SwapSeconds;
        fallLen = last ? FallSeconds : SwapSeconds;

        var t = this.phase switch
        {
            0 => Math.Clamp(this.clock / MathF.Max(0.01f, riseLen), 0f, 1f),
            1 => 1f,
            _ => 1f - Math.Clamp(this.clock / MathF.Max(0.01f, fallLen), 0f, 1f),
        };

        this.Alpha = Ease(t);
        this.Reveal = reduceMotion || this.phase != 0 ? 1f : Ease(this.clock / MathF.Max(0.01f, riseLen));

        // A small drop-and-settle on the way in, nothing on the way out: the glyph condenses
        // downward into place rather than sliding up out of the pet's head.
        if (reduceMotion || this.phase != 0)
        {
            this.Lift = 0f;
        }
        else
        {
            var p = Math.Clamp(this.clock / MathF.Max(0.01f, riseLen), 0f, 1f);
            this.Lift = -7f * (1f - Ease(p)) * MathF.Cos(p * 1.6f);
        }
    }

    private void Apply()
    {
        this.Current = GlyphShapes.Find(this.names[this.index]);
        var over = this.tints.Length > this.index ? this.tints[this.index] : string.Empty;
        this.CurrentElement = over.Length > 0 ? over : this.Current.Element;
        this.Alpha = 0f;
        this.Reveal = 0f;
    }

    private static float Ease(float t)
    {
        var x = Math.Clamp(t, 0f, 1f);
        return x * x * (3f - (2f * x));
    }
}
