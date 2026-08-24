using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AetherOS.Apps.Groove;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace AetherLove.Os.Groove;

/// <summary>The Windows backend: GSMTC sessions plus events, coalesced with a 1s poll because several
/// sources (notoriously browsers) drop change events. Every WinRT touch is guarded; on any activation
/// failure the backend stays a permanent empty state rather than taking the game down.</summary>
internal sealed class GsmtcBackend : IMediaBackend
{
    private const int RebuildIntervalMillis = 1_000;
    private const int ControlFollowUpMillis = 250;

    private readonly GrooveVolumeMixer _mixer = new();
    private readonly ConcurrentDictionary<string, ArtEntry> _art = new();
    private readonly ConcurrentQueue<(IDalamudTextureWrap Wrap, long DiscardAtTick)> _retiredArt = new();
    private readonly Timer _timer;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private volatile Dictionary<string, GlobalSystemMediaTransportControlsSession> _live = new();
    private readonly Dictionary<string, (string Title, string Artist, string Album)> _artKeys = new();
    private volatile GrooveSession[] _sessions = [];
    private volatile string? _systemCurrentId;
    private volatile bool _ready;
    private volatile bool _unavailable;
    private volatile bool _disposed;
    private int _rebuilding;
    private int _starting;
    private long _tick;

    private sealed record ArtEntry(int Version, byte[] Hash, IDalamudTextureWrap? Wrap);

    public GsmtcBackend()
    {
        _timer = new Timer(_ => ScheduleRebuild(), null, Timeout.Infinite, Timeout.Infinite);
        Start();
    }

    private void Start()
    {
        if (Interlocked.CompareExchange(ref _starting, 1, 0) != 0)
        {
            return;
        }
        _ = Task.Run(StartAsync);
    }

    public bool Ready => _ready;

    public bool ReducedTier => false;

    public bool BridgeUnavailable => _unavailable;

    public string? SystemCurrentId => _systemCurrentId;

    public IReadOnlyList<GrooveSession> Sessions => _sessions;

    private async Task StartAsync()
    {
        try
        {
            WinRtBootstrap.EnsureComWrappers();
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed)
            {
                return;
            }
            _manager = manager;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            manager.SessionsChanged += OnSessionsChanged;
            _unavailable = false;
            _ready = true;
            _timer.Change(0, RebuildIntervalMillis);
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Groove] GSMTC activation failed; the app shows its unavailable card.");
            _unavailable = true;
            _ready = true;
        }
        finally
        {
            Interlocked.Exchange(ref _starting, 0);
        }
    }

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        ScheduleRebuild();

    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        ScheduleRebuild();

    private void ScheduleRebuild()
    {
        if (_disposed)
        {
            return;
        }
        if (Interlocked.CompareExchange(ref _rebuilding, 1, 0) != 0)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await RebuildAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug(ex, "[Groove] Session rebuild failed.");
            }
            finally
            {
                Interlocked.Exchange(ref _rebuilding, 0);
            }
        });
    }

    private async Task RebuildAsync()
    {
        var manager = _manager;
        if (manager is null || _disposed)
        {
            return;
        }
        Interlocked.Increment(ref _tick);
        DrainRetiredArt();
        _mixer.Refresh();

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> raw;
        string? currentId;
        try
        {
            raw = manager.GetSessions();
            currentId = manager.GetCurrentSession()?.SourceAppUserModelId;
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[Groove] Session enumeration failed.");
            return;
        }

        var live = new Dictionary<string, GlobalSystemMediaTransportControlsSession>();
        var built = new List<GrooveSession>(raw.Count);
        foreach (var session in raw)
        {
            var snapshot = await BuildSessionAsync(session).ConfigureAwait(false);
            if (snapshot is null)
            {
                continue;
            }
            // First session wins on a duplicate AUMID; controls target that instance.
            if (live.TryAdd(snapshot.SessionId, session))
            {
                built.Add(snapshot);
            }
        }

        var previous = _live;
        _live = live;
        DetachDropped(previous, live);

        foreach (var gone in _artKeys.Keys.Where(id => !live.ContainsKey(id)).ToArray())
        {
            _artKeys.Remove(gone);
            if (_art.TryRemove(gone, out var entry) && entry.Wrap is not null)
            {
                _retiredArt.Enqueue((entry.Wrap, Interlocked.Read(ref _tick) + 2));
            }
        }

        _sessions = built.ToArray();
        _systemCurrentId = currentId;
    }

    private async Task<GrooveSession?> BuildSessionAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var id = session.SourceAppUserModelId;
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var props = await session.TryGetMediaPropertiesAsync();

            var title = props?.Title ?? string.Empty;
            var artist = props?.Artist ?? string.Empty;
            var album = props?.AlbumTitle ?? string.Empty;
            var artVersion = await RefreshArtAsync(id, title, artist, album, props?.Thumbnail).ConfigureAwait(false);

            var controls = playback.Controls;
            var duration = timeline.EndTime - timeline.StartTime;
            return new GrooveSession(
                id,
                FriendlyLabel(id),
                title,
                artist,
                album,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                controls.IsPlayEnabled || controls.IsPauseEnabled || controls.IsPlayPauseToggleEnabled,
                controls.IsPlaybackPositionEnabled && duration > TimeSpan.Zero,
                controls.IsShuffleEnabled,
                controls.IsRepeatEnabled,
                _mixer.HasSession(id),
                playback.IsShuffleActive ?? false,
                MapRepeat(playback.AutoRepeatMode),
                timeline.Position,
                duration,
                timeline.LastUpdatedTime,
                artVersion);
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[Groove] Reading a media session failed.");
            return null;
        }
    }

    /// <summary>Re-reads the thumbnail only when the track metadata changed, then re-creates the texture
    /// only when the bytes actually differ. Returns the session's current art version, 0 for no art.</summary>
    private async Task<int> RefreshArtAsync(string id, string title, string artist, string album,
        IRandomAccessStreamReference? thumbnail)
    {
        var key = (title, artist, album);
        if (_artKeys.TryGetValue(id, out var known) && known == key)
        {
            return _art.TryGetValue(id, out var current) ? current.Version : 0;
        }
        _artKeys[id] = key;

        if (thumbnail is null)
        {
            RetireArt(id);
            return 0;
        }

        byte[] bytes;
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            using var reader = stream.AsStreamForRead();
            using var buffer = new MemoryStream();
            await reader.CopyToAsync(buffer).ConfigureAwait(false);
            bytes = buffer.ToArray();
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[Groove] Thumbnail read failed.");
            RetireArt(id);
            return 0;
        }
        if (bytes.Length == 0)
        {
            RetireArt(id);
            return 0;
        }

        var hash = SHA1.HashData(bytes);
        if (_art.TryGetValue(id, out var existing) && hash.AsSpan().SequenceEqual(existing.Hash))
        {
            return existing.Version;
        }

        // The whole plugin's texture provider goes with the unload, and GSMTC keeps handing us thumbnails
        // for a moment after: creating one here would throw on a disposed provider.
        if (_disposed)
        {
            return existing?.Version ?? 0;
        }

        try
        {
            var wrap = await UiHost.TextureProvider.CreateFromImageAsync(bytes).ConfigureAwait(false);
            if (_disposed)
            {
                wrap.Dispose();
                return existing?.Version ?? 0;
            }
            var version = (existing?.Version ?? 0) + 1;
            if (existing?.Wrap is not null)
            {
                _retiredArt.Enqueue((existing.Wrap, Interlocked.Read(ref _tick) + 2));
            }
            _art[id] = new ArtEntry(version, hash, wrap);
            return version;
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[Groove] Thumbnail decode failed.");
            return existing?.Version ?? 0;
        }
    }

    private void RetireArt(string id)
    {
        if (_art.TryRemove(id, out var entry) && entry.Wrap is not null)
        {
            _retiredArt.Enqueue((entry.Wrap, Interlocked.Read(ref _tick) + 2));
        }
    }

    /// <summary>Replaced art is disposed two rebuild ticks after retirement, far beyond any in-flight
    /// frame that might still reference the handle.</summary>
    private void DrainRetiredArt()
    {
        var tick = Interlocked.Read(ref _tick);
        while (_retiredArt.TryPeek(out var item) && item.DiscardAtTick <= tick)
        {
            if (_retiredArt.TryDequeue(out item))
            {
                item.Wrap.Dispose();
            }
        }
    }

    private void DetachDropped(Dictionary<string, GlobalSystemMediaTransportControlsSession> previous,
        Dictionary<string, GlobalSystemMediaTransportControlsSession> live)
    {
        foreach (var (id, session) in previous)
        {
            if (live.TryGetValue(id, out var kept) && ReferenceEquals(kept, session))
            {
                continue;
            }
            try
            {
                session.MediaPropertiesChanged -= OnSessionChanged;
                session.PlaybackInfoChanged -= OnSessionChanged;
                session.TimelinePropertiesChanged -= OnSessionChanged;
            }
            catch (Exception)
            {
            }
        }
        foreach (var (id, session) in live)
        {
            if (previous.TryGetValue(id, out var old) && ReferenceEquals(old, session))
            {
                continue;
            }
            try
            {
                session.MediaPropertiesChanged += OnSessionChanged;
                session.PlaybackInfoChanged += OnSessionChanged;
                session.TimelinePropertiesChanged += OnSessionChanged;
            }
            catch (Exception)
            {
            }
        }
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => ScheduleRebuild();

    public ImTextureID? Art(string sessionId) =>
        _art.TryGetValue(sessionId, out var entry) ? entry.Wrap?.Handle : null;

    public void TogglePlayPause(string sessionId) =>
        Control(sessionId, s => s.TryTogglePlayPauseAsync());

    public void Next(string sessionId) =>
        Control(sessionId, s => s.TrySkipNextAsync());

    public void Previous(string sessionId) =>
        Control(sessionId, s => s.TrySkipPreviousAsync());

    public void SetShuffle(string sessionId, bool on) =>
        Control(sessionId, s => s.TryChangeShuffleActiveAsync(on));

    public void CycleRepeat(string sessionId)
    {
        var current = _sessions.FirstOrDefault(s => s.SessionId == sessionId)?.Repeat ?? GrooveRepeat.Off;
        var next = current switch
        {
            GrooveRepeat.Off => MediaPlaybackAutoRepeatMode.List,
            GrooveRepeat.All => MediaPlaybackAutoRepeatMode.Track,
            _ => MediaPlaybackAutoRepeatMode.None,
        };
        Control(sessionId, s => s.TryChangeAutoRepeatModeAsync(next));
    }

    public void SeekTo(string sessionId, TimeSpan position) =>
        Control(sessionId, s => s.TryChangePlaybackPositionAsync(position.Ticks));

    public float? GetVolume(string sessionId) => _mixer.GetVolume(sessionId);

    public void SetVolume(string sessionId, float volume) => _mixer.SetVolume(sessionId, volume);

    public void Retry()
    {
        if (_manager is null)
        {
            Start();
        }
    }

    /// <summary>Runs a control call off-thread and schedules a near rebuild so the UI snaps even when the
    /// source app never raises its change event.</summary>
    private void Control(string sessionId, Func<GlobalSystemMediaTransportControlsSession, global::Windows.Foundation.IAsyncOperation<bool>> action)
    {
        if (!_live.TryGetValue(sessionId, out var session))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await action(session);
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug(ex, "[Groove] Media control call failed.");
            }
            await Task.Delay(ControlFollowUpMillis).ConfigureAwait(false);
            ScheduleRebuild();
        });
    }

    private static GrooveRepeat MapRepeat(MediaPlaybackAutoRepeatMode? mode) => mode switch
    {
        MediaPlaybackAutoRepeatMode.Track => GrooveRepeat.One,
        MediaPlaybackAutoRepeatMode.List => GrooveRepeat.All,
        _ => GrooveRepeat.Off,
    };

    /// <summary>"Spotify.exe" reads as "Spotify"; a store AUMID like "...!App" keeps its last segment.</summary>
    internal static string FriendlyLabel(string aumid)
    {
        if (string.IsNullOrEmpty(aumid))
        {
            return aumid;
        }
        var label = aumid;
        var bang = label.LastIndexOf('!');
        if (bang >= 0 && bang < label.Length - 1)
        {
            label = label[(bang + 1)..];
        }
        if (label.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            label = label[..^4];
        }
        return label;
    }

    public void Dispose()
    {
        _disposed = true;
        if (_manager is { } manager)
        {
            try
            {
                manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                manager.SessionsChanged -= OnSessionsChanged;
            }
            catch (Exception)
            {
            }
            _manager = null;
        }
        _timer.Dispose();
        DetachDropped(_live, new Dictionary<string, GlobalSystemMediaTransportControlsSession>());
        _mixer.Dispose();
        foreach (var entry in _art.Values)
        {
            entry.Wrap?.Dispose();
        }
        _art.Clear();
        while (_retiredArt.TryDequeue(out var item))
        {
            item.Wrap.Dispose();
        }
    }
}
