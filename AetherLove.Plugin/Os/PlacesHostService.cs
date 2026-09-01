using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Auth;
using AetherLove.Services.Hub;
using AetherLove.Shared.Places;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Store;
using AetherOS.Apps.Places;

namespace AetherLove.Os;

/// <summary>AetherPlaces' bridge: hub passthroughs and session flags. Sharing goes through the OS share sheet;
/// image picking and cropping for banner/logo uploads come from the shared app capabilities.</summary>
public sealed class PlacesHostService : IPlacesHost
{
    private readonly AetherHubContext _hubClient;
    private readonly SessionBootstrapper _bootstrap;
    private readonly VenueShareContext _shareCtx;

    public PlacesHostService(AetherHubContext hubClient, SessionBootstrapper bootstrap,
        VenueShareContext shareCtx)
    {
        _hubClient = hubClient;
        _bootstrap = bootstrap;
        _shareCtx = shareCtx;
    }

    public Task<PlacesBrowseDto> GetPlacesBrowseAsync(PlacesFilterDto filter, CancellationToken ct = default) =>
        _hubClient.GetPlacesBrowseAsync(filter, ct);

    public Task<VenueDetailDto> GetVenueDetailAsync(Guid venueId, CancellationToken ct = default) =>
        _hubClient.GetVenueDetailAsync(venueId, ct);

    public Task<VenueReviewDto[]> GetVenueReviewsAsync(Guid venueId, int skip, CancellationToken ct = default) =>
        _hubClient.GetVenueReviewsAsync(venueId, skip, ct);

    public Task<VenueReviewDto> SubmitVenueReviewAsync(Guid venueId, short rating, string text, CancellationToken ct = default) =>
        _hubClient.SubmitVenueReviewAsync(venueId, rating, text, ct);

    public Task DeleteMyVenueReviewAsync(Guid venueId, CancellationToken ct = default) =>
        _hubClient.DeleteMyVenueReviewAsync(venueId, ct);

    public Task<int> SetVenueLikeAsync(Guid venueId, bool liked, CancellationToken ct = default) =>
        _hubClient.SetVenueLikeAsync(venueId, liked, ct);

    public Task<int> SetVenueRsvpAsync(Guid venueId, DateTimeOffset occurrenceStartUtc, bool going, CancellationToken ct = default) =>
        _hubClient.SetVenueRsvpAsync(venueId, occurrenceStartUtc, going, ct);

    public Task<MyVenueDto[]> GetMyVenuesAsync(CancellationToken ct = default) =>
        _hubClient.GetMyVenuesAsync(ct);

    public Task<MyVenueDto> SaveVenueAsync(VenueEditDto dto, CancellationToken ct = default) =>
        _hubClient.SaveVenueAsync(dto, ct);

    public Task DeleteVenueAsync(Guid venueId, CancellationToken ct = default) =>
        _hubClient.DeleteVenueAsync(venueId, ct);

    public Task<MyVenueDto> SetVenueImageAsync(Guid venueId, bool banner, PhotoUploadDto upload, short slot = 1, CancellationToken ct = default) =>
        _hubClient.SetVenueImageAsync(venueId, banner, upload, slot, ct);

    public Task<MyVenueDto> RemoveVenueImageAsync(Guid venueId, bool banner, short slot = 1, CancellationToken ct = default) =>
        _hubClient.RemoveVenueImageAsync(venueId, banner, slot, ct);

    public async Task<MyBoostsDto?> GetMyBoostsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _hubClient.GetMyBoostsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public Task<BoostResultDto> ApplyBoostAsync(
        BoostTarget target, Guid targetId, BoostStyle style, CancellationToken ct = default) =>
        _hubClient.ApplyBoostAsync(target, targetId, style, ct);

    public bool NsfwEnabled => _bootstrap.LastConnection?.NsfwEnabled ?? false;

    public bool IsSupporter => _bootstrap.LastConnection is { IsSupporter: true };

    public bool IsVenueOwner => _bootstrap.LastConnection is { IsVenueOwner: true };

    public (Guid VenueId, string? ReturnApp)? TakePendingOpenVenue()
    {
        var pending = _shareCtx.PendingOpenVenueId;
        var returnApp = _shareCtx.PendingOpenReturnApp;
        _shareCtx.PendingOpenVenueId = null;
        _shareCtx.PendingOpenReturnApp = null;
        return pending is { } venueId ? (venueId, returnApp) : null;
    }
}
