using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{
    private bool _imageRulesScrolledToBottom;

    /// <summary>Displays photo-rules that must be read before the Next button unlocks.</summary>
    private void DrawStepImageRules()
    {
        DrawHero("love_imagerules", FontAwesomeIcon.ShieldAlt, Loc.T("onboarding.img_rules_title"),
            Loc.T("onboarding.img_rules_intro"), 30f);

        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var boxH = ImGui.GetContentRegionAvail().Y - Px(6f);

        ImGui.SetCursorPosX(Px(16f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(1f, 1f, 1f, 0.045f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(14f), Px(12f)));
        using (var box = ImRaii.Child("##imgRules", new Vector2(winW - Px(32f), boxH), false))
        {
            if (box.Success)
            {
                ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                DrawRule(FontAwesomeIcon.UserCircle, Loc.T("onboarding.img_rules_character"), t.AccentU32);
                DrawRule(FontAwesomeIcon.EyeSlash, Loc.T("onboarding.img_rules_sfw"), t.AccentU32);
                DrawRule(FontAwesomeIcon.Ban, Loc.T("onboarding.img_rules_illegal"),
                    ImGui.ColorConvertFloat4ToU32(UiColors.Danger));
                DrawRule(FontAwesomeIcon.Check, Loc.T("onboarding.img_rules_consent"), t.AccentU32);
                ImGui.PopTextWrapPos();

                // Content fits (no scroll) or the user reached the end: unlock the Next button.
                if (ImGui.GetScrollMaxY() <= 1f || ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - Px(4f))
                {
                    _imageRulesScrolledToBottom = true;
                }
            }
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();
    }

    private static void DrawRule(FontAwesomeIcon icon, string text, uint iconCol)
    {
        var dl = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        IconDraw.AddCentered(dl, icon, Px(16f), start + new Vector2(Px(9f), ImGui.GetTextLineHeight() * 0.5f), iconCol);
        ImGui.Indent(Px(28f));
        ImGui.TextColored(new Vector4(0.82f, 0.82f, 0.84f, 1f), text);
        ImGui.Unindent(Px(28f));
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }
}
