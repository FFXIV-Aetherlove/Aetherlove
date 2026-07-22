using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AetherOS.Sdk;
using Dalamud.Interface.Textures;

namespace AetherLove.Os;

/// <summary>Built-in and user-uploaded home screen wallpapers. Purely local.</summary>
public sealed class WallpaperService
{
    private readonly Dictionary<string, ISharedImmediateTexture> _cache = new();
    private string[]? _builtIns;

    private static readonly string[] AllowedExtensions = [".png", ".jpg", ".jpeg"];

    private static string MediaDir =>
        Path.Combine(Path.GetDirectoryName(UiHost.PluginInterface.AssemblyLocation.FullName) ?? "", "Media", "wallpapers");

    public IReadOnlyList<string> BuiltIns
    {
        get
        {
            if (_builtIns == null)
            {
                try
                {
                    _builtIns = Directory.Exists(MediaDir)
                        ? Directory.GetFiles(MediaDir)
                            .Where(f => AllowedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            .Select(Path.GetFileName)
                            .OfType<string>()
                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                            .ToArray()
                        : [];
                }
                catch (Exception ex)
                {
                    UiHost.Log.Warning(ex, "[Wallpapers] Failed to scan built-ins.");
                    _builtIns = [];
                }
            }
            return _builtIns;
        }
    }

    public ISharedImmediateTexture? Current()
    {
        var os = UiHost.Configuration.Os;
        return os.WallpaperMode switch
        {
            WallpaperMode.BuiltIn when os.BuiltInWallpaper.Length > 0 =>
                GetTexture(Path.Combine(MediaDir, os.BuiltInWallpaper)),
            WallpaperMode.Custom when os.CustomWallpaperPath.Length > 0 && File.Exists(os.CustomWallpaperPath) =>
                GetTexture(os.CustomWallpaperPath),
            _ => null,
        };
    }

    public ISharedImmediateTexture? GetBuiltInTexture(string fileName) => GetTexture(Path.Combine(MediaDir, fileName));

    public ISharedImmediateTexture? GetTexture(string absPath)
    {
        if (_cache.TryGetValue(absPath, out var tex))
        {
            return tex;
        }
        try
        {
            tex = UiHost.TextureProvider.GetFromFile(absPath);
            _cache[absPath] = tex;
            return tex;
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Wallpapers] Failed to load {Path}.", absPath);
            return null;
        }
    }

    public void SelectGradient()
    {
        UiHost.Configuration.Os.WallpaperMode = WallpaperMode.ThemeGradient;
        UiHost.Configuration.Save();
    }

    public void SelectBuiltIn(string fileName)
    {
        UiHost.Configuration.Os.WallpaperMode = WallpaperMode.BuiltIn;
        UiHost.Configuration.Os.BuiltInWallpaper = fileName;
        UiHost.Configuration.Save();
    }

    /// <summary>Quick-toggle in the shade: steps through the theme gradient and every built-in, so slot 0 is the
    /// theme background and the built-ins follow. A custom wallpaper isn't in the ring; from it the next tap
    /// lands on the theme.</summary>
    public void CycleWallpaper()
    {
        var list = BuiltIns.ToList();
        var os = UiHost.Configuration.Os;
        // Ring positions: 0 = theme gradient, 1..N = built-ins.
        var cur = os.WallpaperMode switch
        {
            WallpaperMode.ThemeGradient => 0,
            WallpaperMode.BuiltIn when list.IndexOf(os.BuiltInWallpaper) is var i and >= 0 => i + 1,
            _ => -1,
        };
        var next = (cur + 1) % (list.Count + 1);
        if (next == 0)
        {
            SelectGradient();
        }
        else
        {
            SelectBuiltIn(list[next - 1]);
        }
    }

    /// <summary>Copies a user-picked image into the plugin config directory and selects it.</summary>
    public bool ApplyCustomFromFile(string sourcePath)
    {
        try
        {
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext) || !File.Exists(sourcePath))
            {
                return false;
            }

            var dir = UiHost.PluginInterface.GetPluginConfigDirectory();
            // Unique name so the texture provider's per-path cache never serves the previous upload.
            var dest = Path.Combine(dir, $"os_wallpaper_{DateTime.UtcNow.Ticks}{ext}");
            File.Copy(sourcePath, dest, overwrite: true);

            var old = UiHost.Configuration.Os.CustomWallpaperPath;
            UiHost.Configuration.Os.WallpaperMode = WallpaperMode.Custom;
            UiHost.Configuration.Os.CustomWallpaperPath = dest;
            UiHost.Configuration.Save();

            if (old.Length > 0 && old != dest && File.Exists(old))
            {
                try
                {
                    File.Delete(old);
                }
                catch (IOException)
                {
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Wallpapers] Failed to apply custom wallpaper.");
            return false;
        }
    }

    public void RemoveCustom()
    {
        var os = UiHost.Configuration.Os;
        var old = os.CustomWallpaperPath;
        os.CustomWallpaperPath = string.Empty;
        if (os.WallpaperMode == WallpaperMode.Custom)
        {
            os.WallpaperMode = WallpaperMode.ThemeGradient;
        }
        UiHost.Configuration.Save();
        if (old.Length > 0 && File.Exists(old))
        {
            try
            {
                File.Delete(old);
            }
            catch (IOException)
            {
            }
        }
    }
}
