using MessagePack;

namespace AetherLove.Shared.Auth;

/// <summary>Refresh request with optional ActiveProfileId for multi-profile selection; trailing default preserves wire compatibility.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record RefreshRequest(string RefreshToken, Guid? ActiveProfileId = null);
