using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.EchoVidya;

namespace AetherLove.Services.Echo;

public enum EchoInstallPhase
{
    NotInstalled,
    Downloading,
    Verifying,
    Extracting,
    Installed,
    Failed,
}

/// <summary>An immutable snapshot of the installer's progress, swapped atomically so the draw thread can
/// read it without locking.</summary>
public sealed record EchoInstallState(
    EchoInstallPhase Phase,
    long BytesDone,
    long BytesTotal,
    string? Version,
    string? FailureReason)
{
    public static readonly EchoInstallState NotInstalled = new(EchoInstallPhase.NotInstalled, 0, 0, null, null);

    public bool Busy => Phase is EchoInstallPhase.Downloading or EchoInstallPhase.Verifying or EchoInstallPhase.Extracting;

    /// <summary>0..1 while downloading, 0 when the size is unknown.</summary>
    public float Progress => BytesTotal > 0 ? Math.Clamp((float)((double)BytesDone / BytesTotal), 0f, 1f) : 0f;
}

/// <summary>Downloads, verifies and unpacks the Echo playback host into the version layout owned by
/// <see cref="EchoHostLocator"/>. Never throws: every failure lands in <see cref="State"/> and returns false.</summary>
public sealed class EchoHostInstaller
{
    private const int CopyBufferBytes = 81920;

    /// <summary>Bytes between progress notifications; per-chunk raises would flood the UI.</summary>
    private const long ProgressReportBytes = 256 * 1024;

    private const string TempPrefix = ".tmp-";

    // The handler timeout would abort a slow multi-megabyte body mid-stream, so cancellation is the caller's.
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly EchoHostLocator _locator;
    private int _running;
    private volatile EchoInstallState _state = EchoInstallState.NotInstalled;

    public EchoHostInstaller(EchoHostLocator locator)
    {
        _locator = locator;
        if (locator.InstalledVersion is { } version)
        {
            _state = new EchoInstallState(EchoInstallPhase.Installed, 0, 0, version, null);
        }
    }

    public EchoInstallState State => _state;

    /// <summary>Raised on a background thread; consumers must not touch ImGui from it (read
    /// <see cref="State"/> from the draw thread instead).</summary>
    public event Action<EchoInstallState>? StateChanged;

    public async Task<bool> InstallAsync(EchoHostManifestDto manifest, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return false;
        }
        var temp = Path.Combine(_locator.Root, TempPrefix + Guid.NewGuid().ToString("N"));
        var zipPath = temp + ".zip";
        try
        {
            if (_locator.IsComplete(manifest.Version))
            {
                Publish(new EchoInstallState(EchoInstallPhase.Installed, 0, 0, manifest.Version, null));
                return true;
            }

            Directory.CreateDirectory(_locator.Root);
            var hash = await DownloadAsync(manifest, zipPath, ct).ConfigureAwait(false);
            if (hash is null)
            {
                return false;
            }

            Publish(new EchoInstallState(EchoInstallPhase.Verifying, manifest.SizeBytes, manifest.SizeBytes, manifest.Version, null));
            if (!string.Equals(hash, manifest.Sha256.Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(zipPath);
                return Fail(manifest.Version, "The download did not match the published checksum.");
            }

            Publish(new EchoInstallState(EchoInstallPhase.Extracting, manifest.SizeBytes, manifest.SizeBytes, manifest.Version, null));
            ZipFile.ExtractToDirectory(zipPath, temp);
            if (!File.Exists(Path.Combine(temp, EchoHostLocator.HostExeName)))
            {
                return Fail(manifest.Version, "The downloaded bundle did not contain the playback host.");
            }

            var target = _locator.VersionDir(manifest.Version);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
            Directory.Move(temp, target);
            await File.WriteAllTextAsync(_locator.MarkerPath(manifest.Version), manifest.Version, ct).ConfigureAwait(false);

            TryDelete(zipPath);
            _locator.PruneOtherVersions(manifest.Version);
            Publish(new EchoInstallState(EchoInstallPhase.Installed, manifest.SizeBytes, manifest.SizeBytes, manifest.Version, null));
            return true;
        }
        catch (OperationCanceledException)
        {
            Publish(_locator.InstalledVersion is { } v
                ? new EchoInstallState(EchoInstallPhase.Installed, 0, 0, v, null)
                : EchoInstallState.NotInstalled);
            return false;
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Echo] Playback host install failed.");
            return Fail(manifest.Version, ex.Message);
        }
        finally
        {
            TryDeleteDirectory(temp);
            TryDelete(zipPath);
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <summary>Streams the bundle to disk and hashes it as it goes; a second pass over a large file would
    /// double the install's disk cost. Returns the hex digest, or null when the download itself failed.</summary>
    private async Task<string?> DownloadAsync(EchoHostManifestDto manifest, string zipPath, CancellationToken ct)
    {
        using var response = await Http.GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Fail(manifest.Version, $"The download failed with HTTP {(int)response.StatusCode}.");
            return null;
        }

        var total = response.Content.Headers.ContentLength ?? manifest.SizeBytes;
        Publish(new EchoInstallState(EchoInstallPhase.Downloading, 0, total, manifest.Version, null));

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (var target = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferBytes, true))
        {
            var buffer = new byte[CopyBufferBytes];
            long done = 0;
            long reported = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }
                hasher.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (done - reported >= ProgressReportBytes)
                {
                    reported = done;
                    Publish(new EchoInstallState(EchoInstallPhase.Downloading, done, total, manifest.Version, null));
                }
            }
        }
        return Convert.ToHexString(hasher.GetHashAndReset());
    }

    private bool Fail(string version, string reason)
    {
        Publish(new EchoInstallState(EchoInstallPhase.Failed, 0, 0, version, reason));
        return false;
    }

    private void Publish(EchoInstallState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, $"[Echo] Could not remove {Path.GetFileName(path)}.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, $"[Echo] Could not remove the staging folder {Path.GetFileName(path)}.");
        }
    }
}
