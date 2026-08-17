using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using AetherOS.Apps.Notes.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace AetherOS.Apps.Notes.Screens;

/// <summary>The library: a search field, the pinned notes, then everything else newest-first.</summary>
internal sealed class ListScreen
{
    private const float RowHeight = 76f;
    private const float RowGap = 9f;

    private readonly NotesStore _store;
    private readonly Action<Note> _open;
    private readonly Action _compose;
    private string _query = string.Empty;
    private double _shownAt;

    internal ListScreen(NotesStore store, Action<Note> open, Action compose)
    {
        _store = store;
        _open = open;
        _compose = compose;
    }

    internal void OnShow()
    {
        _shownAt = ImGui.GetTime();
    }

    internal void Draw(OsAppContext ctx)
    {
        var winW = ImGui.GetWindowSize().X;
        var listW = winW - ctx.Px(NotesUi.PadX) * 2f;

        ImGui.Dummy(new Vector2(0f, ctx.Px(2f)));
        ImGui.SetCursorPosX(ctx.Px(NotesUi.PadX));
        if (NotesUi.SearchBar(ctx, listW, ref _query))
        {
            _shownAt = ImGui.GetTime();
        }
        ImGui.Dummy(new Vector2(0f, ctx.Px(14f)));

        var results = _store.Search(_query);
        if (results.Count == 0)
        {
            DrawEmpty(ctx, listW);
            ImGui.Dummy(new Vector2(0f, ctx.Px(80f)));
            return;
        }

        var index = 0;
        var pinnedCount = 0;
        foreach (var note in results)
        {
            if (note.Pinned)
            {
                pinnedCount++;
            }
        }

        if (pinnedCount > 0)
        {
            DrawSectionLabel(ctx, listW, NotesUi.T(ctx, "os.notes_section_pinned"), FontAwesomeIcon.Thumbtack);
            DrawRows(ctx, listW, results, 0, pinnedCount, ref index);
        }
        if (pinnedCount < results.Count)
        {
            if (pinnedCount > 0)
            {
                ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
                DrawSectionLabel(ctx, listW, NotesUi.T(ctx, "os.notes_section_others"), FontAwesomeIcon.LayerGroup);
            }
            DrawRows(ctx, listW, results, pinnedCount, results.Count, ref index);
        }

        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
        var count = NotesUi.T(ctx, results.Count == 1 ? "os.notes_count_one" : "os.notes_count", results.Count);
        var countSz = ImGui.CalcTextSize(count);
        ImGui.SetCursorPosX(MathF.Max(ctx.Px(NotesUi.PadX), (winW - countSz.X) * 0.5f));
        ImGui.TextColored(UiColors.Hint, count);
        ImGui.Dummy(new Vector2(0f, ctx.Px(74f)));
    }

    /// <summary>The floating compose button; drawn after the body child so it layers over the list.</summary>
    internal void DrawOverlays(OsAppContext ctx)
    {
        if (NotesUi.ComposeButton(ctx, NotesUi.T(ctx, "os.notes_new")))
        {
            _compose();
        }
    }

    internal void ClearSearch()
    {
        _query = string.Empty;
    }

    private void DrawRows(OsAppContext ctx, float listW, List<Note> notes, int from, int to, ref int index)
    {
        for (var i = from; i < to; i++)
        {
            DrawRow(ctx, listW, notes[i], index);
            index++;
        }
    }

    private static void DrawSectionLabel(OsAppContext ctx, float listW, string label, FontAwesomeIcon icon)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorPosX(ctx.Px(NotesUi.PadX));
        var tl = ImGui.GetCursorScreenPos();
        var textSz = ImGui.CalcTextSize(label);
        IconDraw.AddCentered(dl, icon, ctx.Px(9f),
            new Vector2(tl.X + ctx.Px(5f), tl.Y + textSz.Y * 0.5f), ImGui.GetColorU32(UiColors.Hint));
        dl.AddText(new Vector2(tl.X + ctx.Px(15f), tl.Y), ImGui.GetColorU32(UiColors.Subtle), label);
        NotesUi.Separator(ctx, new Vector2(tl.X + ctx.Px(15f) + textSz.X + ctx.Px(10f), tl.Y + textSz.Y * 0.5f),
            listW - ctx.Px(25f) - textSz.X, 1f);
        ImGui.Dummy(new Vector2(0f, textSz.Y + ctx.Px(9f)));
    }

    private void DrawRow(OsAppContext ctx, float listW, Note note, int index)
    {
        var ease = NotesUi.StaggerEase(ctx, _shownAt, index);
        var dl = ImGui.GetWindowDrawList();
        var h = ctx.Px(RowHeight);
        ImGui.SetCursorPosX(ctx.Px(NotesUi.PadX));
        var slot = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton($"##noteRow{note.Id:N}", new Vector2(listW, h));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            HandOnHover();
        }

        var tl = slot + new Vector2(0f, (1f - ease) * ctx.Px(14f));
        var br = tl + new Vector2(listW, h);
        var accent = NoteColors.Accent(note.ColorIndex);
        var rounding = ctx.Px(14f);

        dl.AddRectFilled(tl + new Vector2(0f, ctx.Px(2f)), br + new Vector2(0f, ctx.Px(2f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.22f * ease)), rounding);
        var surface = NoteColors.Surface(note.ColorIndex, (hovered ? 1f : 0.92f) * ease);
        dl.AddRectFilled(tl, br, ImGui.ColorConvertFloat4ToU32(surface), rounding);
        dl.AddRect(tl, br, ImGui.ColorConvertFloat4ToU32(accent with { W = (hovered ? 0.42f : 0.16f) * ease }),
            rounding, ImDrawFlags.RoundCornersAll, ctx.Px(1f));
        dl.AddRectFilled(tl + new Vector2(ctx.Px(1f), ctx.Px(12f)),
            tl + new Vector2(ctx.Px(4.5f), h - ctx.Px(12f)),
            ImGui.ColorConvertFloat4ToU32(accent with { W = 0.95f * ease }), ctx.Px(2f));

        var textX = tl.X + ctx.Px(18f);
        var rightLimit = br.X - ctx.Px(14f) - (note.Pinned ? ctx.Px(18f) : 0f);
        var title = NotesUi.Untitled(ctx, note);
        var titleCol = ImGui.ColorConvertFloat4ToU32(UiColors.Body with { W = ease });
        dl.AddText(new Vector2(textX, tl.Y + ctx.Px(14f)), titleCol,
            TruncateToWidth(title, rightLimit - textX));

        var stamp = NotesUi.RelativeTime(ctx, note.UpdatedUtc);
        var stampSz = ImGui.CalcTextSize(stamp);
        var metaY = tl.Y + ctx.Px(38f);
        dl.AddText(new Vector2(textX, metaY),
            ImGui.ColorConvertFloat4ToU32(accent with { W = 0.85f * ease }), stamp);

        var preview = NotesUi.Flatten(note.Body);
        if (preview.Length > 0)
        {
            var previewX = textX + stampSz.X + ctx.Px(8f);
            dl.AddText(new Vector2(previewX, metaY),
                ImGui.ColorConvertFloat4ToU32(UiColors.Hint with { W = 0.9f * ease }),
                TruncateToWidth(preview, MathF.Max(ctx.Px(20f), br.X - ctx.Px(14f) - previewX)));
        }

        if (note.Pinned)
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.Thumbtack, ctx.Px(11f),
                new Vector2(br.X - ctx.Px(18f), tl.Y + ctx.Px(19f)),
                ImGui.ColorConvertFloat4ToU32(accent with { W = 0.95f * ease }));
        }

        ImGui.Dummy(new Vector2(0f, ctx.Px(RowGap)));
        if (clicked)
        {
            _open(note);
        }
    }

    private void DrawEmpty(OsAppContext ctx, float listW)
    {
        var searching = _query.Trim().Length > 0;
        var t = ThemeService.Current;
        var dl = ImGui.GetWindowDrawList();

        ImGui.Dummy(new Vector2(0f, ctx.Px(46f)));
        var center = new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowSize().X * 0.5f,
            ImGui.GetCursorScreenPos().Y + ctx.Px(34f));
        dl.AddCircleFilled(center, ctx.Px(34f),
            ImGui.ColorConvertFloat4ToU32(NotesApp.TileTopColor with { W = 0.12f }), 48);
        IconDraw.AddCentered(dl, searching ? FontAwesomeIcon.SearchMinus : FontAwesomeIcon.FeatherAlt,
            ctx.Px(28f), center, ImGui.ColorConvertFloat4ToU32(NotesApp.TileTopColor with { W = 0.9f }));
        ImGui.Dummy(new Vector2(0f, ctx.Px(80f)));

        var title = NotesUi.T(ctx, searching ? "os.notes_empty_search_title" : "os.notes_empty_title");
        using (ctx.HeadingFont?.Push())
        {
            var sz = ImGui.CalcTextSize(title);
            ImGui.SetCursorPosX(MathF.Max(ctx.Px(NotesUi.PadX), (ImGui.GetWindowSize().X - sz.X) * 0.5f));
            ImGui.TextColored(t.AccentLight, title);
        }
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));

        var body = NotesUi.T(ctx, searching ? "os.notes_empty_search_body" : "os.notes_empty_body");
        OnboardingUi.DrawCenteredParagraph(body, listW - ctx.Px(20f), UiColors.Hint);
    }
}
