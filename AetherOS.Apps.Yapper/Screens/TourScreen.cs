using System;
using System.Numerics;
using AetherLove.Services.Localization;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;

namespace AetherOS.Apps.Yapper.Screens;

/// <summary>The Yapper tour: yapping, following, staying safe. Hero art loads from
/// Media/icons/yapper_tour_*.png with FontAwesome fallbacks.</summary>
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
        using (var content = ImRaii.Child("##yapTourContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                ImGui.Dummy(new Vector2(0f, Px(14f)));
                switch (_step)
                {
                    case 0:
                        DrawHero("yapper_tour_yap", FontAwesomeIcon.CommentDots,
                            Loc.T("os.yapper_tour1_title"), Loc.T("os.yapper_tour1_body"));
                        break;
                    case 1:
                        DrawHero("yapper_tour_follow", FontAwesomeIcon.UserFriends,
                            Loc.T("os.yapper_tour2_title"), Loc.T("os.yapper_tour2_body"));
                        break;
                    default:
                        DrawHero("yapper_tour_safety", FontAwesomeIcon.ShieldAlt,
                            Loc.T("os.yapper_tour3_title"), Loc.T("os.yapper_tour3_body"));
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
}
