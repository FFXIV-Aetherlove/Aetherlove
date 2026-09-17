using System.Collections.Generic;
using System.Linq;
using AetherLove.Services.Localization;
using AetherOS.Sdk;

namespace AetherLove.Os;

/// <summary>The folders the OS seeds for an untouched home layout. Every seeded folder is an ordinary user
/// folder the moment it exists, and nothing here owns or rebuilds it afterwards.</summary>
internal static class OsFolders
{
    public const string ArcadeId = IOsShell.ArcadeFolderId;

    private const string MediaId = "folder:media";
    private const string UtilitiesId = "folder:utilities";

    private static readonly string[] ArcadeAppIds =
        ["snake", "stacker", "breaker", "meteor", "invaders", "muncher", "plappy", "doom", "sudoku",
         "racooner", "skyswarm", "eordle"];

    /// <summary>What the Media folder is seeded with. Only ever read once, by <see cref="EnsureMedia"/>.</summary>
    private static readonly string[] MediaAppIds = ["groove", "echo"];

    /// <summary>What the Utilities folder is seeded with. Only ever read once, by <see cref="EnsureUtilities"/>.</summary>
    private static readonly string[] UtilitiesAppIds = ["notes", "calculator", "timers"];

    public static string DisplayName(OsFolder folder) => folder.Name;

    /// <summary>Adds the starter Arcade folder while the home layout is still empty. The caller owns the
    /// fresh-layout check; after this seed the folder has exactly the same lifecycle as one made by the user.</summary>
    public static void SeedArcade(OsConfig os)
    {
        var folder = new OsFolder { Id = ArcadeId, Name = Loc.T("os.folder_arcade") };
        folder.AppIds.AddRange(ArcadeAppIds);
        os.Folders.Add(folder);
    }

    public static bool NameArcade(OsConfig os)
    {
        if (os.Folders.FirstOrDefault(f => f.Id == ArcadeId) is not { } arcade
            || !string.IsNullOrWhiteSpace(arcade.Name))
        {
            return false;
        }
        arcade.Name = Loc.T("os.folder_arcade");
        return true;
    }

    /// <summary>Seeds the Media folder with Groove and Echo, once, and only for someone meeting BOTH of them
    /// for the first time: anyone who already has one placed keeps their own arrangement. The decision is
    /// latched, so one the user renames, empties or deletes is never rebuilt. True when the config changed.</summary>
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

    /// <summary>Seeds the Utilities folder with Notes and the calculator, once, on the same terms as
    /// <see cref="EnsureMedia"/>: a plain user folder, the decision latched so one the user renames, empties or
    /// deletes is never rebuilt, and skipped entirely for anyone who has already placed or removed either app.
    /// True when the config changed.</summary>
    public static bool EnsureUtilities(OsConfig os)
    {
        if (os.UtilitiesFolderSeeded)
        {
            return false;
        }
        os.UtilitiesFolderSeeded = true;
        if (UtilitiesAppIds.Any(id => os.RemovedApps.Contains(id) || IsPlaced(os, id)))
        {
            return true;
        }

        var folder = new OsFolder { Id = UtilitiesId, Name = Loc.T("os.folder_utilities") };
        folder.AppIds.AddRange(UtilitiesAppIds);
        os.Folders.Add(folder);
        HomeLayout.PlaceInConfig(os, UtilitiesId);
        return true;
    }

    /// <summary>Drops every folder with nothing left in it and takes its tile off the grid
    /// and the dock. Runs before the seeds each home frame, so emptying a folder by any route (dragging the
    /// last app out, taking it out from the folder page, removing the app outright) makes the folder go away
    /// on its own rather than leaving a tile that opens onto nothing. <paramref name="spare"/> is the folder
    /// just made by hand, which has to survive being empty long enough to be filled. True when the config
    /// changed.</summary>
    public static bool PruneEmpty(OsConfig os, string? spare = null)
    {
        var empty = os.Folders.Where(f => f.AppIds.Count == 0 && f.Id != spare).ToList();
        foreach (var folder in empty)
        {
            os.Folders.Remove(folder);
            HomeLayout.RemoveFromConfig(os, folder.Id);
            os.DockIds.Remove(folder.Id);
        }
        return empty.Count > 0;
    }

    /// <summary>Whether the user's layout already accounts for this app anywhere: a cell, the dock, a folder,
    /// or a pre-2.1 order still awaiting conversion.</summary>
    internal static bool IsPlaced(OsConfig os, string id) =>
        os.DockIds.Contains(id)
        || os.IconOrder.Contains(id)
        || os.Pages.Any(page => page.Items.Any(item => item.Id == id))
        || os.Folders.Any(folder => folder.AppIds.Contains(id));
}
