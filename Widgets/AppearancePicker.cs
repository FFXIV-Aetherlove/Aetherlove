using System;
using System.IO;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace AetherLove.Widgets;

/// <summary>Shared theme and phone-size pickers; <c>padX</c> is the unscaled design-pixel margin.</summary>
public static class AppearancePicker
{
    public static void DrawThemeCards(float winW, float padX)
    {
        var dl = ImGui.GetWindowDrawList();
        var CardH = Px(66f);
        var SwatchH = Px(28f);
        var Gap = Px(8f);
        var Rounding = Px(8f);

        const int Cols = 2;
        var themes = Enum.GetValues<AppTheme>();
        var rows = (themes.Length + Cols - 1) / Cols;
        var usableW = winW - Px(padX) * 2f;
        var cardW = (usableW - Gap * (Cols - 1)) / Cols;

        ImGui.SetCursorPos(new Vector2(Px(padX), ImGui.GetCursorPosY()));
        var originLocal = ImGui.GetCursorPos();
        var originScreen = ImGui.GetCursorScreenPos();

        for (int i = 0; i < themes.Length; i++)
        {
            var key = themes[i];
            var def = ThemeService.Themes[key];
            var selected = ThemeService.CurrentTheme == key;

            var offset = new Vector2((i % Cols) * (cardW + Gap), (i / Cols) * (CardH + Gap));
            var tl = originScreen + offset;
            var br = tl + new Vector2(cardW, CardH);

            ImGui.SetCursorPos(originLocal + offset);
            ImGui.InvisibleButton($"##themeCard{i}", new Vector2(cardW, CardH));
            if (ImGui.IsItemClicked())
            {
                ThemeService.SetTheme(key);
            }
            var hovered = ImGui.IsItemHovered();

            var bgAlpha = selected ? 0.28f : (hovered ? 0.14f : 0.07f);
            dl.AddRectFilled(tl, br, def.AccentDarkWithAlpha(bgAlpha), Rounding);

            var swatchBR = new Vector2(br.X, tl.Y + SwatchH);
            var accU32 = ImGui.ColorConvertFloat4ToU32(def.Accent);
            var secU32 = ImGui.ColorConvertFloat4ToU32(def.SecondaryEnd);
            dl.AddRectFilledMultiColor(tl, swatchBR, accU32, secU32, secU32, accU32);
            DrawSwatchShine(dl, tl, swatchBR, i);

            var borderAlpha = selected ? 1.0f : (hovered ? 0.55f : 0.28f);
            var borderThick = selected ? 2.0f : 1.0f;
            dl.AddRect(tl, br, def.AccentWithAlpha(borderAlpha), Rounding, ImDrawFlags.None, borderThick);

            var nameSz = ImGui.CalcTextSize(def.Name);
            var nameX = tl.X + (cardW - nameSz.X) * 0.5f;
            var nameAreaH = CardH - SwatchH;
            var nameY = tl.Y + SwatchH + (nameAreaH - nameSz.Y) * 0.5f;
            var nameA = (uint)MathF.Round((selected ? 1.0f : (hovered ? 0.85f : 0.62f)) * 255f);
            dl.AddText(new Vector2(nameX, nameY), (nameA << 24) | 0x00FFFFFF, def.Name);
        }

        // Advance past the whole grid, not one row; extra theme rows would overlap the next section.
        var gridH = rows * CardH + (rows - 1) * Gap;
        ImGui.SetCursorPos(new Vector2(originLocal.X, originLocal.Y + gridH + Px(6f)));
    }

    private static void DrawSwatchShine(ImDrawListPtr dl, Vector2 min, Vector2 max, int index)
    {
        if (AccessibilityService.ReduceMotion)
        {
            return;
        }
        const double period = 4.0;
        const double sweep = 0.8;
        var phase = (ImGui.GetTime() + period - index * 0.65) % period;
        if (phase > sweep)
        {
            return;
        }

        var t = (float)(phase / sweep);
        var bandW = (max.Y - min.Y) * 2.2f;
        var centerX = min.X - bandW + t * (max.X - min.X + bandW * 2f);
        const uint peak = 0x59FFFFFFu;
        const uint edge = 0x00FFFFFFu;

        dl.PushClipRect(min, max, true);
        dl.AddRectFilledMultiColor(new Vector2(centerX - bandW, min.Y), new Vector2(centerX, max.Y), edge, peak, peak, edge);
        dl.AddRectFilledMultiColor(new Vector2(centerX, min.Y), new Vector2(centerX + bandW, max.Y), peak, edge, edge, peak);
        dl.PopClipRect();
    }

    public static void DrawPhoneSizeButtons(float winW, float padX, ThemeDefinition t)
    {
        DrawSizePills(winW, padX, t, "phsize", Plugin.Configuration.PhoneSize, preset =>
        {
            Plugin.Configuration.PhoneSize = preset;
            Plugin.Configuration.Save();
            UiScale.Apply(preset);
            UiFonts.Rebuild();
        });
        DrawSizeCaption(winW, padX, Loc.T("settings.phone_size_caption"));
    }

    public static void DrawMiniSizeButtons(float winW, float padX, ThemeDefinition t)
    {
        DrawSizePills(winW, padX, t, "minisize", Plugin.Configuration.MiniPhoneSize, preset =>
        {
            Plugin.Configuration.MiniPhoneSize = preset;
            Plugin.Configuration.Save();
            MiniScale.Apply(preset);
        });
        DrawSizeCaption(winW, padX, Loc.T("settings.mini_phone_size_caption"));
    }

    public static void DrawMiniSizePreview(float winW, float padX)
    {
        var w = MiniScale.Px(85f * (ThemeService.Current.WindowWidth / UiScale.Design.X));
        var h = MiniScale.Px(153f);
        var avail = winW - Px(padX) * 2f;
        ImGui.SetCursorPosX(Px(padX) + MathF.Max(0f, (avail - w) * 0.5f));

        var tl = ImGui.GetCursorScreenPos();
        _previewShell.DrawBackground(tl, new Vector2(w, h));

        EnsurePreviewLogo();
        var logoWrap = _previewLogo?.GetWrapOrDefault();
        if (logoWrap != null)
        {
            var logoSz = MiniScale.Px(64f);
            var logoTL = tl + new Vector2((w - logoSz) * 0.5f, MiniScale.Px(18f));
            ImGui.GetWindowDrawList().AddImage(logoWrap.Handle, logoTL, logoTL + new Vector2(logoSz, logoSz));
        }

        ImGui.Dummy(new Vector2(w, h));
    }

    private static void DrawSizePills(float winW, float padX, ThemeDefinition t, string idPrefix,
        PhoneScalePreset current, Action<PhoneScalePreset> onSelect)
    {
        var gap = Px(6f);
        var avail = winW - Px(padX) * 2f;
        var w3 = (avail - gap * 2f) / 3f;
        var w2 = (avail - gap) / 2f;

        ImGui.SetCursorPosX(Px(padX));
        DrawPhoneSizePill(Loc.T("settings.phone_size_small"), PhoneScalePreset.Small, current, w3, t, idPrefix, onSelect);
        ImGui.SameLine(0f, gap);
        DrawPhoneSizePill(Loc.T("settings.phone_size_medium"), PhoneScalePreset.Medium, current, w3, t, idPrefix, onSelect);
        ImGui.SameLine(0f, gap);
        DrawPhoneSizePill(Loc.T("settings.phone_size_large"), PhoneScalePreset.Large, current, w3, t, idPrefix, onSelect);

        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(padX));
        DrawPhoneSizePill(Loc.T("settings.phone_size_xl"), PhoneScalePreset.XL, current, w2, t, idPrefix, onSelect);
        ImGui.SameLine(0f, gap);
        DrawPhoneSizePill(Loc.T("settings.phone_size_xxl"), PhoneScalePreset.XXL, current, w2, t, idPrefix, onSelect);
    }

    private static void DrawSizeCaption(float winW, float padX, string text)
    {
        ImGui.Spacing();
        ImGui.SetCursorPosX(Px(padX));
        ImGui.PushTextWrapPos(winW - Px(padX));
        ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f), text);
        ImGui.PopTextWrapPos();
    }

    private static void DrawPhoneSizePill(string label, PhoneScalePreset preset, PhoneScalePreset current, float w,
        ThemeDefinition t, string idPrefix, Action<PhoneScalePreset> onSelect)
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
        if (ImGui.Button($"{label}##{idPrefix}{preset}", new Vector2(w, Px(30f))) && !selected)
        {
            onSelect(preset);
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }

    private static readonly PhoneShellWidget _previewShell = new();
    private static ISharedImmediateTexture? _previewLogo;
    private static bool _previewLogoLoaded;

    private static void EnsurePreviewLogo()
    {
        if (_previewLogoLoaded)
        {
            return;
        }
        _previewLogoLoaded = true;
        try
        {
            var dir = Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName) ?? string.Empty;
            var path = Path.Combine(dir, "Media", "logo_mini.png");
            if (File.Exists(path))
            {
                _previewLogo = Plugin.TextureProvider.GetFromFile(path);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[AppearancePicker] Failed to load mini logo for preview.");
        }
    }
}
