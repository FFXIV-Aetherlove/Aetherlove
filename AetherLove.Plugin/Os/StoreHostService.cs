using System;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Services;
using AetherLove.Services.Hub;
using AetherLove.Shared;
using AetherLove.Shared.Store;
using AetherOS.Apps.Store;

namespace AetherLove.Os;

/// <summary>The Store app's host: hub passthroughs that collapse to null offline, and a checkout that
/// parses the hub's typed error payload instead of throwing, so the app can render the exact refusal.</summary>
public sealed class StoreHostService(
    AetherHubContext hubClient,
    Windows.SkinPreviewWindow skinPreview,
    Services.OsAvatarCache osAvatar,
    Services.OwnAvatarCache ownAvatar,
    Services.Store.PremiumThemeService premiumThemes,
    Services.Auth.SessionBootstrapper bootstrap) : IStoreHost
{
    public Dalamud.Interface.Textures.ISharedImmediateTexture? OsAvatarTexture => osAvatar.Texture;

    public Dalamud.Interface.Textures.ISharedImmediateTexture? LoveAvatarTexture => ownAvatar.Texture;

    public async Task<byte[]?> GetYapperAvatarAsync(CancellationToken ct = default)
    {
        try
        {
            var me = await hubClient.GetMyYapperProfileAsync(ct).ConfigureAwait(false);
            return me?.Avatar;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Store] Yapper avatar fetch failed.");
            return null;
        }
    }

    public void ShowSkinPreview(string title, Guid productId)
    {
        skinPreview.BeginLoading(title);
        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await hubClient.GetStoreSkinPreviewAsync(productId).ConfigureAwait(false);
                skinPreview.Deliver(title, bytes);
            }
            catch (Exception)
            {
                skinPreview.Deliver(title, null);
            }
        });
    }

    public async Task<StoreFrontDto?> GetStoreFrontAsync(CancellationToken ct = default)
    {
        try
        {
            return await hubClient.GetStoreFrontAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Store] Storefront fetch failed.");
            return null;
        }
    }

    public async Task<StoreProductPageDto?> GetStoreProductsAsync(StoreProductQueryDto query, CancellationToken ct = default)
    {
        try
        {
            return await hubClient.GetStoreProductsAsync(query, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Store] Browse fetch failed.");
            return null;
        }
    }

    public async Task<StoreProductDto?> GetStoreProductAsync(Guid productId, CancellationToken ct = default)
    {
        try
        {
            return await hubClient.GetStoreProductAsync(productId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Store] Product fetch failed.");
            return null;
        }
    }

    public async Task<byte[]?> GetStoreProductImageAsync(Guid productId, CancellationToken ct = default)
    {
        try
        {
            return await hubClient.GetStoreProductImageAsync(productId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Store] Product image fetch failed.");
            return null;
        }
    }

    public async Task<byte[]?> GetStoreCollectionImageAsync(Guid collectionId, CancellationToken ct = default)
    {
        try
        {
            return await hubClient.GetStoreCollectionImageAsync(collectionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Store] Collection image fetch failed.");
            return null;
        }
    }

    public async Task<byte[]?> GetStoreCategoryImageAsync(Guid categoryId, CancellationToken ct = default)
    {
        try
        {
            return await hubClient.GetStoreCategoryImageAsync(categoryId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Store] Category image fetch failed.");
            return null;
        }
    }

    public async Task<StoreProductDto[]?> GetStoreRelatedAsync(Guid productId, CancellationToken ct = default)
    {
        try
        {
            return await hubClient.GetStoreRelatedAsync(productId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Store] Related products fetch failed.");
            return null;
        }
    }

    public async Task<StoreCheckoutResult> PurchaseAsync(Guid productId, int quantity, CancellationToken ct = default)
    {
        try
        {
            var result = await hubClient.PurchaseStoreProductAsync(productId, quantity, ct).ConfigureAwait(false);
            return new StoreCheckoutResult(true, result, null, []);
        }
        catch (RateLimitException)
        {
            return new StoreCheckoutResult(false, null, "rate_limited", []);
        }
        catch (Exception ex)
        {
            var idx = ex.Message.IndexOf(HubErrors.Sentinel, StringComparison.Ordinal);
            if (idx < 0)
            {
                Plugin.Log.Debug(ex, "[Store] Checkout failed on transport.");
                return new StoreCheckoutResult(false, null, "offline", []);
            }
            var parts = ex.Message[(idx + HubErrors.Sentinel.Length)..].Split('|');
            return new StoreCheckoutResult(false, null, parts[0], parts[1..]);
        }
    }

    public async Task<byte[]?> GetStoreThemeBackgroundPreviewAsync(Guid productId, CancellationToken ct = default)
    {
        try
        {
            return await hubClient.GetStoreThemeBackgroundPreviewAsync(productId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Store] Theme background preview fetch failed.");
            return null;
        }
    }

    public async Task<bool> EnableThemeAsync(Guid productId, CancellationToken ct = default)
    {
        try
        {
            return await premiumThemes.EnableAsync(productId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Store] Enabling theme {Id} failed.", productId);
            return false;
        }
    }

    public async Task<bool> EnableRingEverywhereAsync(string frameRef, CancellationToken ct = default)
    {
        try
        {
            await hubClient.SetAvatarRingEverywhereAsync(frameRef, ct).ConfigureAwait(false);
            await bootstrap.RefreshAccountInfoAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[Store] Enabling ring {Ref} everywhere failed.", frameRef);
            return false;
        }
    }

    public async Task<long?> GetSparkBalanceAsync(CancellationToken ct = default)
    {
        try
        {
            return (await hubClient.GetSparkWalletAsync(ct).ConfigureAwait(false)).Balance;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "[Store] Balance fetch failed.");
            return null;
        }
    }
}
