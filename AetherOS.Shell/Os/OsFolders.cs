using System.Collections.Generic;
using System.Linq;
using AetherLove.Services.Localization;
using AetherLove.UI;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Os;

/// <summary>The one folder the OS owns rather than the user: Arcade. It always exists, always holds every
/// arcade app, and cannot be renamed, emptied or deleted, so the handhelds never sprawl across the grid.</summary>
internal static class OsFolders
{
    public const string ArcadeId = IOsShell.ArcadeFolderId;

    private const string LegacyArcadeId = "folder:games";
    private const string ArcadeIconName = "arcade";

    /// <summary>Every arcade app. Adding a game here is what moves it into the folder on the next home frame.</summary>
    private static readonly string[] GameAppIds =
        ["snake", "stacker", "breaker", "meteor", "invaders", "muncher"];

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
        var adopt = GameAppIds.Where(id => folder == null || !folder.AppIds.Contains(id)).ToList();
        if (folder != null && adopt.Count == 0)
        {
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
