using MessagePack;

namespace AetherLove.Shared.Auth;

/// <summary>An access-token / refresh-token pair issued by the AetherLove server.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record TokenPairDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);
