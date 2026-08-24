using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Services.Translation;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Os;

/// <summary>The one-time translation opt-in for phones that predate the feature: an update lands, the
/// home screen greets the user with what translation is, the Google disclosure, the animated right-click
/// demo, an explicit enable switch (off until they flip it) and their target language. A fresh phone never
/// sees this; the OS onboarding's own translation step marks the offer seen. Continue records the answer
/// either way, so it shows exactly once.</summary>
public sealed class TranslationOffer
{
    private float _in;
    private bool _enable;
    private bool _languageSeeded;
    private string _language = "en";
    private string _filter = string.Empty;

    /// <summary>Whether the offer owns the screen. Same holds as the new-app offer: the caller keeps it
    /// off the boot intro, transitions, the tour, and the new-app offer itself.</summary>
    public bool Active
    {
        get
        {
            var os = UiHost.Configuration.OsSettings;
            return !os.TranslationOfferSeen && !os.TranslationsEnabled;
        }
    }

    public void Draw(Vector2 contentTL, Vector2 contentBR)
    {
        if (!Active)
        {
            return;
        }
        if (!_languageSeeded)
        {
            _languageSeeded = true;
            _language = TranslationLanguages.DefaultForPluginLanguage(UiHost.Configuration.PluginLanguage);
        }

        var size = contentBR - contentTL;
        ImGui.SetCursorScreenPos(contentTL);
        using var layer = ImRaii.Child("##translationOffer", size, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer)
        {
            return;
        }

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        _in = AccessibilityService.ReduceMotion
            ? 1f
            : MathF.Min(1f, _in + (ImGui.GetIO().DeltaTime * 3.2f));
        var ease = 1f - MathF.Pow(1f - _in, 3f);

        dl.AddRectFilled(contentTL, contentBR, OsDraw.Black(ease));
        OsDraw.RoundedGradient(dl, contentTL, contentBR, 0f,
            t.SecondaryStart with { W = 0.40f }, t.SecondaryEnd with { W = 0.14f }, ease);

        var pad = Px(18f);
        var innerW = size.X - (pad * 2f);
        var top = contentTL.Y + Px(22f) + ((1f - ease) * Px(14f));

        var glyphC = new Vector2(contentTL.X + (size.X * 0.5f), top + Px(14f));
        dl.AddCircleFilled(glyphC, Px(20f), ImGui.ColorConvertFloat4ToU32(t.Accent with { W = 0.18f * ease }), 32);
        IconDraw.AddCentered(dl, FontAwesomeIcon.Language, Px(19f), glyphC,
            ImGui.ColorConvertFloat4ToU32(t.Accent with { W = 0.95f * ease }));

        var title = Loc.T("os.translate_offer_title");
        using (UiFonts.H2?.Push())
        {
            var titleSz = ImGui.CalcTextSize(title);
            dl.AddText(new Vector2(contentTL.X + ((size.X - titleSz.X) * 0.5f), top + Px(42f)),
                OsDraw.White(0.98f * ease), title);
        }

        var body = Loc.T("os.translate_offer_body") + " " + Loc.T("os.translate_consent_body");
        var bodyTL = new Vector2(contentTL.X + pad, top + Px(76f));
        var bodyH = ImGui.CalcTextSize(body, false, innerW).Y * 0.88f;
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.88f, bodyTL,
            OsDraw.White(0.62f * ease), body, innerW);

        ImGui.SetCursorScreenPos(new Vector2(contentTL.X + pad, bodyTL.Y + bodyH + Px(14f)));
        using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ease))
        {
            TranslateDemo.Draw(innerW);
        }
        ImGui.Dummy(new Vector2(0f, Px(12f)));

        // The explicit opt-in row: whole row toggles, switch drawn by hand like the new-app offer's.
        var rowTL = ImGui.GetCursorScreenPos();
        var rowH = Px(40f);
        ImGui.SetCursorScreenPos(rowTL);
        if (ImGui.InvisibleButton("##trOfferEnable", new Vector2(innerW, rowH)))
        {
            _enable = !_enable;
        }
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }
        dl.AddRectFilled(rowTL, rowTL + new Vector2(innerW, rowH),
            OsDraw.White((hovered ? 0.10f : 0.06f) * ease), Px(12f));
        dl.AddText(new Vector2(rowTL.X + Px(12f), rowTL.Y + (rowH - ImGui.GetTextLineHeight()) * 0.5f),
            OsDraw.White((_enable ? 0.97f : 0.7f) * ease), Loc.T("settings.translation_enable"));
        DrawSwitch(dl, new Vector2(rowTL.X + innerW - Px(34f), rowTL.Y + rowH * 0.5f), _enable, ease);
        ImGui.Dummy(new Vector2(0f, Px(8f)));

        // Language row, only meaningful once they opt in.
        ImGui.SetCursorPosX(pad);
        if (!_enable)
        {
            ImGui.BeginDisabled();
        }
        dl.AddText(ImGui.GetCursorScreenPos() + new Vector2(0f, Px(4f)), OsDraw.White(0.7f * ease),
            Loc.T("settings.translation_language"));
        var labelW = ImGui.CalcTextSize(Loc.T("settings.translation_language")).X + Px(12f);
        ImGui.SetCursorScreenPos(ImGui.GetCursorScreenPos() + new Vector2(labelW, 0f));
        ImGui.SetNextItemWidth(innerW - labelW);
        if (ImGui.BeginCombo("##trOfferLang", TranslationLanguages.DisplayName(_language),
                ImGuiComboFlags.HeightLarge))
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.IsWindowAppearing())
            {
                _filter = string.Empty;
                ImGui.SetKeyboardFocusHere();
            }
            ImGui.InputTextWithHint("##trOfferFilter", Loc.T("settings.translation_search"), ref _filter, 40);
            ImGui.Separator();
            var filter = _filter.Trim();
            using (var list = ImRaii.Child("##trOfferList", new Vector2(0f, Px(200f)), false))
            {
                if (list)
                {
                    foreach (var language in TranslationLanguages.Renderable)
                    {
                        if (filter.Length > 0 && !language.Matches(filter))
                        {
                            continue;
                        }
                        if (ImGui.Selectable($"{language.NativeName}##trOffer{language.Code}",
                                language.Code.Equals(_language, StringComparison.OrdinalIgnoreCase)))
                        {
                            _language = language.Code;
                            ImGui.CloseCurrentPopup();
                        }
                        SharedUiHelpers.HandOnHover();
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
        SharedUiHelpers.HandOnHover();
        if (!_enable)
        {
            ImGui.EndDisabled();
        }

        var buttonH = Px(42f);
        var buttonTL = new Vector2(contentTL.X + pad, contentBR.Y - buttonH - Px(16f));
        ImGui.SetCursorScreenPos(buttonTL);
        var pressed = ImGui.InvisibleButton("##trOfferGo", new Vector2(innerW, buttonH));
        var goHovered = ImGui.IsItemHovered();
        if (goHovered)
        {
            SharedUiHelpers.HandOnHover();
        }
        dl.AddRectFilled(buttonTL, buttonTL + new Vector2(innerW, buttonH),
            ImGui.ColorConvertFloat4ToU32(t.Accent with { W = (goHovered ? 0.95f : 0.82f) * ease }),
            buttonH * 0.5f);
        var goLabel = Loc.T(_enable ? "os.translate_offer_enable_go" : "os.translate_offer_skip_go");
        var goSz = ImGui.CalcTextSize(goLabel);
        dl.AddText(buttonTL + (new Vector2(innerW, buttonH) - goSz) * 0.5f, OsDraw.White(0.98f * ease), goLabel);
        if (pressed)
        {
            Commit();
        }
    }

    private void Commit()
    {
        var os = UiHost.Configuration.OsSettings;
        os.TranslationOfferSeen = true;
        if (_enable)
        {
            os.TranslationsEnabled = true;
            os.TranslationLanguage = _language;
        }
        UiHost.Configuration.Save();
        _in = 0f;
    }

    private static void DrawSwitch(ImDrawListPtr dl, Vector2 centre, bool on, float ease)
    {
        var t = ThemeService.Current;
        var w = Px(42f);
        var h = Px(23f);
        var tl = centre - new Vector2(w * 0.5f, h * 0.5f);
        var br = tl + new Vector2(w, h);
        dl.AddRectFilled(tl, br,
            on ? ImGui.ColorConvertFloat4ToU32(t.Accent with { W = 0.85f * ease }) : OsDraw.White(0.16f * ease),
            h * 0.5f);
        var knob = new Vector2(on ? br.X - (h * 0.5f) : tl.X + (h * 0.5f), centre.Y);
        dl.AddCircleFilled(knob, (h * 0.5f) - Px(2.5f), OsDraw.White(0.96f * ease), 24);
    }
}
