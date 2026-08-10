using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Store;
using Microsoft.AspNetCore.SignalR.Client;

namespace AetherLove.Services.Hub;

/// <summary>Store passthroughs.</summary>
public sealed partial class AetherHubContext
{
    public async Task<StoreFrontDto> GetStoreFrontAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<StoreFrontDto>("GetStoreFrontAsync", ct).ConfigureAwait(false);

    public async Task<StoreProductPageDto> GetStoreProductsAsync(StoreProductQueryDto query, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<StoreProductPageDto>("GetStoreProductsAsync", query, ct).ConfigureAwait(false);

    public async Task<StoreProductDto?> GetStoreProductAsync(Guid productId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<StoreProductDto?>("GetStoreProductAsync", productId, ct).ConfigureAwait(false);

    public async Task<byte[]?> GetStoreProductImageAsync(Guid productId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<byte[]?>("GetStoreProductImageAsync", productId, ct).ConfigureAwait(false);

    public async Task<byte[]?> GetStoreSkinPreviewAsync(Guid productId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<byte[]?>("GetStoreSkinPreviewAsync", productId, ct).ConfigureAwait(false);

    public async Task<byte[]?> GetStoreCollectionImageAsync(Guid collectionId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<byte[]?>("GetStoreCollectionImageAsync", collectionId, ct).ConfigureAwait(false);

    public async Task<byte[]?> GetStoreCategoryImageAsync(Guid categoryId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<byte[]?>("GetStoreCategoryImageAsync", categoryId, ct).ConfigureAwait(false);

    public async Task<StoreProductDto[]> GetStoreRelatedAsync(Guid productId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<StoreProductDto[]>("GetStoreRelatedAsync", productId, ct).ConfigureAwait(false);

    public async Task<AvatarRingDto[]> GetMyAvatarRingsAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<AvatarRingDto[]>("GetMyAvatarRingsAsync", ct).ConfigureAwait(false);

    public async Task SetAvatarRingAsync(AvatarRingSurface surface, string? frameRef, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetAvatarRingAsync", (short)surface, frameRef, ct).ConfigureAwait(false);

    public async Task<byte[]?> GetAvatarFrameImageAsync(string frameRef, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<byte[]?>("GetAvatarFrameImageAsync", frameRef, ct).ConfigureAwait(false);

    public async Task SetAvatarRingEverywhereAsync(string? frameRef, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync("SetAvatarRingEverywhereAsync", frameRef, ct).ConfigureAwait(false);

    public async Task<OwnedThemeDto[]> GetMyStoreThemesAsync(CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<OwnedThemeDto[]>("GetMyStoreThemesAsync", ct).ConfigureAwait(false);

    public async Task<StoreThemeAssetsDto?> GetStoreThemeAssetsAsync(Guid productId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<StoreThemeAssetsDto?>("GetStoreThemeAssetsAsync", productId, ct)
            .ConfigureAwait(false);

    public async Task<byte[]?> GetStoreThemeBackgroundPreviewAsync(Guid productId, CancellationToken ct = default) =>
        await (await ConnAsync(ct)).InvokeAsync<byte[]?>("GetStoreThemeBackgroundPreviewAsync", productId, ct)
            .ConfigureAwait(false);

    public async Task<StorePurchaseResultDto> PurchaseStoreProductAsync(Guid productId, int quantity, CancellationToken ct = default)
    {
        try
        {
            return await (await ConnAsync(ct))
                .InvokeAsync<StorePurchaseResultDto>("PurchaseStoreProductAsync", productId, quantity, ct)
                .ConfigureAwait(false);
        }
        catch (Microsoft.AspNetCore.SignalR.HubException ex) when (RateLimitException.TryParse(ex) is { } rl)
        {
            throw rl;
        }
    }
}
