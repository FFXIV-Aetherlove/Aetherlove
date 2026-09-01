using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Places;
using AetherLove.Shared.Profile;
using AetherLove.Shared.Store;

namespace AetherOS.Apps.Places;

/// <summary>Host bridge into the plugin: hub passthroughs and session flags. Sharing goes through the OS share
/// sheet; image picking and cropping come from the shared app capabilities.</summary>
public interface IPlacesHost
{
    Task<PlacesBrowseDto> GetPlacesBrowseAsync(PlacesFilterDto filter, CancellationToken ct = default);
    Task<VenueDetailDto> GetVenueDetailAsync(Guid venueId, CancellationToken ct = default);
    Task<VenueReviewDto[]> GetVenueReviewsAsync(Guid venueId, int skip, CancellationToken ct = default);
    Task<VenueReviewDto> SubmitVenueReviewAsync(Guid venueId, short rating, string text, CancellationToken ct = default);
    Task DeleteMyVenueReviewAsync(Guid venueId, CancellationToken ct = default);
    Task<int> SetVenueLikeAsync(Guid venueId, bool liked, CancellationToken ct = default);
    Task<int> SetVenueRsvpAsync(Guid venueId, DateTimeOffset occurrenceStartUtc, bool going, CancellationToken ct = default);
    Task<MyVenueDto[]> GetMyVenuesAsync(CancellationToken ct = default);
    Task<MyVenueDto> SaveVenueAsync(VenueEditDto dto, CancellationToken ct = default);
    Task DeleteVenueAsync(Guid venueId, CancellationToken ct = default);
    Task<MyVenueDto> SetVenueImageAsync(Guid venueId, bool banner, PhotoUploadDto upload, short slot = 1, CancellationToken ct = default);
    Task<MyVenueDto> RemoveVenueImageAsync(Guid venueId, bool banner, short slot = 1, CancellationToken ct = default);

    /// <summary>The boosts the account holds, for the editor's boost row. Null on any hub failure, which the
    /// row reads as "none" and offers the store link instead.</summary>
    Task<MyBoostsDto?> GetMyBoostsAsync(CancellationToken ct = default);

    Task<BoostResultDto> ApplyBoostAsync(
        BoostTarget target, Guid targetId, BoostStyle style, CancellationToken ct = default);

    /// <summary>The profile's NSFW consent; seeds the 18+ browse filter default.</summary>
    bool NsfwEnabled { get; }

    /// <summary>Gates the supporter carousel banner slots in the venue editor.</summary>
    bool IsSupporter { get; }

    /// <summary>Whether the account holds the venue-owner right; picks the manage view over the pitch page.</summary>
    bool IsVenueOwner { get; }

    /// <summary>Read-and-clear of the chat venue-card deep link; polled at the start of every app frame.</summary>
    (Guid VenueId, string? ReturnApp)? TakePendingOpenVenue();
}
