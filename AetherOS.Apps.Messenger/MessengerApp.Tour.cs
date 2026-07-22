using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Messenger;

/// <summary>The first-run tour: what the messenger is, the house rules, how image storage behaves, and the
/// user's own friend code to share. Shown once automatically and re-openable from the menu.</summary>
public sealed partial class MessengerApp
{
    private enum TourStep
    {
        Welcome = 0,
        Terms = 1,
        Storage = 2,
        Done = 3,
    }

    private const int TourTotalSteps = 4;

    private TourStep _tourStep;
    private bool _tourSeen;
    private bool _tourSeenLoaded;
    private bool _tourScrollToTop;

    /// <summary>True once on a cold open for an account that has never taken the tour, so it runs before the
    /// chat list is ever shown.</summary>
    private bool ShouldAutoRunTour()
    {
        if (!_tourSeenLoaded)
        {
            _tourSeenLoaded = true;
            _tourSeen = _caps.Storage(Id).Get<bool?>("tourSeen") ?? false;
        }
        return !_tourSeen;
    }

    private void OpenTour()
    {
        _tourStep = TourStep.Welcome;
        _tourScrollToTop = true;
        _view = View.Tour;
        _openFadeAt = -1;
    }

    private void FinishTour()
    {
        _tourSeen = true;
        _tourSeenLoaded = true;
        _caps.Storage(Id).Set("tourSeen", (bool?)true);
        _view = View.List;
        _openFadeAt = -1;
    }

    private void DrawTour(OsAppContext ctx)
    {
        if (DrawProgress((int)_tourStep, TourTotalSteps, _tourStep != TourStep.Welcome))
        {
            _tourStep = (TourStep)((int)_tourStep - 1);
            _tourScrollToTop = true;
        }

        const float topH = 34f;
        const float navH = 62f;
        var contentH = ImGui.GetWindowSize().Y - Px(topH) - Px(navH);

        ImGui.SetCursorPos(new Vector2(0f, Px(topH)));
        PushScrollbarStyle();
        using (var content = ImRaii.Child("##msgrTourContent", new Vector2(0f, contentH), false))
        {
            PopScrollbarStyle();
            if (content.Success)
            {
                if (_tourScrollToTop)
                {
                    _tourScrollToTop = false;
                    ImGui.SetScrollY(0f);
                }
                switch (_tourStep)
                {
                    case TourStep.Welcome:
                        DrawTourWelcome();
                        break;
                    case TourStep.Terms:
                        DrawTourTerms();
                        break;
                    case TourStep.Storage:
                        DrawTourStorage();
                        break;
                    case TourStep.Done:
                        DrawTourDone();
                        break;
                }
            }
        }

        ImGui.SetCursorPos(new Vector2(0f, ImGui.GetWindowSize().Y - Px(54f)));
        var label = _tourStep == TourStep.Done ? Loc.T("os.msgr_tour_start") : Loc.T("onboarding.next");
        if (DrawPrimaryButton(label, true))
        {
            if (_tourStep == TourStep.Done)
            {
                FinishTour();
            }
            else
            {
                _tourStep = (TourStep)((int)_tourStep + 1);
                _tourScrollToTop = true;
            }
        }
    }

    private void DrawTourWelcome()
    {
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawHero("msgr_welcome", Loc.T("os.msgr_tour_welcome_title"),
            Loc.T("os.msgr_tour_welcome_body"), 40f);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.Comments, Loc.T("os.msgr_tour_welcome_direct"));
        DrawFeatureRow(FontAwesomeIcon.Users, Loc.T("os.msgr_tour_welcome_groups"));
        DrawFeatureRow(FontAwesomeIcon.MapMarkerAlt, Loc.T("os.msgr_tour_welcome_location"));
        DrawFeatureRow(FontAwesomeIcon.Image, Loc.T("os.msgr_tour_welcome_images"));
        DrawFeatureRow(FontAwesomeIcon.IdCard, Loc.T("os.msgr_tour_welcome_code"));
        DrawFeatureRow(FontAwesomeIcon.UserShield, Loc.T("os.msgr_tour_welcome_control"));
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }

    private void DrawTourTerms()
    {
        DrawHero("msgr_terms", Loc.T("os.msgr_tour_terms_title"),
            Loc.T("os.msgr_tour_terms_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Ban, Loc.T("os.msgr_tour_terms_hate"));
        DrawFeatureRow(FontAwesomeIcon.HandPaper, Loc.T("os.msgr_tour_terms_harass"));
        DrawFeatureRow(FontAwesomeIcon.ExclamationTriangle, Loc.T("os.msgr_tour_terms_minors"));
        DrawFeatureRow(FontAwesomeIcon.Bullhorn, Loc.T("os.msgr_tour_terms_spam"));
        DrawFeatureRow(FontAwesomeIcon.Gavel, Loc.T("os.msgr_tour_terms_illegal"));
        DrawFeatureRow(FontAwesomeIcon.Flag, Loc.T("os.msgr_tour_terms_report"));
        DrawFeatureRow(FontAwesomeIcon.Lock, Loc.T("os.msgr_tour_terms_e2e"));
        DrawFeatureRow(FontAwesomeIcon.EyeSlash, Loc.T("os.msgr_tour_terms_images"));
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }

    private void DrawTourStorage()
    {
        DrawHero("msgr_storage", Loc.T("os.msgr_tour_storage_title"),
            Loc.T("os.msgr_tour_storage_body"), 30f);

        var freeMb = SupporterLimits.RegularImageStorageBytes / (1024 * 1024);
        var supporterMb = SupporterLimits.SupporterImageStorageBytes / (1024 * 1024);

        DrawFeatureRow(FontAwesomeIcon.HourglassHalf, Loc.T("os.msgr_tour_storage_expiry",
            SupporterLimits.RegularImageTtlHours, SupporterLimits.SupporterImageTtlHours / 24));
        DrawFeatureRow(FontAwesomeIcon.Hdd, Loc.T("os.msgr_tour_storage_quota", freeMb, supporterMb));
        DrawFeatureRow(FontAwesomeIcon.Recycle, Loc.T("os.msgr_tour_storage_frees"));
        DrawFeatureRow(FontAwesomeIcon.Database, Loc.T("os.msgr_tour_storage_where"));
        DrawFeatureRow(FontAwesomeIcon.CommentDots, Loc.T("os.msgr_tour_storage_text_note"));
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }

    private void DrawTourDone()
    {
        var winW = ImGui.GetWindowSize().X;
        DrawHero("msgr_done", Loc.T("os.msgr_tour_done_title"),
            Loc.T("os.msgr_tour_done_body"), 30f);

        if (_store.Sync?.MyCode is { Length: > 0 } myCode)
        {
            if (DrawSecretBox("##msgrTourCodeBox", MessengerCodeDisplay(myCode), Loc.T("os.msgr_copy_code")))
            {
                _caps.System.CopyToClipboard(myCode);
            }
        }
        else
        {
            DrawCenteredParagraph(Loc.T("os.msgr_tour_done_code_pending"), winW - Px(48f), UiColors.Hint);
        }

        ImGui.Dummy(new Vector2(0f, Px(18f)));
        DrawCenteredParagraph(Loc.T("os.msgr_tour_reopen_hint"), winW - Px(48f), UiColors.Hint);
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }
}
