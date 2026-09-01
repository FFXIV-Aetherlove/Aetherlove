using System.Collections.Generic;
using System.Numerics;

namespace AetherOS.PetKit.Rendering;

/// <summary>One textured quad the creature drew this frame, in screen pixels, with the atlas it came from
/// named by PATH rather than by texture handle: whoever replays this is painting into a bitmap, not into
/// ImGui, and cannot do anything with a GPU handle.</summary>
public readonly record struct PetQuad(
    string TexturePath, Vector2 Min, Vector2 Max, Vector2 Uv0, Vector2 Uv1, uint Colour);

/// <summary>One path the creature drew (the dynamic mouth): a convex fill when
/// <see cref="Thickness"/> is zero, a stroked polyline otherwise.</summary>
public readonly record struct PetStroke(Vector2[] Points, bool Closed, float Thickness, uint Colour);

/// <summary>What the creature drew, recorded as geometry so a still can be composited out of it.
/// <para>The floating creature is an ImGui overlay, and ImGui renders AFTER the backbuffer read a selfie
/// takes, so a photographed creature is never in the shot: it has to be painted back in afterwards. Rather
/// than a second renderer that would drift from this one, the real draw path records what it just drew and
/// the compositor replays exactly that.</para>
/// <para>Draw-thread only, and off unless somebody is about to take a picture.</para></summary>
public static class PetFrameRecorder
{
    private static readonly List<PetQuad> Quads = [];
    private static readonly List<PetStroke> Strokes = [];

    /// <summary>Set while a capture is pending. Off, every method here is a branch and nothing else.</summary>
    public static bool Recording { get; set; }

    public static IReadOnlyList<PetQuad> FrameQuads => Quads;

    public static IReadOnlyList<PetStroke> FrameStrokes => Strokes;

    /// <summary>Starts a fresh frame. Called by the creature's own draw, so the recording is always the last
    /// COMPLETE frame rather than half of two.</summary>
    public static void Begin()
    {
        if (!Recording)
        {
            return;
        }
        Quads.Clear();
        Strokes.Clear();
    }

    public static void Add(string texturePath, Vector2 min, Vector2 max, Vector2 uv0, Vector2 uv1, uint colour)
    {
        if (Recording && texturePath.Length > 0)
        {
            Quads.Add(new PetQuad(texturePath, min, max, uv0, uv1, colour));
        }
    }

    public static void Add(IReadOnlyList<Vector2> points, bool closed, float thickness, uint colour)
    {
        if (!Recording || points.Count < 2)
        {
            return;
        }
        var copy = new Vector2[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            copy[i] = points[i];
        }
        Strokes.Add(new PetStroke(copy, closed, thickness, colour));
    }

    /// <summary>Drops whatever was held, so a cancelled capture leaves nothing behind.</summary>
    public static void Clear()
    {
        Quads.Clear();
        Strokes.Clear();
    }
}
