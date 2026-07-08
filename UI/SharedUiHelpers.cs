using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Profile;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.UI;

/// <summary>Small stateless helpers shared by the profile-editing screens (onboarding and "My profile"):
/// an inline help marker plus the conversions between a stored bitmask and the on/off checkbox state of a
/// multi-select list. They live here, not on a screen, because both screens build the same profile data.</summary>
internal static class SharedUiHelpers
{
    /// <summary>Red destructive-action button colours; pop 3 after the button.</summary>
    internal static void PushDangerButton()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.32f, 0.10f, 0.10f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.55f, 0.15f, 0.15f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.22f, 0.06f, 0.06f, 1f));
    }

    /// <summary>Draws a faint "(?)" that shows <paramref name="text"/> as a tooltip while hovered — an
    /// inline explanation for the field it sits next to.</summary>
    internal static void HelpTooltip(string text)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }

    /// <summary>Returns <paramref name="text"/> shortened with a trailing ellipsis so it fits within
    /// <paramref name="maxWidth"/> at the current font; returns it unchanged when it already fits.</summary>
    internal static string TruncateToWidth(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || ImGui.CalcTextSize(text).X <= maxWidth)
        {
            return text;
        }
        const string ellipsis = "…";
        var ellipsisW = ImGui.CalcTextSize(ellipsis).X;
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid]).X + ellipsisW <= maxWidth)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return lo <= 0 ? ellipsis : text[..lo].TrimEnd() + ellipsis;
    }

    /// <summary>Copies player-authored text to the clipboard and, on the first copy ever, shows the one-time
    /// link-safety warning gated by the shared AcknowledgedProfileCopyTextWarning flag. Used by the profile bio
    /// and chat-message copy actions.</summary>
    internal static void CopyTextWithLinkWarning(string text)
    {
        ImGui.SetClipboardText(text ?? string.Empty);
        if (!Plugin.Configuration.AcknowledgedProfileCopyTextWarning)
        {
            Widgets.ModalHost.Instance?.Open(320f, DrawCopyTextWarningBody);
        }
    }

    private static void DrawCopyTextWarningBody(float availW)
    {
        Widgets.ModalUi.Header(availW, FontAwesomeIcon.ExclamationTriangle,
            Loc.T("profile.copy_warning_title"), UiColors.Amber);

        ImGui.TextColored(UiColors.Body, Loc.T("profile.copy_warning_body"));
        ImGui.Spacing();
        ImGui.Spacing();

        if (Widgets.ModalUi.Button($"{Loc.T("profile.copy_warning_agree")}##copyWarnAgree", availW))
        {
            Plugin.Configuration.AcknowledgedProfileCopyTextWarning = true;
            Plugin.Configuration.Save();
            Widgets.ModalHost.Instance?.Close();
        }
    }

    /// <summary>Draws one "favourite song" input row: a link box plus the server-resolved name preview (or a
    /// "fetching" / "saved link" status). The displayed name is curated server-side — never user-typed.</summary>
    internal static void DrawMusicLinkField(MusicLinkField field, string label, string tip, float width)
    {
        field.Tick();
        ImGui.Text(label);
        ImGui.SameLine();
        HelpTooltip(tip);
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputText($"##music_{field.Provider}", ref field.Input, 256))
        {
            field.OnInputChanged();
        }

        if (field.Fetching)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), Loc.T("music.fetching"));
        }
        else if (field.Invalid)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(UiColors.Danger, Loc.T("music.invalid"));
            ImGui.PopTextWrapPos();
        }
        else if (field.ResolvedName.Length > 0)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(UiColors.Success, $"  {field.ResolvedName}");
            ImGui.PopTextWrapPos();
        }
        else if (field.ResolvedRef.Length > 0)
        {
            ImGui.TextColored(UiColors.Hint, Loc.T("music.saved"));
        }
    }

    /// <summary>Pushes the theme's button colours; pair with <see cref="PopThemeButton"/>.</summary>
    internal static void PushThemeButton(ThemeDefinition t)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
    }

    internal static void PopThemeButton() => ImGui.PopStyleColor(3);

    /// <summary>The themed "← Back" pill used to step out of a sub-page back to its parent (the "My" hub, the
    /// News list). Position the cursor before calling; returns true on click.</summary>
    internal static bool DrawBackButton(string label)
    {
        PushThemeButton(ThemeService.Current);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        var clicked = ImGui.Button(label, new Vector2(Px(96f), Px(28f)));
        ImGui.PopStyleVar();
        PopThemeButton();
        return clicked;
    }

    /// <summary>One moderation notice as a card: an accent stripe + icon + timestamp header above the wrapped
    /// body. Unseen notices get an accent-tinted fill, a brighter border and an unread dot; seen ones use a
    /// neutral card. <paramref name="padX"/> is the design-pixel inset. Shared by the "My" hub lists and the
    /// live warning / message acknowledge screens.</summary>
    internal static void DrawNoticeCard(float listW, Dalamud.Interface.FontAwesomeIcon icon, Vector4 accent,
        DateTimeOffset whenUtc, string body, bool seen, float padX)
    {
        var pad = Px(padX);
        var cardW = listW - pad * 2f;
        var rounding = Px(10f);
        var cx = Px(18f);
        var contentW = cardW - cx - Px(14f);

        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var lineH = ImGui.GetTextLineHeight();
        var bodyH = ImGui.CalcTextSize(body, false, contentW).Y;
        var cardH = Px(11f) + lineH + Px(6f) + bodyH + Px(11f);

        var origin = ImGui.GetCursorScreenPos();
        var tl = new Vector2(origin.X + pad, origin.Y);
        var br = new Vector2(tl.X + cardW, tl.Y + cardH);
        var dl = ImGui.GetWindowDrawList();

        var bg = seen ? new Vector4(1f, 1f, 1f, 0.04f) : accent with { W = 0.10f };
        var border = seen ? new Vector4(1f, 1f, 1f, 0.07f) : accent with { W = 0.42f };
        var stripe = accent with { W = seen ? 0.40f : 1f };
        var dateCol = seen ? UiColors.Muted : accent;
        var bodyCol = seen ? UiColors.Muted : UiColors.Body;

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(bg), rounding);
        dl.AddRect(tl, br, ImGui.GetColorU32(border), rounding, ImDrawFlags.None, Px(1f));
        dl.AddRectFilled(new Vector2(tl.X + Px(7f), tl.Y + Px(9f)), new Vector2(tl.X + Px(10f), br.Y - Px(9f)),
            ImGui.GetColorU32(stripe), Px(2f), ImDrawFlags.RoundCornersAll);

        var hy = tl.Y + Px(11f);
        ImGui.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon);
        var iconStr = icon.ToIconString();
        var iconW = ImGui.CalcTextSize(iconStr).X;
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(tl.X + cx, hy), ImGui.GetColorU32(accent), iconStr);
        ImGui.PopFont();

        var dateStr = whenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        dl.AddText(font, fontSize, new Vector2(tl.X + cx + iconW + Px(8f), hy), ImGui.GetColorU32(dateCol), dateStr);

        if (!seen)
        {
            dl.AddCircleFilled(new Vector2(br.X - Px(12f), hy + lineH * 0.5f), Px(3.5f), ImGui.GetColorU32(accent));
        }

        dl.AddText(font, fontSize, new Vector2(tl.X + cx, hy + lineH + Px(6f)), ImGui.GetColorU32(bodyCol), body, contentW);

        ImGui.Dummy(new Vector2(cardW, cardH));
    }

    /// <summary>One entry of a grouped "menu card" (icon + label + optional badge), invoked on click. Shared so
    /// the "My" hub and the Settings hub build identical menus.</summary>
    internal readonly record struct MenuRow(Dalamud.Interface.FontAwesomeIcon Icon, Vector4 IconColor, string Label,
        int Badge, bool External, Action OnClick);

    /// <summary>Draws a grouped menu card: a faint rounded panel with a thin border holding one
    /// <see cref="DrawMenuRow"/> per entry. <paramref name="padX"/> is the design-pixel inset.</summary>
    internal static void DrawMenuCard(string idPrefix, float winW, float padX, IReadOnlyList<MenuRow> rows)
    {
        var rowH = Px(44f);
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var cardMin = new Vector2(origin.X + Px(padX), origin.Y);
        var cardMax = new Vector2(origin.X + winW - Px(padX), origin.Y + rowH * rows.Count);
        dl.AddRectFilled(cardMin, cardMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.045f)), Px(10f));

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 0f));
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (DrawMenuRow(winW, rowH, $"##{idPrefix}row{i}", r.Icon, r.IconColor, r.Label,
                    i == rows.Count - 1, r.External, r.Badge, padX))
            {
                r.OnClick();
            }
        }
        ImGui.PopStyleVar();
        dl.AddRect(cardMin, cardMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.07f)), Px(10f), ImDrawFlags.None, Px(1f));
    }

    /// <summary>An accent block heading (e.g. "Plugin settings", "Service") inset by <paramref name="padX"/>.</summary>
    internal static void DrawSectionHeader(string title, float padX)
    {
        ImGui.SetCursorPosX(Px(padX));
        ImGui.TextColored(ThemeService.Current.Accent, title);
        ImGui.Spacing();
    }

    /// <summary>A larger accent-light page title for a hub sub-page, inset by <paramref name="padX"/>.</summary>
    internal static void DrawSubpageHeading(string title, float padX)
    {
        ImGui.SetCursorPosX(Px(padX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(ThemeService.Current.AccentLight, title);
        }
        ImGui.Spacing();
    }

    /// <summary>One row of a grouped "menu card": a full-width hit target with a leading coloured icon, a
    /// label, an optional unseen-count badge, and a trailing chevron (or external-link glyph). Flat, not a
    /// filled button: the caller draws the card background and border. Shared by the Settings "Other" list
    /// and the "My" hub.</summary>
    internal static bool DrawMenuRow(float winW, float rowH, string id, Dalamud.Interface.FontAwesomeIcon icon,
        Vector4 iconColor, string label, bool isLast, bool external, int badge, float padX)
    {
        ImGui.SetCursorPosX(Px(padX));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(1f, 1f, 1f, 0.05f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(1f, 1f, 1f, 0.08f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(1f, 1f, 1f, 0.12f));
        var clicked = ImGui.Selectable(id, false, ImGuiSelectableFlags.None, new Vector2(winW - Px(padX) * 2f, rowH));
        ImGui.PopStyleColor(3);

        var rmin = ImGui.GetItemRectMin();
        var rmax = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        var midY = (rmin.Y + rmax.Y) * 0.5f;
        var iconFontPtr = Plugin.PluginInterface.UiBuilder.FontIcon;

        var iconPx = Px(18f);
        ImGui.PushFont(iconFontPtr);
        var iconFont = ImGui.GetFont();
        var iconGlyph = icon.ToIconString();
        var iconSz = ImGui.CalcTextSize(iconGlyph) * (iconPx / ImGui.GetFontSize());
        ImGui.PopFont();
        var iconX = rmin.X + Px(14f);
        dl.AddText(iconFont, iconPx, new Vector2(iconX, midY - iconSz.Y * 0.5f), ImGui.GetColorU32(iconColor), iconGlyph);

        var labelSz = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(iconX + iconSz.X + Px(14f), midY - labelSz.Y * 0.5f),
            ImGui.GetColorU32(new Vector4(0.93f, 0.93f, 0.96f, 1f)), label);

        var rightX = rmax.X - Px(14f);
        var chevGlyph = (external ? Dalamud.Interface.FontAwesomeIcon.ExternalLinkAlt : Dalamud.Interface.FontAwesomeIcon.ChevronRight).ToIconString();
        var chevPx = external ? Px(12f) : Px(13f);
        ImGui.PushFont(iconFontPtr);
        var chevFont = ImGui.GetFont();
        var chevSz = ImGui.CalcTextSize(chevGlyph) * (chevPx / ImGui.GetFontSize());
        ImGui.PopFont();
        dl.AddText(chevFont, chevPx, new Vector2(rightX - chevSz.X, midY - chevSz.Y * 0.5f),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.30f)), chevGlyph);

        if (badge > 0)
        {
            var badgeText = badge.ToString();
            var badgeSz = ImGui.CalcTextSize(badgeText);
            var pad = Px(7f);
            var pillH = Px(18f);
            var pillRight = rightX - chevSz.X - Px(10f);
            var pillMin = new Vector2(pillRight - badgeSz.X - pad * 2f, midY - pillH * 0.5f);
            var pillMax = new Vector2(pillRight, midY + pillH * 0.5f);
            dl.AddRectFilled(pillMin, pillMax, ImGui.GetColorU32(ThemeService.Current.Accent), pillH * 0.5f);
            dl.AddText(new Vector2(pillMin.X + pad, midY - badgeSz.Y * 0.5f), 0xFFFFFFFFu, badgeText);
        }

        if (!isLast)
        {
            dl.AddLine(new Vector2(rmin.X + Px(14f), rmax.Y), new Vector2(rmax.X - Px(14f), rmax.Y),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), 1f);
        }

        return clicked;
    }

    /// <summary>The community Discord invite, shared by the connectivity/error screens and Settings.</summary>
    internal const string DiscordInvite = "https://discord.gg/SkyQmpxWhB";

    /// <summary>Discord-blurple call-to-action button; opens the community invite on click.</summary>
    internal static void DrawDiscordButton(string label, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.345f, 0.396f, 0.949f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.42f, 0.47f, 1.0f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.28f, 0.32f, 0.80f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (ImGui.Button(label, size))
        {
            OpenDiscord();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }

    /// <summary>Opens the community Discord invite in the user's browser. Failures are logged, never surfaced.</summary>
    internal static void OpenDiscord()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(DiscordInvite) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[SharedUiHelpers] Failed to open Discord invite.");
        }
    }

    /// <summary>Draws a full-width amber caution callout containing the wrapped <paramref name="text"/>, the
    /// standard warning card sitting beside a form field. Advances the cursor to just below the box.</summary>
    internal static void DrawWarningCard(string text, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var boxTL = ImGui.GetCursorScreenPos();
        var pad = Px(10f, 8f);
        var textW = width - pad.X * 2f;

        ImGui.SetCursorScreenPos(boxTL + pad);
        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + textW);
        var preY = ImGui.GetCursorScreenPos().Y;
        ImGui.TextColored(UiColors.Amber, text);
        ImGui.PopTextWrapPos();
        var boxH = (ImGui.GetCursorScreenPos().Y - preY) + pad.Y * 2f;

        dl.AddRectFilled(boxTL, boxTL + new Vector2(width, boxH), UiColors.WarningBoxFill, Px(6f));
        dl.AddRect(boxTL, boxTL + new Vector2(width, boxH), UiColors.WarningBoxBorder, Px(6f), ImDrawFlags.None, 1.5f);
        ImGui.SetCursorScreenPos(new Vector2(boxTL.X, boxTL.Y + boxH));
    }

    /// <summary>Recolours the draw-list vertices added since <paramref name="vtxStart"/> with a horizontal
    /// accent gradient anchored to screen-x and scrolled by <paramref name="phase"/> — the shared animated
    /// sheen used by the selected nav button and the decide-later pill. Callers guard on reduce-motion.</summary>
    internal static void GradientSweepVertices(ImDrawListPtr dl, int vtxStart, Vector4 a, Vector4 b, float phase)
    {
        var k = MathF.Tau / Px(70f);
        for (int v = vtxStart; v < dl.VtxBuffer.Size; v++)
        {
            var vert = dl.VtxBuffer[v];
            var blend = 0.5f + 0.5f * MathF.Sin(vert.Pos.X * k - phase);
            vert.Col = ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(a, b, blend));
            dl.VtxBuffer[v] = vert;
        }
    }

    /// <summary>The Terms of Service paragraphs, shared by the onboarding ToS step and the Settings ToS view.</summary>
    internal static string[] TermsOfServiceParagraphs() =>
    [
        Loc.T("onboarding.tos_p1"),
        Loc.T("onboarding.tos_p2"),
        Loc.T("onboarding.tos_ownership"),
        Loc.T("onboarding.tos_race_gender"),
        Loc.T("onboarding.tos_p3"),
        Loc.T("onboarding.tos_nsfl"),
        Loc.T("onboarding.tos_p4"),
        Loc.T("onboarding.tos_p5"),
        Loc.T("onboarding.tos_p6"),
        Loc.T("onboarding.tos_ai"),
        Loc.T("onboarding.tos_p7"),
    ];

    /// <summary>Design-pixel thickness of every in-app scrollbar (scaled through <c>Px</c>).</summary>
    internal const float ScrollbarWidth = 10f;

    /// <summary>Pushes the themed scrollbar style (theme accent as the grab) used by every scrolling panel;
    /// pair with <see cref="PopScrollbarStyle"/>.</summary>
    internal static void PushScrollbarStyle()
    {
        var t = ThemeService.Current;
        PushScrollbarStyle(t.ScrollbarGrab, t.ScrollbarGrabHovered, t.ScrollbarGrabActive);
    }

    /// <summary>Pushes the scrollbar style with explicit grab colours, for a non-accent rail such as the
    /// delete-confirmation danger scrollbar; pair with <see cref="PopScrollbarStyle"/>.</summary>
    internal static void PushScrollbarStyle(Vector4 grab, Vector4 grabHovered, Vector4 grabActive)
    {
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.08f, 0.08f, 0.08f, 0.6f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, grab);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, grabHovered);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, grabActive);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, Px(ScrollbarWidth));
    }

    internal static void PopScrollbarStyle()
    {
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
    }

    /// <summary>Shared in-page (in-phone) overlay shell: dims only the current window/content rect, centres a
    /// measured bordered panel, and returns true on a scrim tap (outside the panel). Drawn as a late child so
    /// it layers above the screen's content. <paramref name="panelH"/> is remembered across frames so the
    /// panel settles to its content height. This is the default popup surface (never the screen-locking
    /// ModalHost); model confirms/editors on it.</summary>
    internal static bool DrawPageOverlayPanel(string id, Vector2 winPos, Vector2 winSize, ref float panelH,
                                              float fallbackH, Action<float> drawContent)
    {
        var dismissed = false;
        ImGui.SetCursorScreenPos(winPos);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        using (var overlay = ImRaii.Child($"##overlay_{id}", winSize, false,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
            if (!overlay.Success)
            {
                return false;
            }

            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(winPos, winPos + winSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f)));

            ImGui.SetCursorScreenPos(winPos);
            if (ImGui.InvisibleButton($"##scrim_{id}", winSize))
            {
                dismissed = true;
            }

            var w = Px(300f);
            var pad = Px(16f, 16f);
            var h = panelH > 0f ? panelH : fallbackH;
            var panelPos = winPos + (winSize - new Vector2(w, h)) * 0.5f;

            ImGui.SetCursorScreenPos(panelPos);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, pad);
            using (var panel = ImRaii.Child($"##panel_{id}", new Vector2(w, h), true,
                       ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding))
            {
                if (panel.Success)
                {
                    drawContent(ImGui.GetContentRegionAvail().X);
                    panelH = ImGui.GetCursorPosY() + pad.Y;
                }
            }
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }
        return dismissed;
    }

    /// <summary>Index of <paramref name="value"/> in <paramref name="arr"/>, or <paramref name="fallback"/>
    /// if absent. Maps a stored enum value back to its position in a fixed choice list (e.g. a combo box).</summary>
    internal static int IndexOf<T>(T[] arr, T value, int fallback)
        where T : struct, Enum
    {
        for (var i = 0; i < arr.Length; i++)
        {
            if (EqualityComparer<T>.Default.Equals(arr[i], value))
            {
                return i;
            }
        }
        return fallback;
    }

    /// <summary><c>arr[idx]</c>, or <paramref name="fallback"/> when the index is out of range. Turns a
    /// combo box's selected index back into its enum value.</summary>
    internal static T ValueAt<T>(T[] arr, int idx, T fallback) =>
        idx >= 0 && idx < arr.Length ? arr[idx] : fallback;

    /// <summary>Fills <paramref name="output"/> with one bool per entry in <paramref name="values"/>, true
    /// when that entry is set in <paramref name="mask"/>. Turns a stored bitmask into checkbox states.</summary>
    internal static void MaskToBools<TEnum>(TEnum[] values, TEnum mask,
        Func<TEnum, TEnum, bool> test, bool[] output)
        where TEnum : struct, Enum
    {
        var count = Math.Min(values.Length, output.Length);
        for (var i = 0; i < count; i++)
        {
            output[i] = test(values[i], mask);
        }
    }

    /// <summary>Combines the <paramref name="values"/> whose checkbox in <paramref name="selected"/> is
    /// ticked into one OR'd bitmask. The inverse of <see cref="MaskToBools"/>.</summary>
    internal static TEnum MaskOr<TEnum>(TEnum[] values, bool[] selected, Func<TEnum, TEnum, TEnum> orFn)
        where TEnum : struct, Enum
    {
        var acc = default(TEnum);
        var count = Math.Min(values.Length, selected.Length);
        for (var i = 0; i < count; i++)
        {
            if (selected[i])
            {
                acc = orFn(acc, values[i]);
            }
        }
        return acc;
    }

    /// <summary>Unpacks a 24-bit hour bitmask into <paramref name="hours"/> (bit 0 = 00:00 UTC).</summary>
    internal static void MaskToHours(int mask, bool[] hours)
    {
        var count = Math.Min(hours.Length, 24);
        for (var i = 0; i < count; i++)
        {
            hours[i] = (mask & (1 << i)) != 0;
        }
    }

    /// <summary>Packs a 24-entry hour checkbox array back into a 24-bit mask (bit 0 = 00:00 UTC). The
    /// inverse of <see cref="MaskToHours"/>.</summary>
    internal static int HoursToMask(bool[] hours)
    {
        var mask = 0;
        var count = Math.Min(hours.Length, 24);
        for (var i = 0; i < count; i++)
        {
            if (hours[i])
            {
                mask |= 1 << i;
            }
        }
        return mask;
    }

    /// <summary>An accent-coloured section title with an underline rule.</summary>
    internal static void DrawSectionHeading(string title, ThemeDefinition t)
    {
        ImGui.Spacing();
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(t.AccentLight, title);
        }
        var sepCol = new Vector4(t.AccentLight.X, t.AccentLight.Y, t.AccentLight.Z, 0.30f);
        ImGui.PushStyleColor(ImGuiCol.Separator, sepCol);
        ImGui.Separator();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    /// <summary>A dimmed accent label for the field that follows it.</summary>
    internal static void DrawFieldLabel(string label, ThemeDefinition t)
    {
        ImGui.TextColored(
            new Vector4(t.AccentLight.X, t.AccentLight.Y, t.AccentLight.Z, 0.78f),
            label);
    }

    /// <summary>24-hour click grid; idSuffix keeps ImGui IDs unique across editors.</summary>
    internal static void DrawOnlineHoursEditor(float availW, bool[] hours, string idSuffix)
    {
        var barH = Px(38f);
        var labelH = Px(16f);
        var t = ThemeService.Current;
        var barW = availW / 24f;
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        for (var h = 0; h < 24; h++)
        {
            var x0 = origin.X + h * barW + Px(1.5f);
            var x1 = origin.X + (h + 1) * barW - Px(1.5f);

            ImGui.SetCursorScreenPos(new Vector2(x0, origin.Y));
            if (ImGui.InvisibleButton($"##hr{idSuffix}{h}", new Vector2(x1 - x0, barH)))
            {
                hours[h] = !hours[h];
            }

            var hovered = ImGui.IsItemHovered();

            uint barCol;
            if (hours[h])
            {
                barCol = hovered ? t.AccentLightU32 : t.AccentU32;
            }
            else
            {
                barCol = hovered ? 0xFF4A4A4Au : 0xFF2D2D2Du;
            }

            dl.AddRectFilled(
                new Vector2(x0, origin.Y),
                new Vector2(x1, origin.Y + barH),
                barCol, Px(3f));

            if (hovered)
            {
                var h12 = h == 0 ? 12 : (h > 12 ? h - 12 : h);
                var amPm = h < 12 ? Loc.T("onboarding.time_am") : Loc.T("onboarding.time_pm");
                ImGui.SetTooltip($"{h12}:00 {amPm}  /  {h:D2}:00");
            }
        }

        // Time labels at 0, 6, 12, 18.
        var labelFsz = ImGui.GetFontSize() * 0.82f;
        foreach (var h in new[] { 0, 6, 12, 18 })
        {
            var lx = origin.X + h * barW;
            dl.AddText(ImGui.GetFont(), labelFsz,
                new Vector2(lx + Px(1f), origin.Y + barH + Px(4f)),
                UiColors.TextMuted, $"{h:D2}:00");
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + barH + labelH + Px(4f)));
    }

    /// <summary>Renders the row of selectable language pills (flag + label). <paramref name="flags"/> are the
    /// per-language flag textures (parallel to <see cref="ProfileFields.LanguageEntries"/>); the caller owns
    /// loading them. <paramref name="isSelected"/>/<paramref name="onToggle"/> read and flip selection state,
    /// so the same renderer serves spoken-language and filter-language pickers.</summary>
    internal static void DrawLanguagePillsCore(
        ISharedImmediateTexture?[] flags,
        float flagW, float flagH, bool useCode, string idPrefix,
        Func<int, bool> isSelected, Action<int> onToggle, int? count = null)
    {
        var t = ThemeService.Current;
        var n = count ?? LanguageEntries.Length;
        var availW = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();

        var pillPadX = Px(6f);
        var pillPadY = Px(5f);
        var labelGap = Px(3f);
        var labelH = ImGui.GetTextLineHeight();
        var pillGap = Px(6f);

        var pillW = flagW + pillPadX * 2f;
        var pillH = pillPadY + flagH + labelGap + labelH + pillPadY;

        var totalW = n * pillW + (n - 1) * pillGap;
        var startX = MathF.Max(0f, (availW - totalW) * 0.5f);
        var startY = ImGui.GetCursorPosY();

        for (var i = 0; i < n; i++)
        {
            var selected = isSelected(i);
            var pillX = startX + i * (pillW + pillGap);

            ImGui.SetCursorPos(new Vector2(pillX, startY));
            var sp = ImGui.GetCursorScreenPos();

            ImGui.InvisibleButton($"##{idPrefix}{i}", new Vector2(pillW, pillH));
            if (ImGui.IsItemClicked())
            {
                onToggle(i);
            }
            var hovered = ImGui.IsItemHovered();

            var bgCol = selected
                ? t.AccentWithAlpha(0.28f)
                : hovered ? 0x22FFFFFFu : 0x0DFFFFFFu;
            dl.AddRectFilled(sp, sp + new Vector2(pillW, pillH), bgCol, Px(7f));

            var borderCol = selected ? t.AccentU32 : (hovered ? 0x55FFFFFFu : 0x33FFFFFFu);
            var borderThick = selected ? 2f : 1f;
            dl.AddRect(sp, sp + new Vector2(pillW, pillH), borderCol, Px(7f), ImDrawFlags.None, borderThick);

            var flagTL = sp + new Vector2(pillPadX, pillPadY);
            var flagBR = flagTL + new Vector2(flagW, flagH);
            var flagTex = flags[i]?.GetWrapOrDefault();
            if (flagTex != null)
            {
                dl.AddImageRounded(flagTex.Handle, flagTL, flagBR,
                    Vector2.Zero, Vector2.One, 0xFFFFFFFF, Px(3f));
            }
            else
            {
                dl.AddRectFilled(flagTL, flagBR, t.AccentDarkWithAlpha(0.6f), Px(3f));
                var code = LanguageEntries[i].Code;
                var codeSz = ImGui.CalcTextSize(code);
                dl.AddText(flagTL + (new Vector2(flagW, flagH) - codeSz) * 0.5f, 0xFFFFFFFF, code);
            }

            var label = useCode ? LanguageEntries[i].Code : LanguageEntries[i].Name;
            var labelSz = ImGui.CalcTextSize(label);
            var labelX = sp.X + (pillW - labelSz.X) * 0.5f;
            var labelY = sp.Y + pillPadY + flagH + labelGap;
            dl.AddText(new Vector2(labelX, labelY), selected ? 0xFFFFFFFF : 0xAAFFFFFF, label);
        }

        ImGui.SetCursorPosY(startY + pillH + Px(4f));
    }

    /// <summary>Draws a flair pill (a capsule of <paramref name="bgHex"/> with contrasting text) at screen
    /// position <paramref name="pos"/> via the draw list and returns its width. Shows <paramref name="description"/>
    /// as a tooltip while hovered (non-rotated screens only).</summary>
    private const float FlairPillPadX = 7f;

    /// <summary>Width a flair pill occupies for <paramref name="text"/> in the current font.</summary>
    internal static float FlairPillWidth(string text)
    {
        return ImGui.CalcTextSize(text).X + Px(FlairPillPadX) * 2f;
    }

    internal static float DrawFlairPill(ImDrawListPtr dl, Vector2 pos, string text, string description, string bgHex, float alpha = 1f)
    {
        var padX = Px(FlairPillPadX);
        var h = ImGui.GetTextLineHeight() + Px(4f);
        var textSz = ImGui.CalcTextSize(text);
        var w = textSz.X + padX * 2f;
        var br = pos + new Vector2(w, h);
        dl.AddRectFilled(pos, br, HexToAbgr(bgHex, alpha), h * 0.5f);
        dl.AddText(new Vector2(pos.X + padX, pos.Y + (h - textSz.Y) * 0.5f), ContrastText(bgHex, alpha), text);
        if (!string.IsNullOrEmpty(description) && ImGui.IsMouseHoveringRect(pos, br))
        {
            ImGui.SetTooltip(description);
        }
        return w;
    }

    /// <summary>"#RRGGBB" → ImGui packed colour (0xAABBGGRR) at the given alpha (0–1).</summary>
    internal static uint HexToAbgr(string bgHex, float alpha = 1f)
    {
        var (r, g, b) = ParseHex(bgHex);
        var a = (uint)Math.Clamp((int)(alpha * 255f), 0, 255);
        return (a << 24) | ((uint)b << 16) | ((uint)g << 8) | (uint)r;
    }

    /// <summary>White or near-black text colour for legibility on <paramref name="bgHex"/>, at the given alpha.</summary>
    internal static uint ContrastText(string bgHex, float alpha = 1f)
    {
        var (r, g, b) = ParseHex(bgHex);
        var a = (uint)Math.Clamp((int)(alpha * 255f), 0, 255);
        var lum = (0.299f * r + 0.587f * g + 0.114f * b) / 255f;
        return (a << 24) | (lum > 0.6f ? 0x00222222u : 0x00FFFFFFu);
    }

    private static (int r, int g, int b) ParseHex(string hex)
    {
        var s = (hex ?? string.Empty).TrimStart('#');
        const System.Globalization.NumberStyles Hex = System.Globalization.NumberStyles.HexNumber;
        if (s.Length >= 6
            && int.TryParse(s.AsSpan(0, 2), Hex, null, out var r)
            && int.TryParse(s.AsSpan(2, 2), Hex, null, out var g)
            && int.TryParse(s.AsSpan(4, 2), Hex, null, out var b))
        {
            return (r, g, b);
        }
        return (88, 101, 242);
    }

    /// <summary>True when the picked file is a cloud placeholder whose contents aren't on disk (OneDrive
    /// "online-only" / Files On-Demand, and other providers using the same attributes). Such a file may fail
    /// to read if hydration doesn't complete, so callers reject the pick and ask for a locally-available file.
    /// A probing failure returns false so a genuine read error still surfaces through the normal path.</summary>
    internal static bool IsUnavailableCloudFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        try
        {
            // Raw Win32 recall bits, not named in the .NET FileAttributes enum. Reading attributes is
            // metadata-only, so it never triggers a download or blocks.
            const FileAttributes recallOnOpen = (FileAttributes)0x00040000;
            const FileAttributes recallOnDataAccess = (FileAttributes)0x00400000;
            var attrs = File.GetAttributes(path);
            return (attrs & (FileAttributes.Offline | recallOnOpen | recallOnDataAccess)) != 0;
        }
        catch
        {
            // Fail open on any error (incl. Wine / Windows 7-8, where these markers aren't supported): treat
            // the file as available so the pick proceeds as before and never crashes.
            return false;
        }
    }

    /// <summary>Loads a just-picked source image for preview/cropping. WIC (notably on Wine) has no WebP
    /// codec, so a <c>.webp</c> source is transcoded to a cached PNG the loader can read; other formats load
    /// straight from disk. The transcoded preview keeps the source's pixel dimensions, so the crop rect still
    /// maps onto the original at save time.</summary>
    internal static ISharedImmediateTexture? LoadPickedPreview(string path)
    {
        if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var png = PhotoTransform.DecodeToPng(File.ReadAllBytes(path));
                var dir = Path.Combine(Path.GetTempPath(), "AetherLovePreview");
                var tex = AvatarDiskCache.Store(dir, "pick", png);
                if (tex is not null)
                {
                    return tex;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[SharedUiHelpers] WebP preview transcode failed.");
            }
        }
        return Plugin.TextureProvider.GetFromFile(path);
    }

    /// <summary>Reads an image file, applies the crop + resize to the slot's target size, and packs the
    /// small PNG into an upload DTO. The crop fields are set to the full (already-processed) image — the
    /// server's signal that no further crop/resize is needed. CPU-bound; call off the UI thread. Throws
    /// <see cref="PhotoProcessingException"/> (a localizable <c>AL_ERR</c> payload) on a bad image.</summary>
    internal static PhotoUploadDto ReadPhotoUpload(string path, Vector4 cropRect, bool isNsfw, PhotoKind kind)
    {
        var bytes = File.ReadAllBytes(path);
        var crop = new CropRect((int)cropRect.X, (int)cropRect.Y, (int)cropRect.Z, (int)cropRect.W);
        var png = PhotoTransform.ProcessToPng(bytes, crop, kind);
        var (width, height) = PhotoTransform.TargetDimensions(kind);
        return new PhotoUploadDto(
            Base64: Convert.ToBase64String(png),
            CropX: 0,
            CropY: 0,
            CropWidth: width,
            CropHeight: height,
            IsNsfw: isNsfw);
    }
}
