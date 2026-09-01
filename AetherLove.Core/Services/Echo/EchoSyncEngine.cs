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

    /// <summary>Quiet window between play/pause corrections. This branch used to run unguarded on every
    /// frame, so an entry the player could not start (a source the host does not understand, a stream that
    /// refuses to begin) produced sixty play commands a second forever. Short enough that a real pause in
    /// a room still lands promptly.</summary>
    private const double TransportRetrySeconds = 0.5;

    /// <summary>Quiet window after a load, long enough for the player to fetch and start the new media.</summary>
    private const double LoadSettleSeconds = 3.0;

    private readonly EchoHostClient _host;
    private readonly EchoStateService _state;

    private DateTimeOffset _lastCorrection;
    private DateTimeOffset _lastTransport;
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

    /// <summary>The live entry THIS player failed on, or <c>Guid.Empty</c>. A live stream failure is
    /// overwhelmingly a local decoder gap (live is H.264 with AAC, which a host built without proprietary
    /// codecs cannot touch), so the entry is never reported as failed to the room: that would mark it
    /// unplayable for every member, including the ones whose player shows it fine. Decided from an ACTUAL
    /// playback error, never from capability probing: MediaSource.isTypeSupported answered false inside the
    /// game-launched host while that same host was visibly decoding the stream.</summary>
    public Guid UndecodableEntryId { get; private set; }

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
        UndecodableEntryId = Guid.Empty;
        _lastTransport = DateTimeOffset.MinValue;
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
            // A broadcast has no shared timeline to start from: everyone joins at the front of it.
            _host.Load(entry.VideoId, entry.IsLive ? 0d : ExpectedPosition(playback, now),
                EchoMediaRefs.WireName((EchoMediaSource)entry.Source));
            return;
        }

        // Only the report for the load we are actually waiting on can end this entry. LastState keeps
        // describing the PREVIOUS video until the host answers, so without this an Ended left over from the
        // video that just finished immediately ends its successor too, and the room walks the whole playlist
        // a frame at a time.
        var current = host.Epoch == _host.Epoch;
        if (current && entry.IsLive && host.Error is not null)
        {
            UndecodableEntryId = entry.Id;
            return;
        }
        UndecodableEntryId = Guid.Empty;

        if (current && _finishedEntryId != entry.Id && (host.Ended || host.Error is not null))
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
            if ((now - _lastTransport).TotalSeconds < TransportRetrySeconds)
            {
                return;
            }
            _lastTransport = now;
            if (playback.IsPlaying)
            {
                if (entry.IsLive)
                {
                    JumpToLive(host);
                }
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
        // A live stream is already the same instant for everyone, and the room's stored position describes
        // nothing: one viewer's seconds-into-the-buffer is not another's. Correcting it would seek people
        // to a meaningless time and, because seeking backwards drops you out of the broadcast, would do it
        // again every tick.
        if (entry.IsLive)
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

    /// <summary>Returns a live stream to the front after a pause. Only a real seekable edge is seeked to:
    /// falling back to the reported DURATION seeks a Twitch channel to its own elapsed running time, which
    /// stalls the player behind its play overlay. A source with no edge (a live channel has nowhere to seek)
    /// resumes on the command alone, which is what the front of it means there.</summary>
    private void JumpToLive(EchoPlayerState host)
    {
        _host.ToLive();
        if (host.LiveEdge > 0)
        {
            _host.Seek(host.LiveEdge);
        }
    }
}
