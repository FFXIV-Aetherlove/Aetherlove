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

/// <summary>One ciphertext message as the server stores and serves it.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EncryptedMessageDto(
    Guid Id,
    Guid SenderProfileId,
    byte[] Ciphertext,
    byte[] Nonce,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadByOtherAtUtc);

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
    byte[] Nonce);

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
    DateTimeOffset CreatedAtUtc);

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
