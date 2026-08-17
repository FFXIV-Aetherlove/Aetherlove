using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Notes.Ui;

/// <summary>The Notes app's own visual grammar: the search bar, the compose button, the in-page overlay
/// layer and the small text helpers both screens share.</summary>
internal static class NotesUi
{
    internal const float PadX = 16f;

    internal static string T(OsAppContext ctx, string key) => ctx.Localize(key);

    internal static string T(OsAppContext ctx, string key, params object[] args) =>
        string.Format(ctx.Culture, ctx.Localize(key), args);

    /// <summary>The list preview: newlines and runs of whitespace collapsed to single spaces.</summary>
    internal static string Flatten(string body)
    {
        if (body.Length == 0)
        {
            return string.Empty;
        }
        var sb = new System.Text.StringBuilder(body.Length);
        var space = true;
        foreach (var c in body)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!space)
                {
                    sb.Append(' ');
                    space = true;
                }
                continue;
            }
            sb.Append(c);
            space = false;
        }
        return sb.ToString().TrimEnd();
    }

    internal static string RelativeTime(OsAppContext ctx, DateTime utc)
    {
        var minutes = (DateTime.UtcNow - utc).TotalMinutes;
        if (minutes < 1.0)
        {
            return T(ctx, "os.notes_time_now");
        }
        if (minutes < 60.0)
        {
            return T(ctx, "os.notes_time_minutes", (int)minutes);
        }
        if (minutes < 60.0 * 24.0)
        {
            return T(ctx, "os.notes_time_hours", (int)(minutes / 60.0));
        }
        if (minutes < 60.0 * 24.0 * 7.0)
        {
            return T(ctx, "os.notes_time_days", (int)(minutes / (60.0 * 24.0)));
        }
        return utc.ToLocalTime().ToString("d", ctx.Culture);
    }

    internal static string LongTime(OsAppContext ctx, DateTime utc) =>
        utc.ToLocalTime().ToString("f", ctx.Culture);

    /// <summary>Cubic ease-out with a per-index delay; snaps to the final frame under reduce motion.</summary>
    internal static float StaggerEase(OsAppContext ctx, double shownAt, int index)
    {
        if (ctx.ReduceMotion)
        {
            return 1f;
        }
        var t = (ImGui.GetTime() - shownAt - index * 0.035) / 0.30;
        if (t <= 0.0)
        {
            return 0f;
        }
        if (t >= 1.0)
        {
            return 1f;
        }
        var raw = (float)t;
        return 1f - MathF.Pow(1f - raw, 3f);
    }

    /// <summary>The rounded search field: a magnifier, the input, and a clear button once something is typed.
    /// Returns true when the text changed.</summary>
    internal static bool SearchBar(OsAppContext ctx, float width, ref string query)
    {
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        var h = ctx.Px(36f);
        var tl = ImGui.GetCursorScreenPos();
        var br = tl + new Vector2(width, h);
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.07f)), h * 0.5f);

        IconDraw.AddCentered(dl, FontAwesomeIcon.Search, ctx.Px(12f),
            new Vector2(tl.X + ctx.Px(18f), tl.Y + h * 0.5f),
            ImGui.GetColorU32(UiColors.Hint));

        var hasText = query.Length > 0;
        var clearW = hasText ? ctx.Px(30f) : 0f;
        var inputX = tl.X + ctx.Px(32f);
        var inputW = MathF.Max(ctx.Px(40f), width - ctx.Px(32f) - clearW - ctx.Px(10f));

        ImGui.SetCursorScreenPos(new Vector2(inputX, tl.Y + (h - ImGui.GetFrameHeight()) * 0.5f));
        ImGui.SetNextItemWidth(inputW);
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.FrameBgHovered, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.FrameBgActive, Vector4.Zero))
        {
            var changed = ImGui.InputTextWithHint("##notesSearch", T(ctx, "os.notes_search_hint"), ref query, 200);
            ArmPasteIfIgnored(changed);
            if (hasText)
            {
                var clearCenter = new Vector2(br.X - ctx.Px(18f), tl.Y + h * 0.5f);
                ImGui.SetCursorScreenPos(clearCenter - new Vector2(ctx.Px(13f), ctx.Px(13f)));
                if (ImGui.InvisibleButton("##notesSearchClear", new Vector2(ctx.Px(26f), ctx.Px(26f))))
                {
                    query = string.Empty;
                    changed = true;
                }
                HandOnHover();
                var tint = ImGui.IsItemHovered() ? t.AccentLight : UiColors.Hint;
                IconDraw.AddCentered(dl, FontAwesomeIcon.TimesCircle, ctx.Px(13f), clearCenter,
                    ImGui.GetColorU32(tint));
            }
            ImGui.SetCursorScreenPos(new Vector2(tl.X, br.Y));
            return changed;
        }
    }

    /// <summary>The round compose button, floating over the bottom-right of the page in its own layer so it
    /// sits above the scrolling list.</summary>
    internal static bool ComposeButton(OsAppContext ctx, string tooltip)
    {
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var d = ctx.Px(52f);
        var pos = winPos + new Vector2(winSize.X - d - ctx.Px(PadX), winSize.Y - d - ctx.Px(18f));

        ImGui.SetCursorScreenPos(pos);
        using var pad = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var layer = ImRaii.Child("##notesCompose", new Vector2(d, d), false,
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoNav);
        if (!layer.Success)
        {
            return false;
        }

        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton("##notesComposeBtn", new Vector2(d, d));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
            ImGui.SetTooltip(tooltip);
        }

        var center = pos + new Vector2(d, d) * 0.5f;
        var r = d * 0.5f;
        dl.AddCircleFilled(center + new Vector2(0f, ctx.Px(2f)), r,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.35f)), 48);
        dl.AddCircleFilled(center, r, ImGui.ColorConvertFloat4ToU32(
            hovered ? NotesApp.TileTopColor : Vector4.Lerp(NotesApp.TileTopColor, NotesApp.TileBottomColor, 0.35f)), 48);
        dl.AddCircle(center, r, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, hovered ? 0.35f : 0.18f)),
            48, ctx.Px(1.2f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Plus, ctx.Px(19f), center,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.07f, 0.02f, 1f)));
        return clicked;
    }

    /// <summary>An in-page overlay: its own layer child so the scrim lands above the screen's body child, the
    /// panel's controls submitted before the full-area scrim. Returns true when the user dismissed it.</summary>
    internal static bool Overlay(OsAppContext ctx, string id, float panelW, ref float panelH, float fallbackH,
                                 Action<float> drawPanel)
    {
        var origin = ImGui.GetWindowPos();
        var avail = ImGui.GetWindowSize();
        ImGui.SetCursorScreenPos(origin);
        using var pad = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var layer = ImRaii.Child($"##{id}Layer", avail, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        if (!layer.Success)
        {
            return false;
        }

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + avail, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.72f)));

        var w = MathF.Min(ctx.Px(panelW), avail.X - ctx.Px(28f) * 2f);
        var h = panelH > 0f ? panelH : ctx.Px(fallbackH);
        var panelTL = origin + new Vector2((avail.X - w) * 0.5f, (avail.Y - h) * 0.5f);
        var panelBR = panelTL + new Vector2(w, h);
        dl.AddRectFilled(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(new Vector4(0.11f, 0.10f, 0.12f, 1f)),
            ctx.Px(16f));
        dl.AddRect(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.10f)),
            ctx.Px(16f), ImDrawFlags.RoundCornersAll, ctx.Px(1f));

        var inner = ctx.Px(18f);
        ImGui.SetCursorScreenPos(panelTL + new Vector2(inner, inner));
        ImGui.PushItemWidth(w - inner * 2f);
        drawPanel(w - inner * 2f);
        ImGui.PopItemWidth();
        panelH = ImGui.GetCursorScreenPos().Y - panelTL.Y + inner;

        ImGui.SetCursorScreenPos(origin);
        var scrim = ImGui.InvisibleButton($"##{id}Scrim", avail);
        var mouse = ImGui.GetMousePos();
        var outside = mouse.X < panelTL.X || mouse.X > panelBR.X || mouse.Y < panelTL.Y || mouse.Y > panelBR.Y;
        return scrim && outside;
    }

    /// <summary>The overlay panel's title line, matching the modal header used elsewhere on the phone.</summary>
    internal static void OverlayTitle(OsAppContext ctx, string title, Vector4 accent)
    {
        using (ctx.HeadingFont?.Push())
        {
            ImGui.TextColored(accent, title);
        }
        ImGui.Dummy(new Vector2(0f, ctx.Px(4f)));
    }

    internal static void Separator(OsAppContext ctx, Vector2 from, float width, float alpha)
    {
        ImGui.GetWindowDrawList().AddRectFilled(from, from + new Vector2(width, MathF.Max(1f, ctx.Px(1f))),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.07f * alpha)));
    }

    internal static string Untitled(OsAppContext ctx, Note note)
    {
        if (note.Title.Trim().Length > 0)
        {
            return note.Title;
        }
        var flat = Flatten(note.Body);
        if (flat.Length > 0)
        {
            return flat;
        }
        return T(ctx, "os.notes_untitled");
    }
}
