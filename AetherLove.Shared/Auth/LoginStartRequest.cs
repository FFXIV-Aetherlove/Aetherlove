using MessagePack;

namespace AetherLove.Shared.Auth;

/// <summary>Client→server request to begin a new XIVAuth login transaction.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LoginStartRequest(string? DeviceName);
