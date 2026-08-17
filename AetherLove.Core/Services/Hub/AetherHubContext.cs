using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services.Signal;
using AetherLove.Shared.Auth;
using AetherLove.Shared.Diagnostics;
using AetherLove.Shared.Feedback;
using AetherLove.Shared.Flairs;
using AetherLove.Shared.Hangouts;
using AetherLove.Shared.Matching;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Moderation;
using AetherLove.Shared.News;
using AetherLove.Shared.Patreon;
using AetherLove.Shared.Places;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Profile.Enums;
using AetherLove.Shared.Pulse;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

/// <summary>Typed client wrapper for <c>AetherHub</c> methods.</summary>
public sealed partial class AetherHubContext
{
    private readonly AetherSignalService _signal;

    public AetherHubContext(AetherSignalService signal)
    {
        _signal = signal;
    }

    public bool IsConnected => _signal.IsConnected;

    private async Task<HubConnection> ConnAsync(CancellationToken ct)
    {
        await _signal.EnsureConnectedAsync(ct).ConfigureAwait(false);
        return _signal.RequireConnection();
    }

    public async Task<DebugInfoDto> GetDebugInfoAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<DebugInfoDto>("GetDebugInfoAsync", ct).ConfigureAwait(false);

    public async Task SaveBasicProfileAsync(BasicProfileDto dto, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SaveBasicProfileAsync", dto, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task SavePhotosAsync(PhotoBatchDto dto, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SavePhotosAsync", dto, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task SaveFiltersAsync(FiltersDto dto, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SaveFiltersAsync", dto, ct).ConfigureAwait(false);

    public async Task<MyCharactersDto> GetMyCharactersAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MyCharactersDto>("GetMyCharactersAsync", ct).ConfigureAwait(false);

    public async Task<MyCharactersDto> SaveCharactersAsync(SaveCharactersRequest req, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<MyCharactersDto>("SaveCharactersAsync", req, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task SetCharacterImageAsync(Guid characterId, PhotoUploadDto image, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SetCharacterImageAsync", characterId, image, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task RemoveCharacterImageAsync(Guid characterId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("RemoveCharacterImageAsync", characterId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task SetCharacterExtraImageAsync(Guid characterId, short sortOrder, PhotoUploadDto image, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SetCharacterExtraImageAsync", characterId, sortOrder, image, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task RemoveCharacterExtraImageAsync(Guid characterId, short sortOrder, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("RemoveCharacterExtraImageAsync", characterId, sortOrder, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    /// <summary>Live preview: server validates a pasted music link and returns the curated name (null if invalid).</summary>
    public async Task<MusicLinkDto?> ResolveMusicLinkAsync(MusicProvider provider, string rawInput, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MusicLinkDto?>("ResolveMusicLinkAsync", provider, rawInput, ct).ConfigureAwait(false);

    public async Task SetProfileNsfwAsync(bool enabled, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SetProfileNsfwAsync", enabled, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task SetSupporterOptionsAsync(NameStyle style, bool showBadge, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SetSupporterOptionsAsync", style, showBadge, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task SetHolidayModeAsync(bool enabled, string message, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SetHolidayModeAsync", enabled, message, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<AetherConnectionDto> GetConnectionInfoAsync(CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<AetherConnectionDto>("GetConnectionInfoAsync", ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (OutdatedClientException.TryParse(ex) is { } outdated) { throw outdated; }
        catch (HubException ex) when (ServerClosedException.TryParse(ex) is { } closed) { throw closed; }
    }

    /// <summary>Account-level snapshot (identity, role, supporter, OS-onboarded/passphrase state, profile count)
    /// for the AetherOS shell. Additive sibling of <see cref="GetConnectionInfoAsync"/>.</summary>
    public async Task<AetherAccountInfoDto> GetAccountInfoAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<AetherAccountInfoDto>("GetAccountInfoAsync", ct).ConfigureAwait(false);

    /// <summary>The account's AetherLove profiles, for the profile switcher.</summary>
    public async Task<ProfileListDto> ListProfilesAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<ProfileListDto>("ListProfilesAsync", ct).ConfigureAwait(false);

    /// <summary>Create a new AetherLove profile under the caller's account (starts in Onboarding).</summary>
    public async Task<ProfileSummaryDto> CreateProfileAsync(CreateProfileRequest req, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<ProfileSummaryDto>("CreateProfileAsync", req, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    /// <summary>Switch the caller's active profile; returns a fresh access token acting as it.</summary>
    public async Task<AccessTokenDto> SelectProfileAsync(Guid profileId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<AccessTokenDto>("SelectProfileAsync", profileId, ct).ConfigureAwait(false);

    /// <summary>Store the account passphrase KEK parameters (write-once, set during OS onboarding).</summary>
    public async Task SetAccountPassphraseAsync(AccountPassphraseDto dto, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetAccountPassphraseAsync", dto, ct).ConfigureAwait(false);

    /// <summary>The account passphrase KEK parameters, or null when no passphrase is set yet.</summary>
    public async Task<AccountPassphraseDto?> GetAccountPassphraseAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<AccountPassphraseDto?>("GetAccountPassphraseAsync", ct).ConfigureAwait(false);

    /// <summary>Lost-passphrase recovery: replaces the passphrase and every keypair with fresh ones.</summary>
    public async Task ResetAccountPassphraseAsync(ResetPassphraseRequest req, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("ResetAccountPassphraseAsync", req, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    /// <summary>Mark the account's OS onboarding complete and set its OS display name.</summary>
    public async Task CompleteOsOnboardingAsync(OsOnboardingCompleteDto dto, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("CompleteOsOnboardingAsync", dto, ct).ConfigureAwait(false);

    /// <summary>Upload/replace the account's OS avatar; returns the stored image for immediate display.</summary>
    public async Task<byte[]> SetOsAvatarAsync(PhotoUploadDto image, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<byte[]>("SetOsAvatarAsync", image, ct).ConfigureAwait(false);

    public async Task MarkWarningsSeenAsync(Guid[] warningIds, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("MarkWarningsSeenAsync", warningIds, ct).ConfigureAwait(false);

    public async Task MarkModeratorMessagesSeenAsync(Guid[] messageIds, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("MarkModeratorMessagesSeenAsync", messageIds, ct).ConfigureAwait(false);

    /// <summary>Acknowledge account-level staff warnings (the OS track). Resolved server-side through the account,
    /// so it works for a caller with no dating profile.</summary>
    public async Task MarkStaffWarningsSeenAsync(Guid[] warningIds, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("MarkStaffWarningsSeenAsync", warningIds, ct).ConfigureAwait(false);

    /// <summary>Acknowledge account-level staff messages (the OS track).</summary>
    public async Task MarkStaffMessagesSeenAsync(Guid[] messageIds, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("MarkStaffMessagesSeenAsync", messageIds, ct).ConfigureAwait(false);

    public async Task<NewsDto?> GetNewsAsync(Guid newsId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<NewsDto?>("GetNewsAsync", newsId, ct).ConfigureAwait(false);

    public async Task<NewsDto?> GetNewsPreviewAsync(Guid newsId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<NewsDto?>("GetNewsPreviewAsync", newsId, ct).ConfigureAwait(false);

    public async Task<NewsSummaryDto[]> GetNewsListAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<NewsSummaryDto[]>("GetNewsListAsync", ct).ConfigureAwait(false);

    public async Task<NewsCardDto?> GetNewsCardAsync(Guid newsId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<NewsCardDto?>("GetNewsCardAsync", newsId, ct).ConfigureAwait(false);

    public async Task MarkNewsSeenAsync(Guid[] newsIds, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("MarkNewsSeenAsync", newsIds, ct).ConfigureAwait(false);

    public async Task<OnboardingStateDto> GetOnboardingStateAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<OnboardingStateDto>("GetOnboardingStateAsync", ct).ConfigureAwait(false);

    public async Task DeletePhotoAsync(int order, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("DeletePhotoAsync", order, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task DeleteProfileAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("DeleteProfileAsync", ct).ConfigureAwait(false);

    public async Task<VenueCardDto> GetVenueCardAsync(Guid venueId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<VenueCardDto>("GetVenueCardAsync", venueId, ct).ConfigureAwait(false);

    public async Task<HangoutSummaryDto> CreateHangoutAsync(CreateHangoutRequest req, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<HangoutSummaryDto>("CreateHangoutAsync", req, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task EndMyHangoutAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("EndMyHangoutAsync", ct).ConfigureAwait(false);

    public async Task<HangoutRsvpResultDto> SetHangoutRsvpAsync(Guid hangoutId, bool going, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<HangoutRsvpResultDto>("SetHangoutRsvpAsync", hangoutId, going, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<HangoutSyncDto> GetHangoutSyncAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<HangoutSyncDto>("GetHangoutSyncAsync", ct).ConfigureAwait(false);

    public async Task<HangoutCardDto> GetHangoutCardAsync(Guid hangoutId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<HangoutCardDto>("GetHangoutCardAsync", hangoutId, ct).ConfigureAwait(false);

    public async Task<HangoutDirectoryPageDto> GetHangoutDirectoryAsync(
        HangoutDirectoryFilterDto filter, int skip, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<HangoutDirectoryPageDto>("GetHangoutDirectoryAsync", filter, skip, ct).ConfigureAwait(false);

    public async Task<Guid> ReportHangoutAsync(ReportHangoutRequest req, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<Guid>("ReportHangoutAsync", req, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<MatchDeckDto> GetMatchDeckAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MatchDeckDto>("GetMatchDeckAsync", ct).ConfigureAwait(false);

    public async Task<SwipeResultDto> SwipeAsync(Guid targetProfileId, SwipeDirection direction, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<SwipeResultDto>("SwipeAsync", targetProfileId, direction, ct).ConfigureAwait(false);

    public async Task<ReswipeResultDto> UndoLastSwipeAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<ReswipeResultDto>("UndoLastSwipeAsync", ct).ConfigureAwait(false);

    public async Task MarkMatchListSeenAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("MarkMatchListSeenAsync", ct).ConfigureAwait(false);

    public async Task UploadKeyBundleAsync(KeyBundleDto dto, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("UploadKeyBundleAsync", dto, ct).ConfigureAwait(false);

    public async Task ReplaceKeyBundleAsync(KeyBundleDto dto, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("ReplaceKeyBundleAsync", dto, ct).ConfigureAwait(false);

    public async Task<KeyBundleDto[]> GetMyRetiredKeyBundlesAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<KeyBundleDto[]>("GetMyRetiredKeyBundlesAsync", ct).ConfigureAwait(false);

    public async Task RewrapKeyBundleAsync(Guid profileId, KeyBundleDto dto, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("RewrapKeyBundleAsync", profileId, dto, ct).ConfigureAwait(false);

    public async Task RewrapAccountKeyBundleAsync(KeyBundleDto dto, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("RewrapAccountKeyBundleAsync", dto, ct).ConfigureAwait(false);

    public async Task<KeyBundleDto?> GetMyKeyBundleAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<KeyBundleDto?>("GetMyKeyBundleAsync", ct).ConfigureAwait(false);

    public async Task<SiblingKeyBundleDto[]> GetSiblingKeyBundlesAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<SiblingKeyBundleDto[]>("GetSiblingKeyBundlesAsync", ct)
            .ConfigureAwait(false);

    public async Task<byte[]?> GetPeerPublicKeyAsync(Guid peerId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<byte[]?>("GetPeerPublicKeyAsync", peerId, ct).ConfigureAwait(false);

    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest req, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<SendMessageResponse>("SendMessageAsync", req, ct).ConfigureAwait(false);

    public async Task<ConversationHistoryDto> GetConversationAsync(Guid peerId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<ConversationHistoryDto>("GetConversationAsync", peerId, ct).ConfigureAwait(false);

    public async Task<Guid[]> MarkConversationReadAsync(Guid peerId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<Guid[]>("MarkConversationReadAsync", peerId, ct).ConfigureAwait(false);

    public async Task<MatchListDto> GetMyMatchesAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MatchListDto>("GetMyMatchesAsync", ct).ConfigureAwait(false);

    public async Task<ChatDeltaDto> GetChatDeltaAsync(ChatDeltaRequest req, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<ChatDeltaDto>("GetChatDeltaAsync", req, ct).ConfigureAwait(false);

    public async Task UnmatchAsync(Guid peerId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("UnmatchAsync", peerId, ct).ConfigureAwait(false);

    public async Task BlockUserAsync(Guid peerId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("BlockUserAsync", peerId, ct).ConfigureAwait(false);

    public async Task<BlockedUserDto[]> GetBlockedUsersAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<BlockedUserDto[]>("GetBlockedUsersAsync", ct).ConfigureAwait(false);

    public async Task UnblockUserAsync(Guid peerId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("UnblockUserAsync", peerId, ct).ConfigureAwait(false);

    public async Task SetMatchPinnedAsync(Guid peerId, bool pinned, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetMatchPinnedAsync", peerId, pinned, ct).ConfigureAwait(false);

    public async Task<MessageReactionsChangedPushDto> ReactToMessageAsync(
        ReactToMessageRequest req, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MessageReactionsChangedPushDto>("ReactToMessageAsync", req, ct)
            .ConfigureAwait(false);

    public async Task<MessagePinChangedPushDto> SetMessagePinnedAsync(
        SetMessagePinnedRequest req, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MessagePinChangedPushDto>("SetMessagePinnedAsync", req, ct)
            .ConfigureAwait(false);

    public async Task DeleteMessageAsync(Guid peerProfileId, Guid messageId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("DeleteMessageAsync", peerProfileId, messageId, ct)
            .ConfigureAwait(false);

    public async Task<ProfileDetailDto> GetProfileDetailAsync(Guid peerId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<ProfileDetailDto>("GetProfileDetailAsync", peerId, ct).ConfigureAwait(false);

    public async Task<ProfileDetailDto> GetMyProfileDetailAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<ProfileDetailDto>("GetMyProfileDetailAsync", ct).ConfigureAwait(false);

    public async Task<MyStatsDto> GetMyStatsAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MyStatsDto>("GetMyStatsAsync", ct).ConfigureAwait(false);

    public async Task<SupporterStatsDto> GetSupporterStatsAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<SupporterStatsDto>("GetSupporterStatsAsync", ct).ConfigureAwait(false);

    public async Task<FlairDto[]> GetFlairCatalogAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<FlairDto[]>("GetFlairCatalogAsync", ct).ConfigureAwait(false);

    public async Task<Guid> ReportUserAsync(ReportUserRequest req, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<Guid>("ReportUserAsync", req, ct).ConfigureAwait(false);

    /// <summary>Permanently stops the peer from appearing in the caller's deck. One-way and not reversible.</summary>
    public async Task HideProfileAsync(Guid peerId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("HideProfileAsync", peerId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<PulseDto?> GetPulseAsync(Language language, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<PulseDto?>("GetPulseAsync", language, ct).ConfigureAwait(false);

    public async Task<PatreonLinkStartDto> StartPatreonLinkAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<PatreonLinkStartDto>("StartPatreonLinkAsync", ct).ConfigureAwait(false);

    public async Task<PatreonStatusDto> GetPatreonStatusAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<PatreonStatusDto>("GetPatreonStatusAsync", ct).ConfigureAwait(false);

    public async Task<PatreonStatusDto> UnlinkPatreonAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<PatreonStatusDto>("UnlinkPatreonAsync", ct).ConfigureAwait(false);

    public async Task<Guid> SubmitFeedbackAsync(SubmitFeedbackRequest req, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<Guid>("SubmitFeedbackAsync", req, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<PlacesBrowseDto> GetPlacesBrowseAsync(PlacesFilterDto filter, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<PlacesBrowseDto>("GetPlacesBrowseAsync", filter, ct).ConfigureAwait(false);

    public async Task<VenueDetailDto> GetVenueDetailAsync(Guid venueId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<VenueDetailDto>("GetVenueDetailAsync", venueId, ct).ConfigureAwait(false);

    public async Task<VenueReviewDto[]> GetVenueReviewsAsync(Guid venueId, int skip, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<VenueReviewDto[]>("GetVenueReviewsAsync", venueId, skip, ct).ConfigureAwait(false);

    public async Task<int> SetVenueLikeAsync(Guid venueId, bool liked, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<int>("SetVenueLikeAsync", venueId, liked, ct).ConfigureAwait(false);

    public async Task<int> SetVenueRsvpAsync(Guid venueId, DateTimeOffset occurrenceStartUtc, bool going, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<int>("SetVenueRsvpAsync", venueId, occurrenceStartUtc, going, ct).ConfigureAwait(false);

    public async Task<MyVenueRsvpDto[]> GetMyVenueRsvpsAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MyVenueRsvpDto[]>("GetMyVenueRsvpsAsync", ct).ConfigureAwait(false);

    public async Task<VenueReviewDto> SubmitVenueReviewAsync(Guid venueId, short rating, string text, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<VenueReviewDto>("SubmitVenueReviewAsync", venueId, rating, text, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task DeleteMyVenueReviewAsync(Guid venueId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("DeleteMyVenueReviewAsync", venueId, ct).ConfigureAwait(false);

    public async Task<MyVenueDto[]> GetMyVenuesAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MyVenueDto[]>("GetMyVenuesAsync", ct).ConfigureAwait(false);

    public async Task<MyVenueDto> SaveVenueAsync(VenueEditDto dto, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<MyVenueDto>("SaveVenueAsync", dto, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task DeleteVenueAsync(Guid venueId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("DeleteVenueAsync", venueId, ct).ConfigureAwait(false);

    public async Task<MyVenueDto> SetVenueImageAsync(Guid venueId, bool banner, PhotoUploadDto upload, short slot = 1, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<MyVenueDto>("SetVenueImageAsync", venueId, banner, slot, upload, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<MyVenueDto> RemoveVenueImageAsync(Guid venueId, bool banner, short slot = 1, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MyVenueDto>("RemoveVenueImageAsync", venueId, banner, slot, ct).ConfigureAwait(false);
}
