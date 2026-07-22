using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.UI;

/// <summary>The shared app-header chrome used by surface apps (Places is the reference): a theme-accent H3
/// app name on the left and a filled, accent-tinted hamburger menu button on the right that opens a styled
/// dropdown. Extracted so Places, Hangouts and the Messenger render an identical header.</summary>
internal static class AppHeader
{
    private const float MenuSize = 30f;
    private const double MenuAnimSeconds = 0.13;

    /// <summary>Per-popup open timestamp, so the dropdown can fade + slide in from under the button.</summary>
    private static readonly Dictionary<string, double> MenuOpenAt = new();

    /// <summary>The accent-coloured H3 app name; call at the start of a header row.</summary>
    public static void DrawTitle(string text, float padX)
    {
        ImGui.SetCursorPosX(Px(padX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(ThemeService.Current.AccentLight, text);
        }
    }

    /// <summary>Draws the filled hamburger button top-right and opens <paramref name="popupId"/> on click.
    /// Returns the button's top-left so the popup can anchor under it. Call right after the title row.</summary>
    public static Vector2 DrawMenuButton(float winW, float padX, string popupId, bool badge = false)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var size = Px(MenuSize);
        var winPos = ImGui.GetWindowPos();
        var tl = new Vector2(winPos.X + winW - Px(padX) - size,
            ImGui.GetCursorScreenPos().Y - ImGui.GetTextLineHeight() - Px(6f));

        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton($"##menuBtn{popupId}", new Vector2(size, size)))
        {
            ImGui.OpenPopup(popupId);
        }
        SharedUiHelpers.HandOnHover();
        var active = ImGui.IsItemHovered() || ImGui.IsPopupOpen(popupId);
        dl.AddRectFilled(tl, tl + new Vector2(size, size),
            ImGui.GetColorU32(active ? t.Accent with { W = 0.30f } : new Vector4(1f, 1f, 1f, 0.06f)), Px(8f));

        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var glyph = FontAwesomeIcon.Bars.ToIconString();
        var glyphSz = ImGui.CalcTextSize(glyph);
        dl.AddText(tl + (new Vector2(size, size) - glyphSz) * 0.5f, ImGui.GetColorU32(t.AccentLight), glyph);
        ImGui.PopFont();

        if (badge)
        {
            dl.AddCircleFilled(tl + new Vector2(size - Px(4f), Px(4f)), Px(3.5f), t.AccentU32);
        }
        return tl;
    }

    /// <summary>Opens the styled menu dropdown anchored under the button; must be paired with
    /// <see cref="EndMenuPopup"/>. Returns whether the popup is open (add rows only when true).</summary>
    public static bool BeginMenuPopup(Vector2 menuTL, string popupId)
    {
        var size = Px(MenuSize);

        // Opening animation: ease-out fade + a short slide down from just under the button.
        var anim = 1f;
        if (ImGui.IsPopupOpen(popupId))
        {
            if (!MenuOpenAt.TryGetValue(popupId, out var start))
            {
                start = ImGui.GetTime();
                MenuOpenAt[popupId] = start;
            }
            var raw = AccessibilityService.ReduceMotion
                ? 1f
                : (float)Math.Clamp((ImGui.GetTime() - start) / MenuAnimSeconds, 0.0, 1.0);
            anim = 1f - MathF.Pow(1f - raw, 3f);
        }
        else
        {
            MenuOpenAt.Remove(popupId);
        }

        var slide = (1f - anim) * Px(8f);
        ImGui.SetNextWindowPos(new Vector2(menuTL.X + size, menuTL.Y + size + Px(4f) - slide),
            ImGuiCond.Always, new Vector2(1f, 0f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.13f, 0.12f, 0.15f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, ThemeService.Current.Accent with { W = 0.5f });
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Px(10f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(4f), Px(4f)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, Px(2f)));
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, anim);
        return ImGui.BeginPopup(popupId);
    }

    public static void EndMenuPopup(bool open)
    {
        if (open)
        {
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(2);
    }

    /// <summary>One icon+label row for the shared menu dropdown (the single source of the Places / Messenger /
    /// Hangouts row look). Icon at 0.9x in the accent, label in body text, subtle rounded hover. Pass
    /// <paramref name="tint"/> to colour a special item (supporter gold, danger red) for both icon and label.</summary>
    public static bool MenuRow(FontAwesomeIcon icon, string label, float w, float rowH, uint? tint = null)
    {
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##menuRow{label}", new Vector2(w, rowH));
        if (ImGui.IsItemHovered())
        {
            dl.AddRectFilled(tl, tl + new Vector2(w, rowH), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), Px(6f));
            SharedUiHelpers.HandOnHover();
        }
        var iconPx = ImGui.GetFontSize() * 0.9f;
        var iconSz = IconDraw.Measure(icon, iconPx);
        var iconCol = tint ?? ImGui.GetColorU32(ThemeService.Current.AccentLight);
        var textCol = tint ?? ImGui.GetColorU32(UiColors.Body);
        IconDraw.Add(dl, icon, iconPx, new Vector2(tl.X + Px(10f), tl.Y + (rowH - iconSz.Y) * 0.5f), iconCol);
        dl.AddText(new Vector2(tl.X + Px(10f) + iconSz.X + Px(10f), tl.Y + (rowH - ImGui.GetTextLineHeight()) * 0.5f),
            textCol, label);
        return clicked;
    }

    /// <summary>The dropdown width for a set of labels, matching the Places menu: a floor plus room for the icon
    /// gutter, sized to the widest label. Compute once, pass to every <see cref="MenuRow"/>.</summary>
    public static float MenuWidth(params string[] labels)
    {
        var max = 0f;
        foreach (var label in labels)
        {
            max = MathF.Max(max, ImGui.CalcTextSize(label).X);
        }
        return MathF.Max(Px(150f), max + Px(56f));
    }

    /// <summary>The dropdown row height matching the Places menu.</summary>
    public static float MenuRowHeight() => ImGui.GetTextLineHeight() + Px(12f);
}
