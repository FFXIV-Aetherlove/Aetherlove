using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Yapper;

namespace AetherOS.Apps.Yapper;

/// <summary>Plugin-side services the Yapper app needs; implemented in the plugin (dependency inversion,
/// so the app never references it).</summary>
public interface IYapperHost
{
    Task<YapperMyProfileDto?> GetMyProfileAsync(CancellationToken ct = default);

    /// <summary>The party behind an invite card in a DM, or null once it has ended.</summary>
    Task<AetherLove.Shared.Together.TogetherPartyCardDto?> GetPartyCardAsync(Guid partyId, CancellationToken ct = default);

    /// <summary>This account's own messenger friend code, or null when it has none yet. Read for the DM
    /// composer's "invite to Messenger" entry, which sends the code as a card.</summary>
    string? MessengerCode { get; }

    /// <summary>Sends a messenger contact request for a code tapped on an invite card. Throws when the code
    /// is unknown, already paired or already pending; the card treats every one of those as "nothing to
    /// do here" and falls back to opening the Messenger app.</summary>
    Task AddMessengerContactAsync(string code, CancellationToken ct = default);

    /// <summary>Sends a picture in a DM, with an optional encrypted caption. The bytes are moderated and are
    /// NOT end-to-end encrypted, unlike the message they ride with.</summary>
    Task<YapperDmMessageDto> SendDmImageAsync(SendYapperDmImageRequest req, CancellationToken ct = default);

    /// <summary>A DM picture's bytes, or null once it has expired or been removed.</summary>
    Task<byte[]?> GetDmImageAsync(Guid imageId, CancellationToken ct = default);

    /// <summary>The sender takes their own picture back; both sides see the placeholder.</summary>
    Task DeleteDmImageAsync(Guid imageId, CancellationToken ct = default);

    /// <summary>Reports a DM picture into the shared moderation queue.</summary>
    Task ReportDmImageAsync(Guid imageId, string reason, CancellationToken ct = default);

    /// <summary>Usage against the account's shared image budget, for the compose panel.</summary>
    Task<AetherLove.Shared.Messenger.MessengerStorageDto> GetDmImageStorageAsync(CancellationToken ct = default);

    /// <summary>A DM picture left: sender delete or moderator removal.</summary>
    event Action<Guid>? DmImageRemoved;

    Task<YapperHandleCheck> CheckHandleAsync(string handle, CancellationToken ct = default);

    Task<YapperMyProfileDto> CreateProfileAsync(
        string handle, string displayName, string? bio, bool isNsfw, bool nsfwEnabled, CancellationToken ct = default);

    Task<YapperMyProfileDto> UpdateProfileAsync(string displayName, string? bio, CancellationToken ct = default);

    Task<YapperMyProfileDto> RenameHandleAsync(string handle, CancellationToken ct = default);

    Task SetAvatarAsync(AetherLove.Shared.Profile.PhotoUploadDto image, CancellationToken ct = default);

    Task SetBannerAsync(AetherLove.Shared.Profile.PhotoUploadDto image, CancellationToken ct = default);

    Task SetRatingAsync(bool isNsfw, bool nsfwEnabled, CancellationToken ct = default);

    Task SetBlurNsfwAsync(bool blur, CancellationToken ct = default);

    /// <summary>The avatar rings the account owns, for the picker.</summary>
    Task<AetherLove.Shared.Store.AvatarRingDto[]> GetOwnedRingsAsync(CancellationToken ct = default);

    /// <summary>Equips (or clears, on null) the Yapper avatar ring.</summary>
    Task SetAvatarRingAsync(string? frameRef, CancellationToken ct = default);

    /// <summary>Pins one of the caller's own posts to their profile; null unpins.</summary>
    Task SetPinAsync(Guid? yapId, CancellationToken ct = default);

    Task SetNotifyPrefsAsync(YapperNotifyPrefsDto prefs, CancellationToken ct = default);

    Task<YapperProfileViewDto> GetProfileAsync(Guid profileId, CancellationToken ct = default);

    Task FollowAsync(Guid profileId, CancellationToken ct = default);

    Task UnfollowAsync(Guid profileId, CancellationToken ct = default);

    Task BlockAsync(Guid profileId, CancellationToken ct = default);

    Task UnblockAsync(Guid profileId, CancellationToken ct = default);

    Task SetMuteAsync(Guid profileId, bool muted, CancellationToken ct = default);

    Task SetFollowFlagsAsync(Guid profileId, bool? notifyPosts, bool? hideReposts, CancellationToken ct = default);

    Task<YapperUserPageDto> GetFollowersAsync(Guid profileId, DateTimeOffset? cursor, CancellationToken ct = default);

    Task<YapperUserPageDto> GetFollowingAsync(Guid profileId, DateTimeOffset? cursor, CancellationToken ct = default);

    /// <summary>Deletes this account's Yapper profile for good. The next open finds no profile and runs
    /// onboarding, so the account can start again with a new handle.</summary>
    Task DeleteProfileAsync(CancellationToken ct = default);

    Task<YapperUserRowDto[]> GetBlockedAsync(CancellationToken ct = default);

    Task<YapperUserRowDto[]> GetMutedAsync(CancellationToken ct = default);

    Task<YapDto> CreateYapAsync(YapCreateDto req, CancellationToken ct = default);

    Task<YapDto> EditYapAsync(Guid yapId, string text, CancellationToken ct = default);

    Task DeleteYapAsync(Guid yapId, CancellationToken ct = default);

    Task UndoYapRepostAsync(Guid targetYapId, CancellationToken ct = default);

    Task SetYapLikeAsync(Guid yapId, bool liked, CancellationToken ct = default);

    Task SetYapBookmarkAsync(Guid yapId, bool bookmarked, CancellationToken ct = default);

    Task ReportYapAsync(Guid? yapId, Guid? profileId, string reason, CancellationToken ct = default);

    Task<YapDto> GetYapAsync(Guid yapId, CancellationToken ct = default);

    Task<byte[]?> GetYapImageAsync(Guid imageId, CancellationToken ct = default);

    Task<YapPageDto> GetYapRepliesAsync(Guid yapId, DateTimeOffset? cursor, CancellationToken ct = default);

    Task<YapPageDto> GetFollowingFeedAsync(DateTimeOffset? cursor, CancellationToken ct = default);

    Task<YapPageDto> GetProfileYapsAsync(Guid profileId, YapperProfileTab tab, DateTimeOffset? cursor, CancellationToken ct = default);

    Task<YapPageDto> GetTagFeedAsync(string tag, DateTimeOffset? cursor, CancellationToken ct = default);

    Task<YapPageDto> GetBookmarksAsync(DateTimeOffset? cursor, CancellationToken ct = default);

    Task ReportViewsAsync(Guid[] yapIds, CancellationToken ct = default);

    Task<YapDto[]> GetForYouFeedAsync(Guid[] seenIds, CancellationToken ct = default);

    Task<YapperTrendingTagDto[]> GetTrendingAsync(CancellationToken ct = default);

    Task<YapperUserRowDto[]> GetSuggestedUsersAsync(CancellationToken ct = default);

    Task<YapperNotificationPageDto> GetNotificationsAsync(DateTimeOffset? cursor, CancellationToken ct = default);

    Task MarkNotificationsReadAsync(CancellationToken ct = default);

    Task<AetherLove.Shared.Places.VenueCardDto> GetVenueCardAsync(Guid venueId, CancellationToken ct = default);

    Task<AetherLove.Shared.Levemetes.LevemeteCardDto?> GetLevemeteCardAsync(Guid adId, CancellationToken ct = default);

    /// <summary>Fires on a live notification push, on a background thread.</summary>
    event Action<YapperNotificationPushDto>? NotificationReceived;

    Task<YapperUserRowDto[]> SearchUsersAsync(string query, CancellationToken ct = default);

    Task<YapPageDto> SearchYapsAsync(string query, DateTimeOffset? cursor, CancellationToken ct = default);

    bool IsSupporter { get; }

    /// <summary>The signed-in account's OS display name, for onboarding prefill.</summary>
    string? OsDisplayName { get; }

    /// <summary>Fires on an incoming DM push, on a background thread.</summary>
    event Action<YapperDmPushDto>? DmReceived;

    event Action<YapperDmReadPushDto>? DmRead;

    event Action<YapperDmReactionPushDto>? DmReaction;

    event Action<YapperDmPinPushDto>? DmPinned;

    event Action<YapperDmDeletedPushDto>? DmDeleted;

    /// <summary>Silently provisions (or unwraps) my DM keypair; false when the account KEK is absent.</summary>
    Task<bool> EnsureDmKeysAsync(CancellationToken ct = default);

    bool HasDmKeys { get; }

    (byte[] Ciphertext, byte[] Nonce)? EncryptDm(byte[] peerPublicKey, string plaintext);

    string? DecryptDm(byte[] peerPublicKey, byte[] ciphertext, byte[] nonce);

    Task<YapperDmConversationDto[]> GetDmConversationsAsync(CancellationToken ct = default);

    Task<YapperDmThreadDto> OpenDmThreadAsync(Guid peerProfileId, CancellationToken ct = default);

    Task<YapperDmPageDto> GetDmThreadAsync(Guid peerProfileId, DateTimeOffset? cursor, CancellationToken ct = default);

    Task<YapperDmMessageDto> SendDmAsync(Guid peerProfileId, byte[] ciphertext, byte[] nonce, Guid? replyToMessageId, CancellationToken ct = default);

    Task MarkDmReadAsync(Guid peerProfileId, CancellationToken ct = default);

    Task ReactDmAsync(Guid messageId, string token, bool add, CancellationToken ct = default);

    Task SetDmPinnedAsync(Guid messageId, bool pinned, CancellationToken ct = default);

    Task DeleteDmAsync(Guid messageId, CancellationToken ct = default);

    Task SetAllowDmsAsync(bool allow, CancellationToken ct = default);
}
