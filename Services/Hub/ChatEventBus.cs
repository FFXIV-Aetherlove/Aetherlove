using System;
using AetherLove.Shared.Matching;
using AetherLove.Shared.Messaging;

namespace AetherLove.Services.Hub;

/// <summary>In-process pub/sub for chat-related SignalR pushes. Handlers run on the SignalR receiver thread.</summary>
public sealed class ChatEventBus
{
    public event Action<MessageReceivedPushDto>? MessageReceived;
    public event Action<MessageReadPushDto>? MessageRead;
    public event Action<UnmatchedPushDto>? Unmatched;
    public event Action<BlockedByPeerPushDto>? BlockedByPeer;
    public event Action<MatchCreatedPushDto>? MatchCreated;

    public void RaiseMessageReceived(MessageReceivedPushDto p) => MessageReceived?.Invoke(p);
    public void RaiseMessageRead(MessageReadPushDto p) => MessageRead?.Invoke(p);
    public void RaiseUnmatched(UnmatchedPushDto p) => Unmatched?.Invoke(p);
    public void RaiseBlockedByPeer(BlockedByPeerPushDto p) => BlockedByPeer?.Invoke(p);
    public void RaiseMatchCreated(MatchCreatedPushDto p) => MatchCreated?.Invoke(p);
}
