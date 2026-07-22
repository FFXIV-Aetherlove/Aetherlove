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
    private readonly string _baseDir;
    private string _dir;
    private readonly List<MatchSummaryDto> _matches = new();
    private readonly Dictionary<Guid, List<EncryptedMessageDto>> _conversations = new();
    private CacheCursor _cursor = new(DateTimeOffset.MinValue, DateTimeOffset.MinValue, DateTimeOffset.MinValue);
    private Guid _owner = Guid.Empty;

    public ChatCacheStore()
    {
        _baseDir = Path.Combine(UiHost.PluginInterface.ConfigDirectory.FullName, "ChatCache");
        _dir = _baseDir;
        try
        {
            Directory.CreateDirectory(_baseDir);
            if (File.Exists(OwnerPath))
            {
                _owner = MessagePackSerializer.Deserialize<Guid>(File.ReadAllBytes(OwnerPath));
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[ChatCache] reading the owner stamp failed; starting unowned.");
            _owner = Guid.Empty;
        }
        if (_owner != Guid.Empty)
        {
            _dir = ProfileDir(_owner);
            MigrateLegacyFlatFiles();
            lock (_lock)
            {
                Load();
            }
        }
    }

    private string ProfileDir(Guid profileId) => Path.Combine(_baseDir, $"p_{profileId:N}");

    /// <summary>Moves a pre-multi-profile flat cache (files directly in the base folder) into the owning
    /// profile's subfolder, once.</summary>
    private void MigrateLegacyFlatFiles()
    {
        try
        {
            Directory.CreateDirectory(_dir);
            foreach (var f in Directory.EnumerateFiles(_baseDir, "*.mp"))
            {
                var name = Path.GetFileName(f);
                if (name == "owner.mp")
                {
                    continue;
                }
                var dest = Path.Combine(_dir, name);
                if (File.Exists(dest))
                {
                    File.Delete(f);
                }
                else
                {
                    File.Move(f, dest);
                }
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[ChatCache] migrating the flat cache into the profile folder failed.");
        }
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

    /// <summary>The profile this cache is currently scoped to (diagnostics).</summary>
    public Guid Owner
    {
        get
        {
            lock (_lock)
            {
                return _owner;
            }
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

    /// <summary>The peer's key timeline from the cached summary; more than one entry means they reset.</summary>
    public KeyHistoryEntryDto[]? GetPeerKeyHistory(Guid peer)
    {
        lock (_lock)
        {
            return _matches.FirstOrDefault(x => x.PeerProfileId == peer)?.PeerKeyHistory;
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

    /// <summary>Scopes the cache to the signed-in profile. Each profile has its own subfolder, so switching
    /// swaps folders in place and never destroys a sibling's cache. <see cref="Guid.Empty"/> (a server that
    /// predates the field) is a no-op.</summary>
    public void EnsureOwner(Guid profileId)
    {
        if (profileId == Guid.Empty)
        {
            return;
        }
        lock (_lock)
        {
            if (_owner == profileId)
            {
                UiHost.Log.Debug("[PSW] ChatCache.EnsureOwner: already owned by {Profile:N} ({Count} matches), no-op.", profileId, _matches.Count);
                return;
            }
            var prev = _owner;
            _matches.Clear();
            _conversations.Clear();
            _cursor = new CacheCursor(DateTimeOffset.MinValue, DateTimeOffset.MinValue, DateTimeOffset.MinValue);
            _owner = profileId;
            _dir = ProfileDir(profileId);
            try
            {
                Directory.CreateDirectory(_dir);
            }
            catch
            {
            }
            Load();
            UiHost.Log.Debug("[PSW] ChatCache.EnsureOwner: swapped {Prev:N} -> {Profile:N}, loaded {Count} matches from {Dir}.", prev, profileId, _matches.Count, _dir);
        }
        WriteOwner();
    }

    /// <summary>Wipes the CURRENT profile's cache folder and unowns the store (sign-out or profile deletion);
    /// sibling profiles' folders are untouched.</summary>
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
        TryDelete(OwnerPath);
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
            // Drop deltas for mismatched profiles (prevents chat merge during no-reconnect switches); Empty pre-field servers keep as-is.
            if (d.ForProfileId != Guid.Empty && d.ForProfileId != _owner)
            {
                UiHost.Log.Debug("[PSW] ChatCache.ApplyDelta: DROPPED delta for {For:N} (owner={Owner:N}) - {Msgs} convos / {Matches} changed / {Removed} removed.", d.ForProfileId, _owner, d.Conversations.Length, d.ChangedMatches.Length, d.RemovedMatches.Length);
                return;
            }
            UiHost.Log.Debug("[PSW] ChatCache.ApplyDelta: applying delta for {For:N} to owner {Owner:N} - {Msgs} convos / {Matches} changed / {Removed} removed.", d.ForProfileId, _owner, d.Conversations.Length, d.ChangedMatches.Length, d.RemovedMatches.Length);
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
    // The owner stamp lives in the BASE folder: it names which profile folder to load on the next boot.
    private string OwnerPath => Path.Combine(_baseDir, "owner.mp");
    private string ConvPath(Guid peer) => Path.Combine(_dir, $"c_{peer:N}.mp");

    /// <summary>Loads the current <see cref="_dir"/> profile's data files. It must NOT read the owner stamp:
    /// the caller (ctor or <see cref="EnsureOwner"/>) has already set <see cref="_owner"/>, and re-reading the
    /// persisted stamp here would clobber a just-switched owner back to the previous profile.</summary>
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
                    UiHost.Log.Warning(ex, "[ChatCache] dropping a corrupt conversation file.");
                    TryDelete(f);
                }
            }
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[ChatCache] load failed; starting empty.");
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
            UiHost.Log.Warning(ex, "[ChatCache] writing the match list failed.");
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
            UiHost.Log.Warning(ex, "[ChatCache] writing the cursor failed.");
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
            UiHost.Log.Warning(ex, "[ChatCache] writing a conversation failed.");
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
            UiHost.Log.Warning(ex, "[ChatCache] writing the owner stamp failed.");
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
