using System;
using System.Numerics;
using AetherOS.Apps.Aetherling.Engine;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Aetherling.Rendering;

/// <summary>What the symbol appears in (GlyphSpec §5.2). Both ship; the light is the default
/// because a being of condensed aether condensing a shape in the air is the least jarring
/// thing we can put on a game screen, and the bubble is there because it is the legible,
/// FFXIV-native answer and some players will simply prefer it.</summary>
public enum GlyphFrame
{
    /// <summary>The glyph light: a soft aether disc, no geometry to clash with anything.</summary>
    Light,

    /// <summary>A rounded bubble with a tail, in the register FFXIV's own NPC chat bubbles
    /// live in. The tail is what it buys: authorship, unambiguously.</summary>
    Bubble,
}

/// <summary>
/// Renders one glyph and its frame (GlyphSpec §7.2): draw-list strokes and convex fills, the
/// same primitive budget as <see cref="MouthDraw"/> and the limb rig. No sheet, no cells, no
/// VRAM, and no localization — the whole asset is <see cref="GlyphShapes"/>.
/// </summary>
internal static class GlyphDraw
{
    /// <summary>Glyph centre above the head anchor, 256-space px. One figure for both frames
    /// on purpose: switching frame must never make the symbol jump.</summary>
    internal const float CentreLift256 = 70f;

    /// <summary>The drawn box, 256-space px (GlyphSpec §4.1: 96 units gives 50 px at the
    /// default floating size; the frame's own margin eats the rest).</summary>
    private const float Box256 = 88f;

    private const float LightRadius256 = 56f;
    private const float BubbleHalfW256 = 58f;
    private const float BubbleHalfH256 = 44f;
    private const float BubbleRound256 = 20f;

    /// <summary>Stroke weight in the glyph's own 100-unit box, before the path's own
    /// multiplier. Heavier than the mouth's because a glyph is read, not felt.</summary>
    private const float Stroke100 = 8.5f;

    /// <summary>
    /// Draws <paramref name="shape"/> above <paramref name="headScreen"/> (the pet's own head
    /// anchor, already in screen space, so a dragged or hopping pet carries its glyph).
    /// <paramref name="scale256"/> converts 256-space px to screen px.
    /// </summary>
    public static void Draw(
        ImDrawListPtr dl,
        Vector2 headScreen,
        float scale256,
        GlyphFrame frame,
        in GlyphShape shape,
        float alpha,
        float reveal,
        float lift256,
        Vector4 ink,
        Vector4 fill,
        Vector4 halo)
    {
        if (alpha <= 0.004f || scale256 <= 0f)
        {
            return;
        }

        var centre = new Vector2(headScreen.X, headScreen.Y - ((CentreLift256 + lift256) * scale256));
        var boxHalf = Box256 * 0.5f * scale256;

        DrawFrame(dl, frame, centre, headScreen, scale256, alpha, ink, halo);

        // 100-box → screen. The box is centred on the frame, so a glyph is always concentric
        // with whatever holds it.
        Vector2 At(Vector2 p) => new(
            centre.X + (((p.X / 100f) - 0.5f) * 2f * boxHalf),
            centre.Y + (((p.Y / 100f) - 0.5f) * 2f * boxHalf));

        var fillCol = ImGui.ColorConvertFloat4ToU32(fill with { W = fill.W * alpha * reveal });
        var inkCol = ImGui.ColorConvertFloat4ToU32(ink with { W = ink.W * alpha });
        var weight = MathF.Max(1f, Stroke100 / 100f * Box256 * scale256);

        // Fills first, strokes over them — WITHIN A LAYER. Two passes rather than array order
        // is what keeps a multi-piece fill seamless under one outline whatever order a shape
        // declares its paths in; doing it per layer rather than once globally is what lets an
        // object in front genuinely hide what it covers, instead of the thing behind sawing
        // its outline straight through it (see GlyphPath.Layer).
        var layers = 0;
        foreach (var path in shape.Paths)
        {
            layers = Math.Max(layers, path.Layer);
        }

        for (var layer = 0; layer <= layers; layer++)
        {
            foreach (var path in shape.Paths)
            {
                if (path.Layer != layer || !path.Fill || path.Points.Length < 3 || fill.W <= 0f)
                {
                    continue;
                }

                foreach (var p in path.Points)
                {
                    dl.PathLineTo(At(p));
                }

                dl.PathFillConvex(fillCol);
            }

            foreach (var path in shape.Paths)
            {
                if (path.Layer != layer || !path.Stroke || path.Points.Length < 2)
                {
                    continue;
                }

                StrokePath(dl, path, At, reveal, inkCol, weight * path.Weight);
            }
        }
    }

    /// <summary>Strokes a path, revealed progressively: the symbol draws itself on, which is
    /// what makes it read as aether condensing rather than as a dialog opening. Under
    /// reduce-motion the caller pins <c>reveal</c> to 1 and this is a plain polyline.</summary>
    private static void StrokePath(ImDrawListPtr dl, in GlyphPath path, Func<Vector2, Vector2> at, float reveal, uint colour, float weight)
    {
        var pts = path.Points;
        var segments = path.Closed ? pts.Length : pts.Length - 1;
        var drawn = reveal >= 1f ? segments : Math.Clamp(reveal, 0f, 1f) * segments;
        if (drawn <= 0f)
        {
            return;
        }

        var whole = (int)MathF.Floor(drawn);
        dl.PathLineTo(at(pts[0]));
        for (var i = 1; i <= whole && i < pts.Length; i++)
        {
            dl.PathLineTo(at(pts[i]));
        }

        if (path.Closed && whole >= segments)
        {
            dl.PathStroke(colour, ImDrawFlags.Closed, weight);
            return;
        }

        var frac = drawn - whole;
        if (frac > 0.01f && whole < segments)
        {
            var a = pts[whole % pts.Length];
            var b = pts[(whole + 1) % pts.Length];
            dl.PathLineTo(at(Vector2.Lerp(a, b, frac)));
        }

        dl.PathStroke(colour, ImDrawFlags.None, weight);
    }

    private static void DrawFrame(ImDrawListPtr dl, GlyphFrame frame, Vector2 centre, Vector2 headScreen, float scale256, float alpha, Vector4 ink, Vector4 halo)
    {
        if (frame == GlyphFrame.Light)
        {
            // Soft concentric rings, the PetDraw contact-shadow idiom: a texture-free falloff
            // that works on any ImGui build and costs rings × 26 path points.
            var r = LightRadius256 * scale256;
            const int rings = 6;
            for (var i = 0; i < rings; i++)
            {
                var t = i / (float)rings;
                var radius = r * (1.32f - (t * 0.62f));
                var col = halo with { W = halo.W * alpha * 0.20f };
                const int segments = 26;
                for (var s = 0; s < segments; s++)
                {
                    var a = MathF.Tau * s / segments;
                    dl.PathLineTo(new Vector2(centre.X + (MathF.Cos(a) * radius), centre.Y + (MathF.Sin(a) * radius)));
                }

                dl.PathFillConvex(ImGui.ColorConvertFloat4ToU32(col));
            }

            // One hairline ring: without it the disc has no edge at all and the glyph reads as
            // floating on nothing against a bright background.
            dl.AddCircle(centre, r * 0.86f, ImGui.ColorConvertFloat4ToU32(ink with { W = ink.W * alpha * 0.26f }), 32,
                MathF.Max(1f, 1.6f * scale256));
            return;
        }

        // The bubble: outline pass first, fill over it, so the body and the tail compose as
        // ONE outlined shape and no seam crosses the join (the DrawLimb trick).
        var halfW = BubbleHalfW256 * scale256;
        var halfH = BubbleHalfH256 * scale256;
        var round = BubbleRound256 * scale256;
        var line = MathF.Max(1.4f, 3.2f * scale256);
        var tipY = headScreen.Y - (10f * scale256);
        var tailHalf = 11f * scale256;

        var inkCol = ImGui.ColorConvertFloat4ToU32(ink with { W = ink.W * alpha });
        var fillCol = ImGui.ColorConvertFloat4ToU32(halo with { W = halo.W * alpha });

        for (var pass = 0; pass < 2; pass++)
        {
            var grow = pass == 0 ? line : 0f;
            var colour = pass == 0 ? inkCol : fillCol;
            var min = new Vector2(centre.X - halfW - grow, centre.Y - halfH - grow);
            var max = new Vector2(centre.X + halfW + grow, centre.Y + halfH + grow);

            // The tail sits under the body and is drawn first, so the body's own fill covers
            // its top edge and the two read as one silhouette.
            dl.PathLineTo(new Vector2(centre.X - tailHalf - grow, max.Y - (2f * scale256)));
            dl.PathLineTo(new Vector2(centre.X + tailHalf + grow, max.Y - (2f * scale256)));
            dl.PathLineTo(new Vector2(centre.X + (2f * scale256), tipY + grow));
            dl.PathFillConvex(colour);

            dl.AddRectFilled(min, max, colour, round + grow, ImDrawFlags.RoundCornersAll);
        }
    }
}
