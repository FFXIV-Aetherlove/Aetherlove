using System.Collections.Generic;

namespace AetherOS.Sdk;

public enum WallpaperMode
{
    ThemeGradient = 0,
    BuiltIn = 1,
    Custom = 2,
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

    /// <summary>Skips the confirm popup when removing a pinned external app.</summary>
    public bool SkipRemoveAppConfirm { get; set; }

    /// <summary>Apps whose home-tile "new" badge has been dismissed by opening them once.</summary>
    public List<string> SeenNewApps { get; set; } = new();

    /// <summary>The guided OS tour ran (or was skipped); it auto-starts once on the first Home landing.</summary>
    public bool TourSeen { get; set; }

    /// <summary>The one-time "Welcome to AetherLove 2.0" splash (shown before the guided tour) was dismissed.</summary>
    public bool Welcome20Seen { get; set; }

    /// <summary>The battery has run flat at least once. Gates the opt-out below, so nobody is offered a way to
    /// hide a joke they have not seen yet.</summary>
    public bool BatteryEmptySeen { get; set; }

    /// <summary>Opted out of the empty-battery prompt, which also stops the battery draining past its floor.</summary>
    public bool HideBatteryGrassPrompt { get; set; }
}
