using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Widgets;

/// <summary>Spinner plus label, centred in the current content region.</summary>
public static class LoadingIndicator
{
    public static void Draw(string? text = null)
    {
        var label = text ?? Loc.T("common.loading");
        var r = Px(8f);
        var thickness = Px(2.5f);
        var gap = Px(10f);

        var textSize = ImGui.CalcTextSize(label);
        var rowH = MathF.Max(r * 2f, textSize.Y);
        var totalW = r * 2f + gap + textSize.X;

        var winW = ImGui.GetWindowSize().X;
        var startX = MathF.Max(0f, (winW - totalW) * 0.5f);
        var top = ImGui.GetCursorPosY();
        var startY = top + MathF.Max(0f, (ImGui.GetContentRegionAvail().Y - rowH) * 0.5f);

        ImGui.SetCursorPos(new Vector2(startX, startY));
        var screen = ImGui.GetCursorScreenPos();
        LoadingSpinner.Draw(new Vector2(screen.X + r, screen.Y + rowH * 0.5f), r, thickness,
            ThemeService.Current.AccentU32);

        ImGui.SetCursorPos(new Vector2(startX + r * 2f + gap, startY + (rowH - textSize.Y) * 0.5f));
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), label);
    }
}
