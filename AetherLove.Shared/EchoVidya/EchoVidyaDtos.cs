using System;
using MessagePack;

namespace AetherLove.Shared.EchoVidya;

// New fields are appended as trailing parameters with defaults; existing parameters are never reordered
// or removed. Trailing defaults keep old servers/clients wire-compatible.

/// <summary>One person in a room. <see cref="AvatarImage"/> is the OS avatar's bytes, inline the way party
/// members carry theirs (JPEG, the always-renders fallback); <see cref="AvatarUrl"/> is a leftover that
/// stays null. Echo never surfaces dating profiles.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EchoMemberDto(
    Guid AccountId,
    string DisplayName,
    string? AvatarUrl,
    DateTimeOffset JoinedAtUtc,
    bool IsOwner,
    string? FrameRef = null,
    byte[]? AvatarImage = null);

/// <summary>One queued video. <see cref="Title"/> is null until a client resolves it;
/// <see cref="Failed"/> marks an entry every client failed to play, so it renders as unavailable rather
/// than stalling the queue.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EchoPlaylistEntryDto(
    Guid Id,
    string VideoId,
    string? Title,
    Guid? AddedByAccountId,
    string? AddedByName,
    int Order,
    bool Failed);

/// <summary>The room's authoritative playback position. <see cref="UpdatedAtUtc"/> is when the server
/// stamped it, so a joining client extrapolates the live position instead of starting behind.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EchoPlaybackDto(
    Guid RoomId,
    Guid? CurrentEntryId,
    double PositionSeconds,
    bool IsPlaying,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Everything a client needs on join or reconnect. Always a full replace of the client's room
/// state, never a merge. <see cref="HostOnly"/> restricts playback and playlist control to the owner.
/// <see cref="HangoutId"/> is the live watch-party hangout this room was published as, null when it has
/// none; a room may only ever have one at a time.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EchoRoomSnapshotDto(
    Guid Id,
    string Code,
    string Name,
    Guid OwnerAccountId,
    bool HostOnly,
    EchoMemberDto[] Members,
    EchoPlaylistEntryDto[] Playlist,
    EchoPlaybackDto Playback,
    Guid? HangoutId = null);

/// <summary>One in-room chat line. Not E2E and not persisted: lines are relayed live and kept only in the
/// client's ring buffer.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EchoChatLineDto(
    Guid RoomId,
    Guid AccountId,
    string DisplayName,
    string Text,
    DateTimeOffset AtUtc);

/// <summary>Lean payload for a room rendered outside itself: directory rows and share cards.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EchoRoomCardDto(
    Guid Id,
    string Code,
    string Name,
    int MemberCount,
    string OwnerName,
    string? NowPlayingTitle);

/// <summary>The current playback host build. <see cref="Sha256"/> is verified against the download before
/// anything is executed.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EchoHostManifestDto(
    string Version,
    string Url,
    string Sha256,
    long SizeBytes);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record EchoMemberLeftDto(Guid RoomId, Guid AccountId);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record EchoRoomEndedDto(Guid RoomId, EchoEndReason Reason);

/// <summary>Push to the kicked member only; the room name rides along so the notification reads without a
/// snapshot the client no longer has.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EchoKickedDto(Guid RoomId, string RoomName);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record CreateEchoRoomRequest(string Name);
