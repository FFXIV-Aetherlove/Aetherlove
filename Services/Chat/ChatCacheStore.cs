using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AetherLove.Shared.Messaging;
using MessagePack;

namespace AetherLove.Services.Chat;

/// <summary>Persistent local chat cache; only ciphertext is written to disk, plaintext stays in memory.</summary>
public sealed class ChatCacheStore
{
    /// <summary>The "changes since X" delta cursor, persisted so a new session resumes incrementally.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed record CacheCursor(DateTimeOffset MsgUtc, DateTimeOffset MsgCreatedUtc, DateTimeOffset MatchUtc);

    private readonly object _lock = new();
    private readonly string _dir;
    private readonly List<MatchSummaryDto> _matches = new();
    private readonly Dictionary<Guid, List<EncryptedMessageDto>> _conversations = new();
    private CacheCursor _cursor = new(DateTimeOffset.MinValue, DateTimeOffset.MinValue, DateTimeOffset.MinValue);
    private Guid _owner = Guid.Empty;

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

    /// <summary>Scopes the cache to the signed-in profile; a different owner wipes it. <see cref="Guid.Empty"/> (a server that predates the field) is a no-op.</summary>
    public void EnsureOwner(Guid profileId)
    {
        if (profileId == Guid.Empty)
        {
            return;
        }
        bool wipe;
        lock (_lock)
        {
            if (_owner == profileId)
            {
                return;
            }
            wipe = _owner != Guid.Empty;
            if (wipe)
            {
                _matches.Clear();
                _conversations.Clear();
                _cursor = new CacheCursor(DateTimeOffset.MinValue, DateTimeOffset.MinValue, DateTimeOffset.MinValue);
            }
            _owner = profileId;
        }
        if (wipe)
        {
            Plugin.Log.Information("[ChatCache] Cache belongs to a different profile; wiping it.");
            DeleteAllFiles();
        }
        WriteOwner();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _matches.Clear();
            _conversations.Clear();
            _cursor = new CacheCursor(DateTimeOffset.MinValue, DateTimeOffset.MinValue, DateTimeOffset.MinValue);
            _owner = Guid.Empty;
        }
        DeleteAllFiles();
    }

    public void RemovePeer(Guid peer)
    {
        bool matchesDirty;
        lock (_lock)
        {
            matchesDirty = _matches.RemoveAll(x => x.PeerProfileId == peer) > 0;
            _conversations.Remove(peer);
        }
        if (matchesDirty)
        {
            WriteMatches();
        }
        TryDelete(ConvPath(peer));
    }

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
    private string OwnerPath => Path.Combine(_dir, "owner.mp");
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
            if (File.Exists(OwnerPath))
            {
                _owner = MessagePackSerializer.Deserialize<Guid>(File.ReadAllBytes(OwnerPath));
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

    private void WriteOwner()
    {
        try
        {
            Guid owner;
            lock (_lock)
            {
                owner = _owner;
            }
            File.WriteAllBytes(OwnerPath, MessagePackSerializer.Serialize(owner));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[ChatCache] writing the owner stamp failed.");
        }
    }

    private void DeleteAllFiles()
    {
        TryDelete(MatchesPath);
        TryDelete(CursorPath);
        TryDelete(OwnerPath);
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "c_*.mp"))
            {
                TryDelete(f);
            }
        }
        catch
        {
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
