using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.Shared;
using AetherLove.Shared.Profile;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.UI;

internal static class SharedUiHelpers
{
    /// <summary>UV rect that cover-fits a source texture into a destination rect, centre-cropping the
    /// overflowing axis. Works for any source aspect (legacy square venue banners and the wide 25:9 ones).</summary>
    internal static (Vector2 Uv0, Vector2 Uv1) CoverFitUvs(float srcW, float srcH, float destW, float destH)
    {
        var srcAspect = srcW / srcH;
        var destAspect = destW / destH;
        if (srcAspect > destAspect)
        {
            var visible = destAspect / srcAspect;
            return (new Vector2((1f - visible) * 0.5f, 0f), new Vector2((1f + visible) * 0.5f, 1f));
        }
        var visibleY = srcAspect / destAspect;
        return (new Vector2(0f, (1f - visibleY) * 0.5f), new Vector2(1f, (1f + visibleY) * 0.5f));
    }

    /// <summary>Pseudo-blur via concentric rings of low-alpha image draws plus a frosted tint; ImGui has
    /// no real blur, and sparse fixed offsets read as discrete ghost copies on detailed images, so the
    /// rings are dense and tight to approximate a gaussian. Callers pass their own UVs so blurred and
    /// revealed states crop identically.</summary>
    internal static void DrawBlurredCover(ImDrawListPtr dl, Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap wrap,
        Vector2 tl, Vector2 sz, Vector2 uv0, Vector2 uv1, float alpha = 1f, float rounding = 0f)
    {
        if (alpha <= 0f)
        {
            return;
        }

        // Center + 4 rings x 8 directions = 33 samples, radii chosen so neighbours overlap.
        ReadOnlySpan<float> radii = [2.5f, 5.5f, 9f, 13f];
        var samples = 1 + radii.Length * 8;
        var sampleA = (uint)Math.Clamp((int)(alpha / samples * 255f), 1, 255);
        var sampleCol = (sampleA << 24) | 0x00FFFFFFu;

        dl.PushClipRect(tl, tl + sz, true);
        dl.AddImage(wrap.Handle, tl, tl + sz, uv0, uv1, sampleCol);
        foreach (var radius in radii)
        {
            var r = Px(radius);
            for (var i = 0; i < 8; i++)
            {
                var angle = i * (MathF.PI / 4f) + (radius * 0.7f);
                var off = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * r;
                dl.AddImage(wrap.Handle, tl + off, tl + sz + off, uv0, uv1, sampleCol);
            }
        }
        dl.PopClipRect();

        var tintA = (uint)Math.Clamp((int)(alpha * 0.55f * 255f), 0, 255);
        dl.AddRectFilled(tl, tl + sz, (tintA << 24) | 0x00101010u, rounding);
    }

    /// <summary>Copies to the clipboard and shows the one-time "chat outside the app at your own risk"
    /// warning modal until acknowledged.</summary>
    internal static void CopyTextWithLinkWarning(string text)
    {
        ImGui.SetClipboardText(text ?? string.Empty);
        if (!UiHost.Configuration.AcknowledgedProfileCopyTextWarning)
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
            UiHost.Configuration.AcknowledgedProfileCopyTextWarning = true;
            UiHost.Configuration.Save();
            Widgets.ModalHost.Instance?.Close();
        }
    }

    /// <summary>Red destructive-action button colours; pop 3 after the button.</summary>
    internal static void PushDangerButton()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.32f, 0.10f, 0.10f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.55f, 0.15f, 0.15f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.22f, 0.06f, 0.06f, 1f));
    }

    /// <summary>Returns true the frame it is clicked; the caller flips the bound bool.</summary>
    internal static bool DrawToggleSwitch(string id, string label, bool on)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var h = ImGui.GetTextLineHeight() + Px(6f);
        var trackW = h * 1.85f;
        var labelSz = ImGui.CalcTextSize(label);
        var origin = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton(id, new Vector2(trackW + Px(10f) + labelSz.X, h));
        var hovered = ImGui.IsItemHovered();

        var trackBR = origin + new Vector2(trackW, h);
        var trackCol = on
            ? t.Accent with { W = hovered ? 0.95f : 0.80f }
            : new Vector4(1f, 1f, 1f, hovered ? 0.20f : 0.13f);
        dl.AddRectFilled(origin, trackBR, ImGui.GetColorU32(trackCol), h * 0.5f);

        var knobR = h * 0.5f - Px(3f);
        var knobX = on ? trackBR.X - knobR - Px(3f) : origin.X + knobR + Px(3f);
        dl.AddCircleFilled(new Vector2(knobX, origin.Y + h * 0.5f), knobR, 0xFFFFFFFFu);
        dl.AddText(new Vector2(trackBR.X + Px(10f), origin.Y + (h - labelSz.Y) * 0.5f),
            ImGui.GetColorU32(UiColors.Body), label);
        return clicked;
    }

    internal static void DrawSectionPill(string title, Vector4 accent, FontAwesomeIcon icon)
    {
        var dl = ImGui.GetWindowDrawList();
        var titleSz = ImGui.CalcTextSize(title);
        var iconPx = ImGui.GetFontSize() * 0.9f;
        var iconSz = IconDraw.Measure(icon, iconPx);
        var innerPad = Px(11f);
        var gap = Px(7f);
        var h = MathF.Max(titleSz.Y, iconSz.Y) + Px(9f);
        var w = innerPad + iconSz.X + gap + titleSz.X + innerPad;

        ImGui.SetCursorPosX(Px(16f));
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(w, h);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(accent with { W = 0.16f }), h * 0.5f);
        dl.AddRect(tl, br, ImGui.GetColorU32(accent with { W = 0.5f }), h * 0.5f, ImDrawFlags.None, Px(1f));

        IconDraw.Add(dl, icon, iconPx, new Vector2(tl.X + innerPad, tl.Y + (h - iconSz.Y) * 0.5f),
            ImGui.GetColorU32(accent));
        dl.AddText(new Vector2(tl.X + innerPad + iconSz.X + gap, tl.Y + (h - titleSz.Y) * 0.5f),
            0xFFFFFFFFu, title);

        ImGui.Dummy(new Vector2(w, h));
        ImGui.Spacing();
    }

    internal static void HelpTooltip(string text)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }

    /// <summary>A checkbox for a fixed-width column: the label is ellipsized to fit and the full text shows
    /// on hover. Option labels run much longer in some languages (German especially) and would otherwise
    /// spill under the next column. <paramref name="columnWidth"/> is the whole column, box included.</summary>
    internal static bool CheckboxTruncated(string id, string label, ref bool value, float columnWidth)
    {
        var room = columnWidth - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemInnerSpacing.X;
        var fitted = TruncateToWidth(label, room);
        var changed = ImGui.Checkbox($"{fitted}##{id}", ref value);
        if (!string.Equals(fitted, label, StringComparison.Ordinal) && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(label);
        }
        return changed;
    }

    internal static string TruncateToWidth(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        // Single-line by contract: an embedded newline would render as a second line on top of whatever
        // sits below the caller's fixed layout (and CalcTextSize would only measure the widest line).
        if (text.Contains('\n') || text.Contains('\r'))
        {
            text = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        }
        if (ImGui.CalcTextSize(text).X <= maxWidth)
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

    // Ctrl+V doesn't fire reliably in ImGui inputs in-game; armed pastes are spliced in through the input
    // callback, the only place ImGui exposes the caret.
    private static bool _pasteArmed;
    private static int _pasteArmedFrame;

    private static readonly Dictionary<string, (double Start, int Seen)> _overlayFades = new();

    /// <summary>Call right after drawing a text input; a true <paramref name="textChanged"/> means the paste
    /// was handled natively, so arming would insert it twice.</summary>
    internal static void ArmPasteIfIgnored(bool textChanged)
    {
        if (!textChanged && ImGui.IsItemActive()
            && ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.V, false))
        {
            _pasteArmed = true;
            _pasteArmedFrame = ImGui.GetFrameCount();
        }
    }

    /// <summary>Call from the input's CallbackAlways handler; returns true when text was inserted.</summary>
    internal static unsafe bool TryConsumeArmedPaste(ImGuiInputTextCallbackData* p)
    {
        if (!_pasteArmed)
        {
            return false;
        }
        _pasteArmed = false;
        if (ImGui.GetFrameCount() - _pasteArmedFrame > 1)
        {
            return false;
        }

        var clip = ImGui.GetClipboardText();
        if (string.IsNullOrEmpty(clip))
        {
            return false;
        }
        var insert = System.Text.Encoding.UTF8.GetBytes(clip.Replace("\r\n", "\n").Replace('\r', '\n'));

        var selMin = Math.Min(p->SelectionStart, p->SelectionEnd);
        var selMax = Math.Max(p->SelectionStart, p->SelectionEnd);
        if (selMax > selMin)
        {
            var tail = p->BufTextLen - selMax;
            for (var i = 0; i < tail; i++)
            {
                p->Buf[selMin + i] = p->Buf[selMax + i];
            }
            p->BufTextLen -= selMax - selMin;
            p->Buf[p->BufTextLen] = 0;
            p->CursorPos = selMin;
            p->SelectionStart = selMin;
            p->SelectionEnd = selMin;
        }

        var len = Math.Min(insert.Length, p->BufSize - p->BufTextLen - 1);
        while (len > 0 && len < insert.Length && (insert[len] & 0xC0) == 0x80)
        {
            len--;
        }
        if (len <= 0)
        {
            return false;
        }

        var cursor = Math.Clamp(p->CursorPos, 0, p->BufTextLen);
        for (var i = p->BufTextLen - 1; i >= cursor; i--)
        {
            p->Buf[i + len] = p->Buf[i];
        }
        for (var i = 0; i < len; i++)
        {
            p->Buf[cursor + i] = insert[i];
        }
        p->BufTextLen += len;
        p->Buf[p->BufTextLen] = 0;
        p->CursorPos = cursor + len;
        p->SelectionStart = p->CursorPos;
        p->SelectionEnd = p->CursorPos;
        p->BufDirty = 1;
        return true;
    }

    /// <summary>ImGui.InputTextMultiline with working Ctrl+V.</summary>
    internal static bool InputTextMultilineWithPaste(string label, ref string text, int maxLength, Vector2 size)
    {
        var changed = ImGui.InputTextMultiline(label, ref text, maxLength, size,
            ImGuiInputTextFlags.CallbackAlways, PasteInputCallback);
        ArmPasteIfIgnored(changed);
        return changed;
    }

    private static unsafe int PasteInputCallback(ImGuiInputTextCallbackDataPtr data)
    {
        try
        {
            ImGuiInputTextCallbackData* p = data;
            if (p->EventFlag == ImGuiInputTextFlags.CallbackAlways)
            {
                TryConsumeArmedPaste(p);
            }
        }
        catch
        {
            // A managed exception must not cross into the native ImGui call.
        }
        return 0;
    }

    /// <summary>The displayed song name is server-resolved, never user-typed. Every row keeps the caller's
    /// cursor X, so an indented call site stays aligned past the first line. The informational tooltip lives
    /// on the section heading, not per provider.</summary>
    internal static void DrawMusicLinkField(MusicLinkField field, string label, float width)
    {
        field.Tick();
        var x = ImGui.GetCursorPosX();
        ImGui.Text(label);
        ImGui.SetCursorPosX(x);
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputText($"##music_{field.Provider}", ref field.Input, 256))
        {
            field.OnInputChanged();
        }

        if (field.Fetching)
        {
            ImGui.SetCursorPosX(x);
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), Loc.T("music.fetching"));
        }
        else if (field.Invalid)
        {
            ImGui.SetCursorPosX(x);
            ImGui.PushTextWrapPos(x + width);
            ImGui.TextColored(UiColors.Danger, Loc.T("music.invalid"));
            ImGui.PopTextWrapPos();
        }
        else if (field.ResolvedName.Length > 0)
        {
            ImGui.SetCursorPosX(x);
            ImGui.PushTextWrapPos(x + width);
            ImGui.TextColored(UiColors.Success, $"  {field.ResolvedName}");
            ImGui.PopTextWrapPos();
        }
        else if (field.ResolvedRef.Length > 0)
        {
            ImGui.SetCursorPosX(x);
            ImGui.TextColored(UiColors.Hint, Loc.T("music.saved"));
        }
        ImGui.Spacing();
    }

    internal static void PushThemeButton(ThemeDefinition t)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
    }

    internal static void PopThemeButton() => ImGui.PopStyleColor(3);

    /// <summary>Show the pointer-hand cursor while the item just submitted is hovered. Call right after a
    /// button/InvisibleButton so it targets that item; ImGui doesn't do this for buttons on its own.</summary>
    internal static void HandOnHover()
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
    }

    /// <summary>An <see cref="ImGui"/> button that also shows the pointer-hand cursor on hover. Use in place of a
    /// bare <c>ImGui.Button</c> so bespoke buttons pick up the cursor with no per-call-site line; the caller keeps
    /// full styling control (push colours/vars around the call exactly as with <c>ImGui.Button</c>).</summary>
    internal static bool Button(string label, Vector2 size)
    {
        var clicked = ImGui.Button(label, size);
        HandOnHover();
        return clicked;
    }

    /// <summary>One icon+label row for a hand-rolled popup menu. Returns true on click.</summary>
    internal static bool DrawIconMenuItem(FontAwesomeIcon icon, string label,
                                          uint textColor = 0xFFEEEEEE, bool enabled = true)
    {
        var dl = ImGui.GetWindowDrawList();
        var itemH = ImGui.GetFrameHeight();
        var cursor = ImGui.GetCursorScreenPos();
        var fontSize = ImGui.GetFontSize();
        const float ItemW = 200f;
        const float IconOffX = 10f;
        const float IconAreaW = 20f;
        ImGui.InvisibleButton($"##mi_{label}", new Vector2(Px(ItemW), itemH));
        var clicked = enabled && ImGui.IsItemClicked();
        if (enabled && ImGui.IsItemHovered())
        {
            dl.AddRectFilled(cursor, cursor + new Vector2(Px(ItemW), itemH), 0x30FFFFFF);
            HandOnHover();
        }
        var drawColor = enabled ? textColor : 0x66AAAAAAu;
        // Draw the glyph through the large downscaled icon font so it stays sharp (never upscaled) at every
        // phone size, sized to the row's text and centred vertically on the row.
        IconDraw.AddCentered(dl, icon, fontSize,
            new Vector2(cursor.X + Px(IconOffX) + Px(IconAreaW) * 0.5f, cursor.Y + itemH * 0.5f),
            drawColor);
        var labelSz = ImGui.CalcTextSize(label);
        dl.AddText(
            new Vector2(cursor.X + Px(IconOffX) + Px(IconAreaW) + Px(8f),
                        cursor.Y + (itemH - labelSz.Y) * 0.5f),
            drawColor, label);
        return clicked;
    }

    /// <summary>Draw after the scroll child so the pill's overlay layers above it. Returns true on click.</summary>
    internal static bool DrawFloatingBackPill(Vector2 pos, string tooltip, FontAwesomeIcon destinationIcon)
    {
        var padX = Px(11f);
        var padY = Px(7f);
        var iconGap = Px(7f);
        var t = ThemeService.Current;

        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var backStr = FontAwesomeIcon.ArrowLeft.ToIconString();
        var destStr = destinationIcon.ToIconString();
        var backSz = ImGui.CalcTextSize(backStr);
        var destSz = ImGui.CalcTextSize(destStr);
        ImGui.PopFont();

        var pillW = padX + backSz.X + iconGap + destSz.X + padX;
        var pillH = MathF.Max(backSz.Y, destSz.Y) + padY * 2f;
        var pillBR = pos + new Vector2(pillW, pillH);

        ImGui.SetCursorScreenPos(pos);
        using var wp = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var overlay = ImRaii.Child("##floatingBackPill", new Vector2(pillW, pillH), false,
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoNav);
        if (!overlay.Success)
        {
            return false;
        }

        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton("##floatingBackPillBtn", new Vector2(pillW, pillH));
        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();
        if (hovered)
        {
            ImGui.SetTooltip(tooltip);
            HandOnHover();
        }

        var tmod = (float)(ImGui.GetTime() % 4.0);
        var pulse = tmod < 0.5f ? MathF.Sin(tmod * MathF.PI / 0.5f) : 0f;
        var baseBg = hovered ? t.ButtonHovered : t.ButtonNormal;
        var blendBg = Vector4.Lerp(baseBg, t.AccentLight, pulse * 0.55f);
        dl.AddRectFilled(pos, pillBR, ImGui.ColorConvertFloat4ToU32(blendBg), pillH * 0.5f);
        if (pulse > 0.05f)
        {
            var glowAlpha = (uint)(pulse * 80f) << 24;
            dl.AddRect(pos, pillBR, glowAlpha | (t.AccentU32 & 0x00FFFFFF), pillH * 0.5f, ImDrawFlags.None, 2f);
        }

        var iconAlpha = MathF.Min(1f, (hovered ? 1.0f : 0.80f) + pulse * 0.20f);
        var iconCol = ((uint)(iconAlpha * 255f) << 24) | 0x00FFFFFF;
        var iconY = pos.Y + padY;
        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(pos.X + padX, iconY), iconCol, backStr);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(pos.X + padX + backSz.X + iconGap, iconY), iconCol, destStr);
        ImGui.PopFont();
        return clicked;
    }

    internal static void DrawNoticeCard(float listW, Dalamud.Interface.FontAwesomeIcon icon, Vector4 accent,
        DateTimeOffset whenUtc, string body, bool seen, float padX, string? subheading = null)
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
        var subH = subheading is null ? 0f : ImGui.CalcTextSize(subheading, false, contentW).Y + Px(4f);
        var cardH = Px(11f) + lineH + Px(6f) + subH + bodyH + Px(11f);

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
        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
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

        var bodyY = hy + lineH + Px(6f);
        if (subheading is not null)
        {
            dl.AddText(font, fontSize, new Vector2(tl.X + cx, bodyY), ImGui.GetColorU32(accent), subheading, contentW);
            bodyY += subH;
        }
        dl.AddText(font, fontSize, new Vector2(tl.X + cx, bodyY), ImGui.GetColorU32(bodyCol), body, contentW);

        ImGui.Dummy(new Vector2(cardW, cardH));
    }

    internal readonly record struct MenuRow(Dalamud.Interface.FontAwesomeIcon Icon, Vector4 IconColor, string Label,
        int Badge, bool External, Action OnClick,
        Dalamud.Interface.FontAwesomeIcon? PulseIcon = null, Vector4 PulseColor = default);

    /// <summary>Design-pixel height of one menu-card row.</summary>
    internal const float MenuCardRowHeight = 44f;

    internal static void DrawMenuCard(string idPrefix, float winW, float padX, IReadOnlyList<MenuRow> rows)
    {
        var rowH = Px(MenuCardRowHeight);
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
                    i == rows.Count - 1, r.External, r.Badge, padX, r.PulseIcon, r.PulseColor))
            {
                r.OnClick();
            }
        }
        ImGui.PopStyleVar();
        dl.AddRect(cardMin, cardMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.07f)), Px(10f), ImDrawFlags.None, Px(1f));
    }

    internal static void DrawPerkCard(float winW, float padX, Dalamud.Interface.FontAwesomeIcon icon,
        Vector4 accent, string title, string body)
    {
        var dl = ImGui.GetWindowDrawList();
        var pad = Px(padX);
        var w = winW - pad * 2f;
        var innerPad = Px(12f);
        var iconCol = Px(46f);
        var gap = Px(12f);
        var textW = w - innerPad * 2f - iconCol - gap;

        var titleH = ImGui.GetTextLineHeight();
        var bodyH = ImGui.CalcTextSize(body, false, textW).Y;
        var contentH = titleH + Px(4f) + bodyH;
        var cardH = MathF.Max(iconCol, contentH) + innerPad * 2f;

        var cursorBefore = ImGui.GetCursorPos();
        var tl = ImGui.GetCursorScreenPos() + new Vector2(pad, 0f);
        var br = tl + new Vector2(w, cardH);
        var rounding = Px(12f);

        dl.AddRectFilled(tl, br, ImGui.GetColorU32(accent with { W = 0.10f }), rounding);
        dl.AddRect(tl, br, ImGui.GetColorU32(accent with { W = 0.42f }), rounding, ImDrawFlags.None, Px(1f));

        var discR = iconCol * 0.5f;
        var discC = new Vector2(tl.X + innerPad + discR, tl.Y + cardH * 0.5f);
        dl.AddCircleFilled(discC, discR, ImGui.GetColorU32(accent with { W = 0.16f }), 40);
        dl.AddCircle(discC, discR, ImGui.GetColorU32(accent with { W = 0.55f }), 40, Px(1.5f));

        var iconFont = UiHost.PluginInterface.UiBuilder.FontIcon;
        ImGui.PushFont(iconFont);
        var glyph = icon.ToIconString();
        var gsz = ImGui.CalcTextSize(glyph);
        dl.AddText(new Vector2(discC.X - gsz.X * 0.5f, discC.Y - gsz.Y * 0.5f), ImGui.GetColorU32(accent), glyph);
        ImGui.PopFont();

        var textX = tl.X + innerPad + iconCol + gap;
        var titleCol = Vector4.Lerp(accent, new Vector4(1f, 1f, 1f, 1f), 0.35f);
        ImGui.SetCursorScreenPos(new Vector2(textX, tl.Y + innerPad));
        ImGui.TextColored(titleCol, title);
        ImGui.SetCursorScreenPos(new Vector2(textX, tl.Y + innerPad + titleH + Px(4f)));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
        ImGui.TextColored(UiColors.Subtle, body);
        ImGui.PopTextWrapPos();

        ImGui.SetCursorPos(new Vector2(cursorBefore.X, cursorBefore.Y + cardH + Px(10f)));
    }

    internal static void DrawSectionHeader(string title, float padX) =>
        DrawSectionHeader(title, padX, ThemeService.Current.Accent);

    internal static void DrawSectionHeader(string title, float padX, Vector4 color)
    {
        ImGui.SetCursorPosX(Px(padX));
        ImGui.TextColored(color, title);
        ImGui.Spacing();
    }

    internal static void DrawSubpageHeading(string title, float padX)
    {
        ImGui.SetCursorPosX(Px(padX));
        using (UiFonts.H3?.Push())
        {
            ImGui.TextColored(ThemeService.Current.AccentLight, title);
        }
        ImGui.Spacing();
    }

    /// <summary>A left-aligned config checkbox that saves on toggle. <paramref name="padX"/> is the caller's left inset.</summary>
    internal static void SettingCheckbox(float padX, string label, Func<bool> get, Action<bool> set)
    {
        ImGui.SetCursorPosX(Px(padX));
        var value = get();
        var boxW = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
        var availW = ImGui.GetContentRegionAvail().X - Px(padX);
        if (ImGui.CalcTextSize(label).X + boxW <= availW)
        {
            if (ImGui.Checkbox(label, ref value))
            {
                set(value);
                UiHost.Configuration.Save();
            }
            HandOnHover();
            return;
        }

        // Long labels wrap beside the box instead of clipping at the phone edge.
        if (ImGui.Checkbox($"##chk_{label}", ref value))
        {
            set(value);
            UiHost.Configuration.Save();
        }
        HandOnHover();
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + availW - boxW);
        ImGui.TextUnformatted(label);
        ImGui.PopTextWrapPos();
    }

    /// <summary>A muted (?) glyph that shows a wrapped tooltip on hover. Place after <see cref="ImGui.SameLine()"/>.</summary>
    internal static void HelpMarker(string text)
    {
        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        ImGui.TextColored(UiColors.Muted, FontAwesomeIcon.QuestionCircle.ToIconString());
        ImGui.PopFont();
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(Px(360f));
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    internal static bool DrawMenuRow(float winW, float rowH, string id, Dalamud.Interface.FontAwesomeIcon icon,
        Vector4 iconColor, string label, bool isLast, bool external, int badge, float padX,
        Dalamud.Interface.FontAwesomeIcon? pulseIcon = null, Vector4 pulseColor = default)
    {
        ImGui.SetCursorPosX(Px(padX));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(1f, 1f, 1f, 0.05f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(1f, 1f, 1f, 0.08f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(1f, 1f, 1f, 0.12f));
        var clicked = ImGui.Selectable(id, false, ImGuiSelectableFlags.None, new Vector2(winW - Px(padX) * 2f, rowH));
        HandOnHover();
        ImGui.PopStyleColor(3);

        var rmin = ImGui.GetItemRectMin();
        var rmax = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        var midY = (rmin.Y + rmax.Y) * 0.5f;
        var iconFontPtr = UiHost.PluginInterface.UiBuilder.FontIcon;

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

        if (pulseIcon is { } pulse)
        {
            var glow = AccessibilityService.ReduceMotion
                ? 1f
                : 0.65f + 0.35f * MathF.Sin((float)ImGui.GetTime() * 2.6f);
            IconDraw.AddCentered(dl, pulse, Px(16f),
                new Vector2(rightX - chevSz.X - Px(20f), midY), ImGui.GetColorU32(pulseColor with { W = glow }));
        }
        else if (badge > 0)
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

    internal const string DiscordInvite = "https://discord.gg/SkyQmpxWhB";

    internal static void DrawDiscordButton(string label, Vector2 size, string? url = null)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.345f, 0.396f, 0.949f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.42f, 0.47f, 1.0f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.28f, 0.32f, 0.80f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(8f));
        if (Button(label, size))
        {
            OpenDiscord(url);
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }

    internal static void OpenDiscord(string? url = null)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url ?? DiscordInvite) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[SharedUiHelpers] Failed to open Discord invite.");
        }
    }

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

    /// <summary>Recolours vertices added since <paramref name="vtxStart"/> with a scrolling horizontal
    /// gradient; callers guard on reduce-motion.</summary>
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

    /// <summary>Design-pixel thickness of every in-app scrollbar.</summary>
    internal const float ScrollbarWidth = 10f;

    internal static void PushScrollbarStyle()
    {
        var t = ThemeService.Current;
        PushScrollbarStyle(t.ScrollbarGrab, t.ScrollbarGrabHovered, t.ScrollbarGrabActive);
    }

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

    /// <summary>In-page overlay panel; returns true on a scrim tap. Draw it after the screen's content so it
    /// layers on top; <paramref name="panelH"/> persists across frames so the panel settles to its content
    /// height.</summary>
    internal static bool DrawPageOverlayPanel(string id, Vector2 winPos, Vector2 winSize, ref float panelH,
                                              float fallbackH, Action<float> drawContent)
    {
        var dismissed = false;
        var frame = ImGui.GetFrameCount();
        var now = ImGui.GetTime();
        var start = _overlayFades.TryGetValue(id, out var prev) && frame - prev.Seen <= 1 ? prev.Start : now;
        _overlayFades[id] = (start, frame);
        var raw = (float)Math.Clamp((now - start) / 0.16, 0.0, 1.0);
        var ease = AccessibilityService.ReduceMotion ? 1f : 1f - MathF.Pow(1f - raw, 3f);

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
            dl.AddRectFilled(winPos, winPos + winSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f * ease)));

            ImGui.SetCursorScreenPos(winPos);
            if (ImGui.InvisibleButton($"##scrim_{id}", winSize))
            {
                dismissed = true;
            }

            var w = Px(300f);
            var pad = Px(16f, 16f);
            var h = panelH > 0f ? panelH : fallbackH;
            var panelPos = winPos + (winSize - new Vector2(w, h)) * 0.5f;
            panelPos.Y += (1f - ease) * Px(10f);

            ImGui.SetCursorScreenPos(panelPos);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.10f, 0.13f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.32f, 0.30f, 0.38f, 0.65f));
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, pad);
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ease * ImGui.GetStyle().Alpha);
            using (var panel = ImRaii.Child($"##panel_{id}", new Vector2(w, h), true,
                       ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysUseWindowPadding))
            {
                if (panel.Success)
                {
                    drawContent(ImGui.GetContentRegionAvail().X);
                    panelH = ImGui.GetCursorPosY() + pad.Y;
                }
            }
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(2);
        }
        return dismissed;
    }

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

    internal static T ValueAt<T>(T[] arr, int idx, T fallback) =>
        idx >= 0 && idx < arr.Length ? arr[idx] : fallback;

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

    /// <summary>Packs a 24-entry hour checkbox array back into a 24-bit mask (bit 0 = 00:00 UTC).</summary>
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

    /// <summary><paramref name="flags"/> is parallel to <see cref="ProfileFields.LanguageEntries"/>; the
    /// caller owns loading the textures.</summary>
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

    private const float FlairPillPadX = 7f;

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

    /// <summary>True when the file is a cloud placeholder (OneDrive online-only and similar) whose contents
    /// aren't on disk.</summary>
    internal static bool IsUnavailableCloudFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        try
        {
            // Raw Win32 recall bits, not named in the .NET FileAttributes enum.
            const FileAttributes recallOnOpen = (FileAttributes)0x00040000;
            const FileAttributes recallOnDataAccess = (FileAttributes)0x00400000;
            var attrs = File.GetAttributes(path);
            return (attrs & (FileAttributes.Offline | recallOnOpen | recallOnDataAccess)) != 0;
        }
        catch
        {
            // Fail open; the attributes aren't supported everywhere (Wine, older Windows).
            return false;
        }
    }

    /// <summary>WIC (notably on Wine) has no WebP codec, so .webp sources are transcoded to a cached PNG;
    /// the preview keeps the source's pixel dimensions so the crop rect still maps onto the original.</summary>
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
                UiHost.Log.Warning(ex, "[SharedUiHelpers] WebP preview transcode failed.");
            }
        }
        return UiHost.TextureProvider.GetFromFile(path);
    }

    /// <summary>The crop fields cover the full processed image, the server's signal that no further crop is
    /// needed. CPU-bound; call off the UI thread.</summary>
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
