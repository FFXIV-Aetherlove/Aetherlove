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

namespace AetherOS.Apps.Wallet;

/// <summary>The Wallet tour: what sparks are, how earning works, the weekly cap explained with a live
/// mini ring demo, the currencies tab, and a confetti finale. Hero art loads from
/// Media/icons/wallet_tour_*.png with FontAwesome fallbacks.</summary>
internal sealed class TourScreen
{
    private const int TotalSteps = 5;
    private const float DemoLoopSeconds = 4.2f;
    private const int DemoRoutine = 300;
    private const int DemoExempt = 105;
    private const int DemoTotal = 450;

    private readonly Action _done;
    private readonly ConfettiBurst _confetti = new();
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
        using (var content = ImRaii.Child("##walletTourContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_step)
                {
                    case 0:
                        DrawWelcome();
                        break;
                    case 1:
                        DrawEarning();
                        break;
                    case 2:
                        DrawCap(ctx);
                        break;
                    case 3:
                        DrawCurrencies();
                        break;
                    default:
                        DrawFinale(ctx);
                        break;
                }
            }
        }
        PopScrollbarStyle();

        var last = _step >= TotalSteps - 1;
        ImGui.SetCursorPos(new Vector2(0f, ImGui.GetWindowSize().Y - Px(54f)));
        if (DrawPrimaryButton(last ? Loc.T("os.wallet_tour_start_btn") : Loc.T("onboarding.next"), true))
        {
            if (last)
            {
                _done();
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

    private static void DrawWelcome()
    {
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawHero("wallet_tour_welcome", FontAwesomeIcon.Wallet, Loc.T("os.wallet_tour_s0_title"),
            Loc.T("os.wallet_tour_s0_body"), 40f);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.Bolt, Loc.T("os.wallet_tour_s0_f1"));
        DrawFeatureRow(FontAwesomeIcon.CalendarWeek, Loc.T("os.wallet_tour_s0_f2"));
        DrawFeatureRow(FontAwesomeIcon.History, Loc.T("os.wallet_tour_s0_f3"));
    }

    private static void DrawEarning()
    {
        DrawHero("wallet_tour_earn", FontAwesomeIcon.Bolt, Loc.T("os.wallet_tour_s1_title"),
            Loc.T("os.wallet_tour_s1_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.SignInAlt, Loc.T("os.wallet_tour_s1_f1"));
        DrawFeatureRow(FontAwesomeIcon.Gamepad, Loc.T("os.wallet_tour_s1_f2"));
        DrawFeatureRow(FontAwesomeIcon.Compass, Loc.T("os.wallet_tour_s1_f3"));
        DrawFeatureRow(FontAwesomeIcon.Feather, Loc.T("os.wallet_tour_s1_f4"));
    }

    private void DrawCap(OsAppContext ctx)
    {
        // The ring demo is this step's hero, so the title and body render without a badge above them.
        var titleW = ImGui.GetWindowSize().X;
        ImGui.Dummy(new Vector2(0f, Px(16f)));
        using (UiFonts.H1?.Push())
        {
            var title = Loc.T("os.wallet_tour_s2_title");
            ImGui.SetCursorPosX(MathF.Max(Px(12f), (titleW - ImGui.CalcTextSize(title).X) * 0.5f));
            ImGui.TextUnformatted(title);
        }
        ImGui.Dummy(new Vector2(0f, Px(7f)));
        DrawCenteredParagraph(Loc.T("os.wallet_tour_s2_body"), titleW - Px(48f), new Vector4(0.72f, 0.72f, 0.75f, 1f));
        ImGui.Dummy(new Vector2(0f, Px(14f)));

        // The live demo: the ring fills to the routine tick, then on into the gold zone, looping.
        var winW = ImGui.GetWindowSize().X;
        var ringR = Px(58f);
        var center = new Vector2(ImGui.GetWindowPos().X + winW * 0.5f,
            ImGui.GetCursorScreenPos().Y + ringR + Px(10f));
        float reveal;
        if (ctx.ReduceMotion)
        {
            reveal = 1f;
        }
        else
        {
            var phase = (float)(ImGui.GetTime() % DemoLoopSeconds) / DemoLoopSeconds;
            // Fill over the first 70% of the loop, hold the full ring for the rest.
            reveal = Math.Clamp(phase / 0.7f, 0f, 1f);
            reveal = 1f - (1f - reveal) * (1f - reveal);
        }
        CapRing.Draw(center, ringR, Px(10f), DemoRoutine, DemoExempt, DemoRoutine, DemoTotal, reveal);
        ImGui.Dummy(new Vector2(0f, ringR * 2f + Px(24f)));

        DrawFeatureRow(FontAwesomeIcon.CircleNotch, Loc.T("os.wallet_tour_s2_f1"));
        DrawFeatureRow(FontAwesomeIcon.Star, Loc.T("os.wallet_tour_s2_f2"));
        DrawFeatureRow(FontAwesomeIcon.PiggyBank, Loc.T("os.wallet_tour_s2_f3"));
    }

    private static void DrawCurrencies()
    {
        DrawHero("wallet_tour_currencies", FontAwesomeIcon.Coins, Loc.T("os.wallet_tour_s3_title"),
            Loc.T("os.wallet_tour_s3_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.MoneyBillWave, Loc.T("os.wallet_tour_s3_f1"));
        DrawFeatureRow(FontAwesomeIcon.Landmark, Loc.T("os.wallet_tour_s3_f2"));
        DrawFeatureRow(FontAwesomeIcon.Hammer, Loc.T("os.wallet_tour_s3_f3"));
    }

    private void DrawFinale(OsAppContext ctx)
    {
        var t = ThemeService.Current;
        var wPos = ImGui.GetWindowPos();
        var wSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();

        if (!ctx.ReduceMotion)
        {
            var glowCenter = wPos + new Vector2(wSize.X * 0.5f, wSize.Y * 0.34f);
            var glowSpan = MathF.Min(wSize.X, wSize.Y);
            for (var i = 0; i < 5; i++)
            {
                var r = glowSpan * (0.14f + i * 0.11f);
                var a = 0.08f * (1f - i * 0.18f);
                dl.AddCircleFilled(glowCenter, r, ImGui.ColorConvertFloat4ToU32(t.Accent with { W = a }), 64);
            }
        }

        ImGui.Dummy(new Vector2(0f, wSize.Y * 0.13f));
        DrawHero("wallet_tour_done", FontAwesomeIcon.Check, Loc.T("os.wallet_tour_s4_title"),
            Loc.T("os.wallet_tour_s4_body"), 42f);

        ImGui.Dummy(new Vector2(0f, Px(12f)));
        DrawCenteredParagraph(Loc.T("os.wallet_tour_s4_hint"), wSize.X - Px(48f), UiColors.Success);

        if (!ctx.ReduceMotion)
        {
            _confetti.Draw(wPos, wPos + wSize);
        }
    }
}
