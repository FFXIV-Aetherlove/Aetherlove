namespace AetherOS.Apps.Racer.Rendering;

using System;
using System.Numerics;

/// <summary>The stage's camera as a value: eye in world bounds, the heading's cosine and sine taken
/// once, zoom in screen pixels per bound, and the pivot. Built once a frame and passed by reference,
/// so no draw call allocates a delegate to reach it. No perspective: one bound is
/// <see cref="Zoom"/> pixels everywhere, at every heading.</summary>
internal readonly record struct StageCam(Vector2 Eye, float Cos, float Sin, float Zoom, Vector2 Centre)
{
    public static StageCam From(Vector2 eye, float heading, float zoom, Vector2 centre)
    {
        return new StageCam(eye, MathF.Cos(heading), MathF.Sin(heading), zoom, centre);
    }

    /// <summary>World (bounds, track space) to screen.</summary>
    public Vector2 ToScreen(Vector2 world)
    {
        var rel = world - Eye;
        return Centre + (new Vector2((rel.X * Cos) - (rel.Y * Sin), (rel.X * Sin) + (rel.Y * Cos)) * Zoom);
    }

    /// <summary>A world OFFSET as a screen offset: the same rotate and scale with the translation
    /// left out. Exact, because the transform has no perspective, so projecting one anchor and
    /// stepping it with this beats projecting every point of a fan.</summary>
    public Vector2 ToScreenDelta(Vector2 world)
    {
        return new Vector2((world.X * Cos) - (world.Y * Sin), (world.X * Sin) + (world.Y * Cos)) * Zoom;
    }

    /// <summary>How far from the eye, in world bounds, the furthest corner of the stage is. Not half
    /// the diagonal: <see cref="Centre"/> is a pivot below the middle of the rect, so the
    /// half-diagonal understates the reach and a walk windowed on it drops ground still on screen.</summary>
    public float VisibleRadius(Vector2 tl, Vector2 size)
    {
        if (Zoom <= 0f)
        {
            return 0f;
        }

        var dx = MathF.Max(Centre.X - tl.X, tl.X + size.X - Centre.X);
        var dy = MathF.Max(Centre.Y - tl.Y, tl.Y + size.Y - Centre.Y);
        return new Vector2(dx, dy).Length() / Zoom;
    }

    /// <summary>Is a projected point within <paramref name="pad"/> pixels of the stage? A screen-space
    /// rect test spelled here so the walks that cull against it do not each write it out again.</summary>
    public static bool OnStage(Vector2 screen, Vector2 tl, Vector2 size, float pad) =>
        screen.X >= tl.X - pad && screen.X <= tl.X + size.X + pad
        && screen.Y >= tl.Y - pad && screen.Y <= tl.Y + size.Y + pad;

    /// <summary>Screen back to world, for anything that must pin a screen point to the ground.</summary>
    public Vector2 ToWorld(Vector2 screen)
    {
        if (Zoom <= 0f)
        {
            return Eye;
        }
        var rel = (screen - Centre) / Zoom;
        return Eye + new Vector2((rel.X * Cos) + (rel.Y * Sin), (rel.Y * Cos) - (rel.X * Sin));
    }
}
