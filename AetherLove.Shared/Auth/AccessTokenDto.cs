using MessagePack;

namespace AetherLove.Shared.Auth;

/// <summary>A freshly minted access token without rotating the refresh session.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record AccessTokenDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc);
