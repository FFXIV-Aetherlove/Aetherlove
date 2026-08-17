using System;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using AetherLove.Widgets;
using AetherOS.Apps.Notes.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Notes.Screens;

/// <summary>The single-note editor: a title line, a soft-wrapping body, and the overflow menu that owns copy,
/// paste, duplicate, pin, colour and delete.</summary>
internal sealed class EditorScreen
{
    private const int TitleMaxLength = 120;
    private const int BodyMaxLength = 20000;
    private const string MenuPopupId = "##notesEditorMenu";

    private readonly NotesStore _store;
    private readonly Action _back;
    private readonly Action<Note> _openDuplicate;
    private readonly SoftWrapInputField _bodyField = new();

    private Note? _note;
    private string _title = string.Empty;
    private string _body = string.Empty;
    private bool _confirmDelete;
    private bool _colorPicker;
    private float _confirmHeight;
    private float _colorHeight;

    internal EditorScreen(NotesStore store, Action back, Action<Note> openDuplicate)
    {
        _store = store;
        _back = back;
        _openDuplicate = openDuplicate;
    }

    internal void Open(Note note)
    {
        _note = note;
        _title = note.Title;
        _body = note.Body;
        _bodyField.Reset(_body);
        _confirmDelete = false;
        _colorPicker = false;
        _confirmHeight = 0f;
        _colorHeight = 0f;
    }

    /// <summary>Writes the buffers back, drops a note that was never typed in, and forces the pending save.</summary>
    internal void Commit()
    {
        if (_note is null)
        {
            return;
        }
        Sync();
        _store.DiscardIfBlank(_note);
        _store.Flush();
        _note = null;
    }

    private void Sync()
    {
        if (_note is null)
        {
            return;
        }
        var body = _bodyField.Value(_body);
        if (_note.Title == _title && _note.Body == body)
        {
            return;
        }
        _note.Title = _title;
        _note.Body = body;
        _store.Touch(_note);
    }

    internal void Draw(OsAppContext ctx)
    {
        if (_note is null)
        {
            _back();
            return;
        }

        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var accent = NoteColors.Accent(_note.ColorIndex);
        var topH = ctx.Px(48f);

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(winPos, winPos + new Vector2(winSize.X, topH * 2.4f),
            ImGui.ColorConvertFloat4ToU32(accent with { W = 0.07f }));

        if (DrawFloatingBackPill(winPos + new Vector2(ctx.Px(NotesUi.PadX), ctx.Px(11f)),
                NotesUi.T(ctx, "os.notes_back"), FontAwesomeIcon.StickyNote))
        {
            _back();
            return;
        }
        DrawMenu(ctx, winPos.Y + ctx.Px(11f) + ctx.Px(15f));

        ImGui.SetCursorScreenPos(new Vector2(winPos.X, winPos.Y + topH));
        ImGui.Dummy(new Vector2(0f, ctx.Px(4f)));

        var fieldW = winSize.X - ctx.Px(NotesUi.PadX) * 2f;
        DrawTitleField(ctx, fieldW);
        DrawMetaRow(ctx, fieldW, accent);
        DrawBodyField(ctx, fieldW, winPos, winSize);

        Sync();
        DrawOverlays(ctx);
    }

    private void DrawTitleField(OsAppContext ctx, float fieldW)
    {
        ImGui.SetCursorPosX(ctx.Px(NotesUi.PadX));
        ImGui.SetNextItemWidth(fieldW);
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.FrameBgHovered, new Vector4(1f, 1f, 1f, 0.03f)))
        using (ImRaii.PushColor(ImGuiCol.FrameBgActive, new Vector4(1f, 1f, 1f, 0.05f)))
        using (ctx.HeadingFont?.Push())
        {
            ImGui.InputTextWithHint("##notesTitle", NotesUi.T(ctx, "os.notes_title_hint"), ref _title,
                TitleMaxLength);
        }
    }

    private void DrawMetaRow(OsAppContext ctx, float fieldW, Vector4 accent)
    {
        if (_note is null)
        {
            return;
        }
        ImGui.SetCursorPosX(ctx.Px(NotesUi.PadX));
        var dl = ImGui.GetWindowDrawList();
        var tl = ImGui.GetCursorScreenPos();
        var lineY = tl.Y + ImGui.GetTextLineHeight() * 0.5f;
        dl.AddCircleFilled(new Vector2(tl.X + ctx.Px(4f), lineY), ctx.Px(4f),
            ImGui.ColorConvertFloat4ToU32(accent), 24);
        var stamp = NotesUi.T(ctx, "os.notes_edited", NotesUi.LongTime(ctx, _note.UpdatedUtc));
        dl.AddText(new Vector2(tl.X + ctx.Px(15f), tl.Y), ImGui.GetColorU32(UiColors.Hint),
            TruncateToWidth(stamp, fieldW - ctx.Px(60f)));
        if (_note.Pinned)
        {
            IconDraw.AddCentered(dl, FontAwesomeIcon.Thumbtack, ctx.Px(11f),
                new Vector2(tl.X + fieldW - ctx.Px(8f), lineY), ImGui.ColorConvertFloat4ToU32(accent));
        }
        ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight() + ctx.Px(10f)));

        NotesUi.Separator(ctx, ImGui.GetCursorScreenPos() with { X = tl.X }, fieldW, 1f);
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
    }

    private void DrawBodyField(OsAppContext ctx, float fieldW, Vector2 winPos, Vector2 winSize)
    {
        var lineH = ImGui.GetTextLineHeight();
        var lines = 1;
        foreach (var c in _body)
        {
            if (c == '\n')
            {
                lines++;
            }
        }
        var padding = ImGui.GetStyle().FramePadding.Y * 2f;
        var bottom = winPos.Y + winSize.Y - ctx.Px(14f);
        var room = MathF.Max(ctx.Px(120f), bottom - ImGui.GetCursorScreenPos().Y);
        var wanted = (lines + 1) * lineH + padding;
        var height = MathF.Min(room, MathF.Max(ctx.Px(150f), wanted));

        ImGui.SetCursorPosX(ctx.Px(NotesUi.PadX));
        using (ImRaii.PushColor(ImGuiCol.FrameBg, Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.FrameBgHovered, new Vector4(1f, 1f, 1f, 0.02f)))
        using (ImRaii.PushColor(ImGuiCol.FrameBgActive, new Vector4(1f, 1f, 1f, 0.04f)))
        using (ImRaii.PushColor(ImGuiCol.Text, UiColors.BioText))
        {
            _bodyField.Draw("##notesBodyField", ref _body, BodyMaxLength, new Vector2(fieldW, height));
        }
        if (_body.Length == 0 && !ImGui.IsItemActive())
        {
            var hint = NotesUi.T(ctx, "os.notes_body_hint");
            var at = ImGui.GetItemRectMin() + ImGui.GetStyle().FramePadding;
            ImGui.GetWindowDrawList().AddText(at, ImGui.GetColorU32(UiColors.BioPlaceholder), hint);
        }
    }

    private void DrawMenu(OsAppContext ctx, float centerY)
    {
        var menuTL = AppHeader.DrawMenuButton(ImGui.GetWindowSize().X, NotesUi.PadX, MenuPopupId, centerY: centerY);
        var open = AppHeader.BeginMenuPopup(menuTL, MenuPopupId);
        if (open)
        {
            var copy = NotesUi.T(ctx, "os.notes_menu_copy");
            var paste = NotesUi.T(ctx, "os.notes_menu_paste");
            var duplicate = NotesUi.T(ctx, "os.notes_menu_duplicate");
            var pin = NotesUi.T(ctx, _note is { Pinned: true } ? "os.notes_menu_unpin" : "os.notes_menu_pin");
            var colour = NotesUi.T(ctx, "os.notes_menu_colour");
            var delete = NotesUi.T(ctx, "os.notes_menu_delete");
            var w = AppHeader.MenuWidth(copy, paste, duplicate, pin, colour, delete);
            var rowH = AppHeader.MenuRowHeight();

            if (AppHeader.MenuRow(FontAwesomeIcon.Copy, copy, w, rowH))
            {
                CopyNote(ctx);
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Paste, paste, w, rowH))
            {
                PasteIntoNote();
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Clone, duplicate, w, rowH))
            {
                Duplicate();
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Thumbtack, pin, w, rowH))
            {
                TogglePin();
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.Palette, colour, w, rowH))
            {
                _colorPicker = true;
                _colorHeight = 0f;
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.TrashAlt, delete, w, rowH,
                    ImGui.ColorConvertFloat4ToU32(UiColors.Danger)))
            {
                _confirmDelete = true;
                _confirmHeight = 0f;
                ImGui.CloseCurrentPopup();
            }
        }
        AppHeader.EndMenuPopup(open);
    }

    private void CopyNote(OsAppContext ctx)
    {
        if (_note is null)
        {
            return;
        }
        Sync();
        var text = _note.Title.Length > 0 && _note.Body.Length > 0
            ? _note.Title + "\n\n" + _note.Body
            : _note.Title + _note.Body;
        ctx.Capabilities.System.CopyToClipboard(text);
    }

    /// <summary>Ctrl+V does not reliably reach an ImGui input in game, so the menu writes the clipboard into
    /// the buffer itself rather than replaying the keystroke.</summary>
    private void PasteIntoNote()
    {
        if (_note is null)
        {
            return;
        }
        var clip = ImGui.GetClipboardText();
        if (string.IsNullOrEmpty(clip))
        {
            return;
        }
        var text = clip.Replace("\r\n", "\n").Replace('\r', '\n');
        var current = _bodyField.Value(_body);
        var merged = current.Length == 0 ? text : current.TrimEnd() + "\n" + text;
        if (merged.Length > BodyMaxLength)
        {
            merged = merged[..BodyMaxLength];
        }
        _body = merged;
        _bodyField.Reset(_body);
        _note.Body = merged;
        _store.Touch(_note);
    }

    private void Duplicate()
    {
        if (_note is null)
        {
            return;
        }
        Sync();
        var copy = _store.Duplicate(_note);
        _store.Flush();
        _openDuplicate(copy);
    }

    private void TogglePin()
    {
        if (_note is null)
        {
            return;
        }
        _note.Pinned = !_note.Pinned;
        _store.MarkDirty();
    }

    private void DrawOverlays(OsAppContext ctx)
    {
        if (_colorPicker)
        {
            if (NotesUi.Overlay(ctx, "notesColour", 268f, ref _colorHeight, 190f, w => DrawColorPanel(ctx, w)))
            {
                _colorPicker = false;
            }
        }
        if (_confirmDelete)
        {
            if (NotesUi.Overlay(ctx, "notesDelete", 268f, ref _confirmHeight, 176f, w => DrawDeletePanel(ctx, w)))
            {
                _confirmDelete = false;
            }
        }
    }

    private void DrawColorPanel(OsAppContext ctx, float width)
    {
        if (_note is null)
        {
            return;
        }
        NotesUi.OverlayTitle(ctx, NotesUi.T(ctx, "os.notes_colour_title"), ThemeService.Current.AccentLight);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(UiColors.Hint, NotesUi.T(ctx, "os.notes_colour_body"));
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, ctx.Px(12f)));

        var dl = ImGui.GetWindowDrawList();
        var cell = ctx.Px(34f);
        var perRow = Math.Max(1, (int)(width / cell));
        var rowStart = ImGui.GetCursorScreenPos();
        for (var i = 0; i < NoteColors.Count; i++)
        {
            var col = i % perRow;
            var row = i / perRow;
            var pos = rowStart + new Vector2(col * cell, row * cell);
            ImGui.SetCursorScreenPos(pos);
            var picked = ImGui.InvisibleButton($"##notesColour{i}", new Vector2(cell - ctx.Px(6f), cell - ctx.Px(6f)));
            var hovered = ImGui.IsItemHovered();
            if (hovered)
            {
                HandOnHover();
            }
            var center = pos + new Vector2(cell - ctx.Px(6f), cell - ctx.Px(6f)) * 0.5f;
            var accent = NoteColors.Accent(i);
            dl.AddCircleFilled(center, ctx.Px(12f), ImGui.ColorConvertFloat4ToU32(accent), 32);
            if (_note.ColorIndex == i)
            {
                dl.AddCircle(center, ctx.Px(15f), ImGui.GetColorU32(UiColors.Body), 32, ctx.Px(1.8f));
            }
            else if (hovered)
            {
                dl.AddCircle(center, ctx.Px(15f), ImGui.ColorConvertFloat4ToU32(accent with { W = 0.5f }), 32,
                    ctx.Px(1.4f));
            }
            if (picked)
            {
                _note.ColorIndex = i;
                _store.MarkDirty();
            }
        }
        var rows = (NoteColors.Count + perRow - 1) / perRow;
        ImGui.SetCursorScreenPos(rowStart + new Vector2(0f, rows * cell + ctx.Px(6f)));

        if (ModalUi.Button(NotesUi.T(ctx, "os.notes_done"), width))
        {
            _colorPicker = false;
        }
        ImGui.Dummy(new Vector2(0f, ctx.Px(2f)));
    }

    private void DrawDeletePanel(OsAppContext ctx, float width)
    {
        NotesUi.OverlayTitle(ctx, NotesUi.T(ctx, "os.notes_delete_title"), UiColors.Danger);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(UiColors.Hint, NotesUi.T(ctx, "os.notes_delete_body"));
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, ctx.Px(14f)));

        var half = (width - ctx.Px(10f)) * 0.5f;
        if (ModalUi.Button(NotesUi.T(ctx, "os.notes_cancel"), half))
        {
            _confirmDelete = false;
        }
        ImGui.SameLine(0f, ctx.Px(10f));
        PushDangerButton();
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ctx.Px(8f));
        var confirmed = Button(NotesUi.T(ctx, "os.notes_delete_confirm"), new Vector2(half, ctx.Px(32f)));
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        ImGui.Dummy(new Vector2(0f, ctx.Px(2f)));
        if (confirmed && _note is not null)
        {
            var doomed = _note;
            _confirmDelete = false;
            _note = null;
            _store.Delete(doomed);
            _store.Flush();
            _back();
        }
    }
}
