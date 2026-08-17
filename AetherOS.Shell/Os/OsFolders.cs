using System.Collections.Generic;
using System.Linq;
using AetherLove.Services.Localization;
using AetherOS.Sdk;

namespace AetherLove.Os;

/// <summary>The folders the OS ships, and only as one-time seeds: Media and Utilities are ordinary user
/// folders the moment they exist, and the decision to seed them is latched so one the user renames, empties
/// or deletes is never rebuilt. Nothing here owns a folder afterwards.
///
/// <para>Arcade used to be an exception that gathered every game every frame. It is gone: adopting on
/// folder membership meant a game moved into a folder of the player's own was stolen straight back, and an
/// Arcade folder would spring into existence to hold it. An Arcade folder somebody already has is a normal
/// folder now, renameable and deletable, and keeps only its tile art.</para></summary>
internal static class OsFolders
{
    public const string ArcadeId = IOsShell.ArcadeFolderId;

    private const string MediaId = "folder:media";
    private const string UtilitiesId = "folder:utilities";

    /// <summary>What the Media folder is seeded with. Only ever read once, by <see cref="EnsureMedia"/>.</summary>
    private static readonly string[] MediaAppIds = ["groove", "echo"];

    /// <summary>What the Utilities folder is seeded with. Only ever read once, by <see cref="EnsureUtilities"/>.</summary>
    private static readonly string[] UtilitiesAppIds = ["notes", "calculator", "timers"];

    public static string DisplayName(OsFolder folder) => folder.Name;

    /// <summary>Gives an Arcade folder somebody already has a name, if it never got one. It used to draw
    /// shipped tile art instead of the stacked mini-icons every other folder wears, and an unnamed folder
    /// behind a picture reads fine right up until the picture goes. True when the config changed.</summary>
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
