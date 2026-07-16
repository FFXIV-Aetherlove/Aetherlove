using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.Widgets;

/// <summary>In-phone "learn about supporting" popup. Hosts draw it at the end of their frame and open it
/// on mouse release, not press; the opening press would otherwise arm the scrim and dismiss it.</summary>
internal static class SupporterInfoPopup
{
    private static bool _open;
    private static float _panelH;
    private static string? _bodyKeyOverride;

    public static bool IsOpen => _open;

    /// <summary><paramref name="bodyKeyOverride"/> swaps the standard body for context-specific copy.</summary>
    public static void Open(string? bodyKeyOverride = null)
    {
        _open = true;
        _panelH = 0f;
        _bodyKeyOverride = bodyKeyOverride;
    }

    public static void Draw(Vector2 windowPos, Vector2 windowSize, Action onMoreInfo)
    {
        if (!_open)
        {
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _open = false;
            return;
        }
        var dismissed = DrawPageOverlayPanel("supInfo", windowPos, windowSize, ref _panelH, Px(210f), innerW =>
        {
            ModalUi.Header(innerW, FontAwesomeIcon.HandHoldingHeart, Loc.T("settings.sup_learn_title"), UiColors.Patreon);
            ImGui.PushTextWrapPos(innerW);
            ImGui.TextColored(UiColors.Body, Loc.T(_bodyKeyOverride ?? "settings.sup_learn_body"));
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            ImGui.Spacing();
            var gap = Px(8f);
            var half = (innerW - gap) * 0.5f;
            if (ModalUi.Button($"{Loc.T("common.cancel")}##supLearnCancel", half))
            {
                _open = false;
            }
            ImGui.SameLine(0f, gap);
            if (ModalUi.Button($"{Loc.T("settings.sup_learn_more")}##supLearnMore", half))
            {
                _open = false;
                onMoreInfo();
            }
        });
        if (dismissed)
        {
            _open = false;
        }
    }
}
