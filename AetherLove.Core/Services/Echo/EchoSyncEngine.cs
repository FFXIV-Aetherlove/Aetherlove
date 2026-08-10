using System;
using AetherLove.Shared.EchoVidya;

namespace AetherLove.Services.Echo;

/// <summary>Reconciles the local playback host against the room's authoritative playback: loads the current
/// entry, matches play/pause, and nudges the position back when it drifts. Every method runs on the draw
/// thread.</summary>
public sealed class EchoSyncEngine
{
    /// <summary>How far the host may sit from the room's position before it is seeked back.</summary>
    public const double DriftToleranceSeconds = 2.0;

    /// <summary>Quiet window after a correction; without it the engine fights its own not-yet-applied seek.</summary>
    private const double CorrectionCooldownSeconds = 1.5;

    /// <summary>Quiet window after a load, long enough for the player to fetch and start the new media.</summary>
    private const double LoadSettleSeconds = 3.0;

    private readonly EchoHostClient _host;
    private readonly EchoStateService _state;

    private DateTimeOffset _lastCorrection;
    private DateTimeOffset _lastLoad;
    private Guid _loadedEntryId;
    private Guid _finishedEntryId;

    public EchoSyncEngine(EchoHostClient host, EchoStateService state)
    {
        _host = host;
        _state = state;
    }

    /// <summary>Off while the host is missing, installing, or the user muted sync; <see cref="Tick"/> is a
    /// no-op then.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The host reached the end of the current entry (or could not play it, second argument true).
    /// The app turns this into AdvanceEchoPlaylistAsync; it fires once per entry.</summary>
    public event Action<Guid, bool>? EntryFinished;

    /// <summary>Where the room should be right now: a paused room sits still, a playing one has advanced by
    /// the time since the server stamped the position.</summary>
    public static double ExpectedPosition(EchoPlaybackDto playback, DateTimeOffset now)
    {
        var elapsed = playback.IsPlaying ? (now - playback.UpdatedAtUtc).TotalSeconds : 0d;
        if (elapsed < 0)
        {
            elapsed = 0;
        }
        return Math.Max(0d, playback.PositionSeconds + elapsed);
    }

    /// <summary>Drops the settle windows so the next tick acts on the new target immediately.</summary>
    public void OnPlaybackChanged(EchoPlaybackDto playback)
    {
        if (playback.CurrentEntryId != _loadedEntryId)
        {
            _finishedEntryId = Guid.Empty;
        }
        _lastCorrection = DateTimeOffset.MinValue;
    }

    /// <summary>Forgets what the host is playing; call when leaving a room or stopping the host.</summary>
    public void Reset()
    {
        _loadedEntryId = Guid.Empty;
        _finishedEntryId = Guid.Empty;
        _lastCorrection = DateTimeOffset.MinValue;
        _lastLoad = DateTimeOffset.MinValue;
    }

    public void Tick(DateTimeOffset now)
    {
        if (!Enabled || !_host.Alive)
        {
            return;
        }
        var playback = _state.Playback;
        var host = _host.LastState;
        if (playback is null || host is null || !host.Ready)
        {
            return;
        }

        var entry = _state.CurrentEntry;
        if (entry is null || entry.Failed)
        {
            if (host.Playing)
            {
                _host.Pause();
            }
            return;
        }

        var staleMedia = (now - _lastLoad).TotalSeconds > LoadSettleSeconds
            && host.VideoId is { Length: > 0 }
            && !string.Equals(host.VideoId, entry.VideoId, StringComparison.Ordinal);
        if (_loadedEntryId != entry.Id || staleMedia)
        {
            _loadedEntryId = entry.Id;
            _finishedEntryId = Guid.Empty;
            _lastLoad = now;
            _lastCorrection = now;
            _host.Load(entry.VideoId, ExpectedPosition(playback, now));
            return;
        }

        if (_finishedEntryId != entry.Id && (host.Ended || host.Error is not null))
        {
            _finishedEntryId = entry.Id;
            EntryFinished?.Invoke(entry.Id, host.Error is not null);
            return;
        }

        if (host.Buffering || (now - _lastLoad).TotalSeconds < LoadSettleSeconds)
        {
            return;
        }

        if (playback.IsPlaying != host.Playing)
        {
            if (playback.IsPlaying)
            {
                _host.Play();
            }
            else
            {
                _host.Pause();
            }
            _lastCorrection = now;
            return;
        }

        if ((now - _lastCorrection).TotalSeconds < CorrectionCooldownSeconds)
        {
            return;
        }
        var expected = ExpectedPosition(playback, now);
        if (Math.Abs(host.Time - expected) > DriftToleranceSeconds)
        {
            _lastCorrection = now;
            _host.Seek(expected);
        }
    }
}
