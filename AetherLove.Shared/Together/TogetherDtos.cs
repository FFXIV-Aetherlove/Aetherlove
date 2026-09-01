using System;
using MessagePack;

namespace AetherLove.Shared.Together;

// New fields are appended as trailing parameters with defaults; existing parameters are never reordered
// or removed. Trailing defaults keep old servers/clients wire-compatible.

/// <summary>One person in a party. Identity is the OS identity; dating profiles are never involved.
/// <see cref="Connected"/> is false while the member's account holds no hub socket: they stay in the
/// roster, dimmed, until the presence grace expires and the sweep retracts them.
/// <see cref="AvatarImage"/> is the OS avatar's bytes (JPEG, the always-renders fallback), inline the way
/// hangout cards carry theirs; null when the member never set one.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record TogetherMemberDto(
    Guid AccountId,
    string DisplayName,
    DateTimeOffset JoinedAtUtc,
    bool IsHost,
    bool Connected,
    string? FrameRef = null,
    TogetherPetDto? Pet = null,
    byte[]? AvatarImage = null);

/// <summary>A party member's Aetherling, as much of it as another client needs to draw one: the form it
/// has grown into, what it is painted and wearing, and what it is called. Sent only for members whose own
/// sharing switch is on, and read fresh from each snapshot rather than stored anywhere by the receiver.
/// <see cref="Stage"/> is 0-2 for the three hatchling forms and 3 for the adult, which is the same ladder
/// the client's own form resolver walks; asset names never cross the wire.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record TogetherPetDto(
    short Stage,
    string Palette,
    string[] Accessories,
    string? Name = null,
    string Shell = "");

/// <summary>What the party is doing right now. Stamped only by the owning app's server service (an Echo
/// room bind, a Wayfinder run), never by a client call. <see cref="Code"/> is an optional join code for
/// the activity itself (an Echo room's code), for the one-tap join on the shade card.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record TogetherActivityDto(
    string AppId,
    Guid RefId,
    string? Code = null);

/// <summary>One relayed chat line. Never stored server-side beyond the in-memory replay ring; the
/// display name is resolved at send time so the line is self-contained.
/// <see cref="IsSystem"/> marks a server-authored notice: <see cref="Text"/> then carries only the
/// subject's display name and the CLIENT renders the sentence in its own language. <see cref="Kind"/> says
/// which sentence: null is the join notice, otherwise the activity's app id ("wayfinder", "echo") with
/// <see cref="RefId"/> and <see cref="Code"/> carrying what a tap on the line should open.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record TogetherChatLineDto(
    Guid PartyId,
    Guid AccountId,
    string DisplayName,
    string Text,
    DateTimeOffset SentAtUtc,
    bool IsSystem = false,
    string? Kind = null,
    Guid? RefId = null,
    string? Code = null);

/// <summary>Everything a client needs on join or reconnect. Always a full replace of the client's party
/// state, never a merge.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record TogetherPartySnapshotDto(
    Guid Id,
    string Code,
    Guid HostAccountId,
    TogetherMemberDto[] Members,
    int MaxMembers,
    TogetherActivityDto? Activity = null,
    TogetherChatLineDto[]? RecentChat = null);

/// <summary>The lean payload for an invite card. Null once the party is gone, so a stale card renders as
/// unavailable instead of resurrecting a dead party.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record TogetherPartyCardDto(
    Guid Id,
    string Code,
    string HostName,
    int MemberCount,
    int MaxMembers);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record TogetherMemberLeftDto(Guid PartyId, Guid AccountId);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record TogetherMemberPresenceDto(Guid PartyId, Guid AccountId, bool Connected);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record TogetherPartyEndedDto(Guid PartyId, TogetherEndReason Reason);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record TogetherKickedDto(Guid PartyId);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record TogetherActivityChangedDto(Guid PartyId, TogetherActivityDto? Activity);
