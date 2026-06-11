using MessagePack;

namespace AetherLove.Shared.Auth;

/// <summary>Server→client response that drives the polling-style browser sign-in.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LoginStartResponse(
    Guid TransactionId,
    string TransactionSecret,
    string LoginUrl,
    DateTimeOffset ExpiresAtUtc);
