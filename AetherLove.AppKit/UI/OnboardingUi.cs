using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherLove.UI;

/// <summary>Shared visual kit for the modern, mobile-style onboarding flows (the AetherOS shell setup and the
/// AetherLove dating profile), so both look and feel identical. A slim segmented progress bar, a gradient icon
/// "hero" (custom PNG art via <see cref="CustomIcons"/> with a FontAwesome glyph fallback), a full-width primary
/// button, and a rounded info callout. All draw against <see cref="ThemeService.Current"/>.</summary>
internal static class OnboardingUi
{
    /// <summary>A slim segmented progress bar at the top of the window, plus a back chevron in the corner. Returns
    /// true on the frame the chevron is clicked. Call at window scope (before the content child).</summary>
    public static bool DrawProgress(int step, int totalSteps, bool canGoBack)
    {
        var t = ThemeService.Current;
        var wPos = ImGui.GetWindowPos();
        var wSize = ImGui.GetWindowSize();
        var dl = ImGui.GetWindowDrawList();

        // When back is available its chevron takes the left margin, so the bar starts to the right of it.
        var backClicked = false;
        var barLeft = Px(20f);
        if (canGoBack)
        {
            barLeft = Px(46f);
            var bTL = wPos + new Vector2(Px(4f), Px(6f));
            ImGui.SetCursorScreenPos(bTL);
            backClicked = ImGui.InvisibleButton("##onbBack", Px(34f, 28f));
            var hov = ImGui.IsItemHovered();
            IconDraw.AddCentered(dl, FontAwesomeIcon.ChevronLeft, Px(15f),
                wPos + new Vector2(Px(18f), Px(20f)), hov ? 0xFFFFFFFFu : 0xA0FFFFFFu);
        }

        var gap = Px(5f);
        var barW = wSize.X - barLeft - Px(20f);
        var segW = (barW - gap * (totalSteps - 1)) / totalSteps;
        var segH = Px(4f);
        var y = wPos.Y + Px(18f);
        for (var i = 0; i < totalSteps; i++)
        {
            var x = wPos.X + barLeft + i * (segW + gap);
            var col = i <= step
                ? t.AccentU32
                : ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.13f));
            dl.AddRectFilled(new Vector2(x, y), new Vector2(x + segW, y + segH), col, segH * 0.5f);
        }
        return backClicked;
    }

    /// <summary>The circular hero badge: a soft accent glow behind the custom PNG art at
    /// <c>Media/icons/&lt;iconKey&gt;.png</c>. Without that art nothing is drawn, unless the caller opts into a
    /// FontAwesome <paramref name="fallback"/> disc; the badge's space is reserved either way.</summary>
    public static void DrawHeroBadge(ImDrawListPtr dl, Vector2 center, float badgeR, string iconKey,
        FontAwesomeIcon? fallback = null)
    {
        var t = ThemeService.Current;
        var glow = ImGui.ColorConvertFloat4ToU32(t.Accent with { W = 0.13f });
        var custom = CustomIcons.Get(iconKey)?.GetWrapOrDefault();
        if (custom != null)
        {
            dl.AddCircleFilled(center, Px(badgeR + 9f), glow, 48);
            // Custom art is authored full-bleed to its frame; inset it so it floats inside the glow ring
            // rather than bursting past it.
            var half = Px(badgeR * 0.72f);
            dl.AddImage(custom.Handle, center - new Vector2(half, half), center + new Vector2(half, half));
            return;
        }
        if (fallback is not { } glyph)
        {
            return;
        }
        dl.AddCircleFilled(center, Px(badgeR + 9f), glow, 48);
        dl.AddCircleFilled(center, Px(badgeR), t.AccentU32, 48);
        dl.AddCircleFilled(center - new Vector2(0f, Px(badgeR * 0.34f)), Px(badgeR * 0.58f),
            ImGui.ColorConvertFloat4ToU32(t.AccentLight with { W = 0.22f }), 32);
        IconDraw.AddCentered(dl, glyph, Px(badgeR * 0.86f), center, 0xFFFFFFFFu);
    }

    /// <summary>The step "hero" with art only: no glyph stands in when the PNG is missing.</summary>
    public static void DrawHero(string iconKey, string title, string? subtitle, float badgeR = 32f)
        => DrawHero(iconKey, null, title, subtitle, badgeR);

    /// <summary>The step "hero": a badge, an H1 title, and an optional centred subtitle. Call at the top of a
    /// step's content.</summary>
    public static void DrawHero(string iconKey, FontAwesomeIcon? fallback, string title, string? subtitle,
        float badgeR = 32f)
    {
        var winW = ImGui.GetWindowSize().X;
        var dl = ImGui.GetWindowDrawList();

        ImGui.Dummy(new Vector2(0f, Px(subtitle != null ? 16f : 10f)));
        var cx = ImGui.GetWindowPos().X + winW * 0.5f;
        var cy = ImGui.GetCursorScreenPos().Y + Px(badgeR);
        DrawHeroBadge(dl, new Vector2(cx, cy), badgeR, iconKey, fallback);
        ImGui.Dummy(new Vector2(0f, Px(badgeR * 2f + 14f)));

        using (UiFonts.H1?.Push())
        {
            ImGui.SetCursorPosX(MathF.Max(Px(12f), (winW - ImGui.CalcTextSize(title).X) * 0.5f));
            ImGui.TextUnformatted(title);
        }
        if (subtitle != null)
        {
            ImGui.Dummy(new Vector2(0f, Px(7f)));
            DrawCenteredParagraph(subtitle, winW - Px(48f), new Vector4(0.72f, 0.72f, 0.75f, 1f));
        }
        ImGui.Dummy(new Vector2(0f, Px(16f)));
    }

    /// <summary>Word-wraps <paramref name="text"/> to <paramref name="maxWidth"/> and centres every line, at the
    /// phone-scaled body size (the default ImGui font ignores the phone scale, so it reads tiny when the phone is
    /// scaled up).</summary>
    public static void DrawCenteredParagraph(string text, float maxWidth, Vector4 color,
        Dalamud.Interface.ManagedFontAtlas.IFontHandle? font = null)
    {
        using ((font ?? UiFonts.H3)?.Push())
        {
            var winW = ImGui.GetWindowSize().X;
            var words = text.Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>();
            var line = string.Empty;
            foreach (var w in words)
            {
                var candidate = line.Length == 0 ? w : line + " " + w;
                if (line.Length > 0 && ImGui.CalcTextSize(candidate).X > maxWidth)
                {
                    lines.Add(line);
                    line = w;
                }
                else
                {
                    line = candidate;
                }
            }
            if (line.Length > 0)
            {
                lines.Add(line);
            }
            foreach (var l in lines)
            {
                ImGui.SetCursorPosX(MathF.Max(Px(12f), (winW - ImGui.CalcTextSize(l).X) * 0.5f));
                ImGui.TextColored(color, l);
            }
        }
    }

    /// <summary>One line of a feature list: a soft accent chip with an icon, and wrapped body text beside it.
    /// The onboarding house style for "here's what this does" bullets.</summary>
    public static void DrawFeatureRow(FontAwesomeIcon icon, string text)
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var dl = ImGui.GetWindowDrawList();
        var margin = Px(30f);
        var chipR = Px(15f);

        ImGui.Dummy(new Vector2(0f, Px(5f)));
        ImGui.SetCursorPosX(margin);
        var sp = ImGui.GetCursorScreenPos();
        var chipC = new Vector2(sp.X + chipR, sp.Y + chipR);
        dl.AddCircleFilled(chipC, chipR, ImGui.ColorConvertFloat4ToU32(t.Accent with { W = 0.16f }));
        IconDraw.AddCentered(dl, icon, Px(14f), chipC, t.AccentU32);

        ImGui.Dummy(new Vector2(chipR * 2f, chipR * 2f));
        ImGui.SameLine(0f, Px(12f));
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + chipR - ImGui.GetTextLineHeight() * 0.5f);
        ImGui.PushTextWrapPos(winW - margin);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    /// <summary>A full-width, rounded, accent-filled primary action button. Returns true when clicked and
    /// enabled.</summary>
    public static bool DrawPrimaryButton(string label, bool enabled)
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;
        var margin = Px(20f);
        ImGui.SetCursorPosX(margin);
        if (!enabled)
        {
            ImGui.BeginDisabled();
        }
        ImGui.PushStyleColor(ImGuiCol.Button, t.Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.AccentLight);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.AccentDark);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(13f));
        bool clicked;
        using (UiFonts.H3?.Push())
        {
            clicked = ImGui.Button(label, new Vector2(winW - margin * 2f, Px(46f)));
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
        if (enabled)
        {
            SharedUiHelpers.HandOnHover();
        }
        if (!enabled)
        {
            ImGui.EndDisabled();
        }
        return clicked && enabled;
    }

    /// <summary>A framed box presenting a value the user is meant to read and keep (a passphrase, a friend
    /// code), at H2 and centred, with a copy button underneath. Returns true on the frame copy is clicked, so
    /// the caller supplies its own clipboard route.</summary>
    public static bool DrawSecretBox(string id, string value, string copyLabel)
    {
        var t = ThemeService.Current;
        var winW = ImGui.GetWindowSize().X;

        // Measured at H2 so a long value wraps to a taller box rather than overflowing it.
        var boxW = winW - Px(40f);
        float txtH;
        using (UiFonts.H2?.Push())
        {
            txtH = ImGui.CalcTextSize(value, false, boxW - Px(28f)).Y;
        }
        var boxH = MathF.Max(Px(56f), txtH + Px(28f));

        ImGui.SetCursorPosX(Px(20f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(1f, 1f, 1f, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.Border, t.Accent with { W = 0.55f });
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Px(12f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, Px(1.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Px(14f), Px(14f)));
        if (ImGui.BeginChild(id, new Vector2(boxW, boxH), true))
        {
            using (UiFonts.H2?.Push())
            {
                var innerW = ImGui.GetContentRegionAvail().X;
                ImGui.PushTextWrapPos(innerW);
                var ts = ImGui.CalcTextSize(value, false, innerW);
                ImGui.SetCursorPosX(MathF.Max(0f, (innerW - ts.X) * 0.5f));
                ImGui.TextUnformatted(value);
                ImGui.PopTextWrapPos();
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);

        ImGui.Dummy(new Vector2(0f, Px(8f)));
        var copyW = Px(120f);
        ImGui.SetCursorPosX((winW - copyW) * 0.5f);
        PushThemeButton(t);
        var copied = ImGui.Button($"{copyLabel}{id}Copy", new Vector2(copyW, Px(30f)));
        PopThemeButton();
        return copied;
    }

    /// <summary>A rounded, tinted info box with a leading icon and wrapped text. Advances the cursor below the
    /// box.</summary>
    public static void DrawInfoCallout(string text, Vector4 color, FontAwesomeIcon icon)
    {
        var winW = ImGui.GetWindowSize().X;
        var dl = ImGui.GetWindowDrawList();
        var margin = Px(16f);
        var pad = Px(11f);
        var iconW = Px(24f);
        var textW = winW - margin * 2f - pad * 2f - iconW;
        // The body is drawn at the phone-scaled H3 size, so measure the height at that size too.
        Vector2 textSz;
        using (UiFonts.H3?.Push())
        {
            textSz = ImGui.CalcTextSize(text, false, textW);
        }
        var tl = ImGui.GetCursorScreenPos() + new Vector2(margin, 0f);
        var h = MathF.Max(Px(40f), textSz.Y + pad * 2f);
        var br = tl + new Vector2(winW - margin * 2f, h);
        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(color with { W = 0.12f }), Px(10f));
        dl.AddRect(tl, br, ImGui.ColorConvertFloat4ToU32(color with { W = 0.42f }), Px(10f));
        IconDraw.AddCentered(dl, icon, Px(17f), new Vector2(tl.X + pad + iconW * 0.5f, (tl.Y + br.Y) * 0.5f),
            ImGui.ColorConvertFloat4ToU32(color));
        ImGui.SetCursorScreenPos(new Vector2(tl.X + pad + iconW, tl.Y + pad));
        using (UiFonts.H3?.Push())
        {
            // PushTextWrapPos takes a window-local X, not a screen X, or the text never wraps.
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textW);
            ImGui.TextColored(color with { W = 0.95f }, text);
            ImGui.PopTextWrapPos();
        }
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X, br.Y));
    }
}
