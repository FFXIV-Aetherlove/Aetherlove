using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using AetherLove.Os.Groove;
using AetherOS.Apps.Groove;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Os;

/// <summary>The Groove app's host: reads and controls the PC's media sessions. Picks one backend at
/// startup: the full GSMTC backend on real Windows, the reduced bridge tier under Wine/Proton. Also feeds
/// the notification shade's media tile as <see cref="IOsMediaRemote"/>.</summary>
public sealed class GrooveHostService : IGrooveHost, IOsMediaRemote, IDisposable
{
    private readonly GrooveSettings _settings;
    private readonly IMediaBackend _backend;

    public GrooveHostService(GrooveSettings settings)
    {
        _settings = settings;
        _backend = RunsUnderWine() ? new MprisBackend() : new GsmtcBackend();
    }

    public bool Ready => _backend.Ready;

    public bool ReducedTier => _backend.ReducedTier;

    public bool BridgeUnavailable => _backend.BridgeUnavailable;

    public GrooveSession? Current
    {
        get
        {
            var sessions = _backend.Sessions;
            if (sessions.Count == 0)
            {
                return null;
            }
            if (_settings.PinnedSession is { } pinned)
            {
                foreach (var session in sessions)
                {
                    if (session.SessionId == pinned)
                    {
                        return session;
                    }
                }
            }
            var currentId = _backend.SystemCurrentId;
            foreach (var session in sessions)
            {
                if (session.SessionId == currentId)
                {
                    return session;
                }
            }
            return sessions[0];
        }
    }

    public IReadOnlyList<GrooveSession> Sessions => _backend.Sessions;

    public string? PinnedSessionId
    {
        get => _settings.PinnedSession;
        set => _settings.PinnedSession = value;
    }

    public ImTextureID? Art(string sessionId) => _backend.Art(sessionId);

    public void TogglePlayPause(string sessionId) => _backend.TogglePlayPause(sessionId);

    public void Next(string sessionId) => _backend.Next(sessionId);

    public void Previous(string sessionId) => _backend.Previous(sessionId);

    public void SetShuffle(string sessionId, bool on) => _backend.SetShuffle(sessionId, on);

    public void CycleRepeat(string sessionId) => _backend.CycleRepeat(sessionId);

    public void SeekTo(string sessionId, TimeSpan position) => _backend.SeekTo(sessionId, position);

    public float? GetVolume(string sessionId) => _backend.GetVolume(sessionId);

    public void SetVolume(string sessionId, float volume) => _backend.SetVolume(sessionId, volume);

    public void RetryBridge() => _backend.Retry();

    bool IOsMediaRemote.TileVisible => _settings.ShowShadeTile && ((IOsMediaRemote)this).HasSession;

    // Removing the Groove app from the home screen means "I don't want this feature": nothing OS-owned
    // (shade tile, bubble transport) may keep showing what the PC is playing. HasSession is the one gate
    // every one of those surfaces reads.
    bool IOsMediaRemote.HasSession => !UiHost.Configuration.Os.RemovedApps.Contains("groove")
        && Ready && !BridgeUnavailable && Current is { Title.Length: > 0 };

    string IOsMediaRemote.Title => Current?.Title ?? string.Empty;

    string IOsMediaRemote.Artist => Current?.Artist ?? string.Empty;

    bool IOsMediaRemote.IsPlaying => Current?.IsPlaying ?? false;

    bool IOsMediaRemote.CanControl => Current?.CanControl ?? false;

    ImTextureID? IOsMediaRemote.Art => Current is { } session ? _backend.Art(session.SessionId) : null;

    void IOsMediaRemote.TogglePlayPause()
    {
        if (Current is { } session)
        {
            _backend.TogglePlayPause(session.SessionId);
        }
    }

    void IOsMediaRemote.Next()
    {
        if (Current is { } session)
        {
            _backend.Next(session.SessionId);
        }
    }

    void IOsMediaRemote.Previous()
    {
        if (Current is { } session)
        {
            _backend.Previous(session.SessionId);
        }
    }

    private static bool RunsUnderWine()
    {
        try
        {
            if (Dalamud.Utility.Util.IsWine())
            {
                return true;
            }
        }
        catch (Exception)
        {
        }
        try
        {
            var ntdll = GetModuleHandle("ntdll.dll");
            return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);

    public void Dispose() => _backend.Dispose();
}
