using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace AetherLove.Services.Echo;

public enum EchoInstallPhase
{
    NotInstalled,
    Extracting,
    Installed,
    Failed,
}

/// <summary>An immutable snapshot of the installer's progress, swapped atomically so the draw thread can
/// read it without locking. The download itself is the asset sync's; this only reports the unpack.</summary>
public sealed record EchoInstallState(
    EchoInstallPhase Phase,
    string? Version,
    string? FailureReason)
{
    public static readonly EchoInstallState NotInstalled = new(EchoInstallPhase.NotInstalled, null, null);

    public bool Busy => Phase is EchoInstallPhase.Extracting;
}

/// <summary>Unpacks a verified Echo playback host bundle into the version layout owned by
/// <see cref="EchoHostLocator"/>. The bytes arrive from the asset sync, already checked against the
/// server's hash, so nothing here downloads or hashes. Never throws: every failure lands in
/// <see cref="State"/> and returns false.</summary>
public sealed class EchoHostInstaller
{
    private const string TempPrefix = ".tmp-";

    private readonly EchoHostLocator _locator;
    private int _running;
    private volatile EchoInstallState _state = EchoInstallState.NotInstalled;

    public EchoHostInstaller(EchoHostLocator locator)
    {
        _locator = locator;
        if (locator.InstalledVersion is { } version)
        {
            _state = new EchoInstallState(EchoInstallPhase.Installed, version, null);
        }
    }

    public EchoInstallState State => _state;

    /// <summary>Raised on a background thread; consumers must not touch ImGui from it (read
    /// <see cref="State"/> from the draw thread instead).</summary>
    public event Action<EchoInstallState>? StateChanged;

    /// <summary>Extracts <paramref name="zip"/> (positioned at its start) into the folder for
    /// <paramref name="version"/>, writes the completion marker last and prunes older builds. A version that
    /// is already complete only prunes.</summary>
    public async Task<bool> InstallFromZipAsync(string version, Stream zip, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return false;
        }
        var temp = Path.Combine(_locator.Root, TempPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            if (_locator.IsComplete(version))
            {
                // A prune blocked by a still-running old build gets its second chance here, or the
                // leftover would survive forever: every later attempt takes this early return.
                _locator.PruneOtherVersions(version);
                Publish(new EchoInstallState(EchoInstallPhase.Installed, version, null));
                return true;
            }

            Directory.CreateDirectory(_locator.Root);
            Publish(new EchoInstallState(EchoInstallPhase.Extracting, version, null));
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true))
            {
                archive.ExtractToDirectory(temp);
            }
            if (!File.Exists(Path.Combine(temp, EchoHostLocator.HostExeName)))
            {
                return Fail(version, "The downloaded bundle did not contain the playback host.");
            }

            var target = _locator.VersionDir(version);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
            Directory.Move(temp, target);
            await File.WriteAllTextAsync(_locator.MarkerPath(version), version, ct).ConfigureAwait(false);

            _locator.PruneOtherVersions(version);
            Publish(new EchoInstallState(EchoInstallPhase.Installed, version, null));
            return true;
        }
        catch (OperationCanceledException)
        {
            Publish(_locator.InstalledVersion is { } v
                ? new EchoInstallState(EchoInstallPhase.Installed, v, null)
                : EchoInstallState.NotInstalled);
            return false;
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Echo] Playback host install failed.");
            return Fail(version, ex.Message);
        }
        finally
        {
            TryDeleteDirectory(temp);
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private bool Fail(string version, string reason)
    {
        Publish(new EchoInstallState(EchoInstallPhase.Failed, version, reason));
        return false;
    }

    private void Publish(EchoInstallState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
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
