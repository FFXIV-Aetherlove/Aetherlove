using AetherLove.Shared.Store;

namespace AetherOS.Apps.Store;

/// <summary>What a product costs this caller right now, as the server has already decided it. The one
/// wrinkle the DTO leaves to the reader is the free first skin: the server marks every skin eligible while
/// the pick is open and keeps DiscountedPriceSparks at what a second skin would cost, so a single product
/// shows as free and the cart is the place that knows only one of them can be.</summary>
internal static class StorePrice
{
    public static int Shown(StoreProductDto product) => product.FreeSkinEligible ? 0 : product.DiscountedPriceSparks;
}
