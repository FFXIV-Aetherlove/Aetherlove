using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Levemetes;
using AetherLove.Shared.Profile;

namespace AetherOS.Apps.Levemetes;

/// <summary>Plugin-side services the Levemetes app needs; implemented in the plugin (dependency inversion, so
/// the app never references it).</summary>
public interface ILevemetesHost
{
    Task<LevemetesBrowseDto> GetBrowseAsync(LevemetesFilterDto filter, CancellationToken ct = default);

    Task<LevemeteDetailDto> GetDetailAsync(Guid adId, CancellationToken ct = default);

    Task<LevemeteReviewDto[]> GetReviewsAsync(Guid adId, int skip, CancellationToken ct = default);

    Task SubmitReviewAsync(Guid adId, short rating, string text, CancellationToken ct = default);

    Task DeleteMyReviewAsync(Guid adId, CancellationToken ct = default);

    Task<MyLevemeteDto[]> GetMineAsync(CancellationToken ct = default);

    Task<MyLevemeteDto> SaveAdAsync(LevemeteEditDto dto, CancellationToken ct = default);

    Task DeleteAdAsync(Guid adId, CancellationToken ct = default);

    Task<MyLevemeteDto> RenewAdAsync(Guid adId, CancellationToken ct = default);

    Task<MyLevemeteDto> SetImageAsync(Guid adId, short slot, PhotoUploadDto upload, CancellationToken ct = default);

    Task<MyLevemeteDto> RemoveImageAsync(Guid adId, short slot, CancellationToken ct = default);

    /// <summary>The server-mediated messenger add behind the contact button; also refreshes the messenger
    /// sync so the outgoing request materializes immediately.</summary>
    Task AddContactAsync(Guid adId, CancellationToken ct = default);

    Task ReportAdAsync(Guid adId, string reason, CancellationToken ct = default);

    /// <summary>Read-and-clear of a pending deep link (chat card click or share intent).</summary>
    (Guid AdId, string? ReturnApp)? TakePendingOpen();

    /// <summary>Back leg for an ad opened from an AetherLove chat card.</summary>
    void OpenLoveChat();

    /// <summary>Whether the caller's own messenger accepts new contact requests, so an owner can be warned
    /// that the contact button on their ads is dead.</summary>
    bool MessengerAddsEnabled { get; }
}
