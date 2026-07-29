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

namespace AetherOS.Apps.Realtor;

/// <summary>The Realtor app tour: three steps covering browsing, the lottery, and where the data comes
/// from. Hero art loads from Media/icons/realtor_tour_*.png with FontAwesome fallbacks.</summary>
internal sealed class TourScreen
{
    private const int TotalSteps = 3;
    private const string PaissaUrl = "https://zhu.codes/paissa";

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
        using (var content = ImRaii.Child("##realtorTourContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_step)
                {
                    case 0:
                        DrawWelcome(ctx);
                        break;
                    case 1:
                        DrawBrowse();
                        break;
                    default:
                        DrawLottery();
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

    private static void DrawWelcome(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawHero("realtor_tour_welcome", FontAwesomeIcon.Home, Loc.T("os.realtor_tour_welcome_title"),
            Loc.T("os.realtor_tour_welcome_body"), 40f);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.GlobeEurope, Loc.T("os.realtor_tour_welcome_f1"));
        DrawFeatureRow(FontAwesomeIcon.Filter, Loc.T("os.realtor_tour_welcome_f2"));
        DrawFeatureRow(FontAwesomeIcon.Clock, Loc.T("os.realtor_tour_welcome_f3"));

        ImGui.Dummy(new Vector2(0f, Px(12f)));
        DrawCenteredParagraph(Loc.T("os.realtor_tour_credit"), ImGui.GetWindowSize().X - Px(48f),
            ThemeService.Current.AccentLight);
        ImGui.Dummy(new Vector2(0f, Px(4f)));
        DrawLink(ctx, Loc.T("os.realtor_tour_credit_link"));
    }

    private static void DrawBrowse()
    {
        DrawHero("realtor_tour_browse", FontAwesomeIcon.MapMarkedAlt, Loc.T("os.realtor_tour_browse_title"),
            Loc.T("os.realtor_tour_browse_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Globe, Loc.T("os.realtor_tour_browse_f1"));
        DrawFeatureRow(FontAwesomeIcon.Home, Loc.T("os.realtor_tour_browse_f2"));
        DrawFeatureRow(FontAwesomeIcon.Filter, Loc.T("os.realtor_tour_browse_f3"));
    }

    private static void DrawLottery()
    {
        DrawHero("realtor_tour_lottery", FontAwesomeIcon.TicketAlt, Loc.T("os.realtor_tour_lotto_title"),
            Loc.T("os.realtor_tour_lotto_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.HourglassHalf, Loc.T("os.realtor_tour_lotto_f1"));
        DrawFeatureRow(FontAwesomeIcon.Users, Loc.T("os.realtor_tour_lotto_f2"));
        DrawFeatureRow(FontAwesomeIcon.History, Loc.T("os.realtor_tour_lotto_f3"));

        ImGui.Dummy(new Vector2(0f, Px(16f)));
        DrawCenteredParagraph(Loc.T("os.realtor_tour_reopen_hint"), ImGui.GetWindowSize().X - Px(48f), UiColors.Hint);
        ImGui.Dummy(new Vector2(0f, Px(10f)));
    }

    private static void DrawLink(OsAppContext ctx, string label)
    {
        var t = ThemeService.Current;
        var sz = ImGui.CalcTextSize(label);
        var winW = ImGui.GetWindowSize().X;
        ImGui.SetCursorPosX((winW - sz.X) * 0.5f);
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##realtorPaissaLink", new Vector2(sz.X, sz.Y + Px(2f)));
        HandOnHover();
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var color = ImGui.GetColorU32(hovered ? t.AccentLight : t.Accent);
        dl.AddText(tl, color, label);
        dl.AddLine(new Vector2(tl.X, tl.Y + sz.Y + Px(1f)), new Vector2(tl.X + sz.X, tl.Y + sz.Y + Px(1f)),
            color, Px(1f));
        if (hovered)
        {
            ImGui.SetTooltip(PaissaUrl);
        }
        if (clicked)
        {
            ctx.Capabilities.System.OpenUrl(PaissaUrl);
        }
    }
}
