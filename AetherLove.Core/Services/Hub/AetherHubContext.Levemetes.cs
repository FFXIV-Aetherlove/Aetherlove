using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Levemetes;
using AetherLove.Shared.Profile;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

/// <summary>Levemetes classifieds passthroughs.</summary>
public sealed partial class AetherHubContext
{
    public async Task<LevemetesBrowseDto> GetLevemetesBrowseAsync(LevemetesFilterDto filter, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<LevemetesBrowseDto>("GetLevemetesBrowseAsync", filter, ct).ConfigureAwait(false);

    public async Task<LevemeteDetailDto> GetLevemeteDetailAsync(Guid adId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<LevemeteDetailDto>("GetLevemeteDetailAsync", adId, ct).ConfigureAwait(false);

    public async Task<LevemeteCardDto?> GetLevemeteCardAsync(Guid adId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<LevemeteCardDto?>("GetLevemeteCardAsync", adId, ct).ConfigureAwait(false);

    public async Task<LevemeteReviewDto[]> GetLevemeteReviewsAsync(Guid adId, int skip, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<LevemeteReviewDto[]>("GetLevemeteReviewsAsync", adId, skip, ct).ConfigureAwait(false);

    public async Task SubmitLevemeteReviewAsync(Guid adId, short rating, string text, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("SubmitLevemeteReviewAsync", adId, rating, text, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task DeleteMyLevemeteReviewAsync(Guid adId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("DeleteMyLevemeteReviewAsync", adId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<MyLevemeteDto[]> GetMyLevemetesAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<MyLevemeteDto[]>("GetMyLevemetesAsync", ct).ConfigureAwait(false);

    public async Task<MyLevemeteDto> SaveLevemeteAdAsync(LevemeteEditDto dto, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<MyLevemeteDto>("SaveLevemeteAdAsync", dto, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task DeleteLevemeteAdAsync(Guid adId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("DeleteLevemeteAdAsync", adId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<MyLevemeteDto> RenewLevemeteAdAsync(Guid adId, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<MyLevemeteDto>("RenewLevemeteAdAsync", adId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<MyLevemeteDto> SetLevemeteImageAsync(Guid adId, short slot, PhotoUploadDto upload, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<MyLevemeteDto>("SetLevemeteImageAsync", adId, slot, upload, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task<MyLevemeteDto> RemoveLevemeteImageAsync(Guid adId, short slot, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct)).InvokeAsync<MyLevemeteDto>("RemoveLevemeteImageAsync", adId, slot, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task AddLevemeteContactAsync(Guid adId, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("AddLevemeteContactAsync", adId, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }

    public async Task ReportLevemeteAdAsync(Guid adId, string reason, CancellationToken ct = default)
    {
        try
        {
            await (await ConnAsync(ct)).InvokeAsync("ReportLevemeteAdAsync", adId, reason, ct).ConfigureAwait(false);
        }
        catch (HubException ex) when (RateLimitException.TryParse(ex) is { } rl) { throw rl; }
    }
}
