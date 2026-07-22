using System;
using System.Numerics;
using AetherLove.Services;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Os;

/// <summary>A one-shot "power-on" reveal, played the first time the phone opens after onboarding finishes: the
/// content starts black, an accent bloom ignites from the centre and a ring ripples outward, then the black
/// dissolves to reveal the home. Drawn as a content-clipped overlay on top of the home. Skipped under
/// reduce-motion.</summary>
public static class OsBootIntro
{
    private const float Duration = 1.7f;
    private static float _startTime = float.MinValue;

    public static bool Active { get; private set; }

    /// <summary>Starts the reveal. Call at the onboarding-to-home handoff.</summary>
    public static void Play()
    {
        if (AccessibilityService.ReduceMotion)
        {
            Active = false;
            return;
        }
        _startTime = (float)ImGui.GetTime();
        Active = true;
    }

    public static void Draw(ImDrawListPtr dl, Vector2 tl, Vector2 br)
    {
        if (!Active)
        {
            return;
        }
        var t = ((float)ImGui.GetTime() - _startTime) / Duration;
        if (t >= 1f)
        {
            Active = false;
            return;
        }

        var center = (tl + br) * 0.5f;
        var w = br.X - tl.X;
        var h = br.Y - tl.Y;
        var maxR = MathF.Sqrt(w * w + h * h) * 0.5f;
        var accent = ThemeService.Current.Accent;

        // Black hold, then an eased dissolve that reveals the home beneath.
        var blackA = t < 0.32f ? 1f : 1f - EaseOutCubic((t - 0.32f) / 0.68f);
        if (blackA > 0.001f)
        {
            dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(new Vector4(0.015f, 0.015f, 0.02f, blackA)));
        }

        // Centre bloom: soft accent glow that ignites then fades.
        var glowT = Math.Clamp(t / 0.6f, 0f, 1f);
        var bloom = MathF.Sin(glowT * MathF.PI);
        if (bloom > 0.001f)
        {
            var baseR = maxR * (0.10f + 0.35f * EaseOutCubic(glowT));
            for (var i = 0; i < 5; i++)
            {
                var rr = baseR * (0.35f + i * 0.20f);
                var aa = bloom * 0.30f * (1f - i * 0.18f);
                dl.AddCircleFilled(center, rr, ImGui.ColorConvertFloat4ToU32(accent with { W = aa }), 64);
            }
        }

        // A single ring ripple expanding from the centre.
        var rt = Math.Clamp((t - 0.12f) / 0.78f, 0f, 1f);
        if (rt > 0f && rt < 1f)
        {
            var ringR = maxR * (0.04f + 0.96f * EaseOutCubic(rt));
            var ringA = (1f - rt) * 0.55f;
            dl.AddCircle(center, ringR, ImGui.ColorConvertFloat4ToU32(accent with { W = ringA }), 96, Px(2.5f));
        }
    }

    private static float EaseOutCubic(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        var p = 1f - x;
        return 1f - p * p * p;
    }
}
