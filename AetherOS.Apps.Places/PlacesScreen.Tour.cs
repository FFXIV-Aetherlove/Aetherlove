using System.Numerics;
using AetherLove;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;

namespace AetherOS.Apps.Places;

public partial class PlacesScreen
{
    private const int TourSteps = 4;

    private int _tourStep;

    /// <summary>Runs the tour on the very first Places visit. The flag is stamped up front so leaving early
    /// sticks; the menu keeps it reachable afterwards.</summary>
    private void EnsureTourSeen()
    {
        var places = UiHost.Configuration.Places;
        if (places.SeenTour)
        {
            return;
        }
        places.SeenTour = true;
        UiHost.Configuration.Save();
        OpenTour();
    }

    private void OpenTour()
    {
        _tourStep = 0;
        _section = Section.Tour;
    }

    private void CloseTour()
    {
        _section = Section.Browse;
        _entrance.Arm();
    }

    /// <summary>The full-surface Places tour, laid out like the AetherOS onboarding: segmented progress bar,
    /// a scrolling step body, and a pinned primary button.</summary>
    private void DrawTour()
    {
        if (DrawProgress(_tourStep, TourSteps, true))
        {
            if (_tourStep == 0)
            {
                CloseTour();
            }
            else
            {
                _tourStep--;
            }
        }

        const float topH = 34f;
        const float navH = 62f;
        var contentH = ImGui.GetWindowSize().Y - Px(topH) - Px(navH);

        ImGui.SetCursorPos(new Vector2(0f, Px(topH)));
        PushScrollbarStyle();
        using (var content = ImRaii.Child("##placesTourContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_tourStep)
                {
                    case 0:
                        DrawTourWelcome();
                        break;
                    case 1:
                        DrawTourFilters();
                        break;
                    case 2:
                        DrawTourReviews();
                        break;
                    default:
                        DrawTourAdvertise();
                        break;
                }
            }
        }
        PopScrollbarStyle();

        var last = _tourStep >= TourSteps - 1;
        ImGui.SetCursorPos(new Vector2(0f, ImGui.GetWindowSize().Y - Px(54f)));
        if (DrawPrimaryButton(last ? Loc.T("common.got_it") : Loc.T("onboarding.next"), true))
        {
            if (last)
            {
                CloseTour();
            }
            else
            {
                _tourStep++;
            }
        }
    }

    private void DrawTourWelcome()
    {
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawHero("places_tour_welcome", FontAwesomeIcon.MapMarkedAlt, Loc.T("places.tour_welcome_title"),
            Loc.T("places.tour_welcome_body"), 40f);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.Store, Loc.T("places.tour_welcome_f1"));
        DrawFeatureRow(FontAwesomeIcon.CalendarAlt, Loc.T("places.tour_welcome_f2"));
        DrawFeatureRow(FontAwesomeIcon.Star, Loc.T("places.tour_welcome_f3"));
    }

    private void DrawTourFilters()
    {
        DrawHero("places_tour_filters", FontAwesomeIcon.Filter, Loc.T("places.tour_filters_title"),
            Loc.T("places.tour_filters_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Tags, Loc.T("places.tour_filters_tags"));
        DrawFeatureRow(FontAwesomeIcon.GlobeEurope, Loc.T("places.tour_filters_regions"));
        DrawFeatureRow(FontAwesomeIcon.ShieldAlt, Loc.T("places.tour_filters_nsfw"));
        DrawFeatureRow(FontAwesomeIcon.Clock, Loc.T("places.tour_filters_247"));
        DrawFeatureRow(FontAwesomeIcon.EyeSlash, Loc.T("places.tour_filters_hide"));
        DrawFeatureRow(FontAwesomeIcon.Search, Loc.T("places.tour_filters_search"));
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }

    private void DrawTourReviews()
    {
        DrawHero("places_tour_reviews", FontAwesomeIcon.Star, Loc.T("places.tour_reviews_title"),
            Loc.T("places.tour_reviews_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Star, Loc.T("places.tour_reviews_r1"));
        DrawFeatureRow(FontAwesomeIcon.Comments, Loc.T("places.tour_reviews_r2"));
        DrawFeatureRow(FontAwesomeIcon.Heart, Loc.T("places.tour_reviews_r3"));
    }

    private void DrawTourAdvertise()
    {
        DrawHero("places_tour_advertise", FontAwesomeIcon.Store, Loc.T("places.tour_ads_title"),
            Loc.T("places.tour_ads_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Gift, Loc.T("places.tour_ads_free"));
        DrawFeatureRow(FontAwesomeIcon.Users, Loc.T("places.tour_ads_reach"));
        DrawFeatureRow(FontAwesomeIcon.TicketAlt, Loc.T("places.tour_ads_how"));
        ImGui.Dummy(new Vector2(0f, Px(16f)));

        var margin = Px(30f);
        ImGui.SetCursorPosX(margin);
        ImGui.PushStyleColor(ImGuiCol.Button, UiColors.Discord with { W = 0.92f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.Discord);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.Discord with { W = 0.82f });
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(12f));
        using (UiFonts.H3?.Push())
        {
            if (ImGui.Button(Loc.T("places.addvenue_discord_btn"),
                    new Vector2(ImGui.GetWindowSize().X - margin * 2f, Px(42f))))
            {
                OpenDiscord();
            }
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        DrawCenteredParagraph(Loc.T("places.tour_reopen_hint"), ImGui.GetWindowSize().X - Px(48f), UiColors.Hint);
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }
}
