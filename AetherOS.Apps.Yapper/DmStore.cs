using System;
using System.Collections.Generic;
using System.Linq;
using AetherLove.Shared.Yapper;

namespace AetherOS.Apps.Yapper;

/// <summary>In-memory DM state: the conversation list, per-peer message threads (newest-last for
/// rendering) and unread totals. Pushes and optimistic sends mutate here so every surface stays in
/// sync; ciphertext decrypts lazily at render through the host.</summary>
internal sealed class DmStore
{
    private readonly object _gate = new();
    private List<YapperDmConversationDto> _conversations = [];
    private readonly Dictionary<Guid, List<YapperDmMessageDto>> _threads = [];
    private readonly Dictionary<Guid, byte[]> _peerKeys = [];
    private readonly Dictionary<Guid, YapAuthorDto> _peers = [];
    private readonly Dictionary<Guid, Guid> _messagePeer = [];

    public void SetConversations(YapperDmConversationDto[] rows)
    {
        lock (_gate)
        {
            _conversations = rows.ToList();
            foreach (var row in rows)
            {
                _peers[row.Peer.ProfileId] = row.Peer;
                if (row.PeerPublicKey is { Length: > 0 } key)
                {
                    _peerKeys[row.Peer.ProfileId] = key;
                }
            }
        }
    }

    public List<YapperDmConversationDto> Conversations()
    {
        lock (_gate)
        {
            return _conversations.ToList();
        }
    }

    public int TotalUnread()
    {
        lock (_gate)
        {
            return _conversations.Sum(c => c.Unread);
        }
    }

    public YapAuthorDto? Peer(Guid profileId)
    {
        lock (_gate)
        {
            return _peers.GetValueOrDefault(profileId);
        }
    }

    public byte[]? PeerKey(Guid profileId)
    {
        lock (_gate)
        {
            return _peerKeys.GetValueOrDefault(profileId);
        }
    }

    public void SetThread(Guid peerId, YapAuthorDto peer, byte[]? peerKey, YapperDmMessageDto[] newestFirst)
    {
        lock (_gate)
        {
            _peers[peerId] = peer;
            if (peerKey is { Length: > 0 })
            {
                _peerKeys[peerId] = peerKey;
            }
            var list = newestFirst.Reverse().ToList();
            _threads[peerId] = list;
            foreach (var m in list)
            {
                _messagePeer[m.Id] = peerId;
            }
        }
    }

    public void PrependOlder(Guid peerId, YapperDmMessageDto[] newestFirst)
    {
        lock (_gate)
        {
            if (!_threads.TryGetValue(peerId, out var list))
            {
                return;
            }
            var known = list.Select(m => m.Id).ToHashSet();
            var older = newestFirst.Reverse().Where(m => !known.Contains(m.Id)).ToList();
            list.InsertRange(0, older);
            foreach (var m in older)
            {
                _messagePeer[m.Id] = peerId;
            }
        }
    }

    public List<YapperDmMessageDto> Thread(Guid peerId)
    {
        lock (_gate)
        {
            return _threads.TryGetValue(peerId, out var list) ? list.ToList() : [];
        }
    }

    /// <summary>Appends an incoming or just-sent message and bumps the conversation row.</summary>
    public void Append(Guid peerId, YapperDmMessageDto message, YapAuthorDto? peer, bool countUnread)
    {
        lock (_gate)
        {
            if (_threads.TryGetValue(peerId, out var list) && list.All(m => m.Id != message.Id))
            {
                list.Add(message);
            }
            _messagePeer[message.Id] = peerId;
            var idx = _conversations.FindIndex(c => c.Peer.ProfileId == peerId);
            if (idx >= 0)
            {
                var row = _conversations[idx];
                _conversations.RemoveAt(idx);
                _conversations.Insert(0, row with
                {
                    LastMessageAtUtc = message.SentAtUtc,
                    Unread = countUnread ? row.Unread + 1 : row.Unread,
                    LastMessage = message,
                });
            }
            else if (peer is not null)
            {
                _peers[peerId] = peer;
                _conversations.Insert(0, new YapperDmConversationDto(
                    peer, _peerKeys.GetValueOrDefault(peerId), message.SentAtUtc, countUnread ? 1 : 0, message));
            }
        }
    }

    public void MarkRead(Guid peerId)
    {
        lock (_gate)
        {
            var idx = _conversations.FindIndex(c => c.Peer.ProfileId == peerId);
            if (idx >= 0)
            {
                _conversations[idx] = _conversations[idx] with { Unread = 0 };
            }
        }
    }

    public void ApplyPeerRead(Guid peerId, Guid[] messageIds, DateTimeOffset readAt)
    {
        Mutate(peerId, m => messageIds.Contains(m.Id) ? m with { ReadByPeerAtUtc = readAt } : m);
    }

    public void ApplyReaction(Guid messageId, Guid profileId, string token, bool added)
    {
        lock (_gate)
        {
            if (!_messagePeer.TryGetValue(messageId, out var peerId)
                || !_threads.TryGetValue(peerId, out var list))
            {
                return;
            }
            var idx = list.FindIndex(m => m.Id == messageId);
            if (idx < 0)
            {
                return;
            }
            var reactions = (list[idx].Reactions ?? []).ToList();
            var mine = reactions.FindIndex(r => r.ProfileId == profileId);
            var tokens = mine >= 0 ? reactions[mine].Tokens.ToList() : [];
            if (added && !tokens.Contains(token))
            {
                tokens.Add(token);
            }
            else if (!added)
            {
                tokens.Remove(token);
            }
            if (mine >= 0)
            {
                reactions.RemoveAt(mine);
            }
            if (tokens.Count > 0)
            {
                reactions.Add(new YapperDmReactionsDto(profileId, tokens.ToArray()));
            }
            list[idx] = list[idx] with { Reactions = reactions.Count == 0 ? null : reactions.ToArray() };
        }
    }

    public void ApplyPin(Guid messageId, DateTimeOffset? pinnedAt)
    {
        MutateById(messageId, m => m with { PinnedAtUtc = pinnedAt });
    }

    public void ApplyDeleted(Guid messageId)
    {
        MutateById(messageId, m => m with
        {
            DeletedAtUtc = DateTimeOffset.UtcNow, Ciphertext = [], Nonce = [], PinnedAtUtc = null,
        });
    }

    private void Mutate(Guid peerId, Func<YapperDmMessageDto, YapperDmMessageDto> mutate)
    {
        lock (_gate)
        {
            if (!_threads.TryGetValue(peerId, out var list))
            {
                return;
            }
            for (var i = 0; i < list.Count; i++)
            {
                list[i] = mutate(list[i]);
            }
        }
    }

    private void MutateById(Guid messageId, Func<YapperDmMessageDto, YapperDmMessageDto> mutate)
    {
        lock (_gate)
        {
            if (!_messagePeer.TryGetValue(messageId, out var peerId)
                || !_threads.TryGetValue(peerId, out var list))
            {
                return;
            }
            var idx = list.FindIndex(m => m.Id == messageId);
            if (idx >= 0)
            {
                list[idx] = mutate(list[idx]);
            }
        }
    }
}
