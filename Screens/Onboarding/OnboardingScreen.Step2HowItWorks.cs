using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{
    private void DrawStepHowItWorks()
    {
        var t = ThemeService.Current;

        ImGui.Spacing();
        ImGui.TextWrapped(Loc.T("onboarding.how_intro"));
        ImGui.Spacing();

        DrawHowStep("1", Loc.T("onboarding.how1_title"),
            Loc.T("onboarding.how1_body"));

        DrawHowStep("2", Loc.T("onboarding.how2_title"),
            Loc.T("onboarding.how2_body"));

        DrawHowStep("3", Loc.T("onboarding.how3_title"),
            Loc.T("onboarding.how3_body"));

        DrawHowStep("4", Loc.T("onboarding.how4_title"),
            Loc.T("onboarding.how4_body"));

        DrawHowStep("5", Loc.T("onboarding.how5_title"),
            Loc.T("onboarding.how5_body"));

        DrawHowStep("6", Loc.T("onboarding.how6_title"),
            Loc.T("onboarding.how6_body"));

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 0.85f),
            Loc.T("onboarding.how_ready"));
    }

    private static void DrawHowStep(string number, string heading, string body)
    {
        var t = ThemeService.Current;
        DrawSectionHeading($"{number}.  {heading}", t);
        ImGui.Indent(Px(8f));
        ImGui.TextWrapped(body);
        ImGui.Unindent(Px(8f));
        ImGui.Spacing();
    }
}
