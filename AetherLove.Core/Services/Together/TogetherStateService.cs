using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AetherLove.Shared.Together;

namespace AetherLove.Services.Together;

/// <summary>One chat line with its client-side arrival sequence.</summary>
public sealed record TogetherChatEntry(long Seq, TogetherChatLineDto Line);

/// <summary>The client's view of the party it is in. Hub pushes land here from the signal thread, so the
/// state itself is lock-guarded and every event is QUEUED rather than raised: the shell calls
/// <see cref="DrainEvents"/> once per frame, which invokes subscribers on the draw thread where touching
/// ImGui is legal. Snapshots are full replaces, never merges.</summary>
public sealed class TogetherStateService
{
    private const int ChatClientLines = 100;

    private readonly object _lock = new();
    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly List<TogetherChatEntry> _chat = new();
    private TogetherPartySnapshotDto? _party;
    private TogetherEndReason? _endReason;
    private long _chatSeq;
    private int _unreadChat;
    private int _dirty;

    /// <summary>The party changed in any way (membership, presence).</summary>
    public event Action? PartyChanged;

    public event Action<TogetherPartyEndedDto>? PartyEnded;

    public event Action<TogetherKickedDto>? Kicked;

    /// <summary>The local account, stamped by the session bootstrapper, so surfaces can answer "am I the
    /// host" without threading the account through every call.</summary>
    public Guid? OwnAccountId { get; set; }

    public bool AmHost
    {
        get
        {
            lock (_lock)
            {
                return _party is not null && OwnAccountId is { } id && _party.HostAccountId == id;
            }
        }
    }

    /// <summary>The party to re-sync after a reconnect; null once it ended or the user left.</summary>
    public Guid? CurrentPartyId
    {
        get
        {
            lock (_lock)
            {
                return _endReason is null ? _party?.Id : null;
            }
        }
    }

    public TogetherPartySnapshotDto? Party
    {
        get
        {
            lock (_lock)
            {
                return _party;
            }
        }
    }

    /// <summary>Set once the party ends; the snapshot is kept so the shell can render a farewell over it
    /// until the user dismisses it and something calls <see cref="Clear"/>.</summary>
    public TogetherEndReason? EndReason
    {
        get
        {
            lock (_lock)
            {
                return _endReason;
            }
        }
    }

    public IReadOnlyList<TogetherMemberDto> Members
    {
        get
        {
            lock (_lock)
            {
                return _party?.Members ?? [];
            }
        }
    }

    /// <summary>The party chat, oldest first. Entries carry a client-side sequence so surfaces can tell
    /// which lines are new since they last looked without comparing contents.</summary>
    public IReadOnlyList<TogetherChatEntry> ChatLines
    {
        get
        {
            lock (_lock)
            {
                return [.. _chat];
            }
        }
    }

    /// <summary>Lines from OTHERS that arrived while no chat surface was open; a surface calls
    /// <see cref="MarkChatRead"/> when it shows them.</summary>
    public int UnreadChat
    {
        get
        {
            lock (_lock)
            {
                return _unreadChat;
            }
        }
    }

    public void MarkChatRead()
    {
        lock (_lock)
        {
            if (_unreadChat == 0)
            {
                return;
            }
            _unreadChat = 0;
        }
        MarkChanged();
    }

    /// <summary>The party's current activity, or null while it is idle or the party ended.</summary>
    public TogetherActivityDto? Activity
    {
        get
        {
            lock (_lock)
            {
                return _endReason is null ? _party?.Activity : null;
            }
        }
    }

    public bool IsHost(Guid accountId)
    {
        lock (_lock)
        {
            return _party is not null && _party.HostAccountId == accountId;
        }
    }

    /// <summary>Invokes the queued events on the calling (draw) thread. Call once per frame. A burst of
    /// pushes collapses into a single <see cref="PartyChanged"/>.</summary>
    public void DrainEvents()
    {
        if (Interlocked.Exchange(ref _dirty, 0) == 1)
        {
            Invoke(() => PartyChanged?.Invoke());
        }
        while (_pending.TryDequeue(out var action))
        {
            Invoke(action);
        }
    }

    private static void Invoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            UiHost.Log.Warning(ex, "[Together] A party event handler threw.");
        }
    }

    public void ApplySnapshot(TogetherPartySnapshotDto snapshot)
    {
        lock (_lock)
        {
            var samePartyAsBefore = _party?.Id == snapshot.Id;
            _party = snapshot;
            _endReason = null;
            // The snapshot's replay ring replaces the local chat wholesale; replayed lines are context,
            // never unread. A same-party re-sync keeps the unread count it had.
            _chat.Clear();
            foreach (var line in snapshot.RecentChat ?? [])
            {
                _chat.Add(new TogetherChatEntry(++_chatSeq, line));
            }
            if (!samePartyAsBefore)
            {
                _unreadChat = 0;
            }
        }
        MarkChanged();
    }

    public void ApplyChat(TogetherChatLineDto line)
    {
        lock (_lock)
        {
            if (_party is null || _party.Id != line.PartyId)
            {
                return;
            }
            _chat.Add(new TogetherChatEntry(++_chatSeq, line));
            if (_chat.Count > ChatClientLines)
            {
                _chat.RemoveAt(0);
            }
            // A join notice must not light the unread badge: nobody said anything to read. An activity
            // notice (the host opened a hunt or a room) is exactly the thing the badge is for.
            var worthReading = !line.IsSystem || line.Kind is not null;
            if (worthReading && (OwnAccountId is not { } own || line.AccountId != own))
            {
                _unreadChat++;
            }
        }
        MarkChanged();
    }

    public void ApplyMemberJoined(Guid partyId, TogetherMemberDto member)
    {
        lock (_lock)
        {
            if (_party is null || _party.Id != partyId)
            {
                return;
            }
            _party = _party with
            {
                Members = [.. _party.Members.Where(m => m.AccountId != member.AccountId), member],
            };
        }
        MarkChanged();
    }

    public void ApplyMemberLeft(TogetherMemberLeftDto push)
    {
        lock (_lock)
        {
            if (_party is null || _party.Id != push.PartyId)
            {
                return;
            }
            _party = _party with
            {
                Members = [.. _party.Members.Where(m => m.AccountId != push.AccountId)],
            };
        }
        MarkChanged();
    }

    public void ApplyMemberPresence(TogetherMemberPresenceDto push)
    {
        lock (_lock)
        {
            if (_party is null || _party.Id != push.PartyId)
            {
                return;
            }
            _party = _party with
            {
                Members = [.. _party.Members.Select(m =>
                    m.AccountId == push.AccountId ? m with { Connected = push.Connected } : m)],
            };
        }
        MarkChanged();
    }

    public void ApplyActivityChanged(TogetherActivityChangedDto push)
    {
        lock (_lock)
        {
            if (_party is null || _party.Id != push.PartyId)
            {
                return;
            }
            _party = _party with { Activity = push.Activity };
        }
        MarkChanged();
    }

    public void ApplyPartyEnded(TogetherPartyEndedDto push)
    {
        lock (_lock)
        {
            if (_party is null || _party.Id != push.PartyId)
            {
                return;
            }
            _endReason = push.Reason;
        }
        MarkChanged();
        Queue(() => PartyEnded?.Invoke(push));
    }

    public void ApplyKicked(TogetherKickedDto push)
    {
        lock (_lock)
        {
            if (_party is null || _party.Id != push.PartyId)
            {
                return;
            }
            _party = null;
            _endReason = null;
            _chat.Clear();
            _unreadChat = 0;
        }
        MarkChanged();
        Queue(() => Kicked?.Invoke(push));
    }

    public void Clear()
    {
        lock (_lock)
        {
            _party = null;
            _endReason = null;
            _chat.Clear();
            _unreadChat = 0;
        }
        MarkChanged();
    }

    private void MarkChanged() => Interlocked.Exchange(ref _dirty, 1);

    private void Queue(Action handler) => _pending.Enqueue(handler);
}
