using System;
using System.Collections.Generic;
using AetherOS.Apps.Groove;
using Dalamud.Bindings.ImGui;

namespace AetherLove.Os.Groove;

/// <summary>One platform's way of reading and controlling media sessions. Implementations publish
/// immutable snapshots; the draw thread only ever reads the latest one.</summary>
internal interface IMediaBackend : IDisposable
{
    bool Ready { get; }

    bool ReducedTier { get; }

    bool BridgeUnavailable { get; }

    /// <summary>The session the platform considers current, or null.</summary>
    string? SystemCurrentId { get; }

    IReadOnlyList<GrooveSession> Sessions { get; }

    ImTextureID? Art(string sessionId);

    void TogglePlayPause(string sessionId);

    void Next(string sessionId);

    void Previous(string sessionId);

    void SetShuffle(string sessionId, bool on);

    void CycleRepeat(string sessionId);

    void SeekTo(string sessionId, TimeSpan position);

    float? GetVolume(string sessionId);

    void SetVolume(string sessionId, float volume);

    void Retry();
}
