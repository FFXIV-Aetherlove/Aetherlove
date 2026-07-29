using System.Numerics;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;

namespace AetherOS.Apps.Levemetes;

public partial class LevemetesScreen
{
    private const int TourSteps = 5;

    private int _tourStep;

    /// <summary>Runs the tour on the very first Levemetes visit. The flag is stamped up front so leaving
    /// early sticks; the menu keeps it reachable afterwards.</summary>
    private void EnsureTourSeen()
    {
        var state = UiHost.Configuration.Levemetes;
        if (state.SeenTour)
        {
            return;
        }
        state.SeenTour = true;
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
        using (var content = ImRaii.Child("##leveTourContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_tourStep)
                {
                    case 0:
                        DrawTourWelcome();
                        break;
                    case 1:
                        DrawTourPost();
                        break;
                    case 2:
                        DrawTourTerms();
                        break;
                    case 3:
                        DrawTourContact();
                        break;
                    default:
                        DrawTourSafety();
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
        DrawHero("leve_tour_welcome", FontAwesomeIcon.Scroll, Loc.T("os.leve_tour_welcome_title"),
            Loc.T("os.leve_tour_welcome_body"), 40f);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.Search, Loc.T("os.leve_tour_welcome_f1"));
        DrawFeatureRow(FontAwesomeIcon.Scroll, Loc.T("os.leve_tour_welcome_f2"));
        DrawFeatureRow(FontAwesomeIcon.Users, Loc.T("os.leve_tour_welcome_f3"));
    }

    private void DrawTourPost()
    {
        DrawHero("leve_tour_post", FontAwesomeIcon.Edit, Loc.T("os.leve_tour_post_title"),
            Loc.T("os.leve_tour_post_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.LayerGroup, Loc.T("os.leve_tour_post_p1"));
        DrawFeatureRow(FontAwesomeIcon.Clock, Loc.T("os.leve_tour_post_p2"));
        DrawFeatureRow(FontAwesomeIcon.Image, Loc.T("os.leve_tour_post_p3"));
        DrawFeatureRow(FontAwesomeIcon.ShieldAlt, Loc.T("os.leve_tour_post_p4"));
    }

    private void DrawTourTerms()
    {
        var t = ThemeService.Current;
        DrawHero("leve_tour_terms", FontAwesomeIcon.FileContract, Loc.T("os.leve_tour_terms_title"),
            Loc.T("os.leve_tour_terms_body"), 30f);

        DrawTermsHeading(Loc.T("os.leve_tour_terms_allowed"), t.AccentLight);
        DrawFeatureRow(FontAwesomeIcon.Check, Loc.T("os.leve_tour_terms_a1"));
        DrawFeatureRow(FontAwesomeIcon.Check, Loc.T("os.leve_tour_terms_a2"));
        DrawFeatureRow(FontAwesomeIcon.Check, Loc.T("os.leve_tour_terms_a3"));

        DrawTermsHeading(Loc.T("os.leve_tour_terms_denied"), UiColors.Danger);
        DrawFeatureRow(FontAwesomeIcon.Times, Loc.T("os.leve_tour_terms_d1"));
        DrawFeatureRow(FontAwesomeIcon.Times, Loc.T("os.leve_tour_terms_d2"));
        DrawFeatureRow(FontAwesomeIcon.Times, Loc.T("os.leve_tour_terms_d3"));
        DrawFeatureRow(FontAwesomeIcon.Times, Loc.T("os.leve_tour_terms_d4"));
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }

    private static void DrawTermsHeading(string text, Vector4 color)
    {
        ImGui.Dummy(new Vector2(0f, Px(10f)));
        ImGui.SetCursorPosX(Px(30f));
        ImGui.TextColored(color, text);
    }

    private void DrawTourContact()
    {
        DrawHero("leve_tour_contact", FontAwesomeIcon.Comment, Loc.T("os.leve_tour_contact_title"),
            Loc.T("os.leve_tour_contact_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Comment, Loc.T("os.leve_tour_contact_c1"));
        DrawFeatureRow(FontAwesomeIcon.Star, Loc.T("os.leve_tour_contact_c2"));
        DrawFeatureRow(FontAwesomeIcon.Share, Loc.T("os.leve_tour_contact_c3"));
    }

    private void DrawTourSafety()
    {
        DrawHero("leve_tour_safety", FontAwesomeIcon.EyeSlash, Loc.T("os.leve_tour_safety_title"),
            Loc.T("os.leve_tour_safety_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Filter, Loc.T("os.leve_tour_safety_s1"));
        DrawFeatureRow(FontAwesomeIcon.EyeSlash, Loc.T("os.leve_tour_safety_s2"));
        DrawFeatureRow(FontAwesomeIcon.Flag, Loc.T("os.leve_tour_safety_s3"));

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        DrawCenteredParagraph(Loc.T("os.leve_tour_reopen_hint"), ImGui.GetWindowSize().X - Px(48f), UiColors.Hint);
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }
}
