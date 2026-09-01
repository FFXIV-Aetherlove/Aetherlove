namespace AetherLove.Shared.Store;

/// <summary>The central catalogue of first-party item refs. A product's identity is
/// (<see cref="StoreItemKind"/>, ref); refs listed here are the compile-time half of that contract for
/// items whose behavior this codebase will implement. Aetherling products use that catalogue's own keys
/// instead (palette names like "gloam", accessory slugs like "angel-halo", arms as "arm-&lt;job&gt;",
/// crystals as "crystal-&lt;element&gt;", shells by their skin folder key), so its future sync can map
/// (Kind, Ref) onto its ownership lists without translation. Refs are lowercase kebab-case, stable
/// forever once a product has shipped under them.
/// <para>Avatar frames, theme packs and powerups deliberately have no refs here: the placeholder ones
/// this file used to name were sample data, and the real items are authored in the admin panel. Nothing
/// in the code needs to know their refs, because the ring and theme paths resolve whatever the account
/// owns rather than a named product.</para></summary>
public static class StoreItemRefs
{
    public const string BundleAetherlingCare = "bundle-aetherling-care";

    /// <summary>Boosts one of the account's venues to the top of the Places listings for five days.</summary>
    public const string PowerupVenueBoost = "boost-venue";

    /// <summary>Boosts one of the account's Levemetes ads to the top of the board for five days.</summary>
    public const string PowerupLevemeteBoost = "boost-levemete";
}
