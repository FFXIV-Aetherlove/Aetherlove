using System;
using AetherLove.Shared.Profile;
using MessagePack;

namespace AetherLove.Shared.Yapper;

/// <summary>Handle claim/rename pre-check result.</summary>
public enum YapperHandleCheck : short
{
    Available = 1,
    Taken = 2,
    Invalid = 3,
    Rejected = 4,
}

/// <summary>Per-kind notification opt-ins, carried whole in both directions.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperNotifyPrefsDto(
    bool Likes,
    bool Replies,
    bool Reposts,
    bool Mentions,
    bool Follows,
    bool NewPosts);

/// <summary>The caller's own Yapper profile.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperMyProfileDto(
    Guid ProfileId,
    string Handle,
    string DisplayName,
    string? Bio,
    byte[]? Avatar,
    byte[]? Banner,
    bool IsNsfw,
    int FollowerCount,
    int FollowingCount,
    int YapCount,
    int UnreadNotifications,
    Guid? PinnedYapId,
    DateTimeOffset JoinedAtUtc,
    YapperNotifyPrefsDto NotifyPrefs,
    DateTimeOffset? LastRenamedAtUtc,
    bool IsBanned,
    bool NsfwEnabled = false,
    bool AllowDms = true,
    bool BlurNsfw = false);

/// <summary>Another user's profile as the viewer sees it. <see cref="Handicapped"/> marks an
/// NSFW-matrix mismatch: the client renders the blurred variant with the warning popup.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperProfileViewDto(
    Guid ProfileId,
    string Handle,
    string DisplayName,
    string? Bio,
    byte[]? Avatar,
    byte[]? Banner,
    bool IsNsfw,
    bool IsSupporter,
    int FollowerCount,
    int FollowingCount,
    int YapCount,
    DateTimeOffset JoinedAtUtc,
    Guid? PinnedYapId,
    bool FollowedByMe,
    bool FollowsMe,
    bool BlockedByMe,
    bool MutedByMe,
    bool NotifyPostsByMe,
    bool HideRepostsByMe,
    bool Handicapped);

/// <summary>One user row in follower/following/blocked/muted lists and people search.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperUserRowDto(
    Guid ProfileId,
    string Handle,
    string DisplayName,
    byte[]? Avatar,
    bool IsNsfw,
    bool IsSupporter,
    bool FollowedByMe,
    bool FollowsMe);

/// <summary>A keyset page of user rows; <see cref="NextCursor"/> is null on the last page.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperUserPageDto(
    YapperUserRowDto[] Rows,
    DateTimeOffset? NextCursor);

/// <summary>The author header on a yap card.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapAuthorDto(
    Guid ProfileId,
    string Handle,
    string DisplayName,
    byte[]? Avatar,
    bool IsNsfw,
    bool IsSupporter);

/// <summary>Metadata for one attached image; the client fetches the bytes lazily per visible card.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapMediaMetaDto(
    Guid ImageId,
    int Width,
    int Height);

/// <summary>A hydrated share-into embed card. <see cref="Unavailable"/> renders the fallback card when
/// the target no longer exists or is no longer listed.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapEmbedDto(
    YapEmbedKind Kind,
    Guid Id,
    string Title,
    byte[]? Thumb,
    bool Unavailable);

/// <summary>One yap as the viewer sees it. Tombstones (<see cref="Deleted"/>) carry no author/text, and
/// <see cref="RemovedByModeration"/> separates a moderator takedown from an author deletion;
/// <see cref="Handicapped"/> marks the NSFW-matrix blur variant on the sanctioned surfaces.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapDto(
    Guid Id,
    YapAuthorDto? Author,
    YapKind Kind,
    string? Text,
    Guid? ParentYapId,
    YapDto? RepostOf,
    YapEmbedDto? Embed,
    YapVisibility Visibility,
    bool IsNsfw,
    bool HasContentWarning,
    YapMediaMetaDto[] Media,
    int LikeCount,
    int ReplyCount,
    int RepostCount,
    int ViewCount,
    int BookmarkCount,
    bool LikedByMe,
    bool BookmarkedByMe,
    bool RepostedByMe,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? EditedAtUtc,
    bool Deleted,
    bool Handicapped,
    bool RemovedByModeration = false,
    YapDto? InReplyTo = null,
    bool BlockedAuthor = false);

/// <summary>The compose payload. Images ride inline; the whole post is rejected if any image fails
/// moderation. A plain repost carries no text; a quote is a repost with text.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapCreateDto(
    YapKind Kind,
    string? Text,
    Guid? ParentYapId,
    Guid? RepostOfYapId,
    YapVisibility Visibility,
    bool HasContentWarning,
    PhotoUploadDto[]? Images,
    YapEmbedKind EmbedKind = YapEmbedKind.None,
    Guid? EmbedId = null);

/// <summary>A keyset page of yaps; <see cref="NextCursor"/> is null on the last page.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapPageDto(
    YapDto[] Yaps,
    DateTimeOffset? NextCursor);

/// <summary>One trending hashtag.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperTrendingTagDto(
    string Tag,
    int YapCount);

/// <summary>One coalesced inbox notification; <see cref="Actor"/> is the latest of
/// <see cref="ActorCount"/> actors.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperNotificationDto(
    Guid Id,
    YapperNotificationKind Kind,
    Guid? YapId,
    YapAuthorDto? Actor,
    int ActorCount,
    string? Snippet,
    DateTimeOffset UpdatedAtUtc,
    bool Read);

/// <summary>The live push for a new/coalesced notification, with the recipient's fresh unread total.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperNotificationPushDto(
    YapperNotificationDto Notification,
    int Unread);

/// <summary>A page of inbox notifications plus the unread total.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperNotificationPageDto(
    YapperNotificationDto[] Notifications,
    DateTimeOffset? NextCursor,
    int Unread);

/// <summary>A yapper profile's E2E key bundle: the X25519 public key plus the private key wrapped
/// under the account passphrase KEK. The server stores ciphertext only.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperKeyBundleDto(
    byte[] PublicKey,
    byte[] EncryptedPrivateKey,
    byte[] WrapNonce);

/// <summary>One DM conversation row: the peer's identity and active public key, my unread count and
/// the latest message (ciphertext; the client decrypts the preview).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperDmConversationDto(
    YapAuthorDto Peer,
    byte[]? PeerPublicKey,
    DateTimeOffset LastMessageAtUtc,
    int Unread,
    YapperDmMessageDto? LastMessage);

/// <summary>One profile's reaction tokens on a DM.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperDmReactionsDto(Guid ProfileId, string[] Tokens);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperDmMessageDto(
    Guid Id,
    Guid SenderProfileId,
    byte[] Ciphertext,
    byte[] Nonce,
    DateTimeOffset SentAtUtc,
    DateTimeOffset? ReadByPeerAtUtc = null,
    Guid? ReplyToMessageId = null,
    YapperDmReactionsDto[]? Reactions = null,
    DateTimeOffset? PinnedAtUtc = null,
    DateTimeOffset? DeletedAtUtc = null);

/// <summary>A keyset page of one thread's messages, newest first.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperDmPageDto(
    YapperDmMessageDto[] Messages,
    DateTimeOffset? NextCursor);

/// <summary>The live push for an incoming DM.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperDmPushDto(
    YapAuthorDto Sender,
    YapperDmMessageDto Message);

/// <summary>A freshly-opened DM thread: peer identity, their active public key (null while their
/// device hasn't provisioned yet) and the newest message page.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperDmThreadDto(
    YapAuthorDto Peer,
    byte[]? PeerPublicKey,
    YapperDmPageDto Page);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperDmReadPushDto(
    Guid PeerProfileId,
    Guid[] MessageIds,
    DateTimeOffset ReadAtUtc);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperDmReactionPushDto(
    Guid MessageId,
    Guid ProfileId,
    string Token,
    bool Added);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperDmPinPushDto(
    Guid MessageId,
    DateTimeOffset? PinnedAtUtc);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record YapperDmDeletedPushDto(
    Guid MessageId);
