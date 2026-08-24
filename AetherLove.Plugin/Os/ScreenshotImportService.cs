using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AetherLove.Services.Localization;
using AetherLove.Windows;
using AetherOS.Apps.Photos;
using AetherOS.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace AetherLove.Os;

/// <summary>Watches the game's screenshot folder, plus the ReShade/GShade save folders when either overlay
/// is installed, and imports every new capture into a persistent "Printscreens" album in the photo library.
/// Only screenshots taken while the plugin runs are imported; existing folders are never back-scanned, and
/// nothing imports until the user has said yes: the first capture seen while the toggle was never set raises
/// a notification into the Photos settings instead of importing.</summary>
public sealed class ScreenshotImportService : IDisposable
{
    private const string AlbumKey = "screenshots_album";

    /// <summary>How recently a file must have been created to count as a capture. An old image renamed or
    /// moved into a watched folder keeps its original creation stamp and is not a screenshot.</summary>
    private static readonly TimeSpan FreshWindow = TimeSpan.FromSeconds(15);

    private readonly IServiceProvider _services;
    private readonly PhotoLibraryService _library;
    private readonly AppStorageService _storage;
    private readonly HashSet<string> _seen = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private volatile bool _disposed;
    private volatile bool _promptedThisSession;

    public ScreenshotImportService(IServiceProvider services, PhotoLibraryService library, AppStorageService storage)
    {
        _services = services;
        _library = library;
        _storage = storage;
        foreach (var (dir, recursive) in ResolveWatchDirs())
        {
            try
            {
                Directory.CreateDirectory(dir);
                var watcher = new FileSystemWatcher(dir)
                {
                    IncludeSubdirectories = recursive,
                };
                var root = dir;
                watcher.Created += (_, e) =>
                {
                    if (PhonePower.IsOn)
                    {
                        HandleFile(root, e.FullPath);
                    }
                };
                watcher.Renamed += (_, e) =>
                {
                    if (PhonePower.IsOn)
                    {
                        HandleFile(root, e.FullPath);
                    }
                };
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[Screenshots] Could not watch a screenshot folder. Dir={Dir}", dir);
            }
        }
        if (_watchers.Count > 0)
        {
            Plugin.Log.Information("[Screenshots] Watching {Count} screenshot folder(s).", _watchers.Count);
        }

        // The file picker's quick links get the game's screenshot folder from here, because this
        // is the one service that already knows where it is.
        var screenshotDir = ResolveGameScreenshotDir();
        if (Directory.Exists(screenshotDir)
            && !AetherLove.Widgets.AetherFileDialogManager.ExtraQuickLinks.Any(l => l.Path == screenshotDir))
        {
            AetherLove.Widgets.AetherFileDialogManager.ExtraQuickLinks.Add(
                ("picker.place_screenshots", screenshotDir, Dalamud.Interface.FontAwesomeIcon.Camera));
        }
    }

    /// <summary>The game's own folder plus any ReShade/GShade save path configured next to the game
    /// executable. Overlay naming templates may create per-game subfolders, so those watch recursively.</summary>
    private static IReadOnlyList<(string Dir, bool Recursive)> ResolveWatchDirs()
    {
        var dirs = new List<(string, bool)>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? dir, bool recursive)
        {
            if (dir is not { Length: > 0 })
            {
                return;
            }
            var normalized = dir.TrimEnd('\\', '/');
            if (keys.Add(normalized))
            {
                dirs.Add((normalized, recursive));
            }
        }

        Add(ResolveGameScreenshotDir(), false);
        try
        {
            var gameDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (gameDir is { Length: > 0 })
            {
                Add(ReadOverlaySavePath(Path.Combine(gameDir, "ReShade.ini"), gameDir), true);
                Add(ReadOverlaySavePath(Path.Combine(gameDir, "GShade.ini"), gameDir), true);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Screenshots] Could not probe for overlay screenshot folders.");
        }
        return dirs;
    }

    private static string ResolveGameScreenshotDir()
    {
        try
        {
            if (Plugin.GameConfig.TryGet(Dalamud.Game.Config.SystemConfigOption.ScreenShotDir, out string dir)
                && dir.Length > 0)
            {
                return dir;
            }
        }
        catch (Exception)
        {
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "FINAL FANTASY XIV - A Realm Reborn", "screenshots");
    }

    /// <summary>Reads the screenshot folder from a ReShade-family ini: [SCREENSHOT] SavePath on current
    /// builds, the legacy [GENERAL] ScreenshotPath key as a fallback. Null when the ini or a usable path is
    /// absent (an empty SavePath means the game folder, which is deliberately not watched).</summary>
    private static string? ReadOverlaySavePath(string iniPath, string baseDir)
    {
        try
        {
            if (!File.Exists(iniPath))
            {
                return null;
            }
            string? section = null;
            string? fallback = null;
            foreach (var raw in File.ReadLines(iniPath))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    section = line;
                    continue;
                }
                var eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                if (value.Length == 0)
                {
                    continue;
                }
                if (key.Equals("SavePath", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(section, "[SCREENSHOT]", StringComparison.OrdinalIgnoreCase))
                {
                    return ResolvePath(value, baseDir);
                }
                if (key.Equals("ScreenshotPath", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(section, "[GENERAL]", StringComparison.OrdinalIgnoreCase))
                {
                    fallback ??= value;
                }
            }
            return fallback == null ? null : ResolvePath(fallback, baseDir);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Screenshots] Failed to read an overlay ini. Path={Path}", iniPath);
            return null;
        }
    }

    private static string ResolvePath(string path, string baseDir)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path));
    }

    private void HandleFile(string root, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp"))
        {
            return;
        }
        if (!IsWithinDepth(root, path) || !IsFresh(path))
        {
            return;
        }
        // Read per file and before the _seen add: a shot that arrived while this was off must stay importable.
        var enabled = PhotoScope.Get<bool?>(PhotoSettings.AutoImportScreenshots);
        if (enabled == null)
        {
            PromptForConsent();
            return;
        }
        if (enabled == false)
        {
            return;
        }
        lock (_seen)
        {
            if (_seen.Count > 512)
            {
                _seen.Clear();
            }
            if (!_seen.Add(path))
            {
                return;
            }
        }
        _ = ImportWhenReadyAsync(path);
    }

    /// <summary>Overlay naming templates create at most one per-game subfolder, so anything deeper under a
    /// recursively-watched save path is somebody's unrelated file tree, not a capture.</summary>
    private static bool IsWithinDepth(string root, string path)
    {
        try
        {
            var relativeDir = Path.GetDirectoryName(Path.GetRelativePath(root, path));
            if (relativeDir is not { Length: > 0 })
            {
                return true;
            }
            return !relativeDir.Contains(Path.DirectorySeparatorChar)
                && !relativeDir.Contains(Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsFresh(string path)
    {
        try
        {
            var age = DateTime.UtcNow - File.GetCreationTimeUtc(path);
            return age > -FreshWindow && age < FreshWindow;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The toggle has never been set, so nothing imports; instead a capture raises (at most once a
    /// session, replacing by tag) a notification that lands on the Photos settings, where saying yes or no
    /// makes the choice explicit.</summary>
    private void PromptForConsent()
    {
        if (_promptedThisSession)
        {
            return;
        }
        _promptedThisSession = true;
        _ = Plugin.Framework.RunOnFrameworkThread(() =>
        {
            try
            {
                _services.GetService<AetherOS.Sdk.IOsShell>()?.PostNotification(
                    "photos", Loc.T("os.app_photos"), Loc.T("os.photos_screenshots_offer"),
                    onTap: () => _services.GetService<MainPluginWindow>()?.OpenToPhotoSettings(),
                    tag: "photos:screenshots");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[Screenshots] Consent notification failed.");
            }
        });
    }

    /// <summary>The Created event fires while the capturer is still writing, so poll until the file opens
    /// exclusively before importing on the framework thread.</summary>
    private async Task ImportWhenReadyAsync(string path)
    {
        try
        {
            for (var attempt = 0; attempt < 20 && !_disposed; attempt++)
            {
                await Task.Delay(300).ConfigureAwait(false);
                if (!IsReadable(path))
                {
                    continue;
                }
                await Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    _library.AddPhoto(EnsureAlbum(), path, null);
                }).ConfigureAwait(false);
                return;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Screenshots] Failed to import a screenshot.");
        }
    }

    private static bool IsReadable(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return fs.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private IAppStorage PhotoScope => _storage.For(PhotoSettings.ScopeId);

    private string EnsureAlbum()
    {
        var storage = PhotoScope;
        var id = storage.Get<string>(AlbumKey);
        if (id is { Length: > 0 })
        {
            foreach (var album in _library.Albums)
            {
                if (album.Id == id)
                {
                    return id;
                }
            }
        }
        id = _library.CreateAlbum(Loc.T("os.screenshots_album"));
        storage.Set(AlbumKey, id);
        return id;
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
        _watchers.Clear();
    }
}
