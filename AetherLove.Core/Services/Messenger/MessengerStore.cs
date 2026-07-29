using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AetherLove.Shared.Messenger;
using MessagePack;

namespace AetherLove.Services.Messenger;

/// <summary>Local messenger state: the last sync snapshot plus per-chat conversations, mutated by pushes and
/// persisted account-level (one folder for the whole account, unlike the per-profile chat cache). Only
/// ciphertext reaches disk; decrypted text and unwrapped group keys stay in memory.</summary>
public sealed class MessengerStore
{
    private readonly object _lock = new();
    private readonly string _dir;
    private Guid _owner = Guid.Empty;
    private MessengerSyncDto? _sync;
    private readonly Dictionary<Guid, List<MessengerMessageDto>> _conversations = new();
    private readonly Dictionary<(Guid GroupId, int Epoch), byte[]> _groupKeys = new();

    // Peer key timelines per direct chat (memory only; refreshed by each conversation fetch). More than one
    // entry means the peer reset their E2E keys at some point.
    private readonly Dictionary<Guid, Shared.Messaging.KeyHistoryEntryDto[]> _keyHistory = new();

    // Images force-removed by a live push (owner delete or moderation). Memory only: after a restart the
    // refetch returns null and reaches the same expired placeholder.
    private readonly HashSet<Guid> _removedImages = new();

    /// <summary>Raised when a live push removes an image, so the app can purge its cached copy.</summary>
    public event Action<Guid>? ImageRemoved;

    public bool IsImageRemoved(Guid imageId)
    {
        lock (_lock)
        {
            return _removedImages.Contains(imageId);
        }
    }

    public void ApplyImageRemoved(Guid imageId)
    {
        lock (_lock)
        {
            _removedImages.Add(imageId);
            Version++;
        }
        ImageRemoved?.Invoke(imageId);
    }

    /// <summary>Bumped on every mutation; per-frame UI readers compare it to invalidate derived state.</summary>
    public int Version { get; private set; }

    /// <summary>Chat the user is looking at right now (contact or group id); suppresses its notifications.</summary>
    public Guid? ActiveChatId { get; set; }

    public MessengerStore()
    {
        _dir = Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "MessengerCache");
        try
        {
            Directory.CreateDirectory(_dir);
            if (File.Exists(OwnerPath))
            {
                _owner = MessagePackSerializer.Deserialize<Guid>(File.ReadAllBytes(OwnerPath));
            }
            if (_owner != Guid.Empty)
            {
                Load();
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[MessengerStore] loading the cache failed; starting empty.");
        }
    }

    private string OwnerPath => Path.Combine(_dir, "owner.mp");
    private string SnapshotPath => Path.Combine(_dir, "sync.mp");
    private string ConversationPath(Guid chatId) => Path.Combine(_dir, $"conv_{chatId:N}.mp");

    /// <summary>Stamps the owning account; a different account wipes the previous one's cache.</summary>
    public void EnsureOwner(Guid accountId)
    {
        lock (_lock)
        {
            if (_owner == accountId)
            {
                return;
            }
            if (_owner != Guid.Empty)
            {
                WipeFilesLocked();
            }
            _owner = accountId;
            _sync = null;
            _conversations.Clear();
            _groupKeys.Clear();
            Version++;
            try
            {
                Directory.CreateDirectory(_dir);
                File.WriteAllBytes(OwnerPath, MessagePackSerializer.Serialize(accountId));
            }
            catch (Exception ex)
            {
                UiHost.Log.Warning(ex, "[MessengerStore] writing the owner stamp failed.");
            }
        }
    }

    /// <summary>Full local wipe (the /love clearcache command): deletes every cached file and drops all
    /// in-memory state, resetting the owner so the next sync rebuilds from scratch.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            WipeFilesLocked();
            _owner = Guid.Empty;
            _sync = null;
            _conversations.Clear();
            _groupKeys.Clear();
            _keyHistory.Clear();
            Version++;
        }
    }

    public Guid MyAccountId
    {
        get
        {
            lock (_lock)
            {
                return _sync?.MyAccountId ?? _owner;
            }
        }
    }

    public MessengerSyncDto? Sync
    {
        get
        {
            lock (_lock)
            {
                return _sync;
            }
        }
    }

    public IReadOnlyList<MessengerContactDto> Contacts
    {
        get
        {
            lock (_lock)
            {
                return _sync?.Contacts ?? [];
            }
        }
    }

    public IReadOnlyList<MessengerRequestDto> Requests
    {
        get
        {
            lock (_lock)
            {
                return _sync?.Requests ?? [];
            }
        }
    }

    public IReadOnlyList<MessengerGroupDto> Groups
    {
        get
        {
            lock (_lock)
            {
                return _sync?.Groups ?? [];
            }
        }
    }

    public MessengerContactDto? Contact(Guid contactId)
    {
        lock (_lock)
        {
            return _sync?.Contacts.FirstOrDefault(c => c.ContactId == contactId);
        }
    }

    public MessengerGroupDto? Group(Guid groupId)
    {
        lock (_lock)
        {
            return _sync?.Groups.FirstOrDefault(g => g.GroupId == groupId);
        }
    }

    public int TotalUnread()
    {
        lock (_lock)
        {
            if (_sync is null)
            {
                return 0;
            }
            return _sync.Contacts.Sum(c => c.Unread) + _sync.Groups.Sum(g => g.Unread);
        }
    }

    public int IncomingRequestCount()
    {
        lock (_lock)
        {
            return _sync?.Requests.Count(r => r.Incoming) ?? 0;
        }
    }

    public void ApplySync(MessengerSyncDto sync)
    {
        lock (_lock)
        {
            _sync = sync;
            // The chat the user is actively viewing must never resurrect an unread count. A full sync that
            // lands while a chat is open (e.g. the one OnForeground fires) carries the server's still-stale
            // unread until its mark-read round trip completes, which otherwise re-lights the tile/chat badge.
            if (ActiveChatId is { } active)
            {
                _sync = _sync with
                {
                    Contacts = _sync.Contacts.Select(c => c.ContactId == active ? c with { Unread = 0 } : c).ToArray(),
                    Groups = _sync.Groups.Select(g => g.GroupId == active ? g with { Unread = 0 } : g).ToArray(),
                };
            }
            // Conversations for chats that no longer exist (removed by me, disbanded groups) are dropped.
            var live = _sync.Contacts.Select(c => c.ContactId).Concat(_sync.Groups.Select(g => g.GroupId)).ToHashSet();
            foreach (var stale in _conversations.Keys.Where(id => !live.Contains(id)).ToList())
            {
                _conversations.Remove(stale);
                TryDelete(ConversationPath(stale));
            }
            Version++;
            SaveSnapshotLocked();
        }
    }

    public IReadOnlyList<MessengerMessageDto> Conversation(Guid chatId)
    {
        lock (_lock)
        {
            return _conversations.TryGetValue(chatId, out var list) ? list.ToArray() : [];
        }
    }

    public bool HasConversation(Guid chatId)
    {
        lock (_lock)
        {
            return _conversations.ContainsKey(chatId);
        }
    }

    public void SetConversation(Guid chatId, IEnumerable<MessengerMessageDto> messages)
    {
        lock (_lock)
        {
            var list = messages.OrderBy(m => m.CreatedAtUtc).ToList();
            // A message applied while the fetch was in flight (own send ack, push echo) survives the replace.
            if (_conversations.TryGetValue(chatId, out var existing) && existing.Count > 0)
            {
                var fetched = list.Select(m => m.Id).ToHashSet();
                var newest = list.Count > 0 ? list[^1].CreatedAtUtc : DateTimeOffset.MinValue;
                var appended = false;
                foreach (var m in existing)
                {
                    if (!fetched.Contains(m.Id) && m.CreatedAtUtc >= newest)
                    {
                        list.Add(m);
                        appended = true;
                    }
                }
                if (appended)
                {
                    list.Sort((a, b) => a.CreatedAtUtc.CompareTo(b.CreatedAtUtc));
                }
            }
            _conversations[chatId] = list;
            Version++;
            SaveConversationLocked(chatId);
        }
    }

    public void ApplyRequest(MessengerRequestDto request)
    {
        lock (_lock)
        {
            if (_sync is null)
            {
                return;
            }
            var rest = _sync.Requests.Where(r => r.ContactId != request.ContactId);
            _sync = _sync with { Requests = rest.Prepend(request).ToArray() };
            Version++;
            SaveSnapshotLocked();
        }
    }

    public void RemoveRequest(Guid contactId)
    {
        lock (_lock)
        {
            if (_sync is null)
            {
                return;
            }
            _sync = _sync with { Requests = _sync.Requests.Where(r => r.ContactId != contactId).ToArray() };
            Version++;
            SaveSnapshotLocked();
        }
    }

    /// <summary>Upserts a contact row (accept, denormal refresh, re-add) and drops any matching request.</summary>
    public void ApplyContactChanged(MessengerContactDto contact)
    {
        lock (_lock)
        {
            if (_sync is null)
            {
                return;
            }
            var rest = _sync.Contacts.Where(c => c.ContactId != contact.ContactId);
            _sync = _sync with
            {
                Contacts = rest.Append(contact).ToArray(),
                Requests = _sync.Requests.Where(r => r.ContactId != contact.ContactId).ToArray(),
                ContactCount = _sync.Contacts.Count(c => c.ContactId != contact.ContactId && !c.RemovedByPeer)
                    + (contact.RemovedByPeer ? 0 : 1),
            };
            Version++;
            SaveSnapshotLocked();
        }
    }

    /// <summary>The peer removed me (tombstone with their name), or my own removal echoed to another device
    /// (empty name: the chat disappears).</summary>
    public void ApplyContactRemoved(Guid contactId, string peerName)
    {
        lock (_lock)
        {
            if (_sync is null)
            {
                return;
            }
            if (peerName.Length == 0)
            {
                _sync = _sync with { Contacts = _sync.Contacts.Where(c => c.ContactId != contactId).ToArray() };
                _conversations.Remove(contactId);
                TryDelete(ConversationPath(contactId));
            }
            else
            {
                _sync = _sync with
                {
                    Contacts = _sync.Contacts
                        .Select(c => c.ContactId == contactId ? c with { RemovedByPeer = true, Unread = 0 } : c)
                        .ToArray(),
                };
            }
            Version++;
            SaveSnapshotLocked();
        }
    }

    /// <summary>Appends a pushed or sent message, bumping the owning row's denormals (and unread when it is
    /// someone else's message in a chat the user isn't looking at).</summary>
    public void ApplyMessage(MessengerMessageDto message)
    {
        lock (_lock)
        {
            if (_conversations.TryGetValue(message.ChatId, out var list))
            {
                list.RemoveAll(m => m.Id == message.Id);
                list.Add(message);
                list.Sort((a, b) => a.CreatedAtUtc.CompareTo(b.CreatedAtUtc));
                SaveConversationLocked(message.ChatId);
            }
            if (_sync is not null)
            {
                var fromMe = message.SenderAccountId == _sync.MyAccountId;
                var viewing = ActiveChatId == message.ChatId;
                if (message.Kind == MessengerChatKind.Direct)
                {
                    _sync = _sync with
                    {
                        Contacts = _sync.Contacts.Select(c => c.ContactId == message.ChatId
                            ? c with
                            {
                                LastMessageAtUtc = message.CreatedAtUtc,
                                LastMessageCiphertext = message.Ciphertext,
                                LastMessageNonce = message.Nonce,
                                LastMessageFromMe = fromMe,
                                Unread = fromMe || viewing ? c.Unread : c.Unread + 1,
                            }
                            : c).ToArray(),
                    };
                }
                else
                {
                    _sync = _sync with
                    {
                        Groups = _sync.Groups.Select(g => g.GroupId == message.ChatId
                            ? g with
                            {
                                LastMessageAtUtc = message.CreatedAtUtc,
                                LastMessageCiphertext = message.Ciphertext,
                                LastMessageNonce = message.Nonce,
                                LastMessageSenderId = message.SenderAccountId,
                                Unread = fromMe || viewing ? g.Unread : g.Unread + 1,
                            }
                            : g).ToArray(),
                    };
                }
                SaveSnapshotLocked();
            }
            Version++;
        }
    }

    /// <summary>Zeroes a chat's local unread counter (mark-read round trips confirm server-side).</summary>
    public void MarkReadLocal(Guid chatId)
    {
        lock (_lock)
        {
            if (_sync is null)
            {
                return;
            }
            _sync = _sync with
            {
                Contacts = _sync.Contacts.Select(c => c.ContactId == chatId ? c with { Unread = 0 } : c).ToArray(),
                Groups = _sync.Groups.Select(g => g.GroupId == chatId ? g with { Unread = 0 } : g).ToArray(),
            };
            Version++;
            SaveSnapshotLocked();
        }
    }

    /// <summary>The peer read my direct messages: stamp their read receipt.</summary>
    public void ApplyRead(Guid contactId, DateTimeOffset readAtUtc, Guid[] messageIds)
    {
        lock (_lock)
        {
            if (!_conversations.TryGetValue(contactId, out var list))
            {
                return;
            }
            var ids = messageIds.ToHashSet();
            for (var i = 0; i < list.Count; i++)
            {
                if (ids.Contains(list[i].Id) && list[i].ReadByPeerAtUtc is null)
                {
                    list[i] = list[i] with { ReadByPeerAtUtc = readAtUtc };
                }
            }
            Version++;
            SaveConversationLocked(contactId);
        }
    }

    public void ApplyReactions(Guid chatId, Guid messageId, MessengerReactionsDto[] reactions)
    {
        lock (_lock)
        {
            if (!_conversations.TryGetValue(chatId, out var list))
            {
                return;
            }
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Id == messageId)
                {
                    list[i] = list[i] with { Reactions = reactions };
                    break;
                }
            }
            Version++;
            SaveConversationLocked(chatId);
        }
    }

    /// <summary>Author delete: scrub the cached copy down to the tombstone right away (ciphertext leaves this
    /// device's disk with the push, not at the next sync) and refresh the chat-list denormal from the cached
    /// conversation. True when the conversation isn't cached here, so the preview may still hold the deleted
    /// content and a sync should reconcile it.</summary>
    public bool ApplyMessageDeleted(Guid chatId, Guid messageId)
    {
        lock (_lock)
        {
            Version++;
            if (!_conversations.TryGetValue(chatId, out var list))
            {
                return true;
            }
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Id == messageId)
                {
                    list[i] = list[i] with
                    {
                        Ciphertext = [],
                        Nonce = [],
                        DeletedAtUtc = DateTimeOffset.UtcNow,
                        PinnedAtUtc = null,
                        Reactions = null,
                        Image = null,
                    };
                    break;
                }
            }
            SaveConversationLocked(chatId);

            var lastLive = list.LastOrDefault(m => m.DeletedAtUtc is null);
            if (_sync is null)
            {
                return false;
            }
            if (_sync.Contacts.Any(c => c.ContactId == chatId))
            {
                _sync = _sync with
                {
                    Contacts = _sync.Contacts.Select(c => c.ContactId == chatId
                        ? c with
                        {
                            LastMessageAtUtc = lastLive?.CreatedAtUtc,
                            LastMessageCiphertext = lastLive?.Ciphertext,
                            LastMessageNonce = lastLive?.Nonce,
                            LastMessageFromMe = lastLive is not null && lastLive.SenderAccountId == MyAccountIdLocked(),
                        }
                        : c).ToArray(),
                };
            }
            else
            {
                _sync = _sync with
                {
                    Groups = _sync.Groups.Select(g => g.GroupId == chatId
                        ? g with
                        {
                            LastMessageAtUtc = lastLive?.CreatedAtUtc,
                            LastMessageCiphertext = lastLive?.Ciphertext,
                            LastMessageNonce = lastLive?.Nonce,
                            LastMessageSenderId = lastLive?.SenderAccountId,
                        }
                        : g).ToArray(),
                };
            }
            SaveSnapshotLocked();
            return false;
        }
    }

    private Guid MyAccountIdLocked() => _sync?.MyAccountId ?? _owner;

    public void ApplyPin(Guid chatId, Guid messageId, DateTimeOffset? pinnedAtUtc)
    {
        lock (_lock)
        {
            if (!_conversations.TryGetValue(chatId, out var list))
            {
                return;
            }
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Id == messageId)
                {
                    list[i] = list[i] with { PinnedAtUtc = pinnedAtUtc };
                    break;
                }
            }
            Version++;
            SaveConversationLocked(chatId);
        }
    }

    /// <summary>Group meta/membership changed: replace the row (or add it, e.g. just added to a group).
    /// Unwrapped keys for epochs past the new one don't exist yet; the key ring refetches on demand.</summary>
    public void ApplyGroupChanged(MessengerGroupDto group)
    {
        lock (_lock)
        {
            if (_sync is null)
            {
                return;
            }
            var rest = _sync.Groups.Where(g => g.GroupId != group.GroupId);
            _sync = _sync with { Groups = rest.Append(group).ToArray() };
            Version++;
            SaveSnapshotLocked();
        }
    }

    public void ApplyRemovedFromGroup(Guid groupId)
    {
        lock (_lock)
        {
            if (_sync is null)
            {
                return;
            }
            _sync = _sync with { Groups = _sync.Groups.Where(g => g.GroupId != groupId).ToArray() };
            _conversations.Remove(groupId);
            var epochs = _groupKeys.Keys.Where(k => k.GroupId == groupId).ToList();
            foreach (var k in epochs)
            {
                _groupKeys.Remove(k);
            }
            TryDelete(ConversationPath(groupId));
            Version++;
            SaveSnapshotLocked();
        }
    }

    public Shared.Messaging.KeyHistoryEntryDto[]? PeerKeyHistory(Guid chatId)
    {
        lock (_lock)
        {
            return _keyHistory.GetValueOrDefault(chatId);
        }
    }

    public void SetPeerKeyHistory(Guid chatId, Shared.Messaging.KeyHistoryEntryDto[]? history)
    {
        lock (_lock)
        {
            if (history is { Length: > 0 })
            {
                _keyHistory[chatId] = history;
            }
            else
            {
                _keyHistory.Remove(chatId);
            }
        }
    }

    public byte[]? GroupKey(Guid groupId, int epoch)
    {
        lock (_lock)
        {
            return _groupKeys.GetValueOrDefault((groupId, epoch));
        }
    }

    public void StoreGroupKey(Guid groupId, int epoch, byte[] key)
    {
        lock (_lock)
        {
            _groupKeys[(groupId, epoch)] = key;
        }
    }

    /// <summary>Epochs 1..current the local ring is missing for a group (drives a wrap refetch).</summary>
    public int[] MissingEpochs(Guid groupId, int currentEpoch)
    {
        lock (_lock)
        {
            return Enumerable.Range(1, Math.Max(0, currentEpoch))
                .Where(e => !_groupKeys.ContainsKey((groupId, e)))
                .ToArray();
        }
    }

    private void Load()
    {
        if (File.Exists(SnapshotPath))
        {
            _sync = MessagePackSerializer.Deserialize<MessengerSyncDto>(File.ReadAllBytes(SnapshotPath));
        }
        foreach (var file in Directory.EnumerateFiles(_dir, "conv_*.mp"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (Guid.TryParse(name["conv_".Length..], out var chatId))
            {
                _conversations[chatId] =
                    MessagePackSerializer.Deserialize<List<MessengerMessageDto>>(File.ReadAllBytes(file));
            }
        }
    }

    private void SaveSnapshotLocked()
    {
        if (_sync is null)
        {
            return;
        }
        try
        {
            File.WriteAllBytes(SnapshotPath, MessagePackSerializer.Serialize(_sync));
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[MessengerStore] saving the snapshot failed.");
        }
    }

    private void SaveConversationLocked(Guid chatId)
    {
        if (!_conversations.TryGetValue(chatId, out var list))
        {
            return;
        }
        try
        {
            File.WriteAllBytes(ConversationPath(chatId), MessagePackSerializer.Serialize(list));
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[MessengerStore] saving a conversation failed.");
        }
    }

    private void WipeFilesLocked()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "*.mp"))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[MessengerStore] wiping the previous account's cache failed.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort; a stale conversation file is re-dropped on the next sync.
        }
    }
}
