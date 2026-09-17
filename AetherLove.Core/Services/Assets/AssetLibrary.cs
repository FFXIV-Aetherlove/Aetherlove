using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Config;
using AetherLove.Services.Media;
using AetherLove.Shared.Assets;
using Dalamud.Plugin.Services;

namespace AetherLove.Services.Assets;

/// <summary>The plugin's copy of the server's asset collection under <c>ConfigDirectory/assets/</c>, plus
/// the HTTP side that fetches it. Files are written beside their final name and moved into place, pack zips
/// download into a scratch file that survives a broken transfer, and only the file types the phone draws or
/// plays may land here, so nothing DRM-shaped can be dropped where the wallpaper scanner reads.</summary>
public sealed class AssetLibrary : IAssetStore, IAssetSource
{
    public const string ManifestFile = "collection.json";
    private const string ScratchFolder = ".scratch";
    private const string PartSuffix = ".part";
    private const string ScratchZipSuffix = ".zip.part";
    private const string ScratchHashSuffix = ".zip.sha256";
    private const string LegacyMusicFolder = "BGM";
    private const int FileBuffer = 64 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".ogg", ".wav", ".json", ".wad", ".sf2", ".txt",
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IPluginLog _log;
    private readonly HttpClient _http;
    private readonly Configuration _config;
    private readonly Echo.EchoHostInstaller _echoInstaller;

    public AssetLibrary(IPluginLog log, HttpClient http, Configuration config, Echo.EchoHostInstaller echoInstaller)
    {
        _log = log;
        _http = http;
        _config = config;
        _echoInstaller = echoInstaller;
    }

    public string Root => MediaPaths.DownloadedRoot;

    private string ManifestPath => Path.Combine(Root, ManifestFile);

    private string ScratchDir => Path.Combine(Root, ScratchFolder);

    /// <summary>The manifest of the last completed sync, or null when there is none or it does not parse.</summary>
    public AssetManifestDto? ReadLocalManifest()
    {
        var path = ManifestPath;
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<AssetManifestDto>(File.ReadAllText(path), Json);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[Assets] The local collection manifest could not be read; the next sync rescans.");
            return null;
        }
    }

    /// <summary>Whether the folder a files pack is derived from holds anything: a top-level folder keeps its
    /// own name (<c>racing-cards</c> is <c>racing-cards/</c>), and a split pack's hyphen is the separator
    /// (<c>unknown-racer</c> is <c>unknown/racer</c>).</summary>
    public bool HasPackFolder(string pack)
    {
        if (!AssetManifest.IsValidPackName(pack))
        {
            return false;
        }
        try
        {
            return HoldsFiles(Path.Combine(Root, pack))
                || HoldsFiles(Path.Combine(Root, pack.Replace('-', Path.DirectorySeparatorChar)));
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool HoldsFiles(string folder) => Directory.Exists(folder) && Directory.EnumerateFiles(folder).Any();

    /// <summary>Deletes every downloaded file, the local manifest and the download scratch, then the folders
    /// left empty. A file the game still holds open (a playing track, a loaded sound) is skipped rather than
    /// failing the whole reset; the next sync hashes it and keeps it when it still matches. A store that is a
    /// link to a shared folder (the UI test harness junctions one in) loses only the link, never the files
    /// behind it. Returns how many files were skipped.</summary>
    public int DeleteAll()
    {
        if (!Directory.Exists(Root))
        {
            return 0;
        }
        if (new DirectoryInfo(Root).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            Directory.Delete(Root);
            return 0;
        }
        var walk = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var skipped = 0;
        foreach (var file in Directory.EnumerateFiles(Root, "*", walk).ToArray())
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                _log.Warning($"[Assets] Could not delete {Path.GetRelativePath(Root, file)}: {ex.Message}");
            }
        }
        foreach (var folder in Directory.EnumerateDirectories(Root, "*", walk).OrderByDescending(d => d.Length).ToArray())
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warning($"[Assets] Could not remove the folder {Path.GetRelativePath(Root, folder)}: {ex.Message}");
            }
        }
        return skipped;
    }

    /// <summary>Moves the tracks of the pre-2.7 music library into the <c>bgm</c> pack folder once, so a
    /// returning player does not fetch 40 MB of music again. The old manifest names files, not packs, so it
    /// is dropped and the first sync hashes what it finds.</summary>
    public void MigrateLegacyMusic()
    {
        var legacy = Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, LegacyMusicFolder);
        var target = MediaPaths.Downloaded(MediaPaths.Music);
        if (!Directory.Exists(legacy) || Directory.Exists(target))
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(target);
            var moved = 0;
            foreach (var file in Directory.EnumerateFiles(legacy, "*.ogg"))
            {
                File.Move(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
                moved++;
            }
            Directory.Delete(legacy, recursive: true);
            _log.Information($"[Assets] Moved {moved} music track(s) into the asset store.");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[Assets] The old music folder could not be moved; the tracks download again.");
        }
    }

    private string FullPath(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    Task<AssetManifestDto?> IAssetStore.ReadManifestAsync(CancellationToken ct) => Task.FromResult(ReadLocalManifest());

    public Task<AssetFileDto[]> ScanAsync(CancellationToken ct)
    {
        var found = new List<AssetFileDto>();
        if (!Directory.Exists(Root))
        {
            return Task.FromResult(found.ToArray());
        }
        var scratch = ScratchDir;
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (file.StartsWith(scratch, StringComparison.OrdinalIgnoreCase) || file.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var relative = Path.GetRelativePath(Root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (string.Equals(relative, ManifestFile, StringComparison.Ordinal) || !AssetManifest.IsValidPath(relative))
            {
                continue;
            }
            try
            {
                using var stream = File.OpenRead(file);
                found.Add(new AssetFileDto(relative, AssetManifest.Hash(stream), stream.Length));
            }
            catch (IOException ex)
            {
                _log.Warning(ex, $"[Assets] Could not hash {relative}; it is fetched again.");
            }
        }
        return Task.FromResult(found.ToArray());
    }

    async Task IAssetStore.WriteFileAsync(string path, byte[] bytes, CancellationToken ct)
    {
        if (!AssetManifest.IsValidPath(path))
        {
            throw new InvalidOperationException($"Refusing to write '{path}': not a valid asset path.");
        }
        if (!AllowedExtensions.Contains(Path.GetExtension(path)))
        {
            throw new InvalidOperationException($"Refusing to write '{path}': the file type is not one the phone uses.");
        }
        var full = FullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var part = full + PartSuffix;
        await File.WriteAllBytesAsync(part, bytes, ct).ConfigureAwait(false);
        File.Move(part, full, overwrite: true);
    }

    Task IAssetStore.DeleteFileAsync(string path, CancellationToken ct)
    {
        if (AssetManifest.IsValidPath(path))
        {
            var full = FullPath(path);
            if (File.Exists(full))
            {
                File.Delete(full);
            }
        }
        return Task.CompletedTask;
    }

    Task<Stream> IAssetStore.OpenScratchAsync(string pack, string zipSha256, CancellationToken ct)
    {
        Directory.CreateDirectory(ScratchDir);
        var part = Path.Combine(ScratchDir, pack + ScratchZipSuffix);
        var hashFile = Path.Combine(ScratchDir, pack + ScratchHashSuffix);
        if (File.Exists(part) && (!File.Exists(hashFile) || !string.Equals(File.ReadAllText(hashFile).Trim(), zipSha256, StringComparison.Ordinal)))
        {
            File.Delete(part);
        }
        File.WriteAllText(hashFile, zipSha256);
        Stream stream = new FileStream(part, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, FileBuffer, useAsync: true);
        return Task.FromResult(stream);
    }

    Task IAssetStore.DiscardScratchAsync(string pack, CancellationToken ct)
    {
        foreach (var file in new[] { Path.Combine(ScratchDir, pack + ScratchZipSuffix), Path.Combine(ScratchDir, pack + ScratchHashSuffix) })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        return Task.CompletedTask;
    }

    async Task IAssetStore.InstallBundleAsync(AssetPackDto pack, Stream zip, CancellationToken ct)
    {
        if (!string.Equals(pack.Name, AssetPacks.EchoHost, StringComparison.Ordinal) || string.IsNullOrEmpty(pack.Version))
        {
            _log.Warning($"[Assets] No installer for bundle {pack.Name}; it is left alone.");
            return;
        }
        if (!await _echoInstaller.InstallFromZipAsync(pack.Version, zip, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException(_echoInstaller.State.FailureReason ?? "the playback host did not install");
        }
    }

    Task IAssetStore.WriteManifestAsync(AssetManifestDto manifest, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        return File.WriteAllTextAsync(ManifestPath, JsonSerializer.Serialize(manifest, Json), ct);
    }

    async Task<AssetManifestDto?> IAssetSource.GetManifestAsync(CancellationToken ct)
    {
        using var request = Authorized(HttpMethod.Get, "assets/manifest");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _log.Warning("[Assets] Manifest request answered {Status}.", (int)response.StatusCode);
            return null;
        }
        return await response.Content.ReadFromJsonAsync<AssetManifestDto>(Json, ct).ConfigureAwait(false);
    }

    async Task<AssetDownload?> IAssetSource.GetPackAsync(string name, long fromOffset, CancellationToken ct)
    {
        var request = Authorized(HttpMethod.Get, "assets/pack/" + name);
        if (fromOffset > 0)
        {
            request.Headers.Range = new RangeHeaderValue(fromOffset, null);
        }
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            response.Dispose();
            throw new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests);
        }
        if (!response.IsSuccessStatusCode)
        {
            _log.Warning("[Assets] Pack {Pack} answered {Status}.", name, (int)response.StatusCode);
            response.Dispose();
            return null;
        }
        var offset = response.StatusCode == HttpStatusCode.PartialContent
            ? response.Content.Headers.ContentRange?.From ?? fromOffset
            : 0;
        var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return new AssetDownload(body, offset);
    }

    async Task<byte[]?> IAssetSource.GetFileAsync(string path, CancellationToken ct)
    {
        using var request = Authorized(HttpMethod.Get, "assets/file/" + path);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests);
        }
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrEmpty(_config.Auth.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.Auth.AccessToken);
        }
        return request;
    }
}
