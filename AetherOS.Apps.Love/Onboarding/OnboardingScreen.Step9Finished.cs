using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherLove.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using static AetherLove.UI.OnboardingUi;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{
    private readonly ConfettiBurst _confetti = new();

    private void ResetConfetti() => _confetti.Reset();

    /// <summary>Renders the celebratory completion screen with glow, hero, and confetti.</summary>
    private void DrawStepFinished()
    {
        var t = ThemeService.Current;
        var wPos = ImGui.GetWindowPos();
        var wSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();
        var winW = wSize.X;

        // Celebratory radial glow behind the hero.
        var glowCenter = wPos + new Vector2(wSize.X * 0.5f, wSize.Y * 0.34f);
        var glowSpan = MathF.Min(wSize.X, wSize.Y);
        for (var i = 0; i < 5; i++)
        {
            var r = glowSpan * (0.14f + i * 0.11f);
            var a = 0.08f * (1f - i * 0.18f);
            dl.AddCircleFilled(glowCenter, r, ImGui.ColorConvertFloat4ToU32(t.Accent with { W = a }), 64);
        }

        ImGui.Dummy(new Vector2(0f, wSize.Y * 0.13f));
        DrawHero("love_done", FontAwesomeIcon.Heart, Loc.T("onboarding.hero_done_title"),
            Loc.T("onboarding.hero_done_sub"), 42f);

        ImGui.Dummy(new Vector2(0f, Px(12f)));
        DrawCenteredParagraph(Loc.T("onboarding.finished_good_luck"), winW - Px(48f), UiColors.Success);

        _confetti.Draw(wPos, wPos + wSize);
    }
}
