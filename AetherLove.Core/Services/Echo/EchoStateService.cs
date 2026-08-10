using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AetherLove.Shared.EchoVidya;

namespace AetherLove.Services.Echo;

/// <summary>The client's view of the Echo room it is in. Hub pushes land here from the signal thread, so the
/// state itself is lock-guarded and every event is QUEUED rather than raised: the app calls
/// <see cref="DrainEvents"/> once per frame, which invokes subscribers on the draw thread where touching
/// ImGui is legal. Chat is in-memory only and capped, never persisted.</summary>
public sealed class EchoStateService
{
    private readonly object _lock = new();
    private readonly Queue<EchoChatLineDto> _chat = new();
    private readonly ConcurrentQueue<Action> _pending = new();
    private EchoRoomSnapshotDto? _room;
    private EchoEndReason? _endReason;
    private int _roomDirty;

    /// <summary>The room changed in any way (membership, playlist, playback, host-only).</summary>
    public event Action? RoomChanged;

    public event Action<EchoChatLineDto>? ChatReceived;

    public event Action<EchoRoomEndedDto>? RoomEnded;

    public event Action<EchoKickedDto>? Kicked;

    /// <summary>The room to re-sync after a reconnect; null once the room ended or the user left.</summary>
    public Guid? CurrentRoomId
    {
        get
        {
            lock (_lock)
            {
                return _endReason is null ? _room?.Id : null;
            }
        }
    }

    public EchoRoomSnapshotDto? Room
    {
        get
        {
            lock (_lock)
            {
                return _room;
            }
        }
    }

    /// <summary>Set once the room ends; the snapshot is kept so the UI can render a farewell over it until it
    /// navigates away and calls <see cref="Clear"/>.</summary>
    public EchoEndReason? EndReason
    {
        get
        {
            lock (_lock)
            {
                return _endReason;
            }
        }
    }

    public IReadOnlyList<EchoMemberDto> Members
    {
        get
        {
            lock (_lock)
            {
                return _room?.Members ?? [];
            }
        }
    }

    public IReadOnlyList<EchoPlaylistEntryDto> Playlist
    {
        get
        {
            lock (_lock)
            {
                return _room?.Playlist ?? [];
            }
        }
    }

    public EchoPlaybackDto? Playback
    {
        get
        {
            lock (_lock)
            {
                return _room?.Playback;
            }
        }
    }

    /// <summary>The entry the room is playing, or null when the queue is empty or the entry is gone.</summary>
    public EchoPlaylistEntryDto? CurrentEntry
    {
        get
        {
            lock (_lock)
            {
                var id = _room?.Playback.CurrentEntryId;
                return id is null ? null : _room!.Playlist.FirstOrDefault(e => e.Id == id.Value);
            }
        }
    }

    public IReadOnlyList<EchoChatLineDto> Chat
    {
        get
        {
            lock (_lock)
            {
                return _chat.ToArray();
            }
        }
    }

    public bool IsOwner(Guid accountId)
    {
        lock (_lock)
        {
            return _room is not null && _room.OwnerAccountId == accountId;
        }
    }

    /// <summary>Whether the given member may drive playback and the playlist.</summary>
    public bool CanControl(Guid accountId)
    {
        lock (_lock)
        {
            return _room is not null && (!_room.HostOnly || _room.OwnerAccountId == accountId);
        }
    }

    /// <summary>Invokes the queued events on the calling (draw) thread. Call once per frame. A burst of pushes
    /// collapses into a single <see cref="RoomChanged"/>.</summary>
    public void DrainEvents()
    {
        if (Interlocked.Exchange(ref _roomDirty, 0) == 1)
        {
            Invoke(() => RoomChanged?.Invoke());
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
            UiHost.Log.Warning(ex, "[Echo] A room event handler threw.");
        }
    }

    /// <summary>Full replace from a join or re-sync fetch, never a merge. Chat survives a re-sync of the same
    /// room (it is relay-only, so a refetch would lose it).</summary>
    public void ApplySnapshot(EchoRoomSnapshotDto snapshot)
    {
        lock (_lock)
        {
            if (_room?.Id != snapshot.Id)
            {
                _chat.Clear();
            }
            _room = Normalize(snapshot);
            _endReason = null;
        }
        MarkChanged();
    }

    public void ApplyMemberJoined(Guid roomId, EchoMemberDto member)
    {
        lock (_lock)
        {
            if (_room is null || _room.Id != roomId)
            {
                return;
            }
            _room = _room with
            {
                Members = [.. _room.Members.Where(m => m.AccountId != member.AccountId), member],
            };
        }
        MarkChanged();
    }

    public void ApplyMemberLeft(EchoMemberLeftDto push)
    {
        lock (_lock)
        {
            if (_room is null || _room.Id != push.RoomId)
            {
                return;
            }
            _room = _room with
            {
                Members = [.. _room.Members.Where(m => m.AccountId != push.AccountId)],
            };
        }
        MarkChanged();
    }

    public void ApplyPlaylistChanged(Guid roomId, EchoPlaylistEntryDto[] playlist)
    {
        lock (_lock)
        {
            if (_room is null || _room.Id != roomId)
            {
                return;
            }
            _room = _room with { Playlist = Sort(playlist) };
        }
        MarkChanged();
    }

    public void ApplyPlaybackChanged(EchoPlaybackDto playback)
    {
        lock (_lock)
        {
            if (_room is null || _room.Id != playback.RoomId)
            {
                return;
            }
            _room = _room with { Playback = playback };
        }
        MarkChanged();
    }

    public void ApplyHostOnlyChanged(Guid roomId, bool hostOnly)
    {
        lock (_lock)
        {
            if (_room is null || _room.Id != roomId)
            {
                return;
            }
            _room = _room with { HostOnly = hostOnly };
        }
        MarkChanged();
    }

    public void ApplyChat(EchoChatLineDto line)
    {
        lock (_lock)
        {
            if (_room is null || _room.Id != line.RoomId)
            {
                return;
            }
            _chat.Enqueue(line);
            while (_chat.Count > EchoLimits.MaxChatHistory)
            {
                _chat.Dequeue();
            }
        }
        Queue(() => ChatReceived?.Invoke(line));
    }

    public void ApplyRoomEnded(EchoRoomEndedDto push)
    {
        lock (_lock)
        {
            if (_room is null || _room.Id != push.RoomId)
            {
                return;
            }
            _endReason = push.Reason;
        }
        MarkChanged();
        Queue(() => RoomEnded?.Invoke(push));
    }

    public void ApplyKicked(EchoKickedDto push)
    {
        lock (_lock)
        {
            if (_room is null || _room.Id != push.RoomId)
            {
                return;
            }
            ClearCore();
        }
        MarkChanged();
        Queue(() => Kicked?.Invoke(push));
    }

    public void Clear()
    {
        lock (_lock)
        {
            ClearCore();
        }
        MarkChanged();
    }

    private void ClearCore()
    {
        _room = null;
        _endReason = null;
        _chat.Clear();
    }

    private void MarkChanged() => Interlocked.Exchange(ref _roomDirty, 1);

    private void Queue(Action handler) => _pending.Enqueue(handler);

    private static EchoRoomSnapshotDto Normalize(EchoRoomSnapshotDto snapshot) =>
        snapshot with { Playlist = Sort(snapshot.Playlist) };

    private static EchoPlaylistEntryDto[] Sort(EchoPlaylistEntryDto[] playlist) =>
        [.. playlist.OrderBy(e => e.Order)];
}
