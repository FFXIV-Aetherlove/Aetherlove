using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Store;

/// <summary>Minus / count / plus in one pill. Clamped segments dim and lose the hand cursor; the count
/// does a small pop when it changes. Returns true on the frame the value changed.</summary>
internal static class QuantityStepper
{
    private static double _popStamp = -10.0;

    public static bool Draw(string id, Vector2 tl, int min, int max, bool reduceMotion, ref int value)
    {
        var dl = ImGui.GetWindowDrawList();
        var segW = Px(30f);
        var height = Px(28f);
        var size = new Vector2(segW * 3f, height);
        dl.AddRectFilled(tl, tl + size, OsDrawShared.White(0.08f), height * 0.5f);

        var changed = false;
        if (Segment(dl, $"{id}minus", tl, new Vector2(segW, height), FontAwesomeIcon.Minus, value > min))
        {
            value--;
            changed = true;
        }
        if (Segment(dl, $"{id}plus", tl + new Vector2(segW * 2f, 0f), new Vector2(segW, height),
            FontAwesomeIcon.Plus, value < max))
        {
            value++;
            changed = true;
        }
        if (changed)
        {
            _popStamp = ImGui.GetTime();
        }

        var pop = (float)(ImGui.GetTime() - _popStamp);
        var scale = !reduceMotion && pop < 0.15f ? 1f + 0.3f * MathF.Sin(pop / 0.15f * MathF.PI) : 1f;
        var text = value.ToString();
        var textSz = ImGui.CalcTextSize(text);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale,
            tl + new Vector2(segW * 1.5f - textSz.X * 0.5f * scale, (height - textSz.Y * scale) * 0.5f),
            ImGui.GetColorU32(UiColors.Body), text);
        ImGui.SetCursorScreenPos(new Vector2(tl.X, tl.Y + height));
        return changed;
    }

    private static bool Segment(
        ImDrawListPtr dl, string id, Vector2 tl, Vector2 size, FontAwesomeIcon icon, bool enabled)
    {
        ImGui.SetCursorScreenPos(tl);
        var clicked = ImGui.InvisibleButton($"##{id}", size) && enabled;
        var hovered = enabled && ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }
        if (hovered)
        {
            dl.AddRectFilled(tl, tl + size, OsDrawShared.White(0.08f), size.Y * 0.5f);
        }
        IconDraw.AddCentered(dl, icon, Px(10f), tl + size * 0.5f,
            ImGui.GetColorU32(enabled ? UiColors.Body : UiColors.Hint with { W = 0.35f }));
        return clicked;
    }

    /// <summary>The stepper's footprint, for layout.</summary>
    public static Vector2 Size() => new(Px(90f), Px(28f));
}
