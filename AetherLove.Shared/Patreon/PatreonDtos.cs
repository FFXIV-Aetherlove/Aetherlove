using System;
using MessagePack;

namespace AetherLove.Shared.Patreon;

/// <summary>State of the caller's most recent Patreon link transaction, surfaced to the plugin so its
/// settings page can drive the link flow (mirrors the XIVAuth login states).</summary>
public enum PatreonLinkFlowStatus : short
{
    None = 0,
    Pending = 1,
    Completed = 2,
    Failed = 3,
    Expired = 4,
}

/// <summary>Returned by StartPatreonLinkAsync: the browser URL to open plus the transaction the client polls.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record PatreonLinkStartDto(
    Guid TransactionId,
    string AuthorizeUrl,
    DateTimeOffset ExpiresAtUtc);

/// <summary>The caller's current Patreon link + supporter state, polled by the plugin during and after linking.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record PatreonStatusDto(
    bool Enabled,
    bool Linked,
    bool IsEntitled,
    bool IsSupporter,
    DateTimeOffset? LinkedAtUtc,
    PatreonLinkFlowStatus Flow,
    string? FlowErrorCode,
    string? CampaignUrl);
