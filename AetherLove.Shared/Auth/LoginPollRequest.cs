using MessagePack;

namespace AetherLove.Shared.Auth;

/// <summary>Client→server poll for the status of a previously-started login transaction.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record LoginPollRequest(Guid TransactionId, string TransactionSecret);
