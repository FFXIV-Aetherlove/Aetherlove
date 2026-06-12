using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{

    private bool     _tosAccepted;
    private bool     _tosScrolledToBottom;
    private DateTime _tosTimerStart = DateTime.MinValue;
    private const double TosDuration = 15.0;

    private static string[] TosParagraphs =>
    [
        Loc.T("onboarding.tos_p1"),
        Loc.T("onboarding.tos_p2"),
        Loc.T("onboarding.tos_p3"),
        Loc.T("onboarding.tos_p4"),
        Loc.T("onboarding.tos_p5"),
        Loc.T("onboarding.tos_p6"),
        Loc.T("onboarding.tos_p7"),
    ];


    private void DrawStepTOS()
    {
        var centerX = ImGui.GetContentRegionAvail().X * 0.5f;

        if (_tosTimerStart == DateTime.MinValue && !_tosAccepted)
            _tosTimerStart = DateTime.Now;

        ImGui.Spacing();
        var Warn = Loc.T("onboarding.tos_read_carefully");
        ImGui.SetCursorPosX(centerX - ImGui.CalcTextSize(Warn).X * 0.5f);
        ImGui.TextColored(new Vector4(0.9f, 0.4f, 0.4f, 1f), Warn);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        var BottomH = Px(46f);
        var scrollH = ImGui.GetContentRegionAvail().Y - BottomH;
        using (var scroll = ImRaii.Child("##tosScroll", new Vector2(0f, scrollH), true))
        {
            if (scroll.Success)
            {
                foreach (var para in TosParagraphs)
                {
                    ImGui.TextWrapped(para);
                    ImGui.Spacing(); ImGui.Spacing();
                }
                var sy = ImGui.GetScrollY();
                var sm = ImGui.GetScrollMaxY();
                _tosScrolledToBottom = sm <= 1f || sy >= sm - Px(5f);
            }
        }
        ImGui.Spacing();

        if (_tosAccepted)
        {
            ImGui.TextColored(UiColors.Success,
                Loc.T("onboarding.tos_accepted"));
        }
        else
        {
#if DEBUG
            var remaining = 0.0; // Skip timer in debug builds
#else
            var elapsed   = (DateTime.Now - _tosTimerStart).TotalSeconds;
            var remaining = Math.Max(0.0, TosDuration - elapsed);
#endif
            if (remaining > 0)
            {
                ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                ImGui.TextColored(new Vector4(0.85f, 0.75f, 0.3f, 1f),
                    Loc.T("onboarding.tos_timer", (int)Math.Ceiling(remaining)));
                ImGui.PopTextWrapPos();
            }
            else if (!_tosScrolledToBottom)
            {
                ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                ImGui.TextColored(new Vector4(0.85f, 0.75f, 0.3f, 1f), Loc.T("onboarding.tos_scroll_bottom"));
                ImGui.PopTextWrapPos();
            }
            else
            {
                var BtnW = Px(140f);
                ImGui.SetCursorPosX(centerX - BtnW * 0.5f);
                ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.18f, 0.52f, 0.24f, 0.90f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.22f, 0.68f, 0.30f, 1.00f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.12f, 0.42f, 0.18f, 1.00f));
                if (ImGui.Button(Loc.T("onboarding.tos_i_agree"), new Vector2(BtnW, Px(30f)))) _tosAccepted = true;
                ImGui.PopStyleColor(3);
            }
        }
    }
}
