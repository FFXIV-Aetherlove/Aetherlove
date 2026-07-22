using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Chat;
using AetherLove.Services.Hub;
using AetherLove.Shared.Messaging;

namespace AetherLove.Os;

/// <summary>The shared social bridge: match data from the hub and cache, plus navigation into the AetherLove
/// chat and profile screens. Reused by every app instead of copying these into each app's host.</summary>
public sealed class SocialBridgeService : ISocialBridge
{
    private readonly AetherHubContext _hub;
    private readonly ChatCacheStore _chatCache;
    private readonly OsShell _osShell;

    public SocialBridgeService(AetherHubContext hub, ChatCacheStore chatCache, OsShell osShell)
    {
        _hub = hub;
        _chatCache = chatCache;
        _osShell = osShell;
    }

    public Task<MatchListDto> GetMyMatchesAsync(CancellationToken ct = default) => _hub.GetMyMatchesAsync(ct);

    public IReadOnlyList<MatchSummaryDto> GetCachedMatches() => _chatCache.GetMatches();

    public void OpenChat() => _osShell.SendIntent("aetherlove", AetherOS.Sdk.OsIntents.Create(AetherOS.Sdk.OsIntents.OpenChat));
}
