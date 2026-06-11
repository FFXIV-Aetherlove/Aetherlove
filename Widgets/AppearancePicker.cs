using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Widgets;

/// <summary>Shared theme and phone-size pickers used by Settings and onboarding.
/// <paramref name="padX"/> is the unscaled design-pixel left/right margin (run through UiScale.Px).</summary>
public static class AppearancePicker
{
    public static void DrawThemeCards(float winW, float padX)
    {
        var dl = ImGui.GetWindowDrawList();
        var CardH = Px(66f);
        var SwatchH = Px(28f);
        var Gap = Px(8f);
        var Rounding = Px(8f);

        var themes = Enum.GetValues<AppTheme>();
        var usableW = winW - Px(padX) * 2f;
        var cardW = (usableW - Gap * (themes.Length - 1)) / themes.Length;

        ImGui.SetCursorPos(new Vector2(Px(padX), ImGui.GetCursorPosY()));
        var originLocal = ImGui.GetCursorPos();
        var originScreen = ImGui.GetCursorScreenPos();

        for (int i = 0; i < themes.Length; i++)
        {
            var key = themes[i];
            var def = ThemeService.Themes[key];
            var selected = ThemeService.CurrentTheme == key;

            var tl = new Vector2(originScreen.X + i * (cardW + Gap), originScreen.Y);
            var br = tl + new Vector2(cardW, CardH);

            ImGui.SetCursorPos(new Vector2(originLocal.X + i * (cardW + Gap), originLocal.Y));
            ImGui.InvisibleButton($"##themeCard{i}", new Vector2(cardW, CardH));
            if (ImGui.IsItemClicked())
            {
                ThemeService.SetTheme(key);
            }
            var hovered = ImGui.IsItemHovered();

            var bgAlpha = selected ? 0.28f : (hovered ? 0.14f : 0.07f);
            dl.AddRectFilled(tl, br, def.AccentDarkWithAlpha(bgAlpha), Rounding);

            var swatchBR = new Vector2(br.X, tl.Y + SwatchH);
            dl.AddRectFilledMultiColor(
                tl, swatchBR,
                def.AccentDarkU32, def.AccentLightU32,
                def.AccentLightU32, def.AccentDarkU32);

            var borderAlpha = selected ? 1.0f : (hovered ? 0.55f : 0.28f);
            var borderThick = selected ? 2.0f : 1.0f;
            dl.AddRect(tl, br, def.AccentWithAlpha(borderAlpha), Rounding, ImDrawFlags.None, borderThick);

            var nameSz = ImGui.CalcTextSize(def.Name);
            var nameX = tl.X + (cardW - nameSz.X) * 0.5f;
            var nameAreaH = CardH - SwatchH;
            var nameY = tl.Y + SwatchH + (nameAreaH - nameSz.Y) * 0.5f;
            var nameA = (uint)MathF.Round((selected ? 1.0f : (hovered ? 0.85f : 0.62f)) * 255f);
            dl.AddText(new Vector2(nameX, nameY), (nameA << 24) | 0x00FFFFFF, def.Name);

            if (selected)
            {
                var dotC = new Vector2(br.X - Px(11f), tl.Y + SwatchH + nameAreaH * 0.5f);
                dl.AddCircleFilled(dotC, Px(4f), def.AccentLightU32);
            }
        }

        ImGui.SetCursorPos(new Vector2(originLocal.X, originLocal.Y + CardH + Px(6f)));
    }

    public static void DrawPhoneSizeButtons(float winW, float padX, ThemeDefinition t)
    {
        var current = Plugin.Configuration.PhoneSize;
        var gap = Px(6f);
        var w = (winW - Px(padX) * 2f - gap * 2f) / 3f;

        ImGui.SetCursorPosX(Px(padX));
        DrawPhoneSizePill(Loc.T("settings.phone_size_small"), PhoneScalePreset.Small, current, w, t);
        ImGui.SameLine(0f, gap);
        DrawPhoneSizePill(Loc.T("settings.phone_size_medium"), PhoneScalePreset.Medium, current, w, t);
        ImGui.SameLine(0f, gap);
        DrawPhoneSizePill(Loc.T("settings.phone_size_large"), PhoneScalePreset.Large, current, w, t);

        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(padX));
        ImGui.PushTextWrapPos(winW - Px(padX));
        ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f),
            Loc.T("settings.phone_size_caption"));
        ImGui.PopTextWrapPos();
    }

    private static void DrawPhoneSizePill(string label, PhoneScalePreset preset, PhoneScalePreset current, float w, ThemeDefinition t)
    {
        var selected = preset == current;
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.18f, 0.18f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.26f, 0.26f, 0.26f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.14f, 0.14f, 0.14f, 1f));
        }
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button($"{label}##phsize{preset}", new Vector2(w, Px(30f))) && !selected)
        {
            Plugin.Configuration.PhoneSize = preset;
            Plugin.Configuration.Save();
            UiScale.Apply(preset);
            UiFonts.Rebuild();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }
}
