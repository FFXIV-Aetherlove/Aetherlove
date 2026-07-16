using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>Shared building blocks for modal bodies: the standard icon/title header and a themed
/// full-width button.</summary>
internal static class ModalUi
{
    /// <summary>Centering is cursor-relative so it lines up inside padded panels.</summary>
    internal static void Header(float availW, string title, Vector4 accent)
    {
        using (UiFonts.H3?.Push())
        {
            var titleSz = ImGui.CalcTextSize(title);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availW - titleSz.X) * 0.5f);
            ImGui.TextColored(accent, title);
        }
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(accent.X, accent.Y, accent.Z, 0.35f));
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    internal static void Header(float availW, FontAwesomeIcon icon, string title, Vector4 accent)
    {
        var iconPx = Px(40f);
        var iconSz = IconDraw.Measure(icon, iconPx);
        var origin = ImGui.GetCursorScreenPos();
        IconDraw.Add(ImGui.GetWindowDrawList(), icon, iconPx,
            new Vector2(origin.X + (availW - iconSz.X) * 0.5f, origin.Y), ImGui.GetColorU32(accent));
        ImGui.Dummy(new Vector2(availW, iconSz.Y));
        ImGui.Spacing();

        Header(availW, title, accent);
    }

    internal static bool Button(string label, float width)
    {
        var t = ThemeService.Current;
        PushThemeButton(t);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        var clicked = ImGui.Button(label, new Vector2(width, Px(32f)));
        ImGui.PopStyleVar();
        PopThemeButton();
        return clicked;
    }
}
