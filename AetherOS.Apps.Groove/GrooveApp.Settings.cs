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
        AppSettingsUi.SectionLabel(ctx, x, Loc.T("os.groove_set_section_surfaces"));

        var widget = _settings.ShowWidget;
        if (AppSettingsUi.SettingToggle(ctx, "grooveWidget", Loc.T("os.groove_set_widget"),
                Loc.T("os.groove_set_widget_hint"), x, width, ref widget))
        {
            _settings.ShowWidget = widget;
        }

        var shade = _settings.ShowShadeTile;
        if (AppSettingsUi.SettingToggle(ctx, "grooveShade", Loc.T("os.groove_set_shade"),
                Loc.T("os.groove_set_shade_hint"), x, width, ref shade))
        {
            _settings.ShowShadeTile = shade;
        }

        var dtr = _settings.ShowDtr;
        if (AppSettingsUi.SettingToggle(ctx, "grooveDtr", Loc.T("os.groove_set_dtr"),
                Loc.T("os.groove_set_dtr_hint"), x, width, ref dtr))
        {
            _settings.ShowDtr = dtr;
        }

        ImGui.Dummy(new Vector2(width, ctx.Px(6f)));
        AppSettingsUi.SectionLabel(ctx, x, Loc.T("os.groove_set_section_audio"));

        var autoMute = _settings.AutoMuteBgm;
        if (AppSettingsUi.SettingToggle(ctx, "grooveAutoMute", Loc.T("os.groove_set_automute"),
                Loc.T("os.groove_set_automute_hint"), x, width, ref autoMute))
        {
            _settings.AutoMuteBgm = autoMute;
        }

        ImGui.Dummy(new Vector2(width, ctx.Px(14f)));
    }

    private void BackToPlayer() => _view = View.Player;
}
