using System.Numerics;
using AetherLove;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;

namespace AetherOS.Apps.Hangouts;

/// <summary>The Hangouts tour, laid out like the AetherOS onboarding: segmented progress bar, a scrolling step
/// body, and a pinned primary button. Drawn in-page over the directory.</summary>
internal sealed class HangoutsTour
{
    private const int TourSteps = 3;

    /// <summary>Near-opaque so the directory underneath reads as backdrop rather than competing content.</summary>
    private static readonly Vector4 Backdrop = new(0.07f, 0.065f, 0.09f, 0.97f);

    private int _step;
    private bool _open;

    public bool IsOpen => _open;

    /// <summary>Auto-runs the tour on first visit, stamping the seen-flag up front so early exit persists and the menu can reopen it.</summary>
    public void EnsureSeen()
    {
        var cfg = UiHost.Configuration.Hangouts;
        if (cfg.SeenTour)
        {
            return;
        }
        cfg.SeenTour = true;
        UiHost.Configuration.Save();
        Open();
    }

    public void Open()
    {
        _step = 0;
        _open = true;
    }

    public void Draw(Vector2 winPos, Vector2 winSize)
    {
        if (!_open)
        {
            return;
        }

        ImGui.SetCursorScreenPos(winPos);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Backdrop);
        using var overlay = ImRaii.Child("##hgTour", winSize, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        if (!overlay.Success)
        {
            return;
        }

        if (DrawProgress(_step, TourSteps, true))
        {
            if (_step == 0)
            {
                _open = false;
            }
            else
            {
                _step--;
            }
        }

        const float topH = 34f;
        const float navH = 62f;
        var contentH = winSize.Y - Px(topH) - Px(navH);

        ImGui.SetCursorPos(new Vector2(0f, Px(topH)));
        PushScrollbarStyle();
        using (var content = ImRaii.Child("##hgTourContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_step)
                {
                    case 0:
                        DrawTourWelcome();
                        break;
                    case 1:
                        DrawTourRules();
                        break;
                    default:
                        DrawTourFilters();
                        break;
                }
            }
        }
        PopScrollbarStyle();

        var last = _step >= TourSteps - 1;
        ImGui.SetCursorPos(new Vector2(0f, winSize.Y - Px(54f)));
        if (DrawPrimaryButton(last ? Loc.T("common.got_it") : Loc.T("onboarding.next"), true))
        {
            if (last)
            {
                _open = false;
            }
            else
            {
                _step++;
            }
        }

        // Submitted last so it only swallows clicks the step's own controls did not take.
        ImGui.SetCursorScreenPos(winPos);
        ImGui.InvisibleButton("##hgTourScrim", winSize);
    }

    private static void DrawTourWelcome()
    {
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawHero("hangouts_tour_welcome", FontAwesomeIcon.Bullhorn, Loc.T("hangout.tour_welcome_title"),
            Loc.T("hangout.tour_welcome_body"), 40f);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.Bullhorn, Loc.T("hangout.tour_welcome_f1"));
        DrawFeatureRow(FontAwesomeIcon.UserFriends, Loc.T("hangout.tour_welcome_f2"));
        DrawFeatureRow(FontAwesomeIcon.Clock, Loc.T("hangout.tour_welcome_f3"));
        DrawFeatureRow(FontAwesomeIcon.CheckCircle, Loc.T("hangout.tour_welcome_f4"));
        DrawFeatureRow(FontAwesomeIcon.PlusCircle, Loc.T("hangout.tour_welcome_f5"));
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }

    private static void DrawTourRules()
    {
        DrawHero("hangouts_tour_rules", FontAwesomeIcon.FileContract, Loc.T("hangout.tour_rules_title"),
            Loc.T("hangout.tour_rules_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Store, Loc.T("hangout.tour_rules_ads"));
        DrawFeatureRow(FontAwesomeIcon.Coins, Loc.T("hangout.tour_rules_paid"));
        DrawFeatureRow(FontAwesomeIcon.ShieldAlt, Loc.T("hangout.tour_rules_nsfw"));
        DrawFeatureRow(FontAwesomeIcon.UserShield, Loc.T("hangout.tour_rules_respect"));
        DrawFeatureRow(FontAwesomeIcon.CalendarCheck, Loc.T("hangout.tour_rules_show_up"));
        DrawFeatureRow(FontAwesomeIcon.Gavel, Loc.T("hangout.tour_rules_moderation"));
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }

    private static void DrawTourFilters()
    {
        DrawHero("hangouts_tour_filters", FontAwesomeIcon.Filter, Loc.T("hangout.tour_filters_title"),
            Loc.T("hangout.tour_filters_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Gamepad, Loc.T("hangout.tour_filters_activities"));
        DrawFeatureRow(FontAwesomeIcon.GlobeEurope, Loc.T("hangout.tour_filters_regions"));
        DrawFeatureRow(FontAwesomeIcon.LayerGroup, Loc.T("hangout.tour_filters_none"));
        DrawFeatureRow(FontAwesomeIcon.Bookmark, Loc.T("hangout.tour_filters_sticky"));
        ImGui.Dummy(new Vector2(0f, Px(16f)));

        DrawCenteredParagraph(Loc.T("hangout.tour_reopen_hint"), ImGui.GetWindowSize().X - Px(48f), UiColors.Hint);
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }
}
