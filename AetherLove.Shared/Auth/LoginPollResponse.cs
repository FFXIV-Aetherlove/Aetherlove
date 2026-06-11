using MessagePack;

namespace AetherLove.Shared.Auth;

/// <summary>Server→client poll response. <c>Status</c> is one of "pending", "completed", "failed".</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LoginPollResponse(
    string Status,
    TokenPairDto? Tokens = null,
    string? Error = null);
