using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Shared.Yapper;
using AetherOS.Apps.Yapper;

namespace AetherLove.Os;

/// <summary>Plugin-side implementation of the Yapper app's host bridge: hub passthroughs plus the
/// session snapshots the app may not read itself.</summary>
public sealed class YapperHostService : IYapperHost
{
    private readonly AetherHubContext _hubClient;
    private readonly SessionBootstrapper _bootstrap;
    private readonly Services.Yapper.YapperDmCryptoService _dmCrypto;

    public YapperHostService(AetherHubContext hubClient, SessionBootstrapper bootstrap,
        Services.Yapper.YapperNotificationRelay relay, Services.Yapper.YapperDmCryptoService dmCrypto)
    {
        _hubClient = hubClient;
        _bootstrap = bootstrap;
        _dmCrypto = dmCrypto;
        relay.NotificationReceived += payload => NotificationReceived?.Invoke(payload);
        relay.DmReceived += payload => DmReceived?.Invoke(payload);
        relay.DmRead += payload => DmRead?.Invoke(payload);
        relay.DmReaction += payload => DmReaction?.Invoke(payload);
        relay.DmPinned += payload => DmPinned?.Invoke(payload);
        relay.DmDeleted += payload => DmDeleted?.Invoke(payload);
    }

    public event Action<YapperNotificationPushDto>? NotificationReceived;

    public event Action<YapperDmPushDto>? DmReceived;

    public event Action<YapperDmReadPushDto>? DmRead;

    public event Action<YapperDmReactionPushDto>? DmReaction;

    public event Action<YapperDmPinPushDto>? DmPinned;

    public event Action<YapperDmDeletedPushDto>? DmDeleted;

    public Task<bool> EnsureDmKeysAsync(CancellationToken ct = default)
        => _dmCrypto.EnsureProvisionedAsync(ct);

    public bool HasDmKeys => _dmCrypto.HasKeys;

    public (byte[] Ciphertext, byte[] Nonce)? EncryptDm(byte[] peerPublicKey, string plaintext)
        => _dmCrypto.Encrypt(peerPublicKey, plaintext);

    public string? DecryptDm(byte[] peerPublicKey, byte[] ciphertext, byte[] nonce)
        => _dmCrypto.Decrypt(peerPublicKey, ciphertext, nonce);

    public Task<YapperDmConversationDto[]> GetDmConversationsAsync(CancellationToken ct = default)
        => _hubClient.GetYapperDmConversationsAsync(ct);

    public Task<YapperDmThreadDto> OpenDmThreadAsync(Guid peerProfileId, CancellationToken ct = default)
        => _hubClient.OpenYapperDmThreadAsync(peerProfileId, ct);

    public Task<YapperDmPageDto> GetDmThreadAsync(Guid peerProfileId, DateTimeOffset? cursor, CancellationToken ct = default)
        => _hubClient.GetYapperDmThreadAsync(peerProfileId, cursor, ct);

    public Task<YapperDmMessageDto> SendDmAsync(Guid peerProfileId, byte[] ciphertext, byte[] nonce, Guid? replyToMessageId, CancellationToken ct = default)
        => _hubClient.SendYapperDmAsync(peerProfileId, ciphertext, nonce, replyToMessageId, ct);

    public Task MarkDmReadAsync(Guid peerProfileId, CancellationToken ct = default)
        => _hubClient.MarkYapperDmReadAsync(peerProfileId, ct);

    public Task ReactDmAsync(Guid messageId, string token, bool add, CancellationToken ct = default)
        => _hubClient.ReactYapperDmAsync(messageId, token, add, ct);

    public Task SetDmPinnedAsync(Guid messageId, bool pinned, CancellationToken ct = default)
        => _hubClient.SetYapperDmPinnedAsync(messageId, pinned, ct);

    public Task DeleteDmAsync(Guid messageId, CancellationToken ct = default)
        => _hubClient.DeleteYapperDmAsync(messageId, ct);

    public Task SetAllowDmsAsync(bool allow, CancellationToken ct = default)
        => _hubClient.SetYapperAllowDmsAsync(allow, ct);

    public Task<YapperNotificationPageDto> GetNotificationsAsync(DateTimeOffset? cursor, CancellationToken ct = default)
        => _hubClient.GetYapperNotificationsAsync(cursor, ct);

    public Task MarkNotificationsReadAsync(CancellationToken ct = default)
        => _hubClient.MarkYapperNotificationsReadAsync(ct);

    public Task<AetherLove.Shared.Places.VenueCardDto> GetVenueCardAsync(Guid venueId, CancellationToken ct = default)
        => _hubClient.GetVenueCardAsync(venueId, ct);

    public Task<AetherLove.Shared.Levemetes.LevemeteCardDto?> GetLevemeteCardAsync(Guid adId, CancellationToken ct = default)
        => _hubClient.GetLevemeteCardAsync(adId, ct);

    public Task<YapperUserRowDto[]> SearchUsersAsync(string query, CancellationToken ct = default)
        => _hubClient.SearchYapperUsersAsync(query, ct);

    public Task<YapPageDto> SearchYapsAsync(string query, DateTimeOffset? cursor, CancellationToken ct = default)
        => _hubClient.SearchYapsAsync(query, cursor, ct);

    public Task<YapperMyProfileDto?> GetMyProfileAsync(CancellationToken ct = default)
        => _hubClient.GetMyYapperProfileAsync(ct);

    public Task<YapperHandleCheck> CheckHandleAsync(string handle, CancellationToken ct = default)
        => _hubClient.CheckYapperHandleAsync(handle, ct);

    public Task<YapperMyProfileDto> CreateProfileAsync(
        string handle, string displayName, string? bio, bool isNsfw, bool nsfwEnabled, CancellationToken ct = default)
        => _hubClient.CreateYapperProfileAsync(handle, displayName, bio, isNsfw, nsfwEnabled, ct);

    public Task<YapperMyProfileDto> UpdateProfileAsync(string displayName, string? bio, CancellationToken ct = default)
        => _hubClient.UpdateYapperProfileAsync(displayName, bio, ct);

    public Task<YapperMyProfileDto> RenameHandleAsync(string handle, CancellationToken ct = default)
        => _hubClient.RenameYapperHandleAsync(handle, ct);

    public Task SetAvatarAsync(AetherLove.Shared.Profile.PhotoUploadDto image, CancellationToken ct = default)
        => _hubClient.SetYapperAvatarAsync(image, ct);

    public Task SetBannerAsync(AetherLove.Shared.Profile.PhotoUploadDto image, CancellationToken ct = default)
        => _hubClient.SetYapperBannerAsync(image, ct);

    public Task SetRatingAsync(bool isNsfw, bool nsfwEnabled, CancellationToken ct = default)
        => _hubClient.SetYapperRatingAsync(isNsfw, nsfwEnabled, ct);

    public Task SetBlurNsfwAsync(bool blur, CancellationToken ct = default)
        => _hubClient.SetYapperBlurNsfwAsync(blur, ct);

    public Task SetPinAsync(Guid? yapId, CancellationToken ct = default)
        => _hubClient.SetYapperPinAsync(yapId, ct);

    public Task SetNotifyPrefsAsync(YapperNotifyPrefsDto prefs, CancellationToken ct = default)
        => _hubClient.SetYapperNotifyPrefsAsync(prefs, ct);

    public Task<YapperProfileViewDto> GetProfileAsync(Guid profileId, CancellationToken ct = default)
        => _hubClient.GetYapperProfileAsync(profileId, ct);

    public Task FollowAsync(Guid profileId, CancellationToken ct = default)
        => _hubClient.FollowYapperAsync(profileId, ct);

    public Task UnfollowAsync(Guid profileId, CancellationToken ct = default)
        => _hubClient.UnfollowYapperAsync(profileId, ct);

    public Task BlockAsync(Guid profileId, CancellationToken ct = default)
        => _hubClient.BlockYapperAsync(profileId, ct);

    public Task UnblockAsync(Guid profileId, CancellationToken ct = default)
        => _hubClient.UnblockYapperAsync(profileId, ct);

    public Task SetMuteAsync(Guid profileId, bool muted, CancellationToken ct = default)
        => _hubClient.SetYapperMuteAsync(profileId, muted, ct);

    public Task SetFollowFlagsAsync(Guid profileId, bool? notifyPosts, bool? hideReposts, CancellationToken ct = default)
        => _hubClient.SetYapperFollowFlagsAsync(profileId, notifyPosts, hideReposts, ct);

    public Task<YapperUserPageDto> GetFollowersAsync(Guid profileId, DateTimeOffset? cursor, CancellationToken ct = default)
        => _hubClient.GetYapperFollowersAsync(profileId, cursor, ct);

    public Task<YapperUserPageDto> GetFollowingAsync(Guid profileId, DateTimeOffset? cursor, CancellationToken ct = default)
        => _hubClient.GetYapperFollowingAsync(profileId, cursor, ct);

    public Task<YapperUserRowDto[]> GetBlockedAsync(CancellationToken ct = default)
        => _hubClient.GetYapperBlockedAsync(ct);

    public Task<YapperUserRowDto[]> GetMutedAsync(CancellationToken ct = default)
        => _hubClient.GetYapperMutedAsync(ct);

    public Task<YapDto> CreateYapAsync(YapCreateDto req, CancellationToken ct = default)
        => _hubClient.CreateYapAsync(req, ct);

    public Task<YapDto> EditYapAsync(Guid yapId, string text, CancellationToken ct = default)
        => _hubClient.EditYapAsync(yapId, text, ct);

    public Task DeleteYapAsync(Guid yapId, CancellationToken ct = default)
        => _hubClient.DeleteYapAsync(yapId, ct);

    public Task UndoYapRepostAsync(Guid targetYapId, CancellationToken ct = default)
        => _hubClient.UndoYapRepostAsync(targetYapId, ct);

    public Task SetYapLikeAsync(Guid yapId, bool liked, CancellationToken ct = default)
        => _hubClient.SetYapLikeAsync(yapId, liked, ct);

    public Task SetYapBookmarkAsync(Guid yapId, bool bookmarked, CancellationToken ct = default)
        => _hubClient.SetYapBookmarkAsync(yapId, bookmarked, ct);

    public Task ReportYapAsync(Guid? yapId, Guid? profileId, string reason, CancellationToken ct = default)
        => _hubClient.ReportYapAsync(yapId, profileId, reason, ct);

    public Task<YapDto> GetYapAsync(Guid yapId, CancellationToken ct = default)
        => _hubClient.GetYapAsync(yapId, ct);

    public Task<byte[]?> GetYapImageAsync(Guid imageId, CancellationToken ct = default)
        => _hubClient.GetYapImageAsync(imageId, ct);

    public Task<YapPageDto> GetYapRepliesAsync(Guid yapId, DateTimeOffset? cursor, CancellationToken ct = default)
        => _hubClient.GetYapRepliesAsync(yapId, cursor, ct);

    public Task<YapPageDto> GetFollowingFeedAsync(DateTimeOffset? cursor, CancellationToken ct = default)
        => _hubClient.GetYapperFollowingFeedAsync(cursor, ct);

    public Task<YapPageDto> GetProfileYapsAsync(Guid profileId, YapperProfileTab tab, DateTimeOffset? cursor, CancellationToken ct = default)
        => _hubClient.GetYapperProfileYapsAsync(profileId, tab, cursor, ct);

    public Task<YapPageDto> GetTagFeedAsync(string tag, DateTimeOffset? cursor, CancellationToken ct = default)
        => _hubClient.GetYapperTagFeedAsync(tag, cursor, ct);

    public Task<YapPageDto> GetBookmarksAsync(DateTimeOffset? cursor, CancellationToken ct = default)
        => _hubClient.GetYapperBookmarksAsync(cursor, ct);

    public Task ReportViewsAsync(Guid[] yapIds, CancellationToken ct = default)
        => _hubClient.ReportYapViewsAsync(yapIds, ct);

    public Task<YapDto[]> GetForYouFeedAsync(Guid[] seenIds, CancellationToken ct = default)
        => _hubClient.GetYapperForYouFeedAsync(seenIds, ct);

    public Task<YapperTrendingTagDto[]> GetTrendingAsync(CancellationToken ct = default)
        => _hubClient.GetYapperTrendingAsync(ct);

    public Task<YapperUserRowDto[]> GetSuggestedUsersAsync(CancellationToken ct = default)
        => _hubClient.GetYapperSuggestedUsersAsync(ct);

    public bool IsSupporter => _bootstrap.LastConnection?.IsSupporter == true;

    public string? OsDisplayName => _bootstrap.LastAccount?.OsDisplayName;
}
