using System;
using System.Numerics;
using AetherLove;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;
using static AetherLove.UI.SharedUiHelpers;
using static AetherLove.UI.UiScale;

namespace AetherOS.Apps.EchoVidya.Screens;

/// <summary>Echo's first-run flow: what it is, how watching together works, and a finale. The playback host
/// is not part of it any more: the phone's asset sync brings it in behind the scenes, and the finale only says
/// so when it has not landed yet. The step index survives leaving and coming back.</summary>
internal sealed class SetupScreen
{
    private const int TotalSteps = 3;
    private const string StepKey = "setupStep";
    private const float TopBarHeight = 34f;
    private const float NavHeight = 62f;

    private readonly IAppStorage _storage;
    private readonly IEchoHost _host;
    private readonly Action _done;
    private readonly ConfettiBurst _confetti = new();

    private int _step;

    public SetupScreen(IAppStorage storage, IEchoHost host, Action done)
    {
        _storage = storage;
        _host = host;
        _done = done;
    }

    /// <summary>Resumes at the step the user last reached; a step index from the four-step flow lands on the finale.</summary>
    public void OnShow() => OnShow(_storage.Get<int?>(StepKey) ?? 0);

    public void OnShow(int step)
    {
        _step = Math.Clamp(step, 0, TotalSteps - 1);
        if (_step == TotalSteps - 1)
        {
            _confetti.Reset();
        }
        Persist();
    }

    public void Draw(OsAppContext ctx)
    {
        if (DrawProgress(_step, TotalSteps, _step > 0))
        {
            GoTo(_step - 1);
        }

        var winH = ImGui.GetWindowSize().Y;
        var contentH = winH - Px(TopBarHeight) - Px(NavHeight);

        ImGui.SetCursorPos(new Vector2(0f, Px(TopBarHeight)));
        PushScrollbarStyle();
        using (var content = ImRaii.Child("##echoSetupContent", new Vector2(0f, contentH), false))
        {
            if (content.Success)
            {
                switch (_step)
                {
                    case 0:
                        DrawWelcome();
                        break;
                    case 1:
                        DrawTogether();
                        break;
                    default:
                        DrawFinale(ctx);
                        break;
                }
            }
        }
        PopScrollbarStyle();

        ImGui.SetCursorPos(new Vector2(0f, winH - Px(54f)));
        var label = _step == TotalSteps - 1 ? Loc.T("os.echo_setup_start_btn") : Loc.T("onboarding.next");
        if (DrawPrimaryButton(label, enabled: true))
        {
            Advance();
        }
    }

    private static void DrawWelcome()
    {
        ImGui.Dummy(new Vector2(0f, Px(14f)));
        DrawHero("echo_setup_welcome", FontAwesomeIcon.Film, Loc.T("os.echo_setup_s0_title"),
            Loc.T("os.echo_setup_s0_body"), 40f);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        DrawFeatureRow(FontAwesomeIcon.PlayCircle, Loc.T("os.echo_setup_s0_f1"));
        DrawFeatureRow(FontAwesomeIcon.Users, Loc.T("os.echo_setup_s0_f2"));
    }

    private static void DrawTogether()
    {
        DrawHero("echo_setup_together", FontAwesomeIcon.Users, Loc.T("os.echo_setup_s1_title"),
            Loc.T("os.echo_setup_s1_body"), 30f);

        DrawFeatureRow(FontAwesomeIcon.Hashtag, Loc.T("os.echo_setup_s1_f1"));
        DrawFeatureRow(FontAwesomeIcon.ListUl, Loc.T("os.echo_setup_s1_f2"));
        DrawFeatureRow(FontAwesomeIcon.Comments, Loc.T("os.echo_setup_s1_f3"));
        DrawFeatureRow(FontAwesomeIcon.Crown, Loc.T("os.echo_setup_s1_f4"));
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
        DrawHero("echo_setup_done", FontAwesomeIcon.CheckCircle, Loc.T("os.echo_setup_s3_title"),
            Loc.T("os.echo_setup_s3_body"), 42f);

        ImGui.Dummy(new Vector2(0f, Px(12f)));
        var ready = _host.RuntimeReady;
        DrawCenteredParagraph(Loc.T(ready ? "os.echo_setup_s3_hint" : "os.echo_setup_player_pending"),
            wSize.X - Px(48f), ready ? UiColors.Success : UiColors.Amber);

        if (!ctx.ReduceMotion)
        {
            _confetti.Draw(wPos, wPos + wSize);
        }
    }

    private void Advance()
    {
        if (_step == TotalSteps - 1)
        {
            _done();
            return;
        }
        GoTo(_step + 1);
    }

    private void GoTo(int step)
    {
        _step = Math.Clamp(step, 0, TotalSteps - 1);
        if (_step == TotalSteps - 1)
        {
            _confetti.Reset();
        }
        Persist();
    }

    private void Persist() => _storage.Set(StepKey, _step);
}
