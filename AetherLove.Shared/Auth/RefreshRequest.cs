using MessagePack;

namespace AetherLove.Shared.Auth;

/// <summary>Client→server refresh request; exchanges a refresh token for a fresh pair.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record RefreshRequest(string RefreshToken);
