using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using AetherOS.Sdk;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace AetherLove.Os;

/// <summary>A home screen tile for another installed Dalamud plugin; opening it shows that plugin's main UI.</summary>
public sealed class ExternalApp : IAetherApp
{
    public const string IdPrefix = "ext:";

    private readonly string _internalName;
    private readonly string _displayName;
    private readonly Vector4 _tileTop;
    private readonly Vector4 _tileBottom;

    public ExternalApp(string internalName, string displayName)
    {
        _internalName = internalName;
        _displayName = displayName;
        var hue = (uint)internalName.GetHashCode() % 360u / 360f;
        _tileTop = FromHsv(hue, 0.62f, 0.85f);
        _tileBottom = FromHsv(hue, 0.72f, 0.45f);
    }

    private static readonly Dictionary<string, ISharedImmediateTexture?> IconCache = new();
    private static readonly HashSet<string> IconDownloads = new();
    private static readonly object IconLock = new();
    private static readonly HttpClient IconHttp = new() { Timeout = TimeSpan.FromSeconds(12) };

    /// <summary>The plugin's icon: a shipped icon file when present, otherwise the manifest's IconUrl
    /// downloaded once into a local cache (the same source the plugin installer uses).</summary>
    public ImTextureID? TileImage => ResolveIcon(_internalName)?.GetWrapOrDefault()?.Handle;

    private static ISharedImmediateTexture? ResolveIcon(string internalName)
    {
        lock (IconLock)
        {
            if (IconCache.TryGetValue(internalName, out var cached))
            {
                return cached;
            }
            if (IconDownloads.Contains(internalName))
            {
                return null;
            }
        }

        var path = FindLocalIcon(internalName);
        if (path != null)
        {
            var tex = UiHost.TextureProvider.GetFromFile(path);
            lock (IconLock)
            {
                IconCache[internalName] = tex;
            }
            return tex;
        }

        StartIconDownload(internalName);
        return null;
    }

    private static string? FindLocalIcon(string internalName)
    {
        try
        {
            var cachedFile = Path.Combine(IconCacheDir, internalName + ".png");
            if (File.Exists(cachedFile))
            {
                return cachedFile;
            }
            var versionDir = LatestVersionDir(internalName);
            if (versionDir == null)
            {
                return null;
            }
            return Directory.EnumerateFiles(versionDir, "icon.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Count(ch => ch == Path.DirectorySeparatorChar))
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, $"[AetherOS] Icon probe failed for {internalName}.");
            return null;
        }
    }

    private static void StartIconDownload(string internalName)
    {
        var candidates = new List<string>();
        try
        {
            var versionDir = LatestVersionDir(internalName);
            var manifest = versionDir == null ? null : Path.Combine(versionDir, internalName + ".json");
            string? channel = null;
            if (manifest != null && File.Exists(manifest))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
                if (doc.RootElement.TryGetProperty("IconUrl", out var iconProp)
                    && iconProp.GetString() is { Length: > 0 } iconUrl
                    && iconUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(iconUrl);
                }
                if (doc.RootElement.TryGetProperty("_Dip17Channel", out var channelProp))
                {
                    channel = channelProp.GetString();
                }
            }
            // Fallback to official dist repo (3rd-party plugins may 404).
            var channels = new List<string>();
            if (!string.IsNullOrEmpty(channel))
            {
                channels.Add(channel);
            }
            foreach (var fallback in new[] { "stable", "testing/live" })
            {
                if (!channels.Contains(fallback))
                {
                    channels.Add(fallback);
                }
            }
            foreach (var c in channels)
            {
                candidates.Add($"https://raw.githubusercontent.com/goatcorp/PluginDistD17/main/{c}/{internalName}/images/icon.png");
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, $"[AetherOS] Failed to read the manifest for {internalName}.");
        }

        if (candidates.Count == 0)
        {
            lock (IconLock)
            {
                IconCache[internalName] = null;
            }
            return;
        }

        lock (IconLock)
        {
            if (!IconDownloads.Add(internalName))
            {
                return;
            }
        }

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                foreach (var url in candidates)
                {
                    byte[] bytes;
                    try
                    {
                        bytes = await IconHttp.GetByteArrayAsync(url).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (!LooksLikeImage(bytes))
                    {
                        continue;
                    }
                    Directory.CreateDirectory(IconCacheDir);
                    var path = Path.Combine(IconCacheDir, internalName + ".png");
                    await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                    var tex = UiHost.TextureProvider.GetFromFile(path);
                    lock (IconLock)
                    {
                        IconCache[internalName] = tex;
                    }
                    return;
                }
                lock (IconLock)
                {
                    IconCache[internalName] = null;
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, $"[AetherOS] Icon download failed for {internalName}.");
                lock (IconLock)
                {
                    IconCache[internalName] = null;
                }
            }
            finally
            {
                lock (IconLock)
                {
                    IconDownloads.Remove(internalName);
                }
            }
        });
    }

    /// <summary>PNG, JPEG, GIF, or WEBP magic; rejects HTML error pages served with 200.</summary>
    private static bool LooksLikeImage(byte[] bytes)
    {
        if (bytes.Length < 12)
        {
            return false;
        }
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return true;
        }
        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return true;
        }
        if (bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F')
        {
            return true;
        }
        return bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';
    }

    private static string IconCacheDir =>
        Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "os_appicons");

    private static string? LatestVersionDir(string internalName)
    {
        var launcherRoot = Path.GetDirectoryName(Path.GetDirectoryName(UiHost.PluginInterface.ConfigDirectory.FullName));
        if (launcherRoot == null)
        {
            return null;
        }
        var pluginDir = Path.Combine(launcherRoot, "installedPlugins", internalName);
        if (!Directory.Exists(pluginDir))
        {
            return null;
        }
        return Directory.GetDirectories(pluginDir)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public string InternalName => _internalName;
    public string Id => IdPrefix + _internalName;
    public string Name => _displayName;
    public FontAwesomeIcon Icon => FontAwesomeIcon.PuzzlePiece;
    public Vector4 TileTop => _tileTop;
    public Vector4 TileBottom => _tileBottom;
    public int Badge => 0;
    public bool HasSurface => false;

    public void Open()
    {
        try
        {
            var plugin = UiHost.PluginInterface.InstalledPlugins
                .FirstOrDefault(p => p.InternalName == _internalName && p.IsLoaded);
            if (plugin is { HasMainUi: true })
            {
                plugin.OpenMainUi();
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, $"[AetherOS] Failed to open external app {_internalName}.");
        }
    }

    public void Draw(OsAppContext ctx)
    {
    }

    public void OnIntent(OsIntent intent)
    {
    }

    private static Vector4 FromHsv(float h, float s, float v)
    {
        var i = (int)(h * 6f) % 6;
        var f = h * 6f - (int)(h * 6f);
        var p = v * (1f - s);
        var q = v * (1f - f * s);
        var t = v * (1f - (1f - f) * s);
        return i switch
        {
            0 => new Vector4(v, t, p, 1f),
            1 => new Vector4(q, v, p, 1f),
            2 => new Vector4(p, v, t, 1f),
            3 => new Vector4(p, q, v, 1f),
            4 => new Vector4(t, p, v, 1f),
            _ => new Vector4(v, p, q, 1f),
        };
    }
}
