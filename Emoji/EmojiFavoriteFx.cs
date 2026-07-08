using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Emoji;

/// <summary>A brief gold star-pop drawn on the foreground layer when an emoji is favorited, giving the action
/// a little confirmation flourish. Skipped under reduced motion. Drawn once per frame by MainPluginWindow so
/// it finishes even after the menu or picker closes.</summary>
internal static class EmojiFavoriteFx
{
    private const double Duration = 0.45;
    private static Vector2 _pos;
    private static double _start = -1;

    internal static void Trigger(Vector2 screenPos)
    {
        if (AccessibilityService.ReduceMotion)
        {
            return;
        }
        _pos = screenPos;
        _start = ImGui.GetTime();
    }

    internal static void Draw()
    {
        if (_start < 0)
        {
            return;
        }
        var t = (ImGui.GetTime() - _start) / Duration;
        if (t >= 1.0)
        {
            _start = -1;
            return;
        }

        var e = (float)t;
        var eased = 1f - MathF.Pow(1f - e, 3f);
        var scale = 0.6f + eased * 0.9f;
        var alpha = 1f - e;
        var rise = eased * Px(14f);

        var dl = ImGui.GetForegroundDrawList();
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var star = FontAwesomeIcon.Star.ToIconString();
        var px = Px(18f) * scale;
        var sz = ImGui.CalcTextSize(star) * (px / ImGui.GetFontSize());
        var col = (UiColors.FavoriteStar & 0x00FFFFFFu) | ((uint)(alpha * 255f) << 24);
        dl.AddText(ImGui.GetFont(), px, new Vector2(_pos.X - sz.X * 0.5f, _pos.Y - sz.Y * 0.5f - rise), col, star);
        ImGui.PopFont();
    }
}
