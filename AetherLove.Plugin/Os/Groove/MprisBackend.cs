using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AetherOS.Apps.Groove;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;

namespace AetherLove.Os.Groove;

/// <summary>The Wine courtesy tier: GSMTC does not exist under Wine, so a host-side playerctl loop
/// (spawned through Wine's start.exe /unix escape) writes MPRIS now-playing lines to /tmp, and the plugin
/// polls the file back through the Z: drive mapping. Reduced by design: metadata plus transport, honest
/// capability flags for everything else.</summary>
internal sealed class MprisBackend : IMediaBackend
{
    private const string HostDir = "/tmp/aetherlove-groove";
    private const string WineDir = @"Z:\tmp\aetherlove-groove";
    private const int PollMillis = 1_500;
    private const int ProbeTimeoutSeconds = 15;
    private const string SessionId = "mpris";

    private readonly Timer _timer;
    private volatile GrooveSession[] _sessions = [];
    private volatile bool _ready;
    private volatile bool _unavailable;
    private string _token = string.Empty;
    private DateTime _spawnedAtUtc;
    private byte[] _artHash = [];
    private IDalamudTextureWrap? _art;
    private int _artVersion;

    public MprisBackend()
    {
        // Mac hosts have no playerctl either; the probe below times out into the same friendly card,
        // so no separate platform check is needed.
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
        Spawn();
    }

    public bool Ready => _ready;

    public bool ReducedTier => true;

    public bool BridgeUnavailable => _unavailable;

    public string? SystemCurrentId => _sessions.Length > 0 ? SessionId : null;

    public IReadOnlyList<GrooveSession> Sessions => _sessions;

    /// <summary>One feeder loop on the host, guarded by a per-session token: a new spawn rewrites the
    /// token file, so an orphaned loop from a crashed game exits within one tick on its own.</summary>
    private void Spawn()
    {
        _token = Guid.NewGuid().ToString("N");
        _spawnedAtUtc = DateTime.UtcNow;
        _unavailable = false;
        var loop =
            $"d={HostDir}; mkdir -p $d; echo {_token} > $d/run; " +
            $"while [ \\\"$(cat $d/run 2>/dev/null)\\\" = \\\"{_token}\\\" ]; do " +
            "playerctl metadata --format '{{status}}\\t{{title}}\\t{{artist}}\\t{{album}}\\t{{mpris:artUrl}}\\t{{position}}\\t{{mpris:length}}' > $d/np.tmp 2>/dev/null && mv $d/np.tmp $d/np.txt; " +
            "sleep 2; done";
        RunHost(loop, waitless: true);
        _timer.Change(PollMillis, PollMillis);
    }

    private void Poll()
    {
        try
        {
            var path = Path.Combine(WineDir, "np.txt");
            if (!File.Exists(path))
            {
                CheckProbe();
                return;
            }
            var stampUtc = File.GetLastWriteTimeUtc(path);
            if (DateTime.UtcNow - stampUtc > TimeSpan.FromSeconds(ProbeTimeoutSeconds))
            {
                CheckProbe(stale: true);
                return;
            }
            _ready = true;
            _unavailable = false;

            var line = File.ReadAllText(path).Trim();
            if (line.Length == 0)
            {
                _sessions = [];
                return;
            }
            var parts = line.Split('\t');
            var status = parts.Length > 0 ? parts[0] : string.Empty;
            var title = parts.Length > 1 ? parts[1] : string.Empty;
            var artist = parts.Length > 2 ? parts[2] : string.Empty;
            var album = parts.Length > 3 ? parts[3] : string.Empty;
            var artUrl = parts.Length > 4 ? parts[4] : string.Empty;
            var position = ParseMicros(parts.Length > 5 ? parts[5] : string.Empty);
            var duration = ParseMicros(parts.Length > 6 ? parts[6] : string.Empty);
            RefreshArt(artUrl);

            _sessions =
            [
                new GrooveSession(
                    SessionId,
                    "MPRIS",
                    title,
                    artist,
                    album,
                    string.Equals(status, "Playing", StringComparison.OrdinalIgnoreCase),
                    CanControl: true,
                    CanSeek: false,
                    CanShuffle: false,
                    CanRepeat: false,
                    HasVolume: false,
                    ShuffleOn: false,
                    GrooveRepeat.Off,
                    position,
                    duration,
                    new DateTimeOffset(stampUtc, TimeSpan.Zero),
                    _artVersion),
            ];
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[Groove] MPRIS poll failed.");
        }
    }

    /// <summary>No file, or a file that stopped moving: after the grace period the bridge declares itself
    /// unavailable and the app shows the install-playerctl card.</summary>
    private void CheckProbe(bool stale = false)
    {
        if (DateTime.UtcNow - _spawnedAtUtc <= TimeSpan.FromSeconds(ProbeTimeoutSeconds) && !stale)
        {
            return;
        }
        _ready = true;
        _unavailable = true;
        _sessions = [];
    }

    private void RefreshArt(string artUrl)
    {
        try
        {
            if (!artUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            var hostPath = Uri.UnescapeDataString(artUrl["file://".Length..]);
            var winePath = "Z:" + hostPath.Replace('/', '\\');
            if (!File.Exists(winePath))
            {
                return;
            }
            var bytes = File.ReadAllBytes(winePath);
            if (bytes.Length == 0)
            {
                return;
            }
            var hash = SHA1.HashData(bytes);
            if (hash.AsSpan().SequenceEqual(_artHash))
            {
                return;
            }
            _artHash = hash;
            _ = Task.Run(async () =>
            {
                try
                {
                    var wrap = await UiHost.TextureProvider.CreateFromImageAsync(bytes).ConfigureAwait(false);
                    var old = Interlocked.Exchange(ref _art, wrap);
                    Interlocked.Increment(ref _artVersion);
                    if (old is not null)
                    {
                        await Task.Delay(2_000).ConfigureAwait(false);
                        old.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    UiHost.Log.Debug(ex, "[Groove] MPRIS art decode failed.");
                }
            });
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[Groove] MPRIS art read failed.");
        }
    }

    public ImTextureID? Art(string sessionId) => _art?.Handle;

    public void TogglePlayPause(string sessionId) => RunHost("playerctl play-pause");

    public void Next(string sessionId) => RunHost("playerctl next");

    public void Previous(string sessionId) => RunHost("playerctl previous");

    public void SetShuffle(string sessionId, bool on)
    {
    }

    public void CycleRepeat(string sessionId)
    {
    }

    public void SeekTo(string sessionId, TimeSpan position)
    {
    }

    public float? GetVolume(string sessionId) => null;

    public void SetVolume(string sessionId, float volume)
    {
    }

    public void Retry()
    {
        DeleteToken();
        Spawn();
    }

    private static void RunHost(string shellCommand, bool waitless = false)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = @"C:\windows\system32\start.exe",
                Arguments = $"/unix /bin/sh -c \"{shellCommand}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (!waitless)
            {
                process?.WaitForExit(2_000);
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[Groove] Host command spawn failed.");
        }
    }

    private static TimeSpan ParseMicros(string value) =>
        long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros) && micros > 0
            ? TimeSpan.FromMilliseconds(micros / 1000.0)
            : TimeSpan.Zero;

    private void DeleteToken()
    {
        try
        {
            var run = Path.Combine(WineDir, "run");
            if (File.Exists(run))
            {
                File.Delete(run);
            }
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        DeleteToken();
        _art?.Dispose();
        _art = null;
    }
}
