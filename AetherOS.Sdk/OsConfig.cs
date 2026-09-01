using System;
using System.Collections.Generic;

namespace AetherOS.Sdk;

public enum WallpaperMode
{
    ThemeGradient = 0,
    BuiltIn = 1,
    Custom = 2,

    /// <summary>The wallpaper that ships with a purchased theme; the image lives sealed, not on disk.</summary>
    Premium = 3,
}

/// <summary>Home grid density, named after common Android launcher presets. Columns and tile sizing come
/// from the preset; rows always auto-fit the phone height, and icons reflow automatically on change.</summary>
public enum HomeGridPreset
{
    Standard = 0,
    Comfortable = 1,
    Compact = 2,
    Dense = 3,
}

/// <summary>A named home-screen folder holding app ids; renders as an iOS-style tile with a mini icon grid.
/// Its <see cref="Id"/> is placed on a home page or docked like an app id.</summary>
public class OsFolder
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> AppIds { get; set; } = new();
}

/// <summary>One icon pinned to a specific home-grid cell. Cells with no placement stay empty.</summary>
public class OsPlacement
{
    public string Id { get; set; } = string.Empty;
    public int Row { get; set; }
    public int Col { get; set; }
}

/// <summary>One home page of placed icons. A page with no items is dropped by the home screen.</summary>
public class OsHomePage
{
    public List<OsPlacement> Items { get; set; } = new();
}

/// <summary>OS-level appearance and home screen layout, persisted by the host.</summary>
public class OsConfig
{
    public WallpaperMode WallpaperMode { get; set; } = WallpaperMode.ThemeGradient;

    /// <summary>File name of the selected built-in wallpaper (inside the host's wallpaper folder).</summary>
    public string BuiltInWallpaper { get; set; } = string.Empty;

    /// <summary>Absolute path of the user-uploaded wallpaper copy.</summary>
    public string CustomWallpaperPath { get; set; } = string.Empty;

    /// <summary>Which purchased theme's wallpaper is in use, when the mode is Premium.</summary>
    public Guid PremiumWallpaperProductId { get; set; }

    /// <summary>0..0.98 dark overlay on image wallpapers; the top end blacks the wallpaper out almost entirely.</summary>
    public float WallpaperDim { get; set; } = 0.25f;

    public HomeGridPreset HomeGrid { get; set; } = HomeGridPreset.Standard;

    /// <summary>The phone UI font family id (see UiFonts.Families); "default" is the built-in Noto.</summary>
    public string FontFamily { get; set; } = "default";

    /// <summary>Home screen folders; their ids are placed on a home page or docked like app ids.</summary>
    public List<OsFolder> Folders { get; set; } = new();

    /// <summary>Home pages of placed icons. Empty means the layout still lives in the retired
    /// <see cref="IconOrder"/> and is converted on the first home frame.</summary>
    public List<OsHomePage> Pages { get; set; } = new();

    /// <summary>Grid geometry the placements were authored at. A mismatch repacks them to fit.</summary>
    public int LayoutColumns { get; set; }

    public int LayoutRows { get; set; }

    /// <summary>Retired in favour of <see cref="Pages"/>; kept so a pre-2.1 config still converts. Emptied
    /// once converted.</summary>
    public List<string> IconOrder { get; set; } = new();

    /// <summary>App ids pinned to the dock, left to right (max 4).</summary>
    public List<string> DockIds { get; set; } = new();

    /// <summary>InternalNames of other Dalamud plugins pinned to the home screen.</summary>
    public List<string> ExternalApps { get; set; } = new();

    /// <summary>Whether the grid is pinned: tiles still open, and every other verb in the right-click
    /// menu still works, but nothing can be dragged out of its cell, its folder or the dock.</summary>
    public bool IconsLocked { get; set; }

    /// <summary>Ids of built-in apps the user removed from the home screen. They stay registered so deep links
    /// keep working, but they own no tile, widget, share entry, badge or notification until they are added back.</summary>
    public List<string> RemovedApps { get; set; } = new();

    /// <summary>Server-bar toggles the player switched OFF: an app id silences every entry that app
    /// owns, an "appId/entryId" key silences one line. Absence means on, so a newly registered entry
    /// defaults to visible (ADR 21).</summary>
    public List<string> ServerBarDisabled { get; set; } = new();

    /// <summary>App ids whose pre-capability "show on the bar" switch has been carried into
    /// <see cref="ServerBarDisabled"/> once; after that the central store rules.</summary>
    public List<string> ServerBarSeeded { get; set; } = new();

    /// <summary>Skips the confirm popup when removing an app.</summary>
    public bool SkipRemoveAppConfirm { get; set; }

    /// <summary>Apps whose home-tile "new" badge has been dismissed by opening them once.</summary>
    public List<string> SeenNewApps { get; set; } = new();

    /// <summary>Apps the player has already answered for, whether they took them or not. An app that has
    /// never been placed and is not in here is one an update has just brought along, which is what the
    /// new-app offer asks about; answering, either way, puts it here for good.</summary>
    public List<string> OfferedApps { get; set; } = new();

    /// <summary>The Media folder seed has been considered. It is a one-shot decision rather than a repeated
    /// check, so a folder the user deleted never returns and a user who already had one of its apps is never
    /// reorganised later.</summary>
    public bool MediaFolderSeeded { get; set; }

    /// <summary>The Utilities folder seed has been considered. Same one-shot posture as
    /// <see cref="MediaFolderSeeded"/>.</summary>
    public bool UtilitiesFolderSeeded { get; set; }

    /// <summary>The guided OS tour ran (or was skipped); it auto-starts once on the first Home landing.</summary>
    public bool TourSeen { get; set; }

    /// <summary>The battery has run flat at least once. Gates the opt-out below, so nobody is offered a way to
    /// hide a joke they have not seen yet.</summary>
    public bool BatteryEmptySeen { get; set; }

    /// <summary>Opted out of the empty-battery prompt, which also stops the battery draining past its floor.</summary>
    public bool HideBatteryGrassPrompt { get; set; }
}
