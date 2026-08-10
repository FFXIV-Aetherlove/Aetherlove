using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Groove;

/// <summary>Groove's shared eye-candy and controls: the periodic light sweep and the themed volume slider
/// that carries it.</summary>
internal static class GrooveFx
{
    private const float SweepPeriod = 5.2f;
    private const float SweepDuration = 1.1f;

    /// <summary>A glossy band that crosses the rect once every few seconds. <paramref name="phase"/> offsets
    /// the cycle so two surfaces sweeping at once stay off each other's beat.</summary>
    public static void Sweep(ImDrawListPtr dl, Vector2 tl, Vector2 br, float rounding, float phase,
        bool reduceMotion, float strength = 1f)
    {
        if (reduceMotion)
        {
            return;
        }
        var cycle = (float)((ImGui.GetTime() + phase) % SweepPeriod);
        if (cycle > SweepDuration)
        {
            return;
        }

        var p = cycle / SweepDuration;
        p = 1f - (1f - p) * (1f - p);
        var w = br.X - tl.X;
        var h = br.Y - tl.Y;
        var slant = h * 0.5f;
        var bandW = MathF.Max(Px(18f), w * 0.22f);
        var travel = w + slant + bandW * 2f;
        var x = tl.X - bandW - slant + travel * p;

        dl.PushClipRect(tl, br, true);
        Span<float> alphas = [0.05f, 0.13f, 0.05f];
        for (var i = 0; i < 3; i++)
        {
            var x0 = x + bandW * 0.33f * i;
            dl.AddQuadFilled(
                new Vector2(x0 + slant, tl.Y),
                new Vector2(x0 + slant + bandW * 0.34f, tl.Y),
                new Vector2(x0 + bandW * 0.34f, br.Y),
                new Vector2(x0, br.Y),
                OsDrawShared.White(alphas[i] * strength));
        }
        dl.PopClipRect();
    }

    /// <summary>The themed volume slider: a rounded track with an accent fill, a grab dot, the value inline,
    /// and the periodic sweep running along the filled part. Returns true while being dragged.</summary>
    /// <param name="labelInset">Shifts the inline value label left, to clear anything the caller drew in the
    /// top-right of the row (the mute toggle).</param>
    public static bool VolumeSlider(string id, Vector2 tl, float width, float value01, float sweepPhase,
        bool reduceMotion, out float newValue, bool muted = false, float labelInset = 0f)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var trackH = Px(22f);
        var barH = Px(8f);

        ImGui.SetCursorScreenPos(tl);
        ImGui.InvisibleButton(id, new Vector2(width, trackH));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        if (hovered || active)
        {
            HandOnHover();
        }

        newValue = value01;
        if (active)
        {
            newValue = Math.Clamp((ImGui.GetMousePos().X - tl.X) / width, 0f, 1f);
        }

        var barTl = new Vector2(tl.X, tl.Y + (trackH - barH) * 0.5f);
        var barBr = barTl + new Vector2(width, barH);
        dl.AddRectFilled(barTl, barBr, OsDrawShared.White(0.10f), barH * 0.5f);

        var fillW = width * Math.Clamp(newValue, 0f, 1f);
        if (fillW > 1f)
        {
            var fillBr = new Vector2(barTl.X + fillW, barBr.Y);
            dl.AddRectFilled(barTl, fillBr, muted ? OsDrawShared.White(0.22f) : ImGui.GetColorU32(t.Accent),
                barH * 0.5f);
            if (!muted)
            {
                Sweep(dl, barTl, fillBr, barH * 0.5f, sweepPhase, reduceMotion, strength: 1.6f);
            }
            dl.AddCircleFilled(new Vector2(fillBr.X, barTl.Y + barH * 0.5f),
                hovered || active ? Px(7f) : Px(5.5f),
                muted ? OsDrawShared.White(0.34f) : ImGui.GetColorU32(t.AccentLight));
        }

        var label = $"{(int)MathF.Round(newValue * 100f)}%";
        var labelSz = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(tl.X + width - labelInset - labelSz.X, tl.Y - labelSz.Y - Px(2f)),
            ImGui.GetColorU32(UiColors.Hint), label);

        ImGui.SetCursorScreenPos(new Vector2(tl.X, tl.Y + trackH));
        return active;
    }

    /// <summary>Row height a <see cref="VolumeSlider"/> occupies including its value label.</summary>
    public static float SliderRowHeight() => ImGui.GetTextLineHeight() + Px(24f);
}
