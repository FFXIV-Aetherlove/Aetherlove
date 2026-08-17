using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace AetherLove.UI;

/// <summary>Traces what the phone does to ImGui's font state and, crucially, what a plugin drawn AFTER us
/// would inherit.
///
/// The bug this exists for: ImGui does not read <c>io.FontGlobalScale</c> when it draws text. It folds the
/// value into a derived base size, and only recomputes that inside NewFrame, PushFont and PopFont. Writing
/// the field back in PostDraw therefore restores the field without restoring the size anyone actually
/// renders at, so the next plugin can inherit a size computed while our pin was up. At a global scale of
/// 1.0 every write is 1.0 over 1.0 and nothing can go wrong, which is why only users who moved Dalamud's
/// font scale ever see it.
///
/// Nothing here is on by default: the probe opens a real (empty, invisible) window every frame while it
/// runs, and the trace is far too loud to leave enabled.</summary>
public static class FontDiagnostics
{
    /// <summary>A downstream size within this fraction of the expected one is treated as a match, so float
    /// noise never reports a leak.</summary>
    private const float Tolerance = 0.001f;

    private static bool _verbose;
    private static int _traceFramesLeft;
    private static double _lastHeartbeat = double.MinValue;
    private static float _lastReportedRatio = 1f;

    /// <summary>Seconds between the standing summary line while verbose logging is on.</summary>
    private const double HeartbeatSeconds = 5.0;

    public static bool Verbose => _verbose;

    /// <summary>Turns the standing trace on or off; returns the new state.</summary>
    public static bool Toggle()
    {
        _verbose = !_verbose;
        _lastHeartbeat = double.MinValue;
        _lastReportedRatio = 1f;
        UiHost.Log.Information(_verbose
            ? "[FontDiag] Verbose font tracing ON. Expect a line per stage per frame."
            : "[FontDiag] Verbose font tracing OFF.");
        if (_verbose)
        {
            LogEnvironment();
        }
        return _verbose;
    }

    /// <summary>Traces the next <paramref name="frames"/> frames in full, then stops on its own. Use this
    /// rather than <see cref="Toggle"/> when capturing a before-and-after comparison.</summary>
    public static void CaptureFrames(int frames)
    {
        _traceFramesLeft = Math.Max(1, frames);
        UiHost.Log.Information($"[FontDiag] Capturing {_traceFramesLeft} frame(s) of font state.");
        LogEnvironment();
    }

    /// <summary>The values that do not change frame to frame, logged once per capture so a pasted log is
    /// self-contained.</summary>
    public static void LogEnvironment()
    {
        try
        {
            var io = ImGui.GetIO();
            var font = ImGui.GetFont();
            UiHost.Log.Information(
                "[FontDiag] env: dalamudGlobalScale={Global:0.####} ioFontGlobalScale={Io:0.####} " +
                "phoneScale={Phone:0.####} defaultFontRasterPx={Raster:0.##} fontsReady={Ready}",
                ImGuiHelpers.GlobalScale, io.FontGlobalScale, UiScale.S,
                FontRaster(font), UiFonts.Ready);
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[FontDiag] Could not read the font environment.");
        }
    }

    /// <summary>One stage of a frame: the field, and the size text would actually render at right now.
    /// <paramref name="stage"/> should name the window and the moment, e.g. "MainWindow.PreDraw/after-pin".</summary>
    public static void Sample(string stage)
    {
        if (!Tracing)
        {
            return;
        }
        try
        {
            var io = ImGui.GetIO();
            var font = ImGui.GetFont();
            // GetFontSize is the derived value, which is the one that decides how big glyphs come out.
            // Watching it rather than the IO field is the whole point: they disagree exactly when the bug bites.
            UiHost.Log.Information(
                "[FontDiag] {Stage}: ioScale={Io:0.####} effectiveFontSize={Size:0.##} " +
                "currentFontRasterPx={Raster:0.##} helpersGlobalScale={Helpers:0.####}",
                stage, io.FontGlobalScale, ImGui.GetFontSize(),
                FontRaster(font), ImGuiHelpers.GlobalScale);
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, $"[FontDiag] Sample '{stage}' failed.");
        }
    }

    /// <summary>Measures what the NEXT plugin will inherit, by opening a throwaway window exactly as one
    /// would and reading the size ImGui hands it. Call once at the very end of our draw handler, after
    /// every restore has run.
    ///
    /// A fresh window at scale 1 reports the derived base size directly, so comparing it against
    /// <c>ioFontGlobalScale * defaultFontRasterPx</c> is a direct test of whether our restores actually
    /// restored anything. Any mismatch here IS the reported bug, and the ratio is how wrong other plugins
    /// will be.</summary>
    public static void ProbeDownstream()
    {
        if (!Tracing && !_verbose)
        {
            return;
        }
        try
        {
            var io = ImGui.GetIO();
            ImGui.SetNextWindowPos(new Vector2(-4000f, -4000f), ImGuiCond.Always);
            ImGui.SetNextWindowSize(Vector2.Zero, ImGuiCond.Always);
            ImGui.Begin("##aetherloveFontProbe",
                ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMouseInputs);
            var inherited = ImGui.GetFontSize();
            var rasterPx = FontRaster(ImGui.GetFont());
            // Begin pairs with End whatever it returned; skipping it would corrupt the window stack.
            ImGui.End();

            var expected = io.FontGlobalScale * rasterPx;
            var ratio = expected > 0f ? inherited / expected : 1f;
            var leaking = MathF.Abs(ratio - 1f) > Tolerance;

            if (Tracing)
            {
                UiHost.Log.Information(
                    "[FontDiag] downstream probe: inherited={Inherited:0.###} expected={Expected:0.###} " +
                    "ratio={Ratio:0.####} ioScale={Io:0.####} rasterPx={Raster:0.##} leaking={Leaking}",
                    inherited, expected, ratio, io.FontGlobalScale, rasterPx, leaking);
            }

            // Outside the trace as well: a standing warning whenever the number moves, so a run with tracing
            // off still records that something changed and by how much.
            if (leaking && MathF.Abs(ratio - _lastReportedRatio) > Tolerance)
            {
                _lastReportedRatio = ratio;
                UiHost.Log.Warning(
                    "[FontDiag] Other plugins drawn after us will render text at {Ratio:0.###}x the correct " +
                    "size (inherited {Inherited:0.###}, expected {Expected:0.###}, Dalamud font scale " +
                    "{Io:0.####}). This is the cross-plugin text resize.",
                    ratio, inherited, expected, io.FontGlobalScale);
            }
            else if (!leaking && MathF.Abs(_lastReportedRatio - 1f) > Tolerance)
            {
                _lastReportedRatio = 1f;
                UiHost.Log.Information("[FontDiag] Downstream font size is back to correct.");
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[FontDiag] Downstream probe failed.");
        }
    }

    /// <summary>Ends a traced frame; call once per frame after the probe.</summary>
    public static void EndFrame()
    {
        if (_traceFramesLeft > 0)
        {
            _traceFramesLeft--;
            if (_traceFramesLeft == 0)
            {
                UiHost.Log.Information("[FontDiag] Capture complete.");
            }
        }
        if (!_verbose)
        {
            return;
        }
        var now = ImGui.GetTime();
        if (now - _lastHeartbeat >= HeartbeatSeconds)
        {
            _lastHeartbeat = now;
            LogEnvironment();
        }
    }

    private static bool Tracing => _verbose || _traceFramesLeft > 0;

    /// <summary>The size the current font was rasterised at, or 0 while no font is current.</summary>
    private static float FontRaster(ImFontPtr font)
    {
        try
        {
            return font.FontSize;
        }
        catch (Exception)
        {
            return 0f;
        }
    }
}
