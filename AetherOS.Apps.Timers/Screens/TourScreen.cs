using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;

namespace AetherOS.Apps.Timers.Screens;

/// <summary>The one-time Timers tour: what the app is, resets, activities, the retainer and fleet books,
/// and reminders. The last step's primary button deep-links into the Reminders screen.</summary>
internal sealed class TourScreen
{
    private const int TotalSteps = 5;

    private readonly Action<bool> _done;
    private readonly ConfettiBurst _confetti = new();
    private int _step;

    internal TourScreen(Action<bool> done)
    {
        _done = done;
    }

    internal void OnShow()
    {
        _step = 0;
    }

    internal void Draw(OsAppContext ctx)
    {
        if (DrawProgress(_step, TotalSteps, true))
        {
            if (_step == 0)
            {
                _done(false);
            }
            else
            {
                _step--;
            }
        }

        var contentH = ImGui.GetWindowSize().Y - ctx.Px(34f) - ctx.Px(62f);
        ImGui.SetCursorPos(new Vector2(0f, ctx.Px(34f)));
        PushScrollbarStyle();
        using (var content = ImRaii.Child("##timersTourContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_step)
                {
                    case 0:
                        DrawWelcome(ctx);
                        break;
                    case 1:
                        DrawResets(ctx);
                        break;
                    case 2:
                        DrawActivities(ctx);
                        break;
                    case 3:
                        DrawFleet(ctx);
                        break;
                    default:
                        DrawReminders(ctx);
                        break;
                }
            }
        }
        PopScrollbarStyle();

        var last = _step >= TotalSteps - 1;
        ImGui.SetCursorPos(new Vector2(0f, ImGui.GetWindowSize().Y - ctx.Px(54f)));
        if (DrawPrimaryButton(Loc.T(last ? "os.timers_tour_reminders_btn" : "os.timers_tour_next"), true))
        {
            if (last)
            {
                _done(true);
            }
            else
            {
                _step++;
                if (_step == TotalSteps - 1)
                {
                    _confetti.Reset();
                }
            }
        }
    }

    private static void DrawWelcome(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(0f, ctx.Px(14f)));
        DrawHero("timers_tour_welcome", FontAwesomeIcon.HourglassHalf, Loc.T("os.timers_tour_s0_title"),
            Loc.T("os.timers_tour_s0_body"), 40f);
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.Clock, Loc.T("os.timers_tour_s0_f1"));
        DrawFeatureRow(FontAwesomeIcon.Users, Loc.T("os.timers_tour_s0_f2"));
        DrawFeatureRow(FontAwesomeIcon.Plug, Loc.T("os.timers_tour_s0_f3"));
    }

    private static void DrawResets(OsAppContext ctx)
    {
        DrawHero("timers_tour_resets", FontAwesomeIcon.Sun, Loc.T("os.timers_tour_s1_title"),
            Loc.T("os.timers_tour_s1_body"), 32f);
        DrawFeatureRow(FontAwesomeIcon.Sun, Loc.T("os.timers_tour_s1_f1"));
        DrawFeatureRow(FontAwesomeIcon.CalendarWeek, Loc.T("os.timers_tour_s1_f2"));
        DrawFeatureRow(FontAwesomeIcon.CalendarPlus, Loc.T("os.timers_tour_s1_f3"));
    }

    private static void DrawActivities(OsAppContext ctx)
    {
        DrawHero("timers_tour_activities", FontAwesomeIcon.Fish, Loc.T("os.timers_tour_s2_title"),
            Loc.T("os.timers_tour_s2_body"), 32f);
        DrawFeatureRow(FontAwesomeIcon.Tshirt, Loc.T("os.timers_tour_s2_f1"));
        DrawFeatureRow(FontAwesomeIcon.Dice, Loc.T("os.timers_tour_s2_f2"));
        DrawFeatureRow(FontAwesomeIcon.Fish, Loc.T("os.timers_tour_s2_f3"));
    }

    private static void DrawFleet(OsAppContext ctx)
    {
        DrawHero("timers_tour_fleet", FontAwesomeIcon.Users, Loc.T("os.timers_tour_s3_title"),
            Loc.T("os.timers_tour_s3_body"), 32f);
        DrawFeatureRow(FontAwesomeIcon.CheckCircle, Loc.T("os.timers_tour_s3_f1"));
        DrawFeatureRow(FontAwesomeIcon.ChevronDown, Loc.T("os.timers_tour_s3_f2"));
        DrawFeatureRow(FontAwesomeIcon.Bell, Loc.T("os.timers_tour_s3_f3"));
    }

    private void DrawReminders(OsAppContext ctx)
    {
        var t = ThemeService.Current;
        var wPos = ImGui.GetWindowPos();
        var wSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();

        if (!ctx.ReduceMotion)
        {
            var glowCenter = wPos + new Vector2(wSize.X * 0.5f, wSize.Y * 0.32f);
            var glowSpan = MathF.Min(wSize.X, wSize.Y);
            for (var i = 0; i < 5; i++)
            {
                var r = glowSpan * (0.14f + i * 0.11f);
                var a = 0.08f * (1f - i * 0.18f);
                dl.AddCircleFilled(glowCenter, r, ImGui.ColorConvertFloat4ToU32(t.Accent with { W = a }), 64);
            }
        }

        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
        DrawHero("timers_tour_reminders", FontAwesomeIcon.Bell, Loc.T("os.timers_tour_s4_title"),
            Loc.T("os.timers_tour_s4_body"), 38f);
        DrawFeatureRow(FontAwesomeIcon.Bell, Loc.T("os.timers_tour_s4_f1"));
        DrawFeatureRow(FontAwesomeIcon.CalendarPlus, Loc.T("os.timers_tour_s4_f2"));

        ImGui.Dummy(new Vector2(0f, ctx.Px(12f)));
        DrawCenteredParagraph(Loc.T("os.timers_tour_s4_hint"), wSize.X - ctx.Px(48f), UiColors.Success);

        if (!ctx.ReduceMotion)
        {
            _confetti.Draw(wPos, wPos + wSize);
        }
    }
}
