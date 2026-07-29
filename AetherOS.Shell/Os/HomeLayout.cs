using System;
using System.Collections.Generic;
using System.Linq;
using AetherOS.Sdk;

namespace AetherLove.Os;

/// <summary>The home grid resolved for one frame: a slot array per page, where a null slot is an empty cell.
/// Built from <see cref="OsConfig.Pages"/> and thrown away each frame; the config is only written on a commit.</summary>
public sealed class HomeLayout
{
    public const int MaxPages = 9;

    public List<string?[]> Pages { get; } = new();

    public List<string> Dock { get; } = new();

    public int Rows { get; }

    public int Columns { get; }

    public int Capacity => Rows * Columns;

    public HomeLayout(int rows, int columns)
    {
        Rows = Math.Max(1, rows);
        Columns = Math.Max(1, columns);
    }

    public string?[] AddPage()
    {
        var page = new string?[Capacity];
        Pages.Add(page);
        return page;
    }

    public void EnsurePage(int index)
    {
        while (Pages.Count <= index && Pages.Count < MaxPages)
        {
            AddPage();
        }
    }

    public string? At(int page, int slot) =>
        page >= 0 && page < Pages.Count && slot >= 0 && slot < Capacity ? Pages[page][slot] : null;

    public bool TryFind(string id, out int page, out int slot)
    {
        for (int p = 0; p < Pages.Count; p++)
        {
            var found = Array.IndexOf(Pages[p], id);
            if (found >= 0)
            {
                page = p;
                slot = found;
                return true;
            }
        }
        page = -1;
        slot = -1;
        return false;
    }

    public void Remove(string id)
    {
        if (TryFind(id, out var page, out var slot))
        {
            Pages[page][slot] = null;
        }
    }

    /// <summary>Places an id in the first empty cell, scanning pages in order and appending one when every page
    /// is full. False when the page cap is reached with nothing free.</summary>
    public bool PlaceInFirstFree(string id)
    {
        for (int p = 0; p < Pages.Count; p++)
        {
            var slot = Array.IndexOf(Pages[p], null);
            if (slot >= 0)
            {
                Pages[p][slot] = id;
                return true;
            }
        }
        if (Pages.Count >= MaxPages)
        {
            return false;
        }
        AddPage()[0] = id;
        return true;
    }

    /// <summary>Drops an id onto a cell the iOS way: an occupied target pushes its occupant, and the run behind
    /// it, one cell along until the first empty cell absorbs the push, so holes beyond that point survive. The
    /// push goes forward by preference and falls back to a hole in front of the target; only a page with no hole
    /// at all spills its last icon onto the next page, which is created if needed. False when nothing could move,
    /// in which case the caller leaves the layout alone and the tile snaps back.</summary>
    public bool DropAt(int page, int slot, string id)
    {
        if (page < 0 || page >= Pages.Count || slot < 0 || slot >= Capacity)
        {
            return false;
        }
        var cells = Pages[page];
        if (cells[slot] == null)
        {
            cells[slot] = id;
            return true;
        }

        var ahead = Array.IndexOf(cells, null, slot);
        if (ahead >= 0)
        {
            for (int i = ahead; i > slot; i--)
            {
                cells[i] = cells[i - 1];
            }
            cells[slot] = id;
            return true;
        }

        var behind = Array.LastIndexOf(cells, null, slot);
        if (behind >= 0)
        {
            for (int i = behind; i < slot; i++)
            {
                cells[i] = cells[i + 1];
            }
            cells[slot] = id;
            return true;
        }

        var spilled = cells[Capacity - 1];
        if (spilled != null && !SpillToNextPage(page, spilled))
        {
            return false;
        }
        for (int i = Capacity - 1; i > slot; i--)
        {
            cells[i] = cells[i - 1];
        }
        cells[slot] = id;
        return true;
    }

    /// <summary>Pushes an icon displaced off the end of a page onto the front of the next one, cascading if that
    /// page is also full.</summary>
    private bool SpillToNextPage(int page, string id)
    {
        var next = page + 1;
        if (next >= MaxPages)
        {
            return false;
        }
        if (next >= Pages.Count)
        {
            AddPage()[0] = id;
            return true;
        }
        return DropAt(next, 0, id);
    }

    /// <summary>Drops every trailing and interior page that holds nothing, keeping at least one. Returns the
    /// removed page indices, oldest first, so the caller can correct the page it is standing on.</summary>
    public List<int> DropEmptyPages()
    {
        var removed = new List<int>();
        for (int p = Pages.Count - 1; p >= 0 && Pages.Count > 1; p--)
        {
            if (Pages[p].All(s => s == null))
            {
                Pages.RemoveAt(p);
                removed.Add(p);
            }
        }
        removed.Reverse();
        return removed;
    }

    /// <summary>Writes the layout back to the persisted shape, dropping empty cells.</summary>
    public void SaveTo(OsConfig os)
    {
        os.Pages = Pages.Select(cells => new OsHomePage
        {
            Items = Enumerable.Range(0, Capacity)
                .Where(i => cells[i] != null)
                .Select(i => new OsPlacement { Id = cells[i]!, Row = i / Columns, Col = i % Columns })
                .ToList(),
        }).ToList();
        os.DockIds = Dock.ToList();
        os.LayoutColumns = Columns;
        os.LayoutRows = Rows;
    }

    /// <summary>Reads the persisted shape into slot arrays. Placements outside the current geometry, duplicated,
    /// or colliding are re-placed into the first free cell rather than dropped, so a geometry change never loses
    /// an icon. <paramref name="keep"/> filters ids that are no longer installed.</summary>
    public static HomeLayout FromConfig(OsConfig os, int rows, int columns, Func<string, bool> keep)
    {
        var layout = new HomeLayout(rows, columns);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var spill = new List<string>();

        foreach (var page in os.Pages)
        {
            var cells = layout.AddPage();
            foreach (var item in page.Items)
            {
                if (string.IsNullOrEmpty(item.Id) || !keep(item.Id) || !seen.Add(item.Id))
                {
                    continue;
                }
                var slot = item.Row * columns + item.Col;
                if (item.Col < 0 || item.Col >= columns || slot < 0 || slot >= layout.Capacity || cells[slot] != null)
                {
                    spill.Add(item.Id);
                    continue;
                }
                cells[slot] = item.Id;
            }
        }
        if (layout.Pages.Count == 0)
        {
            layout.AddPage();
        }
        foreach (var id in spill)
        {
            layout.PlaceInFirstFree(id);
        }
        return layout;
    }

    /// <summary>Converts a pre-2.1 flat icon list into pages, filling every cell in reading order.</summary>
    public static HomeLayout FromLegacyOrder(IEnumerable<string> order, int rows, int columns)
    {
        var layout = new HomeLayout(rows, columns);
        layout.AddPage();
        foreach (var id in order)
        {
            layout.PlaceInFirstFree(id);
        }
        return layout;
    }

    /// <summary>Round-trips the persisted layout through a mutation, for the discrete callers (folder create and
    /// remove, app eject, external-app removal) that run outside a draw frame and so have no live geometry. Uses
    /// the stamped geometry; a config with no stamp yet is left alone, since the next home frame converts it.</summary>
    public static void Edit(OsConfig os, Action<HomeLayout> mutate)
    {
        if (os.LayoutColumns <= 0 || os.LayoutRows <= 0)
        {
            return;
        }
        var layout = FromConfig(os, os.LayoutRows, os.LayoutColumns, _ => true);
        layout.Dock.AddRange(os.DockIds);
        mutate(layout);
        layout.SaveTo(os);
    }

    /// <summary>Clears a cell, leaving a hole rather than closing the gap.</summary>
    public static void RemoveFromConfig(OsConfig os, string id) => Edit(os, layout =>
    {
        layout.Remove(id);
        layout.Dock.Remove(id);
    });

    /// <summary>Puts an id in the first empty cell.</summary>
    public static void PlaceInConfig(OsConfig os, string id) => Edit(os, layout =>
    {
        if (!layout.TryFind(id, out _, out _) && !layout.Dock.Contains(id))
        {
            layout.PlaceInFirstFree(id);
        }
    });
}
