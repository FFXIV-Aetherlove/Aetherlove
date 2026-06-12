using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Screens;

public partial class OnboardingScreen
{
    private int _pluginLangIdx;


    private void DrawStepWelcome()
    {
        var t      = ThemeService.Current;
        var lang   = LanguageProvider.Current;
        var availW = ImGui.GetContentRegionAvail().X;

        ImGui.Spacing(); ImGui.Spacing();

        var title  = lang.WelcomeTitle;
        var dl     = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var col    = ImGui.ColorConvertFloat4ToU32(t.Accent);

        float fsz;
        using (UiFonts.H3?.Push())
        {
            var font   = ImGui.GetFont();
            fsz        = ImGui.GetFontSize();
            var titleW = ImGui.CalcTextSize(title).X;
            var posX   = origin.X + (availW - titleW) * 0.5f;
            dl.AddText(font, fsz, new Vector2(posX,            origin.Y), col, title);
            dl.AddText(font, fsz, new Vector2(posX + Px(0.8f), origin.Y), col, title);
        }
        ImGui.Dummy(new Vector2(availW, fsz + Px(2f)));

        ImGui.Spacing();
        const string Div = "──────────────────────";
        ImGui.SetCursorPosX((availW - ImGui.CalcTextSize(Div).X) * 0.5f);
        ImGui.TextColored(t.AccentDark, Div);
        ImGui.Spacing(); ImGui.Spacing();

        ImGui.TextWrapped(lang.WelcomeBody1);
        ImGui.Spacing();
        ImGui.TextWrapped(lang.WelcomeBody2);
        ImGui.Spacing(); ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f), lang.WelcomePrivacyHeading);
        ImGui.TextWrapped(lang.WelcomePrivacyBody);
        ImGui.Spacing(); ImGui.Spacing();

        var bullets = new[]
        {
            (lang.WelcomeFeatureDiscoverTitle, lang.WelcomeFeatureDiscoverBody),
            (lang.WelcomeFeatureConnectTitle,  lang.WelcomeFeatureConnectBody),
            (lang.WelcomeFeatureChatTitle,     lang.WelcomeFeatureChatBody),
        };

        foreach (var (label, body) in bullets)
        {
            ImGui.TextColored(t.AccentLight, label);
            ImGui.SameLine(0f, Px(6f));
            ImGui.TextWrapped(body);
            ImGui.Spacing();
        }

        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextColored(t.AccentLight, lang.WelcomePluginLanguageLabel);
        ImGui.SameLine(); HelpTooltip(lang.WelcomePluginLanguageTooltip);
        ImGui.Spacing();
        DrawPluginLanguagePills();

        ImGui.Spacing();
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(UiColors.Hint, lang.WelcomeFooter);
        ImGui.PopTextWrapPos();
    }

    private void DrawPluginLanguagePills()
    {
        EnsureLangFlags();
        DrawLanguagePillsCore(
            _langFlags,
            flagW: Px(36f),
            flagH: Px(27f),
            useCode: true,
            idPrefix: "plug",
            isSelected: i => i == _pluginLangIdx,
            onToggle: i =>
            {
                _pluginLangIdx = i;
                LanguageProvider.SetLanguage(LanguageEntries[i].Name);
            });
    }
}
