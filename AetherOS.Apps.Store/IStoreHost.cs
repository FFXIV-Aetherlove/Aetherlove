using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherLove.Shared.Store;

namespace AetherOS.Apps.Store;

/// <summary>What one checkout attempt came back with. Unlike the null-on-failure reads, purchase
/// failures are typed: the host parses the hub error payload so the app can render the exact refusal.
/// ErrorCode "offline" is the transport-failure sentinel.</summary>
public sealed record StoreCheckoutResult(bool Success, StorePurchaseResultDto? Result, string? ErrorCode, string[] ErrorArgs);

/// <summary>The Store app's host bridge. Reads collapse to null on any hub failure (the offline story);
/// the app renders its own retry states.</summary>
public interface IStoreHost
{
    Task<StoreFrontDto?> GetStoreFrontAsync(CancellationToken ct = default);

    Task<StoreProductPageDto?> GetStoreProductsAsync(StoreProductQueryDto query, CancellationToken ct = default);

    Task<StoreProductDto?> GetStoreProductAsync(Guid productId, CancellationToken ct = default);

    /// <summary>One product by its identity (kind + ref) rather than its id, for a deep link that names
    /// what it wants. Null when the caller cannot see it or it is not on sale.</summary>
    Task<StoreProductDto?> GetStoreProductByRefAsync(
        StoreItemKind kind, string itemRef, CancellationToken ct = default);

    Task<byte[]?> GetStoreProductImageAsync(Guid productId, CancellationToken ct = default);

    Task<StoreCheckoutResult> PurchaseAsync(Guid productId, int quantity, CancellationToken ct = default);

    Task<byte[]?> GetStoreCollectionImageAsync(Guid collectionId, CancellationToken ct = default);

    Task<byte[]?> GetStoreCategoryImageAsync(Guid categoryId, CancellationToken ct = default);

    Task<StoreProductDto[]?> GetStoreRelatedAsync(Guid productId, CancellationToken ct = default);

    Task<long?> GetSparkBalanceAsync(CancellationToken ct = default);

    /// <summary>Opens a second phone-shaped window beside the real one showing a skin the user is
    /// considering. The image is fetched from the server, which bakes the watermark in: the app never
    /// holds a clean copy of a frame nobody has bought. A theme's wallpaper arrives already composed
    /// into the frame by the server, so the preview is the whole look rather than an empty bezel.</summary>
    void ShowSkinPreview(string title, Guid productId);

    /// <summary>The user's OS avatar, for the ring try-on preview; null before the first fetch.</summary>
    Dalamud.Interface.Textures.ISharedImmediateTexture? OsAvatarTexture { get; }

    /// <summary>The active AetherLove profile's avatar, null when there is no profile or no photo.</summary>
    Dalamud.Interface.Textures.ISharedImmediateTexture? LoveAvatarTexture { get; }

    /// <summary>The user's Yapper avatar bytes, null when there is no yapper profile or no avatar.</summary>
    Task<byte[]?> GetYapperAvatarAsync(CancellationToken ct = default);

    /// <summary>A theme's wallpaper as the server watermarks it for people who do not own it yet.</summary>
    Task<byte[]?> GetStoreThemeBackgroundPreviewAsync(Guid productId, CancellationToken ct = default);

    /// <summary>Switches the phone to a purchased theme, fetching and sealing its assets first.</summary>
    Task<bool> EnableThemeAsync(Guid productId, CancellationToken ct = default);

    /// <summary>Wears a purchased ring on every identity the account has: OS, each dating profile and
    /// Yapper. Surfaces the account does not have are skipped rather than refused.</summary>
    Task<bool> EnableRingEverywhereAsync(string frameRef, CancellationToken ct = default);
}
