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

/// <summary>The AetherOS home screen: wallpaper, widget page, paged app grid, dock, and icon rearranging.</summary>
public sealed class HomeScreen
{
    private readonly OsShell _shell;
    private readonly WallpaperService _wallpapers;

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
    private const float LongPressSeconds = 0.45f;

    private float _page;
    private int _targetPage;
    private bool _draggingPages;
    private float _pageDragStartX;
    private float _pageAtDragStart;

    private bool _editMode;
    private string? _pressId;
    private Vector2 _pressPos;
    private float _pressT;
    private string? _dragId;

    private readonly Dictionary<string, (Vector2 TL, Vector2 BR)> _tileRects = new();
    private readonly Dictionary<string, Vector2> _animCenters = new();
    private readonly Dictionary<string, float> _removing = new();
    private readonly Dictionary<string, float> _pulse = new();
    private string? _removePromptId;

    private string? _openFolderId;
    private bool _folderEditMode;
    private string? _hoverFolderId;

    /// <summary>A trailing page conjured by dragging to the right edge. Drawn and counted, but only written to
    /// config once an icon actually lands on it.</summary>
    private bool _ghostPage;

    private string? _folderEjectId;
    private readonly Dictionary<string, float> _folderEjecting = new();
    private string _newFolderName = "";

    private const string FolderIdPrefix = "folder:";

    private static bool IsFolderId(string id) => id.StartsWith(FolderIdPrefix, StringComparison.Ordinal);

    private static OsFolder? FindFolder(string? id) =>
        id == null ? null : UiHost.Configuration.Os.Folders.FirstOrDefault(f => f.Id == id);

    public HomeScreen(OsShell shell, WallpaperService wallpapers)
    {
        _shell = shell;
        _wallpapers = wallpapers;
    }

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
        _folderEditMode = false;
        return true;
    }

    /// <summary>Opens the given folder's overlay; unknown ids are ignored. The Arcade folder always
    /// exists (EnsureArcade re-creates it every home frame), so its id is accepted unconditionally.</summary>
    public void OpenFolder(string folderId)
    {
        if (FindFolder(folderId) == null && folderId != OsFolders.ArcadeId)
        {
            return;
        }
        _openFolderId = folderId;
        _folderEditMode = false;
    }

    public void SetEditMode(bool on)
    {
        _editMode = on;
        if (!on)
        {
            _dragId = null;
            _pressId = null;
            _ghostPage = false;
            _edgeHoldT = 0f;
        }
    }

    public void SetAddAppsOpen(bool open)
    {
        _addAppsOpen = open;
        if (open)
        {
            _restoredApps.Clear();
        }
    }

    private Vector2 _donePillTL;
    private Vector2 _donePillBR;

    public bool TryGetDonePillRect(out Vector2 tl, out Vector2 br)
    {
        tl = _donePillTL;
        br = _donePillBR;
        return _editMode && br.X > tl.X;
    }

    public void Draw()
    {
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        var dl = ImGui.GetWindowDrawList();
        var time = (float)ImGui.GetTime();
        var dt = ImGui.GetIO().DeltaTime;

        DrawWallpaper(dl, origin, avail, time);

        if (_addAppsOpen)
        {
            dl.AddRectFilled(origin, origin + avail, OsDraw.Black(0.60f));
            DrawAddAppsOverlay(origin, avail);
            return;
        }

        if (_folderEjectId != null && _openFolderId != null)
        {
            dl.AddRectFilled(origin, origin + avail, OsDraw.Black(0.60f));
            DrawFolderEjectPrompt(origin, avail);
            return;
        }

        if (_openFolderId != null)
        {
            dl.AddRectFilled(origin, origin + avail, OsDraw.Black(0.60f));
            DrawFolderOverlay(origin, avail);
            return;
        }

        if (_removePromptId != null)
        {
            dl.AddRectFilled(origin, origin + avail, OsDraw.Black(0.60f));
            DrawRemovePrompt(origin, avail);
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

        DrawWidgetsPage(dl, origin, avail, XOffset(-1, avail.X));
        DrawGridPages(dl, origin, avail, layout, pageCount, time, dt);
        DrawPageDots(dl, origin, avail, pageCount);
        DrawDock(dl, origin, avail, layout.Dock, time, dt);
        if (_editMode)
        {
            DrawDonePill(dl, origin, avail);
        }
        DrawDraggedTile(dl);
        DrawWordmark(dl, origin, avail);

        dl.PopClipRect();

        HandlePageSwipe(origin, avail);
        UpdateDragState(origin, avail, layout);
    }

    private float XOffset(int pageIndex, float width) => (pageIndex - _page) * width;

    /// <summary>Pages to render: the real ones, the edge-drag ghost, and one trailing page when the last real
    /// page has no room for the add tile.</summary>
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

        // Both seeds have to run before the append below, or a newly shipped app would be placed loose on the
        // grid first and the Media seed would read that as the user having already arranged it.
        var seeded = Os.OsFolders.EnsureArcade(os);
        seeded |= Os.OsFolders.EnsureMedia(os);
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
        var appended = false;
        foreach (var id in shown)
        {
            if (!placed.Contains(id) && layout.PlaceInFirstFree(id))
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
    private string? FolderInSlot(HomeLayout layout, DropSpot target)
    {
        if (_dragId == null || IsFolderId(_dragId) || !target.IsValid)
        {
            return null;
        }
        var occupant = target.InDock
            ? (target.Slot >= 0 && target.Slot < layout.Dock.Count ? layout.Dock[target.Slot] : null)
            : layout.At(target.Page, target.Slot);
        // The Games folder refuses drops: it has no edit mode, so anything dropped in could never come out.
        return occupant != null && occupant != _dragId && IsFolderId(occupant)
            && !Os.OsFolders.IsBuiltIn(occupant) ? occupant : null;
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

        // The add tile sits in the last page's first free cell. It is hidden mid-drag so it never competes with
        // the cell the user is aiming at, least of all on a freshly conjured page where it would sit on slot 0.
        var addPage = pageCount - 1;
        var addSlot = -1;
        if (_dragId == null)
        {
            addSlot = addPage < layout.Pages.Count ? Array.IndexOf(layout.Pages[addPage], null) : 0;
        }

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
                    // A cell held by a switched-off app stays reserved but draws nothing.
                    if (cells[slot] is { } id && _shownIds.Contains(id))
                    {
                        DrawAppSlot(dl, id, SlotCenter(xOff, slot), slotW, time, dt, showLabel: true);
                    }
                }
            }

            if (page == addPage && addSlot >= 0)
            {
                DrawAddTile(dl, SlotCenter(xOff, addSlot), slotW, dt);
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

        if (_editMode && !AccessibilityService.ReduceMotion)
        {
            var phase = Hash01(appId.GetHashCode() & 0xFFFF) * MathF.Tau;
            center += new Vector2(MathF.Sin(time * 9f + phase) * Px(1.4f), MathF.Cos(time * 8f + phase) * Px(1.2f));
        }

        var half = Px(TileSize) * 0.5f;
        var tl = center - new Vector2(half, half);
        var br = center + new Vector2(half, half);
        _tileRects[appId] = (tl, br);

        var removable = removingT < 0f && _editMode && _dragId == null && !Os.OsFolders.IsBuiltIn(appId);
        var removePressed = false;
        var removeC = tl + Px(2f, 2f);
        if (removable)
        {
            ImGui.SetCursorScreenPos(removeC - Px(11f, 11f));
            removePressed = ImGui.InvisibleButton($"##rm_{appId}", Px(22f, 22f));
            SharedUiHelpers.HandOnHover();
        }

        var hovered = false;
        var held = false;
        if (removingT < 0f)
        {
            ImGui.SetCursorScreenPos(tl);
            ImGui.InvisibleButton($"##app_{appId}", br - tl);
            HandleTileInput(appId, app, tl, br);
            hovered = ImGui.IsItemHovered();
            held = ImGui.IsItemActive();
            if (hovered && !_editMode)
            {
                SharedUiHelpers.HandOnHover();
            }
        }

        // Dock tiles carry no label; a hovered one names itself in a pill above the bar.
        if (!showLabel && hovered && !_editMode && _dragId == null)
        {
            DrawDockHoverName(dl, app?.Name ?? folder!.Name, tl.Y, center.X);
        }

        var scale = 1f;
        if (!AccessibilityService.ReduceMotion && !_editMode)
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

        if ((hovered && !_editMode && !AccessibilityService.ReduceMotion) || _hoverFolderId == appId)
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
            if (clipped && hovered && !_editMode && _dragId == null)
            {
                ImGui.SetTooltip(label);
            }
        }

        if (removable)
        {
            dl.AddCircleFilled(removeC, Px(9f), ImGui.ColorConvertFloat4ToU32(new Vector4(0.22f, 0.22f, 0.26f, 0.98f)), 20);
            dl.AddCircle(removeC, Px(9f), OsDraw.White(0.5f), 20, Px(1f));
            IconDraw.AddCentered(dl, FontAwesomeIcon.Times, Px(9f), removeC, OsDraw.White(0.9f));
            if (removePressed)
            {
                // Deleting a folder just spills its apps back onto the grid, so it needs no confirm.
                if (folder != null || UiHost.Configuration.Os.SkipRemoveAppConfirm)
                {
                    StartRemove(appId);
                }
                else
                {
                    _removePromptId = appId;
                }
            }
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
            RemoveFolder(appId);
        }
        else if (appId.StartsWith(Os.ExternalApp.IdPrefix, StringComparison.Ordinal))
        {
            _shell.RemoveExternalApp(appId);
        }
        else
        {
            _shell.RemoveBuiltInApp(appId);
        }
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

    private void RemoveFolder(string folderId)
    {
        var os = UiHost.Configuration.Os;
        if (Os.OsFolders.IsBuiltIn(folderId)
            || os.Folders.FirstOrDefault(f => f.Id == folderId) is not { } folder)
        {
            return;
        }
        // The contents spill back into the folder's own cell first, then into whatever is free after it.
        HomeLayout.Edit(os, layout =>
        {
            var cell = layout.TryFind(folderId, out var page, out var slot) ? (page, slot) : ((int, int)?)null;
            layout.Remove(folderId);
            layout.Dock.Remove(folderId);
            foreach (var appId in folder.AppIds)
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
        if (_openFolderId == folderId)
        {
            _openFolderId = null;
        }
    }

    private void DrawFolderTile(ImDrawListPtr dl, OsFolder folder, Vector2 tl, Vector2 br, float alpha)
    {
        var size = br.X - tl.X;
        var rounding = size * 0.28f;

        if (Os.OsFolders.TileIcon(folder) is { } art)
        {
            dl.AddImageRounded(art, tl, br, Vector2.Zero, Vector2.One, OsDraw.White(alpha), rounding,
                ImDrawFlags.RoundCornersAll);
            dl.AddRect(tl + new Vector2(1f, 1f), br - new Vector2(1f, 1f), OsDraw.White(0.20f * alpha), rounding,
                ImDrawFlags.RoundCornersAll, 1f);
            return;
        }

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
            StartRemove(app.Id);
            _removePromptId = null;
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
            _pressT = 0f;
        }

        if (_pressId == id && ImGui.IsItemActive())
        {
            _pressT += ImGui.GetIO().DeltaTime;
            var moved = (ImGui.GetMousePos() - _pressPos).Length();
            if (!_editMode && _pressT >= LongPressSeconds && moved < Px(6f))
            {
                _editMode = true;
            }
            if (_editMode && moved > Px(5f) && _dragId == null)
            {
                _dragId = id;
            }
        }

        if (ImGui.IsItemDeactivated() && _pressId == id)
        {
            var moved = (ImGui.GetMousePos() - _pressPos).Length();
            if (!_editMode && _dragId == null && _pressT < LongPressSeconds && moved < Px(6f))
            {
                if (app == null)
                {
                    _openFolderId = id;
                    _folderEditMode = false;
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
        layout.SaveTo(os);
        UiHost.Configuration.Save();

        _dragId = null;
        _pressId = null;
        _hoverFolderId = null;
        _ghostPage = false;
        _edgeHoldT = 0f;
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

    private void DrawDonePill(ImDrawListPtr dl, Vector2 origin, Vector2 avail)
    {
        var label = Loc.T("os.edit_done");
        var sz = ImGui.CalcTextSize(label);
        var pad = Px(12f, 6f);
        var tl = new Vector2(origin.X + avail.X - sz.X - pad.X * 2f - Px(14f), origin.Y + Px(14f));
        var br = tl + sz + pad * 2f;
        _donePillTL = tl;
        _donePillBR = br;

        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton("##editDone", br - tl))
        {
            _editMode = false;
            _dragId = null;
        }
        var hovered = ImGui.IsItemHovered();
        dl.AddRectFilled(tl, br, hovered ? ThemeService.Current.AccentU32 : OsDraw.White(0.16f), (br.Y - tl.Y) * 0.5f);
        dl.AddText(tl + pad, OsDraw.White(0.96f), label);
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

    private void HandlePageSwipe(Vector2 origin, Vector2 avail)
    {
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##homeBg", new Vector2(avail.X, avail.Y - Px(DockH) - Px(16f)),
            ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);

        // Right-click on empty home space (submitted after the tiles, so a tile wins its own area) opens the
        // home context menu. Suppressed in edit mode, which has its own Done pill.
        if (!_editMode && _dragId == null && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            ImGui.OpenPopup("##homeCtx");
        }
        DrawHomeContextMenu();

        if (ImGui.IsItemActivated())
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
            if (SharedUiHelpers.DrawIconMenuItem(FontAwesomeIcon.ArrowsAlt, Loc.T("os.home_menu_adjust")))
            {
                SetEditMode(true);
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
        var y = o.Y + Px(34f);

        using (UiFonts.H2?.Push())
        {
            dl.AddText(new Vector2(o.X + padX, y), OsDraw.White(0.97f), Loc.T(GreetingKey()));
            y += ImGui.GetFontSize() + Px(4f);
        }
        dl.AddText(new Vector2(o.X + padX, y), OsDraw.White(0.65f), FormatDate(DateTime.Now));
        y += ImGui.GetFontSize() + Px(18f);

        y = DrawClockWidget(dl, o.X + padX, y, w);
        y = DrawStatusWidget(dl, o.X + padX, y, w);
        y = DrawNotifWidget(dl, o.X + padX, y, w);

        foreach (var app in _shell.Apps)
        {
            if (!app.Available || _shell.IsAppRemoved(app.Id))
            {
                continue;
            }
            var items = app.WidgetItems;
            if (items.Count > 0)
            {
                y = DrawAppWidget(dl, o.X + padX, y, w, app, items);
            }
        }
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

        // Action buttons claim their clicks first; the card's open target is submitted after them.
        if (actions.Count > 0)
        {
            var gap = Px(14f);
            var totalW = actions.Count * actionR * 2f + (actions.Count - 1) * gap;
            var cx = x + (w - totalW) * 0.5f + actionR;
            var cy = y + h - Px(8f) - actionR;
            for (var i = 0; i < actions.Count; i++)
            {
                DrawWidgetAction(dl, app.Id, i, actions[i], new Vector2(cx, cy), actionR);
                cx += actionR * 2f + gap;
            }
        }

        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton($"##widget_{app.Id}", br - tl))
        {
            _shell.OpenApp(app.Id);
        }
        var hovered = ImGui.IsItemHovered();

        if (hovered)
        {
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
        Vector2 center, float radius)
    {
        ImGui.SetCursorScreenPos(center - new Vector2(radius, radius));
        var clicked = ImGui.InvisibleButton($"##widgetAct_{appId}_{index}", new Vector2(radius * 2f, radius * 2f));
        var hovered = ImGui.IsItemHovered();
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

    private const string AddTileKey = "__addtile__";

    private void DrawAddTile(ImDrawListPtr dl, Vector2 slotCenter, float slotW, float dt)
    {
        if (!_animCenters.TryGetValue(AddTileKey, out var center))
        {
            center = slotCenter;
        }
        center += (slotCenter - center) * Math.Min(1f, dt * 14f);
        _animCenters[AddTileKey] = center;

        var half = Px(TileSize) * 0.5f;
        var tl = center - new Vector2(half, half);
        var br = center + new Vector2(half, half);
        ImGui.SetCursorScreenPos(tl);
        if (ImGui.InvisibleButton("##addApps", br - tl))
        {
            SetAddAppsOpen(true);
            _addAppsSearch = "";
        }
        var hovered = ImGui.IsItemHovered();
        SharedUiHelpers.HandOnHover();
        var rounding = half * 0.56f;
        dl.AddRectFilled(tl, br, OsDraw.White(hovered ? 0.16f : 0.10f), rounding);
        dl.AddRect(tl, br, OsDraw.White(hovered ? 0.55f : 0.30f), rounding, ImDrawFlags.RoundCornersAll, Px(1.2f));
        IconDraw.AddCentered(dl, FontAwesomeIcon.Plus, half * 0.8f, center, OsDraw.White(0.8f));
        OsDraw.CenteredText(dl, Loc.T("os.add_apps"), center.X, br.Y + Px(7f), OsDraw.White(0.75f), LabelScale);
    }

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
        HomeLayout.PlaceInConfig(os, folder.Id);
        UiHost.Configuration.Save();
        _newFolderName = "";
        _addAppsOpen = false;
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

        dl.AddRectFilled(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(new Vector4(0.07f, 0.07f, 0.10f, 0.98f)), Px(18f));
        dl.AddRect(panelTL, panelBR, OsDraw.White(0.12f), Px(18f), ImDrawFlags.RoundCornersAll, Px(1f));

        var builtIn = Os.OsFolders.IsBuiltIn(folder.Id);

        // The built-in folder cannot be renamed, so its edit mode keeps the plain title and offers only removal.
        if (_folderEditMode && !builtIn)
        {
            ImGui.SetCursorScreenPos(panelTL + Px(14f, 12f));
            ImGui.SetNextItemWidth(panelW - Px(130f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 1f, 1f, 0.08f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Px(9f));
            var name = folder.Name;
            if (ImGui.InputText("##folderName", ref name, 24))
            {
                folder.Name = name;
                UiHost.Configuration.Save();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }
        else
        {
            using (UiFonts.H3?.Push())
            {
                dl.AddText(panelTL + Px(16f, 14f), OsDraw.White(0.95f), Os.OsFolders.DisplayName(folder));
            }
        }

        var closeC = new Vector2(panelBR.X - Px(20f), panelTL.Y + Px(20f));
        ImGui.SetCursorScreenPos(closeC - Px(11f, 11f));
        if (ImGui.InvisibleButton("##folderClose", Px(22f, 22f)))
        {
            _openFolderId = null;
            _folderEditMode = false;
            return;
        }
        IconDraw.AddCentered(dl, FontAwesomeIcon.Times, Px(12f), closeC, OsDraw.White(0.7f));

        var editLabel = Loc.T(_folderEditMode ? "os.edit_done" : "os.folder_edit");
        var editSz = ImGui.CalcTextSize(editLabel);
        var editTL = new Vector2(closeC.X - Px(11f) - editSz.X - Px(24f), panelTL.Y + Px(11f));
        var editBR = editTL + editSz + Px(16f, 9f);
        ImGui.SetCursorScreenPos(editTL);
        if (ImGui.InvisibleButton("##folderEdit", editBR - editTL))
        {
            _folderEditMode = !_folderEditMode;
        }
        SharedUiHelpers.HandOnHover();
        dl.AddRectFilled(editTL, editBR,
            ImGui.IsItemHovered() || _folderEditMode ? ThemeService.Current.AccentU32 : OsDraw.White(0.14f),
            (editBR.Y - editTL.Y) * 0.5f);
        dl.AddText(editTL + Px(8f, 4f), OsDraw.White(0.96f), editLabel);

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
                _folderEditMode = false;
            }
        }

    }

    /// <summary>Confirm for taking an app out of the open folder; a full sub-mode like the remove prompt,
    /// because anything drawn in the folder's child window would render above (and steal input from) an
    /// in-place overlay. A user folder ejects the app back onto the grid; the built-in one removes it outright,
    /// since an ejected arcade app would just be adopted again on the next frame.</summary>
    private void DrawFolderEjectPrompt(Vector2 origin, Vector2 avail)
    {
        var folder = FindFolder(_openFolderId);
        var app = _folderEjectId == null ? null : _shell.Find(_folderEjectId);
        if (folder == null || app == null || !folder.AppIds.Contains(app.Id))
        {
            _folderEjectId = null;
            return;
        }

        var dl = ImGui.GetWindowDrawList();
        var panelW = avail.X - Px(56f);
        var panelTL = origin + new Vector2((avail.X - panelW) * 0.5f, avail.Y * 0.32f);
        var pad = Px(16f);
        var innerW = panelW - pad * 2f;

        var removes = Os.OsFolders.IsBuiltIn(folder.Id);
        var bodyText = Loc.T(removes ? "os.remove_app_body" : "os.folder_eject_body", app.Name);
        var bodyH = ImGui.CalcTextSize(bodyText, false, innerW).Y;
        var panelH = Px(58f) + bodyH + Px(54f);
        var panelBR = panelTL + new Vector2(panelW, panelH);

        dl.AddRectFilled(panelTL, panelBR, ImGui.ColorConvertFloat4ToU32(new Vector4(0.09f, 0.09f, 0.12f, 0.98f)), Px(16f));
        dl.AddRect(panelTL, panelBR, OsDraw.White(0.14f), Px(16f), ImDrawFlags.RoundCornersAll, Px(1f));

        using (UiFonts.H3?.Push())
        {
            dl.AddText(panelTL + new Vector2(pad, Px(14f)), OsDraw.White(0.95f),
                Loc.T(removes ? "os.remove_app_title" : "os.folder_eject_title"));
        }
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), panelTL + new Vector2(pad, Px(44f)),
            OsDraw.White(0.78f), bodyText, innerW);

        var btnH = Px(30f);
        var btnY = panelBR.Y - btnH - Px(12f);
        var btnW = (innerW - Px(8f)) * 0.5f;

        ImGui.SetCursorScreenPos(new Vector2(panelTL.X + pad, btnY));
        var cancel = ImGui.InvisibleButton("##fejCancel", new Vector2(btnW, btnH));
        var cancelHovered = ImGui.IsItemHovered();
        dl.AddRectFilled(new Vector2(panelTL.X + pad, btnY), new Vector2(panelTL.X + pad + btnW, btnY + btnH),
            OsDraw.White(cancelHovered ? 0.20f : 0.12f), Px(9f));
        OsDraw.CenteredText(dl, Loc.T("common.cancel"), panelTL.X + pad + btnW * 0.5f, btnY + Px(6f), OsDraw.White(0.92f));

        var okX = panelTL.X + pad + btnW + Px(8f);
        ImGui.SetCursorScreenPos(new Vector2(okX, btnY));
        var confirm = ImGui.InvisibleButton("##fejConfirm", new Vector2(btnW, btnH));
        var confirmHovered = ImGui.IsItemHovered();
        dl.AddRectFilled(new Vector2(okX, btnY), new Vector2(okX + btnW, btnY + btnH),
            confirmHovered ? ImGui.ColorConvertFloat4ToU32(ThemeService.Current.AccentLight) : ThemeService.Current.AccentU32, Px(9f));
        OsDraw.CenteredText(dl, Loc.T("os.remove_app_confirm"), okX + btnW * 0.5f, btnY + Px(6f), OsDraw.White(0.97f));

        if (cancel)
        {
            _folderEjectId = null;
        }
        else if (confirm)
        {
            _folderEjectId = null;
            if (AccessibilityService.ReduceMotion)
            {
                CommitFolderRemoval(folder, app.Id);
            }
            else
            {
                _folderEjecting[app.Id] = 0f;
            }
        }
    }

    private void CommitFolderRemoval(OsFolder folder, string appId)
    {
        if (Os.OsFolders.IsBuiltIn(folder.Id))
        {
            _shell.RemoveBuiltInApp(appId);
            return;
        }
        folder.AppIds.Remove(appId);
        HomeLayout.PlaceInConfig(UiHost.Configuration.Os, appId);
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

        var dt = ImGui.GetIO().DeltaTime;
        for (var i = 0; i < apps.Count; i++)
        {
            var app = apps[i];
            // Px(12) headroom keeps the edit-mode minus badge inside the child's clip rect.
            ImGui.SetCursorPos(new Vector2((i % Cols) * slotW + (slotW - tile) * 0.5f, (i / Cols) * rowH + Px(12f)));
            var tl = ImGui.GetCursorScreenPos();
            var br = tl + new Vector2(tile, tile);

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
            if (hovered && !_folderEditMode && !AccessibilityService.ReduceMotion)
            {
                cdl.AddRect(tl - Px(2f, 2f), br + Px(2f, 2f), ThemeService.Current.AccentU32,
                    tile * 0.28f + Px(2f), ImDrawFlags.RoundCornersAll, Px(1.6f));
            }

            if (_folderEditMode && ejectT < 0f)
            {
                var rmC = tl + Px(2f, 2f);
                ImGui.SetCursorScreenPos(rmC - Px(10f, 10f));
                if (ImGui.InvisibleButton($"##feject_{app.Id}", Px(20f, 20f)))
                {
                    _folderEjectId = app.Id;
                }
                SharedUiHelpers.HandOnHover();
                cdl.AddCircleFilled(rmC, Px(9f), ImGui.ColorConvertFloat4ToU32(new Vector4(0.22f, 0.22f, 0.26f, 0.98f)), 20);
                cdl.AddCircle(rmC, Px(9f), OsDraw.White(0.5f), 20, Px(1f));
                IconDraw.AddCentered(cdl, FontAwesomeIcon.Minus, Px(9f), rmC, OsDraw.White(0.9f));
            }
            else if (clicked)
            {
                _openFolderId = null;
                _shell.OpenApp(app.Id);
            }
        }

        ImGui.SetCursorPos(new Vector2(0f, MathF.Ceiling(apps.Count / (float)Cols) * rowH + Px(14f)));
        ImGui.Dummy(new Vector2(1f, 1f));
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
