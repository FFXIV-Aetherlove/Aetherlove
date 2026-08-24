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

/// <summary>The Wine courtesy tier: GSMTC does not exist under Wine, so a host-side playerctl loop writes
/// MPRIS now-playing lines to /tmp and the plugin polls the file back through the Z: drive mapping. Reduced
/// by design: metadata plus transport, honest capability flags for everything else.
/// <para>Nothing here launches a process unless the host can actually answer: Wine, a shell at
/// <c>Z:\bin\sh</c>, a host that is not macOS (which has no MPRIS at all) and a playerctl on disk.
/// A Mac player, or a Linux box without playerctl, goes straight to the unavailable card instead.</para>
/// <para>The shell is run DIRECTLY rather than through <c>start.exe /unix</c>, which goes through
/// ShellExecute: a Wine build that cannot associate a unix binary answers that with its own modal
/// "There is no Windows program configured to open this type of file", and that dialog belongs to Wine, so
/// no catch block on this side can suppress it.</para></summary>
internal sealed class MprisBackend : IMediaBackend
{
    private const string HostDir = "/tmp/aetherlove-groove";
    private const string WineDir = @"Z:\tmp\aetherlove-groove";
    private const string HostShell = @"Z:\bin\sh";
    private const int PollMillis = 1_500;
    private const int ProbeTimeoutSeconds = 15;
    private const string SessionId = "mpris";

    /// <summary>The two directories that exist on macOS and nowhere else, for a host that can never serve
    /// MPRIS however long it is waited on.</summary>
    private static readonly string[] MacMarkers =
    [
        @"Z:\System\Library\CoreServices",
        @"Z:\Applications\Utilities",
    ];

    /// <summary>Where playerctl lives on the distributions that ship it.</summary>
    private static readonly string[] PlayerctlPaths =
    [
        @"Z:\usr\bin\playerctl",
        @"Z:\usr\local\bin\playerctl",
        @"Z:\bin\playerctl",
        @"Z:\var\lib\flatpak\exports\bin\playerctl",
    ];

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
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
        Spawn();
    }

    /// <summary>Whether this host can serve MPRIS at all. Read on every spawn rather than cached once, so a
    /// playerctl installed while the game is running is picked up by the card's retry.</summary>
    private static bool HostCanServe()
    {
        try
        {
            // Belt and braces: this backend is only constructed under Wine, but nothing in it may ever run
            // on a real Windows box, where Z: is somebody's actual drive.
            if (!GrooveHostService.RunsUnderWine() || !File.Exists(HostShell))
            {
                return false;
            }
            foreach (var marker in MacMarkers)
            {
                if (Directory.Exists(marker))
                {
                    return false;
                }
            }
            foreach (var path in PlayerctlPaths)
            {
                if (File.Exists(path))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The unavailable card, reached without launching anything.</summary>
    private void GiveUp()
    {
        _ready = true;
        _unavailable = true;
        _sessions = [];
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
        if (!HostCanServe())
        {
            GiveUp();
            return;
        }

        _token = Guid.NewGuid().ToString("N");
        _spawnedAtUtc = DateTime.UtcNow;
        _unavailable = false;

        // Written to disk and run as a script rather than passed as one long quoted argument: the loop
        // needs shell quoting of its own, and nesting that inside Windows argument parsing is how a quote
        // ends up somewhere nobody expected.
        var loop = string.Join('\n',
        [
            $"d={HostDir}",
            "mkdir -p $d",
            $"echo {_token} > $d/run",
            $"while [ \"$(cat $d/run 2>/dev/null)\" = \"{_token}\" ]; do",
            "  playerctl metadata --format '{{status}}\t{{title}}\t{{artist}}\t{{album}}\t{{mpris:artUrl}}\t{{position}}\t{{mpris:length}}' > $d/np.tmp 2>/dev/null && mv $d/np.tmp $d/np.txt",
            "  sleep 2",
            "done",
            string.Empty,
        ]);
        if (!WriteScript("loop.sh", loop))
        {
            GiveUp();
            return;
        }
        RunHost($"{HostDir}/loop.sh", waitless: true);
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

    public void TogglePlayPause(string sessionId) => RunCommand("playerctl play-pause");

    public void Next(string sessionId) => RunCommand("playerctl next");

    public void Previous(string sessionId) => RunCommand("playerctl previous");

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

    /// <summary>Writes a script into the host's temp directory through the Z: mapping. False means the
    /// mapping is not writable, which is one more host that cannot serve.</summary>
    private static bool WriteScript(string name, string body)
    {
        try
        {
            Directory.CreateDirectory(WineDir);
            // Unix newlines whatever this side wrote: /bin/sh will not run a script full of carriage returns.
            File.WriteAllText(Path.Combine(WineDir, name), body.Replace("\r\n", "\n"));
            return true;
        }
        catch (Exception ex)
        {
            UiHost.Log.Debug(ex, "[Groove] Could not write the host script.");
            return false;
        }
    }

    /// <summary>A one-off transport command. Re-checks the host, so a player uninstalled mid-session stops
    /// spawning anything rather than failing once per press.</summary>
    private static void RunCommand(string command)
    {
        if (HostCanServe())
        {
            RunHost(command, waitless: false, viaC: true);
        }
    }

    /// <summary>Runs the host's own shell directly, arguments passed as a list so nothing is re-parsed on
    /// the way. Never through <c>start.exe /unix</c>: see the type's remarks.</summary>
    private static void RunHost(string target, bool waitless = false, bool viaC = false)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = HostShell,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (viaC)
            {
                psi.ArgumentList.Add("-c");
            }
            psi.ArgumentList.Add(target);
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
