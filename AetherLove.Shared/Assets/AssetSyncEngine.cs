using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherLove.Shared.Assets;

/// <summary>The client's copy of the collection, as the engine sees it. The plugin backs this with a
/// folder under its config directory; tests back it with dictionaries.</summary>
public interface IAssetStore
{
    /// <summary>The manifest written by the last sync, or null when there is none.</summary>
    Task<AssetManifestDto?> ReadManifestAsync(CancellationToken ct);

    /// <summary>Hashes whatever files are present, for a first sync or a lost manifest, so files already
    /// on disk are not fetched again.</summary>
    Task<AssetFileDto[]> ScanAsync(CancellationToken ct);

    Task WriteFileAsync(string path, byte[] bytes, CancellationToken ct);

    Task DeleteFileAsync(string path, CancellationToken ct);

    /// <summary>A seekable read-write stream for the pack's zip in flight. Whatever it already holds is a
    /// partial download of the zip with this hash, so a broken transfer resumes where it stopped; the
    /// store drops it when the hash it was opened for changes.</summary>
    Task<Stream> OpenScratchAsync(string pack, string zipSha256, CancellationToken ct);

    Task DiscardScratchAsync(string pack, CancellationToken ct);

    /// <summary>Hands a verified bundle zip to whoever installs it. The stream is positioned at its start.</summary>
    Task InstallBundleAsync(AssetPackDto pack, Stream zip, CancellationToken ct);

    Task WriteManifestAsync(AssetManifestDto manifest, CancellationToken ct);
}

/// <summary>A pack download in progress: the body and the byte offset it starts at, which is the offset
/// asked for when the server honoured the range and zero when it sent the whole zip instead.</summary>
public sealed record AssetDownload(Stream Body, long Offset);

/// <summary>Where the bytes come from: the server over HTTP in the plugin, dictionaries in tests.</summary>
public interface IAssetSource
{
    Task<AssetManifestDto?> GetManifestAsync(CancellationToken ct);

    /// <summary>The pack's zip from <paramref name="fromOffset"/> on, or null when the server would not
    /// hand it over.</summary>
    Task<AssetDownload?> GetPackAsync(string name, long fromOffset, CancellationToken ct);

    /// <summary>One file's bytes, or null when the server would not hand them over.</summary>
    Task<byte[]?> GetFileAsync(string path, CancellationToken ct);
}

public enum AssetSyncPhase
{
    Planning = 0,
    Downloading = 1,
    Verifying = 2,
    Extracting = 3,
    Installing = 4,
    Done = 5,
    Failed = 6,
}

/// <summary>Where one pack stands, raised as the engine moves and every quarter megabyte while bytes
/// arrive. Bytes are of the zip in pack mode and of the files fetched so far in file mode.</summary>
public sealed record AssetSyncProgress(
    string Pack, AssetSyncPhase Phase, long BytesDone, long BytesTotal, int FilesDone, int FilesTotal);

public sealed record AssetSyncResult(
    bool UpToDate,
    string[] PacksSynced,
    string[] Downloaded,
    string[] Deleted,
    string[] FailedPacks,
    string[] FailedFiles,
    string LocalCollectionHash)
{
    public static readonly AssetSyncResult Unreachable = new(false, [], [], [], [], [], "");
}

/// <summary>Brings a store level with a source: one manifest fetch, one plan, then only the packs whose
/// hash differs, each as one zip or as single files, verified byte for byte before anything is kept.
/// Stateless and free of IO of its own, so the server suite exercises it with fakes. Failures never stop
/// the rest: a failed file or pack is left out of the written manifest, so the next run plans it again.</summary>
public static class AssetSyncEngine
{
    private const int CopyBuffer = 64 * 1024;
    private const long ProgressStep = 256 * 1024;

    /// <summary>Syncs the packs <paramref name="include"/> admits (all when null), smallest zip first and
    /// bundles last, so the small everyday packs land before the heavy ones. <paramref name="pauseBetweenPacks"/>
    /// waits between one pack and the next, so a watching player sees each pack go by.</summary>
    public static async Task<AssetSyncResult> SyncAsync(
        IAssetStore store,
        IAssetSource source,
        AssetSyncPolicy policy,
        Action<AssetSyncProgress> progress,
        Action<string> info,
        Action<string> warn,
        CancellationToken ct,
        Func<AssetPackDto, bool>? include = null,
        TimeSpan pauseBetweenPacks = default)
    {
        var remote = await source.GetManifestAsync(ct).ConfigureAwait(false);
        if (remote is null)
        {
            warn("Assets: the collection manifest could not be fetched; keeping what is on disk.");
            return AssetSyncResult.Unreachable;
        }

        var local = await store.ReadManifestAsync(ct).ConfigureAwait(false);
        if (local is null)
        {
            var present = await store.ScanAsync(ct).ConfigureAwait(false);
            local = present.Length > 0 ? AssetManifest.FromScan(present, remote) : null;
        }
        var localPacks = (local?.Packs ?? []).ToDictionary(p => p.Name, StringComparer.Ordinal);

        var plan = AssetManifest.PlanCollection(local, remote, policy);
        var todo = plan.Packs
            .Where(p => p.Mode != AssetPlanMode.Skip && (include is null || include(p.Remote)))
            .OrderBy(p => p.Remote.Kind)
            .ThenBy(p => p.Remote.ZipSize)
            .ToArray();
        var deletes = include is null ? plan.Delete : [];
        if (todo.Length == 0 && deletes.Length == 0)
        {
            var landed = local is not null && plan.IsEmpty ? remote : local;
            if (landed is not null && local is not null
                && !string.Equals(local.CollectionHash, landed.CollectionHash, StringComparison.Ordinal))
            {
                await store.WriteManifestAsync(landed, ct).ConfigureAwait(false);
            }
            return new AssetSyncResult(plan.IsEmpty, [], [], [], [], [], landed?.CollectionHash ?? "");
        }

        info($"Assets: {todo.Length} pack(s) to sync, {deletes.Length} file(s) to remove.");
        var synced = new List<string>();
        var downloaded = new List<string>();
        var failedPacks = new List<string>();
        var failedFiles = new List<string>();
        var written = new Dictionary<string, AssetPackDto>(localPacks, StringComparer.Ordinal);

        foreach (var packPlan in plan.Packs.Where(p => p.Mode == AssetPlanMode.Skip))
        {
            written[packPlan.Remote.Name] = packPlan.Remote;
        }

        for (var index = 0; index < todo.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            if (index > 0 && pauseBetweenPacks > TimeSpan.Zero)
            {
                await Task.Delay(pauseBetweenPacks, ct).ConfigureAwait(false);
            }
            var packPlan = todo[index];
            var pack = packPlan.Remote;
            var outcome = packPlan.Mode == AssetPlanMode.Pack
                ? await SyncWholeAsync(store, source, pack, progress, info, warn, ct).ConfigureAwait(false)
                : await SyncFilesAsync(store, source, packPlan, progress, info, warn, ct).ConfigureAwait(false);

            downloaded.AddRange(outcome.Downloaded);
            failedFiles.AddRange(outcome.FailedFiles);
            if (outcome.PackFailed)
            {
                failedPacks.Add(pack.Name);
                progress(new AssetSyncProgress(pack.Name, AssetSyncPhase.Failed, 0, pack.ZipSize, 0, pack.Files.Length));
                continue;
            }
            synced.Add(pack.Name);
            var kept = pack.Files.Where(f => !outcome.FailedFiles.Contains(f.Path, StringComparer.Ordinal)).ToArray();
            if (pack.Kind == AssetPackKind.Bundle || kept.Length == pack.Files.Length)
            {
                written[pack.Name] = pack;
            }
            else if (kept.Length > 0)
            {
                written[pack.Name] = AssetManifest.BuildPack(pack.Name, pack.Kind, kept, pack.ZipSha256, pack.ZipSize, pack.Version);
            }
            else
            {
                written.Remove(pack.Name);
            }
            progress(new AssetSyncProgress(pack.Name, AssetSyncPhase.Done, pack.ZipSize, pack.ZipSize, kept.Length, pack.Files.Length));
        }

        foreach (var path in deletes)
        {
            await store.DeleteFileAsync(path, ct).ConfigureAwait(false);
            info($"Assets: removed {path}, no longer served.");
        }
        if (deletes.Length > 0)
        {
            foreach (var (name, entry) in written.ToArray())
            {
                if (entry.Kind == AssetPackKind.Files && entry.Files.Any(f => deletes.Contains(f.Path, StringComparer.Ordinal)))
                {
                    var remaining = entry.Files.Where(f => !deletes.Contains(f.Path, StringComparer.Ordinal)).ToArray();
                    if (remaining.Length == 0)
                    {
                        written.Remove(name);
                    }
                    else
                    {
                        written[name] = AssetManifest.BuildPack(name, entry.Kind, remaining, entry.ZipSha256, entry.ZipSize, entry.Version);
                    }
                }
            }
        }

        var manifest = AssetManifest.Build(written.Values);
        await store.WriteManifestAsync(manifest, ct).ConfigureAwait(false);
        info($"Assets: {synced.Count} pack(s) synced, {downloaded.Count} file(s) downloaded, {deletes.Length} removed, "
            + $"{failedPacks.Count} pack(s) and {failedFiles.Count} file(s) failed.");
        return new AssetSyncResult(false, synced.ToArray(), downloaded.ToArray(), deletes,
            failedPacks.ToArray(), failedFiles.ToArray(), manifest.CollectionHash);
    }

    private sealed record PackOutcome(bool PackFailed, List<string> Downloaded, List<string> FailedFiles);

    /// <summary>Fetches the pack's zip into the store's scratch, resuming a partial one, verifies the zip
    /// hash, then either unpacks the listed files or hands a bundle to its installer.</summary>
    private static async Task<PackOutcome> SyncWholeAsync(
        IAssetStore store, IAssetSource source, AssetPackDto pack,
        Action<AssetSyncProgress> progress, Action<string> info, Action<string> warn, CancellationToken ct)
    {
        var downloaded = new List<string>();
        var failedFiles = new List<string>();
        var scratch = await store.OpenScratchAsync(pack.Name, pack.ZipSha256, ct).ConfigureAwait(false);
        try
        {
            var have = scratch.Length;
            if (have > pack.ZipSize)
            {
                scratch.SetLength(0);
                have = 0;
            }
            if (have < pack.ZipSize)
            {
                progress(new AssetSyncProgress(pack.Name, AssetSyncPhase.Downloading, have, pack.ZipSize, 0, pack.Files.Length));
                AssetDownload? download;
                try
                {
                    download = await source.GetPackAsync(pack.Name, have, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    warn($"Assets: pack {pack.Name} failed to download ({ex.GetType().Name}: {ex.Message}).");
                    return new PackOutcome(true, downloaded, failedFiles);
                }
                if (download is null)
                {
                    warn($"Assets: the server did not serve pack {pack.Name}.");
                    return new PackOutcome(true, downloaded, failedFiles);
                }
                try
                {
                    using var body = download.Body;
                    if (download.Offset != have)
                    {
                        scratch.SetLength(download.Offset);
                        have = download.Offset;
                    }
                    scratch.Seek(have, SeekOrigin.Begin);
                    var buffer = new byte[CopyBuffer];
                    var sinceReport = 0L;
                    int read;
                    while ((read = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        await scratch.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        have += read;
                        sinceReport += read;
                        if (sinceReport >= ProgressStep)
                        {
                            sinceReport = 0;
                            progress(new AssetSyncProgress(pack.Name, AssetSyncPhase.Downloading, have, pack.ZipSize, 0, pack.Files.Length));
                        }
                    }
                    await scratch.FlushAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    warn($"Assets: pack {pack.Name} stopped at {have / 1024} KB ({ex.GetType().Name}: {ex.Message}); it resumes next time.");
                    return new PackOutcome(true, downloaded, failedFiles);
                }
            }

            progress(new AssetSyncProgress(pack.Name, AssetSyncPhase.Verifying, pack.ZipSize, pack.ZipSize, 0, pack.Files.Length));
            scratch.Seek(0, SeekOrigin.Begin);
            var hash = AssetManifest.Hash(scratch);
            if (!string.Equals(hash, pack.ZipSha256, StringComparison.Ordinal))
            {
                warn($"Assets: pack {pack.Name} arrived with the wrong hash and was not kept.");
                await DiscardAsync(store, pack.Name, scratch, ct).ConfigureAwait(false);
                return new PackOutcome(true, downloaded, failedFiles);
            }

            if (pack.Kind == AssetPackKind.Bundle)
            {
                progress(new AssetSyncProgress(pack.Name, AssetSyncPhase.Installing, pack.ZipSize, pack.ZipSize, 0, 1));
                scratch.Seek(0, SeekOrigin.Begin);
                try
                {
                    await store.InstallBundleAsync(pack, scratch, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    warn($"Assets: bundle {pack.Name} could not be installed ({ex.GetType().Name}: {ex.Message}).");
                    return new PackOutcome(true, downloaded, failedFiles);
                }
                downloaded.AddRange(pack.Files.Select(f => f.Path));
                info($"Assets: installed bundle {pack.Name} {pack.Version} ({pack.ZipSize / 1048576} MB).");
                await DiscardAsync(store, pack.Name, scratch, ct).ConfigureAwait(false);
                return new PackOutcome(false, downloaded, failedFiles);
            }

            progress(new AssetSyncProgress(pack.Name, AssetSyncPhase.Extracting, pack.ZipSize, pack.ZipSize, 0, pack.Files.Length));
            scratch.Seek(0, SeekOrigin.Begin);
            using (var archive = new ZipArchive(scratch, ZipArchiveMode.Read, leaveOpen: true))
            {
                var done = 0;
                foreach (var file in pack.Files)
                {
                    ct.ThrowIfCancellationRequested();
                    var entry = archive.GetEntry(file.Path);
                    if (entry is null)
                    {
                        warn($"Assets: pack {pack.Name} does not contain {file.Path}.");
                        failedFiles.Add(file.Path);
                        continue;
                    }
                    byte[] bytes;
                    using (var entryStream = entry.Open())
                    using (var memory = new MemoryStream(checked((int)Math.Max(entry.Length, 0))))
                    {
                        await entryStream.CopyToAsync(memory, ct).ConfigureAwait(false);
                        bytes = memory.ToArray();
                    }
                    if (!string.Equals(AssetManifest.Hash(bytes), file.Sha256, StringComparison.Ordinal))
                    {
                        warn($"Assets: {file.Path} inside pack {pack.Name} has the wrong hash and was not kept.");
                        failedFiles.Add(file.Path);
                        continue;
                    }
                    if (!await TryWriteAsync(store, file.Path, bytes, warn, ct).ConfigureAwait(false))
                    {
                        failedFiles.Add(file.Path);
                        continue;
                    }
                    downloaded.Add(file.Path);
                    done++;
                    progress(new AssetSyncProgress(pack.Name, AssetSyncPhase.Extracting, pack.ZipSize, pack.ZipSize, done, pack.Files.Length));
                }
            }
            info($"Assets: pack {pack.Name} landed, {downloaded.Count} file(s), {pack.ZipSize / 1024} KB.");
            await DiscardAsync(store, pack.Name, scratch, ct).ConfigureAwait(false);
            return new PackOutcome(false, downloaded, failedFiles);
        }
        finally
        {
            await scratch.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>A store may refuse a file (a type the phone never uses, a locked path); that is one failed
    /// file to plan again next time, never the end of the run.</summary>
    private static async Task<bool> TryWriteAsync(IAssetStore store, string path, byte[] bytes, Action<string> warn, CancellationToken ct)
    {
        try
        {
            await store.WriteFileAsync(path, bytes, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warn($"Assets: {path} could not be written ({ex.GetType().Name}: {ex.Message}).");
            return false;
        }
    }

    private static async Task DiscardAsync(IAssetStore store, string pack, Stream scratch, CancellationToken ct)
    {
        await scratch.DisposeAsync().ConfigureAwait(false);
        await store.DiscardScratchAsync(pack, ct).ConfigureAwait(false);
    }

    /// <summary>Fetches the changed files of a pack one by one, each verified before it is written.</summary>
    private static async Task<PackOutcome> SyncFilesAsync(
        IAssetStore store, IAssetSource source, AssetPackPlan plan,
        Action<AssetSyncProgress> progress, Action<string> info, Action<string> warn, CancellationToken ct)
    {
        var pack = plan.Remote;
        var downloaded = new List<string>();
        var failedFiles = new List<string>();
        var total = plan.Download.Sum(f => f.Size);
        var bytes = 0L;
        var done = 0;
        progress(new AssetSyncProgress(pack.Name, AssetSyncPhase.Downloading, 0, total, 0, plan.Download.Length));
        foreach (var file in plan.Download)
        {
            ct.ThrowIfCancellationRequested();
            byte[]? data;
            try
            {
                data = await source.GetFileAsync(file.Path, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warn($"Assets: {file.Path} failed to download ({ex.GetType().Name}: {ex.Message}).");
                failedFiles.Add(file.Path);
                continue;
            }
            if (data is null)
            {
                warn($"Assets: the server did not serve {file.Path}.");
                failedFiles.Add(file.Path);
                continue;
            }
            if (!string.Equals(AssetManifest.Hash(data), file.Sha256, StringComparison.Ordinal))
            {
                warn($"Assets: {file.Path} arrived with the wrong hash and was not kept.");
                failedFiles.Add(file.Path);
                continue;
            }
            if (!await TryWriteAsync(store, file.Path, data, warn, ct).ConfigureAwait(false))
            {
                failedFiles.Add(file.Path);
                continue;
            }
            downloaded.Add(file.Path);
            done++;
            bytes += data.Length;
            progress(new AssetSyncProgress(pack.Name, AssetSyncPhase.Downloading, bytes, total, done, plan.Download.Length));
        }
        info($"Assets: pack {pack.Name} updated file by file, {downloaded.Count} of {plan.Download.Length}.");
        return new PackOutcome(false, downloaded, failedFiles);
    }
}
