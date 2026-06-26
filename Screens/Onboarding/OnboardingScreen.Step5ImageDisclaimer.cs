using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{

    private bool _imageDisclaimerAcknowledged;


    private void DrawImageDisclaimer()
    {
        var t     = ThemeService.Current;
        var red   = new Vector4(0.92f, 0.22f, 0.22f, 1.00f);
        var muted = new Vector4(0.75f, 0.75f, 0.75f, 0.80f);

        ImGui.Spacing();
        ImGui.TextWrapped(Loc.T("onboarding.disclaimer_intro"));
        ImGui.TextColored(muted, Loc.T("onboarding.disclaimer_moderation"));
        ImGui.Spacing();
        ImGui.Separator();

        var scrollH = ImGui.GetContentRegionAvail().Y - Px(50f);
        using (var scroll = ImRaii.Child("##imgDisclaimer", new Vector2(0f, scrollH), false))
        {
            if (!scroll.Success)
            {
                return;
            }

            DrawSectionHeading(Loc.T("onboarding.disclaimer_general_heading"), t);
            ImGui.TextWrapped(Loc.T("onboarding.disclaimer_general_body"));
            ImGui.Spacing();

            DrawSectionHeading(Loc.T("onboarding.disclaimer_profile_images_heading"), t);
            ImGui.TextWrapped(Loc.T("onboarding.disclaimer_profile_images_body"));
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Px(14f));
            ImGui.TextColored(red, Loc.T("onboarding.disclaimer_not_allowed"));
            var notAllowedItems = new[]
            {
                Loc.T("onboarding.disclaimer_na_fan_art"),
                Loc.T("onboarding.disclaimer_na_3d_renders"),
                Loc.T("onboarding.disclaimer_na_ai"),
                Loc.T("onboarding.disclaimer_na_real_photos"),
                Loc.T("onboarding.disclaimer_na_unrelated"),
            };
            foreach (var item in notAllowedItems)
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Px(22f));
                ImGui.TextColored(new Vector4(0.92f, 0.45f, 0.45f, 0.90f), $"-  {item}");
            }
            ImGui.Spacing();
            ImGui.TextWrapped(Loc.T("onboarding.disclaimer_profile_images_consent"));
            ImGui.Spacing();

            DrawSectionHeading(Loc.T("onboarding.disclaimer_nsfw_heading"), t);
            ImGui.TextWrapped(Loc.T("onboarding.disclaimer_nsfw_body1"));
            ImGui.Spacing();
            ImGui.TextWrapped(Loc.T("onboarding.disclaimer_nsfw_body2"));
            ImGui.Spacing();

            DrawSectionHeading(Loc.T("onboarding.disclaimer_nsfl_heading"), t);
            ImGui.TextWrapped(Loc.T("onboarding.disclaimer_nsfl_body"));
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, red);
            ImGui.TextWrapped(Loc.T("onboarding.disclaimer_nsfl_zero_tolerance"));
            ImGui.PopStyleColor();
            ImGui.Spacing();

            DrawSectionHeading(Loc.T("onboarding.disclaimer_ai_heading"), t);
            ImGui.TextWrapped(Loc.T("onboarding.disclaimer_ai_body"));
            ImGui.Spacing();

            DrawSectionHeading(Loc.T("onboarding.disclaimer_rules_heading"), t);
            ImGui.TextWrapped(Loc.T("onboarding.disclaimer_rules_body1"));
            ImGui.Spacing();
            ImGui.TextWrapped(Loc.T("onboarding.disclaimer_rules_body2"));
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Px(14f));
            ImGui.TextColored(red, Loc.T("onboarding.disclaimer_permanent_ban"));
            ImGui.Spacing();
        }

        ImGui.Spacing();
        var BtnW = Px(200f);
        var cx = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX((cx - BtnW) * 0.5f);
        PushThemeButton(t);
        if (ImGui.Button(Loc.T("onboarding.disclaimer_continue"), new Vector2(BtnW, Px(30f))))
        {
            _imageDisclaimerAcknowledged = true;
        }
        PopThemeButton();
    }
}
