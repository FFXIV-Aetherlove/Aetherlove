using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherOS.PetKit.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AetherLove.Services;

/// <summary>Paints the creature back into a selfie.
/// <para>The floating creature is an ImGui overlay, and ImGui renders AFTER the backbuffer read a capture
/// takes, so it is never in the shot. Rather than a second renderer that would drift from the real one,
/// <see cref="PetFrameRecorder"/> records the geometry the creature actually drew and this replays it into
/// the captured bitmap. Everything here is deliberately small and exact: nearest-neighbour sampling of the
/// same atlases, the same per-quad tint multiply, and source-over compositing.</para></summary>
internal static class PetSelfieCompositor
{
    private static readonly Dictionary<string, Image<Rgba32>?> Atlases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Draws the recorded frame into <paramref name="shot"/>. <paramref name="scale"/> converts
    /// screen pixels (what was recorded) into image pixels (what was captured); they differ whenever the
    /// backbuffer is not the same size as the ImGui viewport.</summary>
    public static void Compose(
        Image<Rgba32> shot,
        IReadOnlyList<PetQuad> quads,
        IReadOnlyList<PetStroke> strokes,
        Vector2 scale)
    {
        foreach (var quad in quads)
        {
            try
            {
                DrawQuad(shot, quad, scale);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[Selfie] a creature layer could not be painted.");
            }
        }
        foreach (var stroke in strokes)
        {
            try
            {
                DrawPath(shot, stroke, scale);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[Selfie] a creature path could not be painted.");
            }
        }
    }

    /// <summary>Drops the cached atlases. Called when a capture finishes: a selfie is rare and the sheets
    /// are large, so holding them between shots would be paying rent on nothing.</summary>
    public static void Forget()
    {
        foreach (var image in Atlases.Values)
        {
            image?.Dispose();
        }
        Atlases.Clear();
    }

    private static Image<Rgba32>? Atlas(string path)
    {
        if (Atlases.TryGetValue(path, out var cached))
        {
            return cached;
        }
        Image<Rgba32>? image = null;
        try
        {
            if (File.Exists(path))
            {
                image = Image.Load<Rgba32>(path);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Selfie] atlas {Path} could not be read.", path);
        }
        Atlases[path] = image;
        return image;
    }

    private static void DrawQuad(Image<Rgba32> shot, in PetQuad quad, Vector2 scale)
    {
        if (Atlas(quad.TexturePath) is not { } atlas)
        {
            return;
        }

        var min = quad.Min * scale;
        var max = quad.Max * scale;
        var x0 = (int)MathF.Floor(MathF.Min(min.X, max.X));
        var x1 = (int)MathF.Ceiling(MathF.Max(min.X, max.X));
        var y0 = (int)MathF.Floor(MathF.Min(min.Y, max.Y));
        var y1 = (int)MathF.Ceiling(MathF.Max(min.Y, max.Y));
        var width = max.X - min.X;
        var height = max.Y - min.Y;
        if (width == 0f || height == 0f)
        {
            return;
        }

        var tint = Unpack(quad.Colour);
        for (var y = Math.Max(0, y0); y < Math.Min(shot.Height, y1); y++)
        {
            var row = shot.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y);
            // Sampled at the pixel CENTRE, so a quad landing on a half pixel does not shift a whole one.
            var v = ((y + 0.5f) - min.Y) / height;
            for (var x = Math.Max(0, x0); x < Math.Min(shot.Width, x1); x++)
            {
                var u = ((x + 0.5f) - min.X) / width;
                if (u is < 0f or > 1f || v is < 0f or > 1f)
                {
                    continue;
                }
                var su = quad.Uv0.X + ((quad.Uv1.X - quad.Uv0.X) * u);
                var sv = quad.Uv0.Y + ((quad.Uv1.Y - quad.Uv0.Y) * v);
                var sx = (int)(su * atlas.Width);
                var sy = (int)(sv * atlas.Height);
                if (sx < 0 || sy < 0 || sx >= atlas.Width || sy >= atlas.Height)
                {
                    continue;
                }
                var texel = atlas[sx, sy];
                if (texel.A == 0)
                {
                    continue;
                }
                row[x] = Over(row[x], new Vector4(
                    texel.R / 255f * tint.X,
                    texel.G / 255f * tint.Y,
                    texel.B / 255f * tint.Z,
                    texel.A / 255f * tint.W));
            }
        }
    }

    /// <summary>A recorded path: filled when it carries no thickness, stroked when it does. The fill is a
    /// scanline over the polygon, which is what the draw list's own convex fill amounts to.</summary>
    private static void DrawPath(Image<Rgba32> shot, in PetStroke stroke, Vector2 scale)
    {
        var points = new Vector2[stroke.Points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = stroke.Points[i] * scale;
        }
        var colour = Unpack(stroke.Colour);

        if (stroke.Thickness <= 0f)
        {
            FillPolygon(shot, points, colour);
            return;
        }

        var thickness = MathF.Max(1f, stroke.Thickness * ((scale.X + scale.Y) * 0.5f));
        for (var i = 0; i + 1 < points.Length; i++)
        {
            DrawSegment(shot, points[i], points[i + 1], thickness, colour);
        }
        if (stroke.Closed && points.Length > 2)
        {
            DrawSegment(shot, points[^1], points[0], thickness, colour);
        }
    }

    private static void FillPolygon(Image<Rgba32> shot, Vector2[] points, Vector4 colour)
    {
        var minY = int.MaxValue;
        var maxY = int.MinValue;
        foreach (var p in points)
        {
            minY = Math.Min(minY, (int)MathF.Floor(p.Y));
            maxY = Math.Max(maxY, (int)MathF.Ceiling(p.Y));
        }
        Span<float> crossings = stackalloc float[points.Length + 1];

        for (var y = Math.Max(0, minY); y < Math.Min(shot.Height, maxY); y++)
        {
            var scan = y + 0.5f;
            var hits = 0;
            for (var i = 0; i < points.Length; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Length];
                if (a.Y == b.Y || scan < MathF.Min(a.Y, b.Y) || scan >= MathF.Max(a.Y, b.Y))
                {
                    continue;
                }
                crossings[hits++] = a.X + ((scan - a.Y) / (b.Y - a.Y) * (b.X - a.X));
            }
            if (hits < 2)
            {
                continue;
            }
            crossings[..hits].Sort();
            var row = shot.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y);
            for (var i = 0; i + 1 < hits; i += 2)
            {
                var from = Math.Max(0, (int)MathF.Round(crossings[i]));
                var to = Math.Min(shot.Width - 1, (int)MathF.Round(crossings[i + 1]));
                for (var x = from; x <= to; x++)
                {
                    row[x] = Over(row[x], colour);
                }
            }
        }
    }

    /// <summary>One thick segment, as the set of pixels within half a thickness of the line. Round ends fall
    /// out of the distance test, which is what keeps a joint between two segments from notching.</summary>
    private static void DrawSegment(Image<Rgba32> shot, Vector2 a, Vector2 b, float thickness, Vector4 colour)
    {
        var half = thickness * 0.5f;
        var x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, b.X) - half));
        var x1 = Math.Min(shot.Width - 1, (int)MathF.Ceiling(MathF.Max(a.X, b.X) + half));
        var y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, b.Y) - half));
        var y1 = Math.Min(shot.Height - 1, (int)MathF.Ceiling(MathF.Max(a.Y, b.Y) + half));
        var ab = b - a;
        var lengthSq = ab.LengthSquared();

        for (var y = y0; y <= y1; y++)
        {
            var row = shot.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y);
            for (var x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var t = lengthSq <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / lengthSq, 0f, 1f);
                var distance = (p - (a + (ab * t))).Length();
                if (distance > half)
                {
                    continue;
                }
                // One pixel of feather at the rim, which is all the anti-aliasing a line this thin needs.
                var edge = Math.Clamp(half - distance, 0f, 1f);
                row[x] = Over(row[x], colour with { W = colour.W * edge });
            }
        }
    }

    private static Vector4 Unpack(uint abgr) => new(
        (abgr & 0xFF) / 255f,
        ((abgr >> 8) & 0xFF) / 255f,
        ((abgr >> 16) & 0xFF) / 255f,
        ((abgr >> 24) & 0xFF) / 255f);

    /// <summary>Source-over onto an opaque screenshot, so the result stays opaque.</summary>
    private static Rgba32 Over(Rgba32 dst, Vector4 src)
    {
        if (src.W <= 0f)
        {
            return dst;
        }
        var a = Math.Clamp(src.W, 0f, 1f);
        return new Rgba32(
            (byte)Math.Clamp(((src.X * a) + (dst.R / 255f * (1f - a))) * 255f, 0f, 255f),
            (byte)Math.Clamp(((src.Y * a) + (dst.G / 255f * (1f - a))) * 255f, 0f, 255f),
            (byte)Math.Clamp(((src.Z * a) + (dst.B / 255f * (1f - a))) * 255f, 0f, 255f),
            byte.MaxValue);
    }
}
