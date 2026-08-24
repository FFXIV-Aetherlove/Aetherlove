using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using AetherLove.Os;
using AetherLove.Services;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AetherLove.Screens;

/// <summary>The AetherOS home screen: wallpaper, widget page, paged app grid, dock, and icon rearranging.
///
/// <para>There is no edit mode. Icons drag whenever the cursor moves far enough while held, and everything
/// that is not "move it" is a right-click: on a tile for its own actions, on empty space for adding things.
/// The long-press that used to arm a wiggle mode was a touch idiom with no hover affordance, so nobody found
/// it and it fired on any hesitant click.</para></summary>
public sealed partial class HomeScreen
{
    private readonly OsShell _shell;
    private readonly WallpaperService _wallpapers;
    private readonly Os.NewAppOffer _newApps;

    private static HomeGridPreset Preset => UiHost.Configuration.Os.HomeGrid;

    private static int GridColumns => Preset switch
    {
        HomeGridPreset.Comfortable => 3,
        HomeGridPreset.Dense => 5,
        _ => 4,
    };

    private static float TileSize => Preset switch
    {
        HomeGridPreset.Comfortable => 74f,
        HomeGridPreset.Compact => 54f,
        HomeGridPreset.Dense => 48f,
        _ => 62f,
    };

    private static float SlotH => Preset switch
    {
        HomeGridPreset.Comfortable => 118f,
        HomeGridPreset.Compact => 88f,
        HomeGridPreset.Dense => 82f,
        _ => 98f,
    };

    private static float LabelScale => Preset == HomeGridPreset.Comfortable ? 1.16f : 1f;

    private const float DockH = 76f;
    private const int MaxDock = 4;
    private const int StarCount = 26;

    private float _page;
    private float _widgetScroll;
    private float _widgetOverflow;
    private float _widgetBandTop;
    private float _widgetBandBottom;
    private int _targetPage;
    private bool _draggingPages;
    private float _pageDragStartX;
    private float _pageAtDragStart;

    private string? _pressId;
    private Vector2 _pressPos;
    private string? _dragId;

    /// <summary>How far the cursor travels before a press becomes a drag rather than a tap. Wide enough that
    /// a twitchy click still opens the app, narrow enough that a deliberate move picks the icon up at once.</summary>
    private static float DragThreshold => Px(7f);

    private readonly Dictionary<string, (Vector2 TL, Vector2 BR)> _tileRects = new();
    private readonly List<(Vector2 TL, Vector2 BR, bool Occupied)> _gridSlots = [];
    private readonly Dictionary<string, Vector2> _animCenters = new();
    private readonly Dictionary<string, float> _removing = new();
    private readonly Dictionary<string, float> _pulse = new();
    private string? _removePromptId;

    /// <summary>The folder whose delete is being confirmed.</summary>
    private string? _folderDeleteId;

    /// <summary>The name box for a folder being made from the home menu, and its one-shot focus.</summary>
    private bool _newFolderPrompt;
    private bool _newFolderFocus;

    /// <summary>Whether the cursor is over any tile this frame, so the background knows not to answer a
    /// right-click that belongs to a tile.</summary>
    private bool _tileHovered;

    /// <summary>The grid cell the home menu was opened over, so anything it creates lands where the player
    /// was pointing rather than in the first free cell somewhere else entirely.</summary>
    private (int Page, int Slot)? _menuCell;

    /// <summary>Whether the app awaiting the remove confirm is sitting in the open folder rather than on the
    /// grid, which decides whether the removal can play the tile's shrink or has to be immediate.</summary>
    private bool _removePromptInFolder;

    /// <summary>A folder made this session and still empty. The empty-folder sweep spares it, or a folder
    /// created from the menu would be deleted on the very next frame, before anything could be put in it.</summary>
    private string? _freshFolderId;

    private string? _openFolderId;

    /// <summary>Dragging inside the open folder: the app being carried, where the press began, the slot
    /// rects it is measured against, and the panel it has to leave to be taken out.</summary>
    private string? _folderPressId;
    private Vector2 _folderPressPos;
    private string? _folderDragId;
    private int _folderDropIndex = -1;
    private readonly List<(Vector2 TL, Vector2 BR)> _folderSlotRects = new();
    private (Vector2 TL, Vector2 BR) _folderPanelRect;
    private string? _hoverFolderId;

    /// <summary>A trailing page conjured by dragging to the right edge. Drawn and counted, but only written to
    /// config once an icon actually lands on it.</summary>
    private bool _ghostPage;

    private readonly Dictionary<string, float> _folderEjecting = new();
    private string _newFolderName = "";

    private const string FolderIdPrefix = "folder:";

    private static bool IsFolderId(string id) => id.StartsWith(FolderIdPrefix, StringComparison.Ordinal);

    private static OsFolder? FindFolder(string? id) =>
        id == null ? null : UiHost.Configuration.Os.Folders.FirstOrDefault(f => f.Id == id);

    public HomeScreen(OsShell shell, WallpaperService wallpapers, Os.NewAppOffer newApps,
        Os.IOsTogether together, Os.ShareService share, Os.TogetherOnboarding partyIntro)
    {
        _shell = shell;
        _wallpapers = wallpapers;
        _newApps = newApps;
        _together = together;
        _partyIntro = partyIntro;
        _partyCard = new Os.TogetherPartyCard(together, share, OpenPartyActivity)
        {
            // The first create or join teaches the feature before it does it; the explainer carries the
            // action and runs it on its last page.
            Intercept = pending => partyIntro.Show(pending),
            OpenSettings = partyIntro.ShowSettings,
        };
    }

    private readonly Os.IOsTogether _together;
    private readonly Os.TogetherOnboarding _partyIntro;
    private readonly Os.TogetherPartyCard _partyCard;

    /// <summary>The party card's own confirm, drawn by the phone window over the whole page: an overlay
    /// inside the widget page's clipped band would be cut off by it.</summary>
    public void DrawPartyOverlays(System.Numerics.Vector2 contentTL, System.Numerics.Vector2 contentBR) =>
        _partyCard.DrawKickConfirm(contentTL, contentBR);

    /// <summary>One-tap into the party's activity: Echo joins by the room code so any member lands in the
    /// watch room; anything else just opens the owning app.</summary>
    private void OpenPartyActivity(Os.OsPartyActivity activity)
    {
        if (activity.AppId == "echo" && activity.Code is { Length: > 0 } code)
        {
            _shell.SendIntent("echo", AetherOS.Sdk.OsIntents.CreateRoomJoin(
                AetherOS.Sdk.OsIntents.EchoJoin, activity.RefId, code));
        }
        _shell.OpenApp(activity.AppId);
    }

    /// <summary>Every tile the last frame drew, grid and dock together. The guided tour picks its examples
    /// out of this rather than naming apps: the grid belongs to the player and nothing is guaranteed to be
    /// on it, least of all on a phone being set up for the first time.</summary>
    public IReadOnlyCollection<string> DrawnTiles => _tileRects.Keys;

    /// <summary>Every cell of the page being looked at, in screen space, and whether something is standing
    /// in it. The tour needs an honest empty cell to point at when it demonstrates the background menu.</summary>
    public IReadOnlyList<(Vector2 TL, Vector2 BR, bool Occupied)> GridSlots => _gridSlots;

    /// <summary>The dock bar's rect as last drawn, or null before the first frame.</summary>
    public (Vector2 TL, Vector2 BR)? DockRect => _lastDockRect;

    public bool TryGetTileRect(string appId, out Vector2 tl, out Vector2 br)
    {
        if (_tileRects.TryGetValue(appId, out var rect))
        {
            tl = rect.TL;
            br = rect.BR;
            return true;
        }
        tl = br = default;
        return false;
    }

    // Guided-tour drivers: the tour animates the home screen through these instead of poking privates.
    public void ShowPage(int page) => _targetPage = page;

    /// <summary>Closes the open folder overlay, if any; the home button uses this so pressing home
    /// inside a folder leaves the folder instead of doing nothing.</summary>
    public bool CloseOpenFolder()
    {
        if (_openFolderId == null)
        {
            return false;
        }
        _openFolderId = null;
                return true;
    }

    /// <summary>Whether the home screen is showing (or sliding towards) the widget page, which is what the
    /// home button treats as somewhere to come back from.</summary>
    public bool OnWidgetPage => _targetPage < 0 || _page < -0.5f;

    /// <summary>What the home button does on the home screen itself, in order: leave an open folder, drop a
    /// party confirm, close the party explainer, then come back from the widget page to the first home
    /// page. False means there was nothing to leave.</summary>
    public bool HandleHomePress()
    {
        if (CloseOpenFolder())
        {
            return true;
        }
        if (_partyCard.DismissConfirm())
        {
            return true;
        }
        if (_partyIntro.Active)
        {
            _partyIntro.Dismiss();
            return true;
        }
        if (OnWidgetPage)
        {
            ShowPage(0);
            return true;
        }
        return false;
    }

    /// <summary>Opens the given folder's overlay; unknown ids are ignored. Arcade is checked like any other
    /// now: it is deleted when it empties and only comes back when there is a game to put in it.</summary>
    public void OpenFolder(string folderId)
    {
        if (FindFolder(folderId) == null)
        {
            return;
        }
        _openFolderId = folderId;
            }

    public void SetAddAppsOpen(bool open)
    {
        _addAppsOpen = open;
        if (open)
        {
            _restoredApps.Clear();
            // The sheet can add several things at once, so a single remembered cell means nothing here.
            _menuCell = null;
        }
    }

    public void Draw()
    {
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        var dl = ImGui.GetWindowDrawList();
        var time = (float)ImGui.GetTime();
        var dt = ImGui.GetIO().DeltaTime;

        // A folder can be closed from anywhere (the pill, a deep link, a gate); its drag must not outlive it.
        if (_openFolderId == null)
        {
            _folderDragId = null;
            _folderPressId = null;
        }

        DrawWallpaper(dl, origin, avail, time);

        if (_addAppsOpen)
        {
            dl.AddRectFilled(origin, origin + avail, OsDraw.Black(0.60f));
            DrawAddAppsOverlay(origin, avail);
            return;
        }

        // Both prompts go above the folder overlay: either can be raised from a tile on the folder page, and
        // drawn after it they would never appear at all.
        if (_folderDeleteId != null)
        {
            dl.AddRectFilled(origin, origin + avail, OsDraw.Black(0.60f));
            DrawFolderDeletePrompt(origin, avail);
            return;
        }

        if (_removePromptId != null)
        {
            dl.AddRectFilled(origin, origin + avail, OsDraw.Black(0.60f));
            DrawRemovePrompt(origin, avail);
            return;
        }

        if (_newFolderPrompt)
        {
            dl.AddRectFilled(origin, origin + avail, OsDraw.Black(0.60f));
            DrawNewFolderPrompt(origin, avail);
            return;
        }

        if (_openFolderId != null)
        {
            dl.AddRectFilled(origin, origin + avail, OsDraw.Black(0.60f));
            DrawFolderOverlay(origin, avail);
            return;
        }


        var layout = CurrentLayout(avail);
        NormalizePages(layout);
        var pageCount = PageCount(layout);
        _targetPage = Math.Clamp(_targetPage, -1, pageCount - 1);
        if (!_draggingPages)
        {
            _page += (_targetPage - _page) * Math.Min(1f, dt * 10f);
            if (MathF.Abs(_page - _targetPage) < 0.003f)
            {
                _page = _targetPage;
            }
        }

        dl.PushClipRect(origin, origin + avail, true);

        _tileHovered = false;
        DrawWidgetsPage(dl, origin, avail, XOffset(-1, avail.X));
        DrawGridPages(dl, origin, avail, layout, pageCount, time, dt);
        DrawPageDots(dl, origin, avail, pageCount);
        DrawDock(dl, origin, avail, layout.Dock, time, dt);
        DrawDraggedTile(dl);
        DrawWordmark(dl, origin, avail);

        dl.PopClipRect();

        HandlePageSwipe(origin, avail, layout);
        UpdateDragState(origin, avail, layout);
    }

    private float XOffset(int pageIndex, float width) => (pageIndex - _page) * width;

    /// <summary>Pages to render: the real ones, the edge-drag ghost, and one trailing page when the last real
    /// page is full, so there is always somewhere to drag an icon to.</summary>
    private int PageCount(HomeLayout layout)
    {
        var count = Math.Max(1, layout.Pages.Count);
        if (_ghostPage)
        {
            count++;
        }
        else if (layout.Pages.Count > 0 && Array.IndexOf(layout.Pages[^1], null) < 0)
        {
            count++;
        }
        return Math.Min(count, HomeLayout.MaxPages);
    }

    /// <summary>Drops pages that no longer hold anything and keeps the page the user is standing on pointing at
    /// the same content. Never runs mid-drag: the source page transiently empties the moment the cursor crosses
    /// to another page, and deleting it there would renumber everything and yank the drop target sideways.</summary>
    private void NormalizePages(HomeLayout layout)
    {
        if (_dragId != null)
        {
            return;
        }
        var removed = layout.DropEmptyPages();
        if (removed.Count == 0)
        {
            return;
        }
        foreach (var index in removed)
        {
            if (index < _targetPage)
            {
                _targetPage--;
                _page -= 1f;
            }
        }
        _targetPage = Math.Clamp(_targetPage, -1, Math.Max(0, layout.Pages.Count - 1));
        _animCenters.Clear();
        layout.SaveTo(UiHost.Configuration.Os);
        UiHost.Configuration.Save();
    }

    private int PageRows(Vector2 avail)
    {
        var gridTop = Px(128f);
        var gridBottom = avail.Y - Px(DockH) - Px(44f);
        return Math.Max(1, (int)((gridBottom - gridTop) / Px(SlotH)));
    }

    /// <summary>Two id sets: everything that may KEEP a cell, and the subset that is actually drawn. An app the
    /// server has switched off is registered but not shown, and it must hold on to its cell rather than be
    /// dropped from the saved layout and reappear somewhere else when the switch flips back. Only an id that is
    /// gone entirely (an uninstalled plugin, a deleted folder, an app moved into a folder, an app the user
    /// removed) loses its cell.</summary>
    private (HashSet<string> Keep, HashSet<string> Shown) LayoutIds()
    {
        var os = UiHost.Configuration.Os;
        var foldered = new HashSet<string>(os.Folders.SelectMany(f => f.AppIds), StringComparer.Ordinal);
        var keep = new HashSet<string>(StringComparer.Ordinal);
        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var app in _shell.Apps)
        {
            if (foldered.Contains(app.Id) || _shell.IsAppRemoved(app.Id))
            {
                continue;
            }
            keep.Add(app.Id);
            if (app.Available)
            {
                shown.Add(app.Id);
            }
        }
        foreach (var folder in os.Folders)
        {
            keep.Add(folder.Id);
            shown.Add(folder.Id);
        }
        return (keep, shown);
    }

    /// <summary>Resolves the persisted layout for this frame, converting a pre-2.1 flat order and repacking when
    /// the grid geometry changed. An id whose app is momentarily unavailable keeps its cell in config; it is only
    /// skipped for rendering.</summary>
    private HomeLayout CurrentLayout(Vector2 avail)
    {
        var os = UiHost.Configuration.Os;
        var rows = PageRows(avail);
        var cols = GridColumns;

        if (os.Pages.Count == 0)
        {
            SeedOrConvert(os, rows, cols);
        }
        else if (os.LayoutColumns != cols || os.LayoutRows != rows)
        {
            Repack(os, rows, cols);
        }

        // The offer is computed before the seeds run, or a seeded folder would adopt an app nobody has been
        // asked about yet and it would never be offered at all.
        _newApps.Refresh();

        // Emptied folders go first: a folder the seeds are about to look at must not still be holding a tile
        // it has nothing to put behind.
        if (_freshFolderId != null && FindFolder(_freshFolderId) is not { AppIds.Count: 0 })
        {
            _freshFolderId = null;
        }
        var seeded = Os.OsFolders.PruneEmpty(os, _freshFolderId);
        if (seeded && _openFolderId != null && FindFolder(_openFolderId) == null)
        {
            _openFolderId = null;
                    }

        // Every seed has to run before the append below, or a newly shipped app would be placed loose on the
        // grid first and the seed would read that as the user having already arranged it. They wait while an
        // offer is open, because a seed's whole decision is one-shot and the answer is not in yet.
        if (!_newApps.Active)
        {
            seeded |= Os.OsFolders.EnsureMedia(os);
            seeded |= Os.OsFolders.EnsureUtilities(os);
            seeded |= Os.OsFolders.NameArcade(os);
        }
        if (seeded)
        {
            UiHost.Configuration.Save();
        }

        var (keep, shown) = LayoutIds();
        _shownIds = shown;
        var layout = HomeLayout.FromConfig(os, rows, cols, keep.Contains);

        foreach (var id in os.DockIds)
        {
            if (keep.Contains(id) && !layout.Dock.Contains(id) && layout.Dock.Count < MaxDock)
            {
                layout.Dock.Add(id);
            }
        }

        var placed = new HashSet<string>(layout.Dock, StringComparer.Ordinal);
        foreach (var cells in layout.Pages)
        {
            foreach (var cell in cells)
            {
                if (cell != null)
                {
                    placed.Add(cell);
                }
            }
        }
        // An app only lands once the player has said yes to it; until then it is held back from the grid.
        var appended = false;
        foreach (var id in shown)
        {
            if (!placed.Contains(id) && !_newApps.IsPending(id) && layout.PlaceInFirstFree(id))
            {
                appended = true;
            }
        }
        if (appended)
        {
            layout.SaveTo(os);
            UiHost.Configuration.Save();
        }

        ApplyDragPreview(layout);
        return layout;
    }

    private HashSet<string> _shownIds = new(StringComparer.Ordinal);

    private void SeedOrConvert(OsConfig os, int rows, int cols)
    {
        if (os.IconOrder.Count == 0 && os.DockIds.Count == 0)
        {
            os.DockIds = ["clock", "camera", "settings"];
            os.IconOrder =
            [
                "news", "weather", "photos", "calendar",
                "aetherlove", "hangouts", "places", "messenger",
                "feedback",
            ];
        }
        var docked = new HashSet<string>(os.DockIds.Take(MaxDock), StringComparer.Ordinal);
        var converted = HomeLayout.FromLegacyOrder(os.IconOrder.Where(id => !docked.Contains(id)), rows, cols);
        converted.Dock.AddRange(os.DockIds.Take(MaxDock));
        converted.SaveTo(os);
        os.IconOrder.Clear();
        // A phone being laid out for the first time has answered for every app there is: the seed places what
        // it places and the rest arrive as they always did. Only an app that shows up AFTER this is offered.
        _newApps.MarkAllOffered();
        UiHost.Configuration.Save();
    }

    /// <summary>Re-places every icon into the first free cell of the new geometry, in reading order, after a grid
    /// preset or phone-size change. Holes are not preserved across a repack; the icons all have to fit.</summary>
    private static void Repack(OsConfig os, int rows, int cols)
    {
        var order = os.Pages.SelectMany(p => p.Items.OrderBy(i => i.Row).ThenBy(i => i.Col)).Select(i => i.Id);
        var packed = HomeLayout.FromLegacyOrder(order, rows, cols);
        packed.Dock.AddRange(os.DockIds.Take(MaxDock));
        packed.SaveTo(os);
        UiHost.Configuration.Save();
    }

    /// <summary>Lifts the dragged tile out of its cell and previews where it would land, so the shift is visible
    /// while the finger is still down. Nothing is written until the drop commits, and a target the tile cannot
    /// occupy puts it straight back, because the commit serialises this layout and a tile left lifted out would
    /// be persisted as gone.</summary>
    private void ApplyDragPreview(HomeLayout layout)
    {
        _hoverFolderId = null;
        if (_dragId == null)
        {
            return;
        }
        var fromDock = layout.Dock.IndexOf(_dragId);
        var fromCell = layout.TryFind(_dragId, out var page, out var slot) ? (page, slot) : ((int, int)?)null;
        layout.Remove(_dragId);
        layout.Dock.Remove(_dragId);

        var target = DropTarget(layout);
        // A folder in the hovered cell swallows the drop instead of the grid shifting around it, which would
        // slide the folder out from under the cursor before the drop could land.
        _hoverFolderId = FolderInSlot(layout, target);
        if (_hoverFolderId != null)
        {
            return;
        }
        if (target.InDock)
        {
            layout.Dock.Insert(Math.Clamp(target.Slot, 0, layout.Dock.Count), _dragId);
            return;
        }
        if (target.IsValid)
        {
            layout.EnsurePage(target.Page);
            if (layout.DropAt(target.Page, target.Slot, _dragId))
            {
                return;
            }
        }
        RestoreDrag(layout, _dragId, fromDock, fromCell);
    }

    private static void RestoreDrag(HomeLayout layout, string id, int fromDock, (int Page, int Slot)? fromCell)
    {
        if (fromCell is { } cell && layout.At(cell.Page, cell.Slot) == null)
        {
            layout.Pages[cell.Page][cell.Slot] = id;
            return;
        }
        if (fromDock >= 0)
        {
            layout.Dock.Insert(Math.Clamp(fromDock, 0, layout.Dock.Count), id);
            return;
        }
        layout.PlaceInFirstFree(id);
    }

    private readonly record struct DropSpot(bool InDock, int Page, int Slot)
    {
        public static readonly DropSpot None = new(false, -1, -1);

        public bool IsValid => InDock || (Page >= 0 && Slot >= 0);
    }

    /// <summary>The folder occupying a hovered cell, or null. Resolved against the cell the cursor is in rather
    /// than the tile's drawn rect: the rect is inset well inside its cell and is still easing towards its
    /// resting place, so the two disagree for the frames that matter and the drop never registers.</summary>
    /// <summary>What the dragged icon would land ON, when landing on it means something other than taking
    /// its slot: an existing folder to join, or another app to become a folder with. Null the rest of the
    /// time, which is when the drop is an ordinary reorder and the run shuffles aside.</summary>
    private string? FolderInSlot(HomeLayout layout, DropSpot target)
    {
        if (_dragId == null || IsFolderId(_dragId) || !target.IsValid)
        {
            return null;
        }
        var occupant = target.InDock
            ? (target.Slot >= 0 && target.Slot < layout.Dock.Count ? layout.Dock[target.Slot] : null)
            : layout.At(target.Page, target.Slot);
        if (occupant == null || occupant == _dragId)
        {
            return null;
        }
        // Squarely on the tile, not merely in its cell. Every reorder passes over occupied cells on its way,
        // so treating a cell hit as a merge would make ordinary rearranging impossible; the middle of a tile
        // means "onto this", the rest of the cell still means "here, shuffle along".
        if (!target.InDock && !OverTileCentre(layout, target))
        {
            return null;
        }
        // An app dropped onto another app makes a folder of the two. Only in the grid: the dock is a row of
        // favourites, and a fifth landing there should push, not swallow one of them.
        return IsFolderId(occupant) || (!target.InDock && _shownIds.Contains(occupant)) ? occupant : null;
    }

    private DropSpot DropTarget(HomeLayout layout)
    {
        var mouse = ImGui.GetMousePos();
        if (_lastDockRect.HasValue && mouse.Y >= _lastDockRect.Value.TL.Y - Px(8f) && layout.Dock.Count < MaxDock)
        {
            var slotW = (_lastDockRect.Value.BR.X - _lastDockRect.Value.TL.X) / Math.Max(1, layout.Dock.Count + 1);
            var idx = (int)((mouse.X - _lastDockRect.Value.TL.X) / slotW);
            return new DropSpot(true, -1, Math.Clamp(idx, 0, layout.Dock.Count));
        }
        if (!_lastGridOrigin.HasValue)
        {
            return DropSpot.None;
        }
        var col = (int)((mouse.X - _lastGridOrigin.Value.X) / _lastSlotW);
        var row = (int)((mouse.Y - _lastGridOrigin.Value.Y) / Px(SlotH));
        if (col < 0 || col >= layout.Columns || row < 0 || row >= layout.Rows)
        {
            return DropSpot.None;
        }
        return new DropSpot(false, _lastPageIndex, row * layout.Columns + col);
    }

    private Vector2? _lastGridOrigin;
    private float _lastSlotW;
    private int _lastPageIndex;
    private (Vector2 TL, Vector2 BR)? _lastDockRect;

    private void DrawWallpaper(ImDrawListPtr dl, Vector2 origin, Vector2 avail, float time)
    {
        var tex = _wallpapers.CurrentWrap();
        if (tex != null)
        {
            var (uv0, uv1) = OsDraw.CoverUv(tex.Width, tex.Height, avail.X, avail.Y);
            dl.AddImage(tex.Handle, origin, origin + avail, uv0, uv1);
            var dim = UiHost.Configuration.Os.WallpaperDim;
            if (dim > 0.01f)
            {
                dl.AddRectFilled(origin, origin + avail, OsDraw.Black(dim));
            }
            dl.AddRectFilledMultiColor(origin, new Vector2(origin.X + avail.X, origin.Y + Px(90f)),
                OsDraw.Black(0.35f), OsDraw.Black(0.35f), 0u, 0u);
            dl.AddRectFilledMultiColor(new Vector2(origin.X, origin.Y + avail.Y - Px(110f)), origin + avail,
                0u, 0u, OsDraw.Black(0.40f), OsDraw.Black(0.40f));
            return;
        }

        var t = ThemeService.Current;
        var baseCol = t.ChipFill;
        var top = ImGui.ColorConvertFloat4ToU32(Shade(baseCol, 1.35f));
        var bottom = ImGui.ColorConvertFloat4ToU32(Shade(baseCol, 0.45f));
        dl.AddRectFilledMultiColor(origin, origin + avail, top, top, bottom, bottom);

        var drift = AccessibilityService.ReduceMotion ? Vector2.Zero
            : new Vector2(MathF.Sin(time * 0.21f), MathF.Cos(time * 0.17f)) * Px(9f);
        DrawGlow(dl, origin + new Vector2(avail.X * 0.80f, avail.Y * 0.16f) + drift, avail.X * 0.46f, t.Accent);
        DrawGlow(dl, origin + new Vector2(avail.X * 0.14f, avail.Y * 0.86f) - drift, avail.X * 0.52f, t.SecondaryEnd);

        for (int i = 0; i < StarCount; i++)
        {
            var hx = Hash01(i * 2 + 1);
            var hy = Hash01(i * 2 + 2);
            var pos = origin + new Vector2(hx * avail.X, hy * avail.Y);
            var twinkle = AccessibilityService.ReduceMotion
                ? 0.5f
                : 0.5f + 0.5f * MathF.Sin(time * (0.8f + hx * 1.6f) + i * 2.1f);
            dl.AddCircleFilled(pos, Px(0.9f + hy * 1.3f), OsDraw.White(0.10f + 0.28f * twinkle));
        }
    }

    private static void DrawGlow(ImDrawListPtr dl, Vector2 center, float radius, Vector4 color)
    {
        for (int i = 4; i >= 1; i--)
        {
            dl.AddCircleFilled(center, radius * i / 4f,
                ImGui.ColorConvertFloat4ToU32(color with { W = 0.045f }), 48);
        }
    }

    private void DrawGridPages(ImDrawListPtr dl, Vector2 origin, Vector2 avail, HomeLayout layout, int pageCount,
        float time, float dt)
    {
        var padX = Px(16f);
        var slotW = (avail.X - padX * 2f) / layout.Columns;
        var gridTop = Px(128f);

        _lastSlotW = slotW;
        _tileRects.Clear();
        _gridSlots.Clear();

        // The drop origin is stamped for the page the user is on even while it is scrolled off screen, so a
        // multi-page traverse mid-drag never leaves a stale target behind.
        var current = Math.Clamp(_targetPage, 0, pageCount - 1);
        _lastGridOrigin = new Vector2(origin.X + padX + XOffset(current, avail.X), origin.Y + gridTop);
        _lastPageIndex = current;

        Vector2 SlotCenter(float xOff, int slot) => new(
            origin.X + xOff + padX + slotW * (slot % layout.Columns) + slotW * 0.5f,
            origin.Y + gridTop + Px(SlotH) * (slot / layout.Columns) + Px(TileSize) * 0.5f);

        for (int page = 0; page < pageCount; page++)
        {
            var xOff = XOffset(page, avail.X);
            if (MathF.Abs(xOff) > avail.X + Px(4f))
            {
                continue;
            }

            DrawClock(dl, origin + new Vector2(xOff, 0f), avail);

            if (page < layout.Pages.Count)
            {
                var cells = layout.Pages[page];
                for (int slot = 0; slot < cells.Length; slot++)
                {
                    if (page == current)
                    {
                        var centre = SlotCenter(xOff, slot);
                        var reach = new Vector2(Px(TileSize) * 0.5f, Px(TileSize) * 0.5f);
                        _gridSlots.Add((centre - reach, centre + reach, cells[slot] != null));
                    }

                    // A cell held by a switched-off app stays reserved but draws nothing.
                    if (cells[slot] is { } id && _shownIds.Contains(id))
                    {
                        DrawAppSlot(dl, id, SlotCenter(xOff, slot), slotW, time, dt, showLabel: true);
                    }
                }
            }
        }
    }

    private void DrawDock(ImDrawListPtr dl, Vector2 origin, Vector2 avail, List<string> dock, float time, float dt)
    {
        var barMargin = Px(14f);
        var barTL = new Vector2(origin.X + barMargin, origin.Y + avail.Y - Px(DockH) - Px(12f));
        var barBR = new Vector2(origin.X + avail.X - barMargin, origin.Y + avail.Y - Px(12f));
        _lastDockRect = (barTL, barBR);

        dl.AddRectFilled(barTL, barBR, OsDraw.White(0.10f), Px(24f));
        dl.AddRect(barTL, barBR, OsDraw.White(0.12f), Px(24f), ImDrawFlags.RoundCornersAll, Px(1f));

        // The dock stays dense: a switched-off app keeps its saved slot but the survivors close ranks.
        var visible = dock.Where(_shownIds.Contains).ToList();
        if (visible.Count == 0)
        {
            return;
        }
        var slotW = (barBR.X - barTL.X) / visible.Count;
        for (int i = 0; i < visible.Count; i++)
        {
            var center = new Vector2(barTL.X + slotW * i + slotW * 0.5f, (barTL.Y + barBR.Y) * 0.5f);
            DrawAppSlot(dl, visible[i], center, slotW, time, dt, showLabel: false);
        }
    }

    /// <summary>The name pill floating above a hovered dock icon, with a pointer down to the tile; clamped
    /// so the edge slots' pills stay inside the screen.</summary>
    private static void DrawDockHoverName(ImDrawListPtr dl, string name, float tileTop, float iconCenterX)
    {
        const uint PillBg = 0xF01A1420u; // near-opaque dark fill so the pill reads over any wallpaper

        var padX = Px(10f);
        var padY = Px(5f);
        var winTL = ImGui.GetWindowPos();
        var winBR = winTL + ImGui.GetWindowSize();

        // Cap the label to the screen so a very long docked-plugin name ellipsizes instead of overflowing.
        var maxTextW = winBR.X - winTL.X - Px(12f) - padX * 2f;
        var textSz = ImGui.CalcTextSize(name);
        var labelW = MathF.Min(textSz.X, maxTextW);
        var w = labelW + padX * 2f;
        var h = textSz.Y + padY * 2f;
        var x = Math.Clamp(iconCenterX - w * 0.5f, winTL.X + Px(6f), MathF.Max(winTL.X + Px(6f), winBR.X - Px(6f) - w));
        var tl = new Vector2(x, tileTop - h - Px(10f));
        var br = tl + new Vector2(w, h);

        dl.AddRectFilled(tl, br, PillBg, h * 0.5f);
        dl.AddRect(tl, br, OsDraw.White(0.20f), h * 0.5f, ImDrawFlags.RoundCornersAll, Px(1f));
        var arrowX = Math.Clamp(iconCenterX, tl.X + Px(10f), br.X - Px(10f));
        dl.AddTriangleFilled(new Vector2(arrowX - Px(5f), br.Y), new Vector2(arrowX + Px(5f), br.Y),
            new Vector2(arrowX, br.Y + Px(5f)), PillBg);
        OsDraw.CenteredText(dl, name, (tl.X + br.X) * 0.5f, tl.Y + padY, 0xFFFFFFFFu, 1f, labelW);
    }

    private void DrawAppSlot(ImDrawListPtr dl, string appId, Vector2 slotCenter, float slotW, float time, float dt,
        bool showLabel)
    {
        var app = _shell.Find(appId);
        var folder = app == null ? FindFolder(appId) : null;
        if (app == null && folder == null)
        {
            return;
        }

        if (!_animCenters.TryGetValue(appId, out var center))
        {
            center = slotCenter;
        }
        center += (slotCenter - center) * Math.Min(1f, dt * 14f);
        _animCenters[appId] = center;

        if (_dragId == appId)
        {
            return;
        }

        var removingT = -1f;
        if (_removing.TryGetValue(appId, out var rt))
        {
            rt += dt / 0.28f;
            if (rt >= 1f)
            {
                _removing.Remove(appId);
                _tileRects.Remove(appId);
                CommitRemove(appId);
                return;
            }
            _removing[appId] = rt;
            removingT = rt;
        }

        var half = Px(TileSize) * 0.5f;
        var tl = center - new Vector2(half, half);
        var br = center + new Vector2(half, half);
        _tileRects[appId] = (tl, br);

        var hovered = false;
        var held = false;
        if (removingT < 0f)
        {
            ImGui.SetCursorScreenPos(tl);
            ImGui.InvisibleButton($"##app_{appId}", br - tl);
            HandleTileInput(appId, app, tl, br);
            hovered = ImGui.IsItemHovered();
            held = ImGui.IsItemActive();
            _tileHovered |= hovered;
            if (_dragId == null)
            {
                DrawTileContextMenu(appId, inFolder: false);
            }
            if (hovered)
            {
                SharedUiHelpers.HandOnHover();
            }
        }

        // Dock tiles carry no label; a hovered one names itself in a pill above the bar.
        if (!showLabel && hovered && _dragId == null)
        {
            DrawDockHoverName(dl, app?.Name ?? folder!.Name, tl.Y, center.X);
        }

        var scale = 1f;
        if (!AccessibilityService.ReduceMotion)
        {
            if (held)
            {
                scale = 0.94f;
            }
            else if (hovered)
            {
                scale = 1.05f;
            }
        }
        if (_pulse.TryGetValue(appId, out var pulseT))
        {
            pulseT += dt / 0.35f;
            if (pulseT >= 1f)
            {
                _pulse.Remove(appId);
            }
            else
            {
                _pulse[appId] = pulseT;
                scale *= 1f + 0.16f * MathF.Sin(MathF.PI * pulseT);
            }
        }
        var tileAlpha = 1f;
        if (removingT >= 0f)
        {
            var e = removingT * removingT;
            scale *= 1f - e;
            tileAlpha = 1f - removingT;
        }
        // Connection-needing apps dim while the hub is down; they stay tappable (the offline panel explains).
        var offlineApp = app is { RequiresConnection: true } && !_shell.Connected;
        if (offlineApp)
        {
            tileAlpha *= 0.65f;
        }
        var shalf = half * scale;
        var stl = center - new Vector2(shalf, shalf);
        var sbr = center + new Vector2(shalf, shalf);

        dl.AddRectFilled(stl + Px(0f, 3f), sbr + Px(0f, 3f), OsDraw.Black(0.30f * tileAlpha), shalf * 0.56f);
        if (folder != null)
        {
            DrawFolderTile(dl, folder, stl, sbr, tileAlpha);
        }
        else
        {
            OsDraw.AppTile(dl, app!, stl, sbr, tileAlpha);
        }

        if ((hovered && !AccessibilityService.ReduceMotion) || _hoverFolderId == appId)
        {
            dl.AddRect(stl - new Vector2(2f, 2f), sbr + new Vector2(2f, 2f), ThemeService.Current.AccentU32,
                shalf * 0.56f + 2f, ImDrawFlags.RoundCornersAll, Px(1.6f));
        }

        var badge = folder != null ? FolderBadge(folder) : _shell.BadgeFor(app!);
        if (badge > 0 && removingT < 0f)
        {
            OsDraw.Badge(dl, new Vector2(sbr.X - Px(6f), stl.Y + Px(6f)), badge, 1.1f);
        }
        // A folder wears the "new" pill for whatever is new inside it, or a shipped app would hide silently.
        if (removingT < 0f && (folder != null ? folder.AppIds.Any(_shell.IsNewApp) : _shell.IsNewApp(appId)))
        {
            OsDraw.NewBadge(dl, new Vector2(stl.X, stl.Y + Px(6f)));
        }
        if (offlineApp && removingT < 0f)
        {
            DrawOfflineMarker(dl, new Vector2(sbr.X - Px(7f), sbr.Y - Px(7f)));
        }

        if (showLabel)
        {
            // A very long external-plugin name would otherwise run into its neighbours; clamp to the slot.
            var label = folder != null ? Os.OsFolders.DisplayName(folder) : app!.Name;
            var clipped = OsDraw.CenteredText(dl, label, center.X, br.Y + Px(7f),
                OsDraw.White(0.95f * tileAlpha), LabelScale, slotW - Px(6f));
            if (clipped && hovered && _dragId == null)
            {
                ImGui.SetTooltip(label);
            }
        }

    }

    /// <summary>Asks before taking an app off the home screen, unless the player has ticked the box that says
    /// stop asking. <paramref name="inFolder"/> means the tile is on the folder page rather than the grid, so
    /// there is no tile out there to shrink and the removal has to be immediate.</summary>
    private void RequestRemove(string appId, bool inFolder)
    {
        if (UiHost.Configuration.Os.SkipRemoveAppConfirm)
        {
            CommitRequestedRemove(appId, inFolder);
            return;
        }
        _removePromptId = appId;
        _removePromptInFolder = inFolder;
    }

    private void CommitRequestedRemove(string appId, bool inFolder)
    {
        if (!inFolder)
        {
            StartRemove(appId);
            return;
        }
        RemoveApp(appId);
        // Emptying the folder from inside it: the overlay returns before the sweep that deletes the folder
        // ever runs, so it would sit here open on nothing until it was closed by hand.
        if (FindFolder(_openFolderId) is not { AppIds.Count: > 0 })
        {
            _openFolderId = null;
                    }
    }

    private void StartRemove(string appId)
    {
        if (AccessibilityService.ReduceMotion)
        {
            CommitRemove(appId);
            return;
        }
        _removing[appId] = 0f;
    }

    /// <summary>Deleting a folder spills its apps back onto the grid, unpinning an external app drops it from
    /// config, and removing a built-in app hides and silences it until it is added back.</summary>
    private void CommitRemove(string appId)
    {
        if (IsFolderId(appId))
        {
            RemoveFolder(appId, removeApps: false);
            return;
        }
        RemoveApp(appId);
    }

    /// <summary>Takes one app off the home screen, whichever kind it is: an external plugin is unpinned, a
    /// built-in is hidden and silenced until it is added back.</summary>
    private void RemoveApp(string appId)
    {
        if (appId.StartsWith(Os.ExternalApp.IdPrefix, StringComparison.Ordinal))
        {
            _shell.RemoveExternalApp(appId);
            return;
        }
        _shell.RemoveBuiltInApp(appId);
    }

    private static void DrawOfflineMarker(ImDrawListPtr dl, Vector2 center)
    {
        dl.AddCircleFilled(center, Px(8f), OsDraw.Black(0.55f), 20);
        IconDraw.AddCentered(dl, FontAwesomeIcon.ExclamationTriangle, Px(8.5f), center,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.38f, 0.32f, 0.95f)));
    }

    private int FolderBadge(OsFolder folder)
    {
        var sum = 0;
        foreach (var id in folder.AppIds)
        {
            if (_shell.Find(id) is { } app)
            {
                sum += _shell.BadgeFor(app);
            }
        }
        return sum;
    }

    /// <summary>Deletes a folder. <paramref name="removeApps"/> takes everything inside off the home screen
    /// with it; otherwise the contents spill back onto the grid, into the folder's own cell first and then
    /// into whatever is free after it. Arcade is deletable like any other now, and stays deleted: adoption
    /// only claims games that are not already placed.</summary>
    private void RemoveFolder(string folderId, bool removeApps)
    {
        var os = UiHost.Configuration.Os;
        if (os.Folders.FirstOrDefault(f => f.Id == folderId) is not { } folder)
        {
            return;
        }

        var contents = new List<string>(folder.AppIds);
        folder.AppIds.Clear();
        HomeLayout.Edit(os, layout =>
        {
            var cell = layout.TryFind(folderId, out var page, out var slot) ? (page, slot) : ((int, int)?)null;
            layout.Remove(folderId);
            layout.Dock.Remove(folderId);
            if (removeApps)
            {
                return;
            }
            foreach (var appId in contents)
            {
                if (cell is { } spot && layout.At(spot.Item1, spot.Item2) == null)
                {
                    layout.Pages[spot.Item1][spot.Item2] = appId;
                    continue;
                }
                layout.PlaceInFirstFree(appId);
            }
        });
        os.Folders.Remove(folder);
        UiHost.Configuration.Save();

        if (removeApps)
        {
            foreach (var appId in contents)
            {
                RemoveApp(appId);
            }
        }
        if (_openFolderId == folderId)
        {
            _openFolderId = null;
                    }
    }

    private void DrawFolderTile(ImDrawListPtr dl, OsFolder folder, Vector2 tl, Vector2 br, float alpha)
    {
        var size = br.X - tl.X;
        var rounding = size * 0.28f;


        dl.AddRectFilled(tl, br, OsDraw.White(0.13f * alpha), rounding);
        dl.AddRect(tl + new Vector2(1f, 1f), br - new Vector2(1f, 1f), OsDraw.White(0.22f * alpha), rounding,
            ImDrawFlags.RoundCornersAll, 1f);

        var pad = size * 0.14f;
        var cell = (size - pad * 2f) / 3f;
        var inset = cell * 0.10f;
        var shown = 0;
        foreach (var id in folder.AppIds)
        {
            if (shown >= 9)
            {
                break;
            }
            if (_shell.Find(id) is not { } app)
            {
                continue;
            }
            var mtl = new Vector2(tl.X + pad + (shown % 3) * cell + inset, tl.Y + pad + (shown / 3) * cell + inset);
            OsDraw.AppTile(dl, app, mtl, mtl + new Vector2(cell - inset * 2f, cell - inset * 2f), alpha);
            shown++;
        }
    }

    private void DrawRemovePrompt(Vector2 origin, Vector2 avail)
    {
        var app = _removePromptId == null ? null : _shell.Find(_removePromptId);
        if (app == null)
        {
            _removePromptId = null;
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var panelW = avail.X - Px(56f);
        var panelTL = origin + new Vector2((avail.X - panelW) * 0.5f, avail.Y * 0.30f);
        var pad = Px(16f);
        var innerW = panelW - pad * 2f;

        var bodyText = Loc.T("os.remove_app_body", app.Name);
        var bodyH = ImGui.CalcTextSize(bodyText, false, innerW).Y;
        var panelH = Px(58f) + bodyH + Px(74f);
        var panelBR = panelTL + new Vector2(panelW, panelH);

        dl.AddRectFilled(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(new Vector4(0.09f, 0.09f, 0.12f, 0.98f)), Px(16f));
        dl.AddRect(panelTL, panelBR, OsDraw.White(0.14f), Px(16f), ImDrawFlags.RoundCornersAll, Px(1f));

        using (UiFonts.H3?.Push())
        {
            dl.AddText(panelTL + new Vector2(pad, Px(14f)), OsDraw.White(0.95f), Loc.T("os.remove_app_title"));
        }
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), panelTL + new Vector2(pad, Px(44f)),
            OsDraw.White(0.78f), bodyText, innerW);

        var checkY = panelTL.Y + Px(50f) + bodyH;
        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + pad, checkY));
        var skip = UiHost.Configuration.Os.SkipRemoveAppConfirm;
        if (ImGui.Checkbox(Loc.T("common.close_plugin_dont_ask"), ref skip))
        {
            UiHost.Configuration.Os.SkipRemoveAppConfirm = skip;
            UiHost.Configuration.Save();
        }

        var btnH = Px(30f);
        var btnY = panelBR.Y - btnH - Px(12f);
        var btnW = (innerW - Px(8f)) * 0.5f;

        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + pad, btnY));
        var cancel = ImGui.InvisibleButton("##rmCancel", new Vector2(btnW, btnH));
        var cancelHovered = ImGui.IsItemHovered();
        dl.AddRectFilled(new Vector2(panelTL.X + pad, btnY), new Vector2(panelTL.X + pad + btnW, btnY + btnH),
            OsDraw.White(cancelHovered ? 0.20f : 0.12f), Px(9f));
        OsDraw.CenteredText(dl, Loc.T("common.cancel"), panelTL.X + pad + btnW * 0.5f, btnY + Px(6f), OsDraw.White(0.92f));

        var rmX = panelTL.X + pad + btnW + Px(8f);
        ImGui.SetCursorScreenPos(new Vector2(rmX, btnY));
        var confirm = ImGui.InvisibleButton("##rmConfirm", new Vector2(btnW, btnH));
        var confirmHovered = ImGui.IsItemHovered();
        dl.AddRectFilled(new Vector2(rmX, btnY), new Vector2(rmX + btnW, btnY + btnH),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.72f, 0.20f, 0.24f, confirmHovered ? 1f : 0.88f)), Px(9f));
        OsDraw.CenteredText(dl, Loc.T("os.remove_app_confirm"), rmX + btnW * 0.5f, btnY + Px(6f), OsDraw.White(0.97f));

        if (cancel)
        {
            _removePromptId = null;
        }
        else if (confirm)
        {
            _removePromptId = null;
            CommitRequestedRemove(app.Id, _removePromptInFolder);
        }

        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##rmScrim", avail))
        {
            var m = ImGui.GetMousePos();
            var inPanel = m.X >= panelTL.X && m.X <= panelBR.X && m.Y >= panelTL.Y && m.Y <= panelBR.Y;
            if (!inPanel)
            {
                _removePromptId = null;
            }
        }
    }

    private void HandleTileInput(string id, IAetherApp? app, Vector2 tl, Vector2 br)
    {
        if (ImGui.IsItemActivated())
        {
            _pressId = id;
            _pressPos = ImGui.GetMousePos();
        }

        if (_pressId == id && ImGui.IsItemActive()
            && _dragId == null && (ImGui.GetMousePos() - _pressPos).Length() > DragThreshold)
        {
            _dragId = id;
        }

        if (ImGui.IsItemDeactivated() && _pressId == id)
        {
            if (_dragId == null && (ImGui.GetMousePos() - _pressPos).Length() <= DragThreshold)
            {
                if (app == null)
                {
                    _openFolderId = id;
                                    }
                else if (id.StartsWith(Os.ExternalApp.IdPrefix, StringComparison.Ordinal))
                {
                    if (!AccessibilityService.ReduceMotion)
                    {
                        _pulse[id] = 0f;
                    }
                    _shell.OpenApp(id);
                }
                else
                {
                    OsTransitions.PlayOpen(app, tl, br, () => _shell.OpenApp(app.Id));
                }
            }
            _pressId = null;
        }
    }

    /// <summary>Commits a drag. The layout passed in already carries this frame's preview, so the drop is written
    /// by serialising that, never by rebuilding config from what happened to be rendered.</summary>
    private void UpdateDragState(Vector2 origin, Vector2 avail, HomeLayout layout)
    {
        if (_dragId == null)
        {
            return;
        }
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var mouse = ImGui.GetMousePos();
            if (mouse.X > origin.X + avail.X - Px(14f))
            {
                NudgePage(1, layout);
            }
            else if (mouse.X < origin.X + Px(14f))
            {
                NudgePage(-1, layout);
            }
            else
            {
                _edgeHoldT = 0f;
            }
            return;
        }

        var os = UiHost.Configuration.Os;
        if (FindFolder(_hoverFolderId) is { } target && !IsFolderId(_dragId))
        {
            target.AppIds.Add(_dragId);
            layout.Remove(_dragId);
            layout.Dock.Remove(_dragId);
        }
        else if (_hoverFolderId is { } partner && !IsFolderId(_dragId) && !IsFolderId(partner))
        {
            MergeIntoNewFolder(os, layout, partner, _dragId);
        }
        layout.SaveTo(os);
        UiHost.Configuration.Save();

        _dragId = null;
        _pressId = null;
        _hoverFolderId = null;
        _ghostPage = false;
        _edgeHoldT = 0f;
    }

    /// <summary>Two apps become a folder where the one being dropped ON already sat, so the pair stays put
    /// and nothing else on the page moves. The order is target then dragged, which is the order they were
    /// on screen; the name is the plain default, since asking for one mid-drag interrupts the gesture and
    /// the folder's own page has an always-editable field for it.</summary>
    private static void MergeIntoNewFolder(OsConfig os, HomeLayout layout, string target, string dragged)
    {
        if (!layout.TryFind(target, out var page, out var slot))
        {
            return;
        }
        var folder = new OsFolder
        {
            Id = FolderIdPrefix + Guid.NewGuid().ToString("N"),
            Name = Loc.T("os.folder_default_name"),
        };
        folder.AppIds.Add(target);
        folder.AppIds.Add(dragged);
        os.Folders.Add(folder);

        layout.Remove(target);
        layout.Remove(dragged);
        layout.Dock.Remove(dragged);
        layout.DropAt(page, slot, folder.Id);
    }

    /// <summary>Whether the cursor is on the tile itself rather than out at the edges of its cell. The
    /// window is generous enough to be easy to hit on purpose and small enough to be hard to hit by
    /// accident while carrying an icon past.</summary>
    private bool OverTileCentre(HomeLayout layout, DropSpot target)
    {
        if (!_lastGridOrigin.HasValue || target.Page < 0 || target.Slot < 0)
        {
            return false;
        }
        var columns = Math.Max(1, layout.Columns);
        var centre = new Vector2(
            _lastGridOrigin.Value.X + (_lastSlotW * ((target.Slot % columns) + 0.5f)),
            _lastGridOrigin.Value.Y + (Px(SlotH) * (target.Slot / columns)) + (Px(TileSize) * 0.5f));
        var reach = Px(TileSize) * 0.5f;
        var mouse = ImGui.GetMousePos();
        return MathF.Abs(mouse.X - centre.X) < reach && MathF.Abs(mouse.Y - centre.Y) < reach;
    }

    private float _edgeHoldT;

    /// <summary>Holding a dragged icon against a screen edge flips pages. Past the last real page it conjures a
    /// ghost page, which only becomes real if the icon is dropped there.</summary>
    private void NudgePage(int dir, HomeLayout layout)
    {
        _edgeHoldT += ImGui.GetIO().DeltaTime;
        if (_edgeHoldT < 0.5f)
        {
            return;
        }
        _edgeHoldT = 0f;
        var last = Math.Max(0, layout.Pages.Count - 1);
        if (dir > 0 && _targetPage >= last && layout.Pages.Count < HomeLayout.MaxPages)
        {
            _ghostPage = true;
            _targetPage = last + 1;
            return;
        }
        _targetPage = Math.Clamp(_targetPage + dir, 0, PageCount(layout) - 1);
    }

    private void DrawDraggedTile(ImDrawListPtr dl)
    {
        if (_dragId == null)
        {
            return;
        }
        var app = _shell.Find(_dragId);
        var folder = app == null ? FindFolder(_dragId) : null;
        if (app == null && folder == null)
        {
            return;
        }
        var center = ImGui.GetMousePos();
        var half = Px(TileSize) * 0.56f;
        var tl = center - new Vector2(half, half);
        var br = center + new Vector2(half, half);
        dl.AddRectFilled(tl + Px(0f, 5f), br + Px(0f, 5f), OsDraw.Black(0.35f), half * 0.56f);
        if (folder != null)
        {
            DrawFolderTile(dl, folder, tl, br, 0.92f);
        }
        else
        {
            OsDraw.AppTile(dl, app!, tl, br, 0.92f);
        }
    }

    private void DrawPageDots(ImDrawListPtr dl, Vector2 origin, Vector2 avail, int pageCount)
    {
        var total = pageCount + 1;
        var gap = Px(13f);
        var y = origin.Y + avail.Y - Px(DockH) - Px(30f);
        var startX = origin.X + avail.X * 0.5f - (total - 1) * gap * 0.5f;
        for (int i = 0; i < total; i++)
        {
            var pageIndex = i - 1;
            var active = MathF.Abs(_page - pageIndex) < 0.5f;
            var pos = new Vector2(startX + i * gap, y);
            if (pageIndex == -1)
            {
                UI.IconDraw.AddCentered(dl, Dalamud.Interface.FontAwesomeIcon.Search, Px(7f), pos,
                    OsDraw.White(active ? 0.95f : 0.4f));
            }
            else
            {
                dl.AddCircleFilled(pos, Px(3f), OsDraw.White(active ? 0.95f : 0.4f), 16);
            }

            ImGui.SetCursorScreenPos(pos - Px(7f, 7f));
            if (ImGui.InvisibleButton($"##pageDot_{i}", Px(14f, 14f)))
            {
                _targetPage = pageIndex;
            }
            SharedUiHelpers.HandOnHover();
        }
    }

    private void HandlePageSwipe(Vector2 origin, Vector2 avail, HomeLayout layout)
    {
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##homeBg", new Vector2(avail.X, avail.Y - Px(DockH) - Px(16f)),
            ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);

        // Right-click on empty home space opens the home context menu. Suppressed in edit mode, which has its
        // own Done pill, and suppressed over a tile: this fires on the press while a tile's own menu opens on
        // the release, so without the check the home menu flashed up first and was replaced a frame later.
        var onWidgets = _page < -0.5f;
        if (_dragId == null && !_tileHovered && !WidgetDragActive && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            if (onWidgets)
            {
                // The widget page is not a grid: adding an app or a folder there would put it somewhere the
                // player is not looking, so it gets its own menu, and only while it has something to say.
                if (!_widgetHovered && AnyHiddenWidgets)
                {
                    OpenWidgetMenu(null, ImGui.GetMousePos());
                }
            }
            else
            {
                // Remembered here rather than read when the menu is used: by then the popup owns the cursor.
                var spot = DropTarget(layout);
                _menuCell = spot is { InDock: false, Page: >= 0, Slot: >= 0 } ? (spot.Page, spot.Slot) : null;
                ImGui.OpenPopup("##homeCtx");
            }
        }
        DrawHomeContextMenu();

        if (ImGui.IsItemActivated() && !WidgetDragActive)
        {
            _draggingPages = true;
            _pageDragStartX = ImGui.GetMousePos().X;
            _pageAtDragStart = _page;
        }
        if (_draggingPages)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var delta = ImGui.GetMousePos().X - _pageDragStartX;
                _page = _pageAtDragStart - delta / avail.X;
            }
            else
            {
                _draggingPages = false;
                var delta = ImGui.GetMousePos().X - _pageDragStartX;
                var flick = MathF.Abs(delta) > avail.X * 0.12f ? -MathF.Sign(delta) : 0f;
                _targetPage = (int)Math.Clamp(MathF.Round(_pageAtDragStart + (flick == 0f ? 0f : flick)), -1f, 8f);
            }
        }
    }

    /// <summary>The right-click menu on a tile: the one place removing something is a single deliberate act
    /// rather than a mode to enter first. A folder offers its own delete (which asks about its contents), and
    /// an app opened from inside a folder can be taken out of it or off the home screen entirely, which used
    /// to mean doing it twice: once to spill it onto the grid and again to remove it from there.
    ///
    /// <para>Called immediately after the tile's own button, and opened and drawn in that one place on
    /// purpose: a popup id opened in one ImGui scope cannot be begun in another, and the folder page draws
    /// inside a child window with an id stack of its own.</para></summary>
    private void DrawTileContextMenu(string id, bool inFolder)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Px(8f, 8f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Px(6f, 6f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.10f, 0.09f, 0.12f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.Border, ThemeService.Current.AccentWithAlpha(0.35f));
        if (ImGui.BeginPopupContextItem($"##tileCtx_{id}"))
        {
            // The pill normally clears by opening the app, which is a chore for a folder of them.
            if (NewInside(id).Count > 0
                && SharedUiHelpers.DrawIconMenuItem(FontAwesomeIcon.Check, Loc.T("os.tile_menu_mark_seen")))
            {
                _shell.MarkAppsSeen(NewInside(id));
                ImGui.CloseCurrentPopup();
            }

            if (IsFolderId(id))
            {
                if (SharedUiHelpers.DrawIconMenuItem(FontAwesomeIcon.FolderMinus, Loc.T("os.tile_menu_remove_folder")))
                {
                    _folderDeleteId = id;
                    ImGui.CloseCurrentPopup();
                }
            }
            else
            {
                if (inFolder && SharedUiHelpers.DrawIconMenuItem(
                    FontAwesomeIcon.FolderOpen, Loc.T("os.tile_menu_take_out")))
                {
                    if (FindFolder(_openFolderId) is { } folder && folder.AppIds.Contains(id))
                    {
                        if (AccessibilityService.ReduceMotion)
                        {
                            CommitFolderRemoval(folder, id);
                        }
                        else
                        {
                            _folderEjecting[id] = 0f;
                        }
                    }
                    ImGui.CloseCurrentPopup();
                }
                DrawMoveToFolderMenu(id);
                if (SharedUiHelpers.DrawIconMenuItem(FontAwesomeIcon.TrashAlt, Loc.T("os.tile_menu_remove_app")))
                {
                    RequestRemove(id, inFolder);
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(2);
    }

    /// <summary>The "move to folder" flyout on an app tile: every folder there is, minus the one the app
    /// already sits in. Absent entirely when that leaves nothing, since an empty flyout only teaches that the
    /// row does nothing.
    ///
    /// <para>The row's own label is blank padding measured against
    /// <see cref="SharedUiHelpers.DrawIconMenuItem"/>'s layout, with the icon and text drawn by hand at that
    /// helper's offsets, so a real ImGui submenu still lines up with the hand-rolled rows around it.</para>
    /// </summary>
    private void DrawMoveToFolderMenu(string appId)
    {
        var current = FolderContaining(appId);
        var folders = UiHost.Configuration.Os.Folders.Where(f => f != current).ToList();
        if (folders.Count == 0)
        {
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var style = ImGui.GetStyle();
        var label = Loc.T("os.tile_menu_move_to_folder");
        var labelSz = ImGui.CalcTextSize(label);
        var spaceW = MathF.Max(1f, ImGui.CalcTextSize(" ").X);
        var padCount = (int)MathF.Ceiling(MathF.Max(0f, Px(38f) - style.FramePadding.X + labelSz.X) / spaceW);
        var itemPos = ImGui.GetCursorScreenPos();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,
            new Vector2(style.ItemSpacing.X, style.FramePadding.Y * 2f));
        var open = ImGui.BeginMenu($"{new string(' ', padCount)}##mvfolder");
        ImGui.PopStyleVar();

        var fontSize = ImGui.GetFontSize();
        ImGui.PushFont(UiHost.PluginInterface.UiBuilder.FontIcon);
        var icon = FontAwesomeIcon.FolderOpen.ToIconString();
        var iconSz = ImGui.CalcTextSize(icon);
        dl.AddText(ImGui.GetFont(), fontSize,
            new Vector2(itemPos.X + Px(10f) + (Px(20f) - iconSz.X) * 0.5f,
                        itemPos.Y + (labelSz.Y - iconSz.Y) * 0.5f),
            0xFFEEEEEE, icon);
        ImGui.PopFont();
        dl.AddText(new Vector2(itemPos.X + Px(38f), itemPos.Y), 0xFFEEEEEE, label);
        if (!open)
        {
            return;
        }

        var subDl = ImGui.GetWindowDrawList();
        var inset = Px(30f);
        var rowW = folders.Max(f => inset + ImGui.CalcTextSize(Os.OsFolders.DisplayName(f)).X + Px(12f));
        foreach (var folder in folders)
        {
            var pos = ImGui.GetCursorScreenPos();
            var rowH = ImGui.GetFrameHeight();
            if (ImGui.Selectable($"##mvf_{folder.Id}", false, ImGuiSelectableFlags.None,
                new Vector2(rowW, rowH)))
            {
                MoveIntoFolder(appId, folder);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered())
            {
                SharedUiHelpers.HandOnHover();
            }
            IconDraw.AddCentered(subDl, FontAwesomeIcon.Folder, fontSize,
                new Vector2(pos.X + Px(14f), pos.Y + rowH * 0.5f), 0xFFEEEEEE);
            var name = Os.OsFolders.DisplayName(folder);
            var nameSz = ImGui.CalcTextSize(name);
            subDl.AddText(new Vector2(pos.X + inset, pos.Y + (rowH - nameSz.Y) * 0.5f), 0xFFEEEEEE, name);
        }
        ImGui.EndMenu();
    }

    private static OsFolder? FolderContaining(string appId) =>
        UiHost.Configuration.Os.Folders.FirstOrDefault(f => f.AppIds.Contains(appId));

    /// <summary>Files an app into a folder from wherever it is: another folder, a home cell, or the dock.</summary>
    private void MoveIntoFolder(string appId, OsFolder target)
    {
        if (target.AppIds.Contains(appId))
        {
            return;
        }
        if (FolderContaining(appId) is { } source)
        {
            source.AppIds.Remove(appId);
            if (source.AppIds.Count == 0 && _openFolderId == source.Id)
            {
                _openFolderId = null;
            }
        }
        HomeLayout.RemoveFromConfig(UiHost.Configuration.Os, appId);
        target.AppIds.Add(appId);
        UiHost.Configuration.Save();
    }

    /// <summary>Whatever still wears the "new" pill behind this tile: the app itself, or everything inside
    /// the folder.</summary>
    private List<string> NewInside(string id) =>
        FindFolder(id) is { } folder
            ? folder.AppIds.Where(_shell.IsNewApp).ToList()
            : _shell.IsNewApp(id) ? [id] : [];

    /// <summary>The home right-click menu: enter icon-arrange (wiggle) mode, or jump to the Settings wallpaper
    /// and home-screen page.</summary>
    private void DrawHomeContextMenu()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Px(8f, 8f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Px(6f, 6f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.10f, 0.09f, 0.12f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.Border, ThemeService.Current.AccentWithAlpha(0.35f));
        if (ImGui.BeginPopup("##homeCtx"))
        {
            if (SharedUiHelpers.DrawIconMenuItem(FontAwesomeIcon.Plus, Loc.T("os.home_menu_add_app")))
            {
                SetAddAppsOpen(true);
                _addAppsSearch = "";
                ImGui.CloseCurrentPopup();
            }
            if (SharedUiHelpers.DrawIconMenuItem(FontAwesomeIcon.FolderPlus, Loc.T("os.home_menu_add_folder")))
            {
                _newFolderName = "";
                _newFolderPrompt = true;
                _newFolderFocus = true;
                ImGui.CloseCurrentPopup();
            }
            if (_shell.NewApps().Any()
                && SharedUiHelpers.DrawIconMenuItem(FontAwesomeIcon.CheckDouble, Loc.T("os.home_menu_mark_seen")))
            {
                _shell.MarkAppsSeen(_shell.NewApps().ToList());
                ImGui.CloseCurrentPopup();
            }
            if (SharedUiHelpers.DrawIconMenuItem(FontAwesomeIcon.Image, Loc.T("os.home_menu_wallpaper")))
            {
                _shell.SendIntent("settings", OsIntents.Create(OsIntents.OpenWallpaper));
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(2);
    }

    private void DrawClock(ImDrawListPtr dl, Vector2 origin, Vector2 avail)
    {
        var now = DateTime.Now;
        var y = origin.Y + Px(28f);

        using (UiFonts.Clock?.Push())
        {
            // Invariant so the digits stay ASCII: the Clock font is baked with a digits-only glyph range,
            // and some system locales (Arabic/Persian) would otherwise substitute glyphs it doesn't carry.
            var txt = OsClock.Format(now);
            var sz = ImGui.CalcTextSize(txt);
            var pos = new Vector2(origin.X + (avail.X - sz.X) * 0.5f, y);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos + Px(0f, 2f), OsDraw.Black(0.35f), txt);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos, 0xFFFFFFFFu, txt);
            y += sz.Y + Px(4f);
        }

        var date = FormatDate(now);
        using (UiFonts.H3?.Push())
        {
            OsDraw.CenteredText(dl, date, origin.X + avail.X * 0.5f, y, OsDraw.White(0.82f));
        }
    }

    private void DrawWidgetsPage(ImDrawListPtr dl, Vector2 origin, Vector2 avail, float xOff)
    {
        if (MathF.Abs(xOff) > avail.X + Px(4f))
        {
            return;
        }
        var o = origin + new Vector2(xOff, 0f);
        var padX = Px(20f);
        var w = avail.X - padX * 2f;
        var top = o.Y + Px(34f);

        // The page has grown past the screen: every app that supplies widget items adds a card, and there is
        // no natural end to that list. Clipped to the band above the dock and scrolled by hand rather than
        // hosted in a child window, because a child would capture drags over the whole page and the
        // horizontal swipe between pages is the only way off this one.
        var bottom = origin.Y + avail.Y - Px(DockH) - Px(20f);
        var onThisPage = MathF.Abs(xOff) < Px(2f);
        if (onThisPage && !_draggingPages && _openFolderId == null
            && ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows)
            && ImGui.GetIO().MouseWheel is var wheel and not 0f)
        {
            _widgetScroll -= wheel * Px(48f);
        }
        _widgetScroll = Math.Clamp(_widgetScroll, 0f, MathF.Max(0f, _widgetOverflow));
        _widgetHovered = false;

        // A draw-list clip hides pixels and nothing else, so a card scrolled off the top would still be
        // holding an invisible button over the greeting. The cards check this band before submitting one.
        _widgetBandTop = origin.Y;
        _widgetBandBottom = bottom;

        dl.PushClipRect(new Vector2(origin.X, origin.Y), new Vector2(origin.X + avail.X, bottom), true);
        var y = top - _widgetScroll;

        using (UiFonts.H2?.Push())
        {
            dl.AddText(new Vector2(o.X + padX, y), OsDraw.White(0.97f), Loc.T(GreetingKey()));
            y += ImGui.GetFontSize() + Px(4f);
        }
        dl.AddText(new Vector2(o.X + padX, y), OsDraw.White(0.65f), FormatDate(DateTime.Now));
        y += ImGui.GetFontSize() + Px(18f);

        _widgetRects.Clear();
        var pastSlot = 0f;
        foreach (var id in OrderedWidgetIds())
        {
            // The lifted card keeps its row open while it rides the cursor, so the page under it neither
            // collapses nor jumps as the drop target moves.
            if (id == _widgetDragId)
            {
                _widgetSlotTop = y;
                y += _widgetDragSpan;
                pastSlot = _widgetDragSpan;
                continue;
            }
            var top0 = y;
            y = DrawWidgetCard(dl, id, o.X + padX, y, w);
            if (y <= top0)
            {
                continue;
            }
            // Recorded as if the lifted card were not on the page at all. The open row pushes everything
            // under it down by a card, and measuring the drop against that would make a card dragged
            // downwards need a whole extra card of travel before it moved a single row.
            _widgetRects.Add((id, top0 - pastSlot, y - pastSlot));
            if (!WidgetDragActive)
            {
                HandleWidgetContext(id, new Vector2(o.X + padX, top0), new Vector2(o.X + padX + w, y));
            }
            if (_widgetPressId == id)
            {
                DrawWidgetHoldHint(dl, new Vector2(o.X + padX, top0), new Vector2(o.X + padX + w, y));
            }
            if (_widgetRevealId == id)
            {
                RevealWidgetRow(top0, y, bottom);
            }
        }

        if (_widgetDragId is { } lifted)
        {
            DrawLiftedWidget(dl, lifted, o.X + padX, w);
        }

        dl.PopClipRect();

        // Outside the clip: the menu is allowed to hang past the band it was opened in, and it is submitted
        // here so its rows come before the page-wide swipe button and keep their clicks.
        DrawWidgetMenu(origin, avail);

        // Measured from what was actually drawn, so a card appearing or an app switching its widget off
        // changes the reach on the same frame rather than the next one.
        if (onThisPage)
        {
            _widgetOverflow = MathF.Max(0f, (y + _widgetScroll) - bottom + Px(12f));
            UpdateWidgetDrag(bottom);
            DrawWidgetScrollHint(dl, origin, avail, bottom);
        }
        else
        {
            CancelWidgetDrag();
        }
    }

    /// <summary>One card by id, and the one place that knows which draw belongs to which widget. Returns the
    /// bottom of the card, or the y it was given when the card had nothing to draw.</summary>
    private float DrawWidgetCard(ImDrawListPtr dl, string id, float x, float y, float w)
    {
        switch (id)
        {
            case ClockWidgetId:
                return DrawClockWidget(dl, x, y, w);
            case StatusWidgetId:
                return DrawStatusWidget(dl, x, y, w);
            case NotificationsWidgetId:
                return DrawNotifWidget(dl, x, y, w);
            // The party lives here rather than on the notification shade, where nobody found it. Its own
            // items are gated the way every other card on this page is: drawn while clipped, submitted only
            // in band.
            case PartyWidgetId:
                _partyCard.InputLocked = _partyIntro.Active || WidgetMenuBlocking || WidgetDragActive
                    || _partyCard.ConfirmOpen || y >= _widgetBandBottom || y + Px(220f) <= _widgetBandTop;
                return _partyCard.Draw(dl, x, y, w);
            default:
                var app = _shell.Find(id);
                if (app == null || !app.Available || _shell.IsAppRemoved(id))
                {
                    return y;
                }
                var items = app.WidgetItems;
                return items.Count > 0 ? DrawAppWidget(dl, x, y, w, app, items) : y;
        }
    }

    /// <summary>Scrolls a widget that was just put back into view. A page that has been arranged can be long,
    /// and a card that comes back below the fold reads as one that never came back at all.</summary>
    private void RevealWidgetRow(float top, float bottom, float bandBottom)
    {
        _widgetRevealId = null;
        if (bottom > bandBottom)
        {
            _widgetScroll += bottom - bandBottom + Px(12f);
        }
        else if (top < _widgetBandTop)
        {
            _widgetScroll -= _widgetBandTop - top + Px(12f);
        }
        _widgetScroll = MathF.Max(0f, _widgetScroll);
    }

    /// <summary>A slim track down the right edge while the page has more than fits. The page is one long
    /// column with no other affordance, so without it there is nothing to say the list continues.</summary>
    private void DrawWidgetScrollHint(ImDrawListPtr dl, Vector2 origin, Vector2 avail, float bottom)
    {
        if (_widgetOverflow <= 0f)
        {
            return;
        }
        var top = origin.Y + Px(34f);
        var height = bottom - top;
        var visible = height / (height + _widgetOverflow);
        var thumb = MathF.Max(Px(28f), height * visible);
        var travel = height - thumb;
        var at = top + (travel * Math.Clamp(_widgetScroll / _widgetOverflow, 0f, 1f));
        var x = origin.X + avail.X - Px(6f);
        dl.AddRectFilled(new Vector2(x, top), new Vector2(x + Px(2.5f), bottom), OsDraw.White(0.06f), Px(1.25f));
        dl.AddRectFilled(new Vector2(x, at), new Vector2(x + Px(2.5f), at + thumb), OsDraw.White(0.30f), Px(1.25f));
    }

    /// <summary>A generic app-provided widget card: the app's mini tile + name as header, one line per item,
    /// tap opens the app.</summary>
    private float DrawAppWidget(ImDrawListPtr dl, float x, float y, float w, IAetherApp app, IReadOnlyList<OsWidgetItem> items)
    {
        var actions = app.WidgetActions;
        var rowH = Px(23f);
        var actionR = Px(15f);
        var actionsH = actions.Count > 0 ? actionR * 2f + Px(12f) : 0f;
        var h = Px(38f) + items.Count * rowH + Px(8f) + actionsH;
        var tl = new Vector2(x, y);
        var br = new Vector2(x + w, y + h);

        GlassCard(dl, tl, br);

        // Scrolled out of the band: drawn (and clipped away) but never submitted, or it keeps a button over
        // whatever is really on screen there.
        if (br.Y <= _widgetBandTop || tl.Y >= _widgetBandBottom)
        {
            return y + h + Px(12f);
        }

        // Action buttons claim their clicks first; the card's open target is submitted after them. All of
        // them stand down while the context menu is up, whose rows are submitted after this card and would
        // otherwise lose their clicks to it (first-submitted-wins).
        var live = !WidgetMenuBlocking && !WidgetDragActive;
        if (actions.Count > 0)
        {
            var gap = Px(14f);
            var totalW = actions.Count * actionR * 2f + (actions.Count - 1) * gap;
            var cx = x + (w - totalW) * 0.5f + actionR;
            var cy = y + h - Px(8f) - actionR;
            for (var i = 0; i < actions.Count; i++)
            {
                DrawWidgetAction(dl, app.Id, i, actions[i], new Vector2(cx, cy), actionR, live);
                cx += actionR * 2f + gap;
            }
        }

        var hovered = false;
        if (live)
        {
            ImGui.SetCursorScreenPos(tl);
            if (ImGui.InvisibleButton($"##widget_{app.Id}", br - tl))
            {
                _shell.OpenApp(app.Id);
            }
            hovered = ImGui.IsItemHovered();
        }

        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
            dl.AddRect(tl, br, ThemeService.Current.AccentU32, Px(18f), ImDrawFlags.RoundCornersAll, Px(1.2f));
        }

        var iconSide = Px(18f);
        var iconTL = new Vector2(x + Px(14f), y + Px(9f));
        OsDraw.AppTile(dl, app, iconTL, iconTL + new Vector2(iconSide, iconSide));
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.92f,
            new Vector2(iconTL.X + iconSide + Px(8f), y + Px(10f)), OsDraw.White(0.60f), app.Name);

        var rowY = y + Px(36f);
        foreach (var item in items)
        {
            var detailSz = ImGui.CalcTextSize(item.Detail) * 0.92f;
            var detailX = x + w - detailSz.X - Px(16f);
            dl.PushClipRect(new Vector2(x + Px(16f), rowY), new Vector2(detailX - Px(8f), rowY + rowH), true);
            dl.AddText(new Vector2(x + Px(16f), rowY), OsDraw.White(0.92f), item.Title);
            dl.PopClipRect();
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.92f, new Vector2(detailX, rowY + Px(1f)),
                ImGui.ColorConvertFloat4ToU32(ThemeService.Current.AccentLight), item.Detail);
            rowY += rowH;
        }
        return y + h + Px(12f);
    }

    private static void DrawWidgetAction(ImDrawListPtr dl, string appId, int index, OsWidgetAction action,
        Vector2 center, float radius, bool live)
    {
        var clicked = false;
        var hovered = false;
        if (live)
        {
            ImGui.SetCursorScreenPos(center - new Vector2(radius, radius));
            clicked = ImGui.InvisibleButton($"##widgetAct_{appId}_{index}", new Vector2(radius * 2f, radius * 2f));
            hovered = ImGui.IsItemHovered();
        }
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
            ImGui.SetTooltip(action.Tooltip);
        }

        var t = ThemeService.Current;
        var fill = action.Primary
            ? ImGui.ColorConvertFloat4ToU32(t.Accent with { W = hovered ? 0.95f : 0.8f })
            : OsDraw.White(hovered ? 0.20f : 0.10f);
        dl.AddCircleFilled(center, radius, fill);
        IconDraw.AddCentered(dl, action.Icon, radius * 0.9f, center, OsDraw.White(0.95f));

        if (clicked)
        {
            action.Invoke();
        }
    }

    private float DrawClockWidget(ImDrawListPtr dl, float x, float y, float w)
    {
        var h = Px(122f);
        GlassCard(dl, new Vector2(x, y), new Vector2(x + w, y + h));

        var now = DateTime.Now;
        using (UiFonts.Clock?.Push())
        {
            dl.AddText(new Vector2(x + Px(16f), y + Px(10f)), OsDraw.White(0.97f), OsClock.Format(now));
        }
        var eorzea = EorzeaNow();
        var rowY = y + Px(68f);
        dl.AddText(new Vector2(x + Px(16f), rowY), OsDraw.White(0.62f), Loc.T("os.widget_eorzea"));
        dl.AddText(new Vector2(x + Px(16f), rowY + Px(23f)),
            ImGui.ColorConvertFloat4ToU32(ThemeService.Current.AccentLight), $"{eorzea.Hours:00}:{eorzea.Minutes:00}");
        var utc = DateTime.UtcNow;
        var utcX = x + w * 0.5f;
        dl.AddText(new Vector2(utcX, rowY), OsDraw.White(0.62f), "UTC");
        dl.AddText(new Vector2(utcX, rowY + Px(23f)), OsDraw.White(0.9f), OsClock.Format(utc));
        return y + h + Px(12f);
    }

    private float DrawStatusWidget(ImDrawListPtr dl, float x, float y, float w)
    {
        var h = Px(72f);
        GlassCard(dl, new Vector2(x, y), new Vector2(x + w, y + h));

        var connected = _shell.Connected;
        var dotC = new Vector2(x + Px(20f), y + h * 0.5f);
        dl.AddCircleFilled(dotC, Px(5f), ImGui.ColorConvertFloat4ToU32(
            connected ? new Vector4(0.35f, 0.85f, 0.45f, 1f) : new Vector4(0.9f, 0.35f, 0.3f, 1f)), 20);
        dl.AddText(new Vector2(x + Px(34f), y + Px(12f)), OsDraw.White(0.95f),
            Loc.T(connected ? "os.widget_connected" : "os.widget_offline"));
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.92f, new Vector2(x + Px(34f), y + Px(37f)),
            OsDraw.White(0.60f), Loc.T("os.widget_status"));

        var love = _shell.Find("aetherlove");
        if (love != null)
        {
            var badge = _shell.BadgeFor(love);
            var txt = Loc.T("os.widget_unread", badge);
            var sz = ImGui.CalcTextSize(txt);
            dl.AddText(new Vector2(x + w - sz.X - Px(16f), y + (h - sz.Y) * 0.5f),
                badge > 0 ? ImGui.ColorConvertFloat4ToU32(ThemeService.Current.AccentLight) : OsDraw.White(0.55f), txt);
        }
        return y + h + Px(12f);
    }

    private float DrawNotifWidget(ImDrawListPtr dl, float x, float y, float w)
    {
        var h = Px(84f);
        GlassCard(dl, new Vector2(x, y), new Vector2(x + w, y + h));

        var notifications = _shell.Notifications;
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.92f, new Vector2(x + Px(16f), y + Px(10f)),
            OsDraw.White(0.60f), Loc.T("os.notifications"));
        if (notifications.Count == 0)
        {
            dl.AddText(new Vector2(x + Px(16f), y + Px(36f)), OsDraw.White(0.80f), Loc.T("os.notifications_empty"));
            return y + h + Px(12f);
        }
        var latest = notifications[0];
        dl.PushClipRect(new Vector2(x, y), new Vector2(x + w - Px(12f), y + h), true);
        dl.AddText(new Vector2(x + Px(16f), y + Px(34f)), OsDraw.White(0.95f), latest.Title);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.92f, new Vector2(x + Px(16f), y + Px(57f)),
            OsDraw.White(0.65f), latest.Body);
        dl.PopClipRect();
        var countTxt = notifications.Count.ToString();
        var cSz = ImGui.CalcTextSize(countTxt);
        dl.AddText(new Vector2(x + w - cSz.X - Px(16f), y + Px(10f)),
            ImGui.ColorConvertFloat4ToU32(ThemeService.Current.AccentLight), countTxt);
        return y + h + Px(12f);
    }

    private static void GlassCard(ImDrawListPtr dl, Vector2 tl, Vector2 br)
    {
        dl.AddRectFilled(tl, br, OsDraw.White(0.09f), Px(18f));
        dl.AddRect(tl, br, OsDraw.White(0.11f), Px(18f), ImDrawFlags.RoundCornersAll, Px(1f));
    }

    private bool _addAppsOpen;

    /// <summary>Apps added back during the open sheet, so their row stays put with an "Added" pill instead of
    /// disappearing the instant it is clicked.</summary>
    private readonly HashSet<string> _restoredApps = new(StringComparer.Ordinal);
    private string _addAppsSearch = "";

    private void DrawAddAppsOverlay(Vector2 origin, Vector2 avail)
    {
        var dl = ImGui.GetWindowDrawList();
        var panelW = avail.X - Px(36f);
        var panelTL = origin + new Vector2((avail.X - panelW) * 0.5f, avail.Y * 0.14f);
        var panelBR = panelTL + new Vector2(panelW, avail.Y * 0.64f);

        dl.AddRectFilled(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(new Vector4(0.07f, 0.07f, 0.10f, 0.98f)), Px(18f));
        dl.AddRect(panelTL, panelBR, OsDraw.White(0.12f), Px(18f), ImDrawFlags.RoundCornersAll, Px(1f));

        using (UiFonts.H3?.Push())
        {
            dl.AddText(panelTL + Px(16f, 14f), OsDraw.White(0.95f), Loc.T("os.add_apps_title"));
        }
        var closeC = new Vector2(panelBR.X - Px(20f), panelTL.Y + Px(20f));
        ImGui.SetCursorScreenPos(closeC - Px(11f, 11f));
        if (ImGui.InvisibleButton("##addAppsClose", Px(22f, 22f)))
        {
            _addAppsOpen = false;
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.Times, Px(12f), closeC, OsDraw.White(0.7f));

        var hintX = panelTL.X + Px(16f);
        var hintY = panelTL.Y + Px(46f);
        var hintWrapW = panelW - Px(32f);
        var hint = Loc.T("os.add_apps_hint");
        ImGui.SetCursorScreenPos(new Vector2(hintX, hintY));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 0.62f));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + hintWrapW);
        ImGui.TextUnformatted(hint);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        var searchY = hintY + ImGui.CalcTextSize(hint, false, hintWrapW).Y + Px(8f);
        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + Px(12f), searchY));
        ImGui.SetNextItemWidth(panelW - Px(24f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 1f, 1f, 0.08f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        ImGui.InputTextWithHint("##addAppsSearch", Loc.T("os.add_apps_search"), ref _addAppsSearch, 64);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        var folderY = searchY + Px(34f);
        var createW = Px(92f);
        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + Px(12f), folderY));
        ImGui.SetNextItemWidth(panelW - Px(24f) - createW - Px(8f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 1f, 1f, 0.08f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        ImGui.InputTextWithHint("##newFolderName", Loc.T("os.folder_name_hint"), ref _newFolderName, 24);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        ImGui.SameLine(0f, Px(8f));
        var t = ThemeService.Current;
        ImGui.PushStyleColor(ImGuiCol.Button, t.ButtonNormal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, t.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, t.ButtonActive);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        if (ImGui.Button($"{Loc.T("os.new_folder")}##createFolder", new Vector2(createW, ImGui.GetFrameHeight())))
        {
            CreateFolder();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        var listTop = folderY + Px(36f);
        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + Px(12f), listTop));
        using (var list = ImRaii.Child("##addAppsList",
            new Vector2(panelW - Px(24f), panelBR.Y - listTop - Px(12f)), false, ImGuiWindowFlags.NoBackground))
        {
            if (list)
            {
                DrawInstallableRows(panelW - Px(24f));
            }
        }

        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##addAppsScrim", avail))
        {
            var m = ImGui.GetMousePos();
            var inPanel = m.X >= panelTL.X && m.X <= panelBR.X && m.Y >= panelTL.Y && m.Y <= panelBR.Y;
            if (!inPanel)
            {
                _addAppsOpen = false;
            }
        }
    }

    private void CreateFolder()
    {
        var os = UiHost.Configuration.Os;
        var name = _newFolderName.Trim();
        if (name.Length == 0)
        {
            name = Loc.T("os.folder_default_name");
        }
        var folder = new OsFolder { Id = FolderIdPrefix + Guid.NewGuid().ToString("N"), Name = name };
        os.Folders.Add(folder);
        if (_menuCell is { } cell)
        {
            HomeLayout.PlaceInConfigAt(os, folder.Id, cell.Page, cell.Slot);
        }
        else
        {
            HomeLayout.PlaceInConfig(os, folder.Id);
        }
        UiHost.Configuration.Save();
        _newFolderName = "";
        _addAppsOpen = false;
        _freshFolderId = folder.Id;
        _menuCell = null;
    }

    private void DrawFolderOverlay(Vector2 origin, Vector2 avail)
    {
        var folder = FindFolder(_openFolderId);
        if (folder == null)
        {
            _openFolderId = null;
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var panelW = avail.X - Px(36f);
        var panelTL = origin + new Vector2((avail.X - panelW) * 0.5f, avail.Y * 0.18f);
        var panelBR = panelTL + new Vector2(panelW, avail.Y * 0.56f);
        _folderPanelRect = (panelTL, panelBR);

        dl.AddRectFilled(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(new Vector4(0.07f, 0.07f, 0.10f, 0.98f)), Px(18f));
        dl.AddRect(panelTL, panelBR, OsDraw.White(0.12f), Px(18f), ImDrawFlags.RoundCornersAll, Px(1f));

        // Always editable: renaming used to need a mode, and the mode is gone.
        ImGui.SetCursorScreenPos(panelTL + Px(14f, 12f));
        ImGui.SetNextItemWidth(panelW - Px(64f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 1f, 1f, 0.06f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        var name = folder.Name;
        if (ImGui.InputText("##folderName", ref name, 24))
        {
            folder.Name = name;
            UiHost.Configuration.Save();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        var closeC = new Vector2(panelBR.X - Px(20f), panelTL.Y + Px(20f));
        ImGui.SetCursorScreenPos(closeC - Px(11f, 11f));
        if (ImGui.InvisibleButton("##folderClose", Px(22f, 22f)))
        {
            _openFolderId = null;
            return;
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.Times, Px(12f), closeC, OsDraw.White(0.7f));

        var top = panelTL.Y + Px(52f);
        var apps = new List<IAetherApp>();
        foreach (var id in folder.AppIds)
        {
            if (_shell.Find(id) is { } app)
            {
                apps.Add(app);
            }
        }

        if (apps.Count == 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(panelTL.X + Px(16f), top + Px(12f)));
            ImGui.PushTextWrapPos(panelBR.X - Px(16f));
            ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.70f, 1f), Loc.T("os.folder_empty"));
            ImGui.PopTextWrapPos();
        }
        else
        {
            ImGui.SetCursorScreenPos(new Vector2(panelTL.X + Px(12f), top));
            using var list = ImRaii.Child("##folderApps",
                new Vector2(panelW - Px(24f), panelBR.Y - top - Px(12f)), false, ImGuiWindowFlags.NoBackground);
            if (list)
            {
                DrawFolderAppGrid(folder, apps);
            }
        }

        ImGui.SetCursorScreenPos(origin);
        if (ImGui.InvisibleButton("##folderScrim", avail))
        {
            var m = ImGui.GetMousePos();
            var inPanel = m.X >= panelTL.X && m.X <= panelBR.X && m.Y >= panelTL.Y && m.Y <= panelBR.Y;
            if (!inPanel)
            {
                _openFolderId = null;
                            }
        }

    }

    /// <summary>Takes one app out of a folder and puts it on the grid. Arcade is no longer a special case:
    /// its adoption now leaves placed games alone, so an ejected one stays out instead of being pulled back
    /// on the next frame, which is what used to make this have to remove the app outright.</summary>
    /// <summary>Deleting a folder asks the one question that matters: does everything inside go too. Keeping
    /// them spills them onto the grid; removing them takes them off the home screen with the folder, which is
    /// the errand that used to need one removal per app plus one for the folder.</summary>
    private void DrawFolderDeletePrompt(Vector2 origin, Vector2 avail)
    {
        if (_folderDeleteId is not { } folderId || FindFolder(folderId) is not { } folder)
        {
            _folderDeleteId = null;
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var panelW = avail.X - Px(56f);
        var panelTL = origin + new Vector2((avail.X - panelW) * 0.5f, avail.Y * 0.30f);
        var pad = Px(16f);
        var innerW = panelW - (pad * 2f);

        var bodyText = Loc.T("os.folder_delete_body", Os.OsFolders.DisplayName(folder), folder.AppIds.Count);
        var bodyH = ImGui.CalcTextSize(bodyText, false, innerW).Y;
        var btnH = Px(30f);
        var panelH = Px(58f) + bodyH + Px(16f) + (btnH * 2f) + Px(20f);
        var panelBR = panelTL + new Vector2(panelW, panelH);

        dl.AddRectFilled(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(new Vector4(0.09f, 0.09f, 0.12f, 0.98f)), Px(16f));
        dl.AddRect(panelTL, panelBR, OsDraw.White(0.14f), Px(16f), ImDrawFlags.RoundCornersAll, Px(1f));

        using (UiFonts.H3?.Push())
        {
            dl.AddText(panelTL + new Vector2(pad, Px(14f)), OsDraw.White(0.95f), Loc.T("os.folder_delete_title"));
        }
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), panelTL + new Vector2(pad, Px(44f)),
            OsDraw.White(0.78f), bodyText, innerW);

        var y = panelTL.Y + Px(52f) + bodyH + Px(12f);
        if (PromptButton("##fdelRemove", new Vector2(panelTL.X + pad, y), innerW, btnH,
            Loc.T("os.folder_delete_remove"), primary: true))
        {
            _folderDeleteId = null;
            RemoveFolder(folderId, removeApps: true);
            return;
        }

        y += btnH + Px(6f);
        var half = (innerW - Px(8f)) * 0.5f;
        if (PromptButton("##fdelKeep", new Vector2(panelTL.X + pad, y), half, btnH,
            Loc.T("os.folder_delete_keep"), primary: false))
        {
            _folderDeleteId = null;
            RemoveFolder(folderId, removeApps: false);
            return;
        }
        if (PromptButton("##fdelCancel", new Vector2(panelTL.X + pad + half + Px(8f), y), half, btnH,
            Loc.T("common.cancel"), primary: false))
        {
            _folderDeleteId = null;
        }
    }

    /// <summary>Names the folder before making it. Enter is the same as Create, and an empty box takes the
    /// default name rather than refusing, so the fast path is two keystrokes.</summary>
    private void DrawNewFolderPrompt(Vector2 origin, Vector2 avail)
    {
        var dl = ImGui.GetWindowDrawList();
        var panelW = avail.X - Px(56f);
        var panelTL = origin + new Vector2((avail.X - panelW) * 0.5f, avail.Y * 0.30f);
        var pad = Px(16f);
        var innerW = panelW - (pad * 2f);
        var btnH = Px(30f);
        var panelH = Px(52f) + ImGui.GetFrameHeight() + Px(14f) + btnH + Px(14f);
        var panelBR = panelTL + new Vector2(panelW, panelH);

        dl.AddRectFilled(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(new Vector4(0.09f, 0.09f, 0.12f, 0.98f)), Px(16f));
        dl.AddRect(panelTL, panelBR, OsDraw.White(0.14f), Px(16f), ImDrawFlags.RoundCornersAll, Px(1f));

        using (UiFonts.H3?.Push())
        {
            dl.AddText(panelTL + new Vector2(pad, Px(14f)), OsDraw.White(0.95f), Loc.T("os.new_folder"));
        }

        ImGui.SetCursorScreenPos(panelTL + new Vector2(pad, Px(46f)));
        ImGui.SetNextItemWidth(innerW);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 1f, 1f, 0.08f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
        if (_newFolderFocus)
        {
            _newFolderFocus = false;
            ImGui.SetKeyboardFocusHere();
        }
        var submitted = ImGui.InputTextWithHint("##newFolderPromptName", Loc.T("os.folder_name_hint"),
            ref _newFolderName, 24, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        var btnY = panelBR.Y - btnH - Px(14f);
        var half = (innerW - Px(8f)) * 0.5f;
        var cancel = PromptButton("##nfCancel", new Vector2(panelTL.X + pad, btnY), half, btnH,
            Loc.T("common.cancel"), primary: false);
        var create = PromptButton("##nfCreate", new Vector2(panelTL.X + pad + half + Px(8f), btnY), half, btnH,
            Loc.T("os.folder_create"), primary: true);

        if (cancel)
        {
            _newFolderPrompt = false;
            _newFolderName = "";
            return;
        }
        if (create || submitted)
        {
            _newFolderPrompt = false;
            CreateFolder();
        }
    }

    /// <summary>One button of a prompt panel: an invisible hit box with its own plate drawn under it.</summary>
    private static bool PromptButton(string id, Vector2 tl, float width, float height, string label, bool primary)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(tl);
        var pressed = ImGui.InvisibleButton(id, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            SharedUiHelpers.HandOnHover();
        }
        var fill = primary
            ? (hovered ? ImGui.ColorConvertFloat4ToU32(ThemeService.Current.AccentLight) : ThemeService.Current.AccentU32)
            : OsDraw.White(hovered ? 0.20f : 0.12f);
        dl.AddRectFilled(tl, tl + new Vector2(width, height), fill, Px(9f));
        OsDraw.CenteredText(dl, label, tl.X + (width * 0.5f), tl.Y + Px(6f), OsDraw.White(primary ? 0.97f : 0.92f));
        return pressed;
    }

    private void CommitFolderRemoval(OsFolder folder, string appId)
    {
        folder.AppIds.Remove(appId);
        HomeLayout.PlaceInConfig(UiHost.Configuration.Os, appId);
        if (folder.AppIds.Count == 0)
        {
            _openFolderId = null;
        }
        UiHost.Configuration.Save();
    }

    private void DrawFolderAppGrid(OsFolder folder, List<IAetherApp> apps)
    {
        const int Cols = 4;
        var cdl = ImGui.GetWindowDrawList();
        var innerW = ImGui.GetContentRegionAvail().X;
        var slotW = innerW / Cols;
        var tile = MathF.Min(Px(62f), slotW - Px(10f));
        var rowH = tile + Px(30f);

        // The order to draw: the carried app moved to wherever it is hovering. Measured against last frame's
        // rects, which is a frame behind and invisible at any hand speed.
        var order = apps;
        if (_folderDragId != null && apps.FindIndex(a => a.Id == _folderDragId) is var from and >= 0)
        {
            _folderDropIndex = FolderSlotUnderMouse(apps.Count);
            if (_folderDropIndex >= 0 && _folderDropIndex != from)
            {
                order = new List<IAetherApp>(apps);
                var moved = order[from];
                order.RemoveAt(from);
                order.Insert(Math.Clamp(_folderDropIndex, 0, order.Count), moved);
            }
        }

        _folderSlotRects.Clear();
        var dt = ImGui.GetIO().DeltaTime;
        for (var i = 0; i < order.Count; i++)
        {
            var app = order[i];
            // Px(12) headroom keeps the top row's badges inside the child's clip rect.
            ImGui.SetCursorPos(new Vector2((i % Cols) * slotW + (slotW - tile) * 0.5f, (i / Cols) * rowH + Px(12f)));
            var tl = ImGui.GetCursorScreenPos();
            var br = tl + new Vector2(tile, tile);
            _folderSlotRects.Add((tl, br));

            // The carried tile leaves a hole and is drawn at the cursor instead. Its button still goes in
            // below: dropping the item ImGui holds the active id for would end the drag a frame later.
            var carried = app.Id == _folderDragId;

            var ejectT = -1f;
            if (_folderEjecting.TryGetValue(app.Id, out var et))
            {
                et += dt / 0.28f;
                if (et >= 1f)
                {
                    _folderEjecting.Remove(app.Id);
                    CommitFolderRemoval(folder, app.Id);
                    continue;
                }
                _folderEjecting[app.Id] = et;
                ejectT = et;
            }

            var clicked = false;
            var hovered = false;
            if (ejectT < 0f)
            {
                clicked = ImGui.InvisibleButton($"##fapp_{app.Id}", new Vector2(tile, tile));
                hovered = ImGui.IsItemHovered();
                HandleFolderTileInput(app.Id);
                DrawTileContextMenu(app.Id, inFolder: true);
            }

            if (carried)
            {
                continue;
            }

            var offlineApp = app.RequiresConnection && !_shell.Connected;
            var alpha = offlineApp ? 0.65f : 1f;
            var stl = tl;
            var sbr = br;
            if (ejectT >= 0f)
            {
                alpha *= 1f - ejectT;
                var shalf = tile * 0.5f * (1f - ejectT * ejectT);
                var tileCenter = (tl + br) * 0.5f;
                stl = tileCenter - new Vector2(shalf, shalf);
                sbr = tileCenter + new Vector2(shalf, shalf);
            }
            OsDraw.AppTile(cdl, app, stl, sbr, alpha);

            // Before the badges, exactly as the grid draws it: the ring is a halo around the tile and the
            // "new" pill overhangs the same corner, so a ring submitted afterwards is drawn straight
            // through the pill.
            if (hovered && !AccessibilityService.ReduceMotion)
            {
                cdl.AddRect(tl - Px(2f, 2f), br + Px(2f, 2f), ThemeService.Current.AccentU32,
                    tile * 0.28f + Px(2f), ImDrawFlags.RoundCornersAll, Px(1.6f));
            }

            var badge = _shell.BadgeFor(app);
            if (badge > 0 && ejectT < 0f)
            {
                OsDraw.Badge(cdl, new Vector2(br.X - Px(6f), tl.Y + Px(6f)), badge, 1.1f);
            }
            if (ejectT < 0f && _shell.IsNewApp(app.Id))
            {
                OsDraw.NewBadge(cdl, new Vector2(tl.X, tl.Y + Px(6f)));
            }
            if (offlineApp && ejectT < 0f)
            {
                DrawOfflineMarker(cdl, new Vector2(br.X - Px(7f), br.Y - Px(7f)));
            }
            // Clamped to the slot, not the tile: an unclamped label runs straight into its neighbours.
            var clipped = OsDraw.CenteredText(cdl, app.Name, (tl.X + br.X) * 0.5f, br.Y + Px(6f),
                OsDraw.White(0.9f * alpha), 1f, slotW - Px(6f));
            if (clipped && hovered)
            {
                ImGui.SetTooltip(app.Name);
            }
            if (clicked && _folderDragId == null)
            {
                _openFolderId = null;
                _shell.OpenApp(app.Id);
            }
        }

        DrawCarriedFolderTile(tile);
        UpdateFolderDrag(folder, order);

        ImGui.SetCursorPos(new Vector2(0f, MathF.Ceiling(order.Count / (float)Cols) * rowH + Px(14f)));
        ImGui.Dummy(new Vector2(1f, 1f));
    }

    /// <summary>Picks a tile up once the cursor has travelled far enough, the same threshold the home grid
    /// uses, so a tap still opens the app.</summary>
    private void HandleFolderTileInput(string appId)
    {
        if (ImGui.IsItemActivated())
        {
            _folderPressId = appId;
            _folderPressPos = ImGui.GetMousePos();
        }
        if (_folderPressId == appId && ImGui.IsItemActive() && _folderDragId == null
            && (ImGui.GetMousePos() - _folderPressPos).Length() > DragThreshold)
        {
            _folderDragId = appId;
        }
        if (ImGui.IsItemDeactivated() && _folderPressId == appId)
        {
            _folderPressId = null;
        }
    }

    /// <summary>The slot the cursor is over, or the last one when it is past the end of the grid. -1 while
    /// the cursor is outside the panel, which is what makes dropping there mean "take it out".</summary>
    private int FolderSlotUnderMouse(int count)
    {
        var mouse = ImGui.GetMousePos();
        var (panelTL, panelBR) = _folderPanelRect;
        if (mouse.X < panelTL.X || mouse.X > panelBR.X || mouse.Y < panelTL.Y || mouse.Y > panelBR.Y)
        {
            return -1;
        }

        var best = -1;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < _folderSlotRects.Count && i < count; i++)
        {
            var (tl, br) = _folderSlotRects[i];
            var centre = (tl + br) * 0.5f;
            var distance = (mouse - centre).LengthSquared();
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    /// <summary>The carried tile, on the foreground list because the folder page is a child window and
    /// anything drawn on the page's own list would slide under the tiles it is being dragged over.</summary>
    private void DrawCarriedFolderTile(float tile)
    {
        if (_folderDragId == null || _shell.Find(_folderDragId) is not { } app)
        {
            return;
        }
        var half = tile * 0.5f;
        var centre = ImGui.GetMousePos();
        var dl = ImGui.GetForegroundDrawList();
        dl.AddRectFilled(centre - new Vector2(half - 2f, half - 5f), centre + new Vector2(half + 2f, half + 5f),
            OsDraw.Black(0.35f), half * 0.6f);
        OsDraw.AppTile(dl, app, centre - new Vector2(half, half), centre + new Vector2(half, half), 0.92f);
    }

    /// <summary>Ends a folder drag: dropped on the page it commits the new order, dropped off it the app
    /// leaves the folder for the first free cell on the home screen.</summary>
    private void UpdateFolderDrag(OsFolder folder, List<IAetherApp> order)
    {
        if (_folderDragId == null || ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            return;
        }

        var dragged = _folderDragId;
        _folderDragId = null;
        _folderPressId = null;
        var outside = _folderDropIndex < 0;
        _folderDropIndex = -1;

        if (outside)
        {
            if (AccessibilityService.ReduceMotion)
            {
                CommitFolderRemoval(folder, dragged);
            }
            else
            {
                _folderEjecting[dragged] = 0f;
            }
            return;
        }

        // The drawn order IS the answer; anything else would re-derive a result already on screen.
        folder.AppIds.Clear();
        folder.AppIds.AddRange(order.Select(a => a.Id));
        UiHost.Configuration.Save();
    }

    /// <summary>The apps the user removed, which is what the sheet offers first: getting one of your own apps back
    /// is a far more common errand than pinning someone else's plugin. Rows added back this visit stay listed with
    /// a dead "Added" pill, so the row does not vanish out from under the click.</summary>
    private List<IAetherApp> RestorableApps(string query)
    {
        var ids = new List<string>(UiHost.Configuration.Os.RemovedApps);
        foreach (var id in _restoredApps)
        {
            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }
        return ids
            .Select(_shell.Find)
            .OfType<IAetherApp>()
            .Where(app => query.Length == 0 || app.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void DrawInstallableRows(float w)
    {
        w = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();
        var added = new HashSet<string>(UiHost.Configuration.Os.ExternalApps);
        var query = _addAppsSearch.Trim();
        var restorable = RestorableApps(query);
        var plugins = UiHost.PluginInterface.InstalledPlugins
            .Where(pl => pl.IsLoaded && pl.HasMainUi && pl.InternalName != "AetherLovePlugin")
            .Where(pl => query.Length == 0 || pl.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .GroupBy(pl => pl.InternalName)
            .Select(g => g.First())
            .OrderBy(pl => pl.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Headings only earn their place once there is something above the plugins to separate them from.
        if (restorable.Count > 0)
        {
            DrawAddSectionLabel(dl, Loc.T("os.add_apps_section_removed"));
            foreach (var app in restorable)
            {
                if (DrawAddRow(dl, w, app.Id, app, app.Name, !_shell.IsAppRemoved(app.Id)))
                {
                    _restoredApps.Add(app.Id);
                    _shell.RestoreBuiltInApp(app.Id);
                }
            }
            DrawAddSectionLabel(dl, Loc.T("os.add_apps_section_plugins"));
        }

        if (plugins.Length == 0)
        {
            ImGui.Dummy(Px(0f, 20f));
            OsDraw.CenteredText(dl, Loc.T("os.add_apps_none"),
                ImGui.GetCursorScreenPos().X + w * 0.5f, ImGui.GetCursorScreenPos().Y, OsDraw.White(0.5f), 0.9f);
            return;
        }

        foreach (var pl in plugins)
        {
            var visual = new Os.ExternalApp(pl.InternalName, pl.Name);
            if (DrawAddRow(dl, w, pl.InternalName, visual, pl.Name, added.Contains(pl.InternalName)))
            {
                _shell.AddExternalApp(pl.InternalName);
            }
        }
    }

    private static void DrawAddSectionLabel(ImDrawListPtr dl, string label)
    {
        var tl = ImGui.GetCursorScreenPos();
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.86f, tl + Px(4f, 8f), OsDraw.White(0.48f), label);
        ImGui.SetCursorScreenPos(tl + new Vector2(0f, Px(28f)));
    }

    /// <summary>One tile/name/button row in the add-apps sheet, shared by removed first-party apps and installed
    /// plugins. True when the add button was pressed this frame.</summary>
    private static bool DrawAddRow(ImDrawListPtr dl, float w, string key, IAetherApp visual, string name, bool isAdded)
    {
        var rowH = Px(60f);
        var btnW = Px(72f);
        var btnH = Px(30f);
        var rowTL = ImGui.GetCursorScreenPos();

        var btnTL = new Vector2(rowTL.X + w - btnW - Px(4f), rowTL.Y + (rowH - btnH) * 0.5f - Px(3f));
        var btnBR = btnTL + new Vector2(btnW, btnH);
        var pressed = false;
        if (!isAdded)
        {
            ImGui.SetCursorScreenPos(btnTL);
            pressed = ImGui.InvisibleButton($"##addRow_{key}", btnBR - btnTL);
            SharedUiHelpers.HandOnHover();
        }

        var tileSz = Px(42f);
        var tileTL = rowTL + new Vector2(Px(4f), (rowH - tileSz) * 0.5f - Px(3f));
        OsDraw.AppTile(dl, visual, tileTL, tileTL + new Vector2(tileSz, tileSz));

        var namePx = ImGui.GetFontSize() * 1.05f;
        dl.PushClipRect(rowTL, new Vector2(btnTL.X - Px(6f), rowTL.Y + rowH), true);
        dl.AddText(ImGui.GetFont(), namePx,
            new Vector2(tileTL.X + tileSz + Px(12f), rowTL.Y + (rowH - namePx) * 0.5f - Px(3f)),
            OsDraw.White(0.94f), name);
        dl.PopClipRect();

        if (isAdded)
        {
            dl.AddRectFilled(btnTL, btnBR, OsDraw.White(0.10f), btnH * 0.5f);
            OsDraw.CenteredText(dl, Loc.T("os.add_apps_added"), (btnTL.X + btnBR.X) * 0.5f,
                btnTL.Y + (btnH - ImGui.GetFontSize()) * 0.5f, OsDraw.White(0.55f));
        }
        else
        {
            var hovered = ImGui.IsMouseHoveringRect(btnTL, btnBR);
            dl.AddRectFilled(btnTL, btnBR, hovered
                ? ThemeService.Current.AccentU32
                : ImGui.ColorConvertFloat4ToU32(ThemeService.Current.ButtonNormal), btnH * 0.5f);
            OsDraw.CenteredText(dl, Loc.T("os.add_apps_add"), (btnTL.X + btnBR.X) * 0.5f,
                btnTL.Y + (btnH - ImGui.GetFontSize()) * 0.5f, OsDraw.White(0.97f));
        }

        ImGui.SetCursorScreenPos(rowTL + new Vector2(0f, rowH));
        return pressed;
    }

    private static void DrawWordmark(ImDrawListPtr dl, Vector2 origin, Vector2 avail)
    {
        const string mark = "AetherOS";
        var fsz = ImGui.GetFontSize() * 0.72f;
        var sz = ImGui.CalcTextSize(mark) * (fsz / ImGui.GetFontSize());
        dl.AddText(ImGui.GetFont(), fsz,
            new Vector2(origin.X + (avail.X - sz.X) * 0.5f, origin.Y + Px(6f)), OsDraw.White(0.25f), mark);
    }

    private static string GreetingKey()
    {
        var hour = DateTime.Now.Hour;
        if (hour < 6)
        {
            return "os.greeting_night";
        }
        if (hour < 12)
        {
            return "os.greeting_morning";
        }
        if (hour < 18)
        {
            return "os.greeting_afternoon";
        }
        return "os.greeting_evening";
    }

    private static TimeSpan EorzeaNow()
    {
        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var eorzeaSeconds = (long)(unix * (1440.0 / 70.0)) % 86400;
        return TimeSpan.FromSeconds(eorzeaSeconds);
    }

    private static string FormatDate(DateTime now)
    {
        // The phone's selected language, not the player's OS culture (which is why the date read Polish).
        var culture = LanguageProvider.CurrentCulture;
        var date = LanguageProvider.NormalizeSpaces(now.ToString("dddd d MMMM", culture));
        if (date.Length > 0)
        {
            date = string.Concat(char.ToUpper(date[0], culture).ToString(), date.AsSpan(1));
        }
        return date;
    }

    private static Vector4 Shade(Vector4 c, float f) =>
        new(Math.Clamp(c.X * f, 0f, 1f), Math.Clamp(c.Y * f, 0f, 1f), Math.Clamp(c.Z * f, 0f, 1f), 1f);

    private static float Hash01(int n)
    {
        unchecked
        {
            uint x = (uint)n * 2654435761u;
            x ^= x >> 15;
            x *= 2246822519u;
            x ^= x >> 13;
            return (x & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
