using System;
using MessagePack;

namespace AetherLove.Shared.Messaging;

/// <summary>X25519 identity bundle. Server stores opaque; private key is wrapped under the user's passphrase.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record KeyBundleDto(
    byte[] PublicKey,
    byte[] EncryptedPrivateKey,
    byte[] KdfSalt,
    int KdfMemoryKb,
    int KdfIterations,
    int KdfParallelism,
    byte[] WrapNonce);

/// <summary>One ciphertext message as the server stores and serves it. The reaction/reply/pin fields are
/// additive (default null) so an older client that lacks them simply ignores the extra keys.
/// <see cref="MyReactions"/>/<see cref="TheirReactions"/> are caller-relative: the server maps its two
/// per-participant columns to "mine" vs "theirs" for the requesting client (which never knows its own
/// profile id), so a client can only ever remove the reactions in <see cref="MyReactions"/>.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EncryptedMessageDto(
    Guid Id,
    Guid SenderProfileId,
    byte[] Ciphertext,
    byte[] Nonce,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadByOtherAtUtc,
    Guid? ReplyToMessageId = null,
    string[]? MyReactions = null,
    string[]? TheirReactions = null,
    DateTimeOffset? PinnedAtUtc = null,
    DateTimeOffset UpdatedAtUtc = default);

/// <summary>Load-all conversation snapshot with the peer's public key.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ConversationHistoryDto(
    Guid PeerProfileId,
    byte[] PeerPublicKey,
    EncryptedMessageDto[] Messages);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record SendMessageRequest(
    Guid PeerProfileId,
    byte[] Ciphertext,
    byte[] Nonce,
    Guid? ReplyToMessageId = null);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record SendMessageResponse(
    Guid MessageId,
    DateTimeOffset CreatedAtUtc);

/// <summary>Push from server to the recipient when a new message lands.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessageReceivedPushDto(
    Guid MessageId,
    Guid FromProfileId,
    byte[] Ciphertext,
    byte[] Nonce,
    DateTimeOffset CreatedAtUtc,
    Guid? ReplyToMessageId = null);

/// <summary>Push from server to the sender when the recipient marks the conversation read.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessageReadPushDto(
    Guid ByProfileId,
    DateTimeOffset ReadAtUtc,
    Guid[] MessageIds);

/// <summary>Push to both sides when one of them unmatches.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record UnmatchedPushDto(Guid OtherProfileId);

/// <summary>Push to the blocked side; their plugin should close the chat and drop the row.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record BlockedByPeerPushDto(Guid OtherProfileId);

/// <summary>One row in the chat / match list. The last-message fields carry the E2E ciphertext so
/// the client can decrypt a short preview locally — the server never sees plaintext.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MatchSummaryDto(
    Guid PeerProfileId,
    string PeerDisplayName,
    byte[] PeerAvatarWebp,
    byte[] PeerPublicKey,
    DateTimeOffset MatchedAtUtc,
    DateTimeOffset? LastMessageAtUtc,
    byte[] LastMessageCiphertext,
    byte[] LastMessageNonce,
    bool LastMessageFromMe,
    int UnreadCount,
    bool IsPinned);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record MatchListDto(MatchSummaryDto[] Matches);

/// <summary>Client → server delta cursor: "give me everything changed after this point across all my chats."
/// Messages page by the compound <c>(MsgSinceUtc, MsgSinceCreatedUtc)</c> = the last applied message's
/// (UpdatedAtUtc, CreatedAtUtc), so a shared UpdatedAtUtc tick never splits across a page. Matches advance on
/// their own <see cref="MatchSinceUtc"/> so message paging does not skip a match. First sync sends
/// <see cref="DateTimeOffset.MinValue"/> for all three (a full build).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ChatDeltaRequest(
    DateTimeOffset MsgSinceUtc,
    DateTimeOffset MsgSinceCreatedUtc,
    DateTimeOffset MatchSinceUtc);

/// <summary>Changed messages for one conversation, from the caller's perspective (peer = the other participant),
/// so the client knows which conversation to apply them to. Reactions are caller-relative as in
/// <see cref="EncryptedMessageDto"/>.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ConversationMessagesDelta(
    Guid PeerProfileId,
    EncryptedMessageDto[] Messages);

/// <summary>Server → client delta response. <see cref="Conversations"/> carries new and mutated messages (upsert
/// by <see cref="EncryptedMessageDto.Id"/>); <see cref="ChangedMatches"/> and <see cref="RemovedMatches"/> are
/// populated only on the final page (<see cref="HasMore"/> false). The client pages messages while
/// <see cref="HasMore"/> is true (advancing the message cursor), applies matches on the final page, then stores
/// all three cursors: message (<see cref="NextMsgCursorUtc"/>/<see cref="NextMsgCursorCreatedUtc"/>) and match
/// (<see cref="NextMatchCursorUtc"/>).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ChatDeltaDto(
    ConversationMessagesDelta[] Conversations,
    MatchSummaryDto[] ChangedMatches,
    Guid[] RemovedMatches,
    DateTimeOffset NextMsgCursorUtc,
    DateTimeOffset NextMsgCursorCreatedUtc,
    DateTimeOffset NextMatchCursorUtc,
    bool HasMore);

/// <summary>Add (<see cref="Add"/> = true) or remove a reaction shortcode on a message. The server only ever
/// touches the caller's own reaction column, so a user can never remove another user's reactions.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ReactToMessageRequest(
    Guid PeerProfileId,
    Guid MessageId,
    string Emoji,
    bool Add);

/// <summary>Pin or unpin a message in a conversation. Pins are shared: either participant may toggle them.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SetMessagePinnedRequest(
    Guid PeerProfileId,
    Guid MessageId,
    bool Pinned);

/// <summary>Returned to the caller and pushed to the peer when a message's reactions change. The reaction
/// lists are relative to whoever receives this DTO: <see cref="MyReactions"/> is the receiver's own column,
/// <see cref="TheirReactions"/> is the other participant's. <see cref="PeerProfileId"/> is the conversation
/// peer from the receiver's perspective, used to route the change to the right open chat.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessageReactionsChangedPushDto(
    Guid PeerProfileId,
    Guid MessageId,
    string[] MyReactions,
    string[] TheirReactions);

/// <summary>Returned to the caller and pushed to the peer when a message is pinned or unpinned.
/// <see cref="PeerProfileId"/> is the conversation peer from the receiver's perspective.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessagePinChangedPushDto(
    Guid PeerProfileId,
    Guid MessageId,
    DateTimeOffset? PinnedAtUtc);
