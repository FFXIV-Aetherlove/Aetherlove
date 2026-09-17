using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Signal;
using AetherLove.Shared.Assets;
using Dalamud.Plugin.Services;

namespace AetherLove.Services.Assets;

public enum AssetSyncReason
{
    None = 0,
    Bootstrap = 1,
    Reconnect = 2,
    Manual = 3,
}

public enum AssetSyncRunPhase
{
    Idle = 0,
    Checking = 1,
    Syncing = 2,
    Done = 3,
}

public enum AssetPackState
{
    Pending = 0,
    Active = 1,
    Done = 2,
    Failed = 3,
}

/// <summary>Where one pack of the current run stands.</summary>
public sealed record AssetPackStatus(
    string Name,
    bool Required,
    AssetPackState State,
    AssetSyncPhase Phase,
    long BytesDone,
    long BytesTotal,
    int FilesDone,
    int FilesTotal);

/// <summary>The sync as the draw thread sees it: one immutable object, swapped whole. <see cref="RequiredPending"/>
/// is what gates Home; <see cref="ShownOnScreen"/> is whether the gate has shown this pending set, so the
/// phone window moves onto it only once per set.</summary>
public sealed record AssetSyncSnapshot(
    AssetSyncRunPhase Phase,
    AssetSyncReason Reason,
    bool RequiredPending,
    bool ShownOnScreen,
    bool DownloadedAnything,
    bool HasFailures,
    string? CurrentPack,
    IReadOnlyList<AssetPackStatus> Packs)
{
    public static readonly AssetSyncSnapshot Idle = new(AssetSyncRunPhase.Idle, AssetSyncReason.None, false, false, false, false, null, []);

    public long RequiredBytesDone => Packs.Where(p => p.Required).Sum(p => p.BytesDone);

    public long RequiredBytesTotal => Packs.Where(p => p.Required).Sum(p => p.BytesTotal);

    public int RequiredDone => Packs.Count(p => p.Required && p.State == AssetPackState.Done);

    public int RequiredTotal => Packs.Count(p => p.Required);

    /// <summary>A pack failed, or the run stopped with failures before it could plan any pack (the manifest
    /// check failed while Home was already waiting), which is when the gate must still offer "Try again".</summary>
    public bool RequiredFailed =>
        Packs.Any(p => p.Required && p.State == AssetPackState.Failed)
        || (RequiredPending && HasFailures && Phase == AssetSyncRunPhase.Done && Packs.Count == 0);
}

/// <summary>Runs the asset sync for the phone: a check against the server's manifest decides whether Home
/// must wait, then one worker brings every pack down, smallest first and the Echo bundle last, retrying
/// with a growing pause while anything failed and the hub is up. Progress lands in a lock-free snapshot for
/// the draw thread; pack-landed events queue up and are raised from <see cref="DrainEvents"/> on the draw
/// thread, so no consumer ever touches ImGui from a worker. Nothing here posts a notification.</summary>
public sealed class AssetSyncService : IDisposable
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CheckIsFreshFor = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] RetryBackoff = [TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];
    private static readonly TimeSpan OfflineRetryPoll = TimeSpan.FromSeconds(30);

    /// <summary>The pause between one pack and the next on an ordinary update, so a watching player sees each pack go by.</summary>
    private static readonly TimeSpan RegularPackPause = TimeSpan.FromMilliseconds(200);

    private readonly IPluginLog _log;
    private readonly AssetLibrary _library;
    private readonly AetherSignalService _signal;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<string> _completedPacks = new();

    private volatile AssetSyncSnapshot _snapshot = AssetSyncSnapshot.Idle;
    private volatile AssetManifestDto? _remote;
    private volatile AssetManifestDto? _local;
    private DateTime _checkedAtUtc = DateTime.MinValue;
    private Task? _running;
    private CancellationTokenSource? _runCts;
    private Task? _checking;
    private volatile CancellationTokenSource? _pauseWake;
    private AssetSyncReason _queued = AssetSyncReason.None;
    private bool _debugPending;
    private volatile bool _resetting;

    public AssetSyncService(IPluginLog log, AssetLibrary library, AetherSignalService signal)
    {
        _log = log;
        _library = library;
        _signal = signal;
        _library.MigrateLegacyMusic();
        _local = _library.ReadLocalManifest();
    }

    public AssetSyncSnapshot Snapshot => _snapshot;

    /// <summary>The collection hash of what is on disk; compared with the server's on every reconnect.</summary>
    public string LocalCollectionHash => _local?.CollectionHash ?? string.Empty;

    public bool RequiredPending => _snapshot.RequiredPending;

    /// <summary>Raised on the draw thread, from <see cref="DrainEvents"/>, with the name of a pack that landed.</summary>
    public event Action<string>? PackReady;

    /// <summary>True once the named pack is on disk in any version. Without a local manifest (a store seeded
    /// by hand, or a lost manifest with the server out of reach) the pack's folder answers instead.</summary>
    public bool IsReady(string pack)
    {
        if (_debugPending)
        {
            return false;
        }
        if (_local is { } local)
        {
            return local.Packs.Any(p => string.Equals(p.Name, pack, StringComparison.Ordinal));
        }
        return _library.HasPackFolder(pack);
    }

    public (long Done, long Total) Progress(string pack)
    {
        var status = _snapshot.Packs.FirstOrDefault(p => string.Equals(p.Name, pack, StringComparison.Ordinal));
        return status is null || status.State == AssetPackState.Done
            ? (0, 0)
            : (status.BytesDone, status.BytesTotal);
    }

    /// <summary>Fetches the server's manifest and plans against what is on disk, so <see cref="RequiredPending"/>
    /// is decided before the startup ladder runs. Capped at fifteen seconds; an unreachable or absent endpoint
    /// is a warning and no gate, never a failure of the phone.</summary>
    public async Task<bool> CheckAsync(CancellationToken ct)
    {
        if (_debugPending)
        {
            return true;
        }
        AssetManifestDto? remote;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CheckTimeout);
            remote = await ((IAssetSource)_library).GetManifestAsync(timeout.Token).ConfigureAwait(false);
            if (remote is not null && _local is null)
            {
                var present = await _library.ScanAsync(timeout.Token).ConfigureAwait(false);
                if (present.Length > 0)
                {
                    _local = AssetManifest.FromScan(present, remote);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.Warning("[Assets] The manifest check timed out; the phone keeps what it has.");
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[Assets] The manifest check failed; the phone keeps what it has.");
            return false;
        }
        if (remote is null)
        {
            return false;
        }
        _remote = remote;
        _checkedAtUtc = DateTime.UtcNow;
        ApplyPlan(remote);
        return true;
    }

    /// <summary>Starts a sync, or queues one more run behind the one in flight.</summary>
    public void RequestSync(AssetSyncReason reason)
    {
        if (_debugPending || _resetting)
        {
            return;
        }
        lock (_gate)
        {
            if (_resetting)
            {
                return;
            }
            if (_running is { IsCompleted: false })
            {
                if (reason == AssetSyncReason.Manual && _pauseWake is { } wake)
                {
                    try
                    {
                        wake.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    return;
                }
                _queued = reason;
                return;
            }
            StartRunLocked(reason, RegularPackPause);
        }
    }

    /// <summary>Starts a sync unless one is already in flight. The update gate calls this when it shows, so
    /// the download runs on screen instead of behind the sign-in steps.</summary>
    public void EnsureSyncing(AssetSyncReason reason)
    {
        if (_debugPending || _resetting)
        {
            return;
        }
        lock (_gate)
        {
            if (_resetting || _running is { IsCompleted: false })
            {
                return;
            }
            StartRunLocked(reason, RegularPackPause);
        }
    }

    /// <summary>Throws away every downloaded file and fetches the whole collection again, with the update gate
    /// moving onto a full-screen phone. A run in flight is cancelled first. The gate sits on its checking line
    /// for <paramref name="before"/> before anything is deleted, and the download waits
    /// <paramref name="betweenPacks"/> between packs so each one can be watched. Returns once the new run has
    /// started; a disposed service deletes nothing.</summary>
    public async Task ForceRedownloadAsync(TimeSpan before, TimeSpan betweenPacks, CancellationToken ct)
    {
        Task? running;
        Task? checking;
        lock (_gate)
        {
            if (_resetting || _cts.IsCancellationRequested)
            {
                return;
            }
            _resetting = true;
            _queued = AssetSyncReason.None;
            _runCts?.Cancel();
            running = _running;
            checking = _checking;
            _snapshot = RedownloadHold(showOnScreen: false);
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var restart = false;
        try
        {
            await WaitQuietlyAsync(running).ConfigureAwait(false);
            await WaitQuietlyAsync(checking).ConfigureAwait(false);
            _snapshot = RedownloadHold(_snapshot.ShownOnScreen);
            await Task.Delay(before, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            _local = null;
            _remote = null;
            _checkedAtUtc = DateTime.MinValue;
            restart = true;
            var kept = _library.DeleteAll();
            _log.Information(kept == 0
                ? "[Assets] Deleted every downloaded file; fetching the collection again."
                : $"[Assets] Deleted the downloaded files except {kept} still in use; fetching the collection again.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[Assets] Deleting the downloaded files failed part way; fetching what is missing.");
        }
        finally
        {
            lock (_gate)
            {
                _resetting = false;
                if (!_cts.IsCancellationRequested)
                {
                    StartRunLocked(AssetSyncReason.Manual, restart ? betweenPacks : RegularPackPause);
                }
            }
        }
    }

    private static AssetSyncSnapshot RedownloadHold(bool showOnScreen) =>
        new(AssetSyncRunPhase.Checking, AssetSyncReason.Manual, true, showOnScreen, false, false, null, []);

    private static async Task WaitQuietlyAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Starts a run on its own cancellation source, so a forced re-download can stop it without
    /// disposing the service. Call under <c>_gate</c>.</summary>
    private void StartRunLocked(AssetSyncReason reason, TimeSpan betweenPacks)
    {
        var run = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _runCts = run;
        _running = Task.Run(() => RunAsync(reason, betweenPacks, run.Token));
    }

    /// <summary>Re-plans against the server's manifest without downloading, so the startup ladder sees a
    /// changed collection. Joins a check already in flight and skips while a run is in flight, which plans
    /// for itself.</summary>
    public Task RequestCheck()
    {
        if (_debugPending || _resetting)
        {
            return Task.CompletedTask;
        }
        lock (_gate)
        {
            if (_resetting || _running is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }
            if (_checking is not { IsCompleted: false })
            {
                _checking = Task.Run(() => CheckAsync(_cts.Token));
            }
            return _checking;
        }
    }

    /// <summary>Marks every unfinished pack failed and keeps Home waiting, so the gate offers "Try again"
    /// instead of letting the phone through without its files.</summary>
    private void FailPending()
    {
        var snapshot = _snapshot;
        var packs = snapshot.Packs
            .Select(p => p.State is AssetPackState.Pending or AssetPackState.Active
                ? p with { State = AssetPackState.Failed, Phase = AssetSyncPhase.Failed }
                : p)
            .ToArray();
        _snapshot = snapshot with { Phase = AssetSyncRunPhase.Done, HasFailures = true, CurrentPack = null, Packs = packs };
    }

    /// <summary>Waits between retries; "Try again" cuts the wait short.</summary>
    private async Task PauseAsync(TimeSpan delay, CancellationToken ct)
    {
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pauseWake = wake;
        try
        {
            await Task.Delay(delay, wake.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
        finally
        {
            _pauseWake = null;
        }
    }

    /// <summary>The gate is on screen for this pending set.</summary>
    public void MarkShown()
    {
        _snapshot = _snapshot with { ShownOnScreen = true };
    }

    /// <summary>Raises the queued completion events. Call once per frame from the draw thread.</summary>
    public void DrainEvents()
    {
        while (_completedPacks.TryDequeue(out var pack))
        {
            PackReady?.Invoke(pack);
        }
    }

    /// <summary>Fakes a pending update so the gate can be looked at without a server that carries the
    /// endpoints: every pack reads as missing, one shows progress and one has failed. Off restores the
    /// real state.</summary>
    public void SetDebugPending(bool on)
    {
        _debugPending = on;
        if (!on)
        {
            _snapshot = AssetSyncSnapshot.Idle;
            return;
        }
        var names = new[]
        {
            AssetPacks.Emoji, AssetPacks.Sounds, AssetPacks.Notifications, AssetPacks.Weather, AssetPacks.Stacker,
            AssetPacks.Wallpapers, AssetPacks.Aetherling, AssetPacks.AetherlingAccessories, AssetPacks.Other,
            AssetPacks.AetherlingGames, AssetPacks.Racer,
        };
        const long fakeSize = 4 * 1024 * 1024;
        var packs = names.Select((name, i) => new AssetPackStatus(name, true,
                i < 4 ? AssetPackState.Done : i == 4 ? AssetPackState.Failed : i == 5 ? AssetPackState.Active : AssetPackState.Pending,
                i < 4 ? AssetSyncPhase.Done : i == 4 ? AssetSyncPhase.Failed : AssetSyncPhase.Downloading,
                i < 4 ? fakeSize : i == 5 ? fakeSize * 2 / 5 : 0, fakeSize, 0, 0))
            .ToArray();
        _snapshot = new AssetSyncSnapshot(AssetSyncRunPhase.Syncing, AssetSyncReason.Manual, true, false, true, true,
            AssetPacks.Wallpapers, packs);
    }

    public void Dispose() => _cts.Cancel();

    private void ApplyPlan(AssetManifestDto remote)
    {
        var plan = AssetManifest.PlanCollection(_local, remote, AssetSyncPolicy.Default);
        var previous = _snapshot;
        var packs = plan.Packs
            .Where(p => p.Mode != AssetPlanMode.Skip)
            .OrderBy(p => p.Remote.Kind)
            .ThenBy(p => p.Remote.ZipSize)
            .Select(p => new AssetPackStatus(p.Remote.Name, true, AssetPackState.Pending,
                AssetSyncPhase.Planning, 0, p.Mode == AssetPlanMode.Pack ? p.Remote.ZipSize : p.Download.Sum(f => f.Size), 0, p.Download.Length))
            .ToArray();
        var requiredPending = packs.Any(p => p.Required);
        var samePendingSet = previous.RequiredPending && requiredPending
            && previous.Packs.Where(p => p.Required).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
                .SequenceEqual(packs.Where(p => p.Required).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal), StringComparer.Ordinal);
        _snapshot = new AssetSyncSnapshot(
            previous.Phase == AssetSyncRunPhase.Syncing ? AssetSyncRunPhase.Syncing : AssetSyncRunPhase.Idle,
            previous.Reason,
            requiredPending,
            samePendingSet && previous.ShownOnScreen,
            previous.DownloadedAnything,
            false,
            null,
            packs);
    }

    private async Task RunAsync(AssetSyncReason reason, TimeSpan betweenPacks, CancellationToken ct)
    {
        try
        {
            // A standalone check that finishes later would re-plan over this run's progress.
            if (_checking is { } checking)
            {
                await checking.ConfigureAwait(false);
            }
            _snapshot = _snapshot with { Phase = AssetSyncRunPhase.Checking, Reason = reason };
            if (_remote is null || DateTime.UtcNow - _checkedAtUtc > CheckIsFreshFor)
            {
                if (!await CheckAsync(ct).ConfigureAwait(false))
                {
                    FailPending();
                    return;
                }
            }
            if (_snapshot.Packs.Count == 0)
            {
                _snapshot = _snapshot with { Phase = AssetSyncRunPhase.Done };
                return;
            }

            _snapshot = _snapshot with { Phase = AssetSyncRunPhase.Syncing };
            var attempt = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var result = await SyncAsync(betweenPacks, ct).ConfigureAwait(false);
                var failed = result.FailedPacks.Length > 0 || result.FailedFiles.Length > 0;
                _snapshot = _snapshot with
                {
                    Phase = AssetSyncRunPhase.Done,
                    HasFailures = failed,
                    CurrentPack = null,
                };
                if (!failed || attempt >= RetryBackoff.Length)
                {
                    if (failed)
                    {
                        _log.Warning("[Assets] Giving up on the failed packs for now; \"Try again\" or the next reconnect retries.");
                    }
                    return;
                }
                await PauseAsync(RetryBackoff[attempt], ct).ConfigureAwait(false);
                attempt++;
                while (!_signal.IsConnected)
                {
                    await PauseAsync(OfflineRetryPoll, ct).ConfigureAwait(false);
                }
                _snapshot = _snapshot with { Phase = AssetSyncRunPhase.Syncing };
                _checkedAtUtc = DateTime.MinValue;
                if (!await CheckAsync(ct).ConfigureAwait(false))
                {
                    FailPending();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[Assets] The sync run failed; the phone keeps what it has.");
            FailPending();
        }
        finally
        {
            AssetSyncReason queued;
            lock (_gate)
            {
                queued = _queued;
                _queued = AssetSyncReason.None;
                if (queued != AssetSyncReason.None && !_resetting)
                {
                    StartRunLocked(queued, RegularPackPause);
                }
            }
        }
    }

    /// <summary>One pass over every pack, smallest first and the bundle last, so the update screen's bar
    /// covers the whole download.</summary>
    private async Task<AssetSyncResult> SyncAsync(TimeSpan betweenPacks, CancellationToken ct)
    {
        var result = await AssetSyncEngine.SyncAsync(_library, _library, AssetSyncPolicy.Default, OnProgress,
                message => _log.Information("[Assets] {Message}", message),
                message => _log.Warning("[Assets] {Message}", message),
                ct,
                pauseBetweenPacks: betweenPacks)
            .ConfigureAwait(false);
        _local = _library.ReadLocalManifest() ?? _local;
        var snapshot = _snapshot;
        var packs = snapshot.Packs.Select(p =>
        {
            if (result.PacksSynced.Contains(p.Name, StringComparer.Ordinal))
            {
                return p with { State = AssetPackState.Done, Phase = AssetSyncPhase.Done, BytesDone = p.BytesTotal };
            }
            if (result.FailedPacks.Contains(p.Name, StringComparer.Ordinal))
            {
                return p with { State = AssetPackState.Failed, Phase = AssetSyncPhase.Failed };
            }
            return p;
        }).ToArray();
        foreach (var name in result.PacksSynced)
        {
            _completedPacks.Enqueue(name);
        }
        _snapshot = snapshot with
        {
            Packs = packs,
            RequiredPending = snapshot.RequiredPending && packs.Any(p => p.Required && p.State is AssetPackState.Pending or AssetPackState.Active or AssetPackState.Failed),
            DownloadedAnything = snapshot.DownloadedAnything || result.Downloaded.Length > 0,
        };
        return result;
    }

    private void OnProgress(AssetSyncProgress progress)
    {
        var snapshot = _snapshot;
        var packs = snapshot.Packs.Select(p =>
        {
            if (!string.Equals(p.Name, progress.Pack, StringComparison.Ordinal))
            {
                return p;
            }
            var state = progress.Phase switch
            {
                AssetSyncPhase.Done => AssetPackState.Done,
                AssetSyncPhase.Failed => AssetPackState.Failed,
                _ => AssetPackState.Active,
            };
            return p with
            {
                State = state,
                Phase = progress.Phase,
                BytesDone = progress.BytesDone,
                BytesTotal = progress.BytesTotal > 0 ? progress.BytesTotal : p.BytesTotal,
                FilesDone = progress.FilesDone,
                FilesTotal = progress.FilesTotal,
            };
        }).ToArray();
        _snapshot = snapshot with
        {
            Packs = packs,
            CurrentPack = progress.Phase is AssetSyncPhase.Done or AssetSyncPhase.Failed ? snapshot.CurrentPack : progress.Pack,
        };
    }
}
