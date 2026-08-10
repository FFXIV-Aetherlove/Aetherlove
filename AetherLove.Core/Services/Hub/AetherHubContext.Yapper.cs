using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Yapper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

/// <summary>Yapper profile + social-graph passthroughs.</summary>
public sealed partial class AetherHubContext
{
    public async Task<YapperMyProfileDto?> GetMyYapperProfileAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperMyProfileDto?>("GetMyYapperProfileAsync", ct).ConfigureAwait(false);

    public async Task<YapperHandleCheck> CheckYapperHandleAsync(string handle, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperHandleCheck>("CheckYapperHandleAsync", handle, ct).ConfigureAwait(false);

    public async Task<YapperMyProfileDto> CreateYapperProfileAsync(
        string handle, string displayName, string? bio, bool isNsfw, bool nsfwEnabled, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<YapperMyProfileDto>(
                "CreateYapperProfileAsync", handle, displayName, bio, isNsfw, nsfwEnabled, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<YapperMyProfileDto> UpdateYapperProfileAsync(
        string displayName, string? bio, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<YapperMyProfileDto>(
                "UpdateYapperProfileAsync", displayName, bio, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<YapperMyProfileDto> RenameYapperHandleAsync(string handle, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<YapperMyProfileDto>(
                "RenameYapperHandleAsync", handle, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task DeleteYapperProfileAsync(CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("DeleteYapperProfileAsync", ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task SetYapperRatingAsync(bool isNsfw, bool nsfwEnabled, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetYapperRatingAsync", isNsfw, nsfwEnabled, ct).ConfigureAwait(false);

    public async Task SetYapperBlurNsfwAsync(bool blur, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetYapperBlurNsfwAsync", blur, ct).ConfigureAwait(false);

    public async Task SetYapperNotifyPrefsAsync(YapperNotifyPrefsDto prefs, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetYapperNotifyPrefsAsync", prefs, ct).ConfigureAwait(false);

    public async Task SetYapperPinAsync(Guid? yapId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetYapperPinAsync", yapId, ct).ConfigureAwait(false);

    public async Task SetYapperAvatarAsync(PhotoUploadDto image, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SetYapperAvatarAsync", image, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task SetYapperBannerAsync(PhotoUploadDto image, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SetYapperBannerAsync", image, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<YapperProfileViewDto> GetYapperProfileAsync(Guid profileId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperProfileViewDto>("GetYapperProfileAsync", profileId, ct).ConfigureAwait(false);

    public async Task<YapperProfileViewDto> GetYapperProfileByHandleAsync(string handle, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperProfileViewDto>("GetYapperProfileByHandleAsync", handle, ct).ConfigureAwait(false);

    public async Task FollowYapperAsync(Guid profileId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("FollowYapperAsync", profileId, ct).ConfigureAwait(false);

    public async Task UnfollowYapperAsync(Guid profileId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("UnfollowYapperAsync", profileId, ct).ConfigureAwait(false);

    public async Task BlockYapperAsync(Guid profileId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("BlockYapperAsync", profileId, ct).ConfigureAwait(false);

    public async Task UnblockYapperAsync(Guid profileId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("UnblockYapperAsync", profileId, ct).ConfigureAwait(false);

    public async Task SetYapperMuteAsync(Guid profileId, bool muted, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetYapperMuteAsync", profileId, muted, ct).ConfigureAwait(false);

    public async Task SetYapperFollowFlagsAsync(
        Guid profileId, bool? notifyPosts, bool? hideReposts, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetYapperFollowFlagsAsync", profileId, notifyPosts, hideReposts, ct).ConfigureAwait(false);

    public async Task<YapperUserPageDto> GetYapperFollowersAsync(
        Guid profileId, DateTimeOffset? cursor, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperUserPageDto>("GetYapperFollowersAsync", profileId, cursor, ct).ConfigureAwait(false);

    public async Task<YapperUserPageDto> GetYapperFollowingAsync(
        Guid profileId, DateTimeOffset? cursor, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperUserPageDto>("GetYapperFollowingAsync", profileId, cursor, ct).ConfigureAwait(false);

    public async Task<YapperUserRowDto[]> GetYapperBlockedAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperUserRowDto[]>("GetYapperBlockedAsync", ct).ConfigureAwait(false);

    public async Task<YapperUserRowDto[]> GetYapperMutedAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperUserRowDto[]>("GetYapperMutedAsync", ct).ConfigureAwait(false);

    public async Task<YapDto> CreateYapAsync(YapCreateDto req, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<YapDto>("CreateYapAsync", req, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<YapDto> EditYapAsync(Guid yapId, string text, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<YapDto>("EditYapAsync", yapId, text, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task DeleteYapAsync(Guid yapId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("DeleteYapAsync", yapId, ct).ConfigureAwait(false);

    public async Task UndoYapRepostAsync(Guid targetYapId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("UndoYapRepostAsync", targetYapId, ct).ConfigureAwait(false);

    public async Task SetYapLikeAsync(Guid yapId, bool liked, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetYapLikeAsync", yapId, liked, ct).ConfigureAwait(false);

    public async Task SetYapBookmarkAsync(Guid yapId, bool bookmarked, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetYapBookmarkAsync", yapId, bookmarked, ct).ConfigureAwait(false);

    public async Task ReportYapAsync(Guid? yapId, Guid? profileId, string reason, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("ReportYapAsync", yapId, profileId, reason, ct).ConfigureAwait(false);

    public async Task<YapDto> GetYapAsync(Guid yapId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapDto>("GetYapAsync", yapId, ct).ConfigureAwait(false);

    public async Task<byte[]?> GetYapImageAsync(Guid imageId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<byte[]?>("GetYapImageAsync", imageId, ct).ConfigureAwait(false);

    public async Task<YapPageDto> GetYapRepliesAsync(Guid yapId, DateTimeOffset? cursor, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapPageDto>("GetYapRepliesAsync", yapId, cursor, ct).ConfigureAwait(false);

    public async Task<YapPageDto> GetYapperFollowingFeedAsync(DateTimeOffset? cursor, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapPageDto>("GetYapperFollowingFeedAsync", cursor, ct).ConfigureAwait(false);

    public async Task<YapPageDto> GetYapperProfileYapsAsync(
        Guid profileId, YapperProfileTab tab, DateTimeOffset? cursor, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapPageDto>("GetYapperProfileYapsAsync", profileId, tab, cursor, ct).ConfigureAwait(false);

    public async Task<YapPageDto> GetYapperTagFeedAsync(string tag, DateTimeOffset? cursor, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapPageDto>("GetYapperTagFeedAsync", tag, cursor, ct).ConfigureAwait(false);

    public async Task<YapPageDto> GetYapperBookmarksAsync(DateTimeOffset? cursor, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapPageDto>("GetYapperBookmarksAsync", cursor, ct).ConfigureAwait(false);

    public async Task ReportYapViewsAsync(Guid[] yapIds, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("ReportYapViewsAsync", yapIds, ct).ConfigureAwait(false);

    public async Task<YapDto[]> GetYapperForYouFeedAsync(Guid[] seenIds, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapDto[]>("GetYapperForYouFeedAsync", seenIds, ct).ConfigureAwait(false);

    public async Task<YapperTrendingTagDto[]> GetYapperTrendingAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperTrendingTagDto[]>("GetYapperTrendingAsync", ct).ConfigureAwait(false);

    public async Task<YapperUserRowDto[]> GetYapperSuggestedUsersAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperUserRowDto[]>("GetYapperSuggestedUsersAsync", ct).ConfigureAwait(false);

    public async Task<YapperNotificationPageDto> GetYapperNotificationsAsync(DateTimeOffset? cursor, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperNotificationPageDto>("GetYapperNotificationsAsync", cursor, ct).ConfigureAwait(false);

    public async Task MarkYapperNotificationsReadAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("MarkYapperNotificationsReadAsync", ct).ConfigureAwait(false);

    public async Task<YapperUserRowDto[]> SearchYapperUsersAsync(string query, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperUserRowDto[]>("SearchYapperUsersAsync", query, ct).ConfigureAwait(false);

    public async Task<YapPageDto> SearchYapsAsync(string query, DateTimeOffset? cursor, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapPageDto>("SearchYapsAsync", query, cursor, ct).ConfigureAwait(false);

    public async Task<YapperKeyBundleDto?> GetYapperDmKeysAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperKeyBundleDto?>("GetYapperDmKeysAsync", ct).ConfigureAwait(false);

    public async Task PublishYapperDmKeysAsync(YapperKeyBundleDto bundle, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("PublishYapperDmKeysAsync", bundle, ct).ConfigureAwait(false);

    public async Task SetYapperAllowDmsAsync(bool allow, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetYapperAllowDmsAsync", allow, ct).ConfigureAwait(false);

    public async Task<YapperDmConversationDto[]> GetYapperDmConversationsAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperDmConversationDto[]>("GetYapperDmConversationsAsync", ct).ConfigureAwait(false);

    public async Task<YapperDmThreadDto> OpenYapperDmThreadAsync(Guid peerProfileId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperDmThreadDto>("OpenYapperDmThreadAsync", peerProfileId, ct).ConfigureAwait(false);

    public async Task<YapperDmPageDto> GetYapperDmThreadAsync(Guid peerProfileId, DateTimeOffset? cursor, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperDmPageDto>("GetYapperDmThreadAsync", peerProfileId, cursor, ct).ConfigureAwait(false);

    public async Task<YapperDmMessageDto> SendYapperDmAsync(Guid peerProfileId, byte[] ciphertext, byte[] nonce, Guid? replyToMessageId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<YapperDmMessageDto>("SendYapperDmAsync", peerProfileId, ciphertext, nonce, replyToMessageId, ct).ConfigureAwait(false);

    public async Task MarkYapperDmReadAsync(Guid peerProfileId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("MarkYapperDmReadAsync", peerProfileId, ct).ConfigureAwait(false);

    public async Task ReactYapperDmAsync(Guid messageId, string token, bool add, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("ReactYapperDmAsync", messageId, token, add, ct).ConfigureAwait(false);

    public async Task SetYapperDmPinnedAsync(Guid messageId, bool pinned, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetYapperDmPinnedAsync", messageId, pinned, ct).ConfigureAwait(false);

    public async Task DeleteYapperDmAsync(Guid messageId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("DeleteYapperDmAsync", messageId, ct).ConfigureAwait(false);
}
