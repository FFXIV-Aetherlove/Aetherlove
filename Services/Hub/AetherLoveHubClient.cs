using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Signal;
using AetherLove.Shared.Feedback;
using AetherLove.Shared.Matching;
using AetherLove.Shared.Messaging;
using AetherLove.Shared.Moderation;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Profile.Enums;
using AetherLove.Shared.Pulse;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

/// <summary>Typed client wrapper for <c>AetherLoveHub</c> methods.</summary>
public sealed class AetherLoveHubClient
{
    private readonly AetherSignalService _signal;

    public AetherLoveHubClient(AetherSignalService signal)
    {
        _signal = signal;
    }

    /// <summary>True when the hub connection is live — lets callers tell a connectivity failure apart from a server-side error.</summary>
    public bool IsConnected => _signal.IsConnected;

    /// <summary>Ensures the hub is connected and returns it. Throws via RequireConnection if there's no valid token.</summary>
    private async Task<HubConnection> ConnAsync(CancellationToken ct)
    {
        await _signal.EnsureConnectedAsync(ct).ConfigureAwait(false);
        return _signal.RequireConnection();
    }

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

    public async Task SetProfileNsfwAsync(bool enabled, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SetProfileNsfwAsync", enabled, ct).ConfigureAwait(false);
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
    }

    public async Task MarkWarningsSeenAsync(Guid[] warningIds, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("MarkWarningsSeenAsync", warningIds, ct).ConfigureAwait(false);

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

    public async Task DeleteAccountAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("DeleteAccountAsync", ct).ConfigureAwait(false);

    public async Task<MatchDeckDto> GetMatchDeckAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MatchDeckDto>("GetMatchDeckAsync", ct).ConfigureAwait(false);

    public async Task<SwipeResultDto> SwipeAsync(Guid targetProfileId, SwipeDirection direction, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<SwipeResultDto>("SwipeAsync", targetProfileId, direction, ct).ConfigureAwait(false);

    public async Task MarkMatchListSeenAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("MarkMatchListSeenAsync", ct).ConfigureAwait(false);

    public async Task UploadKeyBundleAsync(KeyBundleDto dto, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("UploadKeyBundleAsync", dto, ct).ConfigureAwait(false);

    public async Task<KeyBundleDto?> GetMyKeyBundleAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<KeyBundleDto?>("GetMyKeyBundleAsync", ct).ConfigureAwait(false);

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

    public async Task UnmatchAsync(Guid peerId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("UnmatchAsync", peerId, ct).ConfigureAwait(false);

    public async Task BlockUserAsync(Guid peerId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("BlockUserAsync", peerId, ct).ConfigureAwait(false);

    public async Task SetMatchPinnedAsync(Guid peerId, bool pinned, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetMatchPinnedAsync", peerId, pinned, ct).ConfigureAwait(false);

    public async Task<ProfileDetailDto> GetProfileDetailAsync(Guid peerId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<ProfileDetailDto>("GetProfileDetailAsync", peerId, ct).ConfigureAwait(false);

    public async Task<ProfileDetailDto> GetMyProfileDetailAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<ProfileDetailDto>("GetMyProfileDetailAsync", ct).ConfigureAwait(false);

    public async Task<Guid> ReportUserAsync(ReportUserRequest req, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<Guid>("ReportUserAsync", req, ct).ConfigureAwait(false);

    public async Task<PulseDto?> GetPulseAsync(Language language, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<PulseDto?>("GetPulseAsync", language, ct).ConfigureAwait(false);

    public async Task<Guid> SubmitFeedbackAsync(SubmitFeedbackRequest req, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<Guid>("SubmitFeedbackAsync", req, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }
}
