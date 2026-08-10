using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;

namespace AetherOS.Apps.Groove;

public enum GrooveRepeat
{
    Off = 0,
    One = 1,
    All = 2,
}

/// <summary>One media source as last observed. Immutable; the host swaps whole snapshots so the draw
/// thread never reads a half-written state.</summary>
public sealed record GrooveSession(
    string SessionId,
    string AppLabel,
    string Title,
    string Artist,
    string Album,
    bool IsPlaying,
    bool CanControl,
    bool CanSeek,
    bool CanShuffle,
    bool CanRepeat,
    bool HasVolume,
    bool ShuffleOn,
    GrooveRepeat Repeat,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset PositionStampUtc,
    int ArtVersion)
{
    /// <summary>The position to show right now: sources report position only on change, so a playing
    /// track extrapolates from the stamp.</summary>
    public TimeSpan PositionNow()
    {
        if (!IsPlaying)
        {
            return Position;
        }
        var extrapolated = Position + (DateTimeOffset.UtcNow - PositionStampUtc);
        return Duration > TimeSpan.Zero && extrapolated > Duration ? Duration : extrapolated;
    }
}

/// <summary>The Groove app's host bridge: whatever the PC is playing, read and controlled through the
/// platform's media session system. Purely local; no server, no accounts.</summary>
public interface IGrooveHost
{
    /// <summary>False until the backend finished starting, and forever on an unusable platform.</summary>
    bool Ready { get; }

    /// <summary>True when the reduced Wine/Linux bridge is active instead of the full Windows backend.</summary>
    bool ReducedTier { get; }

    /// <summary>True when the reduced bridge could not reach a media player on the host (typically
    /// playerctl missing); drives the install-hint card.</summary>
    bool BridgeUnavailable { get; }

    /// <summary>The pinned session while it is alive, else the one the system considers current.</summary>
    GrooveSession? Current { get; }

    IReadOnlyList<GrooveSession> Sessions { get; }

    /// <summary>Null follows the system's current session; a SessionId pins one app. Persisted per
    /// install. A dead pin falls back to the system session without being cleared.</summary>
    string? PinnedSessionId { get; set; }

    /// <summary>Album art for a session, resolved per frame; null while loading or absent. Never cache
    /// the returned handle across frames.</summary>
    ImTextureID? Art(string sessionId);

    void TogglePlayPause(string sessionId);

    void Next(string sessionId);

    void Previous(string sessionId);

    void SetShuffle(string sessionId, bool on);

    void CycleRepeat(string sessionId);

    void SeekTo(string sessionId, TimeSpan position);

    /// <summary>0..1 for the source app's audio session(s); null when the source could not be mapped.</summary>
    float? GetVolume(string sessionId);

    void SetVolume(string sessionId, float volume);

    /// <summary>Kills and respawns the reduced bridge, for the platform card's try-again button.</summary>
    void RetryBridge();
}
