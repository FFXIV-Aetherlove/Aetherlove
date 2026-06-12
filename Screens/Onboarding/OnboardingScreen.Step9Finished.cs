using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{
    private readonly ConfettiBurst _confetti = new();

    private void ResetConfetti() => _confetti.Reset();

    private void DrawStepFinished()
    {
        var t = ThemeService.Current;
        var muted = UiColors.Muted with { W = 0.75f };

        var wPos = ImGui.GetWindowPos();
        var wSize = ImGui.GetWindowSize();
        var clipMin = wPos + Px(0f, 58f + 6f); // below header
        var clipMax = wPos + new Vector2(wSize.X, wSize.Y - Px(48f)); // above nav bar

        var availH = ImGui.GetContentRegionAvail().Y;
        using (var scroll = ImRaii.Child("##finishedScroll", new Vector2(0f, availH), false))
        {
            if (scroll.Success)
            {
                ImGui.Spacing();

                using (UiFonts.H2?.Push())
                {
                    var Heading = Loc.T("onboarding.finished_heading");
                    var headSz = ImGui.CalcTextSize(Heading);
                    ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - headSz.X) * 0.5f);
                    ImGui.TextColored(t.AccentLight, Heading);
                }

                ImGui.Spacing();
                ImGui.TextWrapped(Loc.T("onboarding.finished_intro"));
                ImGui.Spacing();

                DrawSectionHeading(Loc.T("onboarding.finished_verification_heading"), t);
                ImGui.TextWrapped(Loc.T("onboarding.finished_verification_body"));
                ImGui.Spacing();
                ImGui.PushTextWrapPos(0f);
                ImGui.TextColored(muted, Loc.T("onboarding.finished_verification_note"));
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                ImGui.Spacing();

                DrawSectionHeading(Loc.T("onboarding.finished_swiping_heading"), t);
                ImGui.TextWrapped(Loc.T("onboarding.finished_swiping_body"));
                ImGui.Spacing();
                ImGui.Spacing();

                DrawSectionHeading(Loc.T("onboarding.finished_rejected_heading"), t);
                ImGui.TextWrapped(Loc.T("onboarding.finished_rejected_body"));
                ImGui.Spacing();
                ImGui.Spacing();

                ImGui.PushTextWrapPos(0f);
                ImGui.TextColored(UiColors.Success, Loc.T("onboarding.finished_good_luck"));
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
            }
        }


        _confetti.Draw(clipMin, clipMax);
    }
}
