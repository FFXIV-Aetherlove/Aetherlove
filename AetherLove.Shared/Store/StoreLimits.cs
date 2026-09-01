namespace AetherLove.Shared.Store;

/// <summary>Store limits both sides must agree on. The server still owns the enforced value through
/// <c>StoreOptions</c>; this is its default and the client's stepper ceiling for uncapped products.</summary>
public static class StoreLimits
{
    /// <summary>How many of one product a single checkout may carry when the product has no per-account cap.</summary>
    public const int MaxQuantityPerCheckout = 99;
}
