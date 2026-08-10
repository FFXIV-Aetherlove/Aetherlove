using System.Collections.Generic;
using System.Linq;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Os;

/// <summary>The one folder the OS owns rather than the user: Arcade. It always exists, always holds every
/// arcade app, and cannot be renamed, emptied or deleted, so the handhelds never sprawl across the grid.
/// Media is the other folder shipped here, but only as a one-time seed: it is an ordinary user folder the
/// moment it exists.</summary>
internal static class OsFolders
{
    public const string ArcadeId = IOsShell.ArcadeFolderId;

    private const string LegacyArcadeId = "folder:games";
    private const string ArcadeIconName = "arcade";
    private const string MediaId = "folder:media";

    /// <summary>Every arcade app. Adding a game here is what moves it into the folder on the next home frame.</summary>
    private static readonly string[] GameAppIds =
        ["snake", "stacker", "breaker", "meteor", "invaders", "muncher", "plappy", "doom", "sudoku"];

    /// <summary>What the Media folder is seeded with. Only ever read once, by <see cref="EnsureMedia"/>.</summary>
    private static readonly string[] MediaAppIds = ["groove", "echo"];

    public static bool IsBuiltIn(string id) => id == ArcadeId;

    /// <summary>The built-in folder's name follows the OS language rather than the stored string, which only
    /// user-made folders own.</summary>
    public static string DisplayName(OsFolder folder) =>
        IsBuiltIn(folder.Id) ? Loc.T("os.folder_arcade") : folder.Name;

    /// <summary>The built-in folder's own tile art, when <c>Media/appicons/arcade.png</c> ships; null falls the
    /// caller back to the stacked mini-icons every user folder draws.</summary>
    public static ImTextureID? TileIcon(OsFolder folder) =>
        IsBuiltIn(folder.Id) ? AppIcons.Tile(ArcadeIconName) : null;

    /// <summary>Creates the Arcade folder when it is missing and adopts any arcade app still sitting loose, so a
    /// newly shipped game lands inside it instead of on someone's home screen. True when the config changed.</summary>
    public static bool EnsureArcade(OsConfig os)
    {
        var changed = MigrateLegacyId(os);
        var folder = os.Folders.FirstOrDefault(f => f.Id == ArcadeId);
        var adopt = GameAppIds
            .Where(id => !os.RemovedApps.Contains(id))
            .Where(id => folder == null || !folder.AppIds.Contains(id))
            .ToList();
        if (folder != null && adopt.Count == 0)
        {
            // Every game removed leaves an empty folder nobody can delete, so the OS takes its own tile away.
            if (folder.AppIds.Count == 0)
            {
                os.Folders.Remove(folder);
                HomeLayout.RemoveFromConfig(os, ArcadeId);
                os.DockIds.Remove(ArcadeId);
                return true;
            }
            return changed;
        }
        if (folder == null)
        {
            folder = new OsFolder { Id = ArcadeId, Name = Loc.T("os.folder_arcade") };
            os.Folders.Add(folder);
        }
        folder.AppIds.AddRange(adopt);

        HomeLayout.Edit(os, layout =>
        {
            (int Page, int Slot)? vacated = null;
            foreach (var id in folder.AppIds)
            {
                if (layout.TryFind(id, out var page, out var slot))
                {
                    if (vacated is not { } best || page < best.Page || (page == best.Page && slot < best.Slot))
                    {
                        vacated = (page, slot);
                    }
                    layout.Remove(id);
                }
                layout.Dock.Remove(id);
            }
            if (layout.TryFind(ArcadeId, out _, out _) || layout.Dock.Contains(ArcadeId))
            {
                return;
            }
            // The folder inherits the first cell its games gave up, so it appears where they were.
            if (vacated is { } spot)
            {
                layout.Pages[spot.Page][spot.Slot] = ArcadeId;
                return;
            }
            layout.PlaceInFirstFree(ArcadeId);
        });
        return true;
    }

    /// <summary>Seeds the Media folder with Groove and Echo, once, and only for someone meeting BOTH of them
    /// for the first time: anyone who already has one placed keeps their own arrangement. Unlike Arcade this
    /// is a plain user folder from the moment it exists, and the decision is latched, so one the user renames,
    /// empties or deletes is never rebuilt. True when the config changed.</summary>
    public static bool EnsureMedia(OsConfig os)
    {
        if (os.MediaFolderSeeded)
        {
            return false;
        }
        os.MediaFolderSeeded = true;
        if (MediaAppIds.Any(id => os.RemovedApps.Contains(id) || IsPlaced(os, id)))
        {
            return true;
        }

        var folder = new OsFolder { Id = MediaId, Name = Loc.T("os.folder_media") };
        folder.AppIds.AddRange(MediaAppIds);
        os.Folders.Add(folder);
        HomeLayout.PlaceInConfig(os, MediaId);
        return true;
    }

    /// <summary>Whether the user's layout already accounts for this app anywhere: a cell, the dock, a folder,
    /// or a pre-2.1 order still awaiting conversion.</summary>
    private static bool IsPlaced(OsConfig os, string id) =>
        os.DockIds.Contains(id)
        || os.IconOrder.Contains(id)
        || os.Pages.Any(page => page.Items.Any(item => item.Id == id))
        || os.Folders.Any(folder => folder.AppIds.Contains(id));

    /// <summary>The folder shipped briefly as "Games"; re-point that id in place rather than leave a stale user
    /// folder behind claiming the same apps.</summary>
    private static bool MigrateLegacyId(OsConfig os)
    {
        if (os.Folders.Any(f => f.Id == ArcadeId)
            || os.Folders.FirstOrDefault(f => f.Id == LegacyArcadeId) is not { } legacy)
        {
            return false;
        }
        legacy.Id = ArcadeId;
        HomeLayout.Edit(os, layout =>
        {
            if (layout.TryFind(LegacyArcadeId, out var page, out var slot))
            {
                layout.Pages[page][slot] = ArcadeId;
            }
            var dockAt = layout.Dock.IndexOf(LegacyArcadeId);
            if (dockAt >= 0)
            {
                layout.Dock[dockAt] = ArcadeId;
            }
        });
        return true;
    }
}
