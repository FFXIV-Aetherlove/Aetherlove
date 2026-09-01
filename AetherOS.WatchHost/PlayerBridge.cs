using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CefSharp;
using CefSharp.OffScreen;

namespace AetherOS.WatchHost;

/// <summary>A poll of the watch page's window.echo.getState(). Serialized straight onto the control pipe, so
/// it carries its own message tag.</summary>
internal sealed record PlayerState(
    [property: JsonPropertyName("ready")] bool Ready,
    [property: JsonPropertyName("videoId")] string? VideoId,
    [property: JsonPropertyName("time")] double Time,
    [property: JsonPropertyName("dur")] double Duration,
    [property: JsonPropertyName("playing")] bool Playing,
    [property: JsonPropertyName("buffering")] bool Buffering,
    [property: JsonPropertyName("ended")] bool Ended,
    [property: JsonPropertyName("error")] int? Error,
    [property: JsonPropertyName("epoch")] long Epoch,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("isLive")] bool IsLive = false,
    [property: JsonPropertyName("edge")] double Edge = 0d,
    [property: JsonPropertyName("h264")] bool? CanPlayH264 = null)
{
    [JsonPropertyName("t")]
    [JsonPropertyOrder(-1)]
    public string T => "state";
}

/// <summary>Drives the watch page through its window.echo bridge and polls it for state.
///
/// The page defines that bridge some time after navigation starts, so every command runs through a queue that
/// only opens once a state poll has been answered: a load issued during boot is held rather than lost.</summary>
internal sealed class PlayerBridge : IDisposable
{
    private const int PollIntervalMs = 500;
    private const int ScriptTimeoutMs = 2000;

    /// <summary>Roughly five seconds of failed polls at the interval above. Past this the page is not simply
    /// still booting, and the reason is worth saying out loud a second time.</summary>
    private const int PollFailuresBeforeAlarm = 10;
    private const string StateScript = "JSON.stringify(window.echo.getState())";

    private readonly ChromiumWebBrowser browser;
    private readonly object gate = new();
    private readonly Queue<string> pending = new();
    private readonly Timer poll;

    private bool ready;
    private long epoch;
    private int polling;
    private int pollFailures;
    private string lastStateSummary = "";

    /// <summary>Raised on a thread-pool thread every poll that produced readable state.</summary>
    public event Action<PlayerState>? StateChanged;

    /// <summary>Raised once, the first time the page reports ready.</summary>
    public event Action? PageReady;

    public event Action<string>? Log;

    public PlayerBridge(ChromiumWebBrowser browser)
    {
        this.browser = browser;
        this.poll = new Timer(OnPoll, null, Timeout.Infinite, Timeout.Infinite);

        // Navigation results are the difference between "the page is still booting" and "the watch page is
        // not there at all", and only the browser can tell us which.
        this.browser.LoadError += (_, e) =>
            Log?.Invoke($"Page failed to load: {e.FailedUrl} ({e.ErrorCode}) {e.ErrorText}");
        this.browser.LoadingStateChanged += (_, e) =>
        {
            if (!e.IsLoading)
            {
                Log?.Invoke($"Page finished loading: {this.browser.Address} " +
                    $"(canExecuteJs={this.browser.CanExecuteJavascriptInMainFrame})");
            }
        };
    }

    public void Start()
    {
        this.poll.Change(PollIntervalMs, PollIntervalMs);
    }

    /// <summary>Loads a reference from a named source. The source is passed as a fourth argument the page
    /// defaults to youtube when it is absent, so an older page still plays a YouTube entry from a newer
    /// host.</summary>
    public void Load(string videoId, double start, long epoch, string? source)
    {
        Interlocked.Exchange(ref this.epoch, epoch);
        Issue($"window.echo.load({Arg(videoId)}, {Arg(Math.Max(0d, Finite(start)))}, {Arg(epoch)}, "
            + $"{Arg(string.IsNullOrEmpty(source) ? "youtube" : source)})");
    }

    /// <summary>One genuine input event into the page, centred. Twitch's player refuses unmuted playback
    /// without real user activation, which scripted play() can never grant and this can: OSR input goes
    /// through the same pipeline as a human click. Sent once after a Twitch load; if it happens to land on
    /// an already-playing player and pauses it, the plugin's play correction resumes it within a second.</summary>
    public void ClickCenter(int width, int height)
    {
        try
        {
            var host = this.browser.GetBrowser()?.GetHost();
            if (host is null)
            {
                return;
            }
            var ev = new MouseEvent(width / 2, height / 2, CefEventFlags.None);
            host.SendMouseClickEvent(ev, MouseButtonType.Left, false, 1);
            host.SendMouseClickEvent(ev, MouseButtonType.Left, true, 1);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Activation click failed: {ex.Message}");
        }
    }

    public void Play()
    {
        Issue("window.echo.play()");
    }

    public void Pause()
    {
        Issue("window.echo.pause()");
    }

    public void Seek(double seconds)
    {
        Issue($"window.echo.seek({Arg(Math.Max(0d, Finite(seconds)))})");
    }

    /// <summary>Jumps a live stream to the front of its DVR window and resumes. The edge is resolved inside
    /// the page rather than passed in, because it moves while the command is in flight.</summary>
    public void ToLive()
    {
        Issue("window.echo.toLive()");
    }

    public void SetVolume(double volume)
    {
        Issue($"window.echo.setVolume({Arg(Math.Clamp(Finite(volume), 0d, 1d))})");
    }

    public void SetCaptions(bool on)
    {
        Issue($"window.echo.setCaptions({(on ? "true" : "false")})");
    }

    private static string Arg(string value) => JsonSerializer.Serialize(value);

    private static string Arg(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Arg(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static double Finite(double value) => double.IsFinite(value) ? value : 0d;

    private void Issue(string script)
    {
        lock (this.gate)
        {
            if (!this.ready)
            {
                this.pending.Enqueue(script);
                return;
            }
        }
        Execute(script);
    }

    private void Execute(string script)
    {
        if (!this.browser.IsBrowserInitialized)
        {
            Log?.Invoke($"Dropped '{script}': the browser is not initialized yet.");
            return;
        }
        _ = ExecuteAsync(script);
    }

    private async Task ExecuteAsync(string script)
    {
        try
        {
            var response = await EvaluateAsync(script).ConfigureAwait(false);
            if (!response.Success)
            {
                Log?.Invoke($"Script '{script}' failed: {response.Message}");
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Script '{script}' threw: {ex.Message}");
        }
    }

    /// <summary>Evaluates on the main frame directly rather than through the browser-level extension: that
    /// extension refuses whenever CanExecuteJavascriptInMainFrame is false, and the latch behind it is known
    /// to miss the render-process swap on the first real navigation, wedging an otherwise healthy page.</summary>
    private Task<JavascriptResponse> EvaluateAsync(string script)
    {
        var frame = this.browser.GetMainFrame()
            ?? throw new InvalidOperationException("The page has no main frame yet.");
        return frame.EvaluateScriptAsync(script, timeout: TimeSpan.FromMilliseconds(ScriptTimeoutMs));
    }

    private void OnPoll(object? state)
    {
        if (Interlocked.Exchange(ref this.polling, 1) == 1)
        {
            return;
        }
        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        try
        {
            if (!this.browser.IsBrowserInitialized)
            {
                // This was a silent return once, which made a wedged renderer look like an idle player.
                ReportPollProblem("browser not initialized yet");
                return;
            }

            var response = await EvaluateAsync(StateScript).ConfigureAwait(false);
            if (!response.Success)
            {
                ReportPollProblem($"getState failed: {response.Message}");
                return;
            }
            if (response.Result is not string raw)
            {
                ReportPollProblem($"getState returned {response.Result?.GetType().Name ?? "null"}, expected a JSON string.");
                return;
            }
            if (Parse(raw, Interlocked.Read(ref this.epoch)) is not { } parsed)
            {
                ReportPollProblem($"getState returned unreadable JSON: {Clip(raw)}");
                return;
            }
            ReportPollRecovered();
            LogStateShapeChange(parsed, raw);
            // Any answered poll proves window.echo exists, which is all the queue waits for. Gating on the
            // page's ready flag instead deadlocks: its player is only created by the first load, and that
            // load would be sitting in this queue.
            MarkReady();
            StateChanged?.Invoke(parsed);
        }
        catch (Exception ex)
        {
            ReportPollProblem($"getState threw: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref this.polling, 0);
        }
    }

    /// <summary>Reports a failing poll without flooding: the page not yet defining window.echo fails every
    /// tick, so the same reason is logged once and then again only if it persists past the grace below.
    /// Silence here was what made a page that never loaded indistinguishable from an idle player.</summary>
    private void ReportPollProblem(string reason)
    {
        var count = Interlocked.Increment(ref this.pollFailures);
        if (count == 1 || count == PollFailuresBeforeAlarm)
        {
            var prefix = count == 1 ? "Poll problem" : $"Poll still failing after {count} tries";
            Log?.Invoke($"{prefix}: {reason}");
        }
    }

    private void ReportPollRecovered()
    {
        if (Interlocked.Exchange(ref this.pollFailures, 0) >= PollFailuresBeforeAlarm)
        {
            Log?.Invoke("Poll recovered; the page is answering again.");
        }
    }

    private static string Clip(string value) =>
        value.Length <= 200 ? value : value[..200] + "...";

    /// <summary>Logs the page's own state whenever its shape changes. The plugin discards reports from a
    /// stale epoch before logging them, so during boot this is the only record of what the page thinks.</summary>
    private void LogStateShapeChange(PlayerState parsed, string raw)
    {
        var summary = $"{parsed.Ready}|{parsed.VideoId}|{parsed.Playing}|{parsed.Error}|{parsed.Epoch}|{(int)parsed.Duration}";
        if (summary == this.lastStateSummary)
        {
            return;
        }
        this.lastStateSummary = summary;
        Log?.Invoke($"Page state: {Clip(raw)}");
    }

    private void MarkReady()
    {
        string[] queued;
        lock (this.gate)
        {
            if (this.ready)
            {
                return;
            }
            this.ready = true;
            queued = this.pending.ToArray();
            this.pending.Clear();
        }

        Log?.Invoke($"Page bridge is answering; flushing {queued.Length} queued command(s).");
        foreach (var script in queued)
        {
            Execute(script);
        }
        PageReady?.Invoke();
    }

    /// <summary>getState is documented as returning a JSON string, so stringifying it yields a quoted string
    /// rather than an object. Unwrap that layer when it is what came back.</summary>
    private static PlayerState? Parse(string raw, long fallbackEpoch)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.String)
            {
                return Read(doc.RootElement, fallbackEpoch);
            }

            using var inner = JsonDocument.Parse(doc.RootElement.GetString() ?? string.Empty);
            return Read(inner.RootElement, fallbackEpoch);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PlayerState? Read(JsonElement root, long fallbackEpoch)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new PlayerState(
            JsonRead.Flag(root, "ready"),
            JsonRead.Text(root, "videoId"),
            JsonRead.Number(root, "time") ?? 0d,
            JsonRead.Number(root, "dur") ?? 0d,
            JsonRead.Flag(root, "playing"),
            JsonRead.Flag(root, "buffering"),
            JsonRead.Flag(root, "ended"),
            // The page latches YouTube's numeric onError code, so reading it as text silently discarded
            // every playback failure and the room looked merely idle.
            JsonRead.Int32(root, "error"),
            JsonRead.Integer(root, "epoch") ?? fallbackEpoch,
            JsonRead.Text(root, "title"),
            // Everything below is mapped by hand like the rest: a field the page starts sending reaches
            // the plugin only once it is read here. Forgetting that is how h264 arrived as false from a
            // page that had measured it true, and made a capable player report itself outdated.
            JsonRead.Flag(root, "isLive"),
            JsonRead.Number(root, "edge") ?? 0d,
            JsonRead.FlagOrNull(root, "h264"));
    }

    public void Dispose()
    {
        this.poll.Dispose();
    }
}
