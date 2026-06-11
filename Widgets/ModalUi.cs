using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>Shared building blocks for the modal bodies drawn through <see cref="ModalHost"/>: the standard
/// icon/title header, the body text colours, and a themed full-width button.</summary>
internal static class ModalUi
{
    /// <summary>Caution accent (amber) for warning-style modals.</summary>
    internal static readonly Vector4 Amber = new(0.95f, 0.65f, 0.14f, 1f);

    /// <summary>Error accent (red) for failure-style modals.</summary>
    internal static readonly Vector4 Danger = new(0.95f, 0.45f, 0.45f, 1f);

    /// <summary>Primary body text.</summary>
    internal static readonly Vector4 Body = new(0.85f, 0.85f, 0.85f, 1f);

    /// <summary>Secondary / detail body text.</summary>
    internal static readonly Vector4 Subtle = new(0.70f, 0.70f, 0.74f, 1f);

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
