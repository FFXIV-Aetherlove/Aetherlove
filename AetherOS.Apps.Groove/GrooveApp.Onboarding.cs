using System.Numerics;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Groove;

/// <summary>The two screens shown once, before the app is usable, because this is the only app on the phone
/// that reads something off the player's actual computer. The first says what it reads, that it never
/// leaves the machine, and how to refuse; the second is the surface toggles, which are otherwise buried in
/// a settings page nobody opens.
///
/// <para>It is shown to EXISTING users too, on their next launch. They have been using the app without ever
/// being told what it reads, and that is exactly the group the explanation is for.</para></summary>
public sealed partial class GrooveApp
{
    private int _onboardStep;

    private void DrawOnboarding(OsAppContext ctx)
    {
        var winW = ImGui.GetWindowSize().X;
        var padX = Px(PadX);
        var width = winW - (padX * 2f);

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        if (OnboardingUi.DrawProgress(_onboardStep, 2, canGoBack: _onboardStep > 0))
        {
            _onboardStep--;
        }

        if (_onboardStep == 0)
        {
            DrawOnboardingWhat(ctx, width);
            return;
        }
        DrawOnboardingControls(ctx, padX, width);
    }

    /// <summary>What the app reads, and the promise about it. The live track is the point: showing somebody
    /// their own music is a far better explanation of what is being read than any sentence about it.</summary>
    private void DrawOnboardingWhat(OsAppContext ctx, float width)
    {
        OnboardingUi.DrawHero("groove_tour_media", FontAwesomeIcon.Music,
            Loc.T("os.groove_ob_what_title"), Loc.T("os.groove_ob_what_sub"));
        OnboardingUi.DrawCenteredParagraph(Loc.T("os.groove_ob_what_body"), width, UiColors.Body);
        ImGui.Dummy(new Vector2(0f, Px(10f)));

        if (_host.Current is { } session)
        {
            DrawOnboardingSample(session, width);
        }

        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.Desktop, Loc.T("os.groove_ob_what_f1"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.EyeSlash, Loc.T("os.groove_ob_what_f2"));
        OnboardingUi.DrawFeatureRow(FontAwesomeIcon.SlidersH, Loc.T("os.groove_ob_what_f3"));

        ImGui.Dummy(new Vector2(0f, Px(6f)));
        OnboardingUi.DrawInfoCallout(Loc.T("os.groove_ob_optout"), UiColors.Subtle, FontAwesomeIcon.TrashAlt);

        ImGui.Dummy(new Vector2(0f, Px(12f)));
        if (OnboardingUi.DrawPrimaryButton(Loc.T("os.groove_ob_next"), enabled: true))
        {
            _onboardStep = 1;
        }
        ImGui.Dummy(new Vector2(0f, Px(16f)));
    }

    /// <summary>The very thing being described, read live off the machine, so the sentence above it is
    /// checkable rather than a claim. Absent when nothing is playing, since an empty card explains
    /// nothing.</summary>
    private void DrawOnboardingSample(GrooveSession session, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var padX = Px(PadX);
        var h = Px(64f);
        ImGui.SetCursorPosX(padX);
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(width, h);
        dl.AddRectFilled(tl, br, OsDrawShared.White(0.06f), Px(12f));
        dl.AddRect(tl, br, OsDrawShared.White(0.10f), Px(12f), ImDrawFlags.RoundCornersAll, 1f);

        var artSide = h - Px(16f);
        var artTl = new Vector2(tl.X + Px(8f), tl.Y + Px(8f));
        if (_host.Art(session.SessionId) is { } art)
        {
            dl.AddImageRounded(art, artTl, artTl + new Vector2(artSide, artSide), Vector2.Zero, Vector2.One,
                0xFFFFFFFFu, Px(8f), ImDrawFlags.RoundCornersAll);
        }
        else
        {
            dl.AddRectFilled(artTl, artTl + new Vector2(artSide, artSide), OsDrawShared.White(0.10f), Px(8f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Music, Px(16f),
                artTl + new Vector2(artSide * 0.5f, artSide * 0.5f), OsDrawShared.White(0.45f));
        }

        var textX = artTl.X + artSide + Px(12f);
        dl.PushClipRect(new Vector2(textX, tl.Y), new Vector2(br.X - Px(10f), br.Y), true);
        dl.AddText(new Vector2(textX, tl.Y + Px(14f)), ImGui.GetColorU32(UiColors.Body), session.Title);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.9f,
            new Vector2(textX, tl.Y + Px(16f) + ImGui.GetTextLineHeight()),
            ImGui.GetColorU32(UiColors.Hint),
            string.IsNullOrWhiteSpace(session.Album) ? session.Artist : $"{session.Artist} - {session.Album}");
        dl.PopClipRect();

        ImGui.Dummy(new Vector2(width, h + Px(12f)));
    }

    /// <summary>The surfaces, on the way in rather than buried in settings. Everything defaults on, so this
    /// is the one moment somebody is asked before the app starts appearing in four places at once.</summary>
    private void DrawOnboardingControls(OsAppContext ctx, float padX, float width)
    {
        OnboardingUi.DrawHero("groove_tour_controls", FontAwesomeIcon.SlidersH,
            Loc.T("os.groove_ob_controls_title"), Loc.T("os.groove_ob_controls_sub"));
        OnboardingUi.DrawCenteredParagraph(Loc.T("os.groove_ob_controls_body"), width, UiColors.Body);
        ImGui.Dummy(new Vector2(0f, Px(10f)));

        DrawSurfaceToggle(ctx, PadX, width, "##obMini", Loc.T("os.groove_set_mini"),
            Loc.T("os.groove_set_mini_hint"), _settings.ShowMiniControls, v => _settings.ShowMiniControls = v);
        DrawSurfaceToggle(ctx, PadX, width, "##obDtr", Loc.T("os.groove_set_dtr"),
            Loc.T("os.groove_set_dtr_hint"), _serverBar.AppEnabled, v => _serverBar.AppEnabled = v);
        DrawSurfaceToggle(ctx, PadX, width, "##obShade", Loc.T("os.groove_set_shade"),
            Loc.T("os.groove_set_shade_hint"), _settings.ShowShadeTile, v => _settings.ShowShadeTile = v);
        DrawSurfaceToggle(ctx, PadX, width, "##obWidget", Loc.T("os.groove_set_widget"),
            Loc.T("os.groove_set_widget_hint"), _settings.ShowWidget, v => _settings.ShowWidget = v);

        ImGui.Dummy(new Vector2(0f, Px(4f)));
        OnboardingUi.DrawCenteredParagraph(Loc.T("os.groove_ob_controls_later"), width, UiColors.Hint);

        ImGui.Dummy(new Vector2(0f, Px(12f)));
        if (OnboardingUi.DrawPrimaryButton(Loc.T("os.groove_ob_finish"), enabled: true))
        {
            _settings.OnboardingSeen = true;
            _view = View.Player;
            _entrance.Arm();
        }
        ImGui.Dummy(new Vector2(0f, Px(16f)));
    }
}
