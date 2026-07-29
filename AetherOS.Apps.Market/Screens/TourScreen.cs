using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;

namespace AetherOS.Apps.Market;

/// <summary>The Market app tour: five steps walking through search, discovery, alerts, retainers, and
/// sharing. Hero art loads from Media/icons/market_tour_*.png with FontAwesome fallbacks.</summary>
internal sealed class TourScreen
{
    private const int TotalSteps = 5;

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
        using (var content = ImRaii.Child("##marketTourContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_step)
                {
                    case 0:
                        DrawWelcome();
                        break;
                    case 1:
                        DrawDiscover();
                        break;
                    case 2:
                        DrawAlerts();
                        break;
                    case 3:
                        DrawSales();
                        break;
                    default:
                        DrawShare();
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
        DrawHero("market_tour_welcome", FontAwesomeIcon.Coins, Loc.T("os.market_tour_welcome_title"),
            Loc.T("os.market_tour_welcome_body"), 40f);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.Search, Loc.T("os.market_tour_welcome_f1"));
        DrawFeatureRow(FontAwesomeIcon.ChartLine, Loc.T("os.market_tour_welcome_f2"));
        DrawFeatureRow(FontAwesomeIcon.GlobeEurope, Loc.T("os.market_tour_welcome_f3"));

        ImGui.Dummy(new Vector2(0f, Px(12f)));
        DrawCenteredParagraph(Loc.T("os.market_tour_universalis"), ImGui.GetWindowSize().X - Px(48f),
            ThemeService.Current.AccentLight);
    }

    private static void DrawDiscover()
    {
        DrawHero("market_tour_discover", FontAwesomeIcon.Gem, Loc.T("os.market_tour_discover_title"),
            Loc.T("os.market_tour_discover_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Crown, Loc.T("os.market_tour_discover_f1"));
        DrawFeatureRow(FontAwesomeIcon.Seedling, Loc.T("os.market_tour_discover_f2"));
        DrawFeatureRow(FontAwesomeIcon.ListUl, Loc.T("os.market_tour_discover_f3"));
    }

    private static void DrawAlerts()
    {
        DrawHero("market_tour_alerts", FontAwesomeIcon.Bell, Loc.T("os.market_tour_alerts_title"),
            Loc.T("os.market_tour_alerts_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Star, Loc.T("os.market_tour_alerts_f1"));
        DrawFeatureRow(FontAwesomeIcon.Bell, Loc.T("os.market_tour_alerts_f2"));
        DrawFeatureRow(FontAwesomeIcon.Comment, Loc.T("os.market_tour_alerts_f3"));
    }

    private static void DrawSales()
    {
        DrawHero("market_tour_sales", FontAwesomeIcon.Store, Loc.T("os.market_tour_sales_title"),
            Loc.T("os.market_tour_sales_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.ConciergeBell, Loc.T("os.market_tour_sales_f1"));
        DrawFeatureRow(FontAwesomeIcon.ArrowDown, Loc.T("os.market_tour_sales_f2"));
        DrawFeatureRow(FontAwesomeIcon.MoneyBillWave, Loc.T("os.market_tour_sales_f3"));
    }

    private static void DrawShare()
    {
        DrawHero("market_tour_share", FontAwesomeIcon.Share, Loc.T("os.market_tour_share_title"),
            Loc.T("os.market_tour_share_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Comments, Loc.T("os.market_tour_share_f1"));
        DrawFeatureRow(FontAwesomeIcon.Users, Loc.T("os.market_tour_share_f2"));

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        DrawCenteredParagraph(Loc.T("os.market_tour_reopen_hint"), ImGui.GetWindowSize().X - Px(48f), UiColors.Hint);
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }
}
