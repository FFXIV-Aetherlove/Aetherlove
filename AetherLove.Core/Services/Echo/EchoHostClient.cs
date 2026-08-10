using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AetherLove.Services.Echo;

/// <summary>What the playback host last reported. <see cref="Epoch"/> is the load generation the report
/// belongs to; reports from an older generation never reach a consumer.</summary>
public sealed record EchoPlayerState(
    bool Ready,
    string? VideoId,
    double Time,
    double Duration,
    bool Playing,
    bool Buffering,
    bool Ended,
    int? Error,
    long Epoch,
    string? Title);

/// <summary>Failures raised by the client itself; the player's own error codes are non-negative.</summary>
public static class EchoHostErrors
{
    public const int Crashed = -1;
    public const int Protocol = -2;
}

/// <summary>Owns the playback host child process and the control pipe to it. The plugin is the pipe SERVER
/// and the host connects in as the client, so a stale orphan can never attach: names are freshly generated
/// per <see cref="Start"/>. The protocol is UTF-8 JSON Lines, one object per line.</summary>
public sealed class EchoHostClient : IDisposable
{
    private const int ProtocolVersion = 1;
    private const int ConnectTimeoutMs = 15000;
    private const int ShutdownGraceMs = 1500;
    private static readonly int[] RestartBackoffMs = [2000, 5000, 10000];
    private static readonly EchoPlayerState EmptyState = new(false, null, 0, 0, false, false, false, null, 0, null);

    private readonly object _gate = new();
    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private StreamWriter? _writer;
    private CancellationTokenSource? _life;

    private string _exePath = string.Empty;
    private string _pageUrl = string.Empty;
    private int _width = 1;
    private int _height = 1;
    private float _volume = 1f;
    private bool _captions;
    private string? _lastVideoId;
    private double _lastPosition;
    private int _restartAttempt;
    private long _epoch;

    private volatile bool _running;
    private volatile bool _connected;
    private volatile string? _mapName;
    private volatile string? _failureReason;
    private volatile EchoPlayerState? _lastState;

    /// <summary>The shared-memory name generated for the running host, handed to it on the command line.</summary>
    public string? MapName => _mapName;

    /// <summary>True once the host has connected and completed the handshake, until it stops or faults.</summary>
    public bool Alive => _running && _connected;

    public EchoPlayerState? LastState => _lastState;

    /// <summary>Set when the restart budget is spent; cleared by the next <see cref="Start"/>.</summary>
    public string? FailureReason => _failureReason;

    /// <summary>Raised on the pipe reader thread. Handlers MUST NOT touch ImGui: store the state and let the
    /// draw thread read <see cref="LastState"/>.</summary>
    public event Action<EchoPlayerState>? StateChanged;

    public void Start(string hostExePath, string watchPageUrl, int width, int height)
    {
        lock (_gate)
        {
            StopCore();
            _exePath = hostExePath;
            _pageUrl = watchPageUrl;
            _width = Math.Max(1, width);
            _height = Math.Max(1, height);
            _lastVideoId = null;
            _lastPosition = 0;
            _restartAttempt = 0;
            _failureReason = null;
            _lastState = null;
            Interlocked.Exchange(ref _epoch, 0);
            _running = true;
            Launch();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    /// <summary>Starts a new video. Every load opens a new epoch, so a state report still in flight for the
    /// previous video cannot trigger a spurious auto-advance or a correction seek against the wrong media.</summary>
    public void Load(string videoId, double startSeconds)
    {
        var start = Math.Max(0d, startSeconds);
        var epoch = Interlocked.Increment(ref _epoch);
        lock (_gate)
        {
            _lastVideoId = videoId;
            _lastPosition = start;
        }
        Send(new { t = "load", videoId, start, epoch });
    }

    public void Play() => Send(new { t = "play" });

    public void Pause() => Send(new { t = "pause" });

    public void Seek(double seconds)
    {
        var target = Math.Max(0d, seconds);
        lock (_gate)
        {
            _lastPosition = target;
        }
        Send(new { t = "seek", sec = target });
    }

    public void SetVolume(float v01)
    {
        var value = Math.Clamp(v01, 0f, 1f);
        lock (_gate)
        {
            _volume = value;
        }
        Send(new { t = "volume", v = value });
    }

    public void SetCaptions(bool on)
    {
        lock (_gate)
        {
            _captions = on;
        }
        Send(new { t = "captions", on });
    }

    public void SetSize(int w, int h)
    {
        var width = Math.Max(1, w);
        var height = Math.Max(1, h);
        lock (_gate)
        {
            if (_width == width && _height == height)
            {
                return;
            }
            _width = width;
            _height = height;
        }
        Send(new { t = "size", w = width, h = height });
    }

    public void Dispose() => Stop();

    private static void LogHostOutput(string? line, bool error)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }
        if (error)
        {
            UiHost.Log.Warning($"[Echo host stderr] {line}");
            return;
        }
        UiHost.Log.Information($"[Echo host stdout] {line}");
    }

    private static string ExitCodeOf(Process proc)
    {
        try
        {
            return proc.ExitCode.ToString(CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException)
        {
            return "unknown";
        }
    }

    private void Launch()
    {
        var pipeName = "aetheros-echo-" + Guid.NewGuid().ToString("N");
        var mapName = "aetheros-echo-map-" + Guid.NewGuid().ToString("N");
        var life = new CancellationTokenSource();
        _life = life;
        _mapName = mapName;
        try
        {
            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            _pipe = pipe;

            var psi = new ProcessStartInfo
            {
                FileName = _exePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_exePath) ?? string.Empty,
                // Without these the host's own diagnostics, and anything CEF prints on its way to failing,
                // go nowhere: the only symptom left is a player that never starts.
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            // The host ties its life to ours: it exits when this pid does, so a game crash leaves no orphan
            // browser behind.
            psi.ArgumentList.Add("--parent");
            psi.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--pipe");
            psi.ArgumentList.Add(pipeName);
            psi.ArgumentList.Add("--map");
            psi.ArgumentList.Add(mapName);
            psi.ArgumentList.Add("--url");
            psi.ArgumentList.Add(_pageUrl);
            psi.ArgumentList.Add("--w");
            psi.ArgumentList.Add(_width.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--h");
            psi.ArgumentList.Add(_height.ToString(CultureInfo.InvariantCulture));

            UiHost.Log.Information(
                $"[Echo] Launching the playback host.\n  exe  {_exePath}\n  url  {_pageUrl}\n" +
                $"  size {_width}x{_height}\n  pipe {pipeName}\n  map  {mapName}");

            var proc = Process.Start(psi);
            if (proc is null)
            {
                Fault("The playback host did not start.", life);
                return;
            }
            proc.EnableRaisingEvents = true;
            proc.OutputDataReceived += (_, args) => LogHostOutput(args.Data, error: false);
            proc.ErrorDataReceived += (_, args) => LogHostOutput(args.Data, error: true);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.Exited += (_, _) =>
            {
                UiHost.Log.Warning($"[Echo] The playback host exited with code {ExitCodeOf(proc)}.");
                Fault("The playback host exited.", life);
            };
            _process = proc;
            _ = Task.Run(() => RunPipeAsync(pipe, life));
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Echo] Failed to launch the playback host.");
            Fault(ex.Message, life);
        }
    }

    private async Task RunPipeAsync(NamedPipeServerStream pipe, CancellationTokenSource life)
    {
        // Captured once: the source is disposed by the reaper while this loop may still be unwinding.
        var token = life.Token;
        try
        {
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                connect.CancelAfter(ConnectTimeoutMs);
                await pipe.WaitForConnectionAsync(connect.Token).ConfigureAwait(false);
            }

            var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            var reader = new StreamReader(pipe, new UTF8Encoding(false));
            lock (_gate)
            {
                if (!ReferenceEquals(_life, life))
                {
                    return;
                }
                _writer = writer;
            }

            UiHost.Log.Information("[Echo] The playback host connected to the control pipe.");

            var hello = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (!IsHello(hello))
            {
                UiHost.Log.Error($"[Echo] Expected a protocol v{ProtocolVersion} hello, got: {hello ?? "nothing"}");
                Fault("The playback host reported an incompatible protocol.", life, EchoHostErrors.Protocol);
                return;
            }
            UiHost.Log.Information($"[Echo] Handshake complete: {hello}");
            _connected = true;
            Resume();

            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }
                Handle(line);
            }
            Fault("The playback host closed the control pipe.", life);
        }
        catch (OperationCanceledException)
        {
            Fault("The playback host did not connect in time.", life);
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Echo] The control pipe faulted.");
            Fault(ex.Message, life);
        }
    }

    /// <summary>Re-applies the session after a (re)connect so a restarted host lands back on the same video
    /// at the position the previous one reached.</summary>
    private void Resume()
    {
        string? videoId;
        double position;
        float volume;
        bool captions;
        int width;
        int height;
        lock (_gate)
        {
            _restartAttempt = 0;
            videoId = _lastVideoId;
            position = _lastPosition;
            volume = _volume;
            captions = _captions;
            width = _width;
            height = _height;
        }
        Send(new { t = "size", w = width, h = height });
        Send(new { t = "volume", v = volume });
        Send(new { t = "captions", on = captions });
        if (videoId is { Length: > 0 })
        {
            Load(videoId, position);
        }
    }

    private static bool IsHello(string? line)
    {
        if (line is null)
        {
            return false;
        }
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            return root.TryGetProperty("t", out var t)
                && t.GetString() == "hello"
                && root.TryGetProperty("v", out var v)
                && v.TryGetInt32(out var version)
                && version == ProtocolVersion;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void Handle(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var type = root.TryGetProperty("t", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "ready":
                    Publish((_lastState ?? EmptyState) with { Ready = true, Error = null });
                    break;
                case "state":
                    var epoch = root.TryGetProperty("epoch", out var e) && e.ValueKind == JsonValueKind.Number
                        && e.TryGetInt64(out var value) ? value : 0;
                    var current = Interlocked.Read(ref _epoch);
                    if (epoch != current)
                    {
                        UiHost.Log.Verbose($"[Echo] Dropped a state from epoch {epoch}; current is {current}.");
                        return;
                    }
                    var state = new EchoPlayerState(
                        true,
                        Str(root, "videoId"),
                        Num(root, "time"),
                        Num(root, "dur"),
                        Flag(root, "playing"),
                        Flag(root, "buffering"),
                        Flag(root, "ended"),
                        // TryGetInt32 THROWS on a JSON null rather than returning false, and error is null
                        // whenever playback is healthy.
                        root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Number
                            && err.TryGetInt32(out var code) ? code : null,
                        epoch,
                        Str(root, "title"));
                    lock (_gate)
                    {
                        _lastPosition = state.Time;
                    }
                    LogStateChange(state);
                    Publish(state);
                    break;
                case "log":
                    UiHost.Log.Information($"[Echo host] {Str(root, "msg")}");
                    break;
                default:
                    UiHost.Log.Warning($"[Echo] Unknown message from the playback host: {line}");
                    break;
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Echo] Unreadable message from the playback host.");
        }
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static double Num(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
            && el.TryGetDouble(out var value) ? value : 0;

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.True;

    /// <summary>Logs the state poll only when something a human would notice changed. The poll runs twice a
    /// second, so logging every one buries the log and tells you nothing.</summary>
    private void LogStateChange(EchoPlayerState state)
    {
        var previous = _lastState;
        if (previous is not null
            && previous.Ready == state.Ready
            && previous.Playing == state.Playing
            && previous.Buffering == state.Buffering
            && previous.Ended == state.Ended
            && previous.Error == state.Error
            && previous.VideoId == state.VideoId
            && Math.Abs(previous.Duration - state.Duration) < 0.5)
        {
            return;
        }
        UiHost.Log.Information(
            $"[Echo] player video={state.VideoId ?? "none"} ready={state.Ready} playing={state.Playing} " +
            $"buffering={state.Buffering} ended={state.Ended} error={(state.Error?.ToString() ?? "none")} " +
            $"time={state.Time:0.0} dur={state.Duration:0.0} title={state.Title ?? "none"}");
    }

    private void Publish(EchoPlayerState state)
    {
        _lastState = state;
        StateChanged?.Invoke(state);
    }

    private void Send(object message)
    {
        StreamWriter? writer;
        CancellationTokenSource? life;
        lock (_gate)
        {
            writer = _writer;
            life = _life;
        }
        var line = JsonSerializer.Serialize(message, message.GetType());
        if (writer is null)
        {
            UiHost.Log.Warning($"[Echo] Dropped a command, the control pipe is not up yet: {line}");
            return;
        }
        try
        {
            UiHost.Log.Information($"[Echo] -> host {line}");
            writer.WriteLine(line);
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Echo] Control pipe write failed.");
            Fault(ex.Message, life);
        }
    }

    /// <summary>Tears the current host down and restarts it on a backoff; once the budget is spent the client
    /// stays down and surfaces the failure. Calls for an already-replaced host are ignored.</summary>
    private void Fault(string reason, CancellationTokenSource? life, int errorCode = EchoHostErrors.Crashed)
    {
        EchoPlayerState? failed = null;
        lock (_gate)
        {
            if (!_running || life is null || !ReferenceEquals(_life, life))
            {
                return;
            }
            TeardownCurrent();
            if (_restartAttempt >= RestartBackoffMs.Length)
            {
                _running = false;
                _failureReason = reason;
                failed = (_lastState ?? EmptyState) with { Ready = false, Playing = false, Buffering = false, Error = errorCode };
                _lastState = failed;
            }
            else
            {
                var delay = RestartBackoffMs[_restartAttempt++];
                UiHost.Log.Warning($"[Echo] {reason} Restarting the playback host in {delay} ms.");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (_running)
                        {
                            Launch();
                        }
                    }
                });
            }
        }
        if (failed is not null)
        {
            UiHost.Log.Warning($"[Echo] {reason} The playback host will not be restarted.");
            StateChanged?.Invoke(failed);
        }
    }

    private void StopCore()
    {
        if (!_running && _process is null && _pipe is null)
        {
            return;
        }
        _running = false;
        if (_connected)
        {
            Send(new { t = "shutdown" });
        }
        TeardownCurrent();
    }

    /// <summary>Detaches the current process and pipe and reaps them off the draw thread; waiting for the
    /// host to exit inline would hitch the frame.</summary>
    private void TeardownCurrent()
    {
        var proc = _process;
        var pipe = _pipe;
        var life = _life;
        _process = null;
        _pipe = null;
        _writer = null;
        _life = null;
        _connected = false;
        _mapName = null;
        if (proc is null && pipe is null && life is null)
        {
            return;
        }
        _ = Task.Run(() =>
        {
            try
            {
                life?.Cancel();
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[Echo] Cancelling the host session threw: {ex.Message}");
            }
            try
            {
                if (proc is not null && !proc.WaitForExit(ShutdownGraceMs))
                {
                    proc.Kill(true);
                }
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[Echo] Stopping the host process threw: {ex.Message}");
            }
            proc?.Dispose();
            try
            {
                pipe?.Dispose();
            }
            catch (Exception ex)
            {
                UiHost.Log.Debug($"[Echo] Disposing the control pipe threw: {ex.Message}");
            }
            life?.Dispose();
        });
    }
}
