using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace AetherLove.Os.Groove;

/// <summary>Per-app volume over the default render device's audio sessions. A media session maps to its
/// audio sessions by process name against the AUMID, best effort: a miss just means no volume slider,
/// never a wrong target. Sources routed to a non-default output device are not found.</summary>
internal sealed class GrooveVolumeMixer : IDisposable
{
    private sealed record Entry(string ProcessName, AudioSessionControl Session);

    private volatile List<Entry> _entries = [];
    private readonly Dictionary<uint, string> _processNames = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;

    /// <summary>Rebuilds the session snapshot; called from the backend's rebuild tick, never per frame.</summary>
    public void Refresh()
    {
        try
        {
            _enumerator ??= new MMDeviceEnumerator();
            _device ??= _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var manager = _device.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            var entries = new List<Entry>(sessions.Count);
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.IsSystemSoundsSession)
                {
                    continue;
                }
                var pid = session.GetProcessID;
                if (pid == 0)
                {
                    continue;
                }
                if (!_processNames.TryGetValue(pid, out var name))
                {
                    try
                    {
                        name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    _processNames[pid] = name;
                }
                entries.Add(new Entry(name, session));
            }
            _entries = entries;
            if (_processNames.Count > 512)
            {
                _processNames.Clear();
            }
        }
        catch (Exception ex)
        {
            // Device switched or endpoint gone; drop the handles and rebuild from scratch next tick.
            UiHost.Log.Debug(ex, "[Groove] Audio session refresh failed; rebuilding the device.");
            _entries = [];
            _device = null;
        }
    }

    public bool HasSession(string aumid)
    {
        foreach (var entry in _entries)
        {
            if (Matches(entry.ProcessName, aumid))
            {
                return true;
            }
        }
        return false;
    }

    public float? GetVolume(string aumid)
    {
        float? best = null;
        foreach (var entry in _entries)
        {
            if (!Matches(entry.ProcessName, aumid))
            {
                continue;
            }
            try
            {
                var volume = entry.Session.SimpleAudioVolume.Volume;
                best = best is { } current ? MathF.Max(current, volume) : volume;
            }
            catch (Exception)
            {
            }
        }
        return best;
    }

    /// <summary>Sets every matched audio session: multi-process sources (browsers, Spotify) keep their
    /// levels in step instead of only the loudest one moving.</summary>
    public void SetVolume(string aumid, float volume)
    {
        var clamped = Math.Clamp(volume, 0f, 1f);
        foreach (var entry in _entries)
        {
            if (!Matches(entry.ProcessName, aumid))
            {
                continue;
            }
            try
            {
                entry.Session.SimpleAudioVolume.Volume = clamped;
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>Desktop AUMIDs are literally "App.exe"; store AUMIDs carry the process name inside the
    /// package identity, so a contains match covers both without ever matching an unrelated app by luck
    /// of a short name (names under 4 chars only match the exe form exactly).</summary>
    private static bool Matches(string processName, string aumid)
    {
        if (aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(processName + ".exe", aumid, StringComparison.OrdinalIgnoreCase);
        }
        if (processName.Length < 4)
        {
            return false;
        }
        return aumid.Contains(processName, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _entries = [];
        _device?.Dispose();
        _enumerator?.Dispose();
    }
}
