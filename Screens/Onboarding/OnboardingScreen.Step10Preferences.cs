using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{
    private const float PreferencesPadX = 16f;

    /// <summary>Theme and phone-size pickers; both apply and persist instantly.</summary>
    private void DrawStepPreferences()
    {
        var t    = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;

        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(PreferencesPadX));
        ImGui.PushTextWrapPos(winW - Px(PreferencesPadX));
        ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.85f, 1f),
            Loc.T("onboarding.prefs_intro"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(PreferencesPadX));
        ImGui.TextColored(t.AccentLight, Loc.T("onboarding.prefs_theme"));
        ImGui.Spacing();
        Widgets.AppearancePicker.DrawThemeCards(winW, PreferencesPadX);
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.SetCursorPosX(Px(PreferencesPadX));
        ImGui.TextColored(t.AccentLight, Loc.T("onboarding.prefs_phone_size"));
        ImGui.Spacing();
        Widgets.AppearancePicker.DrawPhoneSizeButtons(winW, PreferencesPadX, t);
    }
}
