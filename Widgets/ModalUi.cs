using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>Shared building blocks for the modal bodies drawn through <see cref="ModalHost"/>: the standard
/// icon/title header and a themed full-width button. Accent/body colours live in <see cref="UiColors"/>.</summary>
internal static class ModalUi
{
    /// <summary>Centered title (in <paramref name="accent"/>) over an accent-tinted separator.</summary>
    internal static void Header(float availW, string title, Vector4 accent)
    {
        using (UiFonts.H3?.Push())
        {
            var titleSz = ImGui.CalcTextSize(title);
            ImGui.SetCursorPosX((availW - titleSz.X) * 0.5f);
            ImGui.TextColored(accent, title);
        }
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(accent.X, accent.Y, accent.Z, 0.35f));
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    /// <summary>A large centered icon above the standard <see cref="Header(float, string, Vector4)"/>.</summary>
    internal static void Header(float availW, FontAwesomeIcon icon, string title, Vector4 accent)
    {
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIconFixedWidth);
        ImGui.SetWindowFontScale(2.4f * UiScale.S);
        var iconStr = icon.ToIconString();
        var iconSz = ImGui.CalcTextSize(iconStr);
        ImGui.SetCursorPosX((availW - iconSz.X) * 0.5f);
        ImGui.TextColored(accent, iconStr);
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopFont();
        ImGui.Spacing();

        Header(availW, title, accent);
    }

    /// <summary>A themed, rounded full-width modal button. Returns true on click.</summary>
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
