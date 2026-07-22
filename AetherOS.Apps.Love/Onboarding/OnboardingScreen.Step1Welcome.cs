using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using static AetherLove.UI.OnboardingUi;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{
    /// <summary>The AetherLove first-run landing: a warm hero plus a three-line preview of what the profile setup
    /// covers. The account is already provisioned (AetherOS onboarding handled sign-in, passphrase, OS name), so
    /// this only frames the dating-profile steps that follow.</summary>
    private void DrawStepWelcome()
    {
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawHero("love_welcome", FontAwesomeIcon.Heart, Loc.T("onboarding.hero_welcome_title"),
            Loc.T("onboarding.hero_welcome_sub"), 40f);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawWelcomeRow(FontAwesomeIcon.User, Loc.T("onboarding.welcome_step_profile"));
        DrawWelcomeRow(FontAwesomeIcon.Images, Loc.T("onboarding.welcome_step_photos"));
        DrawWelcomeRow(FontAwesomeIcon.Heart, Loc.T("onboarding.welcome_step_prefs"));

        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawCenteredParagraph(Loc.T("onboarding.welcome_footer"), ImGui.GetWindowSize().X - Px(48f), UiColors.Hint);
    }

    private static void DrawWelcomeRow(FontAwesomeIcon icon, string text) => DrawFeatureRow(icon, text);
}
