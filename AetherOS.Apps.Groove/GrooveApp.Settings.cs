using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Groove;

public sealed partial class GrooveApp
{
    /// <summary>Deliberately titleless: the back pill is the only chrome, so the toggles start at the top
    /// whether this is hosted in the app or in OS Settings.</summary>
    public void DrawSettings(OsAppContext ctx, Action? onBack)
    {
        // The hosted caller swallows exceptions, so an unbalanced child would corrupt the window stack for
        // the rest of the frame; the try/finally keeps Begin and End paired whatever the body does.
        ImGui.BeginChild("##grooveSettings", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.None);
        try
        {
            var winPos = ImGui.GetWindowPos();
            var winW = ImGui.GetWindowSize().X;
            var pad = ctx.Px(14f);

            ImGui.Dummy(new Vector2(0f, ctx.Px(4f)));
            ImGui.SetCursorPosX(pad);
            if (DrawFloatingBackPill(ImGui.GetCursorScreenPos(), Loc.T("os.groove_back"), FontAwesomeIcon.Music))
            {
                (onBack ?? BackToPlayer)();
            }
            ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));

            DrawSettingsBody(ctx, winPos.X + pad, winW - pad * 2f);
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    private void DrawSettingsBody(OsAppContext ctx, float x, float width)
    {
        var padX = x - ImGui.GetWindowPos().X;

        DrawSectionHeader(Loc.T("os.groove_set_section_surfaces"), padX);
        DrawSurfaceToggle(ctx, padX, width, "##grooveMini", Loc.T("os.groove_set_mini"),
            Loc.T("os.groove_set_mini_hint"), _settings.ShowMiniControls, v => _settings.ShowMiniControls = v);
        DrawSurfaceToggle(ctx, padX, width, "##grooveDtr", Loc.T("os.groove_set_dtr"),
            Loc.T("os.groove_set_dtr_hint"), _serverBar.AppEnabled, v => _serverBar.AppEnabled = v);
        DrawSurfaceToggle(ctx, padX, width, "##grooveShade", Loc.T("os.groove_set_shade"),
            Loc.T("os.groove_set_shade_hint"), _settings.ShowShadeTile, v => _settings.ShowShadeTile = v);
        DrawSurfaceToggle(ctx, padX, width, "##grooveWidget", Loc.T("os.groove_set_widget"),
            Loc.T("os.groove_set_widget_hint"), _settings.ShowWidget, v => _settings.ShowWidget = v);

        ImGui.Dummy(new Vector2(width, ctx.Px(8f)));
        DrawSectionHeader(Loc.T("os.groove_set_section_audio"), padX);
        DrawSurfaceToggle(ctx, padX, width, "##grooveAutoMute", Loc.T("os.groove_set_automute"),
            Loc.T("os.groove_set_automute_hint"), _settings.AutoMuteBgm, v => _settings.AutoMuteBgm = v);

        ImGui.Dummy(new Vector2(width, ctx.Px(14f)));
    }

    /// <summary>A switch row over its explanation, the shape the Yapper settings pages use.</summary>
    private static void DrawSurfaceToggle(OsAppContext ctx, float padX, float width, string id, string label,
        string hint, bool value, Action<bool> apply)
    {
        ImGui.SetCursorPosX(Px(padX));
        if (DrawToggleSwitch(id, label, value))
        {
            apply(!value);
        }
        ImGui.SetCursorPosX(Px(padX));
        ImGui.PushTextWrapPos(Px(padX) + width);
        ImGui.TextColored(UiColors.Hint, hint);
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(width, ctx.Px(8f)));
    }

    private void BackToPlayer() => _view = View.Player;
}
