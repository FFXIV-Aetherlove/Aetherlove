using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;

namespace AetherOS.Apps.Wayfinder;

/// <summary>The Wayfinder tour: what the game is, how a challenge plays out, and the daily rhythm. Hero art
/// loads from Media/icons/wayfinder_tour_*.png with FontAwesome fallbacks.</summary>
internal sealed class TourScreen
{
    private const int TotalSteps = 3;

    private readonly Action _done;
    private int _step;

    public TourScreen(Action done)
    {
        _done = done;
    }

    public void OnShow()
    {
        _step = 0;
    }

    public void Draw(OsAppContext ctx)
    {
        if (DrawProgress(_step, TotalSteps, true))
        {
            if (_step == 0)
            {
                _done();
            }
            else
            {
                _step--;
            }
        }

        const float topH = 34f;
        const float navH = 62f;
        var contentH = ImGui.GetWindowSize().Y - Px(topH) - Px(navH);

        ImGui.SetCursorPos(new Vector2(0f, Px(topH)));
        PushScrollbarStyle();
        using (var content = ImRaii.Child("##wfTourContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_step)
                {
                    case 0:
                        DrawWelcome();
                        break;
                    case 1:
                        DrawPlay();
                        break;
                    default:
                        DrawDaily();
                        break;
                }
            }
        }
        PopScrollbarStyle();

        var last = _step >= TotalSteps - 1;
        ImGui.SetCursorPos(new Vector2(0f, ImGui.GetWindowSize().Y - Px(54f)));
        if (DrawPrimaryButton(last ? Loc.T("common.got_it") : Loc.T("onboarding.next"), true))
        {
            if (last)
            {
                _done();
            }
            else
            {
                _step++;
            }
        }
    }

    private static void DrawWelcome()
    {
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawHero("wayfinder_tour_welcome", FontAwesomeIcon.Compass, Loc.T("os.wf_tour_welcome_title"),
            Loc.T("os.wf_tour_welcome_body"), 40f);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.Image, Loc.T("os.wf_tour_welcome_f1"));
        DrawFeatureRow(FontAwesomeIcon.MapMarkedAlt, Loc.T("os.wf_tour_welcome_f2"));
        DrawFeatureRow(FontAwesomeIcon.CameraRetro, Loc.T("os.wf_tour_welcome_f3"));
        DrawFeatureRow(FontAwesomeIcon.Thermometer, Loc.T("os.wf_tour_welcome_f4"));
    }

    private static void DrawPlay()
    {
        DrawHero("wayfinder_tour_play", FontAwesomeIcon.HourglassHalf, Loc.T("os.wf_tour_play_title"),
            Loc.T("os.wf_tour_play_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Clock, Loc.T("os.wf_tour_play_f1"));
        DrawFeatureRow(FontAwesomeIcon.Fire, Loc.T("os.wf_tour_play_f2"));
        DrawFeatureRow(FontAwesomeIcon.Redo, Loc.T("os.wf_tour_play_f3"));
        DrawFeatureRow(FontAwesomeIcon.Bolt, Loc.T("os.wf_tour_play_f4"));
    }

    private static void DrawDaily()
    {
        DrawHero("wayfinder_tour_daily", FontAwesomeIcon.CalendarDay, Loc.T("os.wf_tour_daily_title"),
            Loc.T("os.wf_tour_daily_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Dice, Loc.T("os.wf_tour_daily_f1"));
        DrawFeatureRow(FontAwesomeIcon.Trophy, Loc.T("os.wf_tour_daily_f2"));
        DrawFeatureRow(FontAwesomeIcon.Images, Loc.T("os.wf_tour_daily_f3"));

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        DrawCenteredParagraph(Loc.T("os.wf_tour_reopen_hint"), ImGui.GetWindowSize().X - Px(48f), UiColors.Hint);
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }
}
