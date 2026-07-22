using System;

namespace AetherLove.Widgets;

/// <summary>Easing and tweening helpers for per-frame animation.</summary>
public static class AnimationHelper
{
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Advance progress toward 0 or 1; returns true when the target is reached.</summary>
    public static bool ClampedProgress(ref float progress, float deltaTime, float speed, bool forward)
    {
        progress += (forward ? 1f : -1f) * deltaTime * speed;
        progress = Math.Clamp(progress, 0f, 1f);
        return forward ? progress >= 1f : progress <= 0f;
    }
}
