using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AetherLove.Shared.Messaging;
using MessagePack;

namespace AetherLove.Services.Chat;

/// <summary>Persistent local chat cache: the match list and every conversation's E2E ciphertext, plus the server
/// delta cursor. Only ciphertext (never plaintext) is written to disk; message text is decrypted in memory in the
/// screens. Files are MessagePack under <c>ConfigDirectory/ChatCache</c>. A corrupt file is dropped, never fatal.</summary>
public sealed class ChatCacheStore
{
    /// <summary>The client's "changes since X" cursor: last applied message (UpdatedAtUtc, CreatedAtUtc) and the
    /// separate match cursor. Serialized alongside the cache so a new session resumes incrementally.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed record CacheCursor(DateTimeOffset MsgUtc, DateTimeOffset MsgCreatedUtc, DateTimeOffset MatchUtc);

    private readonly object _lock = new();
    private readonly string _dir;
    private readonly List<MatchSummaryDto> _matches = new();
    private readonly Dictionary<Guid, List<EncryptedMessageDto>> _conversations = new();
    private CacheCursor _cursor = new(DateTimeOffset.MinValue, DateTimeOffset.MinValue, DateTimeOffset.MinValue);

    public ChatCacheStore()
    {
        _dir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "ChatCache");
        Load();
    }

    public (DateTimeOffset MsgUtc, DateTimeOffset MsgCreatedUtc, DateTimeOffset MatchUtc) Cursors
    {
        get
        {
            lock (_lock)
            {
                return (_cursor.MsgUtc, _cursor.MsgCreatedUtc, _cursor.MatchUtc);
            }
        }
    }

    public IReadOnlyList<MatchSummaryDto> GetMatches()
    {
        lock (_lock)
        {
            return _matches.ToArray();
        }
    }

    /// <summary>The peer's public key, taken from the cached match summary (needed to derive the message key).</summary>
    public byte[]? GetPeerPublicKey(Guid peer)
    {
        lock (_lock)
        {
            var m = _matches.FirstOrDefault(x => x.PeerProfileId == peer);
            return m?.PeerPublicKey is { Length: > 0 } key ? key : null;
        }
    }

    public bool HasConversation(Guid peer)
    {
        lock (_lock)
        {
            return _conversations.ContainsKey(peer);
        }
    }

    public EncryptedMessageDto[] GetConversation(Guid peer)
    {
        lock (_lock)
        {
            return _conversations.TryGetValue(peer, out var list) ? list.ToArray() : [];
        }
    }

    /// <summary>Seeds a conversation from a one-shot full fetch, the fallback used when the delta has not yet
    /// covered a brand-new match.</summary>
    public void SeedConversation(Guid peer, EncryptedMessageDto[] messages)
    {
        lock (_lock)
        {
            var list = _conversations.TryGetValue(peer, out var existing) ? existing : new List<EncryptedMessageDto>();
            UpsertMessages(list, messages);
            _conversations[peer] = list;
        }
        WriteConversation(peer);
    }

    /// <summary>Applies one delta page: upserts changed messages by id, upserts/removes matches, advances and
    /// persists the cursor. Called off the UI thread by <see cref="ChatSyncService"/>.</summary>
    public void ApplyDelta(ChatDeltaDto d)
    {
        var dirtyPeers = new List<Guid>();
        bool matchesDirty;
        lock (_lock)
        {
            foreach (var c in d.Conversations)
            {
                var list = _conversations.TryGetValue(c.PeerProfileId, out var existing) ? existing : new List<EncryptedMessageDto>();
                UpsertMessages(list, c.Messages);
                _conversations[c.PeerProfileId] = list;
                dirtyPeers.Add(c.PeerProfileId);
            }
            foreach (var m in d.ChangedMatches)
            {
                var idx = _matches.FindIndex(x => x.PeerProfileId == m.PeerProfileId);
                if (idx >= 0)
                {
                    _matches[idx] = m;
                }
                else
                {
                    _matches.Add(m);
                }
            }
            foreach (var peer in d.RemovedMatches)
            {
                _matches.RemoveAll(x => x.PeerProfileId == peer);
            }
            matchesDirty = d.ChangedMatches.Length > 0 || d.RemovedMatches.Length > 0;
            _cursor = new CacheCursor(d.NextMsgCursorUtc, d.NextMsgCursorCreatedUtc, d.NextMatchCursorUtc);
        }

        foreach (var peer in dirtyPeers)
        {
            WriteConversation(peer);
        }
        if (matchesDirty)
        {
            WriteMatches();
        }
        WriteCursor();
    }

    private static void UpsertMessages(List<EncryptedMessageDto> list, EncryptedMessageDto[] incoming)
    {
        foreach (var m in incoming)
        {
            var idx = list.FindIndex(x => x.Id == m.Id);
            if (idx >= 0)
            {
                list[idx] = m;
            }
            else
            {
                list.Add(m);
            }
        }
        list.Sort((x, y) => x.CreatedAtUtc.CompareTo(y.CreatedAtUtc));
    }

    private string MatchesPath => Path.Combine(_dir, "matches.mp");
    private string CursorPath => Path.Combine(_dir, "cursor.mp");
    private string ConvPath(Guid peer) => Path.Combine(_dir, $"c_{peer:N}.mp");

    private void Load()
    {
        try
        {
            Directory.CreateDirectory(_dir);
            if (File.Exists(MatchesPath))
            {
                _matches.AddRange(MessagePackSerializer.Deserialize<MatchSummaryDto[]>(File.ReadAllBytes(MatchesPath)));
            }
            if (File.Exists(CursorPath))
            {
                _cursor = MessagePackSerializer.Deserialize<CacheCursor>(File.ReadAllBytes(CursorPath)) ?? _cursor;
            }
            foreach (var f in Directory.EnumerateFiles(_dir, "c_*.mp"))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    if (name.Length > 2 && Guid.TryParse(name[2..], out var peer))
                    {
                        _conversations[peer] = MessagePackSerializer
                            .Deserialize<EncryptedMessageDto[]>(File.ReadAllBytes(f)).ToList();
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warning(ex, "[ChatCache] dropping a corrupt conversation file.");
                    TryDelete(f);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[ChatCache] load failed; starting empty.");
        }
    }

    private void WriteMatches()
    {
        try
        {
            MatchSummaryDto[] snapshot;
            lock (_lock)
            {
                snapshot = _matches.ToArray();
            }
            File.WriteAllBytes(MatchesPath, MessagePackSerializer.Serialize(snapshot));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[ChatCache] writing the match list failed.");
        }
    }

    private void WriteCursor()
    {
        try
        {
            CacheCursor c;
            lock (_lock)
            {
                c = _cursor;
            }
            File.WriteAllBytes(CursorPath, MessagePackSerializer.Serialize(c));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[ChatCache] writing the cursor failed.");
        }
    }

    private void WriteConversation(Guid peer)
    {
        try
        {
            EncryptedMessageDto[] snapshot;
            lock (_lock)
            {
                snapshot = _conversations.TryGetValue(peer, out var l) ? l.ToArray() : [];
            }
            File.WriteAllBytes(ConvPath(peer), MessagePackSerializer.Serialize(snapshot));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[ChatCache] writing a conversation failed.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
