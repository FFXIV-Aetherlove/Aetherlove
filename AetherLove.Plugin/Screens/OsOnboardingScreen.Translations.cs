using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Services.Translation;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using static AetherLove.UI.OnboardingUi;

namespace AetherLove.Screens;

public sealed partial class OsOnboardingScreen
{
    private bool _trEnable;
    private string _trLanguage = "en";
    private string _trLangFilter = string.Empty;

    /// <summary>Defaults the target language to the OS language just chosen on the design step, so the
    /// dropdown opens on the answer most people want.</summary>
    private void SeedTranslationStep()
    {
        _trEnable = false;
        _trLanguage = TranslationLanguages.DefaultForPluginLanguage(Plugin.Configuration.PluginLanguage);
        _trLangFilter = string.Empty;
    }

    /// <summary>Records the answer either way: enabling is the explicit opt-in, and a pass-through still
    /// marks the offer seen so the update popup never re-asks somebody who just said no here.</summary>
    private void CommitTranslationStep()
    {
        var os = Plugin.Configuration.OsSettings;
        os.TranslationOfferSeen = true;
        if (_trEnable)
        {
            os.TranslationsEnabled = true;
            os.TranslationLanguage = _trLanguage;
        }
        Plugin.Configuration.Save();
    }

    /// <summary>The translation opt-in step: what it is, the Google disclosure, the animated right-click
    /// demo, the explicit enable switch and the target language.</summary>
    private void DrawTranslations()
    {
        DrawHero("onb_translate", FontAwesomeIcon.Language, Loc.T("os_onboarding.translate_title"),
            Loc.T("os_onboarding.translate_body"), 26f);

        var winW = ImGui.GetWindowSize().X;
        var innerW = winW - Px(32f);

        ImGui.SetCursorPosX(Px(16f));
        ImGui.PushTextWrapPos(winW - Px(16f));
        ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f), Loc.T("os.translate_consent_body"));
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Px(10f)));

        ImGui.SetCursorPosX(Px(16f));
        TranslateDemo.Draw(innerW);
        ImGui.Dummy(new Vector2(0f, Px(12f)));

        ImGui.SetCursorPosX(Px(16f));
        ImGui.Checkbox(Loc.T("settings.translation_enable"), ref _trEnable);
        HandOnHover();
        ImGui.Dummy(new Vector2(0f, Px(6f)));

        ImGui.SetCursorPosX(Px(16f));
        if (!_trEnable)
        {
            ImGui.BeginDisabled();
        }
        ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.85f, 1f), Loc.T("settings.translation_language"));
        ImGui.SetCursorPosX(Px(16f));
        ImGui.SetNextItemWidth(innerW);
        if (ImGui.BeginCombo("##onbTrLang", TranslationLanguages.DisplayName(_trLanguage),
                ImGuiComboFlags.HeightLarge))
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.IsWindowAppearing())
            {
                _trLangFilter = string.Empty;
                ImGui.SetKeyboardFocusHere();
            }
            ImGui.InputTextWithHint("##onbTrLangFilter", Loc.T("settings.translation_search"),
                ref _trLangFilter, 40);
            ImGui.Separator();
            var filter = _trLangFilter.Trim();
            using (var list = ImRaii.Child("##onbTrLangList", new Vector2(0f, Px(200f)), false))
            {
                if (list)
                {
                    foreach (var language in TranslationLanguages.Renderable)
                    {
                        if (filter.Length > 0 && !language.Matches(filter))
                        {
                            continue;
                        }
                        if (ImGui.Selectable($"{language.NativeName}##onbTr{language.Code}",
                                language.Code.Equals(_trLanguage, StringComparison.OrdinalIgnoreCase)))
                        {
                            _trLanguage = language.Code;
                            ImGui.CloseCurrentPopup();
                        }
                        HandOnHover();
                        if (!string.Equals(language.NativeName, language.EnglishName, StringComparison.Ordinal))
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f), language.EnglishName);
                        }
                    }
                }
            }
            ImGui.EndCombo();
        }
        HandOnHover();
        if (!_trEnable)
        {
            ImGui.EndDisabled();
        }

        ImGui.Dummy(new Vector2(0f, Px(8f)));
        ImGui.SetCursorPosX(Px(16f));
        ImGui.PushTextWrapPos(winW - Px(16f));
        ImGui.TextColored(new Vector4(0.50f, 0.50f, 0.50f, 1f), Loc.T("os_onboarding.translate_later"));
        ImGui.PopTextWrapPos();
    }
}
