using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using CefSharp;
using CefSharp.OffScreen;

namespace AetherOS.WatchHost;

/// <summary>The Echo playback host: boots CEF off-screen, drives the watch page over a control pipe, and
/// republishes every painted frame into shared memory for the plugin to draw inside ImGui.
///
/// Usage: AetherOS.WatchHost.exe --parent &lt;pid&gt; --pipe &lt;name&gt; --map &lt;name&gt; --url &lt;watchPageUrl&gt;
/// [--w &lt;n&gt;] [--h &lt;n&gt;]
///
/// It is a separate process so a browser crash kills only this, and so no native browser binaries load into
/// Dalamud's assembly load context. The pipe and map names are chosen per launch by the plugin, so a host
/// orphaned by an earlier crash can never talk to a fresh one.</summary>
internal static class Program
{
    private const int DefaultWidth = 854;
    private const int DefaultHeight = 480;
    private const int MinDimension = 16;
    private const int PipeConnectTimeoutMs = 15000;

    /// <summary>How long an orderly teardown gets once the parent has died before the process exits anyway.
    /// CEF can hang on shutdown, and an orphan must never outlive the game.</summary>
    private const int HardExitGraceMs = 3000;

    private const int ExitBadCommandLine = 1;
    private const int ExitCefFailed = 2;
    private const int ExitPipeFailed = 3;

    private const string Usage =
        "Usage: AetherOS.WatchHost.exe --parent <pid> --pipe <name> --map <name> --url <watchPageUrl> " +
        "[--w <n>] [--h <n>]";

    private static readonly ManualResetEventSlim Exit = new(false);
    private static readonly ManualResetEventSlim ShutdownComplete = new(false);

    private static MemoryMappedViewAccessor? view;
    private static int sequence;
    private static byte[] scratch = [];

    private sealed record HostOptions(
        int ParentPid,
        string PipeName,
        string MapName,
        string Url,
        int Width,
        int Height);

    [STAThread]
    private static int Main(string[] args)
    {
        if (!TryParseArgs(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(Usage);
            return ExitBadCommandLine;
        }

        Process parent;
        try
        {
            parent = Process.GetProcessById(options.ParentPid);
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine($"Parent process {options.ParentPid} is not running.");
            return ExitBadCommandLine;
        }

        var pipe = new ControlPipe(options.PipeName);
        try
        {
            pipe.Connect(PipeConnectTimeoutMs, HostVersion());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Control pipe '{options.PipeName}' failed: {ex.Message}");
            pipe.Dispose();
            return ExitPipeFailed;
        }

        var settings = new CefSettings
        {
            WindowlessRenderingEnabled = true,
            LogSeverity = LogSeverity.Disable,
        };
        // Off-screen rendering normally forces software decode. Asking for GPU compositing anyway is what
        // makes 720p video plausible rather than a slideshow; if this turns out to misbehave on some
        // machines it is the first knob to try turning off.
        settings.CefCommandLineArgs["autoplay-policy"] = "no-user-gesture-required";
        // The OffScreen CefSettings ctor pre-adds this switch (headless is assumed silent), muting the whole
        // browser process regardless of anything the page or the player does.
        settings.CefCommandLineArgs.Remove("mute-audio");

        if (!Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null))
        {
            pipe.SendLog("CEF failed to initialise.");
            pipe.Dispose();
            return ExitCefFailed;
        }

        var map = FrameBridge.Create(options.MapName);
        var accessor = map.CreateViewAccessor(0, FrameBridge.Capacity, MemoryMappedFileAccess.ReadWrite);
        view = accessor;

        // Off-screen CEF paints at 30 fps unless told otherwise, which is the whole reason playback looked
        // choppy at any resolution.
        var browserSettings = new BrowserSettings { WindowlessFrameRate = 60 };
        var browser = new ChromiumWebBrowser(options.Url, browserSettings, automaticallyCreateBrowser: false);
        browser.Size = new System.Drawing.Size(options.Width, options.Height);
        browser.Paint += OnPaint;
        browser.CreateBrowser();

        var player = new PlayerBridge(browser);
        player.StateChanged += pipe.SendState;
        player.PageReady += pipe.SendReady;
        player.Log += pipe.SendLog;

        pipe.CommandReceived += command => Handle(command, browser, player);
        pipe.Faulted += reason =>
        {
            Console.Error.WriteLine(reason);
            Exit.Set();
        };

        player.Start();
        pipe.Start();
        StartWatchdog(parent);

        // Nothing to pump: the plugin owns the UI. Idle until the plugin, the parent, or a fault says stop.
        Exit.Wait();

        browser.Paint -= OnPaint;
        player.Dispose();
        pipe.Dispose();
        browser.Dispose();
        view = null;
        // The map outlives CEF: a paint already in flight would otherwise write into a disposed accessor.
        Cef.Shutdown();
        accessor.Dispose();
        map.Dispose();
        ShutdownComplete.Set();
        return 0;
    }

    /// <summary>Ties the host's life to the game's. The parent exiting is the only signal that reliably
    /// arrives when the game crashes, and the grace period covers CEF hanging on the way out.</summary>
    private static void StartWatchdog(Process parent)
    {
        var watchdog = new Thread(() =>
        {
            parent.WaitForExit();
            Exit.Set();
            if (!ShutdownComplete.Wait(HardExitGraceMs))
            {
                Environment.Exit(0);
            }
        })
        {
            IsBackground = true,
            Name = "watch-host-watchdog",
        };
        watchdog.Start();
    }

    private static void Handle(ControlCommand command, ChromiumWebBrowser browser, PlayerBridge player)
    {
        switch (command.Type)
        {
            case ControlCommands.Load:
                if (command.VideoId is { Length: > 0 } videoId)
                {
                    player.Load(videoId, command.Start ?? 0d, command.Epoch ?? 0L);
                }
                break;
            case ControlCommands.Play:
                player.Play();
                break;
            case ControlCommands.Pause:
                player.Pause();
                break;
            case ControlCommands.Seek:
                player.Seek(command.Seconds ?? 0d);
                break;
            case ControlCommands.Volume:
                player.SetVolume(command.Volume ?? 1d);
                break;
            case ControlCommands.Size:
                Resize(browser, command.Width, command.Height);
                break;
            case ControlCommands.Captions:
                player.SetCaptions(command.On ?? false);
                break;
            case ControlCommands.Shutdown:
                Exit.Set();
                break;
        }
    }

    private static void Resize(ChromiumWebBrowser browser, int? width, int? height)
    {
        if (width is not { } w || height is not { } h)
        {
            return;
        }
        browser.Size = new System.Drawing.Size(
            Math.Clamp(w, MinDimension, FrameBridge.MaxWidth),
            Math.Clamp(h, MinDimension, FrameBridge.MaxHeight));
    }

    private static string HostVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "0.0.0.0";
    }

    private static bool TryParseArgs(string[] args, out HostOptions options, out string error)
    {
        options = new HostOptions(0, string.Empty, string.Empty, string.Empty, DefaultWidth, DefaultHeight);
        error = string.Empty;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                continue;
            }
            values[args[i][2..]] = args[++i];
        }

        if (!values.TryGetValue("parent", out var rawParent)
            || !int.TryParse(rawParent, out var parentPid)
            || parentPid <= 0)
        {
            error = "--parent <pid> is required and must be a process id.";
            return false;
        }
        if (!values.TryGetValue("pipe", out var pipeName) || string.IsNullOrWhiteSpace(pipeName))
        {
            error = "--pipe <name> is required.";
            return false;
        }
        if (!values.TryGetValue("map", out var mapName) || string.IsNullOrWhiteSpace(mapName))
        {
            error = "--map <name> is required.";
            return false;
        }
        if (!values.TryGetValue("url", out var url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            error = "--url <watchPageUrl> is required and must be an absolute URL.";
            return false;
        }

        var width = values.TryGetValue("w", out var rawWidth) && int.TryParse(rawWidth, out var w)
            ? w
            : DefaultWidth;
        var height = values.TryGetValue("h", out var rawHeight) && int.TryParse(rawHeight, out var h)
            ? h
            : DefaultHeight;

        options = new HostOptions(
            parentPid,
            pipeName,
            mapName,
            url,
            Math.Clamp(width, MinDimension, FrameBridge.MaxWidth),
            Math.Clamp(height, MinDimension, FrameBridge.MaxHeight));
        return true;
    }

    /// <summary>CEF hands us a BGRA buffer on its own thread. Copy it straight through: any work done here is
    /// work done on the browser's compositor.</summary>
    private static void OnPaint(object? sender, OnPaintEventArgs e)
    {
        if (view is not { } target || e.Width <= 0 || e.Height <= 0)
        {
            return;
        }
        if (e.Width > FrameBridge.MaxWidth || e.Height > FrameBridge.MaxHeight)
        {
            return;
        }

        var bytes = e.Width * e.Height * 4;
        if (scratch.Length < bytes)
        {
            scratch = new byte[bytes];
        }
        System.Runtime.InteropServices.Marshal.Copy(e.BufferHandle, scratch, 0, bytes);

        var next = ++sequence;
        target.Write(0, next);
        target.Write(4, e.Width);
        target.Write(8, e.Height);
        target.WriteArray(FrameBridge.HeaderBytes, scratch, 0, bytes);
        // Written last: a reader that sees this match the leading counter knows the pixels between them are
        // from one frame.
        target.Write(12, next);
    }
}
