using System;
using MessagePack;

namespace AetherLove.Shared.Messenger;

/// <summary>Whether a messenger chat is a 1:1 contact chat or a group. Direct chats are identified by the
/// contact-pair id, groups by the group id; the two id spaces never mix.</summary>
public enum MessengerChatKind : short
{
    Direct = 0,
    Group = 1,
}

/// <summary>Shared client/server limits for the messenger. Caps that the server enforces from config are
/// carried on <see cref="MessengerSyncDto"/> instead, so only true wire constants live here.</summary>
public static class MessengerLimits
{
    /// <summary>Friend code length (alphabet in <c>MessengerCodes</c> server-side); rendered XXXX@XXXX.</summary>
    public const int CodeLength = 8;

    public const int MaxGroupNameChars = 40;
    public const int MaxMessageChars = 500;
    /// <summary>Generous bound for a 500-char message after encryption; anything bigger is abuse.</summary>
    public const int MaxCiphertextBytes = 8 * 1024;
    public const int MaxReactionsPerUser = 5;

    /// <summary>Max raw chat-image upload accepted before processing.</summary>
    public const int MaxImageUploadBytes = 25 * 1024 * 1024;

    /// <summary>Long edge the server downscales a chat image to (aspect preserved).</summary>
    public const int ImageMaxDimension = 1920;
}

/// <summary>An accepted contact: one row per pair, doubling as the 1:1 chat summary. Name/avatar are the
/// LIVE account identity when available (the snapshot taken at accept is the server-side fallback for a
/// deleted account). <see cref="RemovedByPeer"/> renders the chat read-only with the removal notice.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerContactDto(
    Guid ContactId,
    Guid PeerAccountId,
    string PeerName,
    byte[]? PeerAvatar,
    byte[]? PeerPublicKey,
    DateTimeOffset AddedAtUtc,
    bool PinnedByMe,
    int Unread,
    DateTimeOffset? LastMessageAtUtc,
    byte[]? LastMessageCiphertext,
    byte[]? LastMessageNonce,
    bool LastMessageFromMe,
    bool RemovedByPeer,
    string? PeerFrameRef = null);

/// <summary>A pending add: incoming rows render above the chat list with accept/decline; outgoing rows show
/// the pending state on the sender's side.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerRequestDto(
    Guid ContactId,
    Guid PeerAccountId,
    string PeerName,
    byte[]? PeerAvatar,
    bool Incoming,
    DateTimeOffset RequestedAtUtc,
    string? PeerFrameRef = null);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerGroupMemberDto(
    Guid AccountId,
    string Name,
    byte[]? Avatar,
    byte[]? PublicKey,
    bool IsOwner,
    DateTimeOffset JoinedAtUtc,
    string? FrameRef = null);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerGroupDto(
    Guid GroupId,
    string Name,
    byte[]? Avatar,
    Guid OwnerAccountId,
    int KeyEpoch,
    MessengerGroupMemberDto[] Members,
    bool PinnedByMe,
    int Unread,
    DateTimeOffset? LastMessageAtUtc,
    byte[]? LastMessageCiphertext,
    byte[]? LastMessageNonce,
    Guid? LastMessageSenderId,
    DateTimeOffset CreatedAtUtc);

/// <summary>One member's reaction tokens on a message. Group-capable replacement for the match chat's
/// two-sided mine/theirs split; the client knows its own account id from the sync.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerReactionsDto(Guid AccountId, string[] Tokens);

/// <summary>One ciphertext message. Direct messages are encrypted pairwise (epoch 0); group messages carry
/// the key epoch they were encrypted under so any member can pick the right group key.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerMessageDto(
    Guid Id,
    Guid ChatId,
    MessengerChatKind Kind,
    Guid SenderAccountId,
    byte[] Ciphertext,
    byte[] Nonce,
    int KeyEpoch,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ReadByPeerAtUtc,
    Guid? ReplyToMessageId = null,
    MessengerReactionsDto[]? Reactions = null,
    DateTimeOffset? PinnedAtUtc = null,
    MessengerImageDto? Image = null,
    DateTimeOffset? DeletedAtUtc = null);

/// <summary>A plaintext (moderated, NOT E2E) image attachment on a message. The bytes live server-side and
/// are fetched by <see cref="ImageId"/>; the server deletes the blob after <see cref="ExpiresAtUtc"/>.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerImageDto(
    Guid ImageId,
    int Width,
    int Height,
    long ByteSize,
    DateTimeOffset ExpiresAtUtc);

/// <summary>The trailing fields are additive (default null): <see cref="PeerKeyHistory"/> is the peer's
/// account-key timeline for direct chats (reset boundaries render the "keys reset" notice),
/// <see cref="MyKeysCreatedAtUtc"/> is when the caller's active account keypair was created.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerConversationDto(
    Guid ChatId,
    MessengerChatKind Kind,
    MessengerMessageDto[] Messages,
    Messaging.KeyHistoryEntryDto[]? PeerKeyHistory = null,
    DateTimeOffset? MyKeysCreatedAtUtc = null);

/// <summary>One epoch of a group's symmetric key, wrapped for the calling member via ECDH between the
/// wrapper's and the member's ACCOUNT keypairs. New members receive every epoch (full history); leavers
/// stop at their last epoch.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerGroupKeyDto(
    Guid GroupId,
    int Epoch,
    Guid WrapperAccountId,
    byte[] WrappedKey,
    byte[] Nonce);

/// <summary>One member's wrap of a group key epoch, as uploaded by the wrapping client.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerGroupKeyWrapDto(
    Guid MemberAccountId,
    byte[] WrappedKey,
    byte[] Nonce);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record UploadGroupKeysRequest(
    Guid GroupId,
    int Epoch,
    MessengerGroupKeyWrapDto[] Wraps);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record SendMessengerMessageRequest(
    Guid ChatId,
    MessengerChatKind Kind,
    byte[] Ciphertext,
    byte[] Nonce,
    int KeyEpoch,
    Guid? ReplyToMessageId = null);

/// <summary>Uploads an image (plaintext, moderated) and creates the carrying message in one call. The optional
/// caption is E2E like a normal message body; the image itself is not.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record SendMessengerImageRequest(
    Guid ChatId,
    MessengerChatKind Kind,
    int KeyEpoch,
    Profile.PhotoUploadDto Image,
    byte[]? CaptionCiphertext = null,
    byte[]? CaptionNonce = null,
    Guid? ReplyToMessageId = null,
    // Only camera captures are accepted while file uploads (photo album / disk) are held back pre-release.
    bool FromCamera = false,
    // Sender-chosen lifetime in hours (see SupporterLimits.ImageTtlHourOptions); the server clamps it to the
    // sender's tier cap. Defaults to the free cap.
    int ExpiryHours = 72);

/// <summary>Concurrent image-storage snapshot for the "Data limits" screen: current usage vs the account cap,
/// plus the caller's stored images sorted by expiry (each frees its bytes when it expires or is deleted).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerStorageDto(
    long UsedBytes,
    long CapBytes,
    MessengerStorageItemDto[] Items,
    // The caller's supporter status, so the compose picker can enable the longer expiry options.
    bool IsSupporter = false);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerStorageItemDto(
    Guid ImageId,
    long ByteSize,
    DateTimeOffset ExpiresAtUtc,
    string ChatName);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record CreateMessengerGroupRequest(
    string Name,
    Guid[] MemberAccountIds);

/// <summary>Everything the messenger needs on open/reconnect: identity, effective caps (0 = unlimited),
/// requests, contacts and groups with their list denormals. Conversations are fetched per chat on open and
/// kept fresh by pushes.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerSyncDto(
    Guid MyAccountId,
    string MyCode,
    bool AllowAdds,
    bool HasAccountKeyBundle,
    int ContactCount,
    int ContactCap,
    int GroupsCreated,
    int GroupCap,
    int GroupSizeCap,
    MessengerRequestDto[] Requests,
    MessengerContactDto[] Contacts,
    MessengerGroupDto[] Groups);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerBlockedDto(Guid AccountId, string Name, DateTimeOffset BlockedAtUtc);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerRequestPushDto(MessengerRequestDto Request);

/// <summary>A contact was accepted or its denormals changed; the client upserts the row.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerContactChangedPushDto(MessengerContactDto Contact);

/// <summary>The peer removed me: keep the chat visible read-only with the removal notice.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerContactRemovedPushDto(Guid ContactId, string PeerName);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerMessagePushDto(MessengerMessageDto Message);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerReadPushDto(Guid ContactId, DateTimeOffset ReadAtUtc, Guid[] MessageIds);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerReactionPushDto(
    Guid ChatId, MessengerChatKind Kind, Guid MessageId, MessengerReactionsDto[] Reactions);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerPinPushDto(
    Guid ChatId, MessengerChatKind Kind, Guid MessageId, DateTimeOffset? PinnedAtUtc);

/// <summary>The author deleted a message: every participant's client blanks its copy and renders the
/// tombstone.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerMessageDeletedPushDto(Guid ChatId, MessengerChatKind Kind, Guid MessageId);

/// <summary>Group meta or membership changed; the client replaces the group row (and refetches keys when
/// <see cref="MessengerGroupDto.KeyEpoch"/> advanced past what it holds).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerGroupChangedPushDto(MessengerGroupDto Group);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerRemovedFromGroupPushDto(Guid GroupId, string GroupName, bool Kicked);

/// <summary>A fellow group member reset their E2E keys: the group's epoch rotated and their name is shown in
/// the "keys reset" notification.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerMemberKeysResetPushDto(Guid GroupId, string GroupName, Guid AccountId, string Name);

/// <summary>An image was removed (owner delete or moderation): every participant's client flips it to the
/// expired placeholder and drops any cached copy immediately.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record MessengerImageRemovedPushDto(Guid ImageId);
