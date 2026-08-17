using System;
using System.Collections.Generic;
using System.Numerics;
using AetherLove.Services;
using AetherLove.UI;
using AetherOS.Apps.Notes.Screens;
using AetherOS.Apps.Notes.Ui;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherOS.Apps.Notes;

/// <summary>The Notes app: a searchable local notebook. Everything lives in the app's own storage, so it
/// needs neither the account nor the server.</summary>
public sealed class NotesApp : IAetherApp
{
    internal static readonly Vector4 TileTopColor = new(0.99f, 0.80f, 0.36f, 1f);
    internal static readonly Vector4 TileBottomColor = new(0.72f, 0.44f, 0.10f, 1f);

    private enum View { List, Editor, Tour }

    private readonly Func<string> _name;
    private readonly IAppStorage _storage;
    private readonly NotesStore _store;
    private readonly ListScreen _list;
    private readonly EditorScreen _editor;
    private readonly TourScreen _tour;
    private View _view = View.List;
    private bool _tourSeen;
    private bool _tourSeenLoaded;

    public NotesApp(Func<string> name, IAppCapabilities caps)
    {
        _name = name;
        _storage = caps.Storage("notes");
        _store = new NotesStore(_storage);
        _list = new ListScreen(_store, OpenNote, Compose);
        _editor = new EditorScreen(_store, BackToList, OpenNote);
        _tour = new TourScreen(FinishTour);
    }

    public string Id => "notes";
    public string Name => _name();
    public FontAwesomeIcon Icon => FontAwesomeIcon.StickyNote;
    public Vector4 TileTop => TileTopColor;
    public Vector4 TileBottom => TileBottomColor;

    /// <summary>Nothing here is unread, so the tile never badges.</summary>
    public int Badge => 0;

    public bool HasSurface => true;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings => Localization.AppStrings.Packs;

    public void Open()
    {
    }

    public void OnForeground()
    {
        _store.Reload();
        _view = View.List;
        _list.ClearSearch();
        _list.OnShow();
    }

    /// <summary>Backgrounding closes the editor, so the debounced edit is written before the note objects are
    /// re-read from disk on the way back in.</summary>
    public void OnBackground()
    {
        _editor.Commit();
        _store.Flush();
        _view = View.List;
    }

    public void OnIntent(OsIntent intent)
    {
    }

    public void Draw(OsAppContext ctx)
    {
        if (_view != View.Tour && ShouldAutoRunTour())
        {
            _view = View.Tour;
            _tour.OnShow();
        }

        if (_view == View.Tour)
        {
            _tour.Draw(ctx);
            _store.Tick();
            return;
        }

        if (_view == View.Editor)
        {
            _editor.Draw(ctx);
            _store.Tick();
            return;
        }

        DrawHeader(ctx);
        PushScrollbarStyle();
        using (var body = ImRaii.Child("##notesList", new Vector2(0f, 0f), false))
        {
            if (body)
            {
                _list.Draw(ctx);
            }
        }
        PopScrollbarStyle();
        _list.DrawOverlays(ctx);
        _store.Tick();
    }

    private void DrawHeader(OsAppContext ctx)
    {
        ImGui.Dummy(new Vector2(0f, ctx.Px(10f)));
        var originX = ImGui.GetWindowPos().X;
        var rowTop = ImGui.GetCursorScreenPos().Y;
        var title = NotesUi.T(ctx, "os.app_notes");

        float titleH;
        using (ctx.TitleFont?.Push())
        {
            titleH = ImGui.CalcTextSize(title).Y;
        }
        var rowH = MathF.Max(titleH, ctx.Px(30f));
        var centerY = rowTop + rowH * 0.5f;

        ImGui.SetCursorScreenPos(new Vector2(originX + ctx.Px(NotesUi.PadX), centerY - titleH * 0.5f));
        using (ctx.TitleFont?.Push())
        {
            ImGui.TextColored(ThemeService.Current.AccentLight, title);
        }

        DrawMenu(ctx, centerY);
        ImGui.SetCursorScreenPos(new Vector2(originX, rowTop + rowH));
        ImGui.Dummy(new Vector2(0f, ctx.Px(6f)));
    }

    private void DrawMenu(OsAppContext ctx, float centerY)
    {
        const string popupId = "##notesMenu";
        var menuTL = AppHeader.DrawMenuButton(ImGui.GetWindowSize().X, NotesUi.PadX, popupId, centerY: centerY);
        var open = AppHeader.BeginMenuPopup(menuTL, popupId);
        if (open)
        {
            var create = NotesUi.T(ctx, "os.notes_new");
            var tour = NotesUi.T(ctx, "os.notes_menu_tour");
            var w = AppHeader.MenuWidth(create, tour);
            var rowH = AppHeader.MenuRowHeight();

            if (AppHeader.MenuRow(FontAwesomeIcon.Plus, create, w, rowH))
            {
                Compose();
                ImGui.CloseCurrentPopup();
            }
            if (AppHeader.MenuRow(FontAwesomeIcon.StickyNote, tour, w, rowH))
            {
                _view = View.Tour;
                _tour.OnShow();
                ImGui.CloseCurrentPopup();
            }
        }
        AppHeader.EndMenuPopup(open);
    }

    private void Compose()
    {
        OpenNote(_store.Create());
    }

    private void OpenNote(Note note)
    {
        _editor.Open(note);
        _view = View.Editor;
    }

    private void BackToList()
    {
        _editor.Commit();
        _view = View.List;
        _list.OnShow();
    }

    private bool ShouldAutoRunTour()
    {
        if (!_tourSeenLoaded)
        {
            _tourSeen = _storage.Get<bool?>("tourSeen") ?? false;
            _tourSeenLoaded = true;
        }
        return !_tourSeen;
    }

    private void FinishTour()
    {
        _tourSeen = true;
        _storage.Set("tourSeen", (bool?)true);
        _view = View.List;
        _list.OnShow();
    }
}
